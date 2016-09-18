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

namespace OwaspSanitizerNet.Html
{
    /**
    * Locale independent versions of String case-insensitive operations.
    * <p>
    * The normal case insensitive operators {@link String#toLowerCase}
    * and {@link String#equalsIgnoreCase} depend upon the current locale.
    * They will fold the letters "i" and "I" differently if the locale is
    * Turkish than if it is English.
    * <p>
    * These operations ignore all case folding for non-Roman letters, and are
    * independent of the current locale.
    * Lower-casing is exactly equivalent to {@code tr/A-Z/a-z/}, upper-casing to
    * {@code tr/a-z/A-Z/}, and case insensitive comparison is equivalent to
    * lower-casing both then comparing by code-unit.
    * <p>
    * Because of this simpler case folding, it is the case that for all Strings s
    * <code>
    * Strings.toUpperCase(s).equals(Strings.toUpperCase(Strings.toLowerCase(s)))
    * </code>.
    *
    * @author Mike Samuel (mikesamuel@gmail.com)
    */
    internal static class Strings 
    {
        private static readonly char[] LCaseChars = new char['Z' + 1];
        private static readonly char[] UCaseChars = new char['z' + 1];
        private static readonly long HtmlSpaceCharBitmask =
            (1L << ' ')
            | (1L << '\t')
            | (1L << '\n')
            | (1L << '\u000c')
            | (1L << '\r');

        static Strings() {
            for (int i = 0; i < 'A'; ++i) { LCaseChars[i] = (char) i; }
            for (int i = 'A'; i <= 'Z'; ++i) { LCaseChars[i] = (char) (i | 0x20); }
            for (int i = 0; i < 'a'; ++i) { UCaseChars[i] = (char) i; }
            for (int i = 'a'; i <= 'z'; ++i) { UCaseChars[i] = (char) (i & ~0x20); }
        }

        public static bool equalsIgnoreCase(
            string a, string b)
        {
            if (a == null) { return b == null; }
            if (b == null) { return false; }
            int length = a.Length;
            if (b.Length != length) { return false; }
            for (int i = length; --i >= 0;)
            {
                char c = a[i], d = b[i];
                if (c != d)
                {
                    if (c <= 'z' && c >= 'A') {
                    if (c <= 'Z') { c |= (char)0x20; }
                    if (d <= 'Z' && d >= 'A') { d |= (char)0x20; }
                    if (c == d) { continue; }
                    }
                    return false;
                }
            }
            return true;
        }

        public static bool regionMatchesIgnoreCase(
            string a, int aoffset, string b, int boffset, int n) {
            if (aoffset + n > a.Length || boffset + n > b.Length) { return false; }
            for (int i = n; --i >= 0;)
            {
                char c = a[aoffset + i], d = b[boffset + i];
                if (c != d)
                {
                    if (c <= 'z' && c >= 'A') {
                    if (c <= 'Z') { c |= (char)0x20; }
                    if (d <= 'Z' && d >= 'A') { d |= (char)0x20; }
                    if (c == d) { continue; }
                    }
                    return false;
                }
            }
            return true;
        }

        /** True iff {@code s.equals(String.toLowerCase(s))}. */
        public static bool isLowerCase(string s)
        {
            for (int i = s.Length; --i >= 0;)
            {
                char c = s[i];
                if (c <= 'Z' && c >= 'A')
                {
                    return false;
                }
            }
            return true;
        }

        public static string toLowerCase(string s)
        {
            for (int i = s.Length; --i >= 0;)
            {
                char c = s[i];
                if (c <= 'Z' && c >= 'A')
                {
                    char[] chars = s.ToCharArray();
                    chars[i] = LCaseChars[c];
                    while (--i >= 0)
                    {
                        c = chars[i];
                        if (c <= 'Z')
                        {
                            chars[i] = LCaseChars[c];
                        }
                    }
                    return new string(chars);
                }
            }
            return s;
        }

        public static string toUpperCase(string s)
        {
            for (int i = s.Length; --i >= 0;)
            {
                char c = s[i];
                if (c <= 'z' && c >= 'a')
                {
                    char[] chars = s.ToCharArray();
                    chars[i] = UCaseChars[c];
                    while (--i >= 0)
                    {
                        c = chars[i];
                        if (c <= 'z')
                        {
                            chars[i] = UCaseChars[c];
                        }
                    }
                    return new string(chars);
                }
            }
            return s;
        }

        public static bool isHtmlSpace(int ch)
        {
            return ch <= 0x20 && (HtmlSpaceCharBitmask & (1L << ch)) != 0;
        }

        public static bool containsHtmlSpace(string s)
        {
            for (int i = 0, n = s.Length; i < n; ++i)
            {
                if (isHtmlSpace(s[i])) { return true; }
            }
            return false;
        }

        public static string stripHtmlSpaces(string s)
        {
            int i = 0, n = s.Length;
            for (; n > i; --n)
            {
                if (!isHtmlSpace(s[n - 1]))
                {
                    break;
                }
            }
            for (; i < n; ++i)
            {
                if (!isHtmlSpace(s[i]))
                {
                    break;
                }
            }
            if (i == 0 && n == s.Length)
            {
                return s;
            }
            return s.Substring(i, n-i);
        }
    }
}