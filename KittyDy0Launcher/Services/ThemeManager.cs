using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using KittyDy0Launcher.Controls;

namespace KittyDy0Launcher;

/// <summary>
/// Quản lý giao diện: Liquid Glass (kính nhiều lớp, viền sáng, ánh sáng chuyển động) hay Dark Flat (phẳng, không hiệu ứng).
/// Hai chế độ là hai ResourceDictionary "skin" có cùng bộ key (Themes/Skin.Glass.xaml, Themes/Skin.Flat.xaml).
/// Đổi chế độ chỉ là thay skin lúc đang chạy nên bố cục và chức năng giữ nguyên.
/// </summary>
public static class ThemeManager
{
    private static ResourceDictionary? _skin;

    public static bool GlassOn { get; private set; } = true;
    public static bool ReduceMotion { get; private set; }

    /// <summary>Thiết lập cá nhân hóa hiện tại (màu, độ sáng, độ trong, khúc xạ, vị trí menu...).</summary>
    public static UiPrefs Prefs { get; private set; } = new();

    /// <summary>Hiệu ứng tương tác: điểm sáng theo chuột, ánh sáng chạy, nảy/kéo dãn khi bấm và khi chuyển menu. Mặc định tắt.</summary>
    public static bool FancyEffects => Prefs.FancyEffects;

    /// <summary>Tăng mỗi khi đổi skin: ảnh chụp nền cũ không còn đúng và phải chụp lại.</summary>
    public static int Version { get; private set; }

    /// <summary>Báo rằng khúc xạ đã bị tắt do lỗi (để màn hình Cài đặt cập nhật công tắc).</summary>
    public static event Action? RefractionForcedOff;

    /// <summary>Tắt khúc xạ ngay vì đã xảy ra lỗi: không để launcher tiếp tục dùng hiệu ứng nghi gây lỗi.</summary>
    public static void TurnRefractionOff()
    {
        if (!RefractionOn) return;
        RefractionOn = false;
        Changed?.Invoke();
        RefractionForcedOff?.Invoke();
    }

    /// <summary>Khúc xạ thật bằng pixel shader (chỉ có tác dụng khi Liquid Glass bật và shader nạp được).</summary>
    public static bool RefractionOn { get; private set; }

    /// <summary>Thông báo cần hiện cho người dùng lúc mở (vd: khúc xạ đã tự tắt vì lần trước bị lỗi).</summary>
    public static string? RefractionNotice { get; private set; }

    private static string MarkerPath => Path.Combine(AppPaths.Root, "refraction.testing");
    private static int _healthFrames;

    /// <summary>true nếu máy chỉ render bằng phần mềm (không có GPU hỗ trợ): tắt hiệu ứng ánh sáng chạy nền cho nhẹ.</summary>
    public static bool LowGpu { get; private set; }

    /// <summary>false khi người dùng bật "Giảm chuyển động" hoặc Windows tắt animation.</summary>
    public static bool MotionEnabled => !ReduceMotion && SystemParameters.ClientAreaAnimation;

    /// <summary>Hiệu ứng ánh sáng chạy liên tục chỉ bật khi có animation và có GPU.</summary>
    public static bool AmbientMotion => MotionEnabled && !LowGpu;

    /// <summary>Phát ra sau khi đổi chế độ; các control tự cập nhật lại.</summary>
    public static event Action? Changed;

    /// <summary>Gọi một lần lúc khởi động, trước khi tạo cửa sổ.</summary>
    public static void Initialize(AppConfig cfg)
    {
        GlassOn = cfg.LiquidGlass;
        ReduceMotion = cfg.ReduceMotion;
        Prefs = UiPrefs.From(cfg);
        RefractionOn = cfg.Refraction;

        // Bảo vệ: nếu lần chạy trước bật khúc xạ mà chưa kịp dựng ổn định đã thoát (file dấu còn đó), tự tắt khúc xạ
        try
        {
            if (cfg.Refraction && File.Exists(MarkerPath))
            {
                RefractionOn = false;
                cfg.Refraction = false;
                cfg.Save();
                File.Delete(MarkerPath);
                RefractionNotice = "Khúc xạ đã tự tắt vì lần chạy trước launcher bị lỗi khi dựng hiệu ứng này. " +
                                   "Bạn có thể bật lại trong Cài đặt, mục Giao diện.";
            }
            else if (RefractionOn)
            {
                BeginHealthCheck();
            }
        }
        catch { /* không ghi được file dấu thì bỏ qua bảo vệ */ }

        try { LowGpu = (RenderCapability.Tier >> 16) < 1; } catch { LowGpu = false; }
        LoadSkin();
    }

