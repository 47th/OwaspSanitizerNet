// Copyright (c) 2013, Mike Samuel
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
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace OwaspSanitizerNet.Html
{
    /**
     * Given a string of CSS, produces a string of normalized CSS with certain
     * useful properties detailed below.
     * <ul>
     *   <li>All runs of white-space and comment tokens (including CDO and CDC)
     *     have been replaced with a single space character.</li>
     *   <li>All strings are quoted and escapes are escaped according to the
     *     following scheme:
     *     <table>
     *       <tr><td>NUL</td>            <td><code>\0</code></tr>
     *       <tr><td>line feed</td>      <td><code>\a</code></tr>
     *       <tr><td>vertical feed</td>  <td><code>\c</code></tr>
     *       <tr><td>carriage return</td><td><code>\d</code></tr>
     *       <tr><td>double quote</td>   <td><code>\22</code></tr>
     *       <tr><td>ampersand &amp;</td><td><code>\26</code></tr>
     *       <tr><td>single quote</td>   <td><code>\27</code></tr>
     *       <tr><td>left-angle &lt;</td><td><code>\3c</code></tr>
     *       <tr><td>rt-angle &gt;</td>  <td><code>\3e</code></tr>
     *       <tr><td>back slash</td>     <td><code>\\</code></tr>
     *       <tr><td>all others</td>     <td>raw</td></tr>
     *     </table>
     *   </li>
     *   <li>All <code>url(&hellip;)</code> tokens are quoted.
     *   <li>All keywords, identifiers, and hex literals are lower-case and have
     *       embedded escape sequences decoded, except that .</li>
     *   <li>All brackets nest properly.</li>
     *   <li>Does not contain any case-insensitive variant of the sequences
     *       {@code <!--}, {@code -->}, {@code <![CDATA[}, {@code ]]>}, or
     *       {@code </style}.</li>
     *   <li>All delimiters that can start longer tokens are followed by a space.
     * </ul>
     */
    internal sealed class CssTokens : IEnumerable<string>
    {
        private readonly String _normalizedCss;
        private readonly Brackets _brackets;
        private readonly int[] _tokenBreaks;
        private readonly TokenType[] _tokenTypes;

        public IEnumerator<string> GetEnumerator()
        {
            return new TokenIterator(this, _tokenTypes.Length);
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public static CssTokens Lex(String css)
        {
            var lexer = new Lexer(css);
            lexer.Lex();
            return lexer.Build();
        }

        /** A cursor into a list of tokens. */
        internal sealed class TokenIterator : IEnumerator<string>
        {
            private readonly CssTokens _cssTokens;
            private int _tokenIndex = -1;
            private readonly int _limit;

            public TokenIterator(CssTokens cssTokens, int limit)
            {
                _cssTokens = cssTokens;
                _limit = limit;
            }

            /** The type of the current token. */
            public TokenType Type
            {
                get
                {
                    return _cssTokens._tokenTypes[_tokenIndex];
                }
            }

            public int TokenIndex
            {
                get
                {
                    return _tokenIndex;
                }
            }

            public string Token
            {
                get
                {
                    return _cssTokens._normalizedCss.Substring(StartOffset, EndOffset);
                }
            }

            public int StartOffset
            {
                get { return _cssTokens._tokenBreaks[_tokenIndex]; }
            }

            private int EndOffset
            {
                get
                {
                    return _cssTokens._tokenBreaks[_tokenIndex + 1];
                }
            }

            public bool HasToken
            {
                get
                {
                    return _tokenIndex > 0 && _tokenIndex < _limit;
                }
            }

            public bool HasTokenAfterSpace
            {
                get
                {
                    while (HasToken)
                    {
                        if (Type != TokenType.Whitespace)
                        {
                            return true;
                        }
                        MoveNext();
                    }
                    return false;
                }
            }

            public TokenIterator SpliceToEnd()
            {
                if (!HasToken) { throw new InvalidOperationException("No next element"); }
                int end = _cssTokens._brackets.Partner(_tokenIndex);
                if (end < 0)
                {
                    return null;
                }
                var between = new TokenIterator(_cssTokens, end)
                {
                    _tokenIndex = _tokenIndex + 1
                };
                _tokenIndex = end + 1;
                return between;
            }

            #region IEnumerator<string> members

            public bool MoveNext()
            {
                ++_tokenIndex;
                return HasToken;
            }

            public void Reset()
            {
                _tokenIndex = -1;
            }

            public string Current
            {
                get { return Token; }
            }

            object IEnumerator.Current
            {
                get { return Current; }
            }

            public void Dispose()
            {
            }

            #endregion
        }

        private CssTokens(
            String normalizedCss, Brackets brackets, int[] tokenBreaks,
            TokenType[] tokenTypes)
        {
            _normalizedCss = normalizedCss;
            _brackets = brackets;
            _tokenBreaks = tokenBreaks;
            _tokenTypes = tokenTypes;
        }

        public enum TokenType
        {
            /** An identifier. */
            Ident,
            /** An identifier prefixed with a period. */
            DotIdent,
            /** A function name and opening bracket. */
            Function,
            /** An {@code @<identifier>} directive token. */
            At,
            /** A hash token that contains non-hex characters. */
            HashId,
            /** A hash token that could be a color literal. */
            HashUnrestricted,
            /** A quoted string. */
            String,
            /** A URL of the form <code>url("...")</code>. */
            Url,
            /** A single character. */
            Delim,
            /** A scalar numeric value. */
            Number,
            /** A percentage. */
            Percentage,
            /** A numeric value with a unit suffix. */
            Dimension,
            /** A numeric value with an unknown unit suffix. */
            BadDimension,
            /** {@code U+<hex-or-qmark>} */
            UnicodeRange,
            /**
             * include-match, dash-match, prefix-match, suffix-match, substring-match
             */
            Match,
            /** {@code ||} */
            Column,
            /** A run of white-space, comment, CDO, and CDC tokens. */
            Whitespace,
            /** {@code :} */
            Colon,
            /** {@code ;} */
            Semicolon,
            /** {@code ,} */
            Comma,
            /** {@code [} */
            LeftSquare,
            /** {@code ]} */
            RightSquare,
            /** {@code (} */
            LeftParen,
            /** {@code )} */
            RightParen,
            /** <code>{</code> */
            LeftCurly,
            /** <code>}</code> */
            RightCurly,
        }

        /**
         * Maps tokens to their partners.  A close bracket token like {@code (} may
         * have a partner token like {@code )} if properly nested, and vice-versa.
         */
        private sealed class Brackets
        {
            /**
             * For each token index, the index of the indexed token's partner or -1 if
             * it has none.
             */
            private readonly int[] _brackets;

            internal Brackets(int[] brackets)
            {
                _brackets = brackets;
            }

            /** The index of the partner token or -1 if none. */
            internal int Partner(int tokenIndex)
            {
                int bracketIndex = BracketIndexForToken(tokenIndex);
                if (bracketIndex < 0) { return -1; }
                return _brackets[(bracketIndex << 1) + 1];
            }

            int BracketIndexForToken(int target)
            {
                // Binary search by leftmost element of pair.
                int left = 0;
                int right = _brackets.Length >> 1;
                while (left < right)
                {
                    int mid = left + ((right - left) >> 1);
                    int value = _brackets[mid << 1];
                    if (value == target) { return mid; }
                    if (value < target)
                    {
                        left = mid + 1;
                    }
                    else
                    {
                        right = mid;
                    }
                }
                return -1;
            }
        }

        private static readonly int[] ZeroInts = new int[0];

        private static readonly TokenType[] ZeroTypes = new TokenType[0];

        private static readonly Brackets EmptyBrackets = new Brackets(ZeroInts);

        private static readonly CssTokens Empty = new CssTokens(
            "", EmptyBrackets, ZeroInts, ZeroTypes);

        /**
         * Tokenizes according to section 4 of http://dev.w3.org/csswg/css-syntax/
         */
        private sealed class Lexer
        {
            private readonly String _css;
            private readonly StringBuilder _sb;
            private int _pos;
            private readonly int _cssLimit;

            private List<TokenType> _tokenTypes;
            private int[] _tokenBreaks = new int[128];
            private int _tokenBreaksLimit;

            /**
             * For each bracket, 2 ints: the token index of the bracket, and the token
             * index of its partner.
             * The array is sorted by the first int.
             * The second int is -1 when the bracket has not yet been closed.
             */
            private int[] _brackets = ZeroInts;
            /**
             * The number of elements in {@link #brackets} that are valid.
             * {@code brackets[bracketsLimit:]} is zeroed space that the list can grow
             * into.
             */
            private int _bracketsLimit;
            /**
             * For each bracket that has not been closed, 2 ints:
             * its index in {@link #brackets} and the character of its close bracket
             * as an int.
             * This is a bracket stack so the array is sorted by the first int.
             */
            private int[] _open = ZeroInts;
            /**
             * The number of elements in {@link #open} that are valid.
             * {@code open[openLimit:]} is garbage space that the stack can grow into.
             */
            private int _openLimit;

            internal Lexer(String css)
            {
                _css = css;
                _sb = new StringBuilder();
                _cssLimit = css.Length;
            }

            private TokenType OpenBracket(char bracketChar)
            {
                char close;
                TokenType type;
                switch (bracketChar)
                {
                    case '(': close = ')'; type = TokenType.LeftParen; break;
                    case '[': close = ']'; type = TokenType.LeftSquare; break;
                    case '{': close = '}'; type = TokenType.LeftCurly; break;
                    default:
                        throw new AssertionException("Invalid open bracket " + bracketChar);
                }
                _brackets = ExpandIfNecessary(_brackets, _bracketsLimit, 2);
                _open = ExpandIfNecessary(_open, _openLimit, 2);
                _open[_openLimit++] = _bracketsLimit;
                _open[_openLimit++] = close;
                _brackets[_bracketsLimit++] = _tokenBreaksLimit;
                _brackets[_bracketsLimit++] = -1;
                _sb.Append(bracketChar);
                return type;
            }

            private void CloseBracket(char bracketChar)
            {
                int openLimitAfterClose = _openLimit;
                do
                {
                    if (openLimitAfterClose == 0)
                    {
                        // Drop an orphaned close bracket.
                        BreakOutput();
                        return;
                    }
                    openLimitAfterClose -= 2;
                } while (bracketChar != _open[openLimitAfterClose + 1]);
                CloseBrackets(openLimitAfterClose);
            }

            private void CloseBrackets(int openLimitAfterClose)
            {
                // Make sure we've got space on brackets.
                int spaceNeeded = _openLimit - openLimitAfterClose;
                _brackets = ExpandIfNecessary(_brackets, _bracketsLimit, spaceNeeded);

                int closeTokenIndex = _tokenBreaksLimit;
                while (_openLimit > openLimitAfterClose)
                {
                    // Pop the stack.
                    int closeBracket = _open[--_openLimit];
                    int openBracketIndex = _open[--_openLimit];
                    int openTokenIndex = _brackets[openBracketIndex];
                    // Update open bracket to point to its partner.
                    _brackets[openBracketIndex + 1] = closeTokenIndex;
                    // Emit the close bracket.
                    _brackets[_bracketsLimit++] = closeTokenIndex;
                    _brackets[_bracketsLimit++] = openTokenIndex;
                    _sb.Append(Char.ConvertFromUtf32(closeBracket));
                    closeTokenIndex++;
                }
            }

            internal CssTokens Build()
            {
                // Close any still open brackets.
                {
                    int startOfCloseBrackets = _sb.Length;
                    CloseBrackets(0);
                    EmitMergedTokens(startOfCloseBrackets, _sb.Length);
                }

                if (_tokenTypes == null) { return Empty; }
                int[] bracketsTrunc = TruncateOrShare(_brackets, _bracketsLimit);

                // Strip any trailing space off, since it may have been inserted by a
                // breakAfter call anyway.
                int cssEnd = _sb.Length;
                if (cssEnd > 0 && _sb[cssEnd - 1] == ' ')
                {
                    --cssEnd;
                    _tokenTypes.RemoveAt(--_tokenBreaksLimit);
                }
                String normalizedCss = _sb.ToString().Substring(0, cssEnd);

                // Store the last character on the tokenBreaksList to simplify finding the
                // end of a token.
                _tokenBreaks = ExpandIfNecessary(_tokenBreaks, _tokenBreaksLimit, 1);
                _tokenBreaks[_tokenBreaksLimit++] = normalizedCss.Length;

                int[] tokenBreaksTrunc = TruncateOrShare(_tokenBreaks, _tokenBreaksLimit);
                TokenType[] tokenTypesArr = _tokenTypes.ToArray();

                return new CssTokens(
                    normalizedCss, new Brackets(bracketsTrunc),
                    tokenBreaksTrunc, tokenTypesArr);
            }

            internal void Lex()
            {
                // Fast-track no content.
                ConsumeIgnorable();
                _sb.Clear();
                if (_pos == _cssLimit) { return; }

                _tokenTypes = new List<TokenType>();

                String css = _css;
                int cssLimit = _cssLimit;
                while (_pos < cssLimit)
                {
                    if (_tokenBreaksLimit != _tokenTypes.Count)
                    {
                        throw new AssertionException("token and types out of sync at " + _tokenBreaksLimit
                        + " in `" + css + "`");
                    }
                    // SPEC: 4. Tokenization
                    // The output of the tokenization step is a stream of zero
                    // or more of the following tokens: <ident>, <function>,
                    // <at-keyword>, <hash>, <string>, <bad-string>, <url>,
                    // <bad-url>, <delim>, <number>, <percentage>,
                    // <dimension>, <unicode-range>, <include-match>,
                    // <dash-match>, <prefix-match>, <suffix-match>,
                    // <substring-match>, <column>, <whitespace>, <CDO>,
                    // <CDC>, <colon>, <semicolon>, <comma>, <[>, <]>,
                    // <(>, <)>, <{>, and <}>.

                    // IMPLEMENTS: 4.3 Consume a token
                    char ch = css[_pos];
                    int startOfToken = _pos;
                    int startOfOutputToken = _sb.Length;
                    TokenType type;
                    switch (ch)
                    {
                        case '\t':
                        case '\n':
                        case '\f':
                        case '\r':
                        case ' ':
                        case '\ufeff':
                            ConsumeIgnorable();
                            type = TokenType.Whitespace;
                            break;
                        case '/':
                            {
                                char lookahead = (_pos + 1 < cssLimit) ? css[_pos + 1] : (char)0;
                                if (lookahead == '/' || lookahead == '*')
                                {
                                    ConsumeIgnorable();
                                    type = TokenType.Whitespace;
                                }
                                else
                                {
                                    ConsumeDelim(ch);
                                    type = TokenType.Delim;
                                }
                                break;
                            }
                        case '<':
                            if (ConsumeIgnorable())
                            {  // <!--
                                type = TokenType.Whitespace;
                            }
                            else
                            {
                                ConsumeDelim('<');
                                type = TokenType.Delim;
                            }
                            break;
                        case '>':
                            BreakOutput();
                            _sb.Append('>');
                            type = TokenType.Delim;
                            ++_pos;
                            break;
                        case '@':
                            if (ConsumeAtKeyword())
                            {
                                type = TokenType.At;
                            }
                            else
                            {
                                ConsumeDelim(ch);
                                type = TokenType.Delim;
                            }
                            break;
                        case '#':
                            {
                                _sb.Append('#');
                                TokenType? hashType = ConsumeHash();
                                if (hashType != null)
                                {
                                    type = (TokenType)hashType;
                                }
                                else
                                {
                                    ++_pos;
                                    _sb.Append(' ');
                                    type = TokenType.Delim;
                                }
                                break;
                            }
                        case '"':
                        case '\'':
                            type = ConsumeString();
                            break;
                        case 'U':
                        case 'u':
                            // SPEC handle URL under "ident like token".
                            type = ConsumeUnicodeRange() ? TokenType.UnicodeRange :
                                ConsumeIdentOrUrlOrFunction();
                            break;
                        case '0':
                        case '1':
                        case '2':
                        case '3':
                        case '4':
                        case '5':
                        case '6':
                        case '7':
                        case '8':
                        case '9':
                            type = ConsumeNumberOrPercentageOrDimension();
                            break;
                        case '+':
                        case '-':
                        case '.':
                            {
                                char lookahead = (_pos + 1 < cssLimit) ? css[_pos + 1] : (char)0;
                                if (IsDecimal(lookahead)
                                    || (lookahead == '.' && _pos + 2 < cssLimit
                                        && IsDecimal(css[_pos + 2])))
                                {
                                    type = ConsumeNumberOrPercentageOrDimension();
                                }
                                else if (ch == '+')
                                {
                                    ConsumeDelim(ch);
                                    type = TokenType.Delim;
                                }
                                else if (ch == '-')
                                {
                                    type = ConsumeIgnorable() ? TokenType.Whitespace : 
                                        ConsumeIdentOrUrlOrFunction();
                                }
                                else if (IsIdentPart(lookahead))
                                {
                                    // treat ".<IDENT>" as one token.
                                    _sb.Append('.');
                                    ++_pos;
                                    ConsumeIdent(false);
                                    if (_pos != startOfToken + 1)
                                    {
                                        type = TokenType.DotIdent;
                                        if (_pos < cssLimit)
                                        {
                                            char next = css[_pos];
                                            if ('(' == next)
                                            {
                                                // A dotted identifier followed by a parenthesis is
                                                // ambiguously a function.
                                                _sb.Append(' ');
                                            }
                                        }
                                    }
                                    else
                                    {
                                        type = TokenType.Delim;
                                        _sb.Append(' ');
                                    }
                                }
                                else
                                {
                                    ConsumeDelim('.');
                                    type = TokenType.Delim;
                                }
                                break;
                            }
                        case ':': ConsumeDelim(ch); type = TokenType.Colon; break;
                        case ';': ConsumeDelim(ch); type = TokenType.Semicolon; break;
                        case ',': ConsumeDelim(ch); type = TokenType.Comma; break;
                        case '[':
                        case '(':
                        case '{':
                            type = OpenBracket(ch);
                            ++_pos;
                            break;
                        case '}':
                        case ')':
                        case ']':
                            CloseBracket(ch);
                            ++_pos;
                            // Use DELIM so that a later loop will split output into multiple
                            // tokens since we may have inserted missing close brackets for
                            // unclosed open brackets already on the stack.
                            type = TokenType.Delim;
                            break;
                        case '~':
                        case '|':
                        case '^':
                        case '$':
                        case '*':
                            {
                                char lookahead = (_pos + 1 < cssLimit) ? css[_pos + 1] : (char)0;
                                if (lookahead == '=')
                                {
                                    ConsumeMatch(ch);
                                    type = TokenType.Match;
                                }
                                else if (ch == '|' && lookahead == '|')
                                {
                                    ConsumeColumn();
                                    type = TokenType.Column;
                                }
                                else
                                {
                                    ConsumeDelim(ch);
                                    type = TokenType.Delim;
                                }
                                break;
                            }
                        case '_':
                            type = ConsumeIdentOrUrlOrFunction();
                            break;
                        case '\\':
                            {
                                // Optimistically parse as an ident.
                                type = ConsumeIdentOrUrlOrFunction();
                                // TODO: handle case where "url" is encoded.
                                break;
                            }
                        default:
                            int chlower = ch | 32;
                            if ('a' <= chlower && chlower <= 'z' || ch >= 0x80)
                            {
                                type = ConsumeIdentOrUrlOrFunction();
                            }
                            else if (ch > 0x20)
                            {
                                ConsumeDelim(ch);
                                type = TokenType.Delim;
                            }
                            else
                            {  // Ignore.
                                ConsumeIgnorable();
                                type = TokenType.Whitespace;
                            }
                            break;
                    }
                    if (_pos <= startOfToken)
                    {
                        throw new AssertionException("empty token at " + _pos + ", ch0=" + css[startOfToken]
                            + ":U+" + ((int)css[startOfToken]).ToString("X"));
                    }
                    int endOfOutputToken = _sb.Length;
                    if (endOfOutputToken > startOfOutputToken)
                    {
                        if (type == TokenType.Delim)
                        {
                            EmitMergedTokens(startOfOutputToken, endOfOutputToken);
                        }
                        else
                        {
                            if (type != TokenType.Whitespace
                                && _sb[startOfOutputToken] == ' ')
                            {
                                EmitToken(TokenType.Whitespace, startOfOutputToken);
                                ++startOfOutputToken;
                                if (startOfOutputToken == endOfOutputToken) throw new AssertionException();
                            }
                            EmitToken(type, startOfOutputToken);
                            // Token emitters can emit a space after a token to avoid possible
                            // merges with following tokens
                            if (type != TokenType.Whitespace)
                            {
                                int sbLen = _sb.Length;
                                if (startOfOutputToken + 1 < sbLen
                                    && _sb[sbLen - 1] == ' ')
                                {
                                    EmitToken(TokenType.Whitespace, sbLen - 1);
                                }
                            }
                        }
                    }
                }
            }

            private void EmitMergedTokens(int start, int end)
            {
                // Handle breakOutput and merging of output tokens.
                for (int e = start; e < end; ++e)
                {
                    TokenType delimType;
                    switch (_sb[e])
                    {
                        case ' ': delimType = TokenType.Whitespace; break;
                        case '}': delimType = TokenType.RightCurly; break;
                        case ')': delimType = TokenType.RightParen; break;
                        case ']': delimType = TokenType.RightSquare; break;
                        default: delimType = TokenType.Delim; break;
                    }
                    EmitToken(delimType, e);
                }
            }

            private void EmitToken(TokenType type, int startOfOutputToken)
            {
                if (_tokenBreaksLimit == 0
                    || _tokenBreaks[_tokenBreaksLimit - 1] != startOfOutputToken)
                {
                    _tokenBreaks = ExpandIfNecessary(_tokenBreaks, _tokenBreaksLimit, 1);
                    _tokenBreaks[_tokenBreaksLimit++] = startOfOutputToken;
                    _tokenTypes.Add(type);
                }
            }

            private void ConsumeDelim(char ch)
            {
                _sb.Append(ch);
                switch (ch)
                {
                    // Prevent token merging.
                    case '~':
                    case '|':
                    case '^':
                    case '$':
                    case '\\':
                    case '.':
                    case '+':
                    case '-':
                    case '@':
                    case '/':
                    case '<':
                        _sb.Append(' ');
                        break;
                }
                ++_pos;
            }

            private bool ConsumeIgnorable()
            {
                String css = _css;
                int cssLimit = _cssLimit;
                int posBefore = _pos;
                while (_pos < cssLimit)
                {
                    char ch = css[_pos];
                    if (ch <= 0x20
                        // Treat a BOM as white-space so that it is ignored at the beginning
                        // of a file.
                        || ch == '\ufeff')
                    {
                        ++_pos;
                    }
                    else if (_pos + 1 == cssLimit)
                    {
                        break;
                    }
                    else if (ch == '/')
                    {
                        char next = css[_pos + 1];
                        if (next == '*')
                        {
                            _pos += 2;
                            while (_pos < cssLimit)
                            {
                                int ast = css.IndexOf('*', _pos);
                                if (ast < 0)
                                {
                                    _pos = cssLimit;  // Unclosed /* comment */
                                    break;
                                }
                                // Advance over a run of '*'s.
                                _pos = ast + 1;
                                while (_pos < cssLimit && css[_pos] == '*')
                                {
                                    ++_pos;
                                }
                                if (_pos < cssLimit && css[_pos] == '/')
                                {
                                    ++_pos;
                                    break;
                                }
                            }
                        }
                        else if (next == '/')
                        {  // Non-standard but widely supported
                            while (++_pos < cssLimit)
                            {
                                if (IsLineTerminator(css[_pos])) { break; }
                            }
                        }
                        else
                        {
                            break;
                        }
                    }
                    else if (ch == '<')
                    {
                        if (_pos + 3 < cssLimit
                            && '!' == css[_pos + 1]
                            && '-' == css[_pos + 2]
                            && '-' == css[_pos + 3])
                        {
                            _pos += 4;
                        }
                        else
                        {
                            break;
                        }
                    }
                    else if (ch == '-')
                    {
                        if (_pos + 2 < cssLimit
                            && '-' == css[_pos + 1]
                            && '>' == css[_pos + 2])
                        {
                            _pos += 3;
                        }
                        else
                        {
                            break;
                        }
                    }
                    else
                    {
                        break;
                    }
                }
                if (_pos == posBefore)
                {
                    return false;
                }
                BreakOutput();
                return true;
            }

            private void BreakOutput()
            {
                int last = _sb.Length - 1;
                if (last >= 0 && _sb[last] != ' ') { _sb.Append(' '); }
            }

            private void ConsumeColumn()
            {
                _pos += 2;
                _sb.Append("||");
            }

            private void ConsumeMatch(char ch)
            {
                _pos += 2;
                _sb.Append(ch).Append('=');
            }

            private void ConsumeIdent(bool allowFirstDigit)
            {
                int cssLimit = _cssLimit;
                int last = -1, nCodepoints = 0;
                int sbAtStart = _sb.Length;
                int posAtStart = _pos;
                while (_pos < cssLimit)
                {
                    int posBefore = _pos;

                    int decoded = ReadCodepoint();
                    if (decoded == '\\')
                    {
                        decoded = ConsumeAndDecodeEscapeSequence();
                    }
                    else
                    {
                        ++_pos;
                    }

                    if (decoded >= 0 && IsIdentPart(decoded))
                    {
                        if (!allowFirstDigit && nCodepoints < 2
                            && '0' <= decoded && decoded <= '9')
                        {
                            // Don't allow encoded identifiers that look like numeric tokens
                            // like \-1 or ones that start with an encoded decimal digit.
                            if (last == '-' || last == -1)
                            {
                                _pos = posAtStart;
                                _sb.Length = sbAtStart;
                                return;
                            }
                        }
                        _sb.Append(Char.ConvertFromUtf32(decoded));
                        last = decoded;
                        ++nCodepoints;
                    }
                    else
                    {
                        _pos = posBefore;
                        return;
                    }
                }
            }

            private bool ConsumeAtKeyword()
            {
                if (_css[_pos] != '@') throw new AssertionException();
                int bufferLengthBeforeWrite = _sb.Length;
                _sb.Append('@');
                int posBeforeKeyword = ++_pos;
                ConsumeIdent(false);
                if (_pos == posBeforeKeyword)
                {
                    --_pos;  // back up over '@'
                    _sb.Length = bufferLengthBeforeWrite;  // Unwrite the '@'
                    return false;
                }
                return true;
            }


            private int ConsumeAndDecodeEscapeSequence()
            {
                String css = _css;
                int cssLimit = _cssLimit;
                if (css[_pos] == '\\') throw new AssertionException();
                if (_pos + 1 >= cssLimit) { return -1; }
                char esc = css[_pos + 1];
                if (IsLineTerminator(esc)) { return -1; }
                int escLower = esc | 32;
                if (('0' <= esc && esc <= '9')
                    || ('a' <= escLower && escLower <= 'f'))
                {
                    int hexValue = 0;
                    int hexStart = _pos + 1;
                    int hexLimit = Math.Min(_pos + 7, cssLimit);
                    int hexEnd = hexStart;
                    do
                    {
                        hexValue = (hexValue << 4)
                            | (esc <= '9' ? esc - '0' : escLower - ('a' - 10));
                        ++hexEnd;
                        if (hexEnd == hexLimit) { break; }
                        esc = css[hexEnd];
                        escLower = esc | 32;
                    } while (('0' <= esc && esc <= '9')
                             || ('a' <= escLower && escLower <= 'f'));
                    if (!IsDefinedChar(hexValue))
                    {
                        hexValue = 0xfffd;
                    }
                    _pos = hexEnd;
                    if (_pos < cssLimit)
                    {
                        // A sequence of hex digits can be followed by a space that allows
                        // so that code-point U+A followed by the letter 'b' can be rendered
                        // as "\a b" since "\ab" specifies the single code-point U+AB.
                        char next = css[_pos];
                        if (next == ' ' || next == '\t' || IsLineTerminator(next))
                        {
                            ++_pos;
                        }
                    }
                    return hexValue;
                }
                _pos += 2;
                return esc;
            }

            private const long HexEncodedBitmask =
                (1L << 0) | LineTerminatorBitmask
                | (1L << '"') | (1L << '\'') | (1L << '&') | (1L << '<') | (1L << '>');

            private static bool IsHexEncoded(int codepoint)
            {
                return (0 <= codepoint && codepoint < 63
                        && 0 != ((1L << codepoint) & HexEncodedBitmask));
            }

            private void EncodeCharOntoOutput(int codepoint, int last)
            {
                switch (codepoint)
                {
                    case '\\': _sb.Append("\\\\"); break;
                    case '\0': _sb.Append("\\0"); break;
                    case '\n': _sb.Append("\\a"); break;
                    case '\f': _sb.Append("\\c"); break;
                    case '\r': _sb.Append("\\d"); break;
                    case '\"': _sb.Append("\\22"); break;
                    case '&': _sb.Append("\\26"); break;
                    case '\'': _sb.Append("\\27"); break;
                    case '<': _sb.Append("\\3c"); break;
                    case '>': _sb.Append("\\3e"); break;
                    // The set of escapes above that end with a hex digit must appear in
                    // HEX_ENCODED_BITMASK.
                    case '-':
                        _sb.Append('-');
                        break;
                    default:
                        if (IsHexEncoded(last)
                            // We need to put a space after a trailing hex digit if the
                            // next encoded character on the output would be another hex
                            // digit or a space character.  The other space characters
                            // are handled above.
                            && (codepoint == ' ' || codepoint == '\t'
                                || ('0' <= codepoint && codepoint <= '9')
                                || ('a' <= (codepoint | 32) && (codepoint | 32) <= 'f')))
                        {
                            _sb.Append(' ');
                        }
                        _sb.Append(Char.ConvertFromUtf32(codepoint));
                        break;
                }
            }

            private TokenType ConsumeNumberOrPercentageOrDimension()
            {
                String css = _css;
                int cssLimit = _cssLimit;
                bool isZero = true;
                int intStart = _pos;
                if (intStart < cssLimit)
                {
                    char ch = css[intStart];
                    if (ch == '-' || ch == '+')
                    {
                        ++intStart;
                    }
                }
                // Find the integer part after any sign.
                int intEnd = intStart;
                for (; intEnd < cssLimit; ++intEnd)
                {
                    char ch = css[intEnd];
                    if (!('0' <= ch && ch <= '9')) { break; }
                    if (ch != '0') { isZero = false; }
                }
                // Find a fraction like ".5" or ".".
                int fractionStart = intEnd;
                int fractionEnd = fractionStart;
                if (fractionEnd < cssLimit && '.' == css[fractionEnd])
                {
                    ++fractionEnd;
                    for (; fractionEnd < cssLimit; ++fractionEnd)
                    {
                        char ch = css[fractionEnd];
                        if (!('0' <= ch && ch <= '9')) { break; }
                        if (ch != '0') { isZero = false; }
                    }
                }
                int exponentStart = fractionEnd;
                int exponentIntStart = exponentStart;
                int exponentEnd = exponentStart;
                bool isExponentZero = true;
                if (exponentStart < cssLimit && 'e' == (css[exponentStart] | 32))
                {
                    // 'e' and 'e' in "5e-f" for a
                    exponentEnd = exponentStart + 1;
                    if (exponentEnd < cssLimit)
                    {
                        char ch = css[exponentEnd];
                        if (ch == '+' || ch == '-') { ++exponentEnd; }
                    }
                    exponentIntStart = exponentEnd;
                    for (; exponentEnd < cssLimit; ++exponentEnd)
                    {
                        char ch = css[exponentEnd];
                        if (!('0' <= ch && ch <= '9')) { break; }
                        if (ch != '0') { isExponentZero = false; }
                    }
                    // Since
                    //    dimension := <number> <ident>
                    // the below are technically valid dimensions even though they appear
                    // to have incomplete exponents:
                    //    5e
                    //    5ex
                    //    5e-
                    if (exponentEnd == exponentIntStart)
                    {  // Incomplete exponent.
                        exponentIntStart = exponentEnd = exponentStart;
                        isExponentZero = true;
                    }
                }

                int unitStart = exponentEnd;
                // Skip over space between number and unit.
                // Many user-agents allow "5 ex" instead of "5ex".
                while (unitStart < cssLimit)
                {
                    char ch = css[unitStart];
                    if (ch == ' ' || IsLineTerminator(ch))
                    {
                        ++unitStart;
                    }
                    else
                    {
                        break;
                    }
                }

                if (_sb.Length != 0 && IsIdentPart(_sb[_sb.Length - 1]))
                {
                    _sb.Append(' ');
                }
                // Normalize the number onto the buffer.
                // We will normalize and unit later.
                // Skip the sign if it is positive.
                if (intStart != _pos && '-' == css[_pos] && !isZero)
                {
                    _sb.Append('-');
                }
                if (isZero)
                {
                    _sb.Append('0');
                }
                else
                {
                    // Strip leading zeroes from the integer and exponent and trailing
                    // zeroes from the fraction.
                    while (intStart < intEnd && css[intStart] == '0') { ++intStart; }
                    while (fractionEnd > fractionStart
                           && css[fractionEnd - 1] == '0')
                    {
                        --fractionEnd;
                    }
                    if (intStart == intEnd)
                    {
                        _sb.Append('0');  // .5 -> 0.5
                    }
                    else
                    {
                        _sb.Append(css, intStart, intEnd);
                    }
                    if (fractionEnd > fractionStart + 1)
                    {  // 5. -> 5; 5.0 -> 5
                        _sb.Append(css, fractionStart, fractionEnd);
                    }
                    if (!isExponentZero)
                    {
                        _sb.Append('e');
                        // 1e+1 -> 1e1
                        if ('-' == css[exponentIntStart - 1]) { _sb.Append('-'); }
                        while (exponentIntStart < exponentEnd
                               && css[exponentIntStart] == '0')
                        {
                            ++exponentIntStart;
                        }
                        _sb.Append(css, exponentIntStart, exponentEnd);
                    }
                }

                int unitEnd;
                TokenType type;
                if (unitStart < cssLimit && '%' == css[unitStart])
                {
                    unitEnd = unitStart + 1;
                    type = TokenType.Percentage;
                    _sb.Append('%');
                }
                else
                {
                    // The grammar says that any identifier following a number is a unit.
                    int bufferBeforeUnit = _sb.Length;
                    _pos = unitStart;
                    ConsumeIdent(false);
                    int bufferAfterUnit = _sb.Length;
                    bool knownUnit = IsWellKnownUnit(
                        _sb, bufferBeforeUnit, bufferAfterUnit);
                    if (unitStart == exponentEnd  // No intervening space
                        || knownUnit)
                    {
                        unitEnd = _pos;
                        // 3IN -> 3in
                        for (int i = bufferBeforeUnit; i < bufferAfterUnit; ++i)
                        {
                            char ch = _sb[i];
                            if ('A' <= ch && ch <= 'Z') { _sb[i] = (char)(ch | 32); }
                        }
                    }
                    else
                    {
                        unitEnd = unitStart = exponentEnd;
                        _sb.Length = bufferBeforeUnit;
                    }
                    type = unitStart == unitEnd
                        ? TokenType.Number
                        : knownUnit
                        ? TokenType.Dimension
                        : TokenType.BadDimension;
                }
                _pos = unitEnd;
                if (type != TokenType.Percentage
                    && _pos < cssLimit && css[_pos] == '.')
                {
                    _sb.Append(' ');
                }
                return type;
            }

            private TokenType ConsumeString()
            {
                String css = _css;
                int cssLimit = _cssLimit;

                char delim = css[_pos];
                if (delim != '"' && delim != '\'') throw new AssertionException();
                ++_pos;
                int startOfStringOnOutput = _sb.Length;
                _sb.Append('\'');
                int last = -1;
                bool closed = false;
                while (_pos < cssLimit)
                {
                    char ch = css[_pos];
                    if (ch == delim)
                    {
                        ++_pos;
                        closed = true;
                        break;
                    }
                    if (IsLineTerminator(ch)) { break; }
                    int decoded = ch;
                    if (ch == '\\')
                    {
                        if (_pos + 1 < cssLimit && IsLineTerminator(css[_pos + 1]))
                        {
                            // consume it but generate no tokens.
                            // Lookahead to treat a \r\n sequence as one line-terminator.
                            if (_pos + 2 < cssLimit
                                && css[_pos + 1] == '\r' && css[_pos + 2] == '\n')
                            {
                                _pos += 3;
                            }
                            else
                            {
                                _pos += 2;
                            }
                            continue;
                        }
                        decoded = ConsumeAndDecodeEscapeSequence();
                        if (decoded < 0)
                        {
                            break;
                        }
                    }
                    else
                    {
                        ++_pos;
                    }
                    EncodeCharOntoOutput(decoded, last);
                    last = decoded;
                }
                if (closed)
                {
                    _sb.Append('\'');
                    return TokenType.String;
                }
                // Drop <bad-string>s
                _sb.Length = startOfStringOnOutput;
                BreakOutput();
                return TokenType.Whitespace;
            }

            private TokenType? ConsumeHash()
            {
                if (_css[_pos] != '#')
                    throw new AssertionException();
                ++_pos;
                int beforeIdent = _pos;
                ConsumeIdent(true);
                if (_pos == beforeIdent)
                {
                    _pos = beforeIdent - 1;
                    return null;
                }
                for (int i = beforeIdent; i < _pos; ++i)
                {
                    var chLower = (char)(_css[i] | 32);
                    if (!(('0' <= chLower && chLower <= '9')
                          || ('a' <= chLower && chLower <= 'f')))
                    {
                        return TokenType.HashId;
                    }
                }
                return TokenType.HashUnrestricted;
            }

            private bool ConsumeUnicodeRange()
            {
                String css = _css;
                int cssLimit = _cssLimit;

                if (_pos >= cssLimit || (css[_pos] | 32) != 'u')
                    throw new AssertionException();

                int start = _pos;
                int startOfOutput = _sb.Length;
                ++_pos;
                bool ok = false;
            parse:
                try
                {
                    if (_pos == cssLimit || css[_pos] != '+')
                    {
                        goto parse;
                    }
                    ++_pos;
                    _sb.Append("U+");
                    int numStartDigits = 0;
                    while (_pos < cssLimit && numStartDigits < 6)
                    {
                        var chLower = (char)(css[_pos] | 32);
                        if (('0' <= chLower && chLower <= '9')
                            || ('a' <= chLower && chLower <= 'f'))
                        {
                            _sb.Append(chLower);
                            ++numStartDigits;
                            ++_pos;
                        }
                        else
                        {
                            break;
                        }
                    }
                    if (numStartDigits == 0)
                    {
                        goto parse;
                    }
                    bool hasQmark = false;
                    while (_pos < cssLimit && numStartDigits < 6 && css[_pos] == '?')
                    {
                        hasQmark = true;
                        _sb.Append('?');
                        ++numStartDigits;
                        ++_pos;
                    }
                    if (numStartDigits == 0)
                    {
                        goto parse;
                    }
                    if (_pos < cssLimit && css[_pos] == '-')
                    {
                        if (!hasQmark)
                        {
                            // Look for end of range.
                            ++_pos;
                            _sb.Append('-');
                            int numEndDigits = 0;
                            while (_pos < cssLimit && numEndDigits < 6)
                            {
                                var chLower = (char)(css[_pos] | 32);
                                if (('0' <= chLower && chLower <= '9')
                                    || ('a' <= chLower && chLower <= 'f'))
                                {
                                    ++numEndDigits;
                                    ++_pos;
                                    _sb.Append(chLower);
                                }
                                else
                                {
                                    break;
                                }
                            }
                            if (numEndDigits == 0)
                            {
                                // Back up over '-'
                                --_pos;
                                _sb.Append(' ');
                            }
                        }
                        else
                        {
                            _sb.Append(' ');
                        }
                    }
                    ok = true;
                }
                finally
                {
                    if (!ok)
                    {
                        _pos = start;
                        _sb.Length = startOfOutput;
                    }
                }
                return true;
            }

            private TokenType ConsumeIdentOrUrlOrFunction()
            {
                int bufferStart = _sb.Length;
                int posBefore = _pos;
                ConsumeIdent(false);
                if (_pos == posBefore)
                {
                    ++_pos;
                    BreakOutput();
                    return TokenType.Whitespace;
                }
                bool parenAfter = _pos < _cssLimit && _css[_pos] == '(';
                if (_sb.Length - bufferStart == 3
                    && 'u' == (_sb[bufferStart] | 32)
                    && 'r' == (_sb[bufferStart + 1] | 32)
                    && 'l' == (_sb[bufferStart + 2] | 32))
                {
                    if (parenAfter && ConsumeUrlValue())
                    {
                        _sb[bufferStart] = 'u';
                        _sb[bufferStart + 1] = 'r';
                        _sb[bufferStart + 2] = 'l';
                        return TokenType.Url;
                    }
                    _sb.Length = bufferStart;
                    BreakOutput();
                    return TokenType.Whitespace;
                }
                if (parenAfter)
                {
                    OpenBracket('(');
                    ++_pos;
                    return TokenType.Function;
                }
                if (_pos + 1 < _cssLimit && '.' == _css[_pos])
                {
                    // Prevent merging of ident and number as in
                    //     border:solid.1cm black
                    // when .1 is rewritten to 0.1 becoming
                    //     border:solid0.1cm black
                    char next = _css[_pos + 1];
                    if ('0' <= next && next <= '9')
                    {
                        _sb.Append(' ');
                    }
                }
                return TokenType.Ident;
            }

            private bool ConsumeUrlValue()
            {
                String css = _css;
                int cssLimit = _cssLimit;
                if (_pos == cssLimit || css[_pos] != '(') { return false; }
                ++_pos;
                // skip space.
                for (; _pos < cssLimit; ++_pos)
                {
                    char ch = css[_pos];
                    if (ch != ' ' && !IsLineTerminator(ch)) { break; }
                }
                // Find the value.
                int delim;
                if (_pos < cssLimit)
                {
                    char ch = _pos < cssLimit ? css[_pos] : '\0';
                    if (ch == '"' || ch == '\'')
                    {
                        delim = ch;
                        ++_pos;
                    }
                    else
                    {
                        delim = '\0';
                    }
                }
                else
                {
                    return false;
                }
                _sb.Append("('");
                while (_pos < cssLimit)
                {
                    int decoded = ReadCodepoint();
                    if (delim != 0)
                    {
                        if (decoded == delim)
                        {
                            ++_pos;
                            break;
                        }
                    }
                    else if (decoded <= ' ' || decoded == ')')
                    {
                        break;
                    }
                    if (decoded == '\\')
                    {
                        decoded = ConsumeAndDecodeEscapeSequence();
                        if (decoded < 0)
                        {
                            return false;
                        }
                    }
                    else
                    {
                        ++_pos;
                    }
                    // Any character not in the RFC 3986 safe set is %-encoded.
                    if (decoded < UrlSafe.Length && UrlSafe[decoded])
                    {
                        _sb.Append(Char.ConvertFromUtf32(decoded));
                    }
                    else if (decoded < 0x80)
                    {
                        _sb.Append('%')
                          .Append(HexDigits[((int)((uint)decoded) >> 4) & 0xf])
                          .Append(HexDigits[((int)((uint)decoded) >> 0) & 0xf]);
                    }
                    else if (decoded < 0x800)
                    {
                        int octet0 = 0xc0 | (((int)((uint)decoded) >> 6) & 0x1f),
                            octet1 = 0x80 | (decoded & 0x3f);
                        _sb.Append('%')
                          .Append(HexDigits[((int)((uint)octet0) >> 4) & 0xf])
                          .Append(HexDigits[((int)((uint)octet0) >> 0) & 0xf])
                          .Append('%')
                          .Append(HexDigits[((int)((uint)octet1) >> 4) & 0xf])
                          .Append(HexDigits[((int)((uint)octet1) >> 0) & 0xf]);
                    }
                    else if (decoded < 0x10000)
                    {
                        int octet0 = 0xe0 | (((int)((uint)decoded) >> 12) & 0xf),
                            octet1 = 0x80 | (((int)((uint)decoded) >> 6) & 0x3f),
                            octet2 = 0x80 | (decoded & 0x3f);
                        _sb.Append('%')
                          .Append(HexDigits[((int)((uint)octet0) >> 4) & 0xf])
                          .Append(HexDigits[((int)((uint)octet0) >> 0) & 0xf])
                          .Append('%')
                          .Append(HexDigits[((int)((uint)octet1) >> 4) & 0xf])
                          .Append(HexDigits[((int)((uint)octet1) >> 0) & 0xf])
                          .Append('%')
                          .Append(HexDigits[((int)((uint)octet2) >> 4) & 0xf])
                          .Append(HexDigits[((int)((uint)octet2) >> 0) & 0xf]);
                    }
                    else
                    {
                        int octet0 = 0xf0 | (((int)((uint)decoded) >> 18) & 0x7),
                            octet1 = 0x80 | (((int)((uint)decoded) >> 12) & 0x3f),
                            octet2 = 0x80 | (((int)((uint)decoded) >> 6) & 0x3f),
                            octet3 = 0x80 | (decoded & 0x3f);
                        _sb.Append('%')
                          .Append(HexDigits[((int)((uint)octet0) >> 4) & 0xf])
                          .Append(HexDigits[((int)((uint)octet0) >> 0) & 0xf])
                          .Append('%')
                          .Append(HexDigits[((int)((uint)octet1) >> 4) & 0xf])
                          .Append(HexDigits[((int)((uint)octet1) >> 0) & 0xf])
                          .Append('%')
                          .Append(HexDigits[((int)((uint)octet2) >> 4) & 0xf])
                          .Append(HexDigits[((int)((uint)octet2) >> 0) & 0xf])
                          .Append('%')
                          .Append(HexDigits[((int)((uint)octet3) >> 4) & 0xf])
                          .Append(HexDigits[((int)((uint)octet3) >> 0) & 0xf]);
                    }
                }

                // skip space.
                for (; _pos < cssLimit; ++_pos)
                {
                    char ch = css[_pos];
                    if (ch != ' ' && !IsLineTerminator(ch)) { break; }
                }
                if (_pos < cssLimit && css[_pos] == ')')
                {
                    ++_pos;
                }
                _sb.Append("')");
                return true;
            }

            /**
             * Reads the codepoint at pos, leaving pos at the index of the last code
             * unit.
             */
            private int ReadCodepoint()
            {
                String css = _css;
                char ch = css[_pos];
                if (Char.IsHighSurrogate(ch) && _pos + 1 < _cssLimit)
                {
                    char next = css[_pos + 1];
                    if (Char.IsLowSurrogate(next))
                    {
                        ++_pos;
                        return 0x10000 + (((ch - 0xd800) << 10) | (next - 0xdc00));
                    }
                }
                return ch;
            }
        }

        private static bool IsIdentPart(int codePoint)
        {
            return codePoint >= 0x80
                ? IsDefinedChar(codePoint) && codePoint != '\ufeff'
                : IdentPartAscii[codePoint];
        }

        private static bool IsDefinedChar(int codePoint)
        {
            string surrogate = Char.ConvertFromUtf32(codePoint);
            bool isDefined = char.GetUnicodeCategory(surrogate, 0) != UnicodeCategory.OtherNotAssigned;
            return isDefined;
        }

        private static bool IsDecimal(char ch)
        {
            return '0' <= ch && ch <= '9';
        }

        private static readonly bool[] IdentPartAscii = new bool[128];

        private const int LineTerminatorBitmask = (1 << '\n') | (1 << '\r') | (1 << '\f');

        private static bool IsLineTerminator(char ch)
        {
            return ch < 0x20 && 0 != (LineTerminatorBitmask & (1 << ch));
        }

        private static int[] ExpandIfNecessary(int[] arr, int limit, int needed)
        {
            int neededLength = limit + needed;
            int length = arr.Length;
            if (length >= neededLength) { return arr; }
            var newArr = new int[Math.Max(16, Math.Max(neededLength, length * 2))];
            Array.ConstrainedCopy(arr, 0, newArr, 0, limit);
            return newArr;
        }

        private static int[] TruncateOrShare(int[] arr, int limit)
        {
            if (limit == 0) { return ZeroInts; }
            if (limit == arr.Length)
            {
                return arr;
            }
            var trunc = new int[limit];
            Array.ConstrainedCopy(arr, 0, trunc, 0, limit);
            return trunc;
        }

        private const int LengthUnitType = 0;
        private const int AngleUnitType = 1;
        private const int TimeUnitType = 2;
        private const int FrequencyUnitType = 3;
        private const int ResolutionUnitType = 4;

        /**
         * See http://dev.w3.org/csswg/css-values/#lengths and
         *     http://dev.w3.org/csswg/css-values/#other-units
         */

        private static readonly Trie UnitTrie = new Trie(
            new Dictionary<string, int>
        {
            {"em", LengthUnitType},
            {"ex", LengthUnitType},
            {"ch", LengthUnitType}, // Width of zero character
            {"rem", LengthUnitType}, // Root element font-size
            {"vh", LengthUnitType},
            {"vw", LengthUnitType},
            {"vmin", LengthUnitType},
            {"vmax", LengthUnitType},
            {"px", LengthUnitType},
            {"mm", LengthUnitType},
            {"cm", LengthUnitType},
            {"in", LengthUnitType},
            {"pt", LengthUnitType},
            {"pc", LengthUnitType},
            {"deg", AngleUnitType},
            {"rad", AngleUnitType},
            {"grad", AngleUnitType},
            {"turn", AngleUnitType},
            {"s", TimeUnitType},
            {"ms", TimeUnitType},
            {"hz", FrequencyUnitType},
            {"khz", FrequencyUnitType},
            {"dpi", ResolutionUnitType},
            {"dpcm", ResolutionUnitType},
            {"dppx", ResolutionUnitType},
        });

        private static bool IsWellKnownUnit(StringBuilder s, int start, int end)
        {
            if (start == end) { return false; }
            Trie t = UnitTrie;
            for (int i = start; i < end; ++i)
            {
                char ch = s[i];
                t = t.Lookup('A' <= ch && ch <= 'Z' ? (char)(ch | 32) : ch);
                if (t == null) { return false; }
            }
            return t.IsTerminal;
        }

        private static readonly bool[] UrlSafe = new bool[128];

        private static readonly char[] HexDigits = 
        {
            '0', '1', '2', '3',
            '4', '5', '6', '7',
            '8', '9', 'a', 'b',
            'c', 'd', 'e', 'f'
        };

        static CssTokens()
        {
            for (int i = '0'; i <= '9'; ++i) { IdentPartAscii[i] = true; }
            for (int i = 'A'; i <= 'Z'; ++i) { IdentPartAscii[i] = true; }
            for (int i = 'a'; i <= 'z'; ++i) { IdentPartAscii[i] = true; }
            IdentPartAscii['_'] = true;
            IdentPartAscii['-'] = true;

            // From RFC 3986
            // unreserved  = ALPHA / DIGIT / "-" / "." / "_" / "~"
            for (int i = 'A'; i <= 'Z'; ++i) { UrlSafe[i] = true; }
            for (int i = 'a'; i <= 'z'; ++i) { UrlSafe[i] = true; }
            for (int i = '0'; i <= '9'; ++i) { UrlSafe[i] = true; }
            UrlSafe['-'] = true;
            UrlSafe['.'] = true;
            UrlSafe['_'] = true;
            UrlSafe['~'] = true;
            // gen-delims  = ":" / "/" / "?" / "#" / "[" / "]" / "@"
            UrlSafe[':'] = true;
            UrlSafe['/'] = true;
            UrlSafe['?'] = true;
            UrlSafe['#'] = true;
            UrlSafe['['] = true;
            UrlSafe[']'] = true;
            UrlSafe['@'] = true;
            // sub-delims  = "!" / "$" / "&" / "'" / "(" / ")"
            //             / "*" / "+" / "," / ";" / "="
            UrlSafe['!'] = true;
            UrlSafe['$'] = true;
            UrlSafe['&'] = true;
            // Only used in obsolete mark rule and special in unquoted URLs or comment
            // delimiters.
            // URL_SAFE['\''] = true;
            // URL_SAFE['('] = true;
            // URL_SAFE[')'] = true;
            // URL_SAFE['*'] = true;
            UrlSafe['+'] = true;
            UrlSafe[','] = true;
            UrlSafe[';'] = true;
            UrlSafe['='] = true;
            // % is used to encode unsafe octets.
            UrlSafe['%'] = true;
        }
    }

}
