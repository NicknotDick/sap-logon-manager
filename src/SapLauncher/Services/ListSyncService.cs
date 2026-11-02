using SapLauncher.Models;

namespace SapLauncher.Services;

/// <summary>把 SAP Logon 的最新配置合并进本地清单：新增、更新、改名、标记失效，均保留用户数据。</summary>
public static class ListSyncService
{
    public static SyncResult Merge(List<SapSystem> existing, LandscapeScanResult scan, AppSettings settings)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = new SyncResult { Total = 0 };

        try
        {
            var byUuid = existing
                .Where(s => !string.IsNullOrWhiteSpace(s.Uuid))
                .GroupBy(s => s.Uuid, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            var matched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var isInitialImport = existing.Count == 0;

            foreach (var entry in scan.Entries)
            {
                var key = entry.Uuid;
                if (string.IsNullOrWhiteSpace(key)) continue;

                if (byUuid.TryGetValue(key, out var sys))
                {
                    matched.Add(key);

                    // 系统改名：显示名变化必须同步，否则启动时按 Description 定位会失配
                    if (!string.Equals(sys.Name, entry.Name, StringComparison.Ordinal))
                    {
                        result.Renamed++;
                        result.Details.Add($"改名：{sys.Name} → {entry.Name}");
                        sys.Name = entry.Name;
                    }

                    bool changed = false;
                    if (!string.Equals(sys.Sid, entry.Sid, StringComparison.OrdinalIgnoreCase)) { sys.Sid = entry.Sid; changed = true; }
                    if (!string.Equals(sys.Host, entry.Host, StringComparison.OrdinalIgnoreCase)) { sys.Host = entry.Host; changed = true; }
                    if (sys.Port != entry.Port) { sys.Port = entry.Port; changed = true; }
                    if (!string.Equals(sys.Router, entry.Router, StringComparison.OrdinalIgnoreCase)) { sys.Router = entry.Router; changed = true; }
                    if (!string.Equals(sys.MessageServer, entry.MessageServer, StringComparison.OrdinalIgnoreCase)) { sys.MessageServer = entry.MessageServer; changed = true; }
                    if (sys.Kind != entry.Kind) { sys.Kind = entry.Kind; changed = true; }
                    if (!string.Equals(sys.Memo, entry.Memo, StringComparison.Ordinal)) { sys.Memo = entry.Memo; changed = true; }

                    if (sys.IsStale) { sys.IsStale = false; result.Revived++; changed = true; }
                    if (changed) result.Updated++;
                }
                else
                {
                    var created = new SapSystem
                    {
                        Uuid = entry.Uuid,
                        Name = entry.Name,
                        Sid = entry.Sid,
                        Host = entry.Host,
                        Port = entry.Port,
                        Router = entry.Router,
                        MessageServer = entry.MessageServer,
                        Kind = entry.Kind,
                        Memo = entry.Memo,
                        Env = LandscapeReader.InferEnv(entry.Name, entry.Sid),
                        // 首次导入不逐条标「新发现」，避免整屏 NEW 徽标
                        IsNew = !isInitialImport && settings.ShowNewBadge
                    };
                    existing.Add(created);
                    byUuid[key] = created;
                    matched.Add(key);
                    result.Added++;
                }
            }

            // 源中已不存在的条目：标记失效，不删除，保留凭据
            foreach (var sys in existing)
            {
                if (sys.IsLocal) continue;
                if (matched.Contains(sys.Uuid)) continue;
                if (!sys.IsStale)
                {
                    sys.IsStale = true;
                    result.Stale++;
                    result.Details.Add("失效：" + sys.Name);
                }
            }

            // 环境推断（尊重手工覆盖）
            foreach (var sys in existing)
            {
                if (sys.EnvManual) continue;
                var env = LandscapeReader.InferEnv(sys.Name, sys.Sid);
                if (env != sys.Env) sys.Env = env;
            }

            // 按同步顺序赋排序号
            for (int i = 0; i < scan.Entries.Count; i++)
            {
                if (byUuid.TryGetValue(scan.Entries[i].Uuid, out var sys)) sys.SortOrder = i;
            }

            foreach (var w in scan.Warnings) result.Details.Add("警告：" + w);
            result.Total = existing.Count;
            result.ElapsedMs = sw.ElapsedMilliseconds;
            return result;
        }
        catch (Exception ex)
        {
            Log.Error("合并配置失败", ex);
            sw.Stop();
            return new SyncResult
            {
                Failed = true,
                Error = ex.Message,
                ElapsedMs = sw.ElapsedMilliseconds,
                Total = existing.Count
            };
        }
    }
}
