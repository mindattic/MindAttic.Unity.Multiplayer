using System.Collections.Generic;
using Unity.Multiplayer.Core;

namespace Unity.Multiplayer.Tests
{
    /// <summary>Records everything one <see cref="NetSession"/> raises, for assertions.</summary>
    internal sealed class SessionProbe
    {
        public readonly List<SessionStateChange> Changes = new List<SessionStateChange>();
        public readonly List<Disconnection> Ends = new List<Disconnection>();
        public readonly List<ulong> Joined = new List<ulong>();
        public readonly List<ulong> Left = new List<ulong>();
        public int Starts;

        public SessionProbe(NetSession session)
        {
            Session = session;
            session.StateChanged += change => Changes.Add(change);
            session.Started += () => Starts++;
            session.Ended += why => Ends.Add(why);
            session.ClientConnected += id => Joined.Add(id);
            session.ClientDisconnected += id => Left.Add(id);
        }

        public NetSession Session { get; }

        /// <summary>The states visited, in order, starting from the first change's From.</summary>
        public List<SessionState> Path()
        {
            var path = new List<SessionState>();
            for (int i = 0; i < Changes.Count; i++)
            {
                if (i == 0)
                {
                    path.Add(Changes[i].From);
                }

                path.Add(Changes[i].To);
            }

            return path;
        }
    }
}
