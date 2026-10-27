using System.Windows;
using SapLauncher.Models;
using SapLauncher.Services;

namespace SapLauncher.Views;

public partial class BatchWindow : Window
{
    private readonly BatchLoginService _service = new();
    private CancellationTokenSource? _cts;
    private bool _running;

    public BatchWindow(IReadOnlyList<SapSystem> systems)
    {
        InitializeComponent();
        WindowTheme.Apply(this);

        _service.Prepare(systems);
        _service.Changed += () => Dispatcher.BeginInvoke(new Action(UpdateUi));
        ItemsList.ItemsSource = _service.Items;
        UpdateUi();
    }

    private void UpdateUi()
    {
        int total = _service.Items.Count;
        int done = _service.Items.Count(i => i.State is BatchItemState.Success or BatchItemState.Failed
            or BatchItemState.Skipped or BatchItemState.Manual);
        ProgressText.Text = string.Format(Localizer.Get("Batch.Running"), done, total);
        if (!_running && done >= total) CopyButton.IsEnabled = true;
        StartButton.IsEnabled = !_running;
        StopButton.IsEnabled = _running;
    }

    private async void OnStart(object sender, RoutedEventArgs e)
    {
        if (_running) return;
        _running = true;
        _cts = new CancellationTokenSource();
        StartButton.IsEnabled = false;
        StopButton.IsEnabled = true;
        CopyButton.IsEnabled = false;

        try
        {
            await _service.RunAsync(LoginOneAsync, AppServices.Settings.BatchIntervalSeconds,
                AppServices.Settings.SkipUnreachableInBatch, _cts.Token);
        }
        catch (Exception ex)
        {
            Log.Error("批量登录异常", ex);
        }
        finally
        {
            _running = false;
            StartButton.IsEnabled = true;
            StopButton.IsEnabled = false;
            CopyButton.IsEnabled = true;
            UpdateUi();
        }
    }

    private async Task<LaunchResult> LoginOneAsync(SapSystem system, CancellationToken ct)
    {
        var cred = AppServices.Vault.IsUnlocked ? AppServices.Vault.GetAny(system.Uuid) : null;
        var client = cred?.Client is { Length: > 0 } c
            ? c
            : (!string.IsNullOrWhiteSpace(system.LastClient) ? system.LastClient : AppServices.Settings.DefaultClient);

        var result = await AppServices.Launch.LaunchAsync(system, cred, AppServices.Settings, client,
            cred?.Language ?? "ZH", cred?.LogonGroup, ct);

        if (result.Outcome is LaunchOutcome.Success or LaunchOutcome.LaunchedWithoutProof)
        {
            system.LastLogin = DateTime.Now;
            system.LoginCount++;
            system.LastClient = client;
            system.IsNew = false;
        }
        return result;
    }

    private void OnStop(object sender, RoutedEventArgs e)
    {
        try { _cts?.Cancel(); } catch { }
    }

    private void OnCopyReport(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(_service.BuildReport());
        }
        catch (Exception ex)
        {
            Log.Warn("复制批量报告失败：" + ex.Message);
        }
    }

    private void OnClose(object sender, RoutedEventArgs e)
    {
        try { _cts?.Cancel(); } catch { }
        AppServices.Manifest.Save();
        Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        try { _cts?.Cancel(); } catch { }
        AppServices.Manifest.Save();
        base.OnClosed(e);
    }
}
