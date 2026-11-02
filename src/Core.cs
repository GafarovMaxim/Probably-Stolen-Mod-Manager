using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace ProbablyModManager
{
    public sealed class ModInfo
    {
        public string Id = "", Name = "", Author = "", Version = "", Description = "", Source = "", Folder = "", LoadError = "", Trust = "", Hash = "";
        public string[] Dependencies = Array.Empty<string>(), GameVersions = Array.Empty<string>(), Capabilities = Array.Empty<string>();
        public string[] TypeNames = Array.Empty<string>();
        public bool Loaded;
    }
    public sealed class Profile
    {
        public string Name = "";
        public string GameVersion = "";
        public List<string> Order = new List<string>();
        public Dictionary<string, string> Versions = new Dictionary<string, string>();
    }
    public sealed class HistoryItem
    {
        public string Utc = DateTime.UtcNow.ToString("o"), Title = "", Detail = "";
    }
    public sealed class ManagerState
    {
        public int Schema = 2;
        public bool Russian = true;
        public bool InitialProfileCreated;
        public string ActiveProfileName = "";
        public List<Profile> Profiles = new List<Profile>();
        public Dictionary<string, string> Notes = new Dictionary<string, string>(), Tags = new Dictionary<string, string>();
        public Dictionary<string, List<string>> ModTags = new Dictionary<string, List<string>>();
        public List<HistoryItem> History = new List<HistoryItem>();
        public Dictionary<string, string> LastVersions = new Dictionary<string, string>(), LastHashes = new Dictionary<string, string>();
        public void MigrateTags()
        {
            ModTags = ModTags ?? new Dictionary<string, List<string>>();
            foreach (var pair in Tags ?? new Dictionary<string,string>()) if (!ModTags.ContainsKey(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value)) ModTags[pair.Key] = new List<string> { pair.Value };
            foreach (var key in ModTags.Keys.ToList()) ModTags[key] = Planner.CleanTags(ModTags[key]);
            Schema = 2;
        }
        public void HistoryAdd(string title, string detail = "")
        {
            History.Insert(0, new HistoryItem { Title = title, Detail = detail });
            if (History.Count > 150) History.RemoveRange(150, History.Count - 150);
        }
    }
    public sealed class Finding
    {
        public string Code = "", ModId = "", Title = "", Detail = "";
        public bool Blocks;
    }
    public static class Planner
    {
        public static List<string> MoveGroupBefore(IEnumerable<string> source,IEnumerable<string> selected,string before)
        {
            var order=source.Distinct().ToList();var ids=new HashSet<string>(selected);var moving=order.Where(ids.Contains).ToList();
            if(moving.Count==0||ids.Contains(before??""))return order;
            var rest=order.Where(id=>!ids.Contains(id)).ToList();int index=before==null?rest.Count:rest.IndexOf(before);rest.InsertRange(index<0?rest.Count:index,moving);return rest;
        }
        public static List<string> Dependants(IReadOnlyList<ModInfo> mods, IEnumerable<string> selected, string id)
        {
            var enabled = new HashSet<string>(selected); var removed = new HashSet<string> { id }; bool changed;
            do { changed = false; foreach (var m in mods.Where(m => enabled.Contains(m.Id))) if (!removed.Contains(m.Id) && m.Dependencies.Any(removed.Contains)) { removed.Add(m.Id); changed = true; } } while (changed);
            return selected.Where(removed.Contains).ToList();
        }
        public static List<string> CleanTags(IEnumerable<string> tags) => (tags ?? Enumerable.Empty<string>()).Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t.Trim()).Where(t => t.Length <= 40).Distinct(StringComparer.OrdinalIgnoreCase).Take(12).ToList();
        public static List<string> MoveBefore(IEnumerable<string> source,string id,string before)
        {
            var order=source.Distinct().ToList();if(!order.Contains(id)||id==before)return order;
            order.Remove(id);int index=before==null?order.Count:order.IndexOf(before);order.Insert(index<0?order.Count:index,id);return order;
        }
        public static List<Finding> Validate(IReadOnlyList<ModInfo> mods, IList<string> order, string managerId)
        {
            var results = new List<Finding>();
            var map = mods.GroupBy(m => m.Id).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
            var selected = new HashSet<string>(order, StringComparer.Ordinal);
            if (!selected.Contains(managerId)) results.Add(new Finding { Code = "MANAGER_REQUIRED", Title = "Manager must stay enabled", Blocks = true });
            foreach (var duplicate in mods.GroupBy(m => m.Id).Where(g => g.Count() > 1))
                results.Add(new Finding { Code = "DUPLICATE_ID", ModId = duplicate.Key, Title = "Duplicate mod ID", Detail = duplicate.Key, Blocks = selected.Contains(duplicate.Key) });
            foreach (var id in order)
            {
                if (!map.TryGetValue(id, out var mod)) { results.Add(new Finding { Code = "MISSING_MOD", ModId = id, Title = "Mod is not installed", Detail = id, Blocks = true }); continue; }
                foreach (var dep in mod.Dependencies)
                {
                    if (!selected.Contains(dep)) results.Add(new Finding { Code = "MISSING_DEPENDENCY", ModId = id, Title = "Dependency is missing or disabled", Detail = mod.Name + " → " + dep, Blocks = true });
                    else if (order.IndexOf(dep) > order.IndexOf(id)) results.Add(new Finding { Code = "LOAD_ORDER", ModId = id, Title = "Dependency must load first", Detail = mod.Name + " → " + dep, Blocks = true });
                }
            }
            if (!TrySort(mods, order, out _, out var cycle)) results.Add(new Finding { Code = "DEPENDENCY_CYCLE", Title = "Dependency cycle", Detail = cycle, Blocks = true });
            return results;
        }
        public static bool TrySort(IReadOnlyList<ModInfo> mods, IEnumerable<string> source, out List<string> sorted, out string cycle)
        {
            var result = new List<string>(); var visited = new HashSet<string>(); var visiting = new HashSet<string>(); var path = new List<string>();
            var sourceList = source.Distinct(StringComparer.Ordinal).ToList(); var chosen = new HashSet<string>(sourceList);
            var map = mods.GroupBy(m => m.Id).ToDictionary(g => g.Key, g => g.First()); string foundCycle = "";
            bool Visit(string id)
            {
                if (visited.Contains(id)) return true;
                if (!visiting.Add(id)) { foundCycle = string.Join(" → ", path.Concat(new[] { id })); return false; }
                path.Add(id);
                if (map.TryGetValue(id, out var mod)) foreach (var dep in mod.Dependencies.Where(chosen.Contains)) if (!Visit(dep)) return false;
                path.RemoveAt(path.Count - 1); visiting.Remove(id); visited.Add(id); result.Add(id); return true;
            }
            foreach (var id in sourceList) if (!Visit(id)) { sorted = sourceList; cycle = foundCycle; return false; }
            sorted = result; cycle = ""; return true;
        }
        public static List<string> WithDependencies(IReadOnlyList<ModInfo> mods, IEnumerable<string> original, IEnumerable<string> additions)
        {
            var selected = original.Distinct().ToList(); var map = mods.GroupBy(m => m.Id).ToDictionary(g => g.Key, g => g.First());
            void Add(string id) { if (selected.Contains(id) || !map.ContainsKey(id)) return; selected.Add(id); foreach (var dep in map[id].Dependencies) Add(dep); }
            foreach (var id in additions) Add(id);
            return TrySort(mods, selected, out var sorted, out _) ? sorted : selected;
        }
        public static List<string> EnableBefore(IReadOnlyList<ModInfo> mods,IEnumerable<string> original,IEnumerable<string> additions,string before)
        {
            var previous=original.Distinct().ToList();var selected=additions.Distinct().ToList();
            var expanded=WithDependencies(mods,previous,selected);
            // Newly required dependencies move with the dropped mods, in dependency order.
            var moving=expanded.Where(id=>!previous.Contains(id)||selected.Contains(id)).ToList();
            var placed=MoveGroupBefore(expanded,moving,before);
            return TrySort(mods,placed,out var sorted,out _)?sorted:placed;
        }
        public static string Normalize(string version) => Regex.Replace((version ?? "").ToLowerInvariant().Replace("version", ""), "[^a-z0-9]", "");
        public static string ProfileDifference(Profile p, IReadOnlyList<ModInfo> mods, IEnumerable<string> current)
        {
            var map = mods.GroupBy(m => m.Id).ToDictionary(g => g.Key, g => g.First()); var now = current.ToList(); var b = new StringBuilder();
            foreach (var id in now.Except(p.Order)) b.AppendLine("− " + (map.TryGetValue(id, out var old) ? old.Name : id));
            foreach (var id in p.Order.Except(now)) b.AppendLine("+ " + (map.TryGetValue(id, out var add) ? add.Name : id) + (map.ContainsKey(id) ? "" : " [not installed]"));
            if (!p.Order.SequenceEqual(now) && new HashSet<string>(p.Order).SetEquals(now)) b.AppendLine("↕ Load order differs");
            foreach (var pair in p.Versions) if (map.TryGetValue(pair.Key, out var m) && pair.Value != m.Version) b.AppendLine("! " + m.Name + ": " + pair.Value + " → " + m.Version);
            return b.Length == 0 ? "=" : b.ToString();
        }
    }
    public static class ReportRedactor
    {
        public static string Redact(string text, string userHome)
        {
            if (!string.IsNullOrEmpty(userHome)) text = Regex.Replace(text, Regex.Escape(userHome), "<USER>", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"\b7656119\d{10}\b", "<STEAM_ID>");
            text = Regex.Replace(text, @"(?i)\b(api[_-]?key|access[_-]?token|authorization|password|secret)\b(\s*[:=]\s*)[^\s,;]+", "$1$2<REDACTED>");
            return text;
        }
    }
}
