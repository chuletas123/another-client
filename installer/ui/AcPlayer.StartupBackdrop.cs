using System;
using System.ComponentModel;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
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
        private IDisposable material;
        private bool refreshing;
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
            if(refreshing)return;refreshing=true;
            try{
                SuspendMaterial();
                if(source==null||canClose)return;
                material=StartupMaterialPolicy.TryCreate<IDisposable>(StartupMaterialPolicy.CanUseBlur,()=>StartupBackdropComposition.Create(source));
                if(material==null)RestoreOpaqueSurface();
                Background=material==null?new SolidColorBrush(Color.FromRgb(32,40,42)):Brushes.Transparent;
            }catch(Exception error){
                if(!StartupMaterialPolicy.Recoverable(error))throw;
                SuspendMaterial();
            }finally{refreshing=false;}
        }
        internal void SuspendMaterial()
        {
            var old=material;material=null;StartupMaterialPolicy.Release(old);
            RestoreOpaqueSurface();
            Background=new SolidColorBrush(Color.FromRgb(32,40,42));
        }
        private void RestoreOpaqueSurface()
        {
            if(source==null)return;
            try{if(source.CompositionTarget!=null)source.CompositionTarget.BackgroundColor=Color.FromRgb(32,40,42);int off=0;DwmSetWindowAttribute(source.Handle,17,ref off,4);var margins=new Margins();DwmExtendFrameIntoClientArea(source.Handle,ref margins);}
            catch(Exception error){if(!StartupMaterialPolicy.Recoverable(error))throw;}
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
        [StructLayout(LayoutKind.Sequential)]private struct Margins{internal int Left,Right,Top,Bottom;}
        [DllImport("dwmapi.dll")]private static extern int DwmSetWindowAttribute(IntPtr window,int attribute,ref int value,int size);
        [DllImport("dwmapi.dll")]private static extern int DwmExtendFrameIntoClientArea(IntPtr window,ref Margins margins);
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

        // Keep WinRT activation behind a non-inlined call whose signature uses
        // only framework types. Type loading/JIT failures are caught by the caller.
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static IDisposable Create(HwndSource source)
        {
            StartupBackdropComposition candidate = null;
            try {
                if (SystemParameters.HighContrast || !new Windows.UI.ViewManagement.UISettings().AdvancedEffectsEnabled ||
                    Windows.System.Power.PowerManager.EnergySaverStatus == Windows.System.Power.EnergySaverStatus.On) return null;
                candidate = new StartupBackdropComposition(); candidate.Initialize(source); return candidate;
            } catch {
                StartupMaterialPolicy.Release(candidate);throw;
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
            if(target!=null){try{target.Root=null;}catch(Exception error){if(!StartupMaterialPolicy.Recoverable(error))throw;}}
            var oldTarget=target;target=null;StartupMaterialPolicy.Release(oldTarget);
            var oldBackdrop=backdrop;backdrop=null;StartupMaterialPolicy.Release(oldBackdrop);
            var oldTint=tint;tint=null;StartupMaterialPolicy.Release(oldTint);
            var oldBrush=brush;brush=null;StartupMaterialPolicy.Release(oldBrush);
            var oldTintBrush=tintBrush;tintBrush=null;StartupMaterialPolicy.Release(oldTintBrush);
            var oldRoot=root;root=null;StartupMaterialPolicy.Release(oldRoot);
            var oldCompositor=compositor;compositor=null;StartupMaterialPolicy.Release(oldCompositor);
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
