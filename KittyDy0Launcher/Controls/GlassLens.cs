using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace KittyDy0Launcher.Controls;

/// <summary>
/// Nguồn ảnh nền dùng để khúc xạ: một phần tử nằm dưới cùng của cửa sổ (BackdropArt), do MainWindow đặt.
/// Ảnh nền được chụp một lần thành mảng điểm ảnh rồi dùng chung cho mọi thấu kính; chụp lại khi nền đổi
/// (đổi ảnh nền, đổi chế độ kính/phẳng, đổi DPI hay kích thước).
/// </summary>
public static class GlassBackdrop
{
    public static FrameworkElement? Source { get; private set; }
    public static event Action? Changed;

    private static byte[]? _pixels;
    private static int _w, _h, _cachedVersion = -1;
    private static double _scale;
    private static int _contentVersion;

    public static void SetSource(FrameworkElement? element)
    {
        Source = element;
        Invalidate();
    }

    /// <summary>Báo nền đã đổi: xoá ảnh chụp cũ, các thấu kính sẽ dựng lại.</summary>
    public static void Invalidate()
    {
        _contentVersion++;
        _pixels = null;
        Changed?.Invoke();
    }

    /// <summary>Lấy ảnh chụp nền (Pbgra32). Trả về false nếu chưa chụp được (không có nền, chưa bố cục, lỗi).</summary>
    public static bool TryGetPixels(out byte[] px, out int w, out int h, out double scale, out int version)
    {
        px = Array.Empty<byte>();
        w = h = 0;
        scale = 1;
        version = _contentVersion + ThemeManager.Version * 100000;

        var src = Source;
        if (src == null || src.ActualWidth < 2 || src.ActualHeight < 2) return false;

        try
        {
            var s = Math.Clamp(VisualTreeHelper.GetDpi(src).DpiScaleX, 1.0, 2.0);
            var wp = (int)Math.Ceiling(src.ActualWidth * s);
            var hp = (int)Math.Ceiling(src.ActualHeight * s);

            if (_pixels == null || _cachedVersion != version || _w != wp || _h != hp || _scale != s)
            {
                src.UpdateLayout();
                var rtb = new RenderTargetBitmap(wp, hp, 96 * s, 96 * s, PixelFormats.Pbgra32);

                // Vẽ qua VisualBrush với khung cố định để không bị lệch do phần tử con tràn ra ngoài
                var dv = new DrawingVisual();
                using (var dc = dv.RenderOpen())
                {
                    var brush = new VisualBrush(src)
                    {
                        Stretch = Stretch.Fill,
                        ViewboxUnits = BrushMappingMode.Absolute,
                        Viewbox = new Rect(0, 0, src.ActualWidth, src.ActualHeight)
                    };
                    dc.DrawRectangle(brush, null, new Rect(0, 0, src.ActualWidth, src.ActualHeight));
                }
                rtb.Render(dv);

                var bytes = new byte[wp * hp * 4];
                rtb.CopyPixels(bytes, wp * 4, 0);
                _pixels = bytes;
                _w = wp;
                _h = hp;
                _scale = s;
                _cachedVersion = version;
            }

            px = _pixels;
            w = _w;
            h = _h;
            scale = _scale;
            return true;
        }
        catch
        {
            return false; // không chụp được thì thấu kính tự tắt, giữ kính thường
        }
    }
}

/// <summary>
/// Thấu kính kính lỏng: lấy đúng phần ảnh nền nằm dưới nó, bẻ cong ở vành (khúc xạ + tán sắc, xem LensRenderer)
/// rồi vẽ lên. Tự bật khi: đang ở chế độ Liquid Glass, bật Khúc xạ, có ảnh nền, và phần tử đang hiện.
/// Mọi bước đều tính bằng C# và bọc try/catch: nếu có lỗi, thấu kính tự tắt chứ không làm sập launcher.
/// Mỗi khung hình chỉ kiểm tra vị trí; chỉ dựng lại ảnh khi vị trí hoặc nền thật sự đổi
/// (thấu kính đứng yên thì chỉ dựng một lần).
/// </summary>
public sealed class GlassLens : FrameworkElement
{
    public static readonly DependencyProperty CornerRadiusProperty = DependencyProperty.Register(
        nameof(CornerRadius), typeof(CornerRadius), typeof(GlassLens),
        new PropertyMetadata(new CornerRadius(20), (d, _) => ((GlassLens)d).Refresh()));

    public CornerRadius CornerRadius { get => (CornerRadius)GetValue(CornerRadiusProperty); set => SetValue(CornerRadiusProperty, value); }

