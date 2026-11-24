using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Markup;

namespace KittyDy0Launcher.Controls;

/// <summary>
/// Popup kính: nội dung được bọc trong một LiquidGlassPanel trên nền tối mờ (popup nằm ngoài cửa sổ chính nên
/// cần nền đủ đậm để dễ đọc). Dùng như Popup thường, đặt nội dung vào Content.
/// </summary>
[ContentProperty(nameof(Content))]
public class LiquidGlassPopup : Popup
{
    public static readonly DependencyProperty ContentProperty = DependencyProperty.Register(
        nameof(Content), typeof(object), typeof(LiquidGlassPopup),
        new PropertyMetadata(null, (d, _) => ((LiquidGlassPopup)d).Rebuild()));

    public object? Content { get => GetValue(ContentProperty); set => SetValue(ContentProperty, value); }

    public LiquidGlassPopup()
    {
        AllowsTransparency = true;
        StaysOpen = false;
        PopupAnimation = PopupAnimation.Fade;
        Placement = PlacementMode.Bottom;
    }

    private void Rebuild()
    {
        var panel = new LiquidGlassPanel { Content = Content, Padding = new Thickness(16, 12, 16, 12), HasShadow = false };
        var frame = new Border { Margin = new Thickness(0, 8, 0, 0), CornerRadius = new CornerRadius(20), Child = panel };
        frame.SetResourceReference(Border.BackgroundProperty, "LgPopupFill");
        Child = frame;
    }
}
