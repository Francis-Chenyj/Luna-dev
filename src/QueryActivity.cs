using System;
using System.Threading;

namespace Luna {
    internal sealed class QueryActivity {
        long LastQueryTicks;
        internal void YieldMaintenance(CancellationToken token) {
            int delay = DateTime.UtcNow.Ticks - Interlocked.Read(ref LastQueryTicks) < TimeSpan.FromMilliseconds(400).Ticks ? 80 : 8;
            if (token.WaitHandle.WaitOne(delay)) token.ThrowIfCancellationRequested();
        }
        internal void Touch() { Interlocked.Exchange(ref LastQueryTicks, DateTime.UtcNow.Ticks); }
        internal long LastQuery { get { return Interlocked.Read(ref LastQueryTicks); } }
    }
}
