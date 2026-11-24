using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace KittyDy0Launcher;

/// <summary>
/// Các thiết lập cá nhân hóa giao diện (màu sắc, độ sáng/độ trong, độ mạnh khúc xạ, vị trí menu...).
/// Giá trị mặc định cho ra giao diện giống bản gốc.
/// </summary>
public sealed class UiPrefs
{
    public string AccentColor { get; set; } = "#FFB703";   // màu nhấn (vàng)
    public string BaseColor { get; set; } = "#12090D";     // màu nền cửa sổ
    public string TextColor { get; set; } = "#F6E9EA";     // màu chữ

    public double GlassBrightness { get; set; } = 0.6;     // 0..1: độ sáng kính (càng cao càng sáng, bớt tối)
    public double CardOpacity { get; set; } = 1.0;         // 0.15..1: độ đặc của thẻ
    public double MenuOpacity { get; set; } = 1.0;         // 0.15..1: độ đặc của thanh menu
    public double RefractionStrength { get; set; } = 1.0;  // 0..2: độ mạnh khúc xạ
    public double BackgroundDim { get; set; } = 0.4;       // 0..0.9: độ tối phủ lên ảnh nền

    public NavDock MenuDock { get; set; } = NavDock.Top;
    public bool FancyEffects { get; set; }                 // hiệu ứng tương tác (theo chuột, ánh sáng chạy, kéo dãn menu)

    public UiPrefs Clone() => (UiPrefs)MemberwiseClone();

    public static UiPrefs From(AppConfig c)
    {
        var p = new UiPrefs
        {
            AccentColor = c.AccentColor,
            BaseColor = c.BaseColor,
            TextColor = c.TextColor,
            GlassBrightness = c.GlassBrightness,
            CardOpacity = c.CardOpacity,
            MenuOpacity = c.MenuOpacity,
            RefractionStrength = c.RefractionStrength,
            BackgroundDim = c.BackgroundDim,
            MenuDock = c.MenuDock,
            FancyEffects = c.FancyEffects
        };
        p.Sanitize();
        return p;
    }

    /// <summary>Kẹp mọi giá trị về khoảng hợp lệ; màu sai thì quay về mặc định (file cấu hình sửa tay không làm hỏng giao diện).</summary>
    public void Sanitize()
    {
        var d = new UiPrefs();
        if (!ThemePersonalizer.TryParseHex(AccentColor, out _)) AccentColor = d.AccentColor;
        if (!ThemePersonalizer.TryParseHex(BaseColor, out _)) BaseColor = d.BaseColor;
        if (!ThemePersonalizer.TryParseHex(TextColor, out _)) TextColor = d.TextColor;
        GlassBrightness = Clamp(GlassBrightness, 0, 1, d.GlassBrightness);
        CardOpacity = Clamp(CardOpacity, 0.15, 1, d.CardOpacity);
        MenuOpacity = Clamp(MenuOpacity, 0.15, 1, d.MenuOpacity);
        RefractionStrength = Clamp(RefractionStrength, 0, 2, d.RefractionStrength);
        BackgroundDim = Clamp(BackgroundDim, 0, 0.9, d.BackgroundDim);
        if (!Enum.IsDefined(MenuDock)) MenuDock = NavDock.Top;
    }

    private static double Clamp(double v, double lo, double hi, double fallback) =>
        double.IsNaN(v) || double.IsInfinity(v) ? fallback : Math.Min(hi, Math.Max(lo, v));
}

/// <summary>
/// Áp dụng cá nhân hóa lên giao diện đang chạy bằng cách đổi màu trực tiếp trên các brush dùng chung
/// (không sửa file XAML gốc). Mọi giá trị đều tính từ hằng số gốc, nên áp dụng nhiều lần không bị cộng dồn.
/// </summary>
internal static class ThemePersonalizer
{
    // ---------- Màu ----------

    public static bool TryParseHex(string? s, out Color c)
    {
        c = default;
        if (string.IsNullOrWhiteSpace(s)) return false;
        var t = s.Trim().TrimStart('#');
        if (t.Length == 3) t = string.Concat(t.Select(ch => $"{ch}{ch}"));
        if (t.Length != 6 || !t.All(Uri.IsHexDigit)) return false;
        c = Color.FromRgb(Convert.ToByte(t[..2], 16), Convert.ToByte(t[2..4], 16), Convert.ToByte(t[4..6], 16));
        return true;
    }

    public static string ToHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    private static Color Mix(Color a, Color b, double t) => Color.FromRgb(
        (byte)Math.Round(a.R + (b.R - a.R) * t),
        (byte)Math.Round(a.G + (b.G - a.G) * t),
        (byte)Math.Round(a.B + (b.B - a.B) * t));

    private static Color Lighten(Color c, double t) => Mix(c, Colors.White, t);

    private static double Linear(byte v)
    {
        var s = v / 255.0;
        return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
    }

    private static double Luminance(Color c) => 0.2126 * Linear(c.R) + 0.7152 * Linear(c.G) + 0.0722 * Linear(c.B);

