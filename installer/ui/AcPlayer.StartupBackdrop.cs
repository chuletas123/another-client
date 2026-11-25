using System;
using System.ComponentModel;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Windows.UI.Composition;

namespace AnotherClient.Player
{
    // The compositor owns the live host-backdrop brush. The transparent content
    // window is an owned window above this surface, so DComp never covers WPF's
    // text, icon or Cancel button. Neither window captures or polls the desktop.
    internal sealed class StartupBackdropWindow : Window, IDisposable
    {
        private readonly StartupCard front;
        private HwndSource source;
        private StartupBackdropComposition material;
        private bool canClose;
        internal bool MaterialActive { get { return material != null; } }

        internal StartupBackdropWindow(StartupCard content)
        {
            front = content;
            Title = "Another Client";
            WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false; ShowActivated = false;
            MouseLeftButtonDown += front.DragSurfaceMouseDown;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Background = new SolidColorBrush(Color.FromRgb(32, 40, 42));
            SynchronizeBounds();
            SourceInitialized += delegate {
                source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
                source.AddHook(WindowMessages);
                RefreshMaterial();
            };
            Closing += delegate(object sender, CancelEventArgs e) { if (!canClose) e.Cancel = true; };
            Loaded += delegate { if (source != null) DefWindowProc(source.Handle, 0x86, new IntPtr(1), new IntPtr(-1)); };
        }
        internal void SynchronizeBounds()
        {
            Topmost = front.Topmost;
            if (!Double.IsNaN(front.Left)) Left = front.Left;
            if (!Double.IsNaN(front.Top)) Top = front.Top;
            Width = front.ActualWidth > 0 ? front.ActualWidth : front.Width;
            Height = front.ActualHeight > 0 ? front.ActualHeight : front.Height;
        }
        internal void RefreshMaterial()
        {
            SuspendMaterial();
            if (source == null || canClose) return;
            material = StartupBackdropComposition.TryCreate(source);
            Background = material == null ? new SolidColorBrush(Color.FromRgb(32, 40, 42)) : Brushes.Transparent;
        }
        internal void SuspendMaterial()
        {
            if (material != null) { material.Dispose(); material = null; }
        }
        private IntPtr WindowMessages(IntPtr hwnd, int message, IntPtr wparam, IntPtr lparam, ref bool handled)
        {
            // Keep only the material's non-client appearance active. This does
            // not activate a window or change keyboard focus (WM_ACTIVATE).
            if (message == 0x86 && wparam == IntPtr.Zero) { handled = true; return new IntPtr(1); }
            if (message == 0x112) {
                long command = wparam.ToInt64() & 0xfff0;
                if (command == 0xf020 || command == 0xf030 || (command == 0xf060 && !canClose)) handled = true;
            }
            if (message == 0x31e || message == 0x31a || message == 0x1a) RefreshMaterial();
            return IntPtr.Zero;
        }
        public void Dispose()
        {
            if (canClose) return;
            canClose = true; SuspendMaterial();
            if (source != null) { source.RemoveHook(WindowMessages); source = null; }
            Close();
        }
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr DefWindowProc(IntPtr hwnd, int message, IntPtr wparam, IntPtr lparam);
    }

    internal sealed class StartupBackdropComposition : IDisposable
    {
        // One idle dispatcher for this STA, shared by successive startup windows.
        // Each visible surface, target and brush is released on Hide/Close.
        private static object dispatcherQueue;
        private Compositor compositor;
        private Windows.UI.Composition.CompositionTarget target;
        private Windows.UI.Composition.ContainerVisual root;
        private CompositionBackdropBrush brush;
        private CompositionColorBrush tintBrush;
        private SpriteVisual backdrop, tint;

