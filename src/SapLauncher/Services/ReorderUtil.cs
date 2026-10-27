using SapLauncher.Models;

namespace SapLauncher.Services;

/// <summary>长按拖拽排序：把某项移到目标位置并按新顺序重写 SortOrder（0..n-1）。</summary>
public static class ReorderUtil
{
    public static bool MoveSystem(List<SapSystem> systems, SapSystem dragged, SapSystem target)
    {
        var full = systems.OrderBy(s => s.SortOrder).ToList();
        int from = full.IndexOf(dragged), to = full.IndexOf(target);
        if (from < 0 || to < 0 || from == to) return false;
        var item = full[from];
        full.RemoveAt(from);
        full.Insert(to, item);
        for (int i = 0; i < full.Count; i++) full[i].SortOrder = i;
        return true;
    }

    public static bool MoveGroup(List<GroupDef> groups, GroupDef dragged, GroupDef target)
    {
        var full = groups.OrderBy(g => g.SortOrder).ToList();
        int from = full.IndexOf(dragged), to = full.IndexOf(target);
        if (from < 0 || to < 0 || from == to) return false;
        var item = full[from];
        full.RemoveAt(from);
        full.Insert(to, item);
        for (int i = 0; i < full.Count; i++) full[i].SortOrder = i;
        return true;
    }
}
