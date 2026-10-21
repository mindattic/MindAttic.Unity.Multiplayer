using System;
using System.Collections;

namespace Unity.Multiplayer.Testing
{
    /// <summary>
    /// A wait that ends with a value: <c>yield return</c> it from a <c>[UnityTest]</c>, then read <see cref="Result"/>.
    /// It fails the test (NUnit <c>Assert.Fail</c>) with a message saying what never happened if it times out.
    /// </summary>
    /// <example>
    /// <code>
    /// var avatar = WaitForSpawn&lt;Avatar&gt;(Client, owner: Client.LocalClientId);
    /// yield return avatar;
    /// Assert.That(avatar.Result.Score, Is.Zero);
    /// </code>
    /// </example>
    public sealed class Awaiting<T> : IEnumerator
    {
        private readonly IEnumerator _wait;
        private T _result;
        private bool _done;

        internal Awaiting(Func<Action<T>, IEnumerator> wait)
        {
            _wait = wait(value => _result = value);
        }

        /// <summary>The value the wait ended with.</summary>
        /// <exception cref="InvalidOperationException">Read before the wait finished: <c>yield return</c> this first.</exception>
        public T Result
        {
            get
            {
                if (!_done)
                {
                    throw new InvalidOperationException("Awaiting<" + typeof(T).Name + ">.Result was read before the wait finished; yield return it first.");
                }

                return _result;
            }
        }

        /// <summary>True once the wait has finished (and <see cref="Result"/> is set).</summary>
        public bool IsDone
        {
            get { return _done; }
        }

        object IEnumerator.Current
        {
            get { return _wait.Current; }
        }

        bool IEnumerator.MoveNext()
        {
            if (_done)
            {
                return false;
            }

            if (_wait.MoveNext())
            {
                return true;
            }

            _done = true;
            return false;
        }

        void IEnumerator.Reset()
        {
            throw new NotSupportedException();
        }
    }
}
