using System.Windows;
using System.Windows.Controls;
using RobloxAccountManager.Core.Models;
using RobloxAccountManager.Core.Services;
using static RobloxAccountManager.App.Ui;

namespace RobloxAccountManager.App;

/// <summary>
/// 產生帳號：輸入 BloxGen（BLOX-…）或 Altgen（MG_…）的 API key，買到的帳號會一個一個交給主視窗加入並立刻存檔。
/// </summary>
public sealed class GeneratorWindow : Window
{
    private readonly AppSettings _s = AppSettings.Current;
    private readonly Func<Account, Task> _accept;
    private readonly PasswordBox _key = new() { Margin = Gap(0, 0, 0, 6) };
    private readonly TextBlock _provider = new() { Margin = Gap(0, 0, 0, 10), Opacity = 0.7 };
    private readonly StackPanel _typeRow;
    private readonly ComboBox _type = new() { DisplayMemberPath = "Label", SelectedValuePath = "Value", MinWidth = 180 };
    private readonly TextBox _count;
    private readonly Button _go;
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Margin = Gap(0, 10, 0, 0) };
    private readonly CancellationTokenSource _cts = new();
    private bool _busy;

    /// <param name="accept">每取得一個帳號就呼叫一次（在 UI 執行緒）；呼叫端負責驗證、加入清單並存檔。</param>
    public GeneratorWindow(Func<Account, Task> accept)
    {
        _accept = accept;
        Title = T("Generate Accounts");
        Chrome.Attach(this);
        Width = 480; SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        var p = new StackPanel { Margin = new Thickness(16) };
        p.Children.Add(Label(T("Buy and import accounts from BloxGen or Altgen. Each account is saved as soon as it is received."), 10, dim: true));

        p.Children.Add(Label(T("API key (BloxGen: BLOX-… / Altgen: MG_…):")));
        _key.Password = _s.GeneratorApiKey;
        _key.PasswordChanged += (_, _) => UpdateProvider();
        p.Children.Add(_key);
        p.Children.Add(_provider);

        _type.ItemsSource = AccountGenerator.BloxGenTypes.Select(t => new { t.Value, t.Label }).ToList();
        _type.SelectedValue = AccountGenerator.BloxGenTypes.Any(t => t.Value == _s.GeneratorBloxGenType) ? _s.GeneratorBloxGenType : "alt";
        _typeRow = Row(Label(T("Account type:"), 0), _type);
        p.Children.Add(_typeRow);

        _count = Box(Math.Clamp(_s.GeneratorCount, 1, AccountGenerator.MaxCount).ToString(), 70);
        p.Children.Add(Row(Label(T("Quantity (1-100):"), 0), _count));
        p.Children.Add(Label(T("BloxGen is bought one account at a time; Altgen buys the whole quantity in one request."), 0, dim: true));

        _go = Btn(T("Generate"), async (_, _) => await GenerateAsync(), 100);
        var close = Btn(T("Close"), (_, _) => Close(), 80, left: 8);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = Gap(0, 12, 0, 0) };
        buttons.Children.Add(_go);
        buttons.Children.Add(close);
        p.Children.Add(buttons);
        p.Children.Add(_status);
        Content = p;

        UpdateProvider();
        Closing += (_, e) =>
        {
            if (_busy && !Ui.Confirm(this, T("Generation is still running. Stop it and close?"))) { e.Cancel = true; return; }
            _cts.Cancel();
        };
    }

    private void UpdateProvider()
    {
        var kind = AccountGenerator.Detect(_key.Password);
        _provider.Text = kind switch
        {
            GeneratorProvider.BloxGen => "BloxGen",
            GeneratorProvider.Altgen => "Altgen",
            _ => T("Key not recognised"),
        };
        _typeRow.Visibility = kind == GeneratorProvider.BloxGen ? Visibility.Visible : Visibility.Collapsed;   // 只有 BloxGen 有帳號種類
    }

    private async Task GenerateAsync()
    {
        if (_busy) return;
        var key = _key.Password.Trim();
        if (AccountGenerator.Detect(key) == GeneratorProvider.Unknown)
        {
            _status.Text = T("Enter a valid API key (BloxGen: BLOX-... or Altgen: MG_...)");
            return;
        }
        if (!int.TryParse(_count.Text.Trim(), out var count) || count < 1 || count > AccountGenerator.MaxCount)
        {
            _status.Text = T("Quantity must be between 1 and 100.");
            return;
        }
        var type = _type.SelectedValue as string ?? "alt";

        _s.GeneratorApiKey = key;
        _s.GeneratorBloxGenType = type;
        _s.GeneratorCount = count;
        _s.Save();

        _busy = true; _go.IsEnabled = false;
        _status.Text = T("Generating…");
        int got = 0;
        try
        {
            var result = await AccountGenerator.GenerateAsync(key, count, type, async acc =>
            {
                await _accept(acc);
                got++;
                _status.Text = $"{T("Generating…")} {got}/{count}";
            }, _cts.Token);
            _status.Text = result.Error is null
                ? $"{T("Done:")} {result.Accounts.Count}"
                : $"{T("Stopped:")} {result.Error}  ({T("Generated")} {result.Accounts.Count}/{count})";
        }
        catch (OperationCanceledException) { _status.Text = T("Cancelled."); }
        catch (Exception ex) { _status.Text = ex.Message; }
        finally { _busy = false; _go.IsEnabled = true; }
    }
}
