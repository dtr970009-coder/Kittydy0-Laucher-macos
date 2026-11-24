using System.Diagnostics;
using KittyDy0Launcher.Controls;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace KittyDy0Launcher;

public partial class MainWindow : LiquidGlassWindow
{
    private readonly MainViewModel _vm = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _vm;

        // Có launcher thứ hai được mở: đưa cửa sổ này lên trước, hoặc nhập file .mrpack được gửi tới
        // Đổi ảnh nền thì chụp lại nền để thấu kính khúc xạ đúng ảnh mới (chờ giao diện vẽ xong rồi mới chụp)
        _vm.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(MainViewModel.BackgroundImage))
                Dispatcher.InvokeAsync(GlassBackdrop.Invalidate, System.Windows.Threading.DispatcherPriority.Render);
        };

        ThemeManager.Changed += ApplyNavDock; // đổi vị trí menu trong Cài đặt thì áp dụng ngay

        SingleInstance.ActivateRequested += BringToFront;
        SingleInstance.FileRequested += OnFileRequested;
        Closed += (_, _) =>
        {
            ThemeManager.Changed -= ApplyNavDock;
            SingleInstance.ActivateRequested -= BringToFront;
            SingleInstance.FileRequested -= OnFileRequested;
        };

        // Ẩn launcher khi game chạy, hiện lại khi game thoát
        _vm.GameStateChanged += running =>
        {
            if (!_vm.HideOnPlay) return;
            if (running)
            {
                WindowState = WindowState.Minimized;
            }
            else
            {
                WindowState = WindowState.Normal;
                Activate();
            }
        };

        Loaded += (_, _) =>
        {
            // Ảnh nền làm nguồn khúc xạ cho mọi thấu kính kính-lỏng
            GlassBackdrop.SetSource(BackdropArt);

            // Vị trí thanh menu theo cá nhân hóa (đặt trước khi định vị chỉ báo)
            ApplyNavDock();

            MoveIndicator(NavHome, animate: false);
            ShowPage("home");

            if (ThemeManager.RefractionNotice is { } note)
                MessageBox.Show(note, "KittyDy0 Launcher", MessageBoxButton.OK, MessageBoxImage.Information);

            // Windows mở launcher bằng file .mrpack (bấm đúp): nhập modpack đó
            if (App.StartupFile != null) OnFileRequested(App.StartupFile);
        };
    }

    /// <summary>Đưa cửa sổ lên trước kể cả khi đang thu nhỏ.</summary>
    private void BringToFront()
    {
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Show();
        Activate();
        Topmost = true;   // mẹo để Windows chịu đưa cửa sổ lên trước
        Topmost = false;
        Focus();
    }

    /// <summary>Nhập file modpack được mở từ bên ngoài (đã có hỏi xác nhận trong ViewModel).</summary>
    private async void OnFileRequested(string path)
    {
        BringToFront();
        NavInstances.IsChecked = true; // chuyển sang tab Instance để thấy instance mới
        try
        {
            await _vm.ImportModpackFileAsync(path, confirm: true);
        }
        catch (Exception ex)
        {
            MessageBox.Show("Không nhập được modpack: " + ex.Message, "KittyDy0 Launcher",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>Bấm vào nền mờ phía ngoài thẻ chi tiết thì đóng (bấm vào trong thẻ thì không).</summary>
    private void ModDetailBackdrop_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, sender)) _vm.CloseModDetailCommand.Execute(null);
    }

    private void DefaultApps_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo("ms-settings:defaultapps") { UseShellExecute = true });
        }
        catch
        {
            MessageBox.Show("Không mở được Cài đặt Windows. Hãy chuột phải vào file .mrpack, chọn \"Mở bằng\" rồi \"Chọn ứng dụng khác\".",
                "KittyDy0 Launcher", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    // Kéo cửa sổ bằng thanh tiêu đề tự vẽ
    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void CopyIp_Click(object sender, RoutedEventArgs e)
    {
        try { Clipboard.SetText(GameRunner.ServerHost); }
        catch { /* clipboard có thể đang bị app khác giữ */ }
    }

    // Nhật ký tự cuộn xuống dòng mới nhất
    private void LogBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is TextBox tb) tb.ScrollToEnd();
    }

    // PasswordBox không binding được: chuyển mật khẩu sang ViewModel đúng lúc bấm nút rồi xóa ô nhập
    private void ElyLogin_Click(object sender, RoutedEventArgs e)
    {
        _vm.ElyPassword = ElyPassBox.Password;
        if (_vm.ElyLoginCommand.CanExecute(null)) _vm.ElyLoginCommand.Execute(null);
        ElyPassBox.Clear();
    }

    private void GlassInfo_Click(object sender, RoutedEventArgs e) => GlassInfoPopup.IsOpen = true;

    private void Nav_Checked(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || sender is not RadioButton rb) return;
        MoveIndicator(rb, animate: true);
        ShowPage(rb.Tag as string ?? "home");
    }

    private NavDock? _dock;
    private bool _navVertical;

    private RadioButton? CheckedNavItem() => NavItems.Children.OfType<RadioButton>().FirstOrDefault(r => r.IsChecked == true);

    /// <summary>
    /// Đặt thanh menu ở phía người dùng chọn (trên, dưới, trái, phải). Trái/phải thì xếp mục theo chiều dọc.
    /// Chỉ làm việc khi vị trí thật sự đổi.
    /// </summary>
    private void ApplyNavDock()
    {
        try
        {
            var dock = ThemeManager.Prefs.MenuDock;
            if (_dock == dock) return;
            _dock = dock;

            // Tháo thanh menu khỏi chỗ cũ
            switch (NavBar.Parent)
            {
                case Panel p: p.Children.Remove(NavBar); break;
                case Border bd: bd.Child = null; break;
            }

            _navVertical = dock is NavDock.Left or NavDock.Right;
            NavItems.Orientation = _navVertical ? Orientation.Vertical : Orientation.Horizontal;

            switch (dock)
            {
                case NavDock.Left:
                    NavSlotLeft.Child = NavBar;
                    break;
                case NavDock.Right:
                    NavSlotRight.Child = NavBar;
                    break;
                case NavDock.Bottom:
                    NavSlotBottom.Child = NavBar;
                    break;
                default:
                    TitleBarGrid.Children.Add(NavBar);
                    Grid.SetColumn(NavBar, 1);
                    NavBar.HorizontalAlignment = HorizontalAlignment.Center;
                    break;
            }

            UpdateLayout(); // đo lại để đặt chỉ báo đúng chỗ ngay
            if (CheckedNavItem() is { } cur) MoveIndicator(cur, animate: false);
        }
        catch (Exception ex)
        {
            CrashLog.Write(ex, "NavDock");
        }
    }

    /// <summary>
    /// Chuyển chỉ báo tới mục menu được chọn, theo chiều ngang hoặc dọc tùy vị trí menu.
    /// Mặc định trượt gọn ~220ms; bật "Hiệu ứng tương tác" thì kéo dãn phủ cả mục cũ lẫn mới rồi nảy vào (~460ms).
    /// Tắt chuyển động (Windows hoặc cài đặt) thì nhảy thẳng tới đích.
    /// </summary>
    private void MoveIndicator(FrameworkElement target, bool animate)
    {
        var p = target.TranslatePoint(new Point(0, 0), NavHost);
        var vert = _navVertical;
        var pos1 = vert ? p.Y : p.X;
        var len1 = vert ? target.ActualHeight : target.ActualWidth;
        NavIndicator.Opacity = 1;

        var moveProp = vert ? TranslateTransform.YProperty : TranslateTransform.XProperty;
        var otherMove = vert ? TranslateTransform.XProperty : TranslateTransform.YProperty;
        var sizeProp = vert ? HeightProperty : WidthProperty;
        var squashProp = vert ? ScaleTransform.ScaleXProperty : ScaleTransform.ScaleYProperty;

        var pos0 = vert ? NavMove.Y : NavMove.X;
        var len0 = vert ? NavIndicator.ActualHeight : NavIndicator.ActualWidth;

        // Trục còn lại luôn khớp kích thước mục
        if (vert) NavIndicator.Width = target.ActualWidth; else NavIndicator.Height = target.ActualHeight;
        NavMove.BeginAnimation(otherMove, null);
        if (vert) NavMove.X = 0; else NavMove.Y = 0;

        if (!animate || !ThemeManager.MotionEnabled || len0 < 1)
        {
            NavMove.BeginAnimation(moveProp, null);
            NavIndicator.BeginAnimation(WidthProperty, null);
            NavIndicator.BeginAnimation(HeightProperty, null);
            NavScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            NavScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            NavScale.ScaleX = 1;
            NavScale.ScaleY = 1;
            if (vert) { NavMove.Y = pos1; NavIndicator.Height = len1; NavIndicator.Width = target.ActualWidth; }
            else { NavMove.X = pos1; NavIndicator.Width = len1; NavIndicator.Height = target.ActualHeight; }
            return;
        }

        if (!ThemeManager.FancyEffects)
        {
            // Trượt đơn giản, không kéo dãn hay nảy
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            var dur = TimeSpan.FromMilliseconds(220);
            NavMove.BeginAnimation(moveProp, new DoubleAnimation(pos1, dur) { EasingFunction = ease });
            NavIndicator.BeginAnimation(sizeProp, new DoubleAnimation(len1, dur) { EasingFunction = ease });
            return;
        }

        // Kéo dãn: phủ từ mép đầu tới mép cuối của mục cũ và mục mới
        var start = Math.Min(pos0, pos1);
        var span = Math.Max(pos0 + len0, pos1 + len1) - start;
        var total = TimeSpan.FromMilliseconds(460);
        var stretchEase = new CubicEase { EasingMode = EasingMode.EaseOut };
        var settleEase = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.3 };

        var am = new DoubleAnimationUsingKeyFrames { Duration = total };
        am.KeyFrames.Add(new EasingDoubleKeyFrame(pos0, KeyTime.FromPercent(0)));
        am.KeyFrames.Add(new EasingDoubleKeyFrame(start, KeyTime.FromPercent(0.38), stretchEase));
        am.KeyFrames.Add(new EasingDoubleKeyFrame(pos1, KeyTime.FromPercent(1), settleEase));

        var al = new DoubleAnimationUsingKeyFrames { Duration = total };
        al.KeyFrames.Add(new EasingDoubleKeyFrame(len0, KeyTime.FromPercent(0)));
        al.KeyFrames.Add(new EasingDoubleKeyFrame(span, KeyTime.FromPercent(0.38), stretchEase));
        al.KeyFrames.Add(new EasingDoubleKeyFrame(len1, KeyTime.FromPercent(1), settleEase));

        var sq = new DoubleAnimationUsingKeyFrames { Duration = total };
        sq.KeyFrames.Add(new EasingDoubleKeyFrame(1.0, KeyTime.FromPercent(0)));
        sq.KeyFrames.Add(new EasingDoubleKeyFrame(0.86, KeyTime.FromPercent(0.38), stretchEase));
        sq.KeyFrames.Add(new EasingDoubleKeyFrame(1.06, KeyTime.FromPercent(0.75), stretchEase));
        sq.KeyFrames.Add(new EasingDoubleKeyFrame(1.0, KeyTime.FromPercent(1), stretchEase));

        NavMove.BeginAnimation(moveProp, am);
        NavIndicator.BeginAnimation(sizeProp, al);
        NavScale.BeginAnimation(squashProp, sq);
    }

    private void ShowPage(string tag)
    {
        PageHome.Visibility = tag == "home" ? Visibility.Visible : Visibility.Collapsed;
        PageInstances.Visibility = tag == "instances" ? Visibility.Visible : Visibility.Collapsed;
        PageMods.Visibility = tag == "mods" ? Visibility.Visible : Visibility.Collapsed;
        PageAccounts.Visibility = tag == "accounts" ? Visibility.Visible : Visibility.Collapsed;
        PageLog.Visibility = tag == "log" ? Visibility.Visible : Visibility.Collapsed;
        PageNews.Visibility = tag == "news" ? Visibility.Visible : Visibility.Collapsed;
        PageSettings.Visibility = tag == "settings" ? Visibility.Visible : Visibility.Collapsed;

        // Mở tab Mods lần đầu thì hiện luôn danh sách mod phổ biến cho instance đang chọn
        if (tag == "mods" && _vm.ModResults.Count == 0 && _vm.SearchModsCommand.CanExecute(null))
            _vm.SearchModsCommand.Execute(null);
    }
}