    /// <summary>Tỉ lệ tương phản WCAG giữa hai màu (từ 1 tới 21; chữ thường cần từ 4.5).</summary>
    public static double Contrast(Color a, Color b)
    {
        var la = Luminance(a);
        var lb = Luminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    // ---------- Đổi màu tại chỗ ----------

    private static object? Res(string key) => Application.Current?.TryFindResource(key);

    /// <summary>
    /// Đặt màu cho brush: brush đặc thì dùng <paramref name="solid"/>, brush chuyển màu thì đặt màu từng điểm dừng
    /// (null = giữ nguyên điểm đó). Luôn giữ nguyên độ trong suốt (alpha) của điểm màu gốc, trừ khi có alpha chỉ định.
    /// </summary>
    private static void Paint(string key, Color solid, Color?[]? stops = null, byte?[]? alphas = null)
    {
        switch (Res(key))
        {
            case SolidColorBrush sb when !sb.IsFrozen:
                sb.Color = Color.FromArgb(alphas?.LastOrDefault() ?? sb.Color.A, solid.R, solid.G, solid.B);
                break;
            case GradientBrush gb when !gb.IsFrozen && stops != null:
                for (var i = 0; i < Math.Min(gb.GradientStops.Count, stops.Length); i++)
                {
                    if (stops[i] is not { } c) continue;
                    var st = gb.GradientStops[i];
                    st.Color = Color.FromArgb(alphas?[i] ?? st.Color.A, c.R, c.G, c.B);
                }
                break;
        }
    }

    private static byte A(double v) => (byte)Math.Round(Math.Min(255, Math.Max(0, v)));

    // ---------- Áp dụng ----------

    public static void Apply(UiPrefs p, bool glass)
    {
        var app = Application.Current;
        if (app == null) return;

        TryParseHex(p.AccentColor, out var accent);
        TryParseHex(p.BaseColor, out var back);
        TryParseHex(p.TextColor, out var text);
        var sun = Lighten(accent, 0.55);
        var light = Lighten(accent, 0.78);
        var muted = Mix(text, back, 0.25);
        var panel = Mix(back, Colors.White, 0.075);

        // Các số dùng chung (DynamicResource trong template)
        app.Resources["CardSurfaceOpacity"] = p.CardOpacity;
        app.Resources["NavSurfaceOpacity"] = p.MenuOpacity;
        app.Resources["BackdropDim"] = p.BackgroundDim;

        // --- Bảng màu chung (Theme.xaml) ---
        Paint("BgBrush", back);
        Paint("GoldBrush", accent);
        Paint("SunBrush", sun);
        Paint("TextBrush", text);
        Paint("MutedBrush", muted);
        Paint("GoldGradient", accent, new Color?[] { sun, accent });
        Paint("TitleGradient", accent, new Color?[] { sun, accent });
        Paint("EdgeBrush", accent, new Color?[] { null, accent });
        Paint("EdgeSoftBrush", accent, new Color?[] { null, accent });
        if (Res("GoldGlow") is DropShadowEffect gg && !gg.IsFrozen) gg.Color = accent;

        // --- Skin hiện tại ---
        // Nền cửa sổ
        Paint("WindowBg", Mix(back, Colors.White, 0.03),
            new Color?[] { Mix(back, Colors.White, 0.04), back, Mix(back, Colors.White, 0.015) });

        // Màu nhấn trong các lớp kính
        Paint("LgPrimaryFill", accent, new Color?[] { light, sun, accent });
        Paint("LgPrimaryEdge", accent, new Color?[] { null, sun });
        Paint("LgBubbleTint", accent, new Color?[] { light, Lighten(accent, 0.3), accent });
        Paint("LgThumbFill", sun, new Color?[] { null, sun, accent });
        if (glass)
        {
            Paint("LgEdge", sun, new Color?[] { null, sun });
            Paint("LgEdgeHot", sun, new Color?[] { null, sun });
            Paint("LgInputEdge", sun);
        }
        else
        {
            Paint("LgEdgeHot", sun);
        }
        if (Res("LgPrimaryGlow") is DropShadowEffect pg && !pg.IsFrozen) pg.Color = accent;

        // Nền của popup và ô nhập theo màu nền
        Paint("LgPopupFill", Mix(back, Colors.White, 0.08));
        Paint("LgPopupEdge", Lighten(accent, 0.45));

        // Độ sáng kính: tính từ giá trị gốc nên không cộng dồn
        //   b = 0.5 giống bản gốc; tăng b thì lớp trắng đậm hơn, các lớp tối (bóng đáy, đế bóng đổ) nhạt đi
        var b = p.GlassBrightness;
        var fill = 0.5 + b;          // hệ số cho lớp trắng của kính
        var dark = 1.5 - b;          // hệ số cho các lớp tối
        if (glass)
        {
            Paint("LgFill", Colors.White,
                new Color?[] { Colors.White, Color.FromRgb(0xFF, 0xE9, 0xC4), Color.FromRgb(0xE6, 0x39, 0x46), Colors.White },
                new byte?[] { A(0x3A * fill), A(0x18 * fill), A(0x0E * fill), A(0x2A * fill) });
            Paint("LgShadowCaster", Colors.Black, null, new byte?[] { A(0x3D * dark) });
            Paint("LgInnerShade", Colors.Black,
                new Color?[] { Colors.Black, Colors.Black }, new byte?[] { 0, A(0x2E * dark) });
        }
        else
        {
            // Chế độ phẳng: thẻ theo màu nền, hơi trong để ảnh nền (nếu có) còn thấy được
            Paint("LgFill", panel, null, new byte?[] { 0xD9 });
        }
    }
}
