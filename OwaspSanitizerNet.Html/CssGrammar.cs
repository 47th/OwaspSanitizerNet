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
using System.Globalization;
using System.Text;

namespace OwaspSanitizerNet.Html
{
    internal sealed class CssGrammar
    {
        private static void ErrorRecoveryUntilSemiOrCloseBracket(
            CssTokens.TokenIterator it)
        {
            int bracketDepth = 0;
            while (it.MoveNext())
            {
                switch (it.Type)
                {
                    case CssTokens.TokenType.Semicolon:
                        it.MoveNext();
                        return;
                    case CssTokens.TokenType.LeftCurly:
                    case CssTokens.TokenType.LeftParen:
                    case CssTokens.TokenType.LeftSquare:
                        ++bracketDepth;
                        break;
                    case CssTokens.TokenType.RightCurly:
                    case CssTokens.TokenType.RightParen:
                    case CssTokens.TokenType.RightSquare:
                        --bracketDepth;
                        if (bracketDepth <= 0)
                        {
                            if (bracketDepth != 0) { it.MoveNext(); }
                            return;
                        }
                        break;
                }
            }
        }

        internal static void ParsePropertyGroup(String css, IPropertyHandler handler)
        {
            // Split tokens by semicolons/curly-braces, then by first colon,
            // dropping spaces and comments to identify property names and token runs
            // that form the value.

            CssTokens tokens = CssTokens.Lex(css);
            var it = (CssTokens.TokenIterator)tokens.GetEnumerator();
            it.MoveNext();
            while (it.HasTokenAfterSpace)
            {
                // Check that we have an identifier that might be a property name.
                if (it.Type != CssTokens.TokenType.Ident)
                {
                    ErrorRecoveryUntilSemiOrCloseBracket(it);
                    continue;
                }

                it.MoveNext();
                String name = it.Current;

                // Look for a colon.
                if (!(it.HasTokenAfterSpace && ":".Equals(it.Token)))
                {
                    ErrorRecoveryUntilSemiOrCloseBracket(it);
                    continue;
                }
                it.MoveNext();

                handler.StartProperty(name.ToLowerInvariant());
                ParsePropertyValue(it, handler);
                handler.EndProperty();
            }
        }

        private static void ParsePropertyValue(
            CssTokens.TokenIterator it, IPropertyHandler handler)
        {
            while (it.MoveNext())
            {
                CssTokens.TokenType type = it.Type;
                String token = it.Token;
                switch (type)
                {
                    case CssTokens.TokenType.Semicolon:
                        it.MoveNext();
                        return;
                    case CssTokens.TokenType.Function:
                        CssTokens.TokenIterator actuals = it.SpliceToEnd();
                        handler.StartFunction(token);
                        ParsePropertyValue(actuals, handler);
                        handler.EndFunction(token);
                        continue;  // Skip the advance over token.
                    case CssTokens.TokenType.Ident:
                        handler.Identifier(token);
                        break;
                    case CssTokens.TokenType.HashUnrestricted:
                        if (token.Length == 4 || token.Length == 7)
                        {
                            handler.Hash(token);
                        }
                        break;
                    case CssTokens.TokenType.String:
                        handler.QuotedString(token);
                        break;
                    case CssTokens.TokenType.Url:
                        handler.Url(token);
                        break;
                    case CssTokens.TokenType.Dimension:
                    case CssTokens.TokenType.Number:
                    case CssTokens.TokenType.Percentage:
                        handler.Quantity(token);
                        break;
                    case CssTokens.TokenType.At:
                    case CssTokens.TokenType.BadDimension:
                    case CssTokens.TokenType.Column:
                    case CssTokens.TokenType.DotIdent:
                    case CssTokens.TokenType.HashId:
                    case CssTokens.TokenType.Match:
                    case CssTokens.TokenType.UnicodeRange:
                    case CssTokens.TokenType.Whitespace:
                        break;
                    case CssTokens.TokenType.LeftCurly:
                    case CssTokens.TokenType.LeftParen:
                    case CssTokens.TokenType.LeftSquare:
                    case CssTokens.TokenType.RightCurly:
                    case CssTokens.TokenType.RightParen:
                    case CssTokens.TokenType.RightSquare:
                    case CssTokens.TokenType.Comma:
                    case CssTokens.TokenType.Colon:
                    case CssTokens.TokenType.Delim:
                        handler.Punctuation(token);
                        break;
                }
            }
        }

        /**
         * Decodes any escape sequences and strips any quotes from the input.
         */
        internal static String CssContent(String token)
        {
            int n = token.Length;
            int pos = 0;
            StringBuilder sb = null;
            if (n >= 2)
            {
                char ch0 = token[0];
                if (ch0 == '"' || ch0 == '\'')
                {
                    if (ch0 == token[n - 1])
                    {
                        pos = 1;
                        --n;
                        sb = new StringBuilder(n);
                    }
                }
            }
            for (int esc; (esc = token.IndexOf('\\', pos)) >= 0; )
            {
                int end = esc + 2;
                if (esc > n) { break; }
                if (sb == null) { sb = new StringBuilder(n); }
                sb.Append(token, pos, esc);
                int codepoint = token[end - 1];
                if (IsHex(codepoint))
                {
                    // Parse \hhhhh<opt-break> where hhhhh is one or more hex digits
                    // and <opt-break> is an optional space or tab character that can be
                    // used to separate an escape sequence from a following literal hex
                    // digit.
                    while (end < n && IsHex(token[end])) { ++end; }
                    try
                    {
                        codepoint = int.Parse(token.Substring(esc + 1, end), NumberStyles.HexNumber);
                    }
                    catch (Exception)
                    {
                        codepoint = 0xfffd;  // Unknown codepoint.
                    }
                    if (end < n)
                    {
                        char ch = token[end];
                        if (ch == ' ' || ch == '\t')
                        {  // Ignorable hex follower.
                            ++end;
                        }
                    }
                }
                sb.Append(Char.ConvertFromUtf32(codepoint));
                pos = end;
            }
            if (sb == null) { return token; }
            return sb.Append(token, pos, n).ToString();
        }

        private static bool IsHex(int codepoint)
        {
            return ('0' <= codepoint && codepoint <= '9')
                || ('A' <= codepoint && codepoint <= 'F')
                || ('a' <= codepoint && codepoint <= 'f');
        }

        internal interface IPropertyHandler
        {
            void StartProperty(String propertyName);
            void Quantity(String token);
            void Identifier(String token);
            void Hash(String token);
            void QuotedString(String token);
            void Url(String token);
            void Punctuation(String token);
            void StartFunction(String token);
            void EndFunction(String token);
            void EndProperty();
        }

    }

}
