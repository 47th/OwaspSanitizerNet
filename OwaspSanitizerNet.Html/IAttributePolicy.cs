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
using System.ComponentModel;

namespace OwaspSanitizerNet
{
    /**
     * A policy that can be applied to an HTML attribute to decide whether or not to
     * allow it in the output, possibly after transforming its value.
     *
     * @author Mike Samuel <mikesamuel@gmail.com>
     * @see HtmlPolicyBuilder.AttributeBuilder#matching(AttributePolicy)
     */
    [TCB]
    public interface IAttributePolicy
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
        String apply(
            String elementName, String attributeName, String value);
    }

    internal static class AttributePolicy
    {
        public static IAttributePolicy IDENTITY_ATTRIBUTE_POLICY
            = new IdentityAttributePolicyImpl();

        private class IdentityAttributePolicyImpl : IAttributePolicy
        {
            public String apply(
                String elementName, String attributeName, String value)
            {
                return value;
            }
        }

        public static IAttributePolicy REJECT_ALL_ATTRIBUTE_POLICY
            = new RejectAllAttributePolicyImpl();

        private class RejectAllAttributePolicyImpl : IAttributePolicy
        {
            public String apply(
                String elementName, String attributeName, String value)
            {
                return null;
            }
        }

        /** Utilities for working with attribute policies. */
        internal static class Util
        {

            /**
             * An attribute policy equivalent to applying all the given policies in
             * order, failing early if any of them fails.
             */
            public static IAttributePolicy join(params IAttributePolicy[] policies)
            {

                var pu = new PolicyJoiner();
                foreach (IAttributePolicy policy in policies)
                {
                    if (policy == null) { continue; }
                    pu.join(policy);
                }
                return pu.Result ?? IDENTITY_ATTRIBUTE_POLICY;
            }

            private class PolicyJoiner
            {
                IAttributePolicy _last;
                IAttributePolicy _result;

                internal void join(IAttributePolicy p)
                {
                    if (REJECT_ALL_ATTRIBUTE_POLICY.Equals(p))
                    {
                        _result = p;
                    }
                    else if (!REJECT_ALL_ATTRIBUTE_POLICY.Equals(_result))
                    {
                        var joinedAttributePolicy = p as JoinedAttributePolicy;
                        if (joinedAttributePolicy != null)
                        {
                            join(joinedAttributePolicy.First);
                            join(joinedAttributePolicy.Second);
                        }
                        else if (p != _last)
                        {
                            _last = p;
                            if (_result == null || IDENTITY_ATTRIBUTE_POLICY.Equals(_result))
                            {
                                _result = p;
                            }
                            else if (!IDENTITY_ATTRIBUTE_POLICY.Equals(p))
                            {
                                _result = new JoinedAttributePolicy(_result, p);
                            }
                        }
                    }
                }

                internal IAttributePolicy Result
                {
                    get { return _result; }
                }
            }
        }
    }

    [ImmutableObject(true)]
    internal sealed class JoinedAttributePolicy : IAttributePolicy
    {
        internal readonly IAttributePolicy First, Second;

        internal JoinedAttributePolicy(IAttributePolicy first, IAttributePolicy second)
        {
            First = first;
            Second = second;
        }

        public String apply(
            String elementName, String attributeName, String value)
        {
            value = First.apply(elementName, attributeName, value);
            return value != null
                ? Second.apply(elementName, attributeName, value) : null;
        }
    }

}
