using System;
using System.Collections.Generic;

namespace Melange.Core
{
    /// <summary>
    /// A typed event bus: the one way spokes hear about the game (from the hub's patches) and about each other.
    /// Handlers run on the main thread, in subscription order; one that throws is logged and the rest still run.
    /// </summary>
    public static class Events
    {
        private static readonly Dictionary<Type, List<Delegate>> Handlers = new Dictionary<Type, List<Delegate>>();

        /// <summary>Calls <paramref name="handler"/> for every <typeparamref name="T"/> published. Returns the way to stop.</summary>
        public static IDisposable Subscribe<T>(Action<T> handler)
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            if (!Handlers.TryGetValue(typeof(T), out var list)) Handlers[typeof(T)] = list = new List<Delegate>();
            list.Add(handler);
            return new Subscription(() => list.Remove(handler));
        }

        public static void Publish<T>(T message)
        {
            if (!Handlers.TryGetValue(typeof(T), out var list) || list.Count == 0) return;
            foreach (var handler in list.ToArray())          // a handler may unsubscribe while being called
            {
                try { ((Action<T>)handler)(message); }
                catch (Exception e) { Core.Log?.Error($"a {typeof(T).Name} handler threw: {e}"); }
            }
        }

        private sealed class Subscription : IDisposable
        {
            private Action _undo;
            public Subscription(Action undo) => _undo = undo;
            public void Dispose() { _undo?.Invoke(); _undo = null; }
        }
    }
}
