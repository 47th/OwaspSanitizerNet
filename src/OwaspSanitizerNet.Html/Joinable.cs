using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace OwaspSanitizerNet.Html
{
    /**
    * Something that can request special joining.
    * If two or more things have the same (per equals/hashCode) joinStrategy
    * then they will be grouped together for joining according to that strategy.
    */
    internal interface Joinable<T>
    {
        JoinStrategy<T> getJoinStrategy();
    }

    /**
    * An n-ary function from T to a joined T.
    */
    internal interface JoinStrategy<T>
    {
        /** Joins toJoin into a single T. */
        T join(IEnumerable<T> toJoin);

        /**
        * Must be hashable so that special joinables can be grouped by strategy.
        */
        bool Equals(object o);

        /**
        * Must be hashable so that special joinables can be grouped by strategy.
        */
        int GetHashCode();
    }



    internal abstract class JoinHelper<T, SJ>
        where SJ : Joinable<SJ>
    {
        private readonly Type _baseType;
        private readonly Type _specialJoinableType;
        private readonly T _zeroValue;
        private readonly T _identityValue;
        private Dictionary<JoinStrategy<SJ>, HashSet<SJ>> _requireSpecialJoining;
        private HashSet<T> _uniq = new HashSet<T>();

        public JoinHelper(
            Type baseType,
            Type specialJoinableType,
            T zeroValue,
            T identityValue)
        {
             // TODO (SK): enforce baseType is T and specialJoinableType is SJ
            _baseType = baseType;
            _specialJoinableType = specialJoinableType;
            // TODO (SK): contracts?
            // Preconditions.checkNotNull(zeroValue)
            // Preconditions.checkNotNull(identityValue)
            _zeroValue = zeroValue;
            _identityValue = identityValue;
        }

        public abstract IEnumerable<T> split(T x);

        public abstract T rejoin(HashSet<T> xs);

        internal void unroll(T x)
        {
            IEnumerable<T> splitX = split(x);
            if (splitX != null)
            {
                foreach (T part in splitX)
                {
                    unroll(part);
                }
            }
            else if (_specialJoinableType.GetTypeInfo().IsAssignableFrom(x.GetType()))
            {
                // We shouldn't implement special joinable for AttributePolicies
                // without implementing the properly parameterized variant.
                SJ sj = (SJ)Convert.ChangeType(x, _specialJoinableType);

                JoinStrategy<SJ> strategy = sj.getJoinStrategy();

                if (_requireSpecialJoining == null)
                {
                    _requireSpecialJoining = new Dictionary<JoinStrategy<SJ>, HashSet<SJ>>();
                }
                HashSet<SJ> toJoinTogether;
                if (!_requireSpecialJoining.TryGetValue(strategy, out toJoinTogether)) {
                    toJoinTogether = new HashSet<SJ>();
                    _requireSpecialJoining.Add(strategy, toJoinTogether);
                }

                toJoinTogether.Add(sj);
            }
            else
            {
                // TODO (SK): Preconditions.checkNotNull(x)
                _uniq.Add(x);
            }
        }

        internal T join()
        {
            if (_uniq.Contains(_zeroValue))
            {
                return _zeroValue;
            }

            if (_requireSpecialJoining != null)
            {
                IEnumerator<KeyValuePair<JoinStrategy<SJ>, HashSet<SJ>>> enumerator
                    = _requireSpecialJoining.GetEnumerator();
                while (enumerator.MoveNext())
                {
                    KeyValuePair<JoinStrategy<SJ>, HashSet<SJ>> e = enumerator.Current;

                    JoinStrategy<SJ> strategy = e.Key;
                    HashSet<SJ> toJoin = e.Value;

                    SJ joined = toJoin.Count == 1
                        ? toJoin.Single()
                        : strategy.join(toJoin);

                    // TODO (SK): Preconditions.checkNotNull(
                    _uniq.Add((T)Convert.ChangeType(joined, _baseType));
                }
                _requireSpecialJoining.Clear();
            }

            _uniq.Remove(_identityValue);

            switch (_uniq.Count)
            {
                case 0:  return _identityValue;
                case 1:  return _uniq.Single();
                default: return rejoin(_uniq);
            }
        }
    }
}