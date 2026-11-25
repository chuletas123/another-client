using System;

namespace AnotherClient.Player
{
    // Pure decisions, separated from Win32 and WPF for tests without a desktop.
    internal struct StartupWindowBounds
    {
        internal int Left, Top, Right, Bottom;
        internal bool IsValid { get { return Right > Left && Bottom > Top; } }
        internal bool Matches(StartupWindowBounds other)
        {
            return IsValid && other.IsValid && Math.Abs((long)Left - other.Left) <= 2 &&
                Math.Abs((long)Top - other.Top) <= 2 && Math.Abs((long)Right - other.Right) <= 2 && Math.Abs((long)Bottom - other.Bottom) <= 2;
        }
    }
    internal sealed class StartupForegroundState
    {
        internal bool IsVisible, IsMinimized, IsOwnWindow, IsDesktop, HasCaption, IsMaximized, IsExclusiveFullscreen;
        internal StartupWindowBounds Window, Monitor, VirtualDesktop;
    }
    internal interface IStartupDesktopObserver : IDisposable
    {
        event EventHandler Changed;
        StartupForegroundState Capture();
        void Start();
        void Stop();
    }
    internal interface IStartupPresentation
    {
        void Present(bool activate);
        void Suppress();
    }
    internal sealed class StartupWindowPriority : IDisposable
    {
        private readonly IStartupDesktopObserver desktop;
        private readonly IStartupPresentation presentation;
        private bool requested, disposed, hasPresented, deferred;
        internal StartupWindowPriority(IStartupDesktopObserver observer, IStartupPresentation view)
        {
            desktop = observer; presentation = view;
        }
        internal static bool IsFullscreen(StartupForegroundState state)
        {
            if (state == null || !state.IsVisible || state.IsMinimized || state.IsOwnWindow || state.IsDesktop) return false;
            if (state.IsExclusiveFullscreen) return true;
            // A normal maximized window is not fullscreen, including auto-hide
            // taskbars. F11/borderless windows normally remove the caption.
            if (state.HasCaption && state.IsMaximized) return false;
            return state.Window.Matches(state.Monitor) || state.Window.Matches(state.VirtualDesktop);
        }
        internal void Start()
        {
            if (disposed || requested) return;
            requested = true;
            desktop.Changed += OnChanged; desktop.Start(); Refresh();
        }
        private void OnChanged(object sender, EventArgs e) { Refresh(); }
        internal void Refresh()
        {
            if (!requested || disposed) return;
            if (IsFullscreen(desktop.Capture())) { deferred = true; presentation.Suppress(); return; }
            presentation.Present(!hasPresented && !deferred); hasPresented = true;
        }
        internal void Stop()
        {
            if (!requested) return;
            requested = false; desktop.Changed -= OnChanged; desktop.Stop(); presentation.Suppress();
        }
        public void Dispose()
        {
            if (disposed) return;
            Stop(); disposed = true; desktop.Dispose();
        }
    }
}
