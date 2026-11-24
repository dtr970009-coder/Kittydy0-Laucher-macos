using System.Text.Json;
using System.Text.Json.Serialization;

namespace KittyDy0Launcher;

/// <summary>Cách đăng nhập. Token KHÔNG nằm trong config.json (Microsoft do thư viện lưu, Ely.by mã hóa, xem SecureStore).</summary>
public enum AuthMode { Offline, Microsoft, ElyBy }

/// <summary>Cài đặt chung của launcher, lưu ở config.json.</summary>
public class AppConfig
{
    public string PlayerName { get; set; } = "";
    public AuthMode AuthMode { get; set; } = AuthMode.Offline;

    /// <summary>Tên tài khoản Microsoft gần nhất, chỉ để hiển thị.</summary>
    public string MicrosoftName { get; set; } = "";
    public JavaSourceKind JavaSource { get; set; } = JavaSourceKind.Auto;
    public int DefaultRamMb { get; set; } = 4096;
    public bool HideLauncherWhilePlaying { get; set; } = true;
    public string? LastInstanceId { get; set; }

    /// <summary>API key CurseForge (tùy chọn, tự điền vào config.json) để tải mod của modpack CurseForge.</summary>
    public string CurseForgeApiKey { get; set; } = "";

    /// <summary>Liquid Glass: true = kính (blur giả lập, viền sáng, animation), false = Dark Flat.</summary>
    public bool LiquidGlass { get; set; } = true;

    /// <summary>Khúc xạ thật bằng pixel shader trong chế độ Liquid Glass.</summary>
    public bool Refraction { get; set; } = true;

    /// <summary>Đường dẫn ảnh nền tuỳ chọn (hiện sau lớp kính nên khúc xạ thấy rõ); rỗng = nền mặc định.</summary>
    public string BackgroundImage { get; set; } = "";

    // ----- Cá nhân hóa giao diện (xem UiPrefs) -----
    public string AccentColor { get; set; } = "#FFB703";
    public string BaseColor { get; set; } = "#12090D";
    public string TextColor { get; set; } = "#F6E9EA";
    public double GlassBrightness { get; set; } = 0.6;
    public double CardOpacity { get; set; } = 1.0;
    public double MenuOpacity { get; set; } = 1.0;
    public double RefractionStrength { get; set; } = 1.0;
    public double BackgroundDim { get; set; } = 0.4;
    public NavDock MenuDock { get; set; } = NavDock.Top;
    public bool FancyEffects { get; set; }

    /// <summary>Giảm chuyển động: tắt mọi animation của giao diện.</summary>
    public bool ReduceMotion { get; set; }

    /// <summary>Cho Windows mở file .mrpack bằng launcher (đăng ký trong HKCU).</summary>
    public bool AssociateMrpack { get; set; } = true;

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static AppConfig Load()
    {
        try
        {
            if (File.Exists(AppPaths.ConfigFile))
            {
                var cfg = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(AppPaths.ConfigFile), JsonOptions);
                if (cfg != null)
                {
                    if (cfg.JavaSource == JavaSourceKind.Global) cfg.JavaSource = JavaSourceKind.Auto;
                    return cfg;
                }
            }
        }
        catch { /* file hỏng thì dùng mặc định */ }
        return new AppConfig();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(AppPaths.Root);
            File.WriteAllText(AppPaths.ConfigFile, JsonSerializer.Serialize(this, JsonOptions));
        }
        catch { /* không ghi được thì bỏ qua */ }
    }
}
