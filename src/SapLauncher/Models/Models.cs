using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace SapLauncher.Models;

public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }
}

public enum ConnKind
{
    Direct,
    Router,
    LoadBalanced
}

public enum EnvKind
{
    Unknown,
    Dev,
    Qas,
    Prd
}

public enum ProbeState
{
    Unknown,
    Probing,
    Online,
    Offline
}

/// <summary>一个 SAP 连接条目。非敏感字段持久化在 manifest.json。</summary>
public class SapSystem : ObservableObject
{
    private string _name = "";
    private string _sid = "";
    private string _host = "";
    private string _router = "";
    private string _messageServer = "";
    private string _group = "";
    private string _alias = "";
    private string _lastClient = "";
    private bool _favorite;
    private bool _isStale;
    private bool _isNew;
    private bool _isSelected;
    private int _loginCount;
    private DateTime? _lastLogin;
    private EnvKind _env = EnvKind.Unknown;
    private ProbeState _probe = ProbeState.Unknown;
    private int _probeLatency;
    private int _port = 3200;

    public string Uuid { get; set; } = "";
    public string Name { get => _name; set => Set(ref _name, value); }
    public string Sid { get => _sid; set => Set(ref _sid, value); }
    public string Host { get => _host; set { if (Set(ref _host, value)) OnPropertyChanged(nameof(ConnectionText)); } }
    public int Port { get => _port; set { if (Set(ref _port, value)) OnPropertyChanged(nameof(ConnectionText)); } }
    public string Router { get => _router; set { if (Set(ref _router, value)) { OnPropertyChanged(nameof(ConnectionText)); OnPropertyChanged(nameof(IsRouter)); } } }
    public string MessageServer { get => _messageServer; set { if (Set(ref _messageServer, value)) OnPropertyChanged(nameof(ConnectionText)); } }
    public string Memo { get; set; } = "";
    public ConnKind Kind { get; set; } = ConnKind.Direct;
    public bool IsLocal { get; set; }
    public int SortOrder { get; set; }

    /// <summary>项目分组名称（用户自定义）。</summary>
    public string Group { get => _group; set => Set(ref _group, value); }
    public string Alias { get => _alias; set { if (Set(ref _alias, value)) OnPropertyChanged(nameof(DisplayName)); } }
    public bool Favorite { get => _favorite; set => Set(ref _favorite, value); }
    public EnvKind Env { get => _env; set { if (Set(ref _env, value)) { OnPropertyChanged(nameof(EnvLabel)); OnPropertyChanged(nameof(IsProduction)); } } }
    public bool EnvManual { get; set; }
    public bool IsStale { get => _isStale; set { if (Set(ref _isStale, value)) OnPropertyChanged(nameof(IsDimmed)); } }
    public bool IsNew { get => _isNew; set => Set(ref _isNew, value); }
    public int LoginCount { get => _loginCount; set { if (Set(ref _loginCount, value)) OnPropertyChanged(nameof(LoginText)); } }
    public DateTime? LastLogin { get => _lastLogin; set { if (Set(ref _lastLogin, value)) OnPropertyChanged(nameof(LoginText)); } }
    public string LastClient
    {
        get => _lastClient;
        set { if (Set(ref _lastClient, value)) { OnPropertyChanged(nameof(ClientText)); OnPropertyChanged(nameof(SidClientText)); } }
    }

    /// <summary>登录方式覆盖：Auto=默认 .sap 链 / CmdPw=命令行带密码 / SapOnly=交给 SAP 自己管密码。</summary>
    public string LoginMethod { get; set; } = "Auto";

    /// <summary>
    /// 是否在自动登录时直达事务码。关闭时 .sap 不带 OK 代码，可避免 SAP 的
    /// 「快捷方式文件安全性」确认框（该确认每次登录都会弹）。
    /// </summary>
    public bool DirectTcode { get; set; }
    public List<string> Scripts { get; set; } = new();

    /// <summary>当前默认账号名（仅界面显示，来自加密库，不持久化）。</summary>
    [JsonIgnore] public string DefaultUser { get; set; } = "";