    private WriteableBitmap? _bmp;
    private byte[]? _buf;
    private bool _active;
    private Rect _box;
    private double _lastScale;
    private int _lastVersion = -1;
    private double _lastRadius = -1;

    public GlassLens()
    {
        IsHitTestVisible = false;
        Loaded += (_, _) => { ThemeManager.Changed += Refresh; GlassBackdrop.Changed += Refresh; Refresh(); };
        Unloaded += (_, _) => { ThemeManager.Changed -= Refresh; GlassBackdrop.Changed -= Refresh; Deactivate(); };
        IsVisibleChanged += (_, _) => Refresh();
        SizeChanged += (_, _) => Refresh();
    }

    protected override Size MeasureOverride(Size availableSize) => new(0, 0); // kích thước do bố cục cha quyết định (Stretch)

    protected override void OnRender(DrawingContext dc)
    {
        if (_active && _bmp != null) dc.DrawImage(_bmp, new Rect(0, 0, ActualWidth, ActualHeight));
    }

    private bool ShouldRun =>
        IsLoaded && IsVisible && ThemeManager.GlassOn && ThemeManager.RefractionOn &&
        GlassBackdrop.Source != null && ActualWidth >= 16 && ActualHeight >= 16;

    private void Refresh()
    {
        if (!ShouldRun) { Deactivate(); return; }
        if (!_active) Activate();
        Rebuild(force: true);
    }

    private void Activate()
    {
        _active = true;
        CompositionTarget.Rendering += OnFrame;
    }

    private void Deactivate()
    {
        if (_active) CompositionTarget.Rendering -= OnFrame;
        _active = false;
        _bmp = null;
        _buf = null;
        _lastVersion = -1;
        InvalidateVisual();
    }

    private void OnFrame(object? sender, EventArgs e) => Rebuild(force: false);

    /// <summary>Dựng lại ảnh thấu kính nếu vị trí, kích thước hoặc nền đã đổi (hoặc force).</summary>
    private void Rebuild(bool force)
    {
        try
        {
            var src = GlassBackdrop.Source;
            if (!_active || src == null) return;
            if (!GlassBackdrop.TryGetPixels(out var px, out var bw, out var bh, out var scale, out var version))
            {
                if (_bmp != null) { _bmp = null; InvalidateVisual(); } // chưa chụp được nền: tạm ẩn thấu kính
                return;
            }

            // Vị trí phần nền nằm dưới thấu kính (theo toạ độ ảnh nền, kể cả khi đang bị dịch/co giãn)
            Rect box;
            try
            {
                var t = TransformToVisual(src);
                box = new Rect(t.Transform(new Point(0, 0)), t.Transform(new Point(ActualWidth, ActualHeight)));
            }
            catch (InvalidOperationException)
            {
                return; // chưa nằm chung cây hiển thị với ảnh nền
            }
            if (box.Width < 1 || box.Height < 1) return;

            var radius = Math.Min(CornerRadius.TopLeft, Math.Min(box.Width, box.Height) / 2);
            var cx = (int)Math.Round(box.X * scale);
            var cy = (int)Math.Round(box.Y * scale);
            var wPx = Math.Max(1, (int)Math.Round(box.Width * scale));
            var hPx = Math.Max(1, (int)Math.Round(box.Height * scale));

            if (!force && version == _lastVersion && scale == _lastScale && radius == _lastRadius &&
                _bmp != null && _bmp.PixelWidth == wPx && _bmp.PixelHeight == hPx &&
                Math.Abs(box.X - _box.X) < 0.3 && Math.Abs(box.Y - _box.Y) < 0.3) return;

            _box = box;
            _lastVersion = version;
            _lastScale = scale;
            _lastRadius = radius;

            if (_bmp == null || _bmp.PixelWidth != wPx || _bmp.PixelHeight != hPx)
            {
                _bmp = new WriteableBitmap(wPx, hPx, 96 * scale, 96 * scale, PixelFormats.Pbgra32, null);
                _buf = new byte[wPx * hPx * 4];
            }

            LensRenderer.Render(px, bw, bh, cx, cy, wPx, hPx, (float)(radius * scale), (float)(22 * scale), _buf!);
            _bmp.WritePixels(new Int32Rect(0, 0, wPx, hPx), _buf!, wPx * 4, 0);
            InvalidateVisual();
        }
        catch
        {
            Deactivate(); // bất kỳ lỗi nào: bỏ khúc xạ ở thấu kính này, giữ kính thường
        }
    }
}
