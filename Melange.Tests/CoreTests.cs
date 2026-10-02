using System;
using System.Collections.Generic;
using Melange.Core;
using Xunit;

namespace Melange.Tests
{
    public sealed class EventsTests
    {
        private sealed class Ping { public int N; }
        private sealed class Other { }

        [Fact]
        public void HandlersGetMessagesOfTheirTypeOnly()
        {
            var got = new List<int>();
            using (Events.Subscribe<Ping>(p => got.Add(p.N)))
            {
                Events.Publish(new Ping { N = 1 });
                Events.Publish(new Other());
                Events.Publish(new Ping { N = 2 });
            }
            Assert.Equal(new[] { 1, 2 }, got);
        }

        [Fact]
        public void DisposingTheSubscriptionStopsIt()
        {
            int calls = 0;
            var sub = Events.Subscribe<Ping>(_ => calls++);
            Events.Publish(new Ping());
            sub.Dispose();
            sub.Dispose();                                   // twice is harmless
            Events.Publish(new Ping());
            Assert.Equal(1, calls);
        }

        [Fact]
        public void AThrowingHandlerIsReportedAndTheOthersStillRun()
        {
            var errors = new List<string>();
            var previous = Events.OnHandlerError;
            Events.OnHandlerError = errors.Add;
            int later = 0;
            try
            {
                using (Events.Subscribe<Ping>(_ => throw new InvalidOperationException("boom")))
                using (Events.Subscribe<Ping>(_ => later++))
                    Events.Publish(new Ping());
            }
            finally { Events.OnHandlerError = previous; }
            Assert.Equal(1, later);
            Assert.Single(errors);
            Assert.Contains("boom", errors[0]);
        }

        [Fact]
        public void AHandlerCanUnsubscribeWhileBeingCalled()
        {
            int calls = 0;
            IDisposable sub = null;
            sub = Events.Subscribe<Ping>(_ => { calls++; sub.Dispose(); });
            Events.Publish(new Ping());
            Events.Publish(new Ping());
            Assert.Equal(1, calls);
        }

        [Fact]
        public void HandlersRunInSubscriptionOrder()
        {
            var order = new List<string>();
            using (Events.Subscribe<Ping>(_ => order.Add("a")))
            using (Events.Subscribe<Ping>(_ => order.Add("b")))
                Events.Publish(new Ping());
            Assert.Equal(new[] { "a", "b" }, order);
        }
    }

    public sealed class VersioningTests
    {
        [Theory]
        [InlineData("0.1.0", "0.1.0", true)]
        [InlineData("0.2.0", "0.1.0", true)]   // a newer minor keeps what an older spoke needs
        [InlineData("0.1.0", "0.2.0", false)]  // the spoke needs something this hub doesn't have yet
        [InlineData("1.0.0", "0.1.0", false)]  // a major change may break a spoke
        [InlineData("0.1.5", "0.1.0", true)]
        public void SameMajorAndAtLeastTheMinor(string hub, string spoke, bool ok)
        {
            Assert.Equal(ok, Versioning.IsCompatible(Version.Parse(hub), Version.Parse(spoke), out string problem));
            Assert.Equal(ok, problem == null);
        }
    }
}
