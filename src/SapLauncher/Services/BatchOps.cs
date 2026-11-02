using SapLauncher.Models;

namespace SapLauncher.Services;

/// <summary>批量操作：分组移动、收藏、清除标记、删除。抽成独立类便于自检覆盖。</summary>
public static class BatchOps
{
    /// <summary>把一批连接移动到同一分组（分组不存在则新建）。返回实际处理数量。</summary>
    public static int MoveToGroup(IEnumerable<SapSystem> systems, string? group, List<GroupDef> groups)
    {
        var targets = systems.ToList();
        if (targets.Count == 0) return 0;

        if (!string.IsNullOrWhiteSpace(group) &&
            !groups.Any(g => string.Equals(g.Name, group, StringComparison.CurrentCultureIgnoreCase)))
        {
            groups.Add(new GroupDef { Name = group, SortOrder = groups.Count });
        }

        foreach (var s in targets) s.Group = group ?? "";
        return targets.Count;
    }

    public static int SetFavorite(IEnumerable<SapSystem> systems, bool favorite)
    {
        var targets = systems.ToList();
        foreach (var s in targets) s.Favorite = favorite;
        return targets.Count;
    }

    public static int ClearNew(IEnumerable<SapSystem> systems)
    {
        var targets = systems.ToList();
        foreach (var s in targets) s.IsNew = false;
        return targets.Count;
    }

    public static int Delete(List<SapSystem> all, IEnumerable<SapSystem> systems)
    {
        var targets = systems.ToList();
        foreach (var s in targets) all.Remove(s);
        return targets.Count;
    }
}
