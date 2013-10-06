// Copyright (c) 2012, Mike Samuel
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
using System.Text;

namespace OwaspSanitizerNet.Html
{
    /** Encoders and decoders for HTML. */
    internal sealed class Encoding
    {

        /**
         * Decodes HTML entities to produce a string containing only valid
         * Unicode scalar values.
         */
        //@VisibleForTesting
        internal static String decodeHtml(String s)
        {
            int firstAmp = s.IndexOf('&');
            int safeLimit = longestPrefixOfGoodCodeunits(s);
            if ((firstAmp & safeLimit) < 0) { return s; }

            StringBuilder sb;
            {
                int n = s.Length;
                sb = new StringBuilder(n);
                int pos = 0;
                int amp = firstAmp;
                while (amp >= 0)
                {
                    long endAndCodepoint = HtmlEntities.decodeEntityAt(s, amp, n);
                    int end = (int)(((uint)endAndCodepoint) >> 32);
                    int codepoint = (int)endAndCodepoint;
                    sb.Append(s, pos, amp).Append(Char.ConvertFromUtf32(codepoint));
                    pos = end;
                    amp = s.IndexOf('&', end);
                }
                sb.Append(s, pos, n);
            }

            stripBannedCodeunits(
                sb,
                firstAmp < 0
                  ? safeLimit : safeLimit < 0
                  ? firstAmp : Math.Min(firstAmp, safeLimit));

            return sb.ToString();
        }

        /**
         * Returns the portion of its input that consists of XML safe chars.
         * @see <a href="http://www.w3.org/TR/2008/REC-xml-20081126/#charsets">XML Ch. 2.2 - Characters</a>
         */
        [TCB]
        internal static String stripBannedCodeunits(String s)
        {
            int safeLimit = longestPrefixOfGoodCodeunits(s);
            if (safeLimit < 0) { return s; }

            StringBuilder sb = new StringBuilder(s);
            stripBannedCodeunits(sb, safeLimit);
            return sb.ToString();
        }

        /**
         * Leaves in the input buffer only code-units that comprise XML safe chars.
         * @see <a href="http://www.w3.org/TR/2008/REC-xml-20081126/#charsets">XML Ch. 2.2 - Characters</a>
         */
        [TCB]
        internal static void stripBannedCodeunits(StringBuilder sb)
        {
            stripBannedCodeunits(sb, 0);
        }

        [TCB]
        private static void stripBannedCodeunits(StringBuilder sb, int start)
        {
            int k = start;
            for (int i = start, n = sb.Length; i < n; ++i)
            {
                char ch = sb[i];
                if (ch < 0x20)
                {
                    if (IS_BANNED_ASCII[ch])
                    {
                        continue;
                    }
                }
                else if (0xd800 <= ch)
                {
                    if (ch <= 0xdfff)
                    {
                        if (i + 1 < n)
                        {
                            char next = sb[i + 1];
                            if (Char.IsSurrogatePair(ch, next))
                            {
                                sb[k++] = ch;
                                sb[k++] = next;
                                ++i;
                            }
                        }
                        continue;
                    }
                    else if ((ch & 0xfffe) == 0xfffe)
                    {
                        continue;
                    }
                }
                sb[k++] = ch;
            }
            sb.Length = k;
        }

        /**
         * The number of code-units at the front of s that form code-points in the
         * XML Character production.
         * @return -1 if all of s is in the XML Character production.
         */
        [TCB]
        private static int longestPrefixOfGoodCodeunits(String s)
        {
            int n = s.Length, i;
            for (i = 0; i < n; ++i)
            {
                char ch = s[i];
                if (ch < 0x20)
                {
                    if (IS_BANNED_ASCII[ch])
                    {
                        return i;
                    }
                }
                else if (0xd800 <= ch)
                {
                    if (ch <= 0xdfff)
                    {
                        if (i + 1 < n && Char.IsSurrogatePair(ch, s[i + 1]))
                        {
                            ++i;  // Skip over low surrogate since we know it's ok.
                        }
                        else
                        {
                            return i;
                        }
                    }
                    else if ((ch & 0xfffe) == 0xfffe)
                    {
                        return i;
                    }
                }
            }
            return -1;
        }

