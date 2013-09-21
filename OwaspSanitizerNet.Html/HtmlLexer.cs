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

namespace OwaspSanitizerNet
{
    /**
     * A flexible lexer for HTML.
     * This is hairy code, but it is outside the TCB for the HTML sanitizer.
     *
     * @author Mike Samuel <mikesamuel@gmail.com>
     */
    internal sealed class HtmlLexer : AbstractTokenStream
    {
        private readonly String _input;
        private readonly HtmlInputSplitter _splitter;
        private State _state = State.OutsideTag;

        public HtmlLexer(String input)
        {
            _input = input;
            _splitter = new HtmlInputSplitter(input);
        }

        /**
         * Normalize case of names that are not name-spaced.  This lower-cases HTML
         * element and attribute names, but not ones for embedded SVG or MATHML.
         */
        internal static String CanonicalName(String elementOrAttribName)
        {
            return elementOrAttribName.IndexOf(':') >= 0
                ? elementOrAttribName : elementOrAttribName.ToLowerInvariant();
        }

        /**
         * An FSM that lets us reclassify text tokens inside tags as attribute
         * names/values
         */
        private enum State
        {
            OutsideTag,
            InTag,
            SawName,
            SawEq,
        }

        /**
         * Makes sure that this.token contains a token if one is available.
         * This may require fetching and combining multiple tokens from the underlying
         * splitter.
         */
        protected override HtmlToken Produce()
        {
            HtmlToken token = ReadToken();
            if (token == null) { return null; }

            switch (token.Type)
            {

                // Keep track of whether we're inside a tag or not.
                case HtmlTokenType.TAGBEGIN:
                    _state = State.InTag;
                    break;
                case HtmlTokenType.TAGEND:
                    if (_state == State.SawEq && HtmlTokenType.TAGEND == token.Type)
                    {
                        // Distinguish <input type=checkbox checked=> from
                        // <input type=checkbox checked>
                        PushbackToken(token);
                        _state = State.InTag;
                        return HtmlToken.Instance(
                            token.Start, token.Start, HtmlTokenType.ATTRVALUE);
                    }

                    _state = State.OutsideTag;
                    break;

                // Drop ignorable tokens by zeroing out the one received and recursing
                case HtmlTokenType.IGNORABLE:
                    return Produce();

                // collapse adjacent text nodes if we're outside a tag, or otherwise,
                // Recognize attribute names and values.
                default:
                    switch (_state)
                    {
                        case State.OutsideTag:
                            if (HtmlTokenType.TEXT == token.Type
                                || HtmlTokenType.UNESCAPED == token.Type)
                            {
                                token = CollapseSubsequent(token);
                            }
                            break;
                        case State.InTag:
                            if (HtmlTokenType.TEXT == token.Type
                                && !token.TokenInContextMatches(_input, "="))
                            {
                                // Reclassify as attribute name
                                token = HtmlInputSplitter.Reclassify(
                                    token, HtmlTokenType.ATTRNAME);
                                _state = State.SawName;
                            }
                            break;
                        case State.SawName:
                            if (HtmlTokenType.TEXT == token.Type)
                            {
                                if (token.TokenInContextMatches(_input, "="))
                                {
                                    _state = State.SawEq;
                                    // Skip the '=' token
                                    return Produce();
                                }
                                // Reclassify as attribute name
                                token = HtmlInputSplitter.Reclassify(
                                    token, HtmlTokenType.ATTRNAME);
                            }
                            else
                            {
                                _state = State.InTag;
                            }
                            break;
                        case State.SawEq:
                            if (HtmlTokenType.TEXT == token.Type
                                || HtmlTokenType.QSTRING == token.Type)
                            {
                                if (HtmlTokenType.TEXT == token.Type)
                                {
                                    // Collapse adjacent text nodes to properly handle
                                    //   <a onclick=this.clicked=true>
                                    //   <a title=foo bar>
                                    token = CollapseAttributeName(token);
                                }
                                // Reclassify as value
                                token = HtmlInputSplitter.Reclassify(
                                    token, HtmlTokenType.ATTRVALUE);
                                _state = State.InTag;
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
                 (next = PeekToken(0)) != null && next.Type == token.Type;
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
            for (HtmlToken t; (t = PeekToken(nToMerge)) != null; )
            {
                if (t.Type == HtmlTokenType.IGNORABLE)
                {
                    HtmlToken tok = PeekToken(nToMerge + 1);
                    if (tok == null) { break; }
                    if (tok.Type != HtmlTokenType.TEXT) { break; }
                    if (IsValuelessAttribute(_input.Substring(tok.Start, tok.End)))
                    {
                        break;
                    }
                    HtmlToken eq = PeekToken(nToMerge + 2);
                    if (eq != null && eq.Type == HtmlTokenType.IGNORABLE)
                    {
                        eq = PeekToken(nToMerge + 3);
                    }
                    if (eq == null || eq.TokenInContextMatches(_input, "="))
                    {
                        break;
                    }
                }
                else if (t.Type != HtmlTokenType.TEXT)
                {
                    break;
                }
                ++nToMerge;
            }
            if (nToMerge == 0) { return token; }

            int end;
            do
            {
                end = ReadToken().End;
            } while (--nToMerge > 0);

            return HtmlToken.Instance(token.Start, end, HtmlTokenType.TEXT);
        }

        private static HtmlToken Join(HtmlToken a, HtmlToken b)
        {
            return HtmlToken.Instance(a.Start, b.End, a.Type);
        }

        private readonly LinkedList<HtmlToken> _lookahead = new LinkedList<HtmlToken>();
        private HtmlToken ReadToken()
        {
            if (_lookahead.Count != 0)
            {
                HtmlToken token = _lookahead.First.Value;
                _lookahead.RemoveFirst();
                return token;
            }
            if (_splitter.HasNext)
            {
                return _splitter.Next();
            }
            return null;
        }

        private HtmlToken PeekToken(int i)
        {
            while (_lookahead.Count <= i && _splitter.HasNext)
            {
                _lookahead.AddLast(_splitter.Next());
            }
            return _lookahead.Count > i ? _lookahead.ElementAt(i) : null;
        }

        private void PushbackToken(HtmlToken token)
        {
            _lookahead.AddFirst(token);
        }

        /** Can the attribute appear in HTML without a value. */
        private static bool IsValuelessAttribute(String attribName)
        {
            bool valueless = ValuelessAttribNames.Contains(
                attribName.ToLowerInvariant());
            return valueless;
        }

        // From http://issues.apache.org/jira/browse/XALANC-519
        private static readonly HashSet<String> ValuelessAttribNames = new HashSet<string>(
            new[] { 
          "checked", "compact", "declare", "defer", "disabled",
          "ismap", "multiple", "nohref", "noresize", "noshade",
          "nowrap", "readonly", "selected" 
      });
    }

    /**
     * A token stream that breaks a character stream into <tt>
     * HtmlTokenType.{TEXT,TAGBEGIN,TAGEND,DIRECTIVE,COMMENT,CDATA,DIRECTIVE}</tt>
     * tokens.  The matching of attribute names and values is done in a later step.
     */
    internal sealed class HtmlInputSplitter : AbstractTokenStream
    {
        /** The source of HTML character data. */
        private readonly String _input;
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
        private String _escapeExemptTagName;

        private HtmlTextEscapingMode? _textEscapingMode;

        public HtmlInputSplitter(String input)
        {
            _input = input;
        }

        /**
         * Make sure that there is a token ready to yield in this.token.
         */
        protected override HtmlToken Produce()
        {
            HtmlToken token = ParseToken();
            if (null == token) { return null; }

            // Handle escape-exempt blocks.
            // The parse() method is only dimly aware of escape-excempt blocks, so
            // here we detect the beginning and ends of escape exempt blocks, and
            // reclassify as UNESCAPED, any tokens that appear in the middle.
            if (_inEscapeExemptBlock)
            {
                if (token.Type != HtmlTokenType.SERVERCODE)
                {
                    // classify RCDATA as text since it can contain entities
                    token = Reclassify(
                        token, (_textEscapingMode == HtmlTextEscapingMode.RCDATA
                                ? HtmlTokenType.TEXT
                                : HtmlTokenType.UNESCAPED));
                }
            }
            else
            {
                switch (token.Type)
                {
                    case HtmlTokenType.TAGBEGIN:
                        {
                            String canonTagName = CanonicalName(
                                token.Start + 1, token.End);
                            if (HtmlTextEscapingModeExtensions.isTagFollowedByLiteralContent(
                                    canonTagName))
                            {
                                _escapeExemptTagName = canonTagName;
                                _textEscapingMode = HtmlTextEscapingModeExtensions.getModeForTag(
                                    canonTagName);
                            }
                            break;
                        }
                    case HtmlTokenType.TAGEND:
                        _inEscapeExemptBlock = null != _escapeExemptTagName;
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
            TagName,
            Slash,
            Bang,
            BangDash,
            Comment,
            CommentDash,
            CommentDashDash,
            Directive,
            Done,
            BogusComment,
            ServerCode,
            ServerCodePct,

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
            UnescapedLtBang,           // <!
            UnescapedLtBangDash,       // <!-
            EscapingTextSpan,          // Inside an escaping text span
            EscapingTextSpanDash,      // Seen - inside an escaping text span
            EscapingTextSpanDashDash,  // Seen -- inside an escaping text span
        }

        private HtmlToken _lastNonIgnorable;
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
                else if (!Char.IsWhiteSpace(ch))
                {
                    type = HtmlTokenType.TEXT;
                    for (; end < limit; ++end)
                    {
                        ch = _input[end];
                        // End a text chunk before />
                        if ((_lastNonIgnorable == null
                             || !_lastNonIgnorable.TokenInContextMatches(_input, "="))
                            && '/' == ch && end + 1 < limit
                            && '>' == _input[end + 1])
                        {
                            break;
                        }
                        if ('>' == ch || '=' == ch
                            || Char.IsWhiteSpace(ch))
                        {
                            break;
                        }
                        if ('"' == ch || '\'' == ch)
                        {
                            if (end + 1 < limit)
                            {
                                char ch2 = _input[end + 1];
                                if (ch2 >= 0 && Char.IsWhiteSpace(ch2)
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
                    while (end < limit && Char.IsWhiteSpace(_input[end]))
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
                                state = State.Slash;
                                ++end;
                                break;
                            case '!':  // Comment or declaration
                                if (!_inEscapeExemptBlock)
                                {
                                    state = State.Bang;
                                }
                                else if (HtmlTextEscapingModeExtensions.allowsEscapingTextSpan(
                                             _escapeExemptTagName))
                                {
                                    // Directives, and cdata suppressed in escape
                                    // exempt mode as they could obscure the close of the
                                    // escape exempty block, but comments are similar to escaping
                                    // text spans, and are significant in all CDATA and RCDATA
                                    // blocks except those inside <xmp> tags.
                                    // See "Escaping text spans" in section 8.1.2.6 of HTML5.
                                    // http://www.w3.org/html/wg/html5/#cdata-rcdata-restrictions
                                    state = State.UnescapedLtBang;
                                }
                                ++end;
                                break;
                            case '?':
                                if (!_inEscapeExemptBlock)
                                {
                                    state = State.BogusComment;
                                }
                                ++end;
                                break;
                            case '%':
                                state = State.ServerCode;
                                ++end;
                                break;
                            default:
                                if (IsIdentStart(ch) && !_inEscapeExemptBlock)
                                {
                                    state = State.TagName;
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
                            bool breakCharLoop = false;
                            while (end < limit && !breakCharLoop)
                            {
                                ch = _input[end];
                                switch (state)
                                {
                                    case State.TagName:
                                        if (Char.IsWhiteSpace(ch)
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
                                            state = State.Done;
                                            breakCharLoop = true;
                                        }
                                        break;
                                    case State.Slash:
                                        if (Char.IsLetter(ch))
                                        {
                                            state = State.TagName;
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
                                            breakCharLoop = true;
                                        }
                                        break;
                                    case State.Bang:
                                        state = '-' == ch ? State.BangDash : State.Directive;
                                        break;
                                    case State.BangDash:
                                        state = '-' == ch ? State.Comment : State.Directive;
                                        break;
                                    case State.Comment:
                                        if ('-' == ch)
                                        {
                                            state = State.CommentDash;
                                        }
                                        break;
                                    case State.CommentDash:
                                        state = ('-' == ch)
                                            ? State.CommentDashDash
                                            : State.CommentDash;
                                        break;
                                    case State.CommentDashDash:
                                        if ('>' == ch)
                                        {
                                            state = State.Done;
                                            type = HtmlTokenType.COMMENT;
                                        }
                                        else if ('-' == ch)
                                        {
                                            state = State.CommentDashDash;
                                        }
                                        else
                                        {
                                            state = State.CommentDash;
                                        }
                                        break;
                                    case State.Directive:
                                        if ('>' == ch)
                                        {
                                            type = HtmlTokenType.DIRECTIVE;
                                            state = State.Done;
                                        }
                                        break;
                                    case State.BogusComment:
                                        if ('>' == ch)
                                        {
                                            type = HtmlTokenType.QMARKMETA;
                                            state = State.Done;
                                        }
                                        break;
                                    case State.ServerCode:
                                        if ('%' == ch)
                                        {
                                            state = State.ServerCodePct;
                                        }
                                        break;
                                    case State.ServerCodePct:
                                        if ('>' == ch)
                                        {
                                            type = HtmlTokenType.SERVERCODE;
                                            state = State.Done;
                                        }
                                        else if ('%' != ch)
                                        {
                                            state = State.ServerCode;
                                        }
                                        break;
                                    case State.UnescapedLtBang:
                                        if ('-' == ch)
                                        {
                                            state = State.UnescapedLtBangDash;
                                        }
                                        else
                                        {
                                            type = HtmlTokenType.TEXT;
                                            state = State.Done;
                                        }
                                        break;
                                    case State.UnescapedLtBangDash:
                                        if ('-' == ch)
                                        {
                                            // According to HTML 5 section 8.1.2.6

                                            // An escaping text span start may share its
                                            // U+002D HYPHEN-MINUS characters with its
                                            // corresponding escaping text span end.
                                            state = State.EscapingTextSpanDashDash;
                                        }
                                        else
                                        {
                                            type = HtmlTokenType.TEXT;
                                            state = State.Done;
                                        }
                                        break;
                                    case State.EscapingTextSpan:
                                        if ('-' == ch)
                                        {
                                            state = State.EscapingTextSpanDash;
                                        }
                                        break;
                                    case State.EscapingTextSpanDash:
                                        state = '-' == ch ? State.EscapingTextSpanDashDash : State.EscapingTextSpan;
                                        break;
                                    case State.EscapingTextSpanDashDash:
                                        if ('>' == ch)
                                        {
                                            type = HtmlTokenType.TEXT;
                                            state = State.Done;
                                        }
                                        else if ('-' != ch)
                                        {
                                            state = State.EscapingTextSpan;
                                        }
                                        break;
                                    case State.Done:
                                        throw new AssertionException(
                                            "Unexpectedly DONE while lexing HTML token stream");
                                }
                                ++end;
                                if (State.Done == state) { break; }
                            }
                            if (end == limit)
                            {
                                switch (state)
                                {
                                    case State.Done:
                                        break;
                                    case State.BogusComment:
                                        type = HtmlTokenType.QMARKMETA;
                                        break;
                                    case State.Comment:
                                    case State.CommentDash:
                                    case State.CommentDashDash:
                                        type = HtmlTokenType.COMMENT;
                                        break;
                                    case State.Directive:
                                    case State.ServerCode:
                                    case State.ServerCodePct:
                                        type = HtmlTokenType.SERVERCODE;
                                        break;
                                    case State.TagName:
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
            HtmlToken result = HtmlToken.Instance(start, end, (HtmlTokenType)type);
            if (type != HtmlTokenType.IGNORABLE) { _lastNonIgnorable = result; }
            return result;
        }

        private String CanonicalName(int start, int end)
        {
            return HtmlLexer.CanonicalName(_input.Substring(start, end));
        }

        private static bool IsIdentStart(char ch)
        {
            return ch >= 'A' && ch <= 'z' && (ch <= 'Z' || ch >= 'a');
        }

        internal static HtmlToken Reclassify(HtmlToken token, HtmlTokenType type)
        {
            return HtmlToken.Instance(token.Start, token.End, type);
        }
    }


    /**
     * A TokenStream that lazily fetches one token at a time.
     *
     * @author Mike Samuel <mikesamuel@gmail.com>
     */
    abstract class AbstractTokenStream : ITokenStream
    {
        private HtmlToken _tok;

        public bool HasNext
        {
            get
            {
                if (_tok == null)
                {
                    _tok = Produce();
                }
                return _tok != null;
            }
        }

        public HtmlToken Next()
        {
            if (_tok == null) { _tok = Produce(); }
            HtmlToken t = _tok;
            if (t == null) { throw new InvalidOperationException("No next element"); }
            _tok = null;
            return t;
        }

        protected abstract HtmlToken Produce();
    }

}
