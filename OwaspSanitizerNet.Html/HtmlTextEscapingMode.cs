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
     * From section 8.1.2.6 of http://www.whatwg.org/specs/web-apps/current-work/
     * <p>
     * The text in CDATA and RCDATA elements must not contain any
     * occurrences of the string "</" (U+003C LESS-THAN SIGN, U+002F
     * SOLIDUS) followed by characters that case-insensitively match the
     * tag name of the element followed by one of U+0009 CHARACTER
     * TABULATION, U+000A LINE FEED (LF), U+000B LINE TABULATION, U+000C
     * FORM FEED (FF), U+0020 SPACE, U+003E GREATER-THAN SIGN (>), or
     * U+002F SOLIDUS (/), unless that string is part of an escaping
     * text span.
     * </p>
     *
     * <p>
     * See also
     * http://www.whatwg.org/specs/web-apps/current-work/#cdata-rcdata-restrictions
     * for the elements which fall in each category.
     * </p>
     *
     * @author Mike Samuel <mikesamuel@gmail.com>
     */

    internal enum HtmlTextEscapingMode
    {
        /**
         * Normally escaped character data that breaks around comments and tags.
         */
        PCDATA,
        /**
         * A span of text where HTML special characters are interpreted literally,
         * as in a SCRIPT tag.
         */
        CDATA,
        /**
         * Like {@link #CDATA} but only for certain browsers.
         */
        CDATA_SOMETIMES,
        /**
         * A span of text and character entity references where HTML special
         * characters are interpreted literally, as in a TITLE tag.
         */
        RCDATA,
        /**
         * A span of text where HTML special characters are interpreted literally,
         * where there is no end tag.  PLAIN_TEXT runs until the end of the file.
         */
        PLAIN_TEXT,

        /**
         * Cannot contain data.
         */
        VOID,
    }

    internal static class HtmlTextEscapingModeExtensions
    {

        private static readonly Dictionary<String, HtmlTextEscapingMode> ESCAPING_MODES
          = new Dictionary<String, HtmlTextEscapingMode>
          {
              {"iframe",HtmlTextEscapingMode.CDATA},
              // HTML5 does not treat listing as CDATA and treats XMP as deprecated,
              // but HTML2 does at
              // http://www.w3.org/MarkUp/1995-archive/NonStandard.html
              // Listing is not supported by browsers.
              {"listing",HtmlTextEscapingMode.CDATA_SOMETIMES},
              {"xmp",HtmlTextEscapingMode.CDATA},

              // Technically, noembed, noscript and noframes are CDATA_SOMETIMES but
              // we can only be hurt by allowing tag content that looks like text so
              // we treat them as regular..
              //{"noembed",HtmlTextEscapingMode.CDATA_SOMETIMES},
              //{"noframes",HtmlTextEscapingMode.CDATA_SOMETIMES},
              //{"noscript",HtmlTextEscapingMode.CDATA_SOMETIMES},
              {"comment",HtmlTextEscapingMode.CDATA_SOMETIMES},  // IE only

              // Runs till end of file.
              {"plaintext",HtmlTextEscapingMode.PLAIN_TEXT},

              {"script",HtmlTextEscapingMode.CDATA},
              {"style",HtmlTextEscapingMode.CDATA},

              // Textarea and Title are RCDATA, not CDATA, so decode entity references.
              {"textarea",HtmlTextEscapingMode.RCDATA},
              {"title",HtmlTextEscapingMode.RCDATA},

              // Nodes that can't contain content.
              // http://www.w3.org/TR/html-markup/syntax.html#void-elements
              {"area",HtmlTextEscapingMode.VOID},
              {"base",HtmlTextEscapingMode.VOID},
              {"br",HtmlTextEscapingMode.VOID},
              {"col",HtmlTextEscapingMode.VOID},
              {"command",HtmlTextEscapingMode.VOID},
              {"embed",HtmlTextEscapingMode.VOID},
              {"hr",HtmlTextEscapingMode.VOID},
              {"img",HtmlTextEscapingMode.VOID},
              {"input",HtmlTextEscapingMode.VOID},
              {"keygen",HtmlTextEscapingMode.VOID},
              {"link",HtmlTextEscapingMode.VOID},
              {"meta",HtmlTextEscapingMode.VOID},
              {"param",HtmlTextEscapingMode.VOID},
              {"source",HtmlTextEscapingMode.VOID},
              {"track",HtmlTextEscapingMode.VOID},
              {"wbr",HtmlTextEscapingMode.VOID},
          };


        /**
         * The mode used for content following a start tag with the given name.
         */
        public static HtmlTextEscapingMode getModeForTag(String canonTagName)
        {
            HtmlTextEscapingMode mode;
            if (ESCAPING_MODES.TryGetValue(canonTagName, out mode))
            {
                return mode;
            }
            return HtmlTextEscapingMode.PCDATA;
        }

        /**
         * True iff the content following the given tag allows escaping text
         * spans: {@code <!--&hellip;-->} that escape even things that might
         * be an end tag for the corresponding open tag.
         */
        public static bool allowsEscapingTextSpan(String canonTagName)
        {
            // <xmp> and <plaintext> do not admit escaping text spans.
            return "style".Equals(canonTagName) || "script".Equals(canonTagName)
              || "noembed".Equals(canonTagName) || "noscript".Equals(canonTagName)
              || "noframes".Equals(canonTagName);
        }

        /**
         * True if content immediately following the start tag must be treated as
         * special CDATA so that &lt;'s are not treated as starting tags, comments
         * or directives.
         */
        public static bool isTagFollowedByLiteralContent(String canonTagName)
        {
            HtmlTextEscapingMode mode = getModeForTag(canonTagName);
            return mode != HtmlTextEscapingMode.PCDATA && mode != HtmlTextEscapingMode.VOID;
        }

        /**
         * True iff the tag cannot contain any content -- will an HTML parser consider
         * the element to have ended immediately after the start tag.
         */
        public static bool isVoidElement(String canonTagName)
        {
            return getModeForTag(canonTagName) == HtmlTextEscapingMode.VOID;
        }
    }

}
