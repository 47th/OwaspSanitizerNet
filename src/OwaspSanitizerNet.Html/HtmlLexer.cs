// Copyright (c) 2011, Mike Samuel
// All rights reserved.
//
// Redistribution and use in source and binary forms, with or without
// modification, are permitted provided that the following conditions
// are met:
//
// Redistributions of source code must retain the above copyright
// notice, this list of conditions and the following disclaimer.
// Redistributions in binary form must reproduce the above copyright
// notice, this list of conditions and the following disclaimer in the
// documentation and/or other materials provided with the distribution.
// Neither the name of the OWASP nor the names of its contributors may
// be used to endorse or promote products derived from this software
// without specific prior written permission.
// THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS
// "AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT
// LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS
// FOR A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE
// COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT,
// INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING,
// BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
// LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER
// CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT
// LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN
// ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE
// POSSIBILITY OF SUCH DAMAGE.

using System;
using System.Collections.Generic;
using System.Linq;

namespace OwaspSanitizerNet.Html
{
    /**
    * A flexible lexer for HTML.
    * This is hairy code, but it is outside the TCB for the HTML sanitizer.
    *
    * @author Mike Samuel (mikesamuel@gmail.com)
    */
    internal sealed class HtmlLexer : AbstractTokenStream
    {
        // From http://issues.apache.org/jira/browse/XALANC-519
        private static readonly HashSet<string> VALUELESS_ATTRIB_NAMES = new HashSet<string>(new [] {
            "checked", "compact", "declare", "defer", "disabled",
            "ismap", "multiple", "nohref", "noresize", "noshade",
            "nowrap", "readonly", "selected"
            });
        private readonly string _input;
        private readonly HtmlInputSplitter _splitter;
        private State _state = State.OUTSIDE_TAG;
        private readonly LinkedList<HtmlToken> _lookahead = new LinkedList<HtmlToken>();

        public HtmlLexer(string input)
        {
            _input = input;
            _splitter = new HtmlInputSplitter(input);
        }

        /**
        * Normalize case of names that are not name-spaced.  This lower-cases HTML
        * element and attribute names, but not ones for embedded SVG or MATHML.
        */
        public static string canonicalName(string elementOrAttribName)
        {
            return elementOrAttribName.IndexOf(':') >= 0
                ? elementOrAttribName : Strings.toLowerCase(elementOrAttribName);
        }

        /**
        * An FSM that lets us reclassify text tokens inside tags as attribute
        * names/values
        */
        private enum State
        {
            OUTSIDE_TAG,
            IN_TAG,
            SAW_NAME,
            SAW_EQ
        }

        /**
        * Makes sure that this.token contains a token if one is available.
        * This may require fetching and combining multiple tokens from the underlying
        * splitter.
        */
        protected override HtmlToken produce()
        {
            HtmlToken token = ReadToken();
            if (token == null) { return null; }

            switch (token.type)
            {
                // Keep track of whether we're inside a tag or not.
                case HtmlTokenType.TAGBEGIN:
                        _state = State.IN_TAG;
                    break;
                case HtmlTokenType.TAGEND:
                    if (_state == State.SAW_EQ && HtmlTokenType.TAGEND == token.type)
                    {
                        // Distinguish <input type=checkbox checked=> from
                        // <input type=checkbox checked>
                        PushbackToken(token);
                        _state = State.IN_TAG;
                        return HtmlToken.instance(
                            token.start, token.start, HtmlTokenType.ATTRVALUE);
                    }

                    _state = State.OUTSIDE_TAG;
                    break;

                // Drop ignorable tokens by zeroing out the one received and recursing
                case HtmlTokenType.IGNORABLE:
                    return produce();

                // collapse adjacent text nodes if we're outside a tag, or otherwise,
                // Recognize attribute names and values.
                default:
                    switch (_state)
                    {
                        case State.OUTSIDE_TAG:
                            if (HtmlTokenType.TEXT == token.type
                                || HtmlTokenType.UNESCAPED == token.type)
                            {
                                token = CollapseSubsequent(token);
                            }
                            break;
                        case State.IN_TAG:
                            if (HtmlTokenType.TEXT == token.type
                                && !token.tokenInContextMatches(_input, "="))
                            {
                                // Reclassify as attribute name
                                token = HtmlInputSplitter.reclassify(
                                    token, HtmlTokenType.ATTRNAME);
                                _state = State.SAW_NAME;
                            }
                            break;
                        case State.SAW_NAME:
                            if (HtmlTokenType.TEXT == token.type)
                            {
                                if (token.tokenInContextMatches(_input, "="))
                                {
                                    _state = State.SAW_EQ;
                                    // Skip the '=' token
                                    return produce();
                                }
                                else
                                {
                                    // Reclassify as attribute name
                                    token = HtmlInputSplitter.reclassify(
                                        token, HtmlTokenType.ATTRNAME);
                                }
                            }
                            else
                            {
                                _state = State.IN_TAG;
                            }
                            break;
                        case State.SAW_EQ:
                            if (HtmlTokenType.TEXT == token.type
                                || HtmlTokenType.QSTRING == token.type)
                            {
                                if (HtmlTokenType.TEXT == token.type)
                                {
                                    // Collapse adjacent text nodes to properly handle
                                    //   <a onclick=this.clicked=true>
                                    //   <a title=foo bar>
                                    token = CollapseAttributeName(token);
                                }
                                // Reclassify as value
                                token = HtmlInputSplitter.reclassify(
                                    token, HtmlTokenType.ATTRVALUE);
                                _state = State.IN_TAG;
                            }
                            break;
                    }
                    break;
            }

            return token;
        }

