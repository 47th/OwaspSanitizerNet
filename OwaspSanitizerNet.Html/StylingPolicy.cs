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
using System.Text;

namespace OwaspSanitizerNet.Html
{
    /**
     * An HTML sanitizer policy that tries to preserve simple CSS by white-listing
     * property values and splitting combo properties into multiple more specific
     * ones to reduce the attack-surface.
     */
    [TCB]
    internal sealed class StylingPolicy : IAttributePolicy
    {
        private readonly CssSchema _cssSchema;

        StylingPolicy(CssSchema cssSchema)
        {
            _cssSchema = cssSchema;
        }

        public String Apply(
            String elementName, String attributeName, String value)
        {
            return value != null ? SanitizeCssProperties(value) : null;
        }

        /**
         * Lossy filtering of CSS properties that allows textual styling that affects
         * layout, but does not allow breaking out of a clipping region, absolute
         * positioning, image loading, tab index changes, or code execution.
         *
         * @return A sanitized version of the input.
         */
        //@VisibleForTesting
        internal String SanitizeCssProperties(String style)
        {
            var sanitizedCss = new StringBuilder();
            CssGrammar.ParsePropertyGroup(style, new CssSanitizer(_cssSchema, sanitizedCss));
            return sanitizedCss.Length == 0 ? null : sanitizedCss.ToString();
        }

        private class CssSanitizer : CssGrammar.IPropertyHandler
        {
            private readonly CssSchema _cssSchema;
            private readonly StringBuilder _sanitizedCss;
            CssSchema.Property _cssProperty = CssSchema.Disallowed;
            List<CssSchema.Property> _cssProperties;
            private int _propertyStart;
            private bool _hasTokens;
            private bool _inQuotedIdents;

            public CssSanitizer(CssSchema cssSchema, StringBuilder sanitizedCss)
            {
                _cssSchema = cssSchema;
                _sanitizedCss = sanitizedCss;
            }

            private void EmitToken(String token)
            {
                CloseQuotedIdents();
                if (_hasTokens) { _sanitizedCss.Append(' '); }
                _sanitizedCss.Append(token);
                _hasTokens = true;
            }

            private void CloseQuotedIdents()
            {
                if (_inQuotedIdents)
                {
                    _sanitizedCss.Append('\'');
                    _inQuotedIdents = false;
                }
            }

            public void Url(String token)
            {
                CloseQuotedIdents();
                //if ((schema.bits & CssSchema.BIT_URL) != 0) {
                // TODO: sanitize the URL.
                //}
            }

            public void StartProperty(String propertyName)
            {
                if (_cssProperties != null) { _cssProperties.Clear(); }
                _cssProperty = _cssSchema.ForKey(propertyName);
                _hasTokens = false;
                _propertyStart = _sanitizedCss.Length;
                if (_sanitizedCss.Length != 0)
                {
                    _sanitizedCss.Append(';');
                }
                _sanitizedCss.Append(propertyName).Append(':');
            }

            public void StartFunction(String token)
            {
                CloseQuotedIdents();
                if (_cssProperties == null) { _cssProperties = new List<CssSchema.Property>(); }
                _cssProperties.Add(_cssProperty);
                token = token.ToLowerInvariant();
                String key;
                _cssProperty = CssSchema.Disallowed;
                if (_cssProperty.FnKeys.TryGetValue(token, out key))
                {
                    _cssProperty = _cssSchema.ForKey(key);
                }
                if (_cssProperty != CssSchema.Disallowed)
                {
                    EmitToken(token);
                }
            }

            public void QuotedString(String token)
            {
                CloseQuotedIdents();
                // The contents of a quoted string could be treated as
                // 1. a run of space-separated words, as in a font family name,
                // 2. as a URL,
                // 3. as plain text content as in a list-item bullet,
                // 4. or it could be ambiguous as when multiple bits are set.
                int meaning =
                    _cssProperty.Bits
                    & (CssSchema.BitUnreservedWord | CssSchema.BitUrl);
                if ((meaning & (meaning - 1)) == 0)
                {  // meaning is unambiguous
                    if (meaning == CssSchema.BitUnreservedWord
                        && token.Length > 2
                        && IsAlphanumericOrSpace(token, 1, token.Length - 1))
                    {
                        EmitToken(token.ToLowerInvariant());
                    }
                    else if (meaning == CssSchema.BitUrl)
                    {
                        // convert to a URL token and hand-off to the appropriate method
                        // url("url(" + token + ")");  // TODO: %-encode properly
                    }
                }
            }

            private static bool IsAlphanumericOrSpace(
                String token, int start, int end)
            {
                for (int i = start; i < end; ++i)
                {
                    char ch = token[i];
                    if (ch <= 0x20)
                    {
                        if (ch != '\t' && ch != ' ')
                        {
                            return false;
                        }
                    }
                    else
                    {
                        int chLower = ch | 32;
                        if (!(('0' <= chLower && chLower <= '9')
                              || ('a' <= chLower && chLower <= 'z')))
                        {
                            return false;
                        }
                    }
                }
                return true;
            }


            public void Quantity(String token)
            {
                int test = token.StartsWith("-")
                    ? CssSchema.BitNegative : CssSchema.BitQuantity;
                if ((_cssProperty.Bits & test) != 0
                    // font-weight uses 100, 200, 300, etc.
                    || _cssProperty.Literals.Contains(token))
                {
                    EmitToken(token);
                }
            }

            public void Punctuation(String token)
            {
                CloseQuotedIdents();
                if (_cssProperty.Literals.Contains(token))
                {
                    EmitToken(token);
                }
            }

            private const int IdentToString = CssSchema.BitUnreservedWord | CssSchema.BitString;

            public void Identifier(String token)
            {
                token = token.ToLowerInvariant();
                if (_cssProperty.Literals.Contains(token))
                {
                    EmitToken(token);
                }
                else if ((_cssProperty.Bits & IdentToString) == IdentToString)
                {
                    if (!_inQuotedIdents)
                    {
                        _inQuotedIdents = true;
                        if (_hasTokens) { _sanitizedCss.Append(' '); }
                        _sanitizedCss.Append('\'');
                        _hasTokens = true;
                    }
                    else
                    {
                        _sanitizedCss.Append(' ');
                    }
                    _sanitizedCss.Append(token.ToLowerInvariant());
                }
            }

            public void Hash(String token)
            {
                CloseQuotedIdents();
                if ((_cssProperty.Bits & CssSchema.BitHashValue) != 0)
                {
                    EmitToken(token.ToLowerInvariant());
                }
            }

            public void EndProperty()
            {
                if (!_hasTokens)
                {
                    _sanitizedCss.Length = _propertyStart;
                }
                else
                {
                    CloseQuotedIdents();
                }
            }

            public void EndFunction(String token)
            {
                if (_cssProperty != CssSchema.Disallowed) { EmitToken(")"); }
                _cssProperty = _cssProperties[_cssProperties.Count - 1];
                _cssProperties.RemoveAt(_cssProperties.Count - 1);
            }
        }
    }

}