        /**
         * Writes the HTML equivalent of the given plain text to output.
         * For example, {@code escapeHtmlOnto("1 < 2", w)},
         * is equivalent to {@code w.append("1 &lt; 2")} but possibly with fewer
         * smaller appends.
         * Elides code-units that are not valid XML Characters.
         * @see <a href="http://www.w3.org/TR/2008/REC-xml-20081126/#charsets">XML Ch. 2.2 - Characters</a>
         */
        [TCB]
        internal static void encodeHtmlOnto(String plainText, StringBuilder output)
        {
            int n = plainText.Length;
            int pos = 0;
            for (int i = 0; i < n; ++i)
            {
                char ch = plainText[i];
                if (ch < REPLACEMENTS.Length)
                {
                    String repl = REPLACEMENTS[ch];
                    if (repl != null)
                    {
                        output.Append(plainText, pos, i).Append(repl);
                        pos = i + 1;
                    }
                }
                else if (((char)0xd800) <= ch)
                {
                    if (ch <= ((char)0xdfff))
                    {
                        char next;
                        if (i + 1 < n
                            && Char.IsSurrogatePair(
                                ch, next = plainText[i + 1]))
                        {
                            // Emit supplemental codepoints as entity so that they cannot
                            // be mis-encoded as UTF-8 of surrogates instead of UTF-8 proper
                            // and get involved in UTF-16/UCS-2 confusion.
                            int codepoint = Char.ConvertToUtf32(ch, next);//Char.toCodePoint(ch, next);
                            output.Append(plainText, pos, i);
                            appendNumericEntity(codepoint, output);
                            ++i;
                            pos = i + 1;
                        }
                        else
                        {
                            output.Append(plainText, pos, i);
                            // Elide the orphaned surrogate.
                            pos = i + 1;
                        }
                    }
                    else if (0xff00 <= ch)
                    {
                        output.Append(plainText, pos, i);
                        pos = i + 1;
                        // Is a control character or possible full-width version of a
                        // special character.
                        if ((ch & 0xfffe) == 0xfffe)
                        {
                            // Elide since not an the XML Character.
                        }
                        else
                        {
                            appendNumericEntity(ch, output);
                        }
                    }
                }
            }
            output.Append(plainText, pos, n);
        }

        [TCB]
        internal static void appendNumericEntity(int codepoint, StringBuilder output)
        {
            if (codepoint < 100)
            {
                // TODO: is this dead code due to REPLACEMENTS above.
                output.Append("&#");
                if (codepoint < 10)
                {
                    output.Append((char)('0' + codepoint));
                }
                else
                {
                    output.Append((char)('0' + (codepoint / 10)));
                    output.Append((char)('0' + (codepoint % 10)));
                }
                output.Append(";");
            }
            else
            {
                int nDigits = (codepoint < 0x1000
                               ? codepoint < 0x100 ? 2 : 3
                               : (codepoint < 0x10000 ? 4
                                  : codepoint < 0x100000 ? 5 : 6));
                output.Append("&#x");
                for (int digit = nDigits; --digit >= 0; )
                {
                    int hexDigit = (int)(((uint)codepoint >> (digit << 2))) & 0xf;
                    output.Append(HEX_NUMERAL[hexDigit]);
                }
                output.Append(";");
            }
        }

        private static readonly char[] HEX_NUMERAL = {
   '0', '1', '2', '3', '4', '5', '6', '7',
   '8', '9', 'a', 'b', 'c', 'd', 'e', 'f',
  };

        /** Maps ASCII chars that need to be encoded to an equivalent HTML entity. */
        private static readonly String[] REPLACEMENTS = new String[0x61];
        private static bool[] IS_BANNED_ASCII = new bool[0x20];
        static Encoding()
        {
            for (int i = 0; i < ' '; ++i)
            {
                // We elide control characters so that we can ensure that our output is
                // in the intersection of valid HTML5 and XML.  According to
                // http://www.w3.org/TR/2008/REC-xml-20081126/#charsets
                // Char      ::=          #x9 | #xA | #xD | [#x20-#xD7FF]
                //             |          [#xE000-#xFFFD] | [#x10000-#x10FFFF]
                if (i != '\t' && i != '\n' && i != '\r')
                {
                    REPLACEMENTS[i] = "";  // Elide
                }
            }
            // "&#34;" is shorter than "&quot;"
            REPLACEMENTS['"'] = "&#" + ((int)'"') + ";";  // Attribute delimiter.
            REPLACEMENTS['&'] = "&amp;";                    // HTML special.
            // We don't use &apos; since that is not in the intersection of HTML&XML.
            REPLACEMENTS['\''] = "&#" + ((int)'\'') + ";";  // Attribute delimiter.
            REPLACEMENTS['+'] = "&#" + ((int)'+') + ";";  // UTF-7 special.
            REPLACEMENTS['<'] = "&lt;";                     // HTML special.
            REPLACEMENTS['='] = "&#" + ((int)'=') + ";";  // Special in attributes.
            REPLACEMENTS['>'] = "&gt;";                     // HTML special.
            REPLACEMENTS['@'] = "&#" + ((int)'@') + ";";  // Conditional compilation.
            REPLACEMENTS['`'] = "&#" + ((int)'`') + ";";  // Attribute delimiter.

            // @code DECODES_TO_SELF[c]} is true iff the codepoint c decodes to itself in
            // n HTML5 text node or properly quoted attribute value.
            for (int i = 0; i < IS_BANNED_ASCII.Length; ++i)
            {
                IS_BANNED_ASCII[i] = !(i == '\t' || i == '\n' || i == '\r');
            }
        }

    }
}
