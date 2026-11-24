using System.Windows;
using System.Windows.Media;

namespace KittyDy0Launcher.Controls;

/// <summary>
/// Cửa sổ nền cho launcher: không viền, trong suốt (để tự vẽ bo góc), luôn nằm gọn trong vùng làm việc
/// của màn hình (đúng với mọi mức DPI) và tự cập nhật khi đổi chế độ Liquid Glass / Dark Flat.
/// Bo góc do phần tử gốc trong XAML tự vẽ (mặc định 20px) nên không phụ thuộc bo góc của Windows.
/// </summary>
public class LiquidGlassWindow : Window
{
    public static readonly DependencyProperty CornerRadiusProperty = DependencyProperty.Register(
        nameof(CornerRadius), typeof(CornerRadius), typeof(LiquidGlassWindow), new PropertyMetadata(new CornerRadius(20)));

    /// <summary>Bo góc của cửa sổ (tự vẽ, mặc định 20px).</summary>
    public CornerRadius CornerRadius { get => (CornerRadius)GetValue(CornerRadiusProperty); set => SetValue(CornerRadiusProperty, value); }

    public LiquidGlassWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        SnapsToDevicePixels = true;
        UseLayoutRounding = true;

        Loaded += (_, _) => FitToWorkArea();
    }

    /// <summary>Thu cửa sổ lại nếu lớn hơn vùng làm việc (màn hình nhỏ hoặc DPI cao).</summary>
    private void FitToWorkArea()
    {
        var wa = SystemParameters.WorkArea;
        if (Width > wa.Width) Width = wa.Width;
        if (Height > wa.Height) Height = wa.Height;
        if (Left < wa.Left || Top < wa.Top || Left + Width > wa.Right || Top + Height > wa.Bottom)
        {
            Left = wa.Left + (wa.Width - Width) / 2;
            Top = wa.Top + (wa.Height - Height) / 2;
        }
    }
}
