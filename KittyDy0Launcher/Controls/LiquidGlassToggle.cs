using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Media.Animation;
using System.Windows.Media;

namespace KittyDy0Launcher.Controls;

/// <summary>Công tắc ON/OFF: núm trượt nảy nhẹ, nền chuyển sang vàng khi bật. Tắt chuyển động thì đổi trạng thái tức thì.</summary>
public class LiquidGlassToggle : ToggleButton
{
    private const double Travel = 20; // quãng núm trượt (rộng 48, núm 22, lề 3)
    private TranslateTransform? _move;
    private UIElement? _onFill;

    static LiquidGlassToggle()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(LiquidGlassToggle), new FrameworkPropertyMetadata(typeof(LiquidGlassToggle)));
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _move = GetTemplateChild("PART_ThumbMove") as TranslateTransform;
        _onFill = GetTemplateChild("PART_OnFill") as UIElement;
        UpdateVisual(animate: false);
    }

    protected override void OnChecked(RoutedEventArgs e) { base.OnChecked(e); UpdateVisual(true); }
    protected override void OnUnchecked(RoutedEventArgs e) { base.OnUnchecked(e); UpdateVisual(true); }

    private void UpdateVisual(bool animate)
    {
        var on = IsChecked == true;
        var x = on ? Travel : 0;
        var op = on ? 1.0 : 0.0;

        if (!animate)
        {
            _move?.BeginAnimation(TranslateTransform.XProperty, null);
            if (_move != null) _move.X = x;
            if (_onFill != null) { _onFill.BeginAnimation(OpacityProperty, null); _onFill.Opacity = op; }
            return;
        }

        if (_move != null)
            GlassFx.AnimateTo(_move, TranslateTransform.XProperty, x, 260, new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.45 });
        if (_onFill != null)
            GlassFx.AnimateTo(_onFill, OpacityProperty, op, 200);
    }
}
