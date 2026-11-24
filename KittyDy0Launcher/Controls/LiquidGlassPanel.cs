using System.Windows;
using System.Windows.Controls;

namespace KittyDy0Launcher.Controls;

/// <summary>Khung kính tĩnh để chứa nội dung (không phản ứng khi rê chuột).</summary>
public class LiquidGlassPanel : ContentControl
{
    static LiquidGlassPanel()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(LiquidGlassPanel), new FrameworkPropertyMetadata(typeof(LiquidGlassPanel)));
    }

    public static readonly DependencyProperty CornerRadiusProperty = DependencyProperty.Register(
        nameof(CornerRadius), typeof(CornerRadius), typeof(LiquidGlassPanel), new PropertyMetadata(new CornerRadius(20)));

    public static readonly DependencyProperty HasShadowProperty = DependencyProperty.Register(
        nameof(HasShadow), typeof(bool), typeof(LiquidGlassPanel), new PropertyMetadata(true));

    public CornerRadius CornerRadius { get => (CornerRadius)GetValue(CornerRadiusProperty); set => SetValue(CornerRadiusProperty, value); }
    public bool HasShadow { get => (bool)GetValue(HasShadowProperty); set => SetValue(HasShadowProperty, value); }
}

/// <summary>Thẻ kính: như Panel nhưng sáng viền và phát glow khi rê chuột.</summary>
public class LiquidGlassCard : LiquidGlassPanel
{
    static LiquidGlassCard()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(LiquidGlassCard), new FrameworkPropertyMetadata(typeof(LiquidGlassCard)));
    }
}

/// <summary>Khung viên thuốc chứa thanh menu; thanh trượt chỉ báo do trang chủ tự điều khiển như cũ.</summary>
public class LiquidGlassNavigation : ContentControl
{
    static LiquidGlassNavigation()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(LiquidGlassNavigation), new FrameworkPropertyMetadata(typeof(LiquidGlassNavigation)));
    }
}
