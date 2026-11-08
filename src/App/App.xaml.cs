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
            AppLog.Error(args.Exception.ToString());
            MessageBox.Show(args.Exception.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
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
