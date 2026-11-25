using System;

namespace AnotherClient.Player
{
    // One pending value instead of a UI callback for each verified file.
    // This throttles painting only, never the actual startup work.
    internal sealed class StartupProgressRelay : IProgress<StartupProgress>, IDisposable
    {
        private readonly object gate = new object();
        private readonly Action<StartupProgress> render;
        private readonly System.Windows.Threading.DispatcherTimer refresh;
        private StartupProgress pending;
        private int maximum;
        private bool disposed;

        internal StartupProgressRelay(Action<StartupProgress> renderProgress)
        {
            render = renderProgress;
            refresh = new System.Windows.Threading.DispatcherTimer(System.Windows.Threading.DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(60) };
            refresh.Tick += OnTick;
            refresh.Start();
        }
        public void Report(StartupProgress value)
        {
            if (value == null) return;
            lock (gate) {
                if (disposed) return;
                maximum = Math.Max(maximum, Math.Min(100, Math.Max(0, value.Percent)));
                pending = value;
            }
        }
        internal void Flush()
        {
            StartupProgress value; int percent;
            lock (gate) { value = pending; pending = null; percent = maximum; }
            if (value != null) render(new StartupProgress(value.Message, percent, value.Indeterminate));
        }
        private void OnTick(object sender, EventArgs e) { Flush(); }
        public void Dispose()
        {
            lock (gate) { if (disposed) return; disposed = true; }
            refresh.Stop(); refresh.Tick -= OnTick; Flush();
        }
    }
}
