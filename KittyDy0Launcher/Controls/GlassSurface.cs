using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace KittyDy0Launcher.Controls;

/// <summary>
/// Bề mặt kính nhiều lớp, dùng làm nền cho mọi component Liquid Glass: đổ bóng, nền gradient, bóng đáy,
/// lớp phản chiếu trên cùng, viền sáng bên trong, vệt sáng chuyển động, viền hover và vòng focus.
/// Giao diện từng lớp do skin (Glass hoặc Flat) quyết định. Chỉ để vẽ, không nhận chuột.
/// </summary>
public class GlassSurface : Control
{
    static GlassSurface()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(GlassSurface), new FrameworkPropertyMetadata(typeof(GlassSurface)));
    }

    public GlassSurface()
    {
        IsHitTestVisible = false;
        Focusable = false;
        IsTabStop = false;
    }

    public static readonly DependencyProperty CornerRadiusProperty = DependencyProperty.Register(
        nameof(CornerRadius), typeof(CornerRadius), typeof(GlassSurface),
        new PropertyMetadata(new CornerRadius(20), (d, _) => ((GlassSurface)d).UpdateRadius()));

    /// <summary>
    /// Bán kính bo thực tế: không bao giờ lớn hơn nửa cạnh ngắn, nên nút nhỏ luôn bo tròn đều
    /// và mọi lớp (nền, viền, thấu kính) dùng chung một bán kính, không bị lệch nhau.
    /// </summary>
    public static readonly DependencyProperty RenderRadiusProperty = DependencyProperty.Register(
        nameof(RenderRadius), typeof(CornerRadius), typeof(GlassSurface), new PropertyMetadata(new CornerRadius(20)));

    /// <summary>Mức hover 0..1, được GlassFx chạy animation.</summary>
    public static readonly DependencyProperty HotLevelProperty = DependencyProperty.Register(
        nameof(HotLevel), typeof(double), typeof(GlassSurface), new PropertyMetadata(0.0));

    /// <summary>Mức hiện điểm sáng bám theo con trỏ 0..1 (chỉ dùng khi bật hiệu ứng tương tác).</summary>
    public static readonly DependencyProperty SpotLevelProperty = DependencyProperty.Register(
        nameof(SpotLevel), typeof(double), typeof(GlassSurface), new PropertyMetadata(0.0));

    /// <summary>Mức hiện vòng focus bàn phím 0..1.</summary>
    public static readonly DependencyProperty FocusLevelProperty = DependencyProperty.Register(
        nameof(FocusLevel), typeof(double), typeof(GlassSurface), new PropertyMetadata(0.0));

    /// <summary>Bề mặt nhấn mạnh (vàng) cho nút chính.</summary>
    public static readonly DependencyProperty IsAccentProperty = DependencyProperty.Register(
        nameof(IsAccent), typeof(bool), typeof(GlassSurface), new PropertyMetadata(false));

    public static readonly DependencyProperty HasShadowProperty = DependencyProperty.Register(
        nameof(HasShadow), typeof(bool), typeof(GlassSurface), new PropertyMetadata(true));

    /// <summary>Bật lớp tán sắc cầu vồng sát mép (có làm nhòe nên chỉ dùng cho thẻ, menu, thanh chỉ báo).</summary>
    public static readonly DependencyProperty HasDispersionProperty = DependencyProperty.Register(
        nameof(HasDispersion), typeof(bool), typeof(GlassSurface), new PropertyMetadata(false));

    public bool HasDispersion { get => (bool)GetValue(HasDispersionProperty); set => SetValue(HasDispersionProperty, value); }

    private RadialGradientBrush? _spotBrush;

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        // Điểm sáng bám theo con trỏ: một brush tròn, chỉ đổi tâm khi chuột di chuyển (rất nhẹ)
        if (GetTemplateChild("Spot") is Border spot)
        {
            _spotBrush = new RadialGradientBrush { RadiusX = 0.55, RadiusY = 0.9, Center = new Point(0.5, 0.2), GradientOrigin = new Point(0.5, 0.2) };
            _spotBrush.GradientStops.Add(new GradientStop(Color.FromArgb(0x55, 255, 255, 255), 0));
            _spotBrush.GradientStops.Add(new GradientStop(Color.FromArgb(0x16, 255, 240, 200), 0.55));
            _spotBrush.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 1));
            spot.Background = _spotBrush;
        }

    }

    /// <summary>Đưa điểm sáng tới vị trí con trỏ (toạ độ tính trong chính bề mặt này).</summary>
    public void MoveSpot(Point p)
    {
        if (_spotBrush == null || ActualWidth <= 0 || ActualHeight <= 0) return;
        var c = new Point(Math.Clamp(p.X / ActualWidth, 0, 1), Math.Clamp(p.Y / ActualHeight, 0, 1));
        _spotBrush.Center = c;
        _spotBrush.GradientOrigin = c;
    }

    public CornerRadius CornerRadius { get => (CornerRadius)GetValue(CornerRadiusProperty); set => SetValue(CornerRadiusProperty, value); }
    public CornerRadius RenderRadius { get => (CornerRadius)GetValue(RenderRadiusProperty); private set => SetValue(RenderRadiusProperty, value); }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        UpdateRadius();
    }

    private void UpdateRadius()
    {
        var cap = Math.Min(ActualWidth, ActualHeight) / 2;
        var r = cap > 0 ? Math.Min(CornerRadius.TopLeft, cap) : CornerRadius.TopLeft;
        var next = new CornerRadius(r);
        if (next != RenderRadius) RenderRadius = next;
    }
    public double SpotLevel { get => (double)GetValue(SpotLevelProperty); set => SetValue(SpotLevelProperty, value); }
    public double HotLevel { get => (double)GetValue(HotLevelProperty); set => SetValue(HotLevelProperty, value); }
    public double FocusLevel { get => (double)GetValue(FocusLevelProperty); set => SetValue(FocusLevelProperty, value); }
    public bool IsAccent { get => (bool)GetValue(IsAccentProperty); set => SetValue(IsAccentProperty, value); }
    public bool HasShadow { get => (bool)GetValue(HasShadowProperty); set => SetValue(HasShadowProperty, value); }
}
