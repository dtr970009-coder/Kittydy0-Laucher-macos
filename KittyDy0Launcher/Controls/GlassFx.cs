using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace KittyDy0Launcher.Controls;

/// <summary>
/// Hiệu ứng dùng chung cho mọi component kính, gắn bằng attached property:
/// - Interactive: hover (viền sáng + glow), pressed (co dãn kiểu chất lỏng), focus bàn phím (vòng vàng).
/// - Sheen: vệt sáng nhẹ chạy ngang bề mặt.
/// Mọi animation tôn trọng ThemeManager.MotionEnabled: tắt thì chuyển trạng thái tức thì.
/// Template tìm các phần theo tên: PART_Surface (GlassSurface), PART_Scale, PART_Lift.
/// </summary>
public static class GlassFx
{
    // ---------- Thuộc tính gắn ----------

    public static readonly DependencyProperty InteractiveProperty =
        DependencyProperty.RegisterAttached("Interactive", typeof(bool), typeof(GlassFx),
            new PropertyMetadata(false, OnInteractiveChanged));

    public static readonly DependencyProperty LiftProperty =
        DependencyProperty.RegisterAttached("Lift", typeof(double), typeof(GlassFx), new PropertyMetadata(0.0));

    public static readonly DependencyProperty PressScaleProperty =
        DependencyProperty.RegisterAttached("PressScale", typeof(double), typeof(GlassFx), new PropertyMetadata(1.0));

    public static readonly DependencyProperty SheenProperty =
        DependencyProperty.RegisterAttached("Sheen", typeof(bool), typeof(GlassFx),
            new PropertyMetadata(false, OnSheenChanged));

    private static readonly DependencyProperty SheenControllerProperty =
        DependencyProperty.RegisterAttached("SheenController", typeof(object), typeof(GlassFx), new PropertyMetadata(null));

    public static bool GetInteractive(DependencyObject o) => (bool)o.GetValue(InteractiveProperty);
    public static void SetInteractive(DependencyObject o, bool v) => o.SetValue(InteractiveProperty, v);
    public static double GetLift(DependencyObject o) => (double)o.GetValue(LiftProperty);
    public static void SetLift(DependencyObject o, double v) => o.SetValue(LiftProperty, v);
    public static double GetPressScale(DependencyObject o) => (double)o.GetValue(PressScaleProperty);
    public static void SetPressScale(DependencyObject o, double v) => o.SetValue(PressScaleProperty, v);
    public static bool GetSheen(DependencyObject o) => (bool)o.GetValue(SheenProperty);
    public static void SetSheen(DependencyObject o, bool v) => o.SetValue(SheenProperty, v);

    // ---------- Animation helper ----------

    /// <summary>Chạy animation tới giá trị đích; tắt chuyển động thì nhảy thẳng tới đích.</summary>
    internal static void AnimateTo(IAnimatable target, DependencyProperty dp, double to, int ms, IEasingFunction? ease = null)
    {
        var dur = ThemeManager.MotionEnabled ? TimeSpan.FromMilliseconds(ms) : TimeSpan.Zero;
        target.BeginAnimation(dp, new DoubleAnimation(to, dur) { EasingFunction = ease });
    }

    // ---------- Hover / pressed / focus ----------