        /**
        * Collapses all the following tokens of the same type into this.token.
        */
        private HtmlToken CollapseSubsequent(HtmlToken token)
        {
            HtmlToken collapsed = token;
            for (HtmlToken next;
                (next= PeekToken(0)) != null && next.type == token.type;
                ReadToken())
            {
                collapsed = Join(collapsed, next);
            }
            return collapsed;
        }

        private HtmlToken CollapseAttributeName(HtmlToken token)
        {
            // We want to collapse tokens into the value that are not parts of an
            // attribute value.  We should include any space or text adjacent to the
            // value, but should stop at any of the following constructions:
            //   space end-of-file              e.g. name=foo_
            //   space valueless-attrib-name    e.g. name=foo checked
            //   space tag-end                  e.g. name=foo />
            //   space text space? '='          e.g. name=foo bar=
            int nToMerge = 0;
            for (HtmlToken t; (t = PeekToken(nToMerge)) != null;)
            {
                if (t.type == HtmlTokenType.IGNORABLE)
                {
                    HtmlToken tok = PeekToken(nToMerge + 1);
                    if (tok == null) { break; }
                    if (tok.type != HtmlTokenType.TEXT) { break; }
                    if (IsValuelessAttribute(_input.Substring(tok.start, tok.end)))
                    {
                        break;
                    }
                    HtmlToken eq = PeekToken(nToMerge + 2);
                    if (eq != null && eq.type == HtmlTokenType.IGNORABLE)
                    {
                        eq = PeekToken(nToMerge + 3);
                    }
                    if (eq == null || eq.tokenInContextMatches(_input, "="))
                    {
                        break;
                    }
                }
                else if (t.type != HtmlTokenType.TEXT)
                {
                    break;
                }
                ++nToMerge;
            }
            if (nToMerge == 0) { return token; }

            int end = token.end;
            do
            {
                end = ReadToken().end;
            } 
            while (--nToMerge > 0);

            return HtmlToken.instance(token.start, end, HtmlTokenType.TEXT);
        }

        private static HtmlToken Join(HtmlToken a, HtmlToken b)
        {
            return HtmlToken.instance(a.start, b.end, a.type);
        }

        private HtmlToken ReadToken()
        {
            if (_lookahead.Count != 0)
            {
                HtmlToken token = _lookahead.First.Value;
                _lookahead.RemoveFirst();
                return token;
            } 
            else if (_splitter.hasNext())
            {
                return _splitter.next();
            }
            else
            {
                return null;
            }
        }

        private HtmlToken PeekToken(int i)
        {
            while (_lookahead.Count <= i && _splitter.hasNext())
            {
                _lookahead.AddLast(_splitter.next());
            }
            return _lookahead.Count > i ? _lookahead.ElementAt(i) : null;
        }

        private void PushbackToken(HtmlToken token)
        {
            _lookahead.AddFirst(token);
        }

