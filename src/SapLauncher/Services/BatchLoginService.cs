using SapLauncher.Models;

namespace SapLauncher.Services;

public enum BatchItemState
{
    Pending,
    Running,
    Success,
    Manual,
    Skipped,
    Failed
}

public class BatchItem : ObservableObject
{
    private BatchItemState _state = BatchItemState.Pending;
    private string _detail = "等待";

    public BatchItem(SapSystem system) => System = system;

    public SapSystem System { get; }
    public string Name => System.DisplayName;
    public string SidClient => System.SidClientText;

    public BatchItemState State
    {
        get => _state;
        set
        {
            if (Set(ref _state, value)) OnPropertyChanged(nameof(StateText));
        }
    }

    public string Detail { get => _detail; set => Set(ref _detail, value); }

    public string StateText => State switch
    {
        BatchItemState.Pending => "等待",
        BatchItemState.Running => "连接中",
        BatchItemState.Success => "成功",
        BatchItemState.Manual => "需手输",
        BatchItemState.Skipped => "已跳过",
        _ => "失败"
    };
}

/// <summary>顺序批量登录：单个失败不影响后续，失败不重试（避免账号锁定）。</summary>
public sealed class BatchLoginService
{
    private readonly List<BatchItem> _items = new();

    public IReadOnlyList<BatchItem> Items => _items;
    public bool IsRunning { get; private set; }
    public event Action? Changed;

    public void Prepare(IEnumerable<SapSystem> systems)
    {
        _items.Clear();
        foreach (var s in systems) _items.Add(new BatchItem(s));
        Changed?.Invoke();
    }

    public async Task RunAsync(Func<SapSystem, CancellationToken, Task<LaunchResult>> login,
        int intervalSeconds, bool skipUnreachable, CancellationToken ct)
    {
        IsRunning = true;
        try
        {
            foreach (var item in _items)
            {
                if (ct.IsCancellationRequested) break;

                if (skipUnreachable && item.System.Probe == ProbeState.Offline)
                {
                    item.State = BatchItemState.Skipped;
                    item.Detail = "网络不可达，已跳过";
                    Changed?.Invoke();
                    continue;
                }

                item.State = BatchItemState.Running;
                item.Detail = "正在登录…";
                Changed?.Invoke();

                LaunchResult result;
                try
                {
                    result = await login(item.System, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    item.State = BatchItemState.Failed;
                    item.Detail = "已中止";
                    Changed?.Invoke();
                    break;
                }
                catch (Exception ex)
                {
                    result = new LaunchResult
                    {
                        Outcome = LaunchOutcome.Failed,
                        Level = "-",
                        Message = ex.Message
                    };
                }

                item.State = result.Outcome switch
                {
                    LaunchOutcome.Success => BatchItemState.Success,
                    LaunchOutcome.LaunchedWithoutProof => BatchItemState.Success,
                    LaunchOutcome.NeedsManualLogin => BatchItemState.Manual,
                    _ => BatchItemState.Failed
                };
                item.Detail = result.Message;
                Changed?.Invoke();

                if (ct.IsCancellationRequested) break;
                if (!ReferenceEquals(item, _items[^1]))
                {
                    try { await Task.Delay(Math.Max(1, intervalSeconds) * 1000, ct).ConfigureAwait(false); }
                    catch (OperationCanceledException) { break; }
                }
            }
        }
        finally
        {
            IsRunning = false;
            Changed?.Invoke();
            Log.Info($"批量登录结束：成功 {_items.Count(i => i.State == BatchItemState.Success)}，" +
                     $"需手输 {_items.Count(i => i.State == BatchItemState.Manual)}，" +
                     $"失败 {_items.Count(i => i.State == BatchItemState.Failed)}，" +
                     $"跳过 {_items.Count(i => i.State == BatchItemState.Skipped)}");
        }
    }

    public string BuildReport()
    {
        var lines = new List<string> { $"批量登录结果（{DateTime.Now:yyyy-MM-dd HH:mm}）" };
        foreach (var i in _items)
            lines.Add($"[{i.StateText}] {i.Name}（{i.SidClient}）：{i.Detail}");
        return string.Join(Environment.NewLine, lines);
    }
}
