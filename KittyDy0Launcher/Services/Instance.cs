using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace KittyDy0Launcher;

/// <summary>Nguồn Java. Global = lấy theo cài đặt chung của launcher.</summary>
public enum JavaSourceKind { Global, Auto, Mojang, Adoptium, Azul, Microsoft, System, Custom }

/// <summary>
/// Một instance = một bản cài tách biệt: phiên bản Minecraft riêng, thư mục game riêng
/// (saves, mods, resourcepacks, options...), RAM và Java riêng.
/// </summary>
public partial class Instance : ObservableObject
{
    /// <summary>Tên hiển thị, thứ tự phải khớp với enum JavaSourceKind.</summary>
    public static readonly string[] JavaSourceLabels =
    {
        "Theo cài đặt chung",
        "Tự động (khuyên dùng)",
        "Mojang (thư viện tự tải)",
        "Adoptium Temurin",
        "Azul Zulu",
        "Microsoft OpenJDK",
        "Java có sẵn trên máy",
        "Chọn file Java (java / java.exe) thủ công"
    };

    [ObservableProperty] private string id = Guid.NewGuid().ToString("N")[..8];
    [ObservableProperty] private string name = "Instance mới";
    [ObservableProperty] private string versionId = "";
    [ObservableProperty] private int ramMb = 4096;
    [ObservableProperty] private JavaSourceKind javaSource = JavaSourceKind.Global;
    [ObservableProperty] private string javaPath = "";
    [ObservableProperty] private string jvmArgs = "";
    [ObservableProperty] private bool autoJoin;
    [ObservableProperty] private DateTime created = DateTime.Now;
    [ObservableProperty] private DateTime? lastPlayed;

    // Mod loader (do nhập modpack cài vào). LoaderType rỗng = Minecraft thuần.
    // LaunchId là id phiên bản thực sự để chạy (vd fabric-loader-0.16.9-1.21.1), rỗng thì chạy VersionId.
    [ObservableProperty] private string loaderType = "";
    [ObservableProperty] private string loaderVersion = "";
    [ObservableProperty] private string launchId = "";

    [ObservableProperty]
    [property: JsonIgnore]
    private bool isInstalled;

    [JsonIgnore]
    public int RamGb
    {
        get => Math.Max(1, RamMb / 1024);
        set => RamMb = value * 1024;
    }

    [JsonIgnore]
    public string JavaSourceLabel
    {
        get => JavaSourceLabels[(int)JavaSource];
        set
        {
            var i = Array.IndexOf(JavaSourceLabels, value);
            if (i >= 0) JavaSource = (JavaSourceKind)i;
        }
    }

    [JsonIgnore] public string LoaderBadge => HasLoader ? " · " + LoaderInstaller.Label(LoaderType) : "";
    [JsonIgnore] public bool HasLoader => !string.IsNullOrEmpty(LoaderType);
    [JsonIgnore]
    public string LoaderLabel => HasLoader
        ? $"Mod loader: {LoaderInstaller.Label(LoaderType)} {LoaderVersion}".Trim()
        : "";

    [JsonIgnore] public bool IsCustomJava => JavaSource == JavaSourceKind.Custom;
    [JsonIgnore] public string InstalledText => IsInstalled ? "Đã tải" : "Chưa tải";
    [JsonIgnore] public string Dir => Path.Combine(AppPaths.Instances, Id);
    [JsonIgnore] public string GameDir => Path.Combine(Dir, "minecraft");

    partial void OnRamMbChanged(int value) => OnPropertyChanged(nameof(RamGb));

    partial void OnJavaSourceChanged(JavaSourceKind value)
    {
        OnPropertyChanged(nameof(JavaSourceLabel));
        OnPropertyChanged(nameof(IsCustomJava));
    }

    partial void OnIsInstalledChanged(bool value) => OnPropertyChanged(nameof(InstalledText));

    partial void OnLoaderTypeChanged(string value)
    {
        OnPropertyChanged(nameof(HasLoader));
        OnPropertyChanged(nameof(LoaderLabel));
        OnPropertyChanged(nameof(LoaderBadge));
    }

    partial void OnLoaderVersionChanged(string value) => OnPropertyChanged(nameof(LoaderLabel));
}
