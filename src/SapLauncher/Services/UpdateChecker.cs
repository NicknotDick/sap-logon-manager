using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace SapLauncher.Services;

/// <summary>GitHub Release 中更新检查所需的字段。</summary>
public sealed record ReleaseAsset(string Name, string DownloadUrl, long Size);

/// <summary>一次 Release 的摘要信息（不含完整正文，避免大对象）。</summary>
public sealed record ReleaseInfo(
    string Tag,
    string? Name,
    DateTimeOffset PublishedAt,
    string? Body,
    IReadOnlyList<ReleaseAsset> Assets);

/// <summary>当前运行的安装包形态：zip 版（旁置 DLL）或单文件版。</summary>
public enum PackageKind
{
    Zip,
    SingleFile
}

/// <summary>
/// 更新检查：从 GitHub 公开 Releases API 读取最新版本，比较版本号并给出对应安装包下载链接。
/// 不上传任何本机信息，不使用仓库写权限。
/// </summary>
public static class UpdateChecker
{
    /// <summary>
    /// 当前程序的发行版本号。发布新版本时必须同步更新此常量，否则「查找更新」会误判。
    /// 与 GitHub Release 的 tag 格式一致（vX.Y.Z）。
    /// </summary>
    public const string CurrentTag = "v1.6.4";

    private const string ApiUrl =
        "https://api.github.com/repos/NicknotDick/sap-logon-manager/releases/latest";
    private const string ReleasesUrl =
        "https://github.com/NicknotDick/sap-logon-manager/releases/latest";

    /// <summary>浏览器回退入口：API 不可用/无匹配附件时打开发布页。</summary>
    public static Uri ReleasesUri { get; } = new(ReleasesUrl);

    /// <summary>zip 版附件名（发布脚本使用同一命名规则）。</summary>
    public static string ZipAssetName(string tag) => $"SAPLogonManager-{tag}.zip";

    /// <summary>单文件版附件名（发布脚本使用同一命名规则）。</summary>
    public static string SingleAssetName(string tag) => $"SAPLogonManager-{tag}-SingleFile.exe";

    /// <summary>zip 版特有的旁置原生库，存在即判定为 zip 版。</summary>
    private static readonly string[] NativeMarkers =
    {
        "PresentationNative_cor3.dll", "wpfgfx_cor3.dll", "D3DCompiler_47_cor3.dll"
    };

    /// <summary>
    /// 按程序目录判断当前运行形态：有旁置 WPF 原生 DLL → zip 版；否则按单文件版处理。
    /// 源码目录开发运行时也通常没有这些 DLL，结果与单文件版一致，属可接受兜底。
    /// </summary>
    public static PackageKind DetectPackageKind()
    {
        try
        {
            var dir = AppContext.BaseDirectory;
            foreach (var marker in NativeMarkers)
                if (File.Exists(Path.Combine(dir, marker))) return PackageKind.Zip;
        }
        catch { /* 路径异常按单文件版处理 */ }
        return PackageKind.SingleFile;
    }

    /// <summary>请求 GitHub Releases API；超时/失败由调用方捕获并给出友好提示。</summary>
    public static async Task<ReleaseInfo> GetLatestAsync(CancellationToken ct = default)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
        client.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("SapLauncher", CurrentTag.TrimStart('v')));
        client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

        using var response = await client.GetAsync(ApiUrl, ct);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(ct);
        return Parse(json);
    }

    /// <summary>解析 Releases API 的 JSON；缺字段时给空值，不抛异常。</summary>
    public static ReleaseInfo Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var tag = GetString(root, "tag_name") ?? "";
        var name = GetString(root, "name");
        var published = DateTimeOffset.UtcNow;
        if (root.TryGetProperty("published_at", out var pub) && pub.ValueKind == JsonValueKind.String)
            if (!DateTimeOffset.TryParse(pub.GetString(), out published)) published = DateTimeOffset.UtcNow;

        var assets = new List<ReleaseAsset>();
        if (root.TryGetProperty("assets", out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            foreach (var a in arr.EnumerateArray())
            {
                var assetName = GetString(a, "name");
                var url = GetString(a, "browser_download_url");
                if (string.IsNullOrEmpty(assetName) || string.IsNullOrEmpty(url)) continue;
                long size = 0;
                if (a.TryGetProperty("size", out var s) && s.ValueKind == JsonValueKind.Number)
                    s.TryGetInt64(out size);
                assets.Add(new ReleaseAsset(assetName, url, size));
            }
        }

        return new ReleaseInfo(tag, name, published, GetString(root, "body"), assets);
    }

    private static string? GetString(JsonElement el, string name)
        => el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    /// <summary>
    /// 版本比较：&gt;0 表示 newer 比 current 新。无法解析时返回 0（保守：不提示更新）。
    /// 支持 v 前缀与 -beta 等后缀。
    /// </summary>
    public static int CompareTags(string? current, string? newer)
    {
        if (!TryParseVersion(current, out var a) || !TryParseVersion(newer, out var b)) return 0;
        return b.CompareTo(a);
    }

    private static bool TryParseVersion(string? tag, out Version version)
    {
        version = new Version(0, 0);
        if (string.IsNullOrWhiteSpace(tag)) return false;
        var s = tag.Trim();
        if (s.StartsWith('v') || s.StartsWith('V')) s = s[1..];
        var cut = s.IndexOf('-');
        if (cut > 0) s = s[..cut];
        if (!Version.TryParse(s, out var parsed)) return false;
        version = parsed;
        return true;
    }

    /// <summary>
    /// 选择与当前运行形态匹配的附件：精确文件名优先，找不到则按扩展名/关键字兜底；
    /// 仍找不到返回 null，由调用方打开发布页。
    /// </summary>
    public static string? PickDownloadUrl(ReleaseInfo release, PackageKind kind)
    {
        var expected = kind == PackageKind.Zip
            ? ZipAssetName(release.Tag)
            : SingleAssetName(release.Tag);

        var exact = release.Assets.FirstOrDefault(
            a => string.Equals(a.Name, expected, StringComparison.OrdinalIgnoreCase));
        if (exact is not null) return exact.DownloadUrl;

        var fallback = kind == PackageKind.Zip
            ? release.Assets.FirstOrDefault(
                a => a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            : release.Assets.FirstOrDefault(
                a => a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                     && a.Name.Contains("Single", StringComparison.OrdinalIgnoreCase));

        return fallback?.DownloadUrl;
    }
}
