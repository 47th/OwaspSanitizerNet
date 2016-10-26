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

using System.Collections.Generic;
using System.Linq;

namespace OwaspSanitizerNet.Html
{  
    /**
    * A policy that can be applied to an element to decide whether or not to
    * allow it in the output, possibly after transforming attributes.
    * <p>
    * Element policies are applied <strong>after</strong>
    * {@link AttributePolicy attribute policies} so
    * they can be used to add extra attributes.
    *
    * @author Mike Samuel (mikesamuel@gmail.com)
    * @see HtmlPolicyBuilder#allowElements(ElementPolicy, String...)
    */
    [TCB]
    public interface ElementPolicy
    {
        /**
        * @param elementName the lower-case element name.
        * @param attrs a list of alternating attribute names and values.
        *    The list may be added to or removed from.  When removing, be
        *    careful to remove both the name and its associated value.
        *
        * @return {@code null} to disallow the element, or the adjusted element name.
        */
        string apply(string elementName, List<string> attrs);
    }

    /** Utilities for working with element policies. */
    public sealed class ElementPolicyUtil
    {
        private ElementPolicyUtil() { /* uninstantiable */ }

        /**
        * Given zero or more element policies, returns an element policy equivalent
        * to applying them in order failing early if any of them fails.
        */
        public static ElementPolicy join(params ElementPolicy[] policies)
        {
            PolicyJoiner joiner = new PolicyJoiner();
            foreach (ElementPolicy p in policies)
            {
                if (p != null)
                {
                    joiner.unroll(p);
                }
            }
            return joiner.join();
        }

        internal sealed class PolicyJoiner : JoinHelper<ElementPolicy, JoinableElementPolicy>
        {
            public PolicyJoiner()
                : base(typeof(ElementPolicy), typeof(JoinableElementPolicy),
                    new REJECT_ALL_ELEMENT_POLICY(), new IDENTITY_ELEMENT_POLICY())
            {
            }

            public override IEnumerable<ElementPolicy> split(ElementPolicy x) {
                if (x is JoinedElementPolicy)
                {
                    return ((JoinedElementPolicy) x).policies;
                }
                return null;
            }

            public override ElementPolicy rejoin(HashSet<ElementPolicy> xs)
            {
                return new JoinedElementPolicy(xs);
            }
        }
    }

    /** An element policy that returns the element unchanged. */
    public sealed class IDENTITY_ELEMENT_POLICY : ElementPolicy
    {
        public string apply(string elementName, List<string> attrs)
        {
            return elementName;
        }
    }

    /** An element policy that rejects all elements. */
    public sealed class REJECT_ALL_ELEMENT_POLICY : ElementPolicy
    {
        public string apply(string elementName, List<string> attrs)
        {
            return null;
        }
    }

    internal interface JoinableElementPolicy : ElementPolicy, Joinable<JoinableElementPolicy>
    {
        // Parameterized appropriately.
    }

    internal sealed class JoinedElementPolicy : ElementPolicy
    {
        public readonly List<ElementPolicy> policies;

        public JoinedElementPolicy(IEnumerable<ElementPolicy> policies)
        {
            this.policies = policies.ToList();
        }

        public string apply(string elementName, List<string> attrs)
        {
            string filteredElementName = elementName;
            foreach (ElementPolicy part in policies)
            {
                filteredElementName = part.apply(filteredElementName, attrs);
                if (filteredElementName == null) { break; }
            }
            return filteredElementName;
        }
    }
}
