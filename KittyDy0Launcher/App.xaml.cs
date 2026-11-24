using System.Windows;
using System.Windows.Threading;

namespace KittyDy0Launcher;

public partial class App : Application
{
    /// <summary>File modpack được truyền qua dòng lệnh (người dùng bấm đúp .mrpack); MainWindow sẽ nhập sau khi mở.</summary>
    public static string? StartupFile { get; private set; }

    private static int _uiErrors;

    private void OnUiException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        CrashLog.Write(e.Exception, "UI");
        e.Handled = true;

        // Lỗi lặp đi lặp lại thì thoát hẳn thay vì hiện hộp thoại mãi
        if (++_uiErrors > 5)
        {
            MessageBox.Show("Launcher gặp lỗi lặp lại nên sẽ đóng.\n\nChi tiết đã ghi vào:\n" + CrashLog.FilePath,
                "KittyDy0 Launcher", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        if (ThemeManager.RefractionOn)
        {
            // Nghi do hiệu ứng khúc xạ: tắt nó rồi để launcher chạy tiếp
            ThemeManager.TurnRefractionOff();
            MessageBox.Show("Launcher gặp lỗi khi dựng hiệu ứng nên đã tự tắt Khúc xạ.\n" +
                            "Bạn vẫn dùng bình thường; nếu muốn thử lại, bật lại trong Cài đặt, mục Giao diện.\n\n" +
                            "Chi tiết lỗi đã ghi vào:\n" + CrashLog.FilePath,
                "KittyDy0 Launcher", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        else
        {
            MessageBox.Show("Launcher gặp một lỗi: " + e.Exception.Message + "\n\nChi tiết đã ghi vào:\n" + CrashLog.FilePath,
                "KittyDy0 Launcher", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        var file = e.Args.FirstOrDefault(SingleInstance.IsModpackFile);

        // Chỉ cho một launcher chạy cùng lúc
        if (!SingleInstance.TryAcquire())
        {
            if (file != null)
            {
                // Mở thêm file .mrpack khi launcher đang chạy: chuyển file sang launcher đang chạy, không mở cửa sổ mới
                if (!SingleInstance.SendOpen(file))
                    MessageBox.Show("KittyDy0 Launcher đang chạy nhưng chưa nhận được file. Hãy thử mở lại file sau vài giây.",
                        "KittyDy0 Launcher", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                SingleInstance.SendActivate(); // đưa cửa sổ đang chạy lên trước
                MessageBox.Show(
                    "KittyDy0 Launcher đang chạy rồi nên không thể mở thêm một cửa sổ nữa.\n\n" +
                    "Cửa sổ launcher đang chạy sẽ được đưa lên phía trước.",
                    "KittyDy0 Launcher", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            Environment.Exit(0); // thoát hẳn, không tạo cửa sổ
            return;
        }

        // Lỗi chưa xử lý: ghi vào crash.log và hiện thông báo thân thiện thay vì để launcher sập
        DispatcherUnhandledException += OnUiException;
        AppDomain.CurrentDomain.UnhandledException += (_, a) => CrashLog.Write(a.ExceptionObject as Exception, "AppDomain");
        TaskScheduler.UnobservedTaskException += (_, a) => { CrashLog.Write(a.Exception, "Task"); a.SetObserved(); };

        StartupFile = file;
        SingleInstance.StartServer();

        // Nạp skin (Liquid Glass hoặc Dark Flat) theo cấu hình trước khi cửa sổ đầu tiên được tạo
        AppPaths.EnsureAll();
        var cfg = AppConfig.Load();
        ThemeManager.Initialize(cfg);

        // Cho phép Windows mở file .mrpack bằng launcher (chỉ ghi vào HKCU). Không ép làm mặc định nếu đã có app khác.
        if (cfg.AssociateMrpack && !FileAssociation.IsRegistered())
            FileAssociation.Register(force: false);

        base.OnStartup(e);
    }
}
