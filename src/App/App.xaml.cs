using System.Windows;
using RobloxAccountManager.Core.Services;

namespace RobloxAccountManager.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        Loc.Language = AppSettings.Current.Language;
        DispatcherUnhandledException += (_, args) =>
        {
            var full = args.Exception.ToString();
            AppLog.Error(full);
            try
            {
                Directory.CreateDirectory(AppSettings.DataDir);
                File.AppendAllText(Path.Combine(AppSettings.DataDir, "crash.log"), $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {full}{Environment.NewLine}{Environment.NewLine}");
            }
            catch { }
            // 建構函式例外會被包成 XamlParseException，Message 看不出原因；顯示最內層那個
            var inner = args.Exception;
            while (inner.InnerException is not null) inner = inner.InnerException;
            MessageBox.Show(inner == args.Exception ? inner.Message : $"{inner.GetType().Name}: {inner.Message}\n\n({args.Exception.Message})",
                "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };
        try { ThemeManager.Apply(AppSettings.Current.Theme); }
        catch (Exception ex) { AppLog.Error("套用主題失敗: " + ex.Message); }
        UiSound.Configure(AppSettings.Current);
        // 每個新視窗載入時套用主題的視窗底色
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent,
            new RoutedEventHandler((s, _) => { if (s is Window w) { Chrome.Attach(w); ThemeManager.ApplyToWindow(w); } }));
        base.OnStartup(e);

        if (e.Args.Contains("--dump-theme-keys"))
        {
            File.WriteAllLines(Path.Combine(Path.GetTempPath(), "theme_keys.txt"), ThemeManager.DumpKeys());
            Shutdown();
        }
    }
}
