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
    * Wraps an HTML stream event receiver to fill in missing close tags.
    * If the balancer is given the HTML {@code <p>1<p>2}, the wrapped receiver will
    * see events equivalent to {@code <p>1</p><p>2</p>}.
    *
    * @author Mike Samuel (mikesamuel@gmail.com)
    */
    [TCB]
    public class TagBalancingHtmlStreamEventReceiver
        : HtmlStreamEventReceiver
    {
        private readonly HtmlStreamEventReceiver _underlying;
        private int _nestingLimit = int.MaxValue;
        private readonly List<ElementContainmentInfo> _openElements
            = new List<ElementContainmentInfo>();
        private readonly Queue<ElementContainmentInfo> _toResumeInReverse
            = new Queue<ElementContainmentInfo>();


        /**
        * @param underlying An event receiver that should receive a stream of
        *     balanced events that is as close as possible to the stream of events
        *     received by this.
        */
        public TagBalancingHtmlStreamEventReceiver(
            HtmlStreamEventReceiver underlying)
        {
            _underlying = underlying;
        }

        /**
        * Set the maximum element nesting depth.
        */
        public void setNestingLimit(int limit)
        {
            if (_openElements.Count > limit)
            {
                throw new InvalidOperationException();
            }
            _nestingLimit = limit;
        }

        public void openDocument()
        {
            _underlying.openDocument();
        }

        public void closeDocument()
        {
            for (int i = Math.Min(_nestingLimit, _openElements.Count); --i >= 0;)
            {
                _underlying.closeTag(_openElements[i].ElementName);
            }
            _openElements.Clear();
            _toResumeInReverse.Clear();
            _underlying.closeDocument();
        }

        public void openTag(String elementName, List<String> attrs)
        {
            String canonElementName = HtmlLexer.canonicalName(elementName);
            ElementContainmentInfo elInfo;
            // Treat unrecognized tags as void, but emit closing tags in closeTag().
            if (!ELEMENT_CONTAINMENT_RELATIONSHIPS.TryGetValue(canonElementName, out elInfo))
            {
                if (_openElements.Count < _nestingLimit)
                {
                    _underlying.openTag(elementName, attrs);
                }
                return;
            }

            prepareForContent(elInfo);

            if (_openElements.Count < _nestingLimit)
            {
                _underlying.openTag(elInfo.ElementName, attrs);
            }
            if (!elInfo.IsVoid)
            {
                _openElements.Add(elInfo);
            }
        }

        private void prepareForContent(ElementContainmentInfo elInfo)
        {
            int nOpen = _openElements.Count;
            if (nOpen != 0)
            {
                ElementContainmentInfo top = _openElements[nOpen - 1];
                if ((top.Contents & elInfo.Types) == 0)
                {
                    ElementContainmentInfo blockContainerChild = top.BlockContainerChild;
                    // Open implied elements, such as list-items and table cells & rows.
                    if (blockContainerChild != null
                        && (blockContainerChild.Contents & elInfo.Types) != 0)
                    {
                        _underlying.openTag(
                            blockContainerChild.ElementName, new List<string>());
                        _openElements.Add(blockContainerChild);
                        top = blockContainerChild;
                        ++nOpen;
                    }
                }

                // Close all the elements that cannot contain the element to open.
                while (true)
                {
                    if (canContain(elInfo, top, nOpen - 1))
                    {
                        break;
                    }
                    if (_openElements.Count < _nestingLimit)
                    {
                        _underlying.closeTag(top.ElementName);
                    }
                    _openElements.RemoveAt(--nOpen);
                    if (top.Resumable)
                    {
                        _toResumeInReverse.Enqueue(top);
                    }
                    if (nOpen == 0) { break; }
                    top = _openElements[nOpen - 1];
                }
            }

            while (_toResumeInReverse.Count != 0)
            {
                ElementContainmentInfo toResume = _toResumeInReverse.Peek();
                // If toResume can contain elInfo AND the top of the stack can contain
                // toResume, then we push toResume.
                nOpen = _openElements.Count;
                if ((nOpen == 0
                    || canContain(toResume, _openElements[nOpen - 1], nOpen))
                    && canContain(elInfo, toResume, nOpen))
                {
                    _toResumeInReverse.Dequeue();
                    if (_openElements.Count < _nestingLimit)
                    {
                        _underlying.openTag(toResume.ElementName, new List<string>());
                    }
                    _openElements.Add(toResume);
                }
                else
                {
                    break;
                }
            }
        }

        /**
        * Takes into account transparency when figuring out what
        * can be contained.
        */
        private bool canContain(
            ElementContainmentInfo child, ElementContainmentInfo top, int topIndex)
        {
            int childTypes = child.Types;

            int contents = top.Contents;
            // Compute which content groups show through based on transparency.
            // We only care about the bits in childTypes which have not been found
            // in the current element which allows us to prune the search.
            int transparencyAllowed = childTypes
                & (top.TransparentToContents & ~contents);
            for (int containerIndex = topIndex - 1; transparencyAllowed != 0;
                --containerIndex)
            {
                if (containerIndex < 0)
                {
                    // When the element stack is empty, we don't check containment.
                    // This is effectively assuming, by omission, that any element can
                    // appear at the root of the document fragment.

                    // Allow transparent elements to contain any content that could be
                    // contained by any container of the document fragment.
                    // Revisit this decision if we start constraining what can be top-level.
                    contents |= transparencyAllowed;
                    break;
                }
                ElementContainmentInfo container = _openElements[containerIndex];
                contents |= transparencyAllowed & container.Contents;
                transparencyAllowed =
                    transparencyAllowed & container.TransparentToContents & ~contents;
            }
            return (contents & childTypes) != 0;
        }

        public void closeTag(String elementName)
        {
            String canonElementName = HtmlLexer.canonicalName(elementName);
            ElementContainmentInfo elInfo;
            if (!ELEMENT_CONTAINMENT_RELATIONSHIPS.TryGetValue(canonElementName, out elInfo))
            {  // Allow unrecognized end tags through.
                if (_openElements.Count < _nestingLimit)
                {
                    _underlying.closeTag(elementName);
                }
                return;
            }
            int index = _openElements.LastIndexOf(elInfo);
            // Let any of </h1>, </h2>, ... close other header tags.
            if (isHeaderElementName(canonElementName))
            {
                for (int i = _openElements.Count, limit = index + 1; -- i >= limit;)
                {
                    ElementContainmentInfo openEl = _openElements[i];
                    if (isHeaderElementName(openEl.ElementName))
                    {
                        elInfo = openEl;
                        index = i;
                        canonElementName = openEl.ElementName;
                        break;
                    }
                }
            }
            if (index < 0)
            {
                return;  // Don't close unopened tags.
            }

            // Ensure that index is in the scope of closeable elements.
            // This approximates the "has an element in *** scope" predicates defined at
            // http://www.whatwg.org/specs/web-apps/current-work/multipage/parsing.html
            // #has-an-element-in-the-specific-scope
            int blockingScopes = elInfo.BlockedByScopes;

            for (int i = _openElements.Count; --i > index;)
            {
                ElementContainmentInfo openElementInfo = _openElements[i];
                if ((openElementInfo.InScopes & blockingScopes) != 0)
                {
                    return;
                }
            }

            int last = _openElements.Count;
            // Close all the elements that cannot contain the element to open.
            while (--last > index)
            {
                ElementContainmentInfo unclosed = _openElements[last];
                _openElements.RemoveAt(last);
                if (last + 1 < _nestingLimit)
                {
                    _underlying.closeTag(unclosed.ElementName);
                }
                if (unclosed.Resumable)
                {
                    _toResumeInReverse.Enqueue(unclosed);
                }
            }
            if (_openElements.Count < _nestingLimit)
            {
                _underlying.closeTag(elInfo.ElementName);
            }
            _openElements.RemoveAt(index);
        }

        /**
        * True if text is the value of an inter-element whitespace text node as
        * defined by HTML5.
        * <p>
        * This is the kind of text that is often inserted by
        * HTML authors to nicely indent their HTML documents and which
        * (modulo unconventional use of {@code white-space:pre}) are not apparent
        * to the end-user.
        */
        public static bool isInterElementWhitespace(string text)
        {
            int n = text.Length;
            for (int i = 0; i < n; ++i)
            {
                if (!Strings.isHtmlSpace(text[i]))
                {
                    return false;
                }
            }
            return true;
        }

        public void text(string text)
        {
            if (!isInterElementWhitespace(text))
            {
                prepareForContent(ElementContainmentRelationships.CHARACTER_DATA_ONLY);
            }

            if (_openElements.Count < _nestingLimit)
            {
                _underlying.text(text);
            }
        }

        private static bool isHeaderElementName(String canonElementName)
        {
            return canonElementName.Length == 2 && canonElementName[0] == 'h'
                && canonElementName[1] <= '9';
        }

        private sealed class ElementContainmentInfo
        {
            public readonly String ElementName;
            /**
            * True if the adoption agency algorithm allows an element to be resumed
            * after a mis-nested end tag closes it.
            * E.g. in {@code <b>Foo<i>Bar</b>Baz</i>} the {@code <i>} element is
            * resumed after the {@code <b>} element is closed.
            */
            public readonly bool Resumable;
            /** A set of bits of element groups into which the element falls. */
            public readonly int Types;
            /** The type of elements that an element can contain. */
            public readonly int Contents;
            /**
            * A bit set of the elements that an element can contain when those can
            * be contained in its parent element.
            */
            public readonly int TransparentToContents;
            /** True if the element has no content -- not even text content. */
            public readonly bool IsVoid;
            /** A legal child of this node that can contain block content. */
            public readonly ElementContainmentInfo BlockContainerChild;
            /** A bit set of close tag scopes that block this element's close tags. */
            public readonly int BlockedByScopes;
            /** A bit set of scopes groups into which this element falls. */
            public readonly int InScopes;

            public ElementContainmentInfo(
                string elementName, bool resumable, int types, int contents,
                int transparentToContents,
                ElementContainmentInfo blockContainerChild,
                int inScopes)
            {
                ElementName = elementName;
                Resumable = resumable;
                Types = types;
                Contents = contents;
                TransparentToContents = transparentToContents;
                IsVoid = contents == 0
                    && HtmlTextEscapingModeHelper.isVoidElement(elementName);
                BlockContainerChild = blockContainerChild;
                BlockedByScopes =
                    (int)ElementContainmentRelationships.CloseTagScope.ALL & ~inScopes;
                InScopes = inScopes;
            }

            public override string ToString()
            {
                return "<" + ElementName + ">";
            }
        }

        private static readonly Dictionary<string, ElementContainmentInfo>
            ELEMENT_CONTAINMENT_RELATIONSHIPS
            = ElementContainmentRelationships.make().toMap();

        private sealed class ElementContainmentRelationships
        {
            private enum ElementGroup
            {
                BLOCK,
                INLINE,
                INLINE_MINUS_A,
                MIXED,
                TABLE_CONTENT,
                HEAD_CONTENT,
                TOP_CONTENT,
                AREA_ELEMENT,
                FORM_ELEMENT,
                LEGEND_ELEMENT,
                LI_ELEMENT,
                DL_PART,
                P_ELEMENT,
                OPTIONS_ELEMENT,
                OPTION_ELEMENT,
                PARAM_ELEMENT,
                TABLE_ELEMENT,
                TR_ELEMENT,
                TD_ELEMENT,
                COL_ELEMENT,
                CHARACTER_DATA
            }

            /**
            * An identifier for one of the "has a *** element in scope" predicates
            * used by HTML5 to decide when a close tag implicitly closes tags above
            * the target element on the open element stack.
            */
            public enum CloseTagScope
            {
                COMMON = 1,
                BUTTON = 2,
                LIST_ITEM = 3,
                TABLE = 4,
                ALL = (1 << 4) - 1
            }

            public static ElementContainmentRelationships make()
            {
                return new ElementContainmentRelationships();
            }

            private static int elementGroupBits(params ElementGroup[] groups)
            {
                int bitField = 0;
                int ordinal = 0;
                foreach (ElementGroup group in groups)
                {
                    //assert group.ordinal() < 32;
                    bitField |= (1 << ordinal);
                }
                return bitField;
            }

            private static int scopeBits(CloseTagScope a)
            {
                return 1 << (int)a;
            }

            private static int scopeBits(
                CloseTagScope a, CloseTagScope b, CloseTagScope c)
            {
                return (1 << (int)a) | (1 << (int)b) | (1 << (int)c);
            }

            private static Dictionary<string, ElementContainmentInfo> definitions
                = new Dictionary<string, ElementContainmentInfo>();

            private sealed class ElementContainmentInfoBuilder
            {
                private readonly string _elementName;
                private bool _resumable;
                private int _types;
                private int _contents;
                private int _transparentToContents;
                private ElementContainmentInfo _blockContainerChild = null;
                private int _inScopes = 0;

                public ElementContainmentInfoBuilder(string elementName)
                {
                    _elementName = elementName;
                }

                public ElementContainmentInfoBuilder resumable()
                {
                    _resumable = true;
                    return this;
                }

                public ElementContainmentInfoBuilder types(params ElementGroup[] groups)
                {
                    _types |= elementGroupBits(groups);
                    return this;
                }

                public ElementContainmentInfoBuilder contents(params ElementGroup[] groups)
                {
                    _contents |= elementGroupBits(groups);
                    return this;
                }

                public ElementContainmentInfoBuilder transparentToContents(
                    params ElementGroup[] groups)
                {
                    _transparentToContents |= elementGroupBits(groups);
                    return this;
                }

                public ElementContainmentInfoBuilder blockContainerChild(
                    ElementContainmentInfo c)
                {
                    _blockContainerChild = c;
                    return this;
                }

                public ElementContainmentInfoBuilder inScopes(CloseTagScope scopes)
                {
                    return inScopes(scopeBits(scopes));
                }

                public ElementContainmentInfoBuilder inScopes(
                    CloseTagScope a, CloseTagScope b, CloseTagScope c)
                {
                    return inScopes(scopeBits(a, b, c));
                }

                public ElementContainmentInfoBuilder inScopes(int scopeBits)
                {
                    _inScopes |= scopeBits;
                    return this;
                }

                public ElementContainmentInfo define()
                {
                    ElementContainmentInfo info = new ElementContainmentInfo(
                        _elementName, _resumable, _types, _contents, _transparentToContents,
                        _blockContainerChild, _inScopes);
                    definitions.Add(_elementName, info);
                    return info;
                }
            }

            private static ElementContainmentInfoBuilder defineElement(String elementName)
            {
                return new ElementContainmentInfoBuilder(elementName);
            }


            public Dictionary<string, ElementContainmentInfo> toMap()
            {
                return definitions;
            }

            static ElementContainmentRelationships()
            {
                defineElement("a")
                    .types(
                        ElementGroup.INLINE)
                    .contents(
                        ElementGroup.INLINE_MINUS_A)
                    .transparentToContents(ElementGroup.BLOCK)
                    .define();
                defineElement("abbr")
                    .resumable()
                    .types(
                        ElementGroup.INLINE, ElementGroup.INLINE_MINUS_A)
                    .contents(
                        ElementGroup.INLINE)
                    .define();
                defineElement("acronym")
                    .resumable()
                    .types(
                        ElementGroup.INLINE, ElementGroup.INLINE_MINUS_A)
                    .contents(
                        ElementGroup.INLINE)
                    .define();
                defineElement("address")
                    .types(
                        ElementGroup.BLOCK)
                    .contents(
                        ElementGroup.INLINE, ElementGroup.P_ELEMENT)
                    .define();
                defineElement("applet")
                    .types(
                        ElementGroup.INLINE, ElementGroup.INLINE_MINUS_A)
                    .contents(
                        ElementGroup.BLOCK, ElementGroup.INLINE,
                        ElementGroup.PARAM_ELEMENT)
                    .inScopes(
                        CloseTagScope.COMMON, CloseTagScope.BUTTON,
                        CloseTagScope.LIST_ITEM)
                    .define();
                defineElement("area")
                    .types(ElementGroup.AREA_ELEMENT)
                    .define();
                defineElement("audio")
                    .types(
                        ElementGroup.INLINE, ElementGroup.INLINE_MINUS_A)
                    .define();
                defineElement("b")
                    .resumable()
                    .types(
                        ElementGroup.INLINE, ElementGroup.INLINE_MINUS_A)
                    .contents(
                        ElementGroup.INLINE)
                    .define();
                defineElement("base")
                    .types(ElementGroup.HEAD_CONTENT)
                    .define();
                defineElement("basefont")
                    .types(
                        ElementGroup.INLINE, ElementGroup.INLINE_MINUS_A)
                    .define();
                defineElement("bdi")
                    .resumable()
                    .types(
                        ElementGroup.INLINE, ElementGroup.INLINE_MINUS_A)
                    .contents(
                        ElementGroup.INLINE)
                    .define();
                defineElement("bdo")
                    .resumable()
                    .types(
                        ElementGroup.INLINE, ElementGroup.INLINE_MINUS_A)
                    .contents(
                        ElementGroup.INLINE)
                    .define();
                defineElement("big")
                    .resumable()
                    .types(
                        ElementGroup.INLINE, ElementGroup.INLINE_MINUS_A)
                    .contents(
                        ElementGroup.INLINE)
                    .define();
                defineElement("blink")
                    .resumable()
                    .types(
                        ElementGroup.INLINE, ElementGroup.INLINE_MINUS_A)
                    .contents(
                        ElementGroup.INLINE)
                    .define();
                defineElement("blockquote")
                    .types(
                        ElementGroup.BLOCK)
                    .contents(
                        ElementGroup.BLOCK, ElementGroup.INLINE)
                    .define();
                defineElement("body")
                    .types(
                        ElementGroup.TOP_CONTENT)
                    .contents(
                        ElementGroup.BLOCK, ElementGroup.INLINE)
                    .define();
                defineElement("br")
                    .types(
                        ElementGroup.INLINE, ElementGroup.INLINE_MINUS_A)
                    .define();
                defineElement("button")
                    .types(
                        ElementGroup.INLINE, ElementGroup.INLINE_MINUS_A)
                    .contents(
                        ElementGroup.BLOCK, ElementGroup.INLINE)
                    .inScopes(CloseTagScope.BUTTON)
                    .define();
                defineElement("canvas")
                    .types(
                        ElementGroup.INLINE, ElementGroup.INLINE_MINUS_A)
                    .contents(
                        ElementGroup.INLINE)
                    .define();
                defineElement("caption")
                    .types(
                        ElementGroup.TABLE_CONTENT)
                    .contents(
                        ElementGroup.INLINE)
                    .inScopes(
                        CloseTagScope.COMMON, CloseTagScope.BUTTON,
                        CloseTagScope.LIST_ITEM)
                    .define();
                defineElement("center")
                    .types(
                        ElementGroup.BLOCK)
                    .contents(
                        ElementGroup.BLOCK, ElementGroup.INLINE)
                    .define();
                defineElement("cite")
                    .resumable()
                    .types(
                        ElementGroup.INLINE, ElementGroup.INLINE_MINUS_A)
                    .contents(
                        ElementGroup.INLINE)
                    .define();
                defineElement("code")
                    .resumable()
                    .types(
                        ElementGroup.INLINE, ElementGroup.INLINE_MINUS_A)
                    .contents(
                        ElementGroup.INLINE)
                    .define();
                defineElement("col")
                    .types(
                        ElementGroup.TABLE_CONTENT, ElementGroup.COL_ELEMENT)
                    .define();
                defineElement("colgroup")
                    .types(
                        ElementGroup.TABLE_CONTENT)
                    .contents(
                        ElementGroup.COL_ELEMENT)
                    .define();
                ElementContainmentInfo DD = defineElement("dd")
                    .types(
                        ElementGroup.DL_PART)
                    .contents(
                        ElementGroup.BLOCK, ElementGroup.INLINE)
                    .define();
                defineElement("del")
                    .resumable()
                    .types(
                        ElementGroup.BLOCK, ElementGroup.INLINE,
                        ElementGroup.MIXED)
                    .contents(
                        ElementGroup.BLOCK, ElementGroup.INLINE)
                    .define();
                defineElement("dfn")
                    .resumable()
                    .types(
                        ElementGroup.INLINE, ElementGroup.INLINE_MINUS_A)
                    .contents(
                        ElementGroup.INLINE)
                    .define();
                defineElement("dir")
                    .types(
                        ElementGroup.BLOCK)
                    .contents(
                        ElementGroup.LI_ELEMENT)
                    .define();
                defineElement("div")
                    .types(
                        ElementGroup.BLOCK)
                    .contents(
                        ElementGroup.BLOCK, ElementGroup.INLINE)
                    .define();
                defineElement("dl")
                    .types(
                        ElementGroup.BLOCK)
                    .contents(
                        ElementGroup.DL_PART)
                    .blockContainerChild(DD)
                    .define();
                defineElement("dt")
                    .types(
                        ElementGroup.DL_PART)
                    .contents(
                        ElementGroup.INLINE)
                    .define();
                defineElement("em")
                    .resumable()
                    .types(
                        ElementGroup.INLINE, ElementGroup.INLINE_MINUS_A)
                    .contents(
                        ElementGroup.INLINE)
                    .define();
                defineElement("fieldset")
                    .types(
                        ElementGroup.BLOCK)
                    .contents(
                        ElementGroup.BLOCK, ElementGroup.INLINE,
                        ElementGroup.LEGEND_ELEMENT)
                    .define();
                defineElement("font")
                    .types(
                        ElementGroup.INLINE, ElementGroup.INLINE_MINUS_A)
                    .contents(
                        ElementGroup.INLINE)
                    .define();
                defineElement("form")
                    .types(
                        ElementGroup.BLOCK, ElementGroup.FORM_ELEMENT)
                    .contents(
                        ElementGroup.BLOCK, ElementGroup.INLINE,
                        ElementGroup.INLINE_MINUS_A, ElementGroup.TR_ELEMENT,
                        ElementGroup.TD_ELEMENT)
                    .define();
                defineElement("h1")
                    .types(
                        ElementGroup.BLOCK)
                    .contents(
                        ElementGroup.INLINE)
                    .define();
                defineElement("h2")
                    .types(
                        ElementGroup.BLOCK)
                    .contents(
                        ElementGroup.INLINE)
                    .define();
                defineElement("h3")
                    .types(
                        ElementGroup.BLOCK)
                    .contents(
                        ElementGroup.INLINE)
                    .define();
                defineElement("h4")
                    .types(
                        ElementGroup.BLOCK)
                    .contents(
                        ElementGroup.INLINE)
                    .define();
                defineElement("h5")
                    .types(
                        ElementGroup.BLOCK)
                    .contents(
                        ElementGroup.INLINE)
                    .define();
                defineElement("h6")
                    .types(
                        ElementGroup.BLOCK)
                    .contents(
                        ElementGroup.INLINE)
                    .define();
                defineElement("head")
                    .types(
                        ElementGroup.TOP_CONTENT)
                    .contents(
                        ElementGroup.HEAD_CONTENT)
                    .define();
                defineElement("hr")
                    .types(ElementGroup.BLOCK)
                    .define();
                defineElement("html")
                    .contents(ElementGroup.TOP_CONTENT)
                    .inScopes(CloseTagScope.ALL)
                    .define();
                defineElement("i")
                    .resumable()
                    .types(
                        ElementGroup.INLINE, ElementGroup.INLINE_MINUS_A)
                    .contents(
                        ElementGroup.INLINE)
                    .define();
                defineElement("iframe")
                    .types(
                        ElementGroup.INLINE, ElementGroup.INLINE_MINUS_A)
                    .contents(
                        ElementGroup.BLOCK, ElementGroup.INLINE)
                    .define();
                defineElement("img")
                    .types(
                        ElementGroup.INLINE, ElementGroup.INLINE_MINUS_A)
                    .define();
                defineElement("input")
                    .types(
                        ElementGroup.INLINE, ElementGroup.INLINE_MINUS_A)
                    .define();
                defineElement("ins")
                    .resumable()
                    .types(
                        ElementGroup.BLOCK, ElementGroup.INLINE)
                    .contents(
                        ElementGroup.BLOCK, ElementGroup.INLINE)
                    .define();
                defineElement("isindex")
                    .types(ElementGroup.INLINE)
                    .define();
                defineElement("kbd")
                    .resumable()
                    .types(
                        ElementGroup.INLINE, ElementGroup.INLINE_MINUS_A)
                    .contents(
                        ElementGroup.INLINE)
                    .define();
                defineElement("label")
                    .types(
                        ElementGroup.INLINE, ElementGroup.INLINE_MINUS_A)
                    .contents(
                        ElementGroup.INLINE)
                    .define();
                defineElement("legend")
                    .types(
                        ElementGroup.LEGEND_ELEMENT)
                    .contents(
                        ElementGroup.INLINE)
                    .define();
                ElementContainmentInfo LI = defineElement("li")
                    .types(
                        ElementGroup.LI_ELEMENT)
                    .contents(
                        ElementGroup.BLOCK, ElementGroup.INLINE)
                    .define();
                defineElement("link")
                    .types(
                        ElementGroup.INLINE, ElementGroup.HEAD_CONTENT)
                    .define();
                defineElement("listing")
                    .types(
                        ElementGroup.BLOCK)
                    .contents(
                        ElementGroup.INLINE)
                    .define();
                defineElement("map")
                    .types(
                        ElementGroup.INLINE)
                    .contents(
                        ElementGroup.BLOCK, ElementGroup.AREA_ELEMENT)
                    .define();
                defineElement("meta")
                    .types(ElementGroup.HEAD_CONTENT)
                    .define();
                defineElement("nobr")
                    .types(
                        ElementGroup.INLINE, ElementGroup.INLINE_MINUS_A)
                    .contents(
                        ElementGroup.INLINE)
                    .define();
                defineElement("noframes")
                    .types(
                        ElementGroup.BLOCK, ElementGroup.TOP_CONTENT)
                    .contents(
                        ElementGroup.BLOCK, ElementGroup.INLINE,
                        ElementGroup.TOP_CONTENT)
                    .define();
                defineElement("noscript")
                    .types(
                        ElementGroup.BLOCK)
                    .contents(
                        ElementGroup.BLOCK, ElementGroup.INLINE)
                    .define();
                defineElement("object")
                    .types(
                        ElementGroup.INLINE, ElementGroup.INLINE_MINUS_A,
                        ElementGroup.HEAD_CONTENT)
                    .contents(
                        ElementGroup.BLOCK, ElementGroup.INLINE,
                        ElementGroup.PARAM_ELEMENT)
                    .inScopes(
                        CloseTagScope.COMMON, CloseTagScope.BUTTON,
                        CloseTagScope.LIST_ITEM)
                    .define();
                defineElement("ol")
                    .types(
                        ElementGroup.BLOCK)
                    .contents(
                        ElementGroup.LI_ELEMENT)
                    .blockContainerChild(LI)
                    .inScopes(CloseTagScope.LIST_ITEM)
                    .define();
                defineElement("optgroup")
                    .types(
                        ElementGroup.OPTIONS_ELEMENT)
                    .contents(
                        ElementGroup.OPTIONS_ELEMENT)
                    .define();
                defineElement("option")
                    .types(
                        ElementGroup.OPTIONS_ELEMENT, ElementGroup.OPTION_ELEMENT)
                    .contents(
                        ElementGroup.CHARACTER_DATA)
                    .define();
                defineElement("p")
                    .types(
                        ElementGroup.BLOCK, ElementGroup.P_ELEMENT)
                    .contents(
                        ElementGroup.INLINE, ElementGroup.TABLE_ELEMENT)
                    .define();
                defineElement("param")
                    .types(ElementGroup.PARAM_ELEMENT)
                    .define();
                defineElement("pre")
                    .types(
                        ElementGroup.BLOCK)
                    .contents(
                        ElementGroup.INLINE)
                    .define();
                defineElement("q")
                    .resumable()
                    .types(
                        ElementGroup.INLINE, ElementGroup.INLINE_MINUS_A)
                    .contents(
                        ElementGroup.INLINE)
                    .define();
                defineElement("s")
                    .resumable()
                    .types(
                        ElementGroup.INLINE, ElementGroup.INLINE_MINUS_A)
                    .contents(
                        ElementGroup.INLINE)
                    .define();
                defineElement("samp")
                    .resumable()
                    .types(
                        ElementGroup.INLINE, ElementGroup.INLINE_MINUS_A)
                    .contents(
                        ElementGroup.INLINE)
                    .define();
                defineElement("script")
                    .types(
                        ElementGroup.BLOCK, ElementGroup.INLINE,
                        ElementGroup.INLINE_MINUS_A, ElementGroup.MIXED,
                        ElementGroup.TABLE_CONTENT, ElementGroup.HEAD_CONTENT,
                        ElementGroup.TOP_CONTENT, ElementGroup.AREA_ELEMENT,
                        ElementGroup.FORM_ELEMENT, ElementGroup.LEGEND_ELEMENT,
                        ElementGroup.LI_ELEMENT, ElementGroup.DL_PART,
                        ElementGroup.P_ELEMENT, ElementGroup.OPTIONS_ELEMENT,
                        ElementGroup.OPTION_ELEMENT, ElementGroup.PARAM_ELEMENT,
                        ElementGroup.TABLE_ELEMENT, ElementGroup.TR_ELEMENT,
                        ElementGroup.TD_ELEMENT, ElementGroup.COL_ELEMENT)
                    .contents(
                        ElementGroup.CHARACTER_DATA)
                    .define();
                defineElement("select")
                    .types(
                        ElementGroup.INLINE)
                    .contents(
                        ElementGroup.OPTIONS_ELEMENT)
                    .define();
                defineElement("small")
                    .resumable()
                    .types(
                        ElementGroup.INLINE, ElementGroup.INLINE_MINUS_A)
                    .contents(
                        ElementGroup.INLINE)
                    .define();
                defineElement("span")
                    .types(
                        ElementGroup.INLINE, ElementGroup.INLINE_MINUS_A)
                    .contents(
                        ElementGroup.INLINE)
                    .define();
                defineElement("strike")
                    .resumable()
                    .types(
                        ElementGroup.INLINE, ElementGroup.INLINE_MINUS_A)
                    .contents(
                        ElementGroup.INLINE)
                    .define();
                defineElement("strong")
                    .resumable()
                    .types(
                        ElementGroup.INLINE, ElementGroup.INLINE_MINUS_A)
                    .contents(
                        ElementGroup.INLINE)
                    .define();
                defineElement("style")
                    .types(
                        ElementGroup.BLOCK, ElementGroup.INLINE,
                        ElementGroup.INLINE_MINUS_A, ElementGroup.MIXED,
                        ElementGroup.TABLE_CONTENT, ElementGroup.HEAD_CONTENT,
                        ElementGroup.TOP_CONTENT, ElementGroup.AREA_ELEMENT,
                        ElementGroup.FORM_ELEMENT, ElementGroup.LEGEND_ELEMENT,
                        ElementGroup.LI_ELEMENT, ElementGroup.DL_PART,
                        ElementGroup.P_ELEMENT, ElementGroup.OPTIONS_ELEMENT,
                        ElementGroup.OPTION_ELEMENT, ElementGroup.PARAM_ELEMENT,
                        ElementGroup.TABLE_ELEMENT, ElementGroup.TR_ELEMENT,
                        ElementGroup.TD_ELEMENT, ElementGroup.COL_ELEMENT)
                    .contents(
                        ElementGroup.CHARACTER_DATA)
                    .define();
                defineElement("sub")
                    .resumable()
                    .types(
                        ElementGroup.INLINE, ElementGroup.INLINE_MINUS_A)
                    .contents(
                        ElementGroup.INLINE)
                    .define();
                defineElement("sup")
                    .resumable()
                    .types(
                        ElementGroup.INLINE, ElementGroup.INLINE_MINUS_A)
                    .contents(
                        ElementGroup.INLINE)
                    .define();
                defineElement("table")
                    .types(
                        ElementGroup.BLOCK, ElementGroup.TABLE_ELEMENT)
                    .contents(
                        ElementGroup.TABLE_CONTENT, ElementGroup.FORM_ELEMENT)
                    .inScopes(CloseTagScope.ALL)
                    .define();
                defineElement("tbody")
                    .types(
                        ElementGroup.TABLE_CONTENT)
                    .contents(
                        ElementGroup.TR_ELEMENT)
                    .define();
                ElementContainmentInfo TD = defineElement("td")
                    .types(
                        ElementGroup.TD_ELEMENT)
                    .contents(
                        ElementGroup.BLOCK, ElementGroup.INLINE)
                    .inScopes(
                        CloseTagScope.COMMON, CloseTagScope.BUTTON,
                        CloseTagScope.LIST_ITEM)
                    .define();
                defineElement("textarea")
                    // No, a textarea cannot be inside a link.
                    .types(ElementGroup.INLINE)
                    .contents(ElementGroup.CHARACTER_DATA)
                    .define();
                defineElement("tfoot")
                    .types(
                        ElementGroup.TABLE_CONTENT)
                    .contents(
                        ElementGroup.FORM_ELEMENT, ElementGroup.TR_ELEMENT,
                        ElementGroup.TD_ELEMENT)
                    .define();
                defineElement("th")
                    .types(
                        ElementGroup.TD_ELEMENT)
                    .contents(
                        ElementGroup.BLOCK, ElementGroup.INLINE)
                    .inScopes(
                        CloseTagScope.COMMON, CloseTagScope.BUTTON,
                        CloseTagScope.LIST_ITEM)
                    .define();
                defineElement("thead")
                    .types(
                        ElementGroup.TABLE_CONTENT)
                    .contents(
                        ElementGroup.FORM_ELEMENT, ElementGroup.TR_ELEMENT,
                        ElementGroup.TD_ELEMENT)
                    .define();
                defineElement("title")
                    .types(ElementGroup.HEAD_CONTENT)
                    .contents(ElementGroup.CHARACTER_DATA)
                    .define();
                defineElement("tr")
                    .types(
                        ElementGroup.TABLE_CONTENT, ElementGroup.TR_ELEMENT)
                    .contents(
                        ElementGroup.FORM_ELEMENT, ElementGroup.TD_ELEMENT)
                    .blockContainerChild(TD)
                    .define();
                defineElement("tt")
                    .resumable()
                    .types(
                        ElementGroup.INLINE, ElementGroup.INLINE_MINUS_A)
                    .contents(
                        ElementGroup.INLINE)
                    .define();
                defineElement("u")
                    .resumable()
                    .types(
                        ElementGroup.INLINE, ElementGroup.INLINE_MINUS_A)
                    .contents(
                        ElementGroup.INLINE)
                    .define();
                defineElement("ul")
                    .types(
                        ElementGroup.BLOCK)
                    .contents(
                        ElementGroup.LI_ELEMENT)
                    .blockContainerChild(LI)
                    .inScopes(CloseTagScope.LIST_ITEM)
                    .define();
                defineElement("var")
                    .types(
                        ElementGroup.INLINE, ElementGroup.INLINE_MINUS_A)
                    .contents(
                        ElementGroup.INLINE)
                    .define();
                defineElement("video")
                    .types(
                        ElementGroup.INLINE, ElementGroup.INLINE_MINUS_A)
                    .define();
                defineElement("wbr")
                    .types(
                        ElementGroup.INLINE, ElementGroup.INLINE_MINUS_A)
                    .define();
                defineElement("xmp")
                    .types(
                        ElementGroup.BLOCK)
                    .contents(
                        ElementGroup.INLINE)
                    .define();
            }

            public static readonly ElementContainmentInfo CHARACTER_DATA_ONLY
                = new ElementContainmentInfo(
                    "#text", false,
                    elementGroupBits(
                        ElementGroup.INLINE, ElementGroup.INLINE_MINUS_A,
                        ElementGroup.BLOCK, ElementGroup.CHARACTER_DATA),
                    0, 0, null, 0);
        }

        static bool allowsPlainTextualContent(String canonElementName)
        {
            ElementContainmentInfo info;
            if (!ELEMENT_CONTAINMENT_RELATIONSHIPS.TryGetValue(canonElementName, out info)
                || ((info.Contents
                    & ElementContainmentRelationships.CHARACTER_DATA_ONLY.Types)
                    != 0))
            {
                switch (HtmlTextEscapingModeHelper.getModeForTag(canonElementName))
                {
                    case HtmlTextEscapingMode.PCDATA:     return true;
                    case HtmlTextEscapingMode.RCDATA:     return true;
                    case HtmlTextEscapingMode.PLAIN_TEXT: return true;
                    case HtmlTextEscapingMode.VOID:       return false;
                    case HtmlTextEscapingMode.CDATA:
                    case HtmlTextEscapingMode.CDATA_SOMETIMES:
                    return "xmp".Equals(canonElementName)
                        || "listing".Equals(canonElementName);
                }
            }
            return false;
        }
    }
}