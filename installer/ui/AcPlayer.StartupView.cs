using System;
using System.ComponentModel;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace AnotherClient.Player
{
    // Presentation only. DWM blurs the real windows behind this HWND; no desktop
    // screenshot, wallpaper image, gradient, child HWND or per-frame UI timer.
    internal class StartupCard : Window, IDisposable, IStartupPresentation
    {
        private readonly TextBlock status = new TextBlock();
        private readonly Image brand = new Image();
        private readonly StartupBar bar = new StartupBar();
        private readonly Button cancel = new Button();
        private readonly Canvas layout = new Canvas { Width = 500, Height = 280 };
        private HwndSource source;
        private StartupBackdropWindow backdropWindow;
        private readonly StartupWindowPriority priority;
        private bool canClose;
        internal event EventHandler CancelRequested;
        internal StartupBar ProgressBar { get { return bar; } }
        internal TextBlock StatusLabel { get { return status; } }
        internal Button CancelControl { get { return cancel; } }
        internal Image BrandControl { get { return brand; } }
        internal Canvas LayoutCanvas { get { return layout; } }
        internal bool IsDisposed { get; private set; }
        internal bool AcrylicAccepted { get { return backdropWindow != null && backdropWindow.MaterialActive; } }

        internal StartupCard(bool preview)
        {
            Title = "Another Client";
            Width = 500; Height = 280;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = true; ShowInTaskbar = false; ShowActivated = !preview;
            Background = Brushes.Transparent;
            UseLayoutRounding = true; SnapsToDevicePixels = true;
            FontFamily = new FontFamily("Segoe UI"); Foreground = Brushes.White;
            priority = new StartupWindowPriority(new StartupDesktopObserver(Dispatcher), this);
            Content = new Viewbox { Stretch = Stretch.Uniform, Child = layout };
            brand.Source = LoadBrand(); brand.Stretch = Stretch.Uniform; Icon = brand.Source;
            AutomationProperties.SetName(brand, "Another Client");
            Place(brand, 210, 46, 80, 80);
            status.Text = "Starting Another Client..."; status.FontSize = 18;
            status.TextAlignment = TextAlignment.Center; status.VerticalAlignment = VerticalAlignment.Center;
            status.TextTrimming = TextTrimming.CharacterEllipsis;
            status.Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = Colors.Black, Opacity = .8, BlurRadius = 4, ShadowDepth = 1 };
            Place(status, 24, 177, 452, 27);
            Place(bar, 32, 208, 436, 10);
            cancel.Content = "Cancel"; cancel.FontSize = 14; cancel.Foreground = Brushes.White;
            cancel.Cursor = Cursors.Hand; cancel.FocusVisualStyle = null;
            cancel.Template = CancelTemplate();
            AutomationProperties.SetName(cancel, "Cancel startup");
            Place(cancel, 190, 235, 120, 30);
            cancel.Click += delegate { var handler = CancelRequested; if (handler != null) handler(this, EventArgs.Empty); };
            MouseLeftButtonDown += DragSurfaceMouseDown;
            Closing += delegate(object sender, CancelEventArgs e) { if (!canClose) e.Cancel = true; };
            PreviewKeyDown += delegate(object sender, KeyEventArgs e) {
                if (!canClose && e.Key == Key.System && e.SystemKey == Key.F4 && (Keyboard.Modifiers & ModifierKeys.Alt) != 0) e.Handled = true;
            };
            IsVisibleChanged += delegate {
                bar.SetOwnerVisible(IsVisible);
                if (backdropWindow == null) return;
                if (IsVisible) { backdropWindow.SynchronizeBounds(); backdropWindow.Show(); if (!backdropWindow.MaterialActive) backdropWindow.RefreshMaterial(); }
                else { backdropWindow.Hide(); backdropWindow.SuspendMaterial(); }
            };
            LocationChanged += delegate { if (backdropWindow != null) backdropWindow.SynchronizeBounds(); };
            SizeChanged += delegate { if (backdropWindow != null) backdropWindow.SynchronizeBounds(); };
            SourceInitialized += delegate {
                source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
                source.CompositionTarget.BackgroundColor = Colors.Transparent;
                source.AddHook(WindowMessages);
                backdropWindow = new StartupBackdropWindow(this);
                backdropWindow.Show(); Owner = backdropWindow;
            };
            Loaded += delegate { if (backdropWindow != null) backdropWindow.SynchronizeBounds(); };
        }
        internal void ShowStartup() { if (!IsDisposed) priority.Start(); }
        internal void HideStartup() { priority.Stop(); if (IsVisible) Hide(); }
        void IStartupPresentation.Present(bool activate)
        {
            if (IsDisposed || canClose) return;
            Topmost = true;
            if (backdropWindow != null) backdropWindow.SynchronizeBounds();
            if (!IsVisible) { ShowActivated = activate; Show(); }
            if (source != null) StartupWindowZOrder.RaisePair(backdropWindow == null ? IntPtr.Zero : new WindowInteropHelper(backdropWindow).Handle, source.Handle);
        }
        void IStartupPresentation.Suppress()
        {
            // Hiding, rather than merely removing Topmost, also protects borderless
            // fullscreen apps. Preparation continues without displaying a window.
            if (IsDisposed) return;
            if (IsVisible) Hide(); Topmost = false;
            if (backdropWindow != null) backdropWindow.SynchronizeBounds();
        }
        internal void DragSurfaceMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (canClose || IsDisposed || !IsVisible || e.ChangedButton != MouseButton.Left || e.LeftButton != MouseButtonState.Pressed) return;
            DependencyObject item = e.OriginalSource as DependencyObject;
            while (item != null) {
                if (item is System.Windows.Controls.Primitives.ButtonBase) return;
                var content = item as FrameworkContentElement;
                item = content != null ? content.Parent : item is Visual ? VisualTreeHelper.GetParent(item) : LogicalTreeHelper.GetParent(item);
            }
            e.Handled = true;
            try { DragMove(); }
            catch (InvalidOperationException) { if (!canClose && !IsDisposed && IsVisible && Mouse.LeftButton == MouseButtonState.Pressed) throw; }
        }
        private void Place(FrameworkElement element, double left, double top, double width, double height)
        {
            Canvas.SetLeft(element, left); Canvas.SetTop(element, top); element.Width = width; element.Height = height;
            layout.Children.Add(element);
        }
        private static ControlTemplate CancelTemplate()
        {
            // A single retained visual tree avoids transparent WinForms child
            // repaint artifacts, including a strip copied into the Cancel button.
            var border = new FrameworkElementFactory(typeof(Border), "Surface");
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
            border.SetValue(Border.BackgroundProperty, new SolidColorBrush(Color.FromArgb(22, 255, 255, 255)));
            border.SetValue(Border.BorderBrushProperty, Brushes.Transparent);
            border.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            var content = new FrameworkElementFactory(typeof(ContentPresenter));
            content.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            content.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            content.SetValue(UIElement.EffectProperty, new System.Windows.Media.Effects.DropShadowEffect { Color = Colors.Black, Opacity = .8, BlurRadius = 3, ShadowDepth = 1 });
            border.AppendChild(content);
            var template = new ControlTemplate(typeof(Button)) { VisualTree = border };
            var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(Border.BackgroundProperty, new SolidColorBrush(Color.FromArgb(38, 255, 255, 255)), "Surface"));
            template.Triggers.Add(hover);
            var pressed = new Trigger { Property = Button.IsPressedProperty, Value = true };
            pressed.Setters.Add(new Setter(Border.BackgroundProperty, new SolidColorBrush(Color.FromArgb(52, 255, 255, 255)), "Surface"));
            template.Triggers.Add(pressed);
            var focused = new Trigger { Property = UIElement.IsKeyboardFocusedProperty, Value = true };
            focused.Setters.Add(new Setter(Border.BorderBrushProperty, new SolidColorBrush(Color.FromArgb(110, 255, 255, 255)), "Surface"));
            template.Triggers.Add(focused);
            var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
            disabled.Setters.Add(new Setter(UIElement.OpacityProperty, 0.5, "Surface"));
            template.Triggers.Add(disabled);
            return template;
        }
        private static BitmapSource LoadBrand()
        {
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("AnotherClient.Icon")) {
                if (stream != null) {
                    var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                    BitmapFrame best = decoder.Frames[0];
                    foreach (var frame in decoder.Frames) if (frame.PixelWidth > best.PixelWidth) best = frame;
                    best.Freeze(); return best;
                }
            }
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("AnotherClient.Branding")) {
                if (stream == null) return null;
                var image = BitmapFrame.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                image.Freeze(); return image;
            }
        }
        private IntPtr WindowMessages(IntPtr hwnd, int message, IntPtr wparam, IntPtr lparam, ref bool handled)
        {
            if (message == 0x112 && BlocksSystemCommand(wparam.ToInt64())) handled = true;
            if ((message == 0x31e || message == 0x31a || message == 0x1a) && IsVisible && backdropWindow != null) backdropWindow.RefreshMaterial();
            return IntPtr.Zero;
        }
        internal bool BlocksSystemCommand(long command)
        {
            command &= 0xfff0;
            return command == 0xf020 || command == 0xf030 || (command == 0xf060 && !canClose);
        }
        internal void SetProgress(string message, int percent, bool indeterminate)
        {
            if (IsDisposed) return;
            status.Text = message; bar.SetProgress(percent, indeterminate);
        }
        internal void SetCancelling() { cancel.IsEnabled = false; status.Text = "Cancelling..."; bar.Pause(); }
        internal void CompleteAndClose() { if (IsDisposed) return; canClose = true; bar.Pause(); Close(); }
        internal void PauseAnimation() { bar.Pause(); }
        protected override void OnClosed(EventArgs e)
        {
            IsDisposed = true; priority.Dispose(); bar.Pause();
            if (source != null) { source.RemoveHook(WindowMessages); source = null; }
            base.OnClosed(e);
            if (backdropWindow != null) { backdropWindow.Dispose(); backdropWindow = null; }
        }
        protected virtual void Dispose(bool disposing) { if (disposing && !IsDisposed) CompleteAndClose(); }
        public void Dispose() { Dispose(true); GC.SuppressFinalize(this); }
    }

    internal sealed class StartupBar : Grid
    {
        private readonly Rectangle pulse = new Rectangle { RadiusX = 5, RadiusY = 5, Fill = new SolidColorBrush(Color.FromRgb(236, 233, 255)), HorizontalAlignment = HorizontalAlignment.Left };
        private readonly Rectangle fill = new Rectangle { RadiusX = 5, RadiusY = 5, Fill = new SolidColorBrush(Color.FromRgb(236, 233, 255)) };
        private readonly TranslateTransform travel = new TranslateTransform();
        private readonly ScaleTransform fraction = new ScaleTransform(0, 1);
        private int percent;
        private bool indeterminate = true, paused, ownerVisible;
        internal int Value { get { return percent; } }
        internal bool IsIndeterminate { get { return indeterminate; } }
        internal bool IsAnimating { get; private set; }
        internal StartupBar()
        {
            ClipToBounds = true; IsHitTestVisible = false;
            AutomationProperties.SetName(this, "Startup progress");
            Children.Add(new Rectangle { RadiusX = 5, RadiusY = 5, Fill = new SolidColorBrush(Color.FromArgb(26, 255, 255, 255)) });
            fill.RenderTransform = fraction; pulse.RenderTransform = travel;
            Children.Add(fill); Children.Add(pulse);
            SizeChanged += delegate { Clip = new RectangleGeometry(new Rect(0, 0, ActualWidth, ActualHeight), ActualHeight / 2, ActualHeight / 2); UpdateAnimation(true); };
        }
        internal void SetProgress(int value, bool pending)
        {
            percent = Math.Max(percent, Math.Min(100, Math.Max(0, value)));
            indeterminate = pending && percent < 100; paused = false;
            UpdateAnimation(false);
            fraction.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(percent / 100.0, TimeSpan.FromMilliseconds(140)) { FillBehavior = FillBehavior.HoldEnd });
        }
        internal void Pause() { paused = true; UpdateAnimation(false); fraction.BeginAnimation(ScaleTransform.ScaleXProperty, null); fraction.ScaleX = percent / 100.0; }
        internal void SetOwnerVisible(bool visible) { ownerVisible = visible; UpdateAnimation(false); }
        private void UpdateAnimation(bool resized)
        {
            bool animate = ownerVisible && indeterminate && !paused && ActualWidth > 0;
            pulse.Visibility = animate ? Visibility.Visible : Visibility.Collapsed;
            fill.Visibility = indeterminate && !paused ? Visibility.Collapsed : Visibility.Visible;
            if (animate && (!IsAnimating || resized)) {
                double length = ActualWidth * .43; pulse.Width = length;
                var motion = new DoubleAnimation(-length, ActualWidth, TimeSpan.FromSeconds(1.5)) {
                    RepeatBehavior = RepeatBehavior.Forever, FillBehavior = FillBehavior.Stop
                };
                travel.BeginAnimation(TranslateTransform.XProperty, motion, HandoffBehavior.SnapshotAndReplace);
            } else if (!animate && IsAnimating) travel.BeginAnimation(TranslateTransform.XProperty, null);
            IsAnimating = animate;
        }
    }
}
