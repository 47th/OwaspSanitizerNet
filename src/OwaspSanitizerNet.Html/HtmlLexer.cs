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
    * A flexible lexer for HTML.
    * This is hairy code, but it is outside the TCB for the HTML sanitizer.
    *
    * @author Mike Samuel (mikesamuel@gmail.com)
    */
    internal sealed class HtmlLexer //: AbstractTokenStream 
    {
        private readonly string input;
        //private readonly HtmlInputSplitter splitter;
        //private State state = State.OUTSIDE_TAG;

        public HtmlLexer(string input) {
            this.input = input;
            //this.splitter = new HtmlInputSplitter(input);
        }

        /**
        * Normalize case of names that are not name-spaced.  This lower-cases HTML
        * element and attribute names, but not ones for embedded SVG or MATHML.
        */
        public static string canonicalName(string elementOrAttribName) {
            return elementOrAttribName.IndexOf(':') >= 0
                ? elementOrAttribName : Strings.toLowerCase(elementOrAttribName);
        }
    }
}