using System;
using System.Threading;
using Luna;

// Only the fixture owns virtual time. No service fields are inspected or changed.
internal sealed class TestClock : IRuntimeClock {
    readonly object sync = new object();
    DateTime now = DateTime.UtcNow;
    public DateTime UtcNow { get { lock (sync) return now; } }
    internal void Advance(TimeSpan duration) { lock (sync) now = now.Add(duration); }
    public void WaitUntil(DateTime due, CancellationToken token) {
        token.ThrowIfCancellationRequested();
        lock (sync) if (due > now) now = due;
        token.ThrowIfCancellationRequested();
    }
}
