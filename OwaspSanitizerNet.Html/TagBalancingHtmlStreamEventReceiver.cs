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
using System.ComponentModel;
using System.Linq;

namespace OwaspSanitizerNet.Html
{
    /**
     * Wraps an HTML stream event receiver to fill in missing close tags.
     * If the balancer is given the HTML {@code <p>1<p>2}, the wrapped receiver will
     * see events equivalent to {@code <p>1</p><p>2</p>}.
     *
     * @author Mike Samuel <mikesamuel@gmail.com>
     */
    [TCB]
    public class TagBalancingHtmlStreamEventReceiver
        : IHtmlStreamEventReceiver
    {
        private readonly IHtmlStreamEventReceiver _underlying;
        private int _nestingLimit = int.MaxValue;
        private readonly List<ElementContainmentInfo> _openElements
            = new List<ElementContainmentInfo>();

        public TagBalancingHtmlStreamEventReceiver(
            IHtmlStreamEventReceiver underlying)
        {
            _underlying = underlying;
        }

        public void SetNestingLimit(int limit)
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
            for (int i = Math.Min(_nestingLimit, _openElements.Count); --i >= 0; )
            {
                _underlying.closeTag(_openElements[i].ElementName);
            }
            _openElements.Clear();
            _underlying.closeDocument();
        }

        public void openTag(String elementName, List<String> attrs)
        {
            String canonElementName = HtmlLexer.CanonicalName(elementName);
            ElementContainmentInfo elInfo;
            // Treat unrecognized tags as void, but emit closing tags in closeTag().
            if (!ElementContainmentRelationships.TryGetValue(canonElementName, out elInfo))
            {
                if (_openElements.Count < _nestingLimit)
                {
                    _underlying.openTag(elementName, attrs);
                }
                return;
            }

            PrepareForContent(elInfo);

            if (_openElements.Count < _nestingLimit)
            {
                _underlying.openTag(elInfo.ElementName, attrs);
            }
            if (!elInfo.IsVoid)
            {
                _openElements.Add(elInfo);
            }
        }

        private void PrepareForContent(ElementContainmentInfo elInfo)
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
                List<ElementContainmentInfo> toResumeInReverse = null;
                while (true)
                {
                    if ((top.Contents & elInfo.Types) != 0) { break; }
                    if (_openElements.Count < _nestingLimit)
                    {
                        _underlying.closeTag(top.ElementName);
                    }
                    _openElements.RemoveAt(--nOpen);
                    if (top.Resumable)
                    {
                        if (toResumeInReverse == null)
                        {
                            toResumeInReverse = new List<ElementContainmentInfo>();
                        }
                        toResumeInReverse.Add(top);
                    }
                    if (nOpen == 0) { break; }
                    top = _openElements[nOpen - 1];
                }

