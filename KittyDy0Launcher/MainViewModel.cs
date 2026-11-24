using KittyDy0Launcher.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Media;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Threading;
using CmlLib.Core.Auth;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;

namespace KittyDy0Launcher;

/// <summary>ViewModel của cửa sổ chính: instance, chạy game, Java, nhật ký và cài đặt chung.</summary>
public partial class MainViewModel : ObservableObject
{
    private readonly AppConfig _config;
    private readonly VersionCatalog _catalog = new();
    private readonly GameRunner _runner;
    private readonly DispatcherTimer _saveTimer;
    private readonly DispatcherTimer _logTimer;
    private readonly HashSet<Instance> _dirty = new();
    private readonly StringBuilder _logSb = new();
    private readonly object _logLock = new();
    private bool _logDirty;
    private bool _loading = true;
    private CancellationTokenSource? _cts;
    private Process? _game;
    private readonly MicrosoftAuth? _ms;
    private readonly ElyByAuth _ely = new();

    /// <summary>true = game vừa chạy, false = game đã thoát.</summary>
    public event Action<bool>? GameStateChanged;

    public ObservableCollection<Instance> Instances { get; } = new();
    public ObservableCollection<string> Versions { get; } = new();

    public string[] InstanceJavaLabels => Instance.JavaSourceLabels;
    public string[] GlobalJavaLabels { get; } = Instance.JavaSourceLabels.Skip(1).ToArray();
    public string[] JavaMajors { get; } = { "8", "17", "21", "25" };

    [ObservableProperty] private string playerName = "";

    // ----- Tích hợp Windows -----
    [ObservableProperty] private bool mrpackAssociated;

    partial void OnMrpackAssociatedChanged(bool value)
    {
        if (_loading) return;
        _config.AssociateMrpack = value;
        _config.Save();
        var ok = value ? FileAssociation.Register(force: true) : FileAssociation.Unregister();
        StatusText = !ok
            ? "Không ghi được đăng ký file .mrpack vào Windows."
            : value
                ? "Đã đăng ký: bấm đúp file .mrpack sẽ mở KittyDy0 Launcher."
                : "Đã gỡ đăng ký file .mrpack khỏi launcher.";
    }

    // ----- Giao diện -----
    [ObservableProperty] private bool liquidGlassOn = true;
    [ObservableProperty] private bool reduceMotion;

    partial void OnLiquidGlassOnChanged(bool value)
    {
        if (_loading) return;
        _config.LiquidGlass = value;
        _config.Save();
        ThemeManager.Apply(value, ReduceMotion, Refraction);
    }

