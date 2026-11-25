using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Threading;

namespace AnotherClient.Player
{
    // Read-only foreground notifications. No input hooks, injected DLLs, process
    // access, titles, screenshots or changes to any other application's window.
    internal sealed class StartupDesktopObserver : IStartupDesktopObserver
    {
        private readonly Dispatcher dispatcher;
        private readonly WinEventProc callback;
        private readonly IntPtr[] hooks = new IntPtr[3];
        private DispatcherTimer verification;
        private bool? lastFullscreen;
        private bool running, queued, disposed;
        private int generation;
        public event EventHandler Changed;
        internal StartupDesktopObserver(Dispatcher uiDispatcher) { dispatcher = uiDispatcher; callback = OnWinEvent; }
        public void Start()
        {
            if (running || disposed) return;
            running = true; generation++;
            hooks[0] = SetWinEventHook(3, 3, IntPtr.Zero, callback, 0, 0, 2); // Foreground; skip our process; out of context.
            hooks[1] = SetWinEventHook(0x800a, 0x800b, IntPtr.Zero, callback, 0, 0, 2); // Window state/location.
            hooks[2] = SetWinEventHook(0xb, 0xb, IntPtr.Zero, callback, 0, 0, 2); // Move/resize ended.
            // The shell has no notification for every exclusive-fullscreen mode
            // change. This low-frequency check only runs during startup and
            // notifies the UI if fullscreen status actually changes.
            verification = new DispatcherTimer(DispatcherPriority.Background, dispatcher) { Interval = TimeSpan.FromMilliseconds(500) };
            verification.Tick += OnVerification; verification.Start();
        }
        private void OnVerification(object sender, EventArgs e)
        {
            bool? previous = lastFullscreen;
            Capture();
            if (previous != lastFullscreen) QueueChange();
        }
        private void OnWinEvent(IntPtr hook, uint kind, IntPtr window, int objectId, int childId, uint thread, uint time)
        {
            if (!running || disposed) return;
            if (kind != 3 && (objectId != 0 || childId != 0 || window != GetForegroundWindow())) return;
            QueueChange();
        }
        private void QueueChange()
        {
            if (!running || disposed || queued || dispatcher.HasShutdownStarted) return;
            queued = true; int ticket = generation;
            dispatcher.BeginInvoke(DispatcherPriority.Send, new Action(delegate {
                if (ticket != generation) return;
                queued = false;
                if (!running || disposed) return;
                var handler = Changed; if (handler != null) handler(this, EventArgs.Empty);
            }));
        }
        public StartupForegroundState Capture()
        {
            var state = ReadForeground(); lastFullscreen = StartupWindowPriority.IsFullscreen(state); return state;
        }
        private StartupForegroundState ReadForeground()
        {
            var state = new StartupForegroundState();
            IntPtr window = GetForegroundWindow();
            if (window == IntPtr.Zero) return state;
            uint pid; GetWindowThreadProcessId(window, out pid);
            state.IsOwnWindow = pid == GetCurrentProcessId();
            state.IsVisible = IsWindowVisible(window); state.IsMinimized = IsIconic(window);
            if (state.IsOwnWindow || !state.IsVisible || state.IsMinimized) return state;
            var name = new StringBuilder(64); GetClassName(window, name, name.Capacity);
            string kind = name.ToString();
            state.IsDesktop = kind == "Progman" || kind == "WorkerW" || kind == "Shell_TrayWnd" || kind == "Shell_SecondaryTrayWnd";
            if (state.IsDesktop) return state;
            state.HasCaption = (GetWindowLongPtr(window, -16).ToInt64() & 0x00c00000L) == 0x00c00000L;
            state.IsMaximized = IsZoomed(window);
            NativeRect rectangle;
            if (DwmGetWindowAttribute(window, 9, out rectangle, Marshal.SizeOf(typeof(NativeRect))) != 0 && !GetWindowRect(window, out rectangle)) return state;
            state.Window = rectangle.Bounds;
            var monitor = new MonitorInfo { Size = Marshal.SizeOf(typeof(MonitorInfo)) };
            if (GetMonitorInfo(MonitorFromWindow(window, 2), ref monitor)) state.Monitor = monitor.Monitor.Bounds;
            int left = GetSystemMetrics(76), top = GetSystemMetrics(77);
            state.VirtualDesktop = new StartupWindowBounds { Left = left, Top = top, Right = left + GetSystemMetrics(78), Bottom = top + GetSystemMetrics(79) };
            int notification;
            state.IsExclusiveFullscreen = SHQueryUserNotificationState(out notification) == 0 && notification == 3;
            return state;
        }
        public void Stop()
        {
            running = false; generation++; queued = false;
            for (int i = 0; i < hooks.Length; i++) if (hooks[i] != IntPtr.Zero) { UnhookWinEvent(hooks[i]); hooks[i] = IntPtr.Zero; }
            if (verification != null) { verification.Stop(); verification.Tick -= OnVerification; verification = null; }
            lastFullscreen = null;
        }
        public void Dispose() { if (disposed) return; Stop(); disposed = true; }
        [StructLayout(LayoutKind.Sequential)] private struct NativeRect {
            internal int Left, Top, Right, Bottom;
            internal StartupWindowBounds Bounds { get { return new StartupWindowBounds { Left = Left, Top = Top, Right = Right, Bottom = Bottom }; } }
        }
        [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { internal int Size; internal NativeRect Monitor, Work; internal uint Flags; }
        private delegate void WinEventProc(IntPtr hook, uint kind, IntPtr window, int objectId, int childId, uint thread, uint time);
        [DllImport("user32.dll")] private static extern IntPtr SetWinEventHook(uint first, uint last, IntPtr module, WinEventProc callback, uint pid, uint tid, uint flags);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool UnhookWinEvent(IntPtr hook);
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint pid);
        [DllImport("kernel32.dll")] private static extern uint GetCurrentProcessId();
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsIconic(IntPtr window);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsZoomed(IntPtr window);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, StringBuilder name, int length);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetWindowRect(IntPtr window, out NativeRect rectangle);
        [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out NativeRect rectangle, int size);
        [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
        [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
        [DllImport("shell32.dll")] private static extern int SHQueryUserNotificationState(out int state);
    }

    internal static class StartupWindowZOrder
    {
        internal static void RaisePair(IntPtr background, IntPtr content)
        {
            const uint flags = 0x0001 | 0x0002 | 0x0010; // No size, no move, no activation.
            IntPtr topmost = new IntPtr(-1);
            IntPtr batch = BeginDeferWindowPos(background == IntPtr.Zero ? 1 : 2);
            if (batch != IntPtr.Zero) {
                if (background != IntPtr.Zero) batch = DeferWindowPos(batch, background, topmost, 0, 0, 0, 0, flags);
                if (batch != IntPtr.Zero) batch = DeferWindowPos(batch, content, topmost, 0, 0, 0, 0, flags);
                if (batch != IntPtr.Zero && EndDeferWindowPos(batch)) return;
            }
            if (background != IntPtr.Zero) SetWindowPos(background, topmost, 0, 0, 0, 0, flags);
            SetWindowPos(content, topmost, 0, 0, 0, 0, flags);
        }
        [DllImport("user32.dll")] private static extern IntPtr BeginDeferWindowPos(int count);
        [DllImport("user32.dll")] private static extern IntPtr DeferWindowPos(IntPtr batch, IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool EndDeferWindowPos(IntPtr batch);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
    }
}
