using System.Windows;
using System.Windows.Controls;

namespace KittyDy0Launcher.Controls;

/// <summary>Thanh trượt kính: rãnh kính, phần đã chọn gradient vàng, núm tròn như giọt kính.</summary>
public class LiquidGlassSlider : Slider
{
    static LiquidGlassSlider()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(LiquidGlassSlider), new FrameworkPropertyMetadata(typeof(LiquidGlassSlider)));
    }
}
