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
    * A policy that can be applied to an HTML attribute to decide whether or not to
    * allow it in the output, possibly after transforming its value.
    *
    * @author Mike Samuel (mikesamuel@gmail.com)
    * @see HtmlPolicyBuilder.AttributeBuilder#matching(AttributePolicy)
    */
    [TCB]
    public interface AttributePolicy
    {

        /**
        * @param elementName the lower-case element name.
        * @param attributeName the lower-case attribute name.
        * @param value the attribute value without quotes and with HTML entities
        *     decoded.
        *
        * @return {@code null} to disallow the attribute or the adjusted value if
        *     allowed.
        */
        string apply(
            string elementName, string attributeName, string value);      
    }


    /** An attribute policy that returns the value unchanged. */
    internal sealed class IDENTITY_ATTRIBUTE_POLICY : AttributePolicy
    {
        public static readonly IDENTITY_ATTRIBUTE_POLICY Instance = new IDENTITY_ATTRIBUTE_POLICY();

        public string apply(
            string elementName, string attributeName, string value)
        {
            return value;
        }
    }

    /** An attribute policy that rejects all values. */
    internal sealed class REJECT_ALL_ATTRIBUTE_POLICY : AttributePolicy
    {
        public static readonly REJECT_ALL_ATTRIBUTE_POLICY Instance = new REJECT_ALL_ATTRIBUTE_POLICY();

        public string apply(
            string elementName, string attributeName, string value)
        {
            return null;
        }
    };

    /** Utilities for working with attribute policies. */
    internal static class AttributePolicyUtil
    {
        /**
        * An attribute policy equivalent to applying all the given policies in
        * order, failing early if any of them fails.
        */
        public static AttributePolicy join(params AttributePolicy[] policies)
        {
            AttributePolicyJoiner joiner = new AttributePolicyJoiner();

            foreach (AttributePolicy p in policies)
            {
                if (p != null)
                {
                    joiner.unroll(p);
                }
            }

            return joiner.join();
        }

        private sealed class AttributePolicyJoiner : JoinHelper<AttributePolicy, JoinableAttributePolicy>
        {
            public AttributePolicyJoiner()
                : base(typeof(AttributePolicy),
                    typeof(JoinableAttributePolicy),
                    REJECT_ALL_ATTRIBUTE_POLICY.Instance,
                    IDENTITY_ATTRIBUTE_POLICY.Instance)
            {
            }

            public override IEnumerable<AttributePolicy> split(AttributePolicy x)
            {
                if (x is JoinedAttributePolicy)
                {
                    return ((JoinedAttributePolicy) x).policies;
                }
                else
                {
                    return null;
                }
            }

            public override AttributePolicy rejoin(HashSet<AttributePolicy> xs)
            {
                return new JoinedAttributePolicy(xs.ToArray());
            }
        }
    }

        //@Immutable
    internal sealed class JoinedAttributePolicy : AttributePolicy
    {
        internal readonly IReadOnlyList<AttributePolicy> policies;

        public JoinedAttributePolicy(IReadOnlyList<AttributePolicy> policies)
        {
            this.policies = policies;
        }

        public string apply(
            string elementName, string attributeName, string rawValue)
        {
            string value = rawValue;
            foreach (AttributePolicy p in policies)
            {
                if (value == null) { break; }
                value = p.apply(elementName, attributeName, value);
            }
            return value;
        }

        public override bool Equals(object o)
        {
            return o != null && this.GetType() == o.GetType()
                && policies.SequenceEqual(((JoinedAttributePolicy) o).policies);
        }

        public override int GetHashCode()
        {
            return policies.Aggregate(0, (h, p) => h * 397 ^ p.GetHashCode());
        }
    }

    internal interface JoinableAttributePolicy : AttributePolicy, Joinable<JoinableAttributePolicy>
    {
        // Parameterized Appropriately.
    }
}