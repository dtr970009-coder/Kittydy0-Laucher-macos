using System.Net.Http;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;

namespace KittyDy0Launcher;

// Phần giao diện (WPF) của ModHit: ảnh đại diện và màu thay thế.
// File này KHÔNG nằm trong KittyDy0Launcher.Core (bản Mac sẽ có phần tương ứng bằng Avalonia).
public sealed partial class ModHit
{
    // ---------- Ảnh đại diện: có dự phòng khi thiếu ảnh hoặc tải lỗi ----------

    private ImageSource? _icon;
    private Brush? _placeholder;

    public ImageSource? Icon
    {
        get => _icon;
        private set
        {
            if (!SetProperty(ref _icon, value)) return;
            OnPropertyChanged(nameof(HasIcon));
            OnPropertyChanged(nameof(ShowPlaceholder));
        }
    }

    public bool HasIcon => _icon != null;
    public bool ShowPlaceholder => _icon == null;

    /// <summary>Chữ cái đầu của tên, hiện trong ô màu khi mục không có ảnh.</summary>
    public string Initial
    {
        get
        {
            foreach (var ch in Title) if (char.IsLetterOrDigit(ch)) return char.ToUpperInvariant(ch).ToString();
            return "?";
        }
    }

    /// <summary>Màu nền cho ô thay thế ảnh, cố định theo tên nên cùng một mục luôn cùng màu.</summary>
    public Brush PlaceholderBrush
    {
        get
        {
            if (_placeholder != null) return _placeholder;
            var hash = 17;
            foreach (var ch in Title) hash = unchecked(hash * 31 + ch);
            var hue = (uint)hash % 360;
            var b = new LinearGradientBrush(FromHsv(hue, 0.55, 0.78), FromHsv((hue + 40) % 360, 0.65, 0.5), 45);
            b.Freeze();
            return _placeholder = b;
        }
    }

    private static Color FromHsv(double h, double s, double v)
    {
        var c = v * s;
        var x = c * (1 - Math.Abs(h / 60 % 2 - 1));
        var m = v - c;
        var (r, g, b) = h switch
        {
            < 60 => (c, x, 0.0),
            < 120 => (x, c, 0.0),
            < 180 => (0.0, c, x),
            < 240 => (0.0, x, c),
            < 300 => (x, 0.0, c),
            _ => (c, 0.0, x)
        };
        return Color.FromRgb((byte)((r + m) * 255), (byte)((g + m) * 255), (byte)((b + m) * 255));
    }

    private static readonly HttpClient IconHttp = CreateIconClient();
    private static readonly SemaphoreSlim IconGate = new(6);
    private static readonly Dictionary<string, ImageSource?> IconCache = new();
    private static readonly HashSet<string> IconHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "cdn.modrinth.com", "media.forgecdn.net", "media.curseforge.com", "cdn.curseforge.com",
        "edge.forgecdn.net", "mediafilez.forgecdn.net"
    };

    private static HttpClient CreateIconClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("KittyDy0Launcher/1.0 (minecraft launcher)");
        return c;
    }

    /// <summary>
    /// Tải ảnh đại diện (không chặn giao diện). Chỉ khi tải và giải mã xong mới hiện ảnh; thiếu ảnh, lỗi mạng,
    /// định dạng không đọc được (SVG, WebP) thì giữ ô chữ cái màu, nên danh sách luôn đồng đều.
    /// </summary>
    public async Task LoadIconAsync()
    {
        var url = IconUrl;
        if (string.IsNullOrEmpty(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps || !IconHosts.Contains(uri.Host)) return;

        ImageSource? cached = null;
        bool hit;
        lock (IconCache) hit = IconCache.TryGetValue(url, out cached);
        if (hit) { Icon = cached; return; } // đã tải trước đó (hoặc đã biết là lỗi thì giữ ô chữ cái)

        await IconGate.WaitAsync();
        ImageSource? result = null;
        try
        {
            var bytes = await IconHttp.GetByteArrayAsync(uri);
            if (bytes.Length is > 0 and <= 4_000_000)
            {
                using var ms = new MemoryStream(bytes);
                var bi = new BitmapImage();
                bi.BeginInit();
                bi.CacheOption = BitmapCacheOption.OnLoad;
                bi.DecodePixelWidth = 112;
                bi.StreamSource = ms;
                bi.EndInit();
                bi.Freeze();
                result = bi;
            }
        }
        catch { /* giữ ô thay thế */ }
        finally { IconGate.Release(); }

        lock (IconCache) IconCache[url] = result;
        if (result != null) Icon = result;
    }
}