        internal static StartupBackdropComposition TryCreate(HwndSource source)
        {
            StartupBackdropComposition candidate = null;
            try {
                if (SystemParameters.HighContrast || !new Windows.UI.ViewManagement.UISettings().AdvancedEffectsEnabled ||
                    Windows.System.Power.PowerManager.EnergySaverStatus == Windows.System.Power.EnergySaverStatus.On) return null;
                candidate = new StartupBackdropComposition(); candidate.Initialize(source); return candidate;
            } catch (Exception error) {
                if (candidate != null) candidate.Dispose();
                if (!(error is COMException || error is DllNotFoundException || error is EntryPointNotFoundException ||
                      error is TypeLoadException || error is NotSupportedException || error is InvalidCastException)) throw;
                return null;
            }
        }
        private void Initialize(HwndSource source)
        {
            int host = 1, none = 1, round = 2, border = unchecked((int)0xfffffffe);
            // USE_HOSTBACKDROPBRUSH is supported on Windows 11. Failure uses the
            // solid fallback rather than allowing an unsupported black brush.
            Marshal.ThrowExceptionForHR(DwmSetWindowAttribute(source.Handle, 17, ref host, 4));
            DwmSetWindowAttribute(source.Handle, 38, ref none, 4);
            DwmSetWindowAttribute(source.Handle, 33, ref round, 4);
            DwmSetWindowAttribute(source.Handle, 34, ref border, 4);
            var margins = new Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 };
            Marshal.ThrowExceptionForHR(DwmExtendFrameIntoClientArea(source.Handle, ref margins));
            source.CompositionTarget.BackgroundColor = Colors.Transparent;
            if (Windows.System.DispatcherQueue.GetForCurrentThread() == null) {
                var options = new QueueOptions { Size = Marshal.SizeOf(typeof(QueueOptions)), ThreadType = 2, ApartmentType = 2 };
                Marshal.ThrowExceptionForHR(CreateDispatcherQueueController(options, out dispatcherQueue));
            }
            compositor = new Compositor();
            target = ((ICompositorDesktopInterop)(object)compositor).CreateDesktopWindowTarget(source.Handle, false);
            root = compositor.CreateContainerVisual(); root.RelativeSizeAdjustment = Vector2.One;
            brush = compositor.CreateHostBackdropBrush();
            backdrop = compositor.CreateSpriteVisual(); backdrop.RelativeSizeAdjustment = Vector2.One; backdrop.Brush = brush;
            tintBrush = compositor.CreateColorBrush(Windows.UI.Color.FromArgb(96, 20, 26, 28));
            tint = compositor.CreateSpriteVisual(); tint.RelativeSizeAdjustment = Vector2.One; tint.Brush = tintBrush;
            root.Children.InsertAtBottom(backdrop); root.Children.InsertAtTop(tint); target.Root = root;
        }
        public void Dispose()
        {
            if (target != null) { target.Root = null; target.Dispose(); target = null; }
            if (backdrop != null) { backdrop.Dispose(); backdrop = null; }
            if (tint != null) { tint.Dispose(); tint = null; }
            if (brush != null) { brush.Dispose(); brush = null; }
            if (tintBrush != null) { tintBrush.Dispose(); tintBrush = null; }
            if (root != null) { root.Dispose(); root = null; }
            if (compositor != null) { compositor.Dispose(); compositor = null; }
        }
        [StructLayout(LayoutKind.Sequential)] private struct Margins { internal int Left, Right, Top, Bottom; }
        [StructLayout(LayoutKind.Sequential)] private struct QueueOptions { internal int Size, ThreadType, ApartmentType; }
        [DllImport("CoreMessaging.dll")] private static extern int CreateDispatcherQueueController(QueueOptions options, [MarshalAs(UnmanagedType.IUnknown)] out object controller);
        [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
        [DllImport("dwmapi.dll")] private static extern int DwmExtendFrameIntoClientArea(IntPtr window, ref Margins margins);
        [ComImport, Guid("29E691FA-4567-4DCA-B319-D0F207EB6807"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface ICompositorDesktopInterop {
            Windows.UI.Composition.Desktop.DesktopWindowTarget CreateDesktopWindowTarget(IntPtr window, [MarshalAs(UnmanagedType.Bool)] bool topmost);
        }
    }
}