    [JsonIgnore] public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value); }
    [JsonIgnore] public ProbeState Probe { get => _probe; set => Set(ref _probe, value); }
    [JsonIgnore] public int ProbeLatency { get => _probeLatency; set => Set(ref _probeLatency, value); }
    [JsonIgnore] public bool IsRouter => !string.IsNullOrWhiteSpace(Router);
    [JsonIgnore] public bool IsProduction => Env == EnvKind.Prd;
    [JsonIgnore] public bool IsDimmed => IsStale;
    [JsonIgnore] public string DisplayName => string.IsNullOrWhiteSpace(Alias) ? Name : Alias;

    [JsonIgnore]
    public string EnvLabel => Env switch
    {
        EnvKind.Dev => "DEV",
        EnvKind.Qas => "QAS",
        EnvKind.Prd => "PRD",
        _ => "SYS"
    };

    [JsonIgnore]
    public string ConnectionText => Kind switch
    {
        ConnKind.LoadBalanced => $"消息服务器 {MessageServer}",
        ConnKind.Router when IsRouter => $"经 Router → {Host}:{Port}",
        _ => $"{Host}:{Port}"
    };

    [JsonIgnore]
    public string SidClientText => string.IsNullOrWhiteSpace(LastClient) ? Sid : $"{Sid} · {LastClient}";

    /// <summary>列表右侧显示的客户端（未记录过则显示占位符）。</summary>
    [JsonIgnore]
    public string ClientText => string.IsNullOrWhiteSpace(LastClient) ? "—" : LastClient;

    [JsonIgnore]
    public string LoginText => LastLogin is null ? "未登录" : $"上次 {LastLogin:MM-dd HH:mm} · {LoginCount} 次";

    /// <summary>账号附加段（无默认账号时为空，用于行内显示）。</summary>
    [JsonIgnore]
    public string AccountLine => string.IsNullOrWhiteSpace(DefaultUser) ? "" : " · " + DefaultUser;

    [JsonIgnore]
    public string Tooltip =>
        $"{DisplayName}\n系统 ID：{Sid}\n连接：{ConnectionText}\nRouter：{(IsRouter ? Router : "无")}\n备注：{(string.IsNullOrWhiteSpace(Memo) ? "无" : Memo)}";
}

/// <summary>凭据（仅存在于加密库中）。</summary>
/// <summary>凭据（仅存在于加密库中）。同一「系统 + 客户端」可保存多个账号，上限 8 个。</summary>
public class Credential
{
    public string SystemUuid { get; set; } = "";
    public string Client { get; set; } = "";
    public string User { get; set; } = "";
    /// <summary>账号对应的真实姓名（可选），用于界面更友好地显示。</summary>
    public string DisplayName { get; set; } = "";
    public string Password { get; set; } = "";

    /// <summary>是否默认账号：未指定账号登录时使用它。</summary>
    public bool IsDefault { get; set; }

    public bool Remember { get; set; }
    public string Language { get; set; } = "ZH";
    public string Tcode { get; set; } = "";
    public string LogonGroup { get; set; } = "";
    public DateTime UpdatedAt { get; set; } = DateTime.Now;

    /// <summary>界面显示：「姓名 (账号)」，无姓名时仅显示账号。</summary>
    [JsonIgnore] public string Display => string.IsNullOrWhiteSpace(DisplayName) ? User : $"{DisplayName} ({User})";

    [JsonIgnore] public string Key => MakeKey(SystemUuid, Client, User);
    public static string MakeKey(string uuid, string client, string user) => uuid + "|" + client + "|" + user;

    /// <summary>深拷贝（逐字段），避免手写克隆漏字段导致数据丢失（如姓名）。</summary>
    public Credential Clone() => (Credential)MemberwiseClone();
}

public class VaultPayload
{
    public List<Credential> Items { get; set; } = new();
}

public class GroupDef
{
    public string Name { get; set; } = "";
    public int SortOrder { get; set; }
}

public class ScriptDef
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string FileName { get; set; } = "";
    public string Description { get; set; } = "";
}