    private static void OnInteractiveChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement fe) return;

        fe.MouseEnter -= OnEnter;
        fe.MouseLeave -= OnLeave;
        fe.MouseMove -= OnMove;
        fe.PreviewMouseLeftButtonDown -= OnDown;
        fe.PreviewMouseLeftButtonUp -= OnUp;
        fe.GotKeyboardFocus -= OnGotFocus;
        fe.LostKeyboardFocus -= OnLostFocus;
        fe.IsEnabledChanged -= OnEnabledChanged;

        if (e.NewValue is not true) return;
        fe.MouseEnter += OnEnter;
        fe.MouseLeave += OnLeave;
        fe.MouseMove += OnMove;
        fe.PreviewMouseLeftButtonDown += OnDown;
        fe.PreviewMouseLeftButtonUp += OnUp;
        fe.GotKeyboardFocus += OnGotFocus;
        fe.LostKeyboardFocus += OnLostFocus;
        fe.IsEnabledChanged += OnEnabledChanged;
    }

    private static T? Part<T>(FrameworkElement fe, string name) where T : class
    {
        try { return (fe as Control)?.Template?.FindName(name, fe) as T; }
        catch (InvalidOperationException) { return null; } // template chưa áp dụng
    }

    private static void OnEnter(object s, MouseEventArgs e) { if (s is FrameworkElement fe && fe.IsEnabled) Hover(fe, true); }
    private static void OnLeave(object s, MouseEventArgs e) { if (s is FrameworkElement fe) { Press(fe, false); Hover(fe, false); } }
    private static void OnMove(object s, MouseEventArgs e)
    {
        if (!ThemeManager.GlassOn || !ThemeManager.FancyEffects || s is not FrameworkElement fe || !fe.IsEnabled) return;
        if (Part<GlassSurface>(fe, "PART_Surface") is { } surface) surface.MoveSpot(e.GetPosition(surface));
    }

    private static void OnDown(object s, MouseButtonEventArgs e) { if (s is FrameworkElement fe && fe.IsEnabled) Press(fe, true); }
    private static void OnUp(object s, MouseButtonEventArgs e) { if (s is FrameworkElement fe) Press(fe, false); }

    private static void OnGotFocus(object s, KeyboardFocusChangedEventArgs e)
    {
        // Chỉ hiện vòng focus khi người dùng đang điều khiển bằng bàn phím, không hiện khi bấm chuột
        if (s is FrameworkElement fe && ReferenceEquals(e.OriginalSource, s) &&
            InputManager.Current.MostRecentInputDevice is KeyboardDevice) Ring(fe, true);
    }

    private static void OnLostFocus(object s, KeyboardFocusChangedEventArgs e)
    {
        if (s is FrameworkElement fe && ReferenceEquals(e.OriginalSource, s)) Ring(fe, false);
    }

    private static void OnEnabledChanged(object s, DependencyPropertyChangedEventArgs e)
    {
        if (s is FrameworkElement fe && e.NewValue is false) { Press(fe, false); Hover(fe, false); Ring(fe, false); }
    }

    private static void Hover(FrameworkElement fe, bool on)
    {
        // Mặc định chỉ sáng nhẹ viền khi rê chuột; điểm sáng theo chuột và nâng nút chỉ có khi bật "Hiệu ứng tương tác"
        var fancy = ThemeManager.FancyEffects;
        if (Part<GlassSurface>(fe, "PART_Surface") is { } surface)
        {
            AnimateTo(surface, GlassSurface.HotLevelProperty, on ? (fancy ? 1 : 0.4) : 0, on ? 160 : 280);
            AnimateTo(surface, GlassSurface.SpotLevelProperty, on && fancy ? 1 : 0, on ? 160 : 280);
        }

        var lift = fancy ? GetLift(fe) : 0;
        if (lift != 0 && Part<TranslateTransform>(fe, "PART_Lift") is { } t)
            AnimateTo(t, TranslateTransform.YProperty, on ? lift : 0, 200, new CubicEase { EasingMode = EasingMode.EaseOut });
    }

    private static void Press(FrameworkElement fe, bool on)
    {
        var k = GetPressScale(fe);
        if (!ThemeManager.FancyEffects || k >= 1 || Part<ScaleTransform>(fe, "PART_Scale") is not { } sc) return;

        // Ấn xuống thì bẹp theo chiều dọc và hơi phình ngang; thả ra thì nảy lại (cảm giác chất lỏng)
        IEasingFunction ease = on
            ? new CubicEase { EasingMode = EasingMode.EaseOut }
            : new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.6 };
        var ms = on ? 90 : 320;
        AnimateTo(sc, ScaleTransform.ScaleYProperty, on ? k : 1, ms, ease);
        AnimateTo(sc, ScaleTransform.ScaleXProperty, on ? 1 + (1 - k) * 0.6 : 1, ms, ease);
    }

    private static void Ring(FrameworkElement fe, bool on)
    {
        if (Part<GlassSurface>(fe, "PART_Surface") is { } surface)
            AnimateTo(surface, GlassSurface.FocusLevelProperty, on ? 1 : 0, 140);
    }

    // ---------- Vệt sáng chạy nhẹ ----------

    private static void OnSheenChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Border b) return;

        if (d.GetValue(SheenControllerProperty) is SheenController old) old.Detach();
        d.SetValue(SheenControllerProperty, null);

        if (e.NewValue is true)
        {
            var c = new SheenController(b);
            d.SetValue(SheenControllerProperty, c);
            c.Attach();
        }
    }

    /// <summary>Điều khiển vệt sáng của một Border: chỉ chạy khi đang hiện, đang ở chế độ kính và cho phép animation nền.</summary>
    private sealed class SheenController
    {
        private static readonly Random Rng = new();
        private readonly Border _b;
        private TranslateTransform? _move;

        public SheenController(Border b) => _b = b;

        public void Attach()
        {
            _b.Loaded += OnLoaded;
            _b.Unloaded += OnUnloaded;
            _b.IsVisibleChanged += OnVisibleChanged;
            if (_b.IsLoaded) OnLoaded(null, null!);
        }

        public void Detach()
        {
            _b.Loaded -= OnLoaded;
            _b.Unloaded -= OnUnloaded;
            _b.IsVisibleChanged -= OnVisibleChanged;
            ThemeManager.Changed -= Refresh;
            Stop();
        }

        private void OnLoaded(object? s, RoutedEventArgs e) { ThemeManager.Changed -= Refresh; ThemeManager.Changed += Refresh; Refresh(); }
        private void OnUnloaded(object? s, RoutedEventArgs e) { ThemeManager.Changed -= Refresh; Stop(); }
        private void OnVisibleChanged(object s, DependencyPropertyChangedEventArgs e) => Refresh();

        private void Refresh()
        {
            if (_b.IsLoaded && _b.IsVisible && ThemeManager.GlassOn && ThemeManager.AmbientMotion && ThemeManager.FancyEffects) Start();
            else Stop();
        }

        private void Start()
        {
            if (_move != null) return;

            // Dải sáng chéo, dịch bằng RelativeTransform nên không phụ thuộc kích thước thẻ (co giãn cửa sổ vẫn đúng)
            var brush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0.35) };
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 0.30));
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(0x26, 255, 255, 255), 0.50));
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 0.70));
            _move = new TranslateTransform(-1.3, 0);
            brush.RelativeTransform = _move;
            _b.Background = brush;

            var anim = new DoubleAnimationUsingKeyFrames
            {
                Duration = TimeSpan.FromSeconds(12),
                RepeatBehavior = RepeatBehavior.Forever,
                BeginTime = TimeSpan.FromMilliseconds(Rng.Next(0, 5000)) // lệch pha để các thẻ không sáng cùng lúc
            };
            anim.KeyFrames.Add(new EasingDoubleKeyFrame(-1.3, KeyTime.FromPercent(0)));
            anim.KeyFrames.Add(new EasingDoubleKeyFrame(1.3, KeyTime.FromPercent(0.4),
                new SineEase { EasingMode = EasingMode.EaseInOut }));
            anim.KeyFrames.Add(new EasingDoubleKeyFrame(1.3, KeyTime.FromPercent(1)));
            anim.SetValue(Timeline.DesiredFrameRateProperty, 30); // chạy 30 fps cho nhẹ máy
            _move.BeginAnimation(TranslateTransform.XProperty, anim);
        }

        private void Stop()
        {
            if (_move == null) return;
            _move.BeginAnimation(TranslateTransform.XProperty, null);
            _move = null;
            _b.Background = null;
        }
    }
}