                if (toResumeInReverse != null)
                {
                    Resume(toResumeInReverse);
                }
            }
        }

        public void closeTag(String elementName)
        {
            String canonElementName = HtmlLexer.CanonicalName(elementName);
            ElementContainmentInfo elInfo;
            if (!ElementContainmentRelationships.TryGetValue(canonElementName, out elInfo))
            {  // Allow unrecognized end tags through.
                if (_openElements.Count < _nestingLimit)
                {
                    _underlying.closeTag(elementName);
                }
                return;
            }
            int index = _openElements.LastIndexOf(elInfo);
            // Let any of </h1>, </h2>, ... close other header tags.
            if (IsHeaderElementName(canonElementName))
            {
                for (int i = _openElements.Count, limit = index + 1; --i >= limit; )
                {
                    ElementContainmentInfo openEl = _openElements[i];
                    if (IsHeaderElementName(openEl.ElementName))
                    {
                        elInfo = openEl;
                        index = i;
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
            for (int i = _openElements.Count; --i > index; )
            {
                if ((_openElements[i].InScopes & blockingScopes) != 0)
                {
                    return;
                }
            }

            int last = _openElements.Count;
            // Close all the elements that cannot contain the element to open.
            List<ElementContainmentInfo> toResumeInReverse = null;
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
                    if (toResumeInReverse == null)
                    {
                        toResumeInReverse = new List<ElementContainmentInfo>();
                    }
                    toResumeInReverse.Add(unclosed);
                }
            }
            if (_openElements.Count < _nestingLimit)
            {
                _underlying.closeTag(elInfo.ElementName);
            }
            _openElements.RemoveAt(index);
            if (toResumeInReverse != null)
            {
                Resume(toResumeInReverse);
            }
        }

        private void Resume(IEnumerable<ElementContainmentInfo> toResumeInReverse)
        {
            foreach (ElementContainmentInfo toResume in toResumeInReverse)
            {
                if (_openElements.Count < _nestingLimit)
                {
                    _underlying.openTag(toResume.ElementName, new List<string>());
                }
                _openElements.Add(toResume);
            }
        }

        private const int HtmlSpaceCharBitmask = (1 << ' ') | (1 << '\t') | (1 << '\n') | (1 << '\u000c') | (1 << '\r');

        public void text(String text)
        {
            int n = text.Length;
            for (int i = 0; i < n; ++i)
            {
                int ch = text[i];
                if (ch > 0x20 || (HtmlSpaceCharBitmask & (1 << ch)) == 0)
                {
                    PrepareForContent(ElementContainmentRelationshipRegistry.CharacterDataOnly);
                    break;
                }
            }

            if (_openElements.Count < _nestingLimit)
            {
                _underlying.text(text);
            }
        }

        private static bool IsHeaderElementName(String canonElementName)
        {
            return canonElementName.Length == 2 && canonElementName[0] == 'h'
                && canonElementName[1] <= '9';
        }


        [ImmutableObject(true)]
        private sealed class ElementContainmentInfo
        {
            internal readonly String ElementName;
            /**
             * True if the adoption agency algorithm allows an element to be resumed
             * after a mis-nested end tag closes it.
             * E.g. in {@code <b>Foo<i>Bar</b>Baz</i>} the {@code <i>} element is
             * resumed after the {@code <b>} element is closed.
             */
            internal readonly bool Resumable;
            /** A set of bits of element groups into which the element falls. */
            internal readonly int Types;
            /** The type of elements that an element can contain. */
            internal readonly int Contents;
            /** True if the element has no content -- not even text content. */
            internal readonly bool IsVoid;
            /** A legal child of this node that can contain block content. */
            internal readonly ElementContainmentInfo BlockContainerChild;
            /** A bit set of close tag scopes that block this element's close tags. */
            internal readonly int BlockedByScopes;
            /** A bit set of scopes groups into which this element falls. */
            internal readonly int InScopes;

            internal ElementContainmentInfo(
                String elementName, bool resumable, int types, int contents,
                ElementContainmentInfo blockContainerChild,
                int inScopes)
            {
                ElementName = elementName;
                Resumable = resumable;
                Types = types;
                Contents = contents;
                IsVoid = contents == 0
                    && HtmlTextEscapingModeExtensions.isVoidElement(elementName);
                BlockContainerChild = blockContainerChild;
                BlockedByScopes =
                    (int)ElementContainmentRelationshipRegistry.CloseTagScope.All & ~inScopes;
                InScopes = inScopes;
            }

            public override String ToString()
            {
                return "<" + ElementName + ">";
            }
        }

        private static readonly Dictionary<String, ElementContainmentInfo>
            ElementContainmentRelationships
            = new ElementContainmentRelationshipRegistry().ToDictionary();

        private class ElementContainmentRelationshipRegistry
        {
            private enum ElementGroup
            {
                Block,
                Inline,
                InlineMinusA,
                Mixed,
                TableContent,
                HeadContent,
                TopContent,
                AreaElement,
                FormElement,
                LegendElement,
                LiElement,
                DlPart,
                PElement,
                OptionsElement,
                OptionElement,
                ParamElement,
                TableElement,
                TrElement,
                TdElement,
                ColElement,
                CharacterData,
            }

            /**
             * An identifier for one of the "has a *** element in scope" predicates
             * used by HTML5 to decide when a close tag implicitly closes tags above
             * the target element on the open element stack.
             */
            internal enum CloseTagScope
            {
                Common,
                Button,
                ListItem,
                Table,
                All = (1 << Table) - 1
            }

            private static int ElementGroupBits(ElementGroup a)
            {
                return 1 << (int)a;
            }

            private static int ElementGroupBits(
                ElementGroup a, ElementGroup b)
            {
                return (1 << (int)a) | (1 << (int)b);
            }

            private static int ElementGroupBits(
                ElementGroup a, ElementGroup b, ElementGroup c)
            {
                return (1 << (int)a) | (1 << (int)b) | (1 << (int)c);
            }

            private static int ElementGroupBits(
                params ElementGroup[] bits)
            {
                return bits.Aggregate(0, (current, bit) => current | (1 << (int)bit));
            }

            private static int ScopeBits(CloseTagScope a)
            {
                return 1 << (int)a;
            }

            private static int ScopeBits(
                CloseTagScope a, CloseTagScope b, CloseTagScope c)
            {
                return (1 << (int)a) | (1 << (int)b) | (1 << (int)c);
            }

            private readonly Dictionary<String, ElementContainmentInfo> _definitions
                = new Dictionary<string, ElementContainmentInfo>();

            private ElementContainmentInfo DefineElement(
              String elementName, bool resumable, int types, int contentTypes,
              int inScopes)
            {
                return DefineElement(
                    elementName, resumable, types, contentTypes, null, inScopes);
            }

            private ElementContainmentInfo DefineElement(
                String elementName, bool resumable, int types, int contentTypes,
                ElementContainmentInfo blockContainer = null)
            {
                return DefineElement(
                    elementName, resumable, types, contentTypes, blockContainer, 0);
            }

            private ElementContainmentInfo DefineElement(
                String elementName, bool resumable, int types, int contentTypes,
                ElementContainmentInfo blockContainer, int inScopes)
            {
                var info = new ElementContainmentInfo(
                    elementName, resumable, types, contentTypes, blockContainer,
                    inScopes);
                _definitions.Add(elementName, info);
                return info;
            }

            internal Dictionary<String, ElementContainmentInfo> ToDictionary()
            {
                return _definitions;
            }

            internal ElementContainmentRelationshipRegistry()
            {
                DefineElement(
                    "a", false, ElementGroupBits(
                        ElementGroup.Inline
                    ), ElementGroupBits(
                        ElementGroup.InlineMinusA
                    ));
                DefineElement(
                    "abbr", true, ElementGroupBits(
                        ElementGroup.Inline, ElementGroup.InlineMinusA
                    ), ElementGroupBits(
                        ElementGroup.Inline
                    ));
                DefineElement(
                    "acronym", true, ElementGroupBits(
                        ElementGroup.Inline, ElementGroup.InlineMinusA
                    ), ElementGroupBits(
                        ElementGroup.Inline
                    ));
                DefineElement(
                    "address", false, ElementGroupBits(
                        ElementGroup.Block
                    ), ElementGroupBits(
                        ElementGroup.Inline, ElementGroup.PElement
                    ));
                DefineElement(
                    "applet", false, ElementGroupBits(
                        ElementGroup.Inline, ElementGroup.InlineMinusA
                    ), ElementGroupBits(
                        ElementGroup.Block, ElementGroup.Inline,
                        ElementGroup.ParamElement
                    ), ScopeBits(
                        CloseTagScope.Common, CloseTagScope.Button,
                        CloseTagScope.ListItem
                    ));
                DefineElement(
                    "area", false, ElementGroupBits(ElementGroup.AreaElement), 0);
                DefineElement(
                    "audio", false, ElementGroupBits(
                        ElementGroup.Inline, ElementGroup.InlineMinusA
                    ), 0);
                DefineElement(
                    "b", true, ElementGroupBits(
                        ElementGroup.Inline, ElementGroup.InlineMinusA
                    ), ElementGroupBits(
                        ElementGroup.Inline
                    ));
                DefineElement(
                    "base", false, ElementGroupBits(ElementGroup.HeadContent), 0);
                DefineElement(
                    "basefont", false, ElementGroupBits(
                        ElementGroup.Inline, ElementGroup.InlineMinusA
                    ), 0);
                DefineElement(
                    "bdi", true, ElementGroupBits(
                        ElementGroup.Inline, ElementGroup.InlineMinusA
                    ), ElementGroupBits(
                        ElementGroup.Inline
                    ));
                DefineElement(
                    "bdo", true, ElementGroupBits(
                        ElementGroup.Inline, ElementGroup.InlineMinusA
                    ), ElementGroupBits(
                        ElementGroup.Inline
                    ));
                DefineElement(
                    "big", true, ElementGroupBits(
                        ElementGroup.Inline, ElementGroup.InlineMinusA
                    ), ElementGroupBits(
                        ElementGroup.Inline
                    ));
                DefineElement(
                    "blink", true, ElementGroupBits(
                        ElementGroup.Inline, ElementGroup.InlineMinusA
                    ), ElementGroupBits(
                        ElementGroup.Inline
                    ));
                DefineElement(
                    "blockquote", false, ElementGroupBits(
                        ElementGroup.Block
                    ), ElementGroupBits(
                        ElementGroup.Block, ElementGroup.Inline
                    ));
                DefineElement(
                    "body", false, ElementGroupBits(
                        ElementGroup.TopContent
                    ), ElementGroupBits(
                        ElementGroup.Block, ElementGroup.Inline
                    ));
                DefineElement(
                    "br", false, ElementGroupBits(
                        ElementGroup.Inline, ElementGroup.InlineMinusA
                    ), 0);
                DefineElement(
                    "button", false, ElementGroupBits(
                        ElementGroup.Inline, ElementGroup.InlineMinusA
                    ), ElementGroupBits(
                        ElementGroup.Block, ElementGroup.Inline
                    ), ScopeBits(CloseTagScope.Button));
                DefineElement(
                    "canvas", false, ElementGroupBits(
                        ElementGroup.Inline, ElementGroup.InlineMinusA
                    ), ElementGroupBits(
                        ElementGroup.Inline
                    ));
                DefineElement(
                    "caption", false, ElementGroupBits(
                        ElementGroup.TableContent
                    ), ElementGroupBits(
                        ElementGroup.Inline
                    ), ScopeBits(
                        CloseTagScope.Common, CloseTagScope.Button,
                        CloseTagScope.ListItem
                    ));
                DefineElement(
                    "center", false, ElementGroupBits(
                        ElementGroup.Block
                    ), ElementGroupBits(
                        ElementGroup.Block, ElementGroup.Inline
                    ));
                DefineElement(
                    "cite", true, ElementGroupBits(
                        ElementGroup.Inline, ElementGroup.InlineMinusA
                    ), ElementGroupBits(
                        ElementGroup.Inline
                    ));
                DefineElement(
                    "code", true, ElementGroupBits(
                        ElementGroup.Inline, ElementGroup.InlineMinusA
                    ), ElementGroupBits(
                        ElementGroup.Inline
                    ));
                DefineElement(
                    "col", false, ElementGroupBits(
                        ElementGroup.TableContent, ElementGroup.ColElement
                    ), 0);
                DefineElement(
                    "colgroup", false, ElementGroupBits(
                        ElementGroup.TableContent
                    ), ElementGroupBits(
                        ElementGroup.ColElement
                    ));
                ElementContainmentInfo dd = DefineElement(
                    "dd", false, ElementGroupBits(
                        ElementGroup.DlPart
                    ), ElementGroupBits(
                        ElementGroup.Block, ElementGroup.Inline
                    ));
                DefineElement(
                    "del", true, ElementGroupBits(
                        ElementGroup.Block, ElementGroup.Inline,
                        ElementGroup.Mixed
                    ), ElementGroupBits(
                        ElementGroup.Block, ElementGroup.Inline
                    ));
                DefineElement(
                    "dfn", true, ElementGroupBits(
                        ElementGroup.Inline, ElementGroup.InlineMinusA
                    ), ElementGroupBits(
                        ElementGroup.Inline
                    ));
                DefineElement(
                    "dir", false, ElementGroupBits(
                        ElementGroup.Block
                    ), ElementGroupBits(
                        ElementGroup.LiElement
                    ));
                DefineElement(
                    "div", false, ElementGroupBits(
                        ElementGroup.Block
                    ), ElementGroupBits(
                        ElementGroup.Block, ElementGroup.Inline
                    ));
                DefineElement(
                    "dl", false, ElementGroupBits(
                        ElementGroup.Block
                    ), ElementGroupBits(
                        ElementGroup.DlPart
                    ),
                    dd);
                DefineElement(
                    "dt", false, ElementGroupBits(
                        ElementGroup.DlPart
                    ), ElementGroupBits(
                        ElementGroup.Inline
                    ));
                DefineElement(
                    "em", true, ElementGroupBits(
                        ElementGroup.Inline, ElementGroup.InlineMinusA
                    ), ElementGroupBits(
                        ElementGroup.Inline
                    ));
                DefineElement(
                    "fieldset", false, ElementGroupBits(
                        ElementGroup.Block
                    ), ElementGroupBits(
                        ElementGroup.Block, ElementGroup.Inline,
                        ElementGroup.LegendElement
                    ));
                DefineElement(
                    "font", false, ElementGroupBits(
                        ElementGroup.Inline, ElementGroup.InlineMinusA
                    ), ElementGroupBits(
                        ElementGroup.Inline
                    ));
                DefineElement(
                    "form", false, ElementGroupBits(
                        ElementGroup.Block, ElementGroup.FormElement
                    ), ElementGroupBits(
                        ElementGroup.Block, ElementGroup.Inline,
                        ElementGroup.InlineMinusA, ElementGroup.TrElement,
                        ElementGroup.TdElement
                    ));
                DefineElement(
                    "h1", false, ElementGroupBits(
                        ElementGroup.Block
                    ), ElementGroupBits(
                        ElementGroup.Inline
                    ));
                DefineElement(
                    "h2", false, ElementGroupBits(
                        ElementGroup.Block
                    ), ElementGroupBits(
                        ElementGroup.Inline
                    ));
                DefineElement(
                    "h3", false, ElementGroupBits(
                        ElementGroup.Block
                    ), ElementGroupBits(
                        ElementGroup.Inline
                    ));
                DefineElement(
                    "h4", false, ElementGroupBits(
                        ElementGroup.Block
                    ), ElementGroupBits(
                        ElementGroup.Inline
                    ));
                DefineElement(
                    "h5", false, ElementGroupBits(
                        ElementGroup.Block
                    ), ElementGroupBits(
                        ElementGroup.Inline
                    ));
                DefineElement(
                    "h6", false, ElementGroupBits(
                        ElementGroup.Block
                    ), ElementGroupBits(
                        ElementGroup.Inline
                    ));
                DefineElement(
                    "head", false, ElementGroupBits(
                        ElementGroup.TopContent
                    ), ElementGroupBits(
                        ElementGroup.HeadContent
                    ));
                DefineElement(
                    "hr", false, ElementGroupBits(ElementGroup.Block), 0);
                DefineElement(
                    "html", false, 0, ElementGroupBits(ElementGroup.TopContent),
                    (int)CloseTagScope.All);
                DefineElement(
                    "i", true, ElementGroupBits(
                        ElementGroup.Inline, ElementGroup.InlineMinusA
                    ), ElementGroupBits(
                        ElementGroup.Inline
                    ));
                DefineElement(
                    "iframe", false, ElementGroupBits(
                        ElementGroup.Inline, ElementGroup.InlineMinusA
                    ), ElementGroupBits(
                        ElementGroup.Block, ElementGroup.Inline
                    ));
                DefineElement(
                    "img", false, ElementGroupBits(
                        ElementGroup.Inline, ElementGroup.InlineMinusA
                    ), 0);
                DefineElement(
                    "input", false, ElementGroupBits(
                        ElementGroup.Inline, ElementGroup.InlineMinusA
                    ), 0);
                DefineElement(
                    "ins", true, ElementGroupBits(
                        ElementGroup.Block, ElementGroup.Inline
                    ), ElementGroupBits(
                        ElementGroup.Block, ElementGroup.Inline
                    ));
                DefineElement(
                    "isindex", false, ElementGroupBits(ElementGroup.Inline), 0);
                DefineElement(
                    "kbd", true, ElementGroupBits(
                        ElementGroup.Inline, ElementGroup.InlineMinusA
                    ), ElementGroupBits(
                        ElementGroup.Inline
                    ));
                DefineElement(
                    "label", false, ElementGroupBits(
                        ElementGroup.Inline, ElementGroup.InlineMinusA
                    ), ElementGroupBits(
                        ElementGroup.Inline
                    ));
                DefineElement(
                    "legend", false, ElementGroupBits(
                        ElementGroup.LegendElement
                    ), ElementGroupBits(
                        ElementGroup.Inline
                    ));
                ElementContainmentInfo li = DefineElement(
                    "li", false, ElementGroupBits(
                        ElementGroup.LiElement
                    ), ElementGroupBits(
                        ElementGroup.Block, ElementGroup.Inline
                    ));
                DefineElement(
                    "link", false, ElementGroupBits(
                        ElementGroup.Inline, ElementGroup.HeadContent
                    ), 0);
                DefineElement(
                    "listing", false, ElementGroupBits(
                        ElementGroup.Block
                    ), ElementGroupBits(
                        ElementGroup.Inline
                    ));
                DefineElement(
                    "map", false, ElementGroupBits(
                        ElementGroup.Inline
                    ), ElementGroupBits(
                        ElementGroup.Block, ElementGroup.AreaElement
                    ));
                DefineElement(
                    "meta", false, ElementGroupBits(ElementGroup.HeadContent), 0);
                DefineElement(
                    "nobr", false, ElementGroupBits(
                        ElementGroup.Inline, ElementGroup.InlineMinusA
                    ), ElementGroupBits(
                        ElementGroup.Inline
                    ));
                DefineElement(
                    "noframes", false, ElementGroupBits(
                        ElementGroup.Block, ElementGroup.TopContent
                    ), ElementGroupBits(
                        ElementGroup.Block, ElementGroup.Inline,
                        ElementGroup.TopContent
                    ));
                DefineElement(
                    "noscript", false, ElementGroupBits(
                        ElementGroup.Block
                    ), ElementGroupBits(
                        ElementGroup.Block, ElementGroup.Inline
                    ));
                DefineElement(
                    "object", false, ElementGroupBits(
                        ElementGroup.Inline, ElementGroup.InlineMinusA,
                        ElementGroup.HeadContent
                    ), ElementGroupBits(
                        ElementGroup.Block, ElementGroup.Inline,
                        ElementGroup.ParamElement
                    ), ScopeBits(
                        CloseTagScope.Common, CloseTagScope.Button,
                        CloseTagScope.ListItem
                    ));
                DefineElement(
                    "ol", false, ElementGroupBits(
                        ElementGroup.Block
                    ), ElementGroupBits(
                        ElementGroup.LiElement
                    ),
                    li,
                    ScopeBits(CloseTagScope.ListItem));
                DefineElement(
                    "optgroup", false, ElementGroupBits(
                        ElementGroup.OptionsElement
                    ), ElementGroupBits(
                        ElementGroup.OptionsElement
                    ));
                DefineElement(
                    "option", false, ElementGroupBits(
                        ElementGroup.OptionsElement, ElementGroup.OptionElement
                    ), ElementGroupBits(
                        ElementGroup.CharacterData
                    ));
                DefineElement(
                    "p", false, ElementGroupBits(
                        ElementGroup.Block, ElementGroup.PElement
                    ), ElementGroupBits(
                        ElementGroup.Inline, ElementGroup.TableElement
                    ));
                DefineElement(
                    "param", false, ElementGroupBits(ElementGroup.ParamElement), 0);
                DefineElement(
                    "pre", false, ElementGroupBits(
                        ElementGroup.Block
                    ), ElementGroupBits(
                        ElementGroup.Inline
                    ));
                DefineElement(
                    "q", true, ElementGroupBits(
                        ElementGroup.Inline, ElementGroup.InlineMinusA
                    ), ElementGroupBits(
                        ElementGroup.Inline
                    ));
                DefineElement(
                    "s", true, ElementGroupBits(
                        ElementGroup.Inline, ElementGroup.InlineMinusA
                    ), ElementGroupBits(
                        ElementGroup.Inline
                    ));
                DefineElement(
                    "samp", true, ElementGroupBits(
                        ElementGroup.Inline, ElementGroup.InlineMinusA
                    ), ElementGroupBits(
                        ElementGroup.Inline
                    ));
                DefineElement(
                    "script", false, ElementGroupBits(
                        ElementGroup.Block, ElementGroup.Inline,
                        ElementGroup.InlineMinusA, ElementGroup.Mixed,
                        ElementGroup.TableContent, ElementGroup.HeadContent,
                        ElementGroup.TopContent, ElementGroup.AreaElement,
                        ElementGroup.FormElement, ElementGroup.LegendElement,
                        ElementGroup.LiElement, ElementGroup.DlPart,
                        ElementGroup.PElement, ElementGroup.OptionsElement,
                        ElementGroup.OptionElement, ElementGroup.ParamElement,
                        ElementGroup.TableElement, ElementGroup.TrElement,
                        ElementGroup.TdElement, ElementGroup.ColElement
                    ), ElementGroupBits(
                        ElementGroup.CharacterData));
                DefineElement(
                    "select", false, ElementGroupBits(
                        ElementGroup.Inline
                    ), ElementGroupBits(
                        ElementGroup.OptionsElement
                    ));
                DefineElement(
                    "small", true, ElementGroupBits(
                        ElementGroup.Inline, ElementGroup.InlineMinusA
                    ), ElementGroupBits(
                        ElementGroup.Inline
                    ));
                DefineElement(
                    "span", false, ElementGroupBits(
                        ElementGroup.Inline, ElementGroup.InlineMinusA
                    ), ElementGroupBits(
                        ElementGroup.Inline
                    ));
                DefineElement(
                    "strike", true, ElementGroupBits(
                        ElementGroup.Inline, ElementGroup.InlineMinusA
                    ), ElementGroupBits(
                        ElementGroup.Inline
                    ));
                DefineElement(
                    "strong", true, ElementGroupBits(
                        ElementGroup.Inline, ElementGroup.InlineMinusA
                    ), ElementGroupBits(
                        ElementGroup.Inline
                    ));
                DefineElement(
                    "style", false, ElementGroupBits(
                        ElementGroup.Inline, ElementGroup.HeadContent
                    ), ElementGroupBits(
                        ElementGroup.CharacterData
                    ));
                DefineElement(
                    "sub", true, ElementGroupBits(
                        ElementGroup.Inline, ElementGroup.InlineMinusA
                    ), ElementGroupBits(
                        ElementGroup.Inline
                    ));
                DefineElement(
                    "sup", true, ElementGroupBits(
                        ElementGroup.Inline, ElementGroup.InlineMinusA
                    ), ElementGroupBits(
                        ElementGroup.Inline
                    ));
                DefineElement(
                    "table", false, ElementGroupBits(
                        ElementGroup.Block, ElementGroup.TableElement
                    ), ElementGroupBits(
                        ElementGroup.TableContent, ElementGroup.FormElement
                    ), (int)CloseTagScope.All);
                DefineElement(
                    "tbody", false, ElementGroupBits(
                        ElementGroup.TableContent
                    ), ElementGroupBits(
                        ElementGroup.TrElement
                    ));
                ElementContainmentInfo td = DefineElement(
                    "td", false, ElementGroupBits(
                        ElementGroup.TdElement
                    ), ElementGroupBits(
                        ElementGroup.Block, ElementGroup.Inline
                    ), ScopeBits(
                        CloseTagScope.Common, CloseTagScope.Button,
                        CloseTagScope.ListItem
                    ));
                DefineElement(
                    "textarea", false,
                    // No, a textarea cannot be inside a link.
                    ElementGroupBits(ElementGroup.Inline),
                    ElementGroupBits(ElementGroup.CharacterData));
                DefineElement(
                    "tfoot", false, ElementGroupBits(
                        ElementGroup.TableContent
                    ), ElementGroupBits(
                        ElementGroup.FormElement, ElementGroup.TrElement,
                        ElementGroup.TdElement
                    ));
                DefineElement(
                    "th", false, ElementGroupBits(
                        ElementGroup.TdElement
                    ), ElementGroupBits(
                        ElementGroup.Block, ElementGroup.Inline
                    ), ScopeBits(
                        CloseTagScope.Common, CloseTagScope.Button,
                        CloseTagScope.ListItem
                    ));
                DefineElement(
                    "thead", false, ElementGroupBits(
                        ElementGroup.TableContent
                    ), ElementGroupBits(
                        ElementGroup.FormElement, ElementGroup.TrElement,
                        ElementGroup.TdElement
                    ));
                DefineElement(
                    "title", false, ElementGroupBits(ElementGroup.HeadContent),
                    ElementGroupBits(ElementGroup.CharacterData));
                DefineElement(
                    "tr", false, ElementGroupBits(
                        ElementGroup.TableContent, ElementGroup.TrElement
                    ), ElementGroupBits(
                        ElementGroup.FormElement, ElementGroup.TdElement
                    ),
                    td);
                DefineElement(
                    "tt", true, ElementGroupBits(
                        ElementGroup.Inline, ElementGroup.InlineMinusA
                    ), ElementGroupBits(
                        ElementGroup.Inline
                    ));
                DefineElement(
                    "u", true, ElementGroupBits(
                        ElementGroup.Inline, ElementGroup.InlineMinusA
                    ), ElementGroupBits(
                        ElementGroup.Inline
                    ));
                DefineElement(
                    "ul", false, ElementGroupBits(
                        ElementGroup.Block
                    ), ElementGroupBits(
                        ElementGroup.LiElement
                    ),
                    li,
                    ScopeBits(CloseTagScope.ListItem));
                DefineElement(
                    "var", false, ElementGroupBits(
                        ElementGroup.Inline, ElementGroup.InlineMinusA
                    ), ElementGroupBits(
                        ElementGroup.Inline
                    ));
                DefineElement(
                    "video", false, ElementGroupBits(
                        ElementGroup.Inline, ElementGroup.InlineMinusA
                    ), 0);
                DefineElement(
                    "wbr", false, ElementGroupBits(
                        ElementGroup.Inline, ElementGroup.InlineMinusA
                    ), 0);
                DefineElement(
                    "xmp", false, ElementGroupBits(
                        ElementGroup.Block
                    ), ElementGroupBits(
                        ElementGroup.Inline
                    ));

            }

            internal static readonly ElementContainmentInfo CharacterDataOnly
                = new ElementContainmentInfo(
                    "#text", false,
                    ElementGroupBits(
                        ElementGroup.Inline, ElementGroup.InlineMinusA,
                        ElementGroup.Block, ElementGroup.CharacterData),
                    0, null, 0);
        }

        internal static bool AllowsPlainTextualContent(String canonElementName)
        {
            ElementContainmentInfo elInfo;
            if (!ElementContainmentRelationships.TryGetValue(canonElementName, out elInfo)
                || ((elInfo.Contents
                     & ElementContainmentRelationshipRegistry.CharacterDataOnly.Types)
                    != 0))
            {
                switch (HtmlTextEscapingModeExtensions.getModeForTag(canonElementName))
                {
                    case HtmlTextEscapingMode.PCDATA: return true;
                    case HtmlTextEscapingMode.RCDATA: return true;
                    case HtmlTextEscapingMode.PLAIN_TEXT: return true;
                    case HtmlTextEscapingMode.VOID: return false;
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