public class AppSettings
{
    public string Theme { get; set; } = "Dark";
    public string Language { get; set; } = "zh-CN";
    public bool RememberMasterKey { get; set; }
    /// <summary>启用后用 Windows Hello（PIN/指纹/面容）验证代替手输主密码解锁。</summary>
    public bool UseWindowsHello { get; set; }
    public int IdleLockMinutes { get; set; } = 10;
    public bool StartWithWindows { get; set; }
    public bool MinimizeToTray { get; set; } = true;
    public bool ConfirmProduction { get; set; } = true;
    public bool SkipUnreachableInBatch { get; set; } = true;
    /// <summary>
    /// <summary>
    /// 是否允许密码出现在 sapshcut 命令行（极端备选，默认关闭）。
    /// 主路径是「.sap 定位 + 登录框自动填表」，密码不进命令行也不落盘。
    /// </summary>
    public bool AllowPasswordOnCommandLine { get; set; }

    /// <summary>
    /// SAP 弹出「快捷方式 - 登录」对话框时，是否用凭据库里的密码自动填充并点击登录。
    /// 仅当窗口属于 SAP 安装目录下的进程、且标题里的 SID/客户端与本次登录目标一致时才动；
    /// 一次登录最多回填 1 次，避免密码被拒后反复重试导致账号锁定。
    /// </summary>
    public bool AutoFillLogonDialog { get; set; } = true;

    public int BatchIntervalSeconds { get; set; } = 3;
    public string Hotkey { get; set; } = "Ctrl+Alt+S";
    public string SortMode { get; set; } = "Favorite";
    public string DefaultClient { get; set; } = "100";
    public bool ShowNewBadge { get; set; } = true;
    public DateTime? LastSyncUtc { get; set; }
    public string WorkDir { get; set; } = @"C:\SAPWorkDir";
    public bool SeenSecurityNotice { get; set; }
    public bool ReuseSession { get; set; } = true;
}

/// <summary>manifest.json 根对象（不含任何密钥）。</summary>
public class ManifestData
{
    public int Version { get; set; } = 1;
    public List<SapSystem> Systems { get; set; } = new();
    public List<GroupDef> Groups { get; set; } = new();
    public List<ScriptDef> Scripts { get; set; } = new();
    public AppSettings Settings { get; set; } = new();
}

public class SyncResult
{
    public int Added { get; set; }
    public int Updated { get; set; }
    public int Renamed { get; set; }
    public int Stale { get; set; }
    public int Revived { get; set; }
    public int Total { get; set; }
    public long ElapsedMs { get; set; }
    public bool Failed { get; set; }
    public string? Error { get; set; }
    public List<string> Details { get; } = new();

    public string Summary => Failed
        ? $"同步失败：{Error}"
        : $"新增 {Added} · 更新 {Updated}" + (Renamed > 0 ? $" · 改名 {Renamed}" : "") +
          (Stale > 0 ? $" · 失效 {Stale}" : "") + $" · 共 {Total} 条 · {ElapsedMs}ms";
}

public enum LaunchOutcome
{
    Success,
    LaunchedWithoutProof,
    NeedsManualLogin,
    Failed
}

public class LaunchResult
{
    public LaunchOutcome Outcome { get; set; }
    public string Level { get; set; } = "";
    public string Message { get; set; } = "";
}

public class LandscapeEntry
{
    public string Uuid { get; set; } = "";
    public string Name { get; set; } = "";
    public string Sid { get; set; } = "";
    public string Host { get; set; } = "";
    public int Port { get; set; } = 3200;
    public string Router { get; set; } = "";
    public string MessageServer { get; set; } = "";
    public string Memo { get; set; } = "";
    public ConnKind Kind { get; set; } = ConnKind.Direct;
    public int Order { get; set; }
    public string SourceFile { get; set; } = "";
}

public class LandscapeScanResult
{
    public List<LandscapeEntry> Entries { get; } = new();
    public List<string> Sources { get; } = new();
    public List<string> Warnings { get; } = new();
}