        /** Can the attribute appear in HTML without a value. */
        private static bool IsValuelessAttribute(string attribName)
        {
            bool valueless = VALUELESS_ATTRIB_NAMES.Contains(
                Strings.toLowerCase(attribName));
            return valueless;
        }

        /**
        * A token stream that breaks a character stream into <tt>
        * HtmlTokenType.{TEXT,TAGBEGIN,TAGEND,DIRECTIVE,COMMENT,CDATA,DIRECTIVE}</tt>
        * tokens.  The matching of attribute names and values is done in a later step.
        */
        internal sealed class HtmlInputSplitter : AbstractTokenStream
        {
            /** The source of HTML character data. */
            private readonly string _input;
            /** An offset into input. */
            private int _offset;
            /** True iff the current character is inside a tag. */
            private bool _inTag;
            /**
            * True if inside a script, xmp, listing, or similar tag whose content does
            * not follow the normal escaping rules.
            */
            private bool _inEscapeExemptBlock;

            /**
            * Null or the name of the close tag required to end the current escape exempt
            * block.
            * Preformatted tags include &lt;script&gt;, &lt;xmp&gt;, etc. that may
            * contain unescaped HTML input.
            */
            private string _escapeExemptTagName = null;

            private HtmlTextEscapingMode? _textEscapingMode;

            private HtmlToken _lastNonIgnorable = null;

            public HtmlInputSplitter(string input)
            {
                _input = input;
            }

            /**
            * Make sure that there is a token ready to yield in this.token.
            */
            protected override HtmlToken produce()
            {
                HtmlToken token = ParseToken();
                if (null == token) { return null; }

                // Handle escape-exempt blocks.
                // The parse() method is only dimly aware of escape-excempt blocks, so
                // here we detect the beginning and ends of escape exempt blocks, and
                // reclassify as UNESCAPED, any tokens that appear in the middle.
                if (_inEscapeExemptBlock)
                {
                    if (token.type != HtmlTokenType.SERVERCODE)
                    {
                        // classify RCDATA as text since it can contain entities
                        token = reclassify(
                            token, (this._textEscapingMode == HtmlTextEscapingMode.RCDATA
                                    ? HtmlTokenType.TEXT
                                    : HtmlTokenType.UNESCAPED));
                    }
                }
                else
                {
                    switch (token.type)
                    {
                        case HtmlTokenType.TAGBEGIN:
                        {
                            string canonTagName = CanonicalName(
                                token.start + 1, token.end);
                            if (HtmlTextEscapingModeHelper.isTagFollowedByLiteralContent(
                                    canonTagName))
                            {
                                _escapeExemptTagName = canonTagName;
                                _textEscapingMode = HtmlTextEscapingModeHelper.getModeForTag(
                                    canonTagName);
                            }
                            break;
                        }
                        case HtmlTokenType.TAGEND:
                            _inEscapeExemptBlock = null != _escapeExemptTagName;
                        break;
                        default:
                        break;
                    }
                }
                return token;
            }

            /**
            * States for a state machine for optimistically identifying tags and other
            * html/xml/phpish structures.
            */
            private enum State
            {
                TAGNAME,
                SLASH,
                BANG,
                BANG_DASH,
                COMMENT,
                COMMENT_DASH,
                COMMENT_DASH_DASH,
                DIRECTIVE,
                DONE,
                BOGUS_COMMENT,
                SERVER_CODE,
                SERVER_CODE_PCT,

                // From HTML 5 section 8.1.2.6

                // The text in CDATA and RCDATA elements must not contain any
                // occurrences of the string "</" followed by characters that
                // case-insensitively match the tag name of the element followed
                // by one of U+0009 CHARACTER TABULATION, U+000A LINE FEED (LF),
                // U+000B LINE TABULATION, U+000C FORM FEED (FF), U+0020 SPACE,
                // U+003E GREATER-THAN SIGN (>), or U+002F SOLIDUS (/), unless
                // that string is part of an escaping text span.

                // An escaping text span is a span of text (in CDATA and RCDATA
                // elements) and character entity references (in RCDATA elements)
                // that starts with an escaping text span start that is not itself
                // in an escaping text span, and ends at the next escaping text
                // span end.

                // An escaping text span start is a part of text that consists of
                // the four character sequence "<!--".

                // An escaping text span end is a part of text that consists of
                // the three character sequence "-->".

