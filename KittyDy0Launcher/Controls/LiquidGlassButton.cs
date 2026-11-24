using System.Windows;
using System.Windows.Controls;

namespace KittyDy0Launcher.Controls;

/// <summary>Nút kính hình viên thuốc. IsPrimary = true cho nút chính (nền vàng, glow vàng).</summary>
public class LiquidGlassButton : Button
{
    static LiquidGlassButton()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(LiquidGlassButton), new FrameworkPropertyMetadata(typeof(LiquidGlassButton)));
    }

    public static readonly DependencyProperty IsPrimaryProperty = DependencyProperty.Register(
        nameof(IsPrimary), typeof(bool), typeof(LiquidGlassButton), new PropertyMetadata(false));

    public bool IsPrimary { get => (bool)GetValue(IsPrimaryProperty); set => SetValue(IsPrimaryProperty, value); }
}
