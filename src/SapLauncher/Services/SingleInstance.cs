using System.IO;
using System.IO.Pipes;
using System.Text;

namespace SapLauncher.Services;

/// <summary>单实例：第二个进程通过命名管道把「唤出窗口」指令发给已运行实例。</summary>
public sealed class SingleInstance : IDisposable
{
    private static readonly string MutexName = "Local\\SapLauncher_" + Environment.UserName;
    private static readonly string PipeName = "SapLauncherPipe_" + Environment.UserName;

    private Mutex? _mutex;
    private CancellationTokenSource? _cts;
    private bool _owned;

    public bool IsFirstInstance { get; private set; }

    public event Action<string>? CommandReceived;

    public bool Acquire()
    {
        // 允许重试：先释放自身持有的旧句柄，否则内核对象仍存在、created 永远为 false
        try { _mutex?.Dispose(); } catch { }
        _mutex = null;
        _owned = false;

        _mutex = new Mutex(true, MutexName, out bool created);
        IsFirstInstance = created;
        _owned = created;
        if (created) StartServer();
        return created;
    }

    private void StartServer()
    {
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        _ = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await using var server = new NamedPipeServerStream(PipeName, PipeDirection.In, 1,
                        PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                    await server.WaitForConnectionAsync(token).ConfigureAwait(false);
                    using var reader = new StreamReader(server, Encoding.UTF8);
                    var cmd = await reader.ReadLineAsync(token).ConfigureAwait(false);
                    if (!string.IsNullOrWhiteSpace(cmd)) CommandReceived?.Invoke(cmd!);
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    Log.Warn("单实例管道异常：" + ex.Message);
                    await Task.Delay(500).ConfigureAwait(false);
                }
            }
        }, token);
    }

    public static bool NotifyExisting(string command = "show")
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(1200);
            using var writer = new StreamWriter(client, Encoding.UTF8) { AutoFlush = true };
            writer.WriteLine(command);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        try { _cts?.Cancel(); } catch { }
        try
        {
            if (_owned) _mutex?.ReleaseMutex();
        }
        catch { }
        _mutex?.Dispose();
        _cts?.Dispose();
    }
}