                // An escaping text span start may share its U+002D HYPHEN-MINUS characters
                // with its corresponding escaping text span end.
                UNESCAPED_LT_BANG,             // <!
                UNESCAPED_LT_BANG_DASH,        // <!-
                ESCAPING_TEXT_SPAN,            // Inside an escaping text span
                ESCAPING_TEXT_SPAN_DASH,       // Seen - inside an escaping text span
                ESCAPING_TEXT_SPAN_DASH_DASH  // Seen -- inside an escaping text span
            }

            /**
            * Breaks the character stream into tokens.
            * This method returns a stream of tokens such that each token starts where
            * the last token ended.
            *
            * <p>This property is useful as it allows fetch to collapse and reclassify
            * ranges of tokens based on state that is easy to maintain there.
            *
            * <p>Later passes are responsible for throwing away useless tokens.
            */
            private HtmlToken ParseToken()
            {
                int start = _offset;
                int limit = _input.Length;
                if (start == limit) { return null; }

                int end = start + 1;
                HtmlTokenType? type;

                char ch = _input[start];
                if (_inTag)
                {
                    if ('>' == ch)
                    {
                        type = HtmlTokenType.TAGEND;
                        _inTag = false;
                    }
                    else if ('/' == ch)
                    {
                        if (end != limit && '>' == _input[end])
                        {
                            type = HtmlTokenType.TAGEND;
                            _inTag = false;
                            ++end;
                        }
                        else
                        {
                            type = HtmlTokenType.TEXT;
                        }
                    }
                    else if ('=' == ch)
                    {
                        type = HtmlTokenType.TEXT;
                    }
                    else if ('"' == ch || '\'' == ch)
                    {
                        type = HtmlTokenType.QSTRING;
                        int delim = ch;
                        for (; end < limit; ++end)
                        {
                            if (_input[end] == delim)
                            {
                                ++end;
                                break;
                            }
                        }
                    }
                    else if (!char.IsWhiteSpace(ch))
                    {
                        type = HtmlTokenType.TEXT;
                        for (; end < limit; ++end)
                        {
                            ch = _input[end];
                            // End a text chunk before />
                            if ((_lastNonIgnorable == null
                                || !_lastNonIgnorable.tokenInContextMatches(_input, "="))
                                && '/' == ch && end + 1 < limit
                                && '>' == _input[end + 1])
                            {
                                break;
                            }
                            else if ('>' == ch || '=' == ch
                                        || char.IsWhiteSpace(ch))
                            {
                                break;
                            }
                            else if ('"' == ch || '\'' == ch)
                            {
                                if (end + 1 < limit)
                                {
                                    char ch2 = _input[end + 1];
                                    if (char.IsWhiteSpace(ch2)
                                        || ch2 == '>' || ch2 == '/')
                                    {
                                        ++end;
                                        break;
                                    }
                                }
                            }
                        }
                    }
                    else
                    {
                        // We skip whitespace tokens inside tag bodies.
                        type = HtmlTokenType.IGNORABLE;
                        while (end < limit && char.IsWhiteSpace(_input[end]))
                        {
                            ++end;
                        }
                    }
                }
                else
                {
                    if (ch == '<')
                    {
                        if (end == limit)
                        {
                            type = HtmlTokenType.TEXT;
                        }
                        else
                        {
                            ch = _input[end];
                            type = null;
                            State? state = null;
                            switch (ch)
                            {
                                case '/':  // close tag?
                                    state = State.SLASH;
                                    ++end;
                                break;
                                case '!':  // Comment or declaration
                                    if (!_inEscapeExemptBlock)
                                    {
                                        state = State.BANG;
                                    }
                                    else if (HtmlTextEscapingModeHelper.allowsEscapingTextSpan(
                                                    _escapeExemptTagName))
                                    {
                                        // Directives, and cdata suppressed in escape
                                        // exempt mode as they could obscure the close of the
                                        // escape exempty block, but comments are similar to escaping
                                        // text spans, and are significant in all CDATA and RCDATA
                                        // blocks except those inside <xmp> tags.
                                        // See "Escaping text spans" in section 8.1.2.6 of HTML5.
                                        // http://www.w3.org/html/wg/html5/#cdata-rcdata-restrictions
                                        state = State.UNESCAPED_LT_BANG;
                                    }
                                    ++end;
                                break;
                                case '?':
                                    if (!_inEscapeExemptBlock)
                                    {
                                        state = State.BOGUS_COMMENT;
                                    }
                                    ++end;
                                break;
                                case '%':
                                    state = State.SERVER_CODE;
                                    ++end;
                                break;
                                default:
                                    if (IsIdentStart(ch) && !_inEscapeExemptBlock)
                                    {
                                        state = State.TAGNAME;
                                        ++end;
                                    }
                                    else if ('<' == ch)
                                    {
                                        type = HtmlTokenType.TEXT;
                                    }
                                    else
                                    {
                                        ++end;
                                    }
                                break;
                            }
                            if (null != state)
                            {
                                //charloop:
                                while (end < limit)
                                {
                                    ch = _input[end];
                                    switch (state)
                                    {
                                        case State.TAGNAME:
                                            if (char.IsWhiteSpace(ch)
                                                || '>' == ch || '/' == ch || '<' == ch)
                                            {
                                                // End processing of an escape exempt block when we see
                                                // a corresponding end tag.
                                                if (_inEscapeExemptBlock
                                                    && '/' == _input[start + 1]
                                                    && _textEscapingMode != HtmlTextEscapingMode.PLAIN_TEXT
                                                    && CanonicalName(start + 2, end)
                                                        .Equals(_escapeExemptTagName))
                                                {
                                                    _inEscapeExemptBlock = false;
                                                    _escapeExemptTagName = null;
                                                    _textEscapingMode = null;
                                                }
                                                type = HtmlTokenType.TAGBEGIN;
                                                // Don't process content as attributes if we're inside
                                                // an escape exempt block.
                                                _inTag = !_inEscapeExemptBlock;
                                                state = State.DONE;
                                                goto end_charloop;
                                            }
                                        break;
                                        case State.SLASH:
                                            if (char.IsLetter(ch))
                                            {
                                                state = State.TAGNAME;
                                            }
                                            else
                                            {
                                                if ('<' == ch)
                                                {
                                                    type = HtmlTokenType.TEXT;
                                                }
                                                else
                                                {
                                                    ++end;
                                                }
                                                goto end_charloop;
                                            }
                                        break;
                                        case State.BANG:
                                            if ('-' == ch)
                                            {
                                                state = State.BANG_DASH;
                                            }
                                            else
                                            {
                                                state = State.DIRECTIVE;
                                            }
                                        break;
                                        case State.BANG_DASH:
                                            if ('-' == ch)
                                            {
                                                state = State.COMMENT;
                                            }
                                            else
                                            {
                                                state = State.DIRECTIVE;
                                            }
                                        break;
                                        case State.COMMENT:
                                            if ('-' == ch)
                                            {
                                                state = State.COMMENT_DASH;
                                            }
                                        break;
                                        case State.COMMENT_DASH:
                                            state = ('-' == ch)
                                                ? State.COMMENT_DASH_DASH
                                                : State.COMMENT_DASH;
                                        break;
                                        case State.COMMENT_DASH_DASH:
                                            if ('>' == ch)
                                            {
                                                state = State.DONE;
                                                type = HtmlTokenType.COMMENT;
                                            }
                                            else if ('-' == ch)
                                            {
                                                state = State.COMMENT_DASH_DASH;
                                            }
                                            else
                                            {
                                                state = State.COMMENT_DASH;
                                            }
                                        break;
                                        case State.DIRECTIVE:
                                            if ('>' == ch)
                                            {
                                                type = HtmlTokenType.DIRECTIVE;
                                                state = State.DONE;
                                            }
                                        break;
                                        case State.BOGUS_COMMENT:
                                            if ('>' == ch)
                                            {
                                                type = HtmlTokenType.QMARKMETA;
                                                state = State.DONE;
                                            }
                                        break;
                                        case State.SERVER_CODE:
                                            if ('%' == ch)
                                            {
                                                state = State.SERVER_CODE_PCT;
                                            }
                                        break;
                                        case State.SERVER_CODE_PCT:
                                            if ('>' == ch)
                                            {
                                                type = HtmlTokenType.SERVERCODE;
                                                state = State.DONE;
                                            }
                                            else if ('%' != ch)
                                            {
                                                state = State.SERVER_CODE;
                                            }
                                        break;
                                        case State.UNESCAPED_LT_BANG:
                                            if ('-' == ch)
                                            {
                                                state = State.UNESCAPED_LT_BANG_DASH;
                                            }
                                            else
                                            {
                                                type = HtmlTokenType.TEXT;
                                                state = State.DONE;
                                            }
                                        break;
                                        case State.UNESCAPED_LT_BANG_DASH:
                                            if ('-' == ch)
                                            {
                                                // According to HTML 5 section 8.1.2.6

                                                // An escaping text span start may share its
                                                // U+002D HYPHEN-MINUS characters with its
                                                // corresponding escaping text span end.
                                                state = State.ESCAPING_TEXT_SPAN_DASH_DASH;
                                            }
                                            else
                                            {
                                                type = HtmlTokenType.TEXT;
                                                state = State.DONE;
                                            }
                                        break;
                                        case State.ESCAPING_TEXT_SPAN:
                                            if ('-' == ch)
                                            {
                                                state = State.ESCAPING_TEXT_SPAN_DASH;
                                            }
                                        break;
                                        case State.ESCAPING_TEXT_SPAN_DASH:
                                            if ('-' == ch)
                                            {
                                                state = State.ESCAPING_TEXT_SPAN_DASH_DASH;
                                            }
                                            else
                                            {
                                                state = State.ESCAPING_TEXT_SPAN;
                                            }
                                        break;
                                        case State.ESCAPING_TEXT_SPAN_DASH_DASH:
                                            if ('>' == ch)
                                            {
                                                type = HtmlTokenType.TEXT;
                                                state = State.DONE;
                                            }
                                            else if ('-' != ch)
                                            {
                                                state = State.ESCAPING_TEXT_SPAN;
                                            }
                                        break;
                                        case State.DONE:
                                        throw new InvalidOperationException(
                                            "Unexpectedly DONE while lexing HTML token stream");
                                    }
                                    ++end;
                                    if (State.DONE == state) { break; }
                                }
                                end_charloop:
                                if (end == limit)
                                {
                                    switch (state)
                                    {
                                        case State.DONE:
                                        break;
                                        case State.BOGUS_COMMENT:
                                            type = HtmlTokenType.QMARKMETA;
                                        break;
                                        case State.COMMENT:
                                        case State.COMMENT_DASH:
                                        case State.COMMENT_DASH_DASH:
                                            type = HtmlTokenType.COMMENT;
                                        break;
                                        case State.DIRECTIVE:
                                        case State.SERVER_CODE:
                                        case State.SERVER_CODE_PCT:
                                            type = HtmlTokenType.SERVERCODE;
                                        break;
                                        case State.TAGNAME:
                                            type = HtmlTokenType.TAGBEGIN;
                                        break;
                                        default:
                                            type = HtmlTokenType.TEXT;
                                        break;
                                    }
                                }
                            }
                        }
                    }
                    else
                    {
                        type = null;
                    }
                }
                if (null == type)
                {
                    while (end < limit && '<' != _input[end]) { ++end; }
                    type = HtmlTokenType.TEXT;
                }

                _offset = end;
                HtmlToken result = HtmlToken.instance(start, end, (HtmlTokenType)type);
                if (type != HtmlTokenType.IGNORABLE) { _lastNonIgnorable = result; }
                return result;
            }

            private string CanonicalName(int start, int end)
            {
                return HtmlLexer.canonicalName(_input.Substring(start, end));
            }

            private static bool IsIdentStart(char ch)
            {
                return ch >= 'A' && ch <= 'z' && (ch <= 'Z' || ch >= 'a');
            }

            internal static HtmlToken reclassify(HtmlToken token, HtmlTokenType type)
            {
                return HtmlToken.instance(token.start, token.end, type);
            }
        }
    }


    /**
    * A TokenStream that lazily fetches one token at a time.
    *
    * @author Mike Samuel (mikesamuel@gmail.com)
    */
    internal abstract class AbstractTokenStream : TokenStream 
    {
        private HtmlToken _tok;

        public bool hasNext()
        {
            if (_tok == null) { _tok = produce(); }
            return _tok != null;
        }

        public HtmlToken next()
        {
            if (this._tok == null) { this._tok = produce(); }
            HtmlToken t = this._tok;
            if (t == null) { throw new InvalidOperationException("No next token"); }
            this._tok = null;
            return t;
        }

        protected abstract HtmlToken produce();
    }
}