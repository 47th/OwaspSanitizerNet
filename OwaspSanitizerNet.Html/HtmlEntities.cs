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

namespace OwaspSanitizerNet.Html
{
    /**
     * Utilities for decoding HTML entities, e.g., {@code &amp;}.
     */
    internal class HtmlEntities
    {
        private static int MAX_CODE_POINT = 0x10FFFF;


        /**
         * Decodes any HTML entity at the given location.  This handles both named and
         * numeric entities.
         *
         * @param html HTML text.
         * @param offset the position of the sequence to decode.
         * @param limit the last position in chars that could be part of the sequence
         *    to decode.
         * @return The offset after the end of the decoded sequence and the decoded
         *    code-point or code-unit packed into a long.
         *    The first 32 bits are the offset, and the second 32 bits are a
         *    code-point or a code-unit.
         */
        public static long decodeEntityAt(String html, int offset, int limit)
        {
            char ch = html[offset];
            if ('&' != ch)
            {
                return ((offset + 1L) << 32) | ch;
            }

            int entityLimit = Math.Min(limit, offset + 10);
            int end = -1;
            int tail = -1;
            if (entityLimit == limit)
            {
                // Assume a broken entity that ends at the end until shown otherwise.
                end = tail = entityLimit;
            }
            bool breakEntityLoop = false;
            for (int i = offset + 1; i < entityLimit && !breakEntityLoop; ++i)
            {
                switch (html[i])
                {
                    case ';':  // An unbroken entity.
                        end = i;
                        tail = end + 1;
                        breakEntityLoop = true;
                        break;
                    case '#':
                    case 'A': case 'B': case 'C': case 'D': case 'E': case 'F':
                    case 'G': case 'H': case 'I': case 'J': case 'K': case 'L':
                    case 'M': case 'N': case 'O': case 'P': case 'Q': case 'R':
                    case 'S': case 'T': case 'U': case 'V': case 'W': case 'X':
                    case 'Y': case 'Z':
                    case 'a': case 'b': case 'c': case 'd': case 'e': case 'f':
                    case 'g': case 'h': case 'i': case 'j': case 'k': case 'l':
                    case 'm': case 'n': case 'o': case 'p': case 'q': case 'r':
                    case 's': case 't': case 'u': case 'v': case 'w': case 'x':
                    case 'y': case 'z':
                    case '0': case '1': case '2': case '3': case '4': case '5':
                    case '6': case '7': case '8': case '9':
                        break;
                    case '=':
                        // An equal sign after an entity missing a closing semicolon should
                        // never have the semicolon inserted since that causes trouble with
                        // parameters in partially encoded URLs.
                        return ((offset + 1L) << 32) | '&';
                    default:  // A possible broken entity.
                        end = i;
                        tail = i;
                        breakEntityLoop = true;
                        break;
                }
            }
            if (end < 0 || offset + 2 >= end)
            {
                return ((offset + 1L) << 32) | '&';
            }
            // Now we know where the entity ends, and that there is at least one
            // character in the entity name
            char ch1 = html[offset + 1];
            char ch2 = html[offset + 2];
            int codepoint = -1;
            if ('#' == ch1)
            {
                // numeric entity
                if ('x' == ch2 || 'X' == ch2)
                {
                    if (end == offset + 3)
                    {  // No digits
                        return ((offset + 1L) << 32) | '&';
                    }
                    codepoint = 0;
                    // hex literal
                    bool breakDigLoop = false;
                    for (int i = offset + 3; i < end && !breakDigLoop; ++i)
                    {
                        char digit = html[i];
                        switch (digit & 0xfff8)
                        {
                            case 0x30:
                            case 0x38: // ASCII 48-57 are '0'-'9'
                                int decDig = digit & 0xf;
                                if (decDig < 10)
                                {
                                    codepoint = (codepoint << 4) | decDig;
                                }
                                else
                                {
                                    codepoint = -1;
                                    breakDigLoop = true;
                                }
                                break;
                            // ASCII 65-70 and 97-102 are 'A'-'Z' && 'a'-'z'
                            case 0x40:
                            case 0x60:
                                int hexDig = (digit & 0x7);
                                if (hexDig != 0 && hexDig < 7)
                                {
                                    codepoint = (codepoint << 4) | (hexDig + 9);
                                }
                                else
                                {
                                    codepoint = -1;
                                    breakDigLoop = true;
                                }
                                break;
                            default:
                                codepoint = -1;
                                breakDigLoop = true;
                                break;
                        }
                    }
                    if (codepoint > MAX_CODE_POINT)
                    {
                        codepoint = 0xfffd;  // Unknown.
                    }
                }
                else
                {
                    codepoint = 0;
                    // decimal literal
                    bool breakDigLoop = false;
                    for (int i = offset + 2; i < end && !breakDigLoop; ++i)
                    {
                        char digit = html[i];
                        switch (digit & 0xfff8)
                        {
                            case 0x30:
                            case 0x38: // ASCII 48-57 are '0'-'9'
                                int decDig = digit - '0';
                                if (decDig < 10)
                                {
                                    codepoint = (codepoint * 10) + decDig;
                                }
                                else
                                {
                                    codepoint = -1;
                                    breakDigLoop = true;
                                }
                                break;
                            default:
                                codepoint = -1;
                                breakDigLoop = true;
                                break;
                        }
                    }
                    if (codepoint > MAX_CODE_POINT)
                    {
                        codepoint = 0xfffd;  // Unknown.
                    }
                }
            }
            else
            {
                Trie t = ENTITY_TRIE;
                for (int i = offset + 1; i < end; ++i)
                {
                    char nameChar = html[i];
                    t = t.Lookup(nameChar);
                    if (t == null) { break; }
                }
                if (t == null)
                {
                    t = ENTITY_TRIE;
                    for (int i = offset + 1; i < end; ++i)
                    {
                        char nameChar = html[i];
                        if ('Z' >= nameChar && nameChar >= 'A') { nameChar = (char)(nameChar | 32); }
                        t = t.Lookup(nameChar);
                        if (t == null) { break; }
                    }
                }
                if (t != null && t.IsTerminal)
                {
                    codepoint = t.Value;
                }
            }
            if (codepoint < 0)
            {
                return ((offset + 1L) << 32) | '&';
            }
            return (((long)tail) << 32) | codepoint;
        }

        /** A trie that maps entity names to codepoints. */
        internal static readonly Trie ENTITY_TRIE = new Trie(
            new Dictionary<String, int>
      {
    // C0 Controls and Basic Latin
      {"quot", '"'},
      {"amp", '&'},
      {"lt", '<'},
      {"gt", '>'},

    // XML 1.0
      {"apos", '\''},

    // HTML4 entities
      {"nbsp", '\u00a0'},
      {"iexcl", '\u00a1'},
      {"cent", '\u00a2'},
      {"pound", '\u00a3'},
      {"curren", '\u00a4'},
      {"yen", '\u00a5'},
      {"brvbar", '\u00a6'},
      {"sect", '\u00a7'},
      {"uml", '\u00a8'},
      {"copy", '\u00a9'},
      {"ordf", '\u00aa'},
      {"laquo", '\u00ab'},
      {"not", '\u00ac'},
      {"shy", '\u00ad'},
      {"reg", '\u00ae'},
      {"macr", '\u00af'},
      {"deg", '\u00b0'},
      {"plusmn", '\u00b1'},
      {"sup2", '\u00b2'},
      {"sup3", '\u00b3'},
      {"acute", '\u00b4'},
      {"micro", '\u00b5'},
      {"para", '\u00b6'},
      {"middot", '\u00b7'},
      {"cedil", '\u00b8'},
      {"sup1", '\u00b9'},
      {"ordm", '\u00ba'},
      {"raquo", '\u00bb'},
      {"frac14", '\u00bc'},
      {"frac12", '\u00bd'},
      {"frac34", '\u00be'},
      {"iquest", '\u00bf'},
      {"Agrave", '\u00c0'},
      {"Aacute", '\u00c1'},
      {"Acirc", '\u00c2'},
      {"Atilde", '\u00c3'},
      {"Auml", '\u00c4'},
      {"Aring", '\u00c5'},
      {"AElig", '\u00c6'},
      {"Ccedil", '\u00c7'},
      {"Egrave", '\u00c8'},
      {"Eacute", '\u00c9'},
      {"Ecirc", '\u00ca'},
      {"Euml", '\u00cb'},
      {"Igrave", '\u00cc'},
      {"Iacute", '\u00cd'},
      {"Icirc", '\u00ce'},
      {"Iuml", '\u00cf'},
      {"ETH", '\u00d0'},
      {"Ntilde", '\u00d1'},
      {"Ograve", '\u00d2'},
      {"Oacute", '\u00d3'},
      {"Ocirc", '\u00d4'},
      {"Otilde", '\u00d5'},
      {"Ouml", '\u00d6'},
      {"times", '\u00d7'},
      {"Oslash", '\u00d8'},
      {"Ugrave", '\u00d9'},
      {"Uacute", '\u00da'},
      {"Ucirc", '\u00db'},
      {"Uuml", '\u00dc'},
      {"Yacute", '\u00dd'},
      {"THORN", '\u00de'},
      {"szlig", '\u00df'},
      {"agrave", '\u00e0'},
      {"aacute", '\u00e1'},
      {"acirc", '\u00e2'},
      {"atilde", '\u00e3'},
      {"auml", '\u00e4'},
      {"aring", '\u00e5'},
      {"aelig", '\u00e6'},
      {"ccedil", '\u00e7'},
      {"egrave", '\u00e8'},
      {"eacute", '\u00e9'},
      {"ecirc", '\u00ea'},
      {"euml", '\u00eb'},
      {"igrave", '\u00ec'},
      {"iacute", '\u00ed'},
      {"icirc", '\u00ee'},
      {"iuml", '\u00ef'},
      {"eth", '\u00f0'},
      {"ntilde", '\u00f1'},
      {"ograve", '\u00f2'},
      {"oacute", '\u00f3'},
      {"ocirc", '\u00f4'},
      {"otilde", '\u00f5'},
      {"ouml", '\u00f6'},
      {"divide", '\u00f7'},
      {"oslash", '\u00f8'},
      {"ugrave", '\u00f9'},
      {"uacute", '\u00fa'},
      {"ucirc", '\u00fb'},
      {"uuml", '\u00fc'},
      {"yacute", '\u00fd'},
      {"thorn", '\u00fe'},
      {"yuml", '\u00ff'},

    // Latin Extended-B
      {"fnof", '\u0192'},

    // Greek
      {"Alpha", '\u0391'},
      {"Beta", '\u0392'},
      {"Gamma", '\u0393'},
      {"Delta", '\u0394'},
      {"Epsilon", '\u0395'},
      {"Zeta", '\u0396'},
      {"Eta", '\u0397'},
      {"Theta", '\u0398'},
      {"Iota", '\u0399'},
      {"Kappa", '\u039a'},
      {"Lambda", '\u039b'},
      {"Mu", '\u039c'},
      {"Nu", '\u039d'},
      {"Xi", '\u039e'},
      {"Omicron", '\u039f'},
      {"Pi", '\u03a0'},
      {"Rho", '\u03a1'},
      {"Sigma", '\u03a3'},
      {"Tau", '\u03a4'},
      {"Upsilon", '\u03a5'},
      {"Phi", '\u03a6'},
      {"Chi", '\u03a7'},
      {"Psi", '\u03a8'},
      {"Omega", '\u03a9'},

      {"alpha", '\u03b1'},
      {"beta", '\u03b2'},
      {"gamma", '\u03b3'},
      {"delta", '\u03b4'},
      {"epsilon", '\u03b5'},
      {"zeta", '\u03b6'},
      {"eta", '\u03b7'},
      {"theta", '\u03b8'},
      {"iota", '\u03b9'},
      {"kappa", '\u03ba'},
      {"lambda", '\u03bb'},
      {"mu", '\u03bc'},
      {"nu", '\u03bd'},
      {"xi", '\u03be'},
      {"omicron", '\u03bf'},
      {"pi", '\u03c0'},
      {"rho", '\u03c1'},
      {"sigmaf", '\u03c2'},
      {"sigma", '\u03c3'},
      {"tau", '\u03c4'},
      {"upsilon", '\u03c5'},
      {"phi", '\u03c6'},
      {"chi", '\u03c7'},
      {"psi", '\u03c8'},
      {"omega", '\u03c9'},
      {"thetasym", '\u03d1'},
      {"upsih", '\u03d2'},
      {"piv", '\u03d6'},

    // General Punctuation
      {"bull", '\u2022'},
      {"hellip", '\u2026'},
      {"prime", '\u2032'},
      {"Prime", '\u2033'},
      {"oline", '\u203e'},
      {"frasl", '\u2044'},

    // Letterlike Symbols
      {"weierp", '\u2118'},
      {"image", '\u2111'},
      {"real", '\u211c'},
      {"trade", '\u2122'},
      {"alefsym", '\u2135'},

    // Arrows
      {"larr", '\u2190'},
      {"uarr", '\u2191'},
      {"rarr", '\u2192'},
      {"darr", '\u2193'},
      {"harr", '\u2194'},
      {"crarr", '\u21b5'},
      {"lArr", '\u21d0'},
      {"uArr", '\u21d1'},
      {"rArr", '\u21d2'},
      {"dArr", '\u21d3'},
      {"hArr", '\u21d4'},

    // Mathematical Operators
      {"forall", '\u2200'},
      {"part", '\u2202'},
      {"exist", '\u2203'},
      {"empty", '\u2205'},
      {"nabla", '\u2207'},
      {"isin", '\u2208'},
      {"notin", '\u2209'},
      {"ni", '\u220b'},
      {"prod", '\u220f'},
      {"sum", '\u2211'},
      {"minus", '\u2212'},
      {"lowast", '\u2217'},
      {"radic", '\u221a'},
      {"prop", '\u221d'},
      {"infin", '\u221e'},
      {"ang", '\u2220'},
      {"and", '\u2227'},
      {"or", '\u2228'},
      {"cap", '\u2229'},
      {"cup", '\u222a'},
      {"int", '\u222b'},
      {"there4", '\u2234'},
      {"sim", '\u223c'},
      {"cong", '\u2245'},
      {"asymp", '\u2248'},
      {"ne", '\u2260'},
      {"equiv", '\u2261'},
      {"le", '\u2264'},
      {"ge", '\u2265'},
      {"sub", '\u2282'},
      {"sup", '\u2283'},
      {"nsub", '\u2284'},
      {"sube", '\u2286'},
      {"supe", '\u2287'},
      {"oplus", '\u2295'},
      {"otimes", '\u2297'},
      {"perp", '\u22a5'},
      {"sdot", '\u22c5'},

    // Miscellaneous Technical
      {"lceil", '\u2308'},
      {"rceil", '\u2309'},
      {"lfloor", '\u230a'},
      {"rfloor", '\u230b'},
      {"lang", '\u2329'},
      {"rang", '\u232a'},

    // Geometric Shapes
      {"loz", '\u25ca'},

    // Miscellaneous Symbols
      {"spades", '\u2660'},
      {"clubs", '\u2663'},
      {"hearts", '\u2665'},
      {"diams", '\u2666'},

    // Latin Extended-A
      {"OElig", '\u0152'},
      {"oelig", '\u0153'},
      {"Scaron", '\u0160'},
      {"scaron", '\u0161'},
      {"Yuml", '\u0178'},

    // Spacing Modifier Letters
      {"circ", '\u02c6'},
      {"tilde", '\u02dc'},

    // General Punctuation
      {"ensp", '\u2002'},
      {"emsp", '\u2003'},
      {"thinsp", '\u2009'},
      {"zwnj", '\u200c'},
      {"zwj", '\u200d'},
      {"lrm", '\u200e'},
      {"rlm", '\u200f'},
      {"ndash", '\u2013'},
      {"mdash", '\u2014'},
      {"lsquo", '\u2018'},
      {"rsquo", '\u2019'},
      {"sbquo", '\u201a'},
      {"ldquo", '\u201c'},
      {"rdquo", '\u201d'},
      {"bdquo", '\u201e'},
      {"dagger", '\u2020'},
      {"Dagger", '\u2021'},
      {"permil", '\u2030'},
      {"lsaquo", '\u2039'},
      {"rsaquo", '\u203a'},
      {"euro", '\u20ac'},
      });

        private HtmlEntities() { /* uninstantiable */ }
    }

}