    [ObservableProperty] private bool refraction = true;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasBackgroundImage))]
    [NotifyPropertyChangedFor(nameof(ShowDefaultArt))]
    private ImageSource? backgroundImage;

    public bool HasBackgroundImage => BackgroundImage != null;
    public bool ShowDefaultArt => BackgroundImage == null;

    /// <summary>Khúc xạ tính bằng CPU nên máy nào cũng dùng được; giữ thuộc tính này để khóa công tắc nếu cần.</summary>
    public bool RefractionAvailable => true;

    partial void OnRefractionChanged(bool value)
    {
        if (_loading) return;
        _config.Refraction = value;
        _config.Save();
        ThemeManager.Apply(LiquidGlassOn, ReduceMotion, value);
    }

    private static ImageSource? LoadImage(string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
            var bi = new BitmapImage();
            bi.BeginInit();
            bi.CacheOption = BitmapCacheOption.OnLoad;   // không giữ khóa file
            bi.DecodePixelWidth = 1400;                  // giải mã nhỏ lại cho nhẹ bộ nhớ
            bi.UriSource = new Uri(path);
            bi.EndInit();
            bi.Freeze();
            return bi;
        }
        catch { return null; }
    }

    [RelayCommand]
    private void ChooseBackground()
    {
        var dlg = new OpenFileDialog { Title = "Chọn ảnh nền", Filter = "Ảnh (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp" };
        if (dlg.ShowDialog() != true) return;
        var img = LoadImage(dlg.FileName);
        if (img == null) { StatusText = "Không đọc được ảnh này."; return; }
        _config.BackgroundImage = dlg.FileName;
        _config.Save();
        BackgroundImage = img;
        StatusText = "Đã đặt ảnh nền (hiện ở chế độ Liquid Glass).";
    }

    [RelayCommand]
    private void ClearBackground()
    {
        _config.BackgroundImage = "";
        _config.Save();
        BackgroundImage = null;
        StatusText = "Đã xóa ảnh nền.";
    }

    partial void OnReduceMotionChanged(bool value)
    {
        if (_loading) return;
        _config.ReduceMotion = value;
        _config.Save();
        ThemeManager.Apply(LiquidGlassOn, value, Refraction);
    }

    // ----- Cá nhân hóa -----
    public string[] MenuDockLabels { get; } = { "Trên", "Dưới", "Trái", "Phải" }; // cùng thứ tự với NavDock

    [ObservableProperty] private string menuDockLabel = "Trên";
    [ObservableProperty] private string accentHex = "#FFB703";
    [ObservableProperty] private string baseHex = "#12090D";
    [ObservableProperty] private string textHex = "#F6E9EA";
    [ObservableProperty] private Brush accentSwatch = Brushes.Transparent;
    [ObservableProperty] private Brush baseSwatch = Brushes.Transparent;
    [ObservableProperty] private Brush textSwatch = Brushes.Transparent;
    [ObservableProperty] private double glassBrightness = 0.6;
    [ObservableProperty] private double cardOpacity = 1.0;
    [ObservableProperty] private double menuOpacity = 1.0;
    [ObservableProperty] private double refractionStrength = 1.0;
    [ObservableProperty] private double backgroundDim = 0.4;
    [ObservableProperty] private bool fancyEffects;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasContrastWarning))]
    private string contrastWarning = "";

    public bool HasContrastWarning => ContrastWarning.Length > 0;

    private bool _applyingPrefs;
    private DispatcherTimer? _cfgSaveTimer;

    partial void OnMenuDockLabelChanged(string value) => PrefsChanged();
    partial void OnGlassBrightnessChanged(double value) => PrefsChanged();
    partial void OnCardOpacityChanged(double value) => PrefsChanged();
    partial void OnMenuOpacityChanged(double value) => PrefsChanged();
    partial void OnRefractionStrengthChanged(double value) => PrefsChanged();
    partial void OnBackgroundDimChanged(double value) => PrefsChanged();
    partial void OnFancyEffectsChanged(bool value) => PrefsChanged();

    partial void OnAccentHexChanged(string value) => HexChanged(value, c => _config.AccentColor = c, b => AccentSwatch = b);
    partial void OnBaseHexChanged(string value) => HexChanged(value, c => _config.BaseColor = c, b => BaseSwatch = b);
    partial void OnTextHexChanged(string value) => HexChanged(value, c => _config.TextColor = c, b => TextSwatch = b);

    /// <summary>Mã HEX hợp lệ thì cập nhật ô màu mẫu và áp dụng ngay; đang gõ dở (chưa hợp lệ) thì giữ màu cũ.</summary>
    private void HexChanged(string text, Action<string> store, Action<Brush> swatch)
    {
        if (!ThemePersonalizer.TryParseHex(text, out var c)) return;
        var brush = new SolidColorBrush(c);
        brush.Freeze();
        swatch(brush);
        if (_loading) return;
        store(ThemePersonalizer.ToHex(c));
        PrefsChanged();
    }

    private UiPrefs BuildPrefs() => new()
    {
        AccentColor = _config.AccentColor,
        BaseColor = _config.BaseColor,
        TextColor = _config.TextColor,
        GlassBrightness = GlassBrightness,
        CardOpacity = CardOpacity,
        MenuOpacity = MenuOpacity,
        RefractionStrength = RefractionStrength,
        BackgroundDim = BackgroundDim,
        MenuDock = (NavDock)Math.Max(0, Array.IndexOf(MenuDockLabels, MenuDockLabel)),
        FancyEffects = FancyEffects
    };

    /// <summary>Áp dụng ngay thiết lập cá nhân hóa và lưu vào cấu hình (gom lại để không ghi file mỗi lần kéo thanh trượt).</summary>
    private void PrefsChanged()
    {
        if (_loading || _applyingPrefs) return;
        try
        {
            var p = BuildPrefs();
            ThemeManager.SetPrefs(p);

            _config.GlassBrightness = p.GlassBrightness;
            _config.CardOpacity = p.CardOpacity;
            _config.MenuOpacity = p.MenuOpacity;
            _config.RefractionStrength = p.RefractionStrength;
            _config.BackgroundDim = p.BackgroundDim;
            _config.MenuDock = p.MenuDock;
            _config.FancyEffects = p.FancyEffects;
            QueueConfigSave();
            UpdateContrast(p);
        }
        catch (Exception ex)
        {
            CrashLog.Write(ex, "PrefsChanged");
        }
    }

    private void QueueConfigSave()
    {
        _cfgSaveTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _cfgSaveTimer.Tick -= OnCfgSaveTick;
        _cfgSaveTimer.Tick += OnCfgSaveTick;
        _cfgSaveTimer.Stop();
        _cfgSaveTimer.Start();
    }

    private void OnCfgSaveTick(object? sender, EventArgs e)
    {
        _cfgSaveTimer?.Stop();
        _config.Save();
    }

    /// <summary>Cảnh báo nếu chữ khó đọc trên nền (dưới 4.5:1) hoặc màu nhấn quá tối cho chữ trên nút chính.</summary>
    private void UpdateContrast(UiPrefs p)
    {
        ThemePersonalizer.TryParseHex(p.TextColor, out var text);
        ThemePersonalizer.TryParseHex(p.BaseColor, out var back);
        ThemePersonalizer.TryParseHex(p.AccentColor, out var accent);

        var parts = new List<string>();
        var r = ThemePersonalizer.Contrast(text, back);
        if (r < 4.5) parts.Add($"Chữ khó đọc trên nền (tương phản {r:0.0}:1, nên từ 4.5:1).");
        var ra = ThemePersonalizer.Contrast(accent, System.Windows.Media.Color.FromRgb(0x1B, 0x0A, 0x0D));
        if (ra < 4.5) parts.Add($"Màu nhấn khá tối nên chữ trên nút chính khó đọc (tương phản {ra:0.0}:1).");
        ContrastWarning = string.Join(" ", parts);
    }

    private static readonly Dictionary<string, (string Accent, string Base, string Text)> ColorPresets = new()
    {
        ["Mặc định"] = ("#FFB703", "#12090D", "#F6E9EA"),
        ["Hồng"] = ("#FF5FA8", "#14080F", "#FFEAF3"),
        ["Xanh biển"] = ("#38BDF8", "#07111A", "#E6F4FF"),
        ["Xanh lá"] = ("#3DDC84", "#07140E", "#E8FFF2"),
        ["Tím"] = ("#A78BFA", "#0E0A18", "#F1ECFF"),
        ["Đỏ"] = ("#E63946", "#140808", "#FFECEC")
    };

    [RelayCommand]
    private void ApplyColorPreset(string? name)
    {
        if (name == null || !ColorPresets.TryGetValue(name, out var c)) return;
        _applyingPrefs = true;
        try { AccentHex = c.Accent; BaseHex = c.Base; TextHex = c.Text; }
        finally { _applyingPrefs = false; }
        PrefsChanged();
    }

    /// <summary>Đưa toàn bộ cá nhân hóa về mặc định (giao diện như bản gốc).</summary>
    [RelayCommand]
    private void ResetPersonalization()
    {
        var d = new UiPrefs();
        _applyingPrefs = true;
        try
        {
            AccentHex = d.AccentColor; BaseHex = d.BaseColor; TextHex = d.TextColor;
            GlassBrightness = d.GlassBrightness; CardOpacity = d.CardOpacity; MenuOpacity = d.MenuOpacity;
            RefractionStrength = d.RefractionStrength; BackgroundDim = d.BackgroundDim;
            MenuDockLabel = MenuDockLabels[(int)d.MenuDock];
            FancyEffects = d.FancyEffects;
        }
        finally { _applyingPrefs = false; }
        PrefsChanged();
        StatusText = "Đã đặt lại cá nhân hóa về mặc định.";
    }

    /// <summary>Mở thư mục con (mods, resourcepacks...) của instance đang chọn trong Explorer.</summary>
    [RelayCommand]
    private void OpenInstanceSubFolder(string? sub)
    {
        var inst = SelectedInstance;
        if (inst == null) { StatusText = "Chưa chọn instance."; return; }
        if (sub != null && (sub.Contains("..") || sub.Contains('/') || sub.Contains('\\'))) return;
        try
        {
            var dir = string.IsNullOrEmpty(sub) ? inst.GameDir : Path.Combine(inst.GameDir, sub);
            Directory.CreateDirectory(dir);
            Process.Start(new ProcessStartInfo(dir) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            StatusText = "Không mở được thư mục: " + ex.Message;
        }
    }

    // ----- Tài khoản -----
    public string[] AuthModeLabels { get; } = { "Offline", "Microsoft", "Ely.by" };

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOfflineMode))]
    [NotifyPropertyChangedFor(nameof(IsOnlineMode))]
    [NotifyPropertyChangedFor(nameof(HomeAccountText))]
    private string authLabel = "Offline";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsMicrosoftSignedIn))]
    [NotifyPropertyChangedFor(nameof(IsMicrosoftSignedOut))]
    [NotifyPropertyChangedFor(nameof(HomeAccountText))]
    private string msAccountName = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsElySignedIn))]
    [NotifyPropertyChangedFor(nameof(IsElySignedOut))]
    [NotifyPropertyChangedFor(nameof(HomeAccountText))]
    private string elyAccountName = "";

    [ObservableProperty] private string elyUser = "";
    [ObservableProperty] private string elyTotp = "";
    [ObservableProperty] private string authMessage = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PlayCommand))]
    [NotifyCanExecuteChangedFor(nameof(MicrosoftSignInCommand))]
    [NotifyCanExecuteChangedFor(nameof(MicrosoftSignOutCommand))]
    [NotifyCanExecuteChangedFor(nameof(ElyLoginCommand))]
    [NotifyCanExecuteChangedFor(nameof(ElyLogoutCommand))]
    private bool isAuthBusy;

    /// <summary>Mật khẩu Ely.by: chỉ đi qua bộ nhớ một lúc ngắn, không lưu, được xóa ngay khi đăng nhập.</summary>
    public string ElyPassword { get; set; } = "";

    public AuthMode CurrentAuth
    {
        get
        {
            var i = Array.IndexOf(AuthModeLabels, AuthLabel);
            return i < 0 ? AuthMode.Offline : (AuthMode)i;
        }
    }

    public bool IsOfflineMode => CurrentAuth == AuthMode.Offline;
    public bool IsOnlineMode => CurrentAuth != AuthMode.Offline;
    public bool IsMicrosoftSignedIn => MsAccountName.Length > 0;
    public bool IsMicrosoftSignedOut => MsAccountName.Length == 0;
    public bool IsElySignedIn => ElyAccountName.Length > 0;
    public bool IsElySignedOut => ElyAccountName.Length == 0;

    /// <summary>Dòng trạng thái tài khoản hiện ở Trang chủ khi dùng Microsoft hoặc Ely.by.</summary>
    public string HomeAccountText => CurrentAuth switch
    {
        AuthMode.Microsoft => IsMicrosoftSignedIn
            ? $"Đã đăng nhập Microsoft: {MsAccountName}"
            : "Chưa đăng nhập Microsoft. Vào tab Tài khoản để đăng nhập.",
        AuthMode.ElyBy => IsElySignedIn
            ? $"Đã đăng nhập Ely.by: {ElyAccountName}"
            : "Chưa đăng nhập Ely.by. Vào tab Tài khoản để đăng nhập.",
        _ => ""
    };

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasInstance))]
    [NotifyPropertyChangedFor(nameof(NoInstance))]
    [NotifyCanExecuteChangedFor(nameof(PlayCommand))]
    [NotifyCanExecuteChangedFor(nameof(InstallLoaderCommand))]
    private Instance? selectedInstance;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PlayCommand))]
    [NotifyCanExecuteChangedFor(nameof(StopCommand))]
    [NotifyCanExecuteChangedFor(nameof(InstallJavaCommand))]
    [NotifyCanExecuteChangedFor(nameof(ImportModpackCommand))]
    [NotifyCanExecuteChangedFor(nameof(InstallLoaderCommand))]
    [NotifyCanExecuteChangedFor(nameof(InstallModCommand))]
    [NotifyCanExecuteChangedFor(nameof(InstallSelectedModVersionCommand))]
    private bool isBusy;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PlayCommand))]
    [NotifyCanExecuteChangedFor(nameof(StopCommand))]
    [NotifyCanExecuteChangedFor(nameof(InstallJavaCommand))]
    [NotifyCanExecuteChangedFor(nameof(ImportModpackCommand))]
    [NotifyCanExecuteChangedFor(nameof(InstallLoaderCommand))]
    [NotifyCanExecuteChangedFor(nameof(InstallModCommand))]
    [NotifyCanExecuteChangedFor(nameof(InstallSelectedModVersionCommand))]
    private bool isGameRunning;

    [ObservableProperty] private double progress;
    [ObservableProperty] private string statusText = "Sẵn sàng";
    [ObservableProperty] private string logText = "";
    [ObservableProperty] private string versionInfo = "";

    // Bộ lọc loại phiên bản trong trình chỉnh instance
    [ObservableProperty] private bool showRelease = true;
    [ObservableProperty] private bool showSnapshot;
    [ObservableProperty] private bool showBeta;
    [ObservableProperty] private bool showAlpha;

    // Cài đặt chung
    [ObservableProperty] private string globalJavaLabel = Instance.JavaSourceLabels[(int)JavaSourceKind.Auto];
    [ObservableProperty] private int defaultRamGb = 4;
    [ObservableProperty] private bool hideOnPlay = true;
    [ObservableProperty] private string javaInstallMajor = "17";
    [ObservableProperty] private string managedJavaText = "";

    // ----- Mod loader (chọn và cài trong trình chỉnh sửa instance) -----
    private const string LatestLoaderLabel = "Mới nhất (khuyên dùng)";
    private bool _syncingLoader;
    private int _loaderListStamp;

    public string[] LoaderTypeLabels { get; } = { "Không (Minecraft thuần)", "Fabric", "Quilt", "Forge", "NeoForge" };
    public ObservableCollection<string> LoaderVersionList { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLoaderChosen))]
    [NotifyCanExecuteChangedFor(nameof(InstallLoaderCommand))]
    private string loaderTypeLabel = "Không (Minecraft thuần)";

    [ObservableProperty] private string? loaderVersionChoice;
    [ObservableProperty] private string loaderListStatus = "";

    public bool IsLoaderChosen => LoaderTypeLabel != LoaderTypeLabels[0];

    /// <summary>Loại loader đang chọn ở dạng khóa ("fabric", "quilt", "forge", "neoforge"); rỗng nếu chọn Không.</summary>
    private string CurrentLoaderType => IsLoaderChosen ? LoaderTypeLabel.ToLowerInvariant() : "";

    public bool HasInstance => SelectedInstance != null;
    public bool NoInstance => SelectedInstance == null;

    /// <summary>Phiên bản của instance đang chọn (bỏ qua giá trị rỗng khi danh sách đang được lọc lại).</summary>
    public string? EditVersion
    {
        get => SelectedInstance?.VersionId;
        set
        {
            if (SelectedInstance != null && !string.IsNullOrEmpty(value) && value != SelectedInstance.VersionId)
            {
                SelectedInstance.VersionId = value;
                if (SelectedInstance.HasLoader)
                {
                    // Loader cài cho bản Minecraft cũ, không dùng được với bản mới
                    AppendLog($"Đổi phiên bản Minecraft nên bỏ mod loader {SelectedInstance.LoaderLabel} của \"{SelectedInstance.Name}\".");
                    SelectedInstance.LoaderType = "";
                    SelectedInstance.LoaderVersion = "";
                    SelectedInstance.LaunchId = "";
                }
                OnPropertyChanged();
                _ = RefreshLoaderVersionsAsync();
            }
        }
    }

    public MainViewModel()
    {
        AppPaths.EnsureAll();
        _config = AppConfig.Load();
        _runner = new GameRunner(_catalog, AppendLog);

        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        _saveTimer.Tick += (_, _) => FlushSave();
        _logTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _logTimer.Tick += (_, _) => FlushLog();
        _logTimer.Start();

        PlayerName = _config.PlayerName;
        MrpackAssociated = FileAssociation.IsRegistered();
        Refraction = _config.Refraction;
        {
            var up = UiPrefs.From(_config);   // đã kẹp giá trị hợp lệ
            _config.AccentColor = up.AccentColor; _config.BaseColor = up.BaseColor; _config.TextColor = up.TextColor;
            AccentHex = up.AccentColor; BaseHex = up.BaseColor; TextHex = up.TextColor;
            GlassBrightness = up.GlassBrightness; CardOpacity = up.CardOpacity; MenuOpacity = up.MenuOpacity;
            RefractionStrength = up.RefractionStrength; BackgroundDim = up.BackgroundDim;
            MenuDockLabel = MenuDockLabels[(int)up.MenuDock];
            FancyEffects = up.FancyEffects;
            UpdateContrast(up);
        }
        // Khi có lỗi chưa xử lý lúc đang bật khúc xạ, App tắt hiệu ứng này; đồng bộ lại công tắc và cấu hình
        ThemeManager.RefractionForcedOff += () => Application.Current.Dispatcher.Invoke(() => { Refraction = false; });
        BackgroundImage = LoadImage(_config.BackgroundImage);
        LiquidGlassOn = _config.LiquidGlass;   // ThemeManager đã nạp skin từ App.OnStartup
        ReduceMotion = _config.ReduceMotion;
        AuthLabel = AuthModeLabels[(int)_config.AuthMode];
        MsAccountName = _config.MicrosoftName;
        ElyAccountName = _ely.Account?.Username ?? "";
        try { _ms = new MicrosoftAuth(); }
        catch (Exception ex) { AppendLog("Không khởi tạo được đăng nhập Microsoft: " + ex.Message); }
        GlobalJavaLabel = Instance.JavaSourceLabels[(int)_config.JavaSource];
        DefaultRamGb = Math.Max(2, _config.DefaultRamMb / 1024);
        HideOnPlay = _config.HideLauncherWhilePlaying;

        foreach (var i in InstanceStore.LoadAll()) AddInstance(i);
        if (Instances.Count == 0)
        {
            var first = new Instance { Name = "Mặc định", VersionId = "1.21.4", RamMb = _config.DefaultRamMb };
            InstanceStore.Save(first);
            AddInstance(first);
        }
        SelectedInstance = Instances.FirstOrDefault(i => i.Id == _config.LastInstanceId) ?? Instances[0];

        _loading = false;
        ApplyFilter();
        RefreshManagedJava();
        _ = LoadVersionsAsync();
        _ = RestoreMicrosoftAsync();
    }

    // ---------- Thay đổi cài đặt ----------

    partial void OnPlayerNameChanged(string value)
    {
        if (_loading) return;
        _config.PlayerName = value;
        _config.Save();
    }

    partial void OnAuthLabelChanged(string value)
    {
        if (_loading) return;
        _config.AuthMode = CurrentAuth;
        _config.Save();
    }

    partial void OnSelectedInstanceChanged(Instance? value)
    {
        if (!_loading && value != null)
        {
            _config.LastInstanceId = value.Id;
            _config.Save();
        }
        ApplyFilter();
        SyncLoaderSelection(value);
        RefreshModCompat();
        OnPropertyChanged(nameof(ModTargetText));
    }

    partial void OnLoaderTypeLabelChanged(string value)
    {
        if (_syncingLoader) return;
        _ = RefreshLoaderVersionsAsync();
    }

    partial void OnGlobalJavaLabelChanged(string value)
    {
        if (_loading) return;
        var i = Array.IndexOf(Instance.JavaSourceLabels, value);
        if (i > 0)
        {
            _config.JavaSource = (JavaSourceKind)i;
            _config.Save();
        }
    }

    partial void OnDefaultRamGbChanged(int value)
    {
        if (_loading) return;
        _config.DefaultRamMb = value * 1024;
        _config.Save();
    }

    partial void OnHideOnPlayChanged(bool value)
    {
        if (_loading) return;
        _config.HideLauncherWhilePlaying = value;
        _config.Save();
    }

    partial void OnShowReleaseChanged(bool value) => ApplyFilter();
    partial void OnShowSnapshotChanged(bool value) => ApplyFilter();
    partial void OnShowBetaChanged(bool value) => ApplyFilter();
    partial void OnShowAlphaChanged(bool value) => ApplyFilter();

    // ---------- Danh sách phiên bản ----------

    private async Task LoadVersionsAsync()
    {
        var src = await _catalog.LoadAsync();
        if (!IsBusy && !IsGameRunning)
        {
            StatusText = src switch
            {
                CatalogSource.Cache => "Không có mạng, dùng danh sách phiên bản đã lưu.",
                CatalogSource.BuiltIn => "Không tải được danh sách phiên bản, đang dùng danh sách tạm.",
                _ => "Sẵn sàng"
            };
        }
        foreach (var i in Instances) i.IsInstalled = VersionCatalog.IsInstalled(i.VersionId);
        ApplyFilter();
    }

    /// <summary>Lọc theo loại đã bật (mới nhất → cổ nhất), luôn giữ phiên bản của instance đang chọn trong danh sách.</summary>
    private void ApplyFilter()
    {
        var ids = _catalog.All.Where(v => v.Type switch
        {
            "release" => ShowRelease,
            "snapshot" => ShowSnapshot,
            "old_beta" => ShowBeta,
            "old_alpha" => ShowAlpha,
            _ => false
        }).Select(v => v.Id).ToList();

        var cur = SelectedInstance?.VersionId;
        if (!string.IsNullOrEmpty(cur) && !ids.Contains(cur)) ids.Insert(0, cur);

        Versions.Clear();
        foreach (var id in ids) Versions.Add(id);

        VersionInfo = $"{ids.Count} phiên bản";
        OnPropertyChanged(nameof(EditVersion));
    }

    // ---------- Instance ----------

    private void AddInstance(Instance inst)
    {
        inst.IsInstalled = VersionCatalog.IsInstalled(inst.VersionId);
        inst.PropertyChanged += OnInstanceChanged;
        Instances.Add(inst);
    }

    private void OnInstanceChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(Instance.VersionId) or nameof(Instance.LoaderType) or nameof(Instance.LoaderVersion))
        {
            OnPropertyChanged(nameof(ModTargetText));
            RefreshModCompat();
        }

        if (_loading || sender is not Instance inst) return;

        switch (e.PropertyName)
        {
            case nameof(Instance.IsInstalled):
            case nameof(Instance.InstalledText):
            case nameof(Instance.JavaSourceLabel):
            case nameof(Instance.RamGb):
            case nameof(Instance.IsCustomJava):
            case nameof(Instance.HasLoader):
            case nameof(Instance.LoaderLabel):
            case nameof(Instance.LoaderBadge):
                return; // thuộc tính phụ, không cần lưu
            case nameof(Instance.VersionId):
                inst.IsInstalled = VersionCatalog.IsInstalled(inst.VersionId);
                break;
        }

        _dirty.Add(inst);
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void FlushSave()
    {
        _saveTimer.Stop();
        foreach (var i in _dirty.ToList())
        {
            try { InstanceStore.Save(i); } catch { /* bỏ qua lỗi ghi */ }
        }
        _dirty.Clear();
    }

    [RelayCommand]
    private void NewInstance()
    {
        var latest = _catalog.All.FirstOrDefault(v => v.Type == "release")?.Id ?? "1.21.4";
        var inst = new Instance
        {
            Name = $"Instance {Instances.Count + 1}",
            VersionId = latest,
            RamMb = _config.DefaultRamMb
        };
        InstanceStore.Save(inst);
        AddInstance(inst);
        SelectedInstance = inst;
    }

    [RelayCommand]
    private void DuplicateInstance()
    {
        if (SelectedInstance == null) return;
        FlushSave();
        try
        {
            var copy = InstanceStore.Duplicate(SelectedInstance);
            AddInstance(copy);
            SelectedInstance = copy;
        }
        catch (Exception ex)
        {
            StatusText = "Không nhân bản được: " + ex.Message;
        }
    }

    [RelayCommand]
    private void DeleteInstance()
    {
        var inst = SelectedInstance;
        if (inst == null) return;
        if (IsGameRunning)
        {
            StatusText = "Hãy tắt game trước khi xóa instance.";
            return;
        }

        var ask = MessageBox.Show(
            $"Xóa instance \"{inst.Name}\" cùng toàn bộ dữ liệu (world, mod, cài đặt) của nó?\nKhông thể khôi phục.",
            "Xác nhận xóa", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (ask != MessageBoxResult.Yes) return;

        try
        {
            _dirty.Remove(inst);
            InstanceStore.Delete(inst);
        }
        catch (Exception ex)
        {
            StatusText = "Không xóa được: " + ex.Message;
            return;
        }

        inst.PropertyChanged -= OnInstanceChanged;
        Instances.Remove(inst);
        SelectedInstance = Instances.FirstOrDefault();
    }

    [RelayCommand]
    private void OpenInstanceFolder()
    {
        if (SelectedInstance == null) return;
        Directory.CreateDirectory(SelectedInstance.GameDir);
        OpenFolder(SelectedInstance.GameDir);
    }

    [RelayCommand]
    private void BrowseJava()
    {
        if (SelectedInstance == null) return;
        var dlg = new OpenFileDialog
        {
            Title = "Chọn java.exe",
            Filter = "Java (java.exe;javaw.exe)|java.exe;javaw.exe|Tất cả file|*.*"
        };
        if (dlg.ShowDialog() == true) SelectedInstance.JavaPath = dlg.FileName;
    }

    [RelayCommand] private void OpenDataFolder() => OpenFolder(AppPaths.Root);

    [RelayCommand]
    private void OpenJavaFolder()
    {
        Directory.CreateDirectory(AppPaths.Java);
        OpenFolder(AppPaths.Java);
    }

    private static void OpenFolder(string path)
    {
        try { Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true }); }
        catch { /* bỏ qua */ }
    }

    // ---------- Chạy game ----------

    private bool CanPlay() => !IsBusy && !IsGameRunning && !IsAuthBusy && SelectedInstance != null;

    [RelayCommand(CanExecute = nameof(CanPlay))]
    private async Task PlayAsync()
    {
        var inst = SelectedInstance;
        if (inst == null) return;

        var name = PlayerName.Trim();
        if (CurrentAuth == AuthMode.Offline && !Regex.IsMatch(name, "^[A-Za-z0-9_]{3,16}$"))
        {
            StatusText = "Tên người chơi phải dài 3–16 ký tự: chữ, số hoặc dấu gạch dưới.";
            return;
        }

        FlushSave();
        IsBusy = true;
        Progress = 0;
        _cts = new CancellationTokenSource();
        try
        {
            var (session, extraJvm) = await ResolveSessionAsync(name, _cts.Token);
            AppendLog($"=== Chạy {inst.Name} ({inst.VersionId}) với tên {session.Username} [{AuthLabel}] ===");
            var proc = await _runner.LaunchAsync(inst, session, extraJvm, _config, Report, _cts.Token);
            _game = proc;

            inst.LastPlayed = DateTime.Now;
            inst.IsInstalled = true;
            _dirty.Add(inst);
            FlushSave();

            IsGameRunning = true;
            Progress = 100;
            StatusText = "Game đang chạy...";
            GameStateChanged?.Invoke(true);

            proc.Exited += (_, _) => Ui(() => OnGameExited(proc));
            if (proc.HasExited) OnGameExited(proc);
        }
        catch (OperationCanceledException)
        {
            StatusText = "Đã hủy.";
        }
        catch (Exception ex)
        {
            AppendLog("LỖI: " + ex);
            StatusText = "Lỗi: " + ex.Message + " (xem tab Nhật ký)";
        }
        finally
        {
            IsBusy = false;
            _cts?.Dispose();
            _cts = null;
        }
    }

    private void OnGameExited(Process p)
    {
        if (!ReferenceEquals(_game, p)) return;
        var code = -1;
        try { code = p.ExitCode; } catch { }

        _game = null;
        IsGameRunning = false;
        StatusText = code == 0 ? "Game đã thoát." : $"Game thoát với mã {code}. Xem tab Nhật ký.";
        AppendLog($"=== Game đã thoát (mã {code}) ===");
        GameStateChanged?.Invoke(false);
    }

    private bool CanStop() => IsBusy || IsGameRunning;

    [RelayCommand(CanExecute = nameof(CanStop))]
    private void Stop()
    {
        if (_game != null && !_game.HasExited)
        {
            try { _game.Kill(true); } catch { }
        }
        else
        {
            _cts?.Cancel();
        }
    }

    // ---------- Mod loader ----------

    /// <summary>Đưa ô chọn loader về đúng loader đang gắn với instance (hoặc "Không") rồi tải danh sách bản.</summary>
    private void SyncLoaderSelection(Instance? inst)
    {
        var label = inst != null && inst.HasLoader ? LoaderInstaller.Label(inst.LoaderType) : LoaderTypeLabels[0];
        _syncingLoader = true;
        try { LoaderTypeLabel = Array.IndexOf(LoaderTypeLabels, label) >= 0 ? label : LoaderTypeLabels[0]; }
        finally { _syncingLoader = false; }
        _ = RefreshLoaderVersionsAsync();
    }

    /// <summary>Tải danh sách bản loader cho phiên bản Minecraft của instance đang chọn. Kết quả cũ bị bỏ nếu đã có lần gọi mới hơn.</summary>
    private async Task RefreshLoaderVersionsAsync()
    {
        var stamp = ++_loaderListStamp;
        var inst = SelectedInstance;
        var type = CurrentLoaderType;

        LoaderVersionList.Clear();
        LoaderVersionChoice = null;
        if (inst == null || type.Length == 0)
        {
            LoaderListStatus = "";
            return;
        }

        var mc = inst.VersionId;
        var name = LoaderInstaller.Label(type);
        // Nếu instance đang dùng đúng loader này thì chọn sẵn bản đã cài
        var want = inst.LoaderType == type && inst.LoaderVersion.Length > 0 ? inst.LoaderVersion : null;

        LoaderVersionList.Add(LatestLoaderLabel);
        LoaderVersionChoice = LatestLoaderLabel;
        LoaderListStatus = $"Đang tải danh sách {name} cho Minecraft {mc}...";

        try
        {
            var list = await new LoaderInstaller(AppendLog).ListVersionsAsync(type, mc, CancellationToken.None);
            if (stamp != _loaderListStamp) return;

            foreach (var v in list) LoaderVersionList.Add(v);
            LoaderVersionChoice = want != null && list.Contains(want) ? want : LatestLoaderLabel;
            LoaderListStatus = list.Count == 0
                ? $"{name} chưa hỗ trợ Minecraft {mc}. Hãy thử phiên bản Minecraft khác."
                : $"Có {list.Count} bản {name} cho Minecraft {mc}.";
        }
        catch (Exception ex)
        {
            if (stamp != _loaderListStamp) return;
            AppendLog($"Không tải được danh sách {name}: {ex.Message}");
            LoaderListStatus = "Không tải được danh sách (kiểm tra mạng). Bạn vẫn có thể chọn \"Mới nhất\".";
        }
    }

    private bool CanInstallLoader() => !IsBusy && !IsGameRunning && SelectedInstance != null && CurrentLoaderType.Length > 0;

    /// <summary>Tải và cài mod loader đã chọn cho instance đang chọn (Forge/NeoForge sẽ tự tải Java nếu thiếu).</summary>
    [RelayCommand(CanExecute = nameof(CanInstallLoader))]
    private async Task InstallLoaderAsync()
    {
        var inst = SelectedInstance;
        if (inst == null) return;

        var type = CurrentLoaderType;
        var name = LoaderInstaller.Label(type);
        var mc = inst.VersionId;
        var wanted = string.IsNullOrEmpty(LoaderVersionChoice) || LoaderVersionChoice == LatestLoaderLabel
            ? "" : LoaderVersionChoice;

        IsBusy = true;
        Progress = 0;
        var cts = new CancellationTokenSource();
        _cts = cts;
        try
        {
            AppendLog($"=== Cài {name} {(wanted.Length > 0 ? wanted : "(mới nhất)")} cho Minecraft {mc} ===");
            var launchId = await new LoaderInstaller(AppendLog).InstallAsync(
                type, mc, wanted, c => ResolveInstallerJavaAsync(mc, c), Report, cts.Token);

            var version = wanted.Length > 0 ? wanted : LoaderInstaller.VersionFromId(type, launchId, mc);
            inst.LoaderType = type;
            inst.LoaderVersion = version;
            inst.LaunchId = launchId;
            FlushSave();

            Progress = 100;
            StatusText = $"Đã cài {name} {version} cho \"{inst.Name}\". Bấm CHƠI để vào game.";
            AppendLog($"=== Xong: {name} {version} (id {launchId}) ===");
        }
        catch (OperationCanceledException)
        {
            StatusText = "Đã hủy cài mod loader.";
        }
        catch (Exception ex)
        {
            AppendLog("LỖI cài mod loader: " + ex);
            StatusText = "Lỗi cài mod loader: " + ex.Message + " (xem tab Nhật ký)";
        }
        finally
        {
            IsBusy = false;
            _cts = null;
            cts.Dispose();
        }
    }

    /// <summary>Bỏ liên kết loader khỏi instance (file loader trong thư mục dùng chung và mod trong instance được giữ lại).</summary>
    [RelayCommand]
    private void RemoveLoader()
    {
        var inst = SelectedInstance;
        if (inst == null || !inst.HasLoader || IsBusy || IsGameRunning) return;

        AppendLog($"Gỡ mod loader {inst.LoaderLabel} khỏi \"{inst.Name}\" (file loader và thư mục mods được giữ nguyên).");
        inst.LoaderType = "";
        inst.LoaderVersion = "";
        inst.LaunchId = "";
        FlushSave();
        StatusText = "Đã gỡ mod loader. Instance sẽ chạy Minecraft thuần.";
        SyncLoaderSelection(inst);
    }

    // ---------- Tải về: Mods / Resource Pack / Modpack (Modrinth, CurseForge) ----------

    public string[] ModSources { get; } = { "Modrinth", "CurseForge" };
    public ObservableCollection<ModHit> ModResults { get; } = new();

    [ObservableProperty] private string modSource = "Modrinth";
    [ObservableProperty] private string modQuery = "";
    [ObservableProperty] private string modStatus = "";

    // Loại đang xem: ba nút chọn Mods / Resource Pack / Modpack
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCatMods))]
    [NotifyPropertyChangedFor(nameof(IsCatResourcePack))]
    [NotifyPropertyChangedFor(nameof(IsCatModpack))]
    [NotifyPropertyChangedFor(nameof(NeedsInstance))]
    [NotifyPropertyChangedFor(nameof(ModTargetText))]
    private ModKind modCategory = ModKind.Mod;

    public bool IsCatMods { get => ModCategory == ModKind.Mod; set { if (value) ModCategory = ModKind.Mod; } }
    public bool IsCatResourcePack { get => ModCategory == ModKind.ResourcePack; set { if (value) ModCategory = ModKind.ResourcePack; } }
    public bool IsCatModpack { get => ModCategory == ModKind.Modpack; set { if (value) ModCategory = ModKind.Modpack; } }

    /// <summary>Modpack tạo instance mới nên không cần chọn instance.</summary>
    public bool NeedsInstance => ModCategory != ModKind.Modpack;

    // Phân trang
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ModPageText))]
    [NotifyPropertyChangedFor(nameof(HasPrevModPage))]
    [NotifyPropertyChangedFor(nameof(HasNextModPage))]
    private int modPage = 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ModPageText))]
    [NotifyPropertyChangedFor(nameof(HasNextModPage))]
    private int modTotalPages = 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPrevModPage))]
    [NotifyPropertyChangedFor(nameof(HasNextModPage))]
    private bool isModSearching;

    public string ModPageText => $"Trang {ModPage} / {ModTotalPages}";
    public bool HasPrevModPage => ModPage > 1 && !IsModSearching;
    public bool HasNextModPage => ModPage < ModTotalPages && !IsModSearching;

    // Cửa sổ chi tiết
    [ObservableProperty] private bool isModDetailOpen;
    [ObservableProperty] private bool isModDetailLoading;
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(InstallSelectedModVersionCommand))]
    private ModHit? modDetailHit;
    [ObservableProperty] private ModDetail? modDetail;

    private int _modStamp;

    // Lọc theo phiên bản Minecraft (vd 1.12.2); bỏ trống = theo instance đang chọn
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ModTargetText))]
    private string modMcVersion = "";

    // Danh sách phiên bản của mục đang xem chi tiết (để chọn đúng bản muốn cài)
    public ObservableCollection<ModVersionInfo> ModVersions { get; } = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(InstallSelectedModVersionCommand))]
    private ModVersionInfo? selectedModVersion;

    [ObservableProperty] private bool isModVersionsLoading;
    public bool HasModVersions => ModVersions.Count > 0;

    /// <summary>Phiên bản Minecraft người dùng nhập để lọc; null nếu bỏ trống hoặc sai định dạng.</summary>
    private string? ValidModMc()
    {
        var t = (ModMcVersion ?? "").Trim();
        return Regex.IsMatch(t, @"^\d+\.\d+(\.\d+)?$") ? t : null;
    }

    private static string KindName(ModKind k) => k switch
    {
        ModKind.ResourcePack => "resource pack", ModKind.Modpack => "modpack", _ => "mod"
    };

    /// <summary>Dòng mô tả nơi mục tải về sẽ được cài.</summary>
    public string ModTargetText
    {
        get
        {
            var filter = ValidModMc();
            var i = SelectedInstance;

            if (ModCategory == ModKind.Modpack)
                return "Modpack được tải về rồi nhập thành một instance mới, cài sẵn đúng phiên bản Minecraft và mod loader của gói." +
                       (filter != null ? $" Đang lọc theo Minecraft {filter}." : "");

            if (filter != null)
            {
                var match = Instances.FirstOrDefault(x => x.VersionId == filter);
                return match != null
                    ? $"Đang xem bản cho Minecraft {filter} · sẽ cài vào \"{match.Name}\"."
                    : $"Đang xem bản cho Minecraft {filter} · chưa có instance này, khi tải launcher sẽ hỏi tạo instance mới.";
            }

            if (i == null) return "Chưa có instance nào.";
            if (ModCategory == ModKind.ResourcePack)
                return $"Cài vào thư mục resourcepacks của \"{i.Name}\" · Minecraft {i.VersionId}";
            return i.HasLoader
                ? $"Minecraft {i.VersionId} · {LoaderInstaller.Label(i.LoaderType)} {i.LoaderVersion}".Trim()
                : $"Minecraft {i.VersionId} · chưa có mod loader. Hãy cài loader ở tab Instance trước.";
        }
    }

    partial void OnModMcVersionChanged(string value)
    {
        if (_loading) return;
        var mc = ValidModMc();
        if (mc != null && ModCategory != ModKind.Modpack)
        {
            // Đã có instance đúng phiên bản thì tự chọn (ưu tiên instance có loader)
            var match = Instances.Where(x => x.VersionId == mc).OrderByDescending(x => x.HasLoader).FirstOrDefault();
            if (match != null && SelectedInstance != match) SelectedInstance = match;
        }
        RefreshModCompat();
    }

    partial void OnModSourceChanged(string value)
    {
        if (_loading) return;
        _ = SearchModsAsync(); // đổi nguồn thì tải lại từ trang 1
    }

    partial void OnModCategoryChanged(ModKind value)
    {
        if (_loading) return;
        ModResults.Clear();
        RefreshModCompat();
        _ = SearchModsAsync(); // đổi loại thì tải lại từ trang 1
    }

    /// <summary>Tìm từ trang 1 (đổi từ khóa, nguồn hoặc loại). Danh sách luôn hiện, không cần chọn instance trước.</summary>
    [RelayCommand]
    private async Task SearchModsAsync()
    {
        ModPage = 1;
        await LoadModPageAsync();
    }

    [RelayCommand]
    private async Task PrevModPageAsync()
    {
        if (!HasPrevModPage) return;
        ModPage--;
        await LoadModPageAsync();
    }

    [RelayCommand]
    private async Task NextModPageAsync()
    {
        if (!HasNextModPage) return;
        ModPage++;
        await LoadModPageAsync();
    }

    /// <summary>Tải trang ModPage của loại và nguồn đang chọn. Kết quả của lần gọi cũ bị bỏ nếu đã có lần gọi mới hơn.</summary>
    private async Task LoadModPageAsync()
    {
        var stamp = ++_modStamp;
        var source = ModSource;
        var kind = ModCategory;
        var page = ModPage;
        var query = ModQuery.Trim();

        // Lọc theo phiên bản Minecraft người dùng nhập; loader chỉ lọc khi instance đúng phiên bản đó và có loader
        var mcFilter = ValidModMc();
        var sel = SelectedInstance;
        string? loaderFilter = kind == ModKind.Mod && mcFilter != null && sel is { HasLoader: true } && sel.VersionId == mcFilter
            ? sel.LoaderType : null;

        IsModSearching = true;
        ModStatus = $"Đang tải {KindName(kind)} từ {source}...";
        ModResults.Clear();
        try
        {
            var res = await new ModBrowser(AppendLog, _config.CurseForgeApiKey)
                .SearchAsync(source, kind, query, page, mcFilter, loaderFilter, CancellationToken.None);
            if (stamp != _modStamp) return;

            ModTotalPages = Math.Clamp((res.Total + ModBrowser.PageSize - 1) / ModBrowser.PageSize, 1, 500);
            foreach (var h in res.Hits) ModResults.Add(h);
            RefreshModCompat();
            foreach (var h in res.Hits) _ = h.LoadIconAsync(); // ảnh tải nền, mục nào lỗi sẽ giữ ô chữ cái màu

            ModStatus = res.Hits.Count == 0
                ? $"Không tìm thấy {KindName(kind)} nào trên {source}."
                : $"{res.Total:N0} {KindName(kind)} trên {source}." +
                  (kind == ModKind.Modpack ? "" : " Mục không hợp với instance đang chọn sẽ có cảnh báo.");
        }
        catch (ModException ex)
        {
            if (stamp == _modStamp) ModStatus = ex.Message;
        }
        catch (Exception ex)
        {
            if (stamp != _modStamp) return;
            AppendLog("LỖI tải danh sách: " + ex);
            ModStatus = "Không tải được danh sách: " + ex.Message;
        }
        finally
        {
            if (stamp == _modStamp) IsModSearching = false;
        }
    }

    /// <summary>Kiểm tra lại từng mục với instance đang chọn (gọi khi đổi instance, phiên bản, loader hoặc loại).</summary>
    private void RefreshModCompat()
    {
        var inst = SelectedInstance;
        var filter = ValidModMc();
        foreach (var h in ModResults)
        {
            if (h.Kind == ModKind.Modpack)
            {
                h.Warning = "";
            }
            else if (filter != null)
            {
                // Đang xem bản cho phiên bản Minecraft khác: kiểm tra theo phiên bản đó; loader chỉ biết khi instance cùng phiên bản
                var ld = inst is { HasLoader: true } && inst.VersionId == filter ? inst.LoaderType : "";
                h.Warning = h.CheckCompat(filter, ld);
            }
            else if (inst == null)
            {
                h.Warning = "Chưa chọn instance để cài.";
            }
            else if (h.Kind == ModKind.Mod && !inst.HasLoader)
            {
                h.Warning = $"Instance \"{inst.Name}\" chưa có mod loader. Cài loader ở tab Instance trước.";
            }
            else
            {
                h.Warning = h.CheckCompat(inst.VersionId, inst.LoaderType);
            }
        }
    }

    // ----- Chi tiết -----

    [RelayCommand]
    private async Task ShowModDetailAsync(ModHit? hit)
    {
        if (hit == null) return;

        ModDetailHit = hit;
        ModDetail = null;
        IsModDetailOpen = true;
        IsModDetailLoading = true;
        try
        {
            var inst = SelectedInstance;
            var detail = await new ModBrowser(AppendLog, _config.CurseForgeApiKey).GetDetailAsync(
                hit, inst?.VersionId, inst is { HasLoader: true } ? inst.LoaderType : null, CancellationToken.None);
            if (ModDetailHit == hit) ModDetail = detail;
        }
        catch (Exception ex)
        {
            if (ex is not ModException) AppendLog("LỖI tải chi tiết: " + ex);
            if (ModDetailHit == hit)
                ModDetail = new ModDetail
                {
                    Title = hit.Title, Description = hit.Description, PageUrl = hit.PageUrl,
                    Error = ex is ModException ? ex.Message : "Không tải được chi tiết: " + ex.Message
                };
        }
        finally
        {
            if (ModDetailHit == hit) IsModDetailLoading = false;
        }

        await LoadModVersionsAsync(hit);
    }

    /// <summary>Nạp danh sách phiên bản của mục đang xem và chọn sẵn bản hợp với phiên bản Minecraft đang nhắm tới.</summary>
    private async Task LoadModVersionsAsync(ModHit hit)
    {
        ModVersions.Clear();
        SelectedModVersion = null;
        OnPropertyChanged(nameof(HasModVersions));
        IsModVersionsLoading = true;
        try
        {
            var list = await new ModBrowser(AppendLog, _config.CurseForgeApiKey).GetVersionsAsync(hit, CancellationToken.None);
            if (ModDetailHit != hit) return;

            foreach (var v in list) ModVersions.Add(v);
            OnPropertyChanged(nameof(HasModVersions));

            var inst = SelectedInstance;
            var target = ValidModMc() ?? inst?.VersionId;
            var loader = hit.Kind == ModKind.Mod && inst is { HasLoader: true } && (ValidModMc() == null || inst.VersionId == ValidModMc())
                ? inst.LoaderType : null;
            var accepted = loader == "quilt" ? new[] { "quilt", "fabric" } : loader != null ? new[] { loader } : Array.Empty<string>();

            SelectedModVersion =
                list.FirstOrDefault(v => target != null && v.McVersions.Contains(target) &&
                                         (accepted.Length == 0 || v.Loaders.Count == 0 || v.Loaders.Any(accepted.Contains)))
                ?? list.FirstOrDefault(v => target != null && v.McVersions.Contains(target))
                ?? list.FirstOrDefault();
        }
        catch (Exception ex)
        {
            AppendLog("Không tải được danh sách phiên bản: " + ex.Message);
        }
        finally
        {
            if (ModDetailHit == hit) IsModVersionsLoading = false;
        }
    }

    [RelayCommand]
    private void CloseModDetail() => IsModDetailOpen = false;

    /// <summary>Mở trang của mục trên trình duyệt (chỉ cho phép liên kết https).</summary>
    [RelayCommand]
    private void OpenModPage(string? url)
    {
        if (string.IsNullOrEmpty(url) || !Uri.TryCreate(url, UriKind.Absolute, out var u) || u.Scheme != Uri.UriSchemeHttps) return;
        try { Process.Start(new ProcessStartInfo(u.AbsoluteUri) { UseShellExecute = true }); }
        catch { StatusText = "Không mở được trình duyệt."; }
    }

    // ----- Tải về -----

    private bool CanInstallMod(ModHit? hit) => !IsBusy && !IsGameRunning;

    /// <summary>Tải về theo cách tự động: bản hợp với phiên bản Minecraft đang nhắm tới (ô lọc, hoặc instance đang chọn).</summary>
    [RelayCommand(CanExecute = nameof(CanInstallMod))]
    private async Task InstallModAsync(ModHit? hit) => await InstallModCoreAsync(hit, null);

    private bool CanInstallSelectedVersion() => !IsBusy && !IsGameRunning && ModDetailHit != null && SelectedModVersion != null;

    /// <summary>Cài đúng phiên bản người dùng đã chọn trong cửa sổ chi tiết (ví dụ bản cho Minecraft 1.12.2).</summary>
    [RelayCommand(CanExecute = nameof(CanInstallSelectedVersion))]
    private async Task InstallSelectedModVersionAsync() => await InstallModCoreAsync(ModDetailHit, SelectedModVersion);

    /// <summary>
    /// Tải về một mục. Mod và resource pack cài vào instance đúng phiên bản Minecraft: dùng instance đang chọn nếu đúng,
    /// không thì chuyển sang instance có sẵn đúng phiên bản, không có thì hỏi để tạo instance mới (kèm mod loader).
    /// Modpack được tải rồi nhập thành instance mới.
    /// </summary>
    private async Task InstallModCoreAsync(ModHit? hit, ModVersionInfo? ver)
    {
        if (hit == null) return;
        IsModDetailOpen = false;

        if (hit.Kind == ModKind.Modpack)
        {
            await InstallModpackAsync(hit, ver);
            return;
        }

        // Phiên bản Minecraft cần cài cho: theo bản đã chọn, hoặc ô lọc, hoặc instance đang chọn
        var filter = ValidModMc();
        var inst = SelectedInstance;
        string? mc = ver != null
            ? (filter != null && ver.McVersions.Contains(filter) ? filter
               : inst != null && ver.McVersions.Contains(inst.VersionId) ? inst.VersionId
               : ver.McVersions.FirstOrDefault())
            : filter ?? inst?.VersionId;
        if (mc == null)
        {
            ModStatus = "Hãy chọn một instance hoặc nhập phiên bản Minecraft.";
            return;
        }

        // Instance đích phải đúng phiên bản Minecraft
        if (inst == null || inst.VersionId != mc)
        {
            var existing = Instances.Where(i => i.VersionId == mc).OrderByDescending(i => i.HasLoader).FirstOrDefault();
            if (existing != null)
            {
                inst = existing;
                SelectedInstance = existing;
            }
            else
            {
                inst = await CreateInstanceForModAsync(hit, ver, mc);
                if (inst == null) return; // người dùng hủy hoặc tạo lỗi
            }
        }

        if (hit.Kind == ModKind.Mod && !inst.HasLoader)
        {
            ModStatus = "Instance này chưa có mod loader. Hãy cài loader ở tab Instance trước.";
            MessageBox.Show($"Instance \"{inst.Name}\" chưa có mod loader nên chưa cài mod được.\n\n" +
                            "Vào tab Instance, mục Mod loader để cài Fabric/Quilt/Forge/NeoForge trước.",
                "Chưa có mod loader", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Bản đã chọn dành cho loader khác với loader của instance
        if (ver != null && hit.Kind == ModKind.Mod && ver.Loaders.Count > 0)
        {
            var accepted = inst.LoaderType == "quilt" ? new[] { "quilt", "fabric" } : new[] { inst.LoaderType };
            if (!ver.Loaders.Any(accepted.Contains))
            {
                var ask = MessageBox.Show(
                    $"Phiên bản này dành cho {string.Join(", ", ver.Loaders.Select(LoaderInstaller.Label))} " +
                    $"nhưng instance \"{inst.Name}\" dùng {LoaderInstaller.Label(inst.LoaderType)}.\n\nVẫn cài?",
                    "Cảnh báo mod loader", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
                if (ask != MessageBoxResult.Yes) return;
            }
        }
        // Cài tự động mà mục không hợp phiên bản/loader thì cảnh báo, mặc định là không cài
        else if (ver == null && hit.HasWarning)
        {
            var ask = MessageBox.Show(
                $"{hit.Title}\n\n⚠ {hit.Warning}\n\n" +
                $"Instance \"{inst.Name}\" dùng Minecraft {inst.VersionId}" +
                (hit.Kind == ModKind.Mod ? $" và {inst.LoaderLabel}" : "") + ".\n" +
                "Vẫn thử cài? Launcher chỉ cài nếu tìm được file đúng phiên bản.",
                "Cảnh báo phiên bản", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
            if (ask != MessageBoxResult.Yes) return;
        }

        IsBusy = true;
        Progress = 0;
        var cts = new CancellationTokenSource();
        _cts = cts;
        try
        {
            var what = KindName(hit.Kind);
            AppendLog($"=== Cài {what} {hit.Title} ({hit.Source}) vào \"{inst.Name}\": Minecraft {mc}" +
                      (hit.Kind == ModKind.Mod ? $", {inst.LoaderLabel}" : "") +
                      (ver != null ? $", bản đã chọn: {ver.Name}" : "") + " ===");
            var res = await new ModBrowser(AppendLog, _config.CurseForgeApiKey).InstallAsync(
                hit, mc, inst.LoaderType, inst.GameDir, Report, cts.Token, ver?.Id);

            Progress = 100;
            foreach (var w in res.Warnings) AppendLog("CẢNH BÁO: " + w);
            var deps = res.Installed.Count - 1;
            ModStatus = $"Đã cài {hit.Title}" + (deps > 0 ? $" cùng {deps} mod phụ thuộc" : "") +
                        $" vào \"{inst.Name}\" (Minecraft {mc})." + (res.Warnings.Count > 0 ? " Có cảnh báo, xem tab Nhật ký." : "");
            StatusText = ModStatus;
        }
        catch (OperationCanceledException)
        {
            ModStatus = "Đã hủy tải.";
            StatusText = ModStatus;
        }
        catch (ModException ex)
        {
            AppendLog("LỖI tải về: " + ex.Message);
            ModStatus = ex.Message;
            StatusText = ex.Message;
        }
        catch (Exception ex)
        {
            AppendLog("LỖI tải về: " + ex);
            ModStatus = "Lỗi tải về: " + ex.Message + " (xem tab Nhật ký)";
            StatusText = ModStatus;
        }
        finally
        {
            IsBusy = false;
            _cts = null;
            cts.Dispose();
        }
    }

    /// <summary>Chọn mod loader phù hợp cho một instance mới: theo loader mà bản/mục hỗ trợ; Minecraft cũ (dưới 1.14) chỉ có Forge.</summary>
    private static string? PickLoaderFor(ModHit hit, ModVersionInfo? ver, string mc)
    {
        if (hit.Kind != ModKind.Mod) return null;

        var found = new List<string>();
        if (ver != null) found.AddRange(ver.Loaders);
        found.AddRange(hit.Loaders);
        if (hit.Builds != null) found.AddRange(hit.Builds.Where(b => b.Mc == mc && b.Loader.Length > 0).Select(b => b.Loader));
        var available = found.Select(l => l.ToLowerInvariant()).Distinct().ToList();

        var old = Version.TryParse(mc, out var v) && v < new Version(1, 14);
        var order = old ? new[] { "forge" } : new[] { "fabric", "forge", "neoforge", "quilt" };
        foreach (var l in order)
            if (available.Contains(l)) return l;
        return old ? "forge" : available.FirstOrDefault() ?? "fabric";
    }

    /// <summary>Hỏi rồi tạo instance mới đúng phiên bản Minecraft (kèm mod loader nếu cần). Trả về null nếu hủy hoặc lỗi.</summary>
    private async Task<Instance?> CreateInstanceForModAsync(ModHit hit, ModVersionInfo? ver, string mc)
    {
        var cur = SelectedInstance;
        var loader = PickLoaderFor(hit, ver, mc);
        var what = loader != null ? $"Minecraft {mc} + {LoaderInstaller.Label(loader)}" : $"Minecraft {mc}";

        var ask = MessageBox.Show(
            (cur != null ? $"Instance đang chọn (\"{cur.Name}\") dùng Minecraft {cur.VersionId}, còn bạn muốn cài cho Minecraft {mc}.\n\n" : "") +
            $"Tạo instance mới \"{what}\" rồi cài \"{hit.Title}\" vào đó?\n\n" +
            $"Launcher sẽ tự tải Minecraft {mc}" + (loader != null ? " và mod loader" : "") + ", có thể mất vài phút.",
            "Chưa có instance đúng phiên bản", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.Yes);
        if (ask != MessageBoxResult.Yes) return null;

        var ni = new Instance
        {
            Name = loader != null ? $"Minecraft {mc} {LoaderInstaller.Label(loader)}" : $"Minecraft {mc}",
            VersionId = mc,
            RamMb = _config.DefaultRamMb
        };

        IsBusy = true;
        Progress = 0;
        var cts = new CancellationTokenSource();
        _cts = cts;
        try
        {
            AppendLog($"=== Tạo instance \"{ni.Name}\" cho {what} ===");
            if (loader != null)
            {
                var launchId = await new LoaderInstaller(AppendLog).InstallAsync(
                    loader, mc, "", c => ResolveInstallerJavaAsync(mc, c), Report, cts.Token);
                ni.LoaderType = loader;
                ni.LoaderVersion = LoaderInstaller.VersionFromId(loader, launchId, mc);
                ni.LaunchId = launchId;
            }
            InstanceStore.Save(ni);
            AddInstance(ni);
            SelectedInstance = ni;
            return ni;
        }
        catch (OperationCanceledException)
        {
            ModStatus = "Đã hủy tạo instance.";
            try { InstanceStore.Delete(ni); } catch { }
            return null;
        }
        catch (Exception ex)
        {
            AppendLog("LỖI tạo instance: " + ex);
            ModStatus = "Không tạo được instance: " + ex.Message + " (xem tab Nhật ký)";
            StatusText = ModStatus;
            try { InstanceStore.Delete(ni); } catch { }
            return null;
        }
        finally
        {
            IsBusy = false;
            _cts = null;
            cts.Dispose();
        }
    }

    /// <summary>Tải file modpack (đúng phiên bản đã chọn, hoặc bản mới nhất hợp với ô lọc Minecraft) rồi nhập thành instance mới.</summary>
    private async Task InstallModpackAsync(ModHit hit, ModVersionInfo? ver)
    {
        string? path = null;
        IsBusy = true;
        Progress = 0;
        var cts = new CancellationTokenSource();
        _cts = cts;
        try
        {
            var browser = new ModBrowser(AppendLog, _config.CurseForgeApiKey);

            // Không chọn bản cụ thể mà có lọc Minecraft: lấy bản mới nhất hợp với phiên bản đó
            var filter = ValidModMc();
            if (ver == null && filter != null)
            {
                Report(-1, $"Đang tìm bản của {hit.Title} cho Minecraft {filter}...");
                var all = await browser.GetVersionsAsync(hit, cts.Token);
                ver = all.FirstOrDefault(v => v.McVersions.Contains(filter) && v.Type == "release")
                      ?? all.FirstOrDefault(v => v.McVersions.Contains(filter))
                      ?? throw new ModException($"{hit.Title}: không có bản cho Minecraft {filter}. Hãy mở Chi tiết để chọn phiên bản khác.");
            }

            AppendLog($"=== Tải modpack {hit.Title} ({hit.Source})" + (ver != null ? $", bản {ver.Name}" : "") + " ===");
            path = await browser.DownloadModpackAsync(hit, Path.Combine(AppPaths.Cache, "modpacks"), Report, cts.Token, ver?.Id);
            Progress = 100;
            ModStatus = $"Đã tải {Path.GetFileName(path)}. Đang nhập modpack...";
        }
        catch (OperationCanceledException)
        {
            ModStatus = "Đã hủy tải.";
            StatusText = ModStatus;
        }
        catch (ModException ex)
        {
            AppendLog("LỖI tải modpack: " + ex.Message);
            ModStatus = ex.Message;
            StatusText = ex.Message;
        }
        catch (Exception ex)
        {
            AppendLog("LỖI tải modpack: " + ex);
            ModStatus = "Lỗi tải modpack: " + ex.Message + " (xem tab Nhật ký)";
            StatusText = ModStatus;
        }
        finally
        {
            IsBusy = false;
            _cts = null;
            cts.Dispose();
        }

        // Nhập sau khi đã nhả trạng thái bận (luồng nhập tự đặt trạng thái bận của riêng nó)
        if (path != null)
        {
            await ImportModpackFileAsync(path, confirm: false);
            ModStatus = StatusText;
        }
    }

    // ---------- Tài khoản ----------

    private bool CanAuth() => !IsAuthBusy;

    /// <summary>Lấy phiên đăng nhập theo chế độ đang chọn (làm mới token nếu cần).</summary>
    private async Task<(MSession Session, string? ExtraJvm)> ResolveSessionAsync(string offlineName, CancellationToken ct)
    {
        switch (CurrentAuth)
        {
            case AuthMode.Microsoft:
            {
                if (_ms == null) throw new AuthException("Đăng nhập Microsoft không khả dụng. Xem tab Nhật ký.");
                Report(-1, "Đang làm mới phiên đăng nhập Microsoft...");
                var s = await _ms.TrySilentAsync();
                if (s == null || string.IsNullOrEmpty(s.AccessToken))
                    throw new AuthException("Chưa đăng nhập Microsoft hoặc phiên đã hết hạn. Hãy đăng nhập ở tab Tài khoản.");
                MsAccountName = s.Username ?? "";
                return (s, null);
            }

            case AuthMode.ElyBy:
            {
                Report(-1, "Đang kiểm tra phiên đăng nhập Ely.by...");
                var acc = await _ely.EnsureValidAsync(ct);
                ElyAccountName = acc.Username;
                var jar = await AuthlibInjector.EnsureAsync(AppendLog, ct);
                var session = new MSession(acc.Username, acc.AccessToken, acc.Uuid);
                // Dấu ngoặc kép để đường dẫn có dấu cách vẫn đúng; "ely.by" là dạng rút gọn authlib-injector hiểu
                return (session, $"\"-javaagent:{jar}=ely.by\"");
            }

            default:
                return (MSession.CreateOfflineSession(offlineName), null);
        }
    }

    /// <summary>Khi mở launcher: thử làm mới phiên Microsoft ngầm để hiện đúng tên (lỗi thì giữ nguyên, không báo).</summary>
    private async Task RestoreMicrosoftAsync()
    {
        if (_ms == null || MsAccountName.Length == 0) return;
        try
        {
            var s = await _ms.TrySilentAsync();
            if (s != null && !string.IsNullOrEmpty(s.Username)) MsAccountName = s.Username;
        }
        catch { /* không có mạng thì thôi */ }
    }

    [RelayCommand(CanExecute = nameof(CanAuth))]
    private async Task MicrosoftSignInAsync()
    {
        if (_ms == null)
        {
            AuthMessage = "Đăng nhập Microsoft không khả dụng. Xem tab Nhật ký.";
            return;
        }

        IsAuthBusy = true;
        AuthMessage = "Đang mở cửa sổ đăng nhập Microsoft...";
        try
        {
            var s = await _ms.SignInAsync();
            MsAccountName = s.Username ?? "";
            _config.MicrosoftName = MsAccountName;
            _config.Save();
            AuthMessage = $"Đã đăng nhập Microsoft: {MsAccountName}";
        }
        catch (Exception ex)
        {
            AppendLog("LỖI đăng nhập Microsoft: " + ex);
            AuthMessage = FriendlyAuthError(ex);
        }
        finally
        {
            IsAuthBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanAuth))]
    private async Task MicrosoftSignOutAsync()
    {
        IsAuthBusy = true;
        try
        {
            if (_ms != null) await _ms.SignOutAsync();
        }
        catch (Exception ex)
        {
            AppendLog("LỖI đăng xuất Microsoft: " + ex.Message);
        }
        finally
        {
            MsAccountName = "";
            _config.MicrosoftName = "";
            _config.Save();
            AuthMessage = "Đã đăng xuất Microsoft.";
            IsAuthBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanAuth))]
    private async Task ElyLoginAsync()
    {
        var pass = ElyPassword;
        ElyPassword = ""; // không giữ mật khẩu lâu hơn cần thiết
        var user = ElyUser.Trim();
        var totp = ElyTotp.Trim();

        if (user.Length == 0 || pass.Length == 0)
        {
            AuthMessage = "Nhập tên đăng nhập (hoặc email) và mật khẩu Ely.by.";
            return;
        }

        IsAuthBusy = true;
        AuthMessage = "Đang đăng nhập Ely.by...";
        try
        {
            var acc = await _ely.LoginAsync(user, pass, totp, CancellationToken.None);
            ElyAccountName = acc.Username;
            ElyUser = "";
            ElyTotp = "";
            AuthMessage = $"Đã đăng nhập Ely.by: {acc.Username}";
        }
        catch (AuthException ex)
        {
            AuthMessage = ex.Message;
        }
        catch (Exception ex)
        {
            AppendLog("LỖI đăng nhập Ely.by: " + ex);
            AuthMessage = FriendlyAuthError(ex);
        }
        finally
        {
            IsAuthBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanAuth))]
    private async Task ElyLogoutAsync()
    {
        IsAuthBusy = true;
        try { await _ely.LogoutAsync(); }
        finally
        {
            ElyAccountName = "";
            AuthMessage = "Đã đăng xuất Ely.by.";
            IsAuthBusy = false;
        }
    }

    /// <summary>Đổi lỗi kỹ thuật thành câu dễ hiểu: mất mạng, không sở hữu game, đóng cửa sổ đăng nhập...</summary>
    private static string FriendlyAuthError(Exception ex)
    {
        for (var e = ex; e != null; e = e.InnerException)
        {
            if (e is AuthException) return e.Message;
        }

        var all = string.Join(" ", EnumerateChain(ex).Select(e => e.GetType().Name + " " + e.Message));

        if (EnumerateChain(ex).Any(e => e is System.Net.Http.HttpRequestException or System.Net.Sockets.SocketException))
            return "Không có mạng hoặc không kết nối được máy chủ đăng nhập. Kiểm tra Internet rồi thử lại.";
        if (EnumerateChain(ex).Any(e => e is OperationCanceledException) ||
            all.Contains("cancel", StringComparison.OrdinalIgnoreCase))
            return "Bạn đã đóng cửa sổ đăng nhập.";
        if (all.Contains("NotOwn", StringComparison.OrdinalIgnoreCase) ||
            all.Contains("not own", StringComparison.OrdinalIgnoreCase) ||
            all.Contains("entitle", StringComparison.OrdinalIgnoreCase) ||
            all.Contains("game ownership", StringComparison.OrdinalIgnoreCase))
            return "Tài khoản này không sở hữu Minecraft Java Edition bản quyền.";

        return "Đăng nhập thất bại: " + ex.Message;
    }

    private static IEnumerable<Exception> EnumerateChain(Exception ex)
    {
        for (var e = ex; e != null; e = e.InnerException) yield return e;
    }

    // ---------- Nhập modpack (.mrpack / .zip) ----------

    private bool CanImport() => !IsBusy && !IsGameRunning;

    [RelayCommand(CanExecute = nameof(CanImport))]
    private async Task ImportModpackAsync()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Chọn modpack",
            Filter = "Modpack (*.mrpack;*.zip)|*.mrpack;*.zip|Tất cả file|*.*"
        };
        if (dlg.ShowDialog() != true) return;
        await ImportModpackFileAsync(dlg.FileName, confirm: false);
    }

    /// <summary>
    /// Nhập modpack từ một đường dẫn (dùng cho nút "Nhập modpack" và khi Windows mở file .mrpack bằng launcher).
    /// confirm = true thì hỏi xác nhận trước, vì file đến từ bên ngoài.
    /// </summary>
    public async Task ImportModpackFileAsync(string path, bool confirm)
    {
        if (IsBusy || IsGameRunning)
        {
            MessageBox.Show("Launcher đang bận (đang tải hoặc đang chạy game). Hãy thử mở lại file sau khi xong.",
                "KittyDy0 Launcher", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (!File.Exists(path))
        {
            StatusText = "Không tìm thấy file modpack: " + path;
            return;
        }

        ModpackInfo info;
        try
        {
            info = ModpackInstaller.Inspect(path);
        }
        catch (Exception ex)
        {
            AppendLog("LỖI đọc modpack: " + ex);
            StatusText = "Không đọc được modpack: " + ex.Message;
            return;
        }

        if (confirm)
        {
            var title = string.IsNullOrWhiteSpace(info.Name) ? Path.GetFileNameWithoutExtension(path) : info.Name.Trim();
            var what = info.Kind == ModpackKind.Plain
                ? "file zip (không có manifest)"
                : $"modpack \"{title}\" (Minecraft {info.McVersion}" +
                  (info.LoaderType.Length > 0 ? $", {LoaderInstaller.Label(info.LoaderType)} {info.LoaderVersion}" : "") + ")";
            var go = MessageBox.Show($"Nhập {what} vào KittyDy0 Launcher?\n\nFile: {path}",
                "Nhập modpack", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (go != MessageBoxResult.Yes) return;
        }

        // Modpack có manifest -> tạo instance mới. Zip thường -> giải nén vào instance đang chọn.
        Instance target;
        var isNew = info.Kind != ModpackKind.Plain;
        if (isNew)
        {
            var name = string.IsNullOrWhiteSpace(info.Name) ? Path.GetFileNameWithoutExtension(path) : info.Name.Trim();
            target = new Instance { Name = name, VersionId = info.McVersion, RamMb = _config.DefaultRamMb };
        }
        else
        {
            if (SelectedInstance == null)
            {
                StatusText = "Hãy chọn một instance để giải nén file zip vào.";
                return;
            }
            target = SelectedInstance;
            var ask = MessageBox.Show(
                $"File zip này không có manifest nên launcher không biết phiên bản Minecraft hay loader.\n\n" +
                $"Giải nén vào instance \"{target.Name}\"? File trùng tên sẽ bị ghi đè.",
                "Cài file zip", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (ask != MessageBoxResult.Yes) return;
        }

        FlushSave();
        IsBusy = true;
        Progress = 0;
        var cts = new CancellationTokenSource();
        _cts = cts;
        try
        {
            AppendLog($"=== Nhập modpack: {Path.GetFileName(path)} ({info.Kind}) ===");

            // 1. Cài mod loader (nếu modpack cần)
            if (isNew && !string.IsNullOrEmpty(info.LoaderType))
            {
                AppendLog($"Modpack dùng Minecraft {info.McVersion} + {LoaderInstaller.Label(info.LoaderType)} {info.LoaderVersion}");
                var launchId = await new LoaderInstaller(AppendLog).InstallAsync(
                    info.LoaderType, info.McVersion, info.LoaderVersion,
                    c => ResolveInstallerJavaAsync(info.McVersion, c), Report, cts.Token);
                target.LoaderType = info.LoaderType;
                target.LoaderVersion = info.LoaderVersion;
                target.LaunchId = launchId;
            }

            // 2. Tải mod và chép file cấu hình vào thư mục game của instance
            var result = await new ModpackInstaller(AppendLog, _config.CurseForgeApiKey)
                .InstallAsync(path, info, target.GameDir, Report, cts.Token);

            // 3. Lưu instance mới
            if (isNew)
            {
                InstanceStore.Save(target);
                AddInstance(target);
                SelectedInstance = target;
            }

            Progress = 100;
            foreach (var w in result.Warnings) AppendLog("CẢNH BÁO: " + w);
            StatusText = result.Warnings.Count == 0
                ? $"Đã cài \"{target.Name}\" ({result.Installed} file). Bấm CHƠI để vào game."
                : $"Đã cài \"{target.Name}\" nhưng có {result.Warnings.Count} cảnh báo, xem tab Nhật ký.";
            AppendLog($"=== Xong: {target.Name}, {result.Installed} file ===");
        }
        catch (OperationCanceledException)
        {
            StatusText = "Đã hủy nhập modpack.";
            CleanUpFailedImport(target, isNew);
        }
        catch (Exception ex)
        {
            AppendLog("LỖI nhập modpack: " + ex);
            StatusText = "Lỗi nhập modpack: " + ex.Message + " (xem tab Nhật ký)";
            CleanUpFailedImport(target, isNew);
        }
        finally
        {
            IsBusy = false;
            _cts = null;
            cts.Dispose();
        }
    }

    /// <summary>Instance mới nhập dở thì xóa thư mục tạm; zip thường nhập vào instance cũ thì giữ nguyên.</summary>
    private static void CleanUpFailedImport(Instance target, bool isNew)
    {
        if (!isNew) return;
        try { InstanceStore.Delete(target); } catch { /* bỏ qua */ }
    }

    /// <summary>Java để chạy installer của Forge/NeoForge (theo nguồn Java trong cài đặt chung).</summary>
    private async Task<string> ResolveInstallerJavaAsync(string mcVersion, CancellationToken ct)
    {
        var major = await _catalog.GetRequiredJavaMajorAsync(mcVersion, ct);
        var source = _config.JavaSource;
        if (source == JavaSourceKind.Custom) source = JavaSourceKind.Auto; // chưa có đường dẫn riêng ở cấp chung

        var jm = new JavaManager(AppendLog);
        var exe = await jm.ResolveAsync(major, source, "", Report, ct);
        exe ??= await jm.DownloadChainAsync(major, null, Report, ct); // Mojang trả null: installer cần java.exe thật
        return exe ?? throw new InvalidOperationException($"Không có Java {major} để chạy trình cài mod loader.");
    }

    // ---------- Java ----------

    private bool CanInstallJava() => !IsBusy && !IsGameRunning;

    [RelayCommand(CanExecute = nameof(CanInstallJava))]
    private async Task InstallJavaAsync()
    {
        if (!int.TryParse(JavaInstallMajor, out var major)) return;

        IsBusy = true;
        Progress = 0;
        var cts = new CancellationTokenSource();
        _cts = cts;
        try
        {
            var exe = await new JavaManager(AppendLog)
                .DownloadChainAsync(major, JavaManager.VendorOf(_config.JavaSource), Report, cts.Token);
            Progress = 100;
            StatusText = exe != null ? $"Đã sẵn sàng Java {major}." : $"Không tải được Java {major} từ các nguồn.";
        }
        catch (OperationCanceledException)
        {
            StatusText = "Đã hủy.";
        }
        catch (Exception ex)
        {
            AppendLog("LỖI: " + ex);
            StatusText = "Lỗi: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
            _cts = null;
            cts.Dispose();
            RefreshManagedJava();
        }
    }

    private void RefreshManagedJava()
    {
        var list = JavaManager.ListManaged();
        ManagedJavaText = list.Length == 0 ? "Chưa có Java nào do launcher tải." : string.Join(", ", list);
    }

    // ---------- Nhật ký & tiến trình ----------

    private void Report(double pct, string text) => Ui(() =>
    {
        if (pct >= 0) Progress = Math.Min(100, pct);
        StatusText = text;
    });

    /// <summary>Gọi được từ mọi luồng; nhật ký được gom lại rồi đẩy lên giao diện mỗi 250ms.</summary>
    public void AppendLog(string line)
    {
        lock (_logLock)
        {
            _logSb.AppendLine(line);
            if (_logSb.Length > 300_000) _logSb.Remove(0, 100_000);
            _logDirty = true;
        }
    }

    private void FlushLog()
    {
        string? text = null;
        lock (_logLock)
        {
            if (_logDirty)
            {
                text = _logSb.ToString();
                _logDirty = false;
            }
        }
        if (text != null) LogText = text;
    }

    [RelayCommand]
    private void ClearLog()
    {
        lock (_logLock)
        {
            _logSb.Clear();
            _logDirty = true;
        }
    }

    [RelayCommand]
    private void CopyLog()
    {
        try { Clipboard.SetText(LogText); } catch { }
    }

    private static void Ui(Action a)
    {
        var d = Application.Current?.Dispatcher;
        if (d == null || d.CheckAccess()) a();
        else d.BeginInvoke(a);
    }
}
