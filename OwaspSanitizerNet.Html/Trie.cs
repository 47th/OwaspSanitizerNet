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
using System.Linq;
using System.Text;

namespace OwaspSanitizerNet
{
    /**
     * A trie used to separate punctuation tokens in a run of non-whitespace
     * characters by preferring the longest punctuation string possible in a
     * greedy left-to-right scan.
     *
     * @author Mike Samuel <mikesamuel@gmail.com>
     */
    internal sealed class Trie
    {
        private static readonly char[] ZeroChars = new char[0];
        private static readonly Trie[] ZeroTries = new Trie[0];

        private readonly char[] _childMap;
        private readonly Trie[] _children;
        private readonly bool _terminal;
        private readonly int _value;

        /**
         * @param elements not empty, non null.
         */
        public Trie(Dictionary<String, int> elements)
            : this(SortedUniqEntries(elements), 0)
        {
        }

        private Trie(List<KeyValuePair<String, int>> elements, int depth)
            : this(elements, depth, 0, elements.Count)
        {
        }

        /**
         * @param elements not empty, non null.  Not modified.
         * @param depth the depth in the tree.
         * @param start an index into punctuationStrings of the first string in this
         *   subtree.
         * @param end an index into punctuationStrings past the last string in this
         *   subtree.
         */
        private Trie(
            List<KeyValuePair<String, int>> elements, int depth,
            int start, int end)
        {
            _terminal = depth == elements[start].Key.Length;
            if (_terminal)
            {
                _value = elements[start].Value;
                if (start + 1 == end)
                {  // base case
                    _childMap = ZeroChars;
                    _children = ZeroTries;
                    return;
                }
                ++start;
            }
            else
            {
                _value = int.MaxValue;
            }
            int childCount = 0;
            {
                int last = -1;
                for (int i = start; i < end; ++i)
                {
                    char ch = elements[i].Key[depth];
                    if (ch != last)
                    {
                        ++childCount;
                        last = ch;
                    }
                }
            }
            _childMap = new char[childCount];
            _children = new Trie[childCount];
            int childStart = start;
            int childIndex = 0;
            char lastCh = elements[start].Key[depth];
            for (int i = start + 1; i < end; ++i)
            {
                char ch = elements[i].Key[depth];
                if (ch != lastCh)
                {
                    _childMap[childIndex] = lastCh;
                    _children[childIndex++] = new Trie(
                      elements, depth + 1, childStart, i);
                    childStart = i;
                    lastCh = ch;
                }
            }
            _childMap[childIndex] = lastCh;
            _children[childIndex + 1] = new Trie(elements, depth + 1, childStart, end);
        }

        /** Does this node correspond to a complete string in the input set. */
        public bool IsTerminal
        {
            get { return _terminal; }
        }

        public int Value
        {
            get { return _value; }
        }

        /**
         * The child corresponding to the given character.
         * @return null if no such trie.
         */
        public Trie Lookup(char ch)
        {
            int i = Array.BinarySearch(_childMap, ch);
            return i >= 0 ? _children[i] : null;
        }

        /**
         * The descendant of this trie corresponding to the string for this trie
         * appended with s.
         * @param s non null.
         * @return null if no such trie.
         */
        public Trie Lookup(char[] s)
        {
            Trie t = this;
            for (int i = 0, n = s.Length; i < n; ++i)
            {
                t = t.Lookup(s[i]);
                if (null == t) { break; }
            }
            return t;
        }

        public bool Contains(char ch)
        {
            return Array.BinarySearch(_childMap, ch) >= 0;
        }

        private static List<KeyValuePair<String, T>> SortedUniqEntries<T>(
            Dictionary<String, T> m)
        {
            return new List<KeyValuePair<String, T>>(
                m.OrderBy(kv => kv.Key));
        }

        /**
         * Append all strings s such that {@code this.lookup(s).isTerminal()} to the
         * given list in lexical order.
         */
        public void ToStringList(List<String> strings)
        {
            ToStringList("", strings);
        }

        private void ToStringList(String prefix, List<String> strings)
        {
            if (_terminal) { strings.Add(prefix); }
            for (int i = 0, n = _childMap.Length; i < n; ++i)
            {
                _children[i].ToStringList(prefix + _childMap[i], strings);
            }
        }

        public override String ToString()
        {
            var sb = new StringBuilder();
            ToStringBuilder(0, sb);
            return sb.ToString();
        }

        private void ToStringBuilder(int depth, StringBuilder sb)
        {
            sb.Append(_terminal ? "terminal" : "nonterminal");
            ++depth;
            for (int i = 0; i < _childMap.Length; ++i)
            {
                sb.Append('\n');
                for (int d = 0; d < depth; ++d)
                {
                    sb.Append('\t');
                }
                sb.Append('\'').Append(_childMap[i]).Append("' ");
                _children[i].ToStringBuilder(depth, sb);
            }
        }
    }

}