    public static void Apply(bool glass, bool reduceMotion, bool refraction)
    {
        GlassOn = glass;
        ReduceMotion = reduceMotion;
        var enableNow = refraction && !RefractionOn;
        RefractionOn = refraction;
        LoadSkin();
        if (enableNow) { try { BeginHealthCheck(); } catch { } }
        Changed?.Invoke();
    }

    /// <summary>Ghi file dấu rồi xoá sau 40 khung hình dựng ổn định; nếu launcher chết giữa chừng, lần sau sẽ tự tắt khúc xạ.</summary>
    private static void BeginHealthCheck()
    {
        File.WriteAllText(MarkerPath, "1");
        _healthFrames = 0;
        CompositionTarget.Rendering -= OnHealthFrame;
        CompositionTarget.Rendering += OnHealthFrame;
    }

    private static void OnHealthFrame(object? sender, EventArgs e)
    {
        if (++_healthFrames < 40) return;
        CompositionTarget.Rendering -= OnHealthFrame;
        try { File.Delete(MarkerPath); } catch { }
    }

    private static void LoadSkin()
    {
        var app = Application.Current;
        if (app == null) return;

        var name = GlassOn ? "Skin.Glass.xaml" : "Skin.Flat.xaml";
        var dict = new ResourceDictionary { Source = new Uri($"pack://application:,,,/Themes/{name}", UriKind.Absolute) };

        var merged = app.Resources.MergedDictionaries;
        if (_skin != null) merged.Remove(_skin);
        merged.Add(dict);
        _skin = dict;
        Version++;
        ApplyPrefs();
    }

    // ---------- Cá nhân hóa ----------

    private static DispatcherTimer? _coalesce;
    private static bool _pending;

    /// <summary>Áp dụng cá nhân hóa lên skin đang dùng (skin mới nạp luôn có màu gốc nên phải áp dụng lại).</summary>
    private static void ApplyPrefs()
    {
        try
        {
            ThemePersonalizer.Apply(Prefs, GlassOn);
            LensRenderer.StrengthScale = (float)Prefs.RefractionStrength;
            // Độ sương trắng của thấu kính đi theo độ sáng kính: 0.5 => 0.06 như bản gốc
            LensRenderer.TintAmount = (float)Math.Clamp(0.06 + (Prefs.GlassBrightness - 0.5) * 0.12, 0.0, 0.2);
        }
        catch (Exception ex)
        {
            CrashLog.Write(ex, "Personalize"); // lỗi cá nhân hóa không được làm hỏng giao diện
        }
    }

    /// <summary>Đặt thiết lập mới và áp dụng ngay. Nhiều lần gọi liên tiếp (kéo thanh trượt) được gom lại thành một lần báo thay đổi.</summary>
    public static void SetPrefs(UiPrefs p)
    {
        var old = Prefs;
        p.Sanitize();
        Prefs = p;
        ApplyPrefs();

        // Màu nền hoặc độ tối ảnh nền đổi thì ảnh chụp nền của thấu kính phải chụp lại
        if (old.BaseColor != p.BaseColor || Math.Abs(old.BackgroundDim - p.BackgroundDim) > 1e-6) Version++;

        _pending = true;
        _coalesce ??= CreateCoalesceTimer();
        if (!_coalesce.IsEnabled) _coalesce.Start();
    }

    private static DispatcherTimer CreateCoalesceTimer()
    {
        var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(60) };
        t.Tick += (_, _) =>
        {
            t.Stop();
            if (!_pending) return;
            _pending = false;
            Changed?.Invoke();
        };
        return t;
    }
}
