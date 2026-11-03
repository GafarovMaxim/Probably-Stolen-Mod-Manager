using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace ProbablyModManager
{
    public sealed class EntryPoint : IMod
    {
        public void Init(ModManifest manifest)
        {
            var go = new GameObject("ProbablyModManager"); UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<Manager>();
        }
        public void OnEnable() { }
        public void OnDisable() { if (Manager.Instance != null) UnityEngine.Object.Destroy(Manager.Instance.gameObject); }
    }
    [HarmonyPatch(typeof(ModUIManager), nameof(ModUIManager.OpenPanel))]
    internal static class MenuPatch
    {
        private static void Postfix(ModUIManager __instance)
        {
            if (Manager.Instance != null && !Manager.NativeBypass) Manager.Instance.RequestOpen(__instance);
        }
    }
    public sealed partial class Manager : MonoBehaviour
    {
        public const string Id = "probably_mod_manager";
        public static Manager Instance;
        public static bool NativeBypass;
        private readonly Journal journal = new Journal();
        private ManagerState data;
        private readonly List<ModInfo> mods = new List<ModInfo>();
        private List<string> staged = new List<string>(), observed = new List<string>(), startup = new List<string>();
        private readonly Stack<List<string>> undo = new Stack<List<string>>();
        private readonly HashSet<string> selectedRows = new HashSet<string>();
        private string selectedId = "", toast = "", report = "", reportTitle = "", draftNote = "", profileName = "", importJson = "";
        private bool initialized, visible, dual = true, previousSession, showSettings, applying, reviewOnly, nativePending;
        private int tab, profileIndex, modFilter, levelFilter;
        private string searchOff = "", searchOn = "", searchAll = "", logSearch = "";
        private List<LogMessage> archived = new List<LogMessage>();
        private List<Finding> findings = new List<Finding>();
        private List<KeyValuePair<LoadedMod, ModEnableCheck>> checks;
        private List<string> applyTarget;
        private ModUIManager waitingNative;
        private float nextPoll, nextFlush;
        private string sessionId = Guid.NewGuid().ToString("N");
        private GameObject blocker;
        private string home;
        // PlayerStore.Instance lazily constructs gameplay state even from the main menu. Never read it as a scene probe.
        private bool InMenu => MainMenuUIController.Instance != null && MainMenuUIController.Instance.isActiveAndEnabled;
        private bool Russian => data == null || data.Russian;
        private string T(string ru, string en) => Russian ? ru : en;
        private List<string> EnabledOrder() => ModLoader.Mods.Where(m => ModLoader.IsModEnabled(m.Manifest.ID)).Select(m => m.Manifest.ID).Distinct().ToList();
        private void Awake()
        {
            if (Instance != null) { Destroy(gameObject); return; } Instance = this;
            Storage.Initialize(ModHelper.GetModDataFolder(Id)); home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            data = Storage.Read<ManagerState>("manager.json") ?? new ManagerState();
            data.Profiles = data.Profiles ?? new List<Profile>(); data.Notes = data.Notes ?? new Dictionary<string, string>(); data.Tags = data.Tags ?? new Dictionary<string, string>(); data.History = data.History ?? new List<HistoryItem>(); data.LastVersions = data.LastVersions ?? new Dictionary<string, string>(); data.LastHashes = data.LastHashes ?? new Dictionary<string, string>();
            data.MigrateTags();
            journal.Attach(); Debug.Log("[ProbablyModManager] PMM session " + sessionId);
        }
        private void Start()
        {
            try
            {
                InitializeRepairs(); InitializeRestartSession(); InitializePublishing(); Refresh(); startup = EnabledOrder(); observed = startup.ToList(); staged = startup.ToList(); if (!staged.Contains(Id)) staged.Insert(0, Id);
                data.Profiles = data.Profiles.Where(p => p != null && p.Order != null && p.Versions != null && !string.IsNullOrWhiteSpace(p.Name)).ToList();
                if (!data.InitialProfileCreated && data.Profiles.Count == 0) data.Profiles.Add(new Profile { Name = T("Основной", "Main"), GameVersion = PlayerStore.VERSION, Order = staged.ToList(), Versions = mods.GroupBy(m => m.Id).ToDictionary(g => g.Key, g => g.First().Version) });
                data.InitialProfileCreated = true; CompareSnapshot(); Save(); initialized = true;
            }
            catch (Exception e) { journal.Add("error", "Manager startup failed", e.ToString()); }
            journal.ImportBootstrap(Application.consoleLogPath, "[ProbablyModManager] PMM session " + sessionId);
        }
        private void Refresh()
        {
            mods.Clear();
            foreach (var m in ModLoader.Mods.Where(m => m.Manifest != null))
                mods.Add(new ModInfo { Id = m.Manifest.ID, Name = string.IsNullOrWhiteSpace(m.Manifest.Name) ? m.Manifest.ID : m.Manifest.Name, Version = m.Manifest.ModVersion, Author = m.Manifest.Author, Description = m.Manifest.Description, Dependencies = m.Manifest.Prerequisites.ToArray(), GameVersions = m.Manifest.GameVersions.ToArray(), Capabilities = m.Manifest.Capabilities.ToArray(), Source = m.Source.ToString(), Folder = m.FolderPath, Loaded = m.IsEnabled, LoadError = m.LoadError ?? "", Trust = m.TrustNote ?? m.TrustState.ToString(), Hash = m.Fingerprint?.Combined ?? "" });
            foreach(var m in mods){var loaded=ModLoader.Mods.FirstOrDefault(x=>x.Manifest.ID==m.Id);try{m.TypeNames=loaded?.Assembly?.GetTypes().Select(t=>t.FullName).Where(n=>n!=null).ToArray()??Array.Empty<string>();}catch(ReflectionTypeLoadException ex){m.TypeNames=ex.Types.Where(t=>t!=null).Select(t=>t.FullName).ToArray();}catch{m.TypeNames=Array.Empty<string>();}}
            if (!mods.Any(m => m.Id == selectedId)) Select(mods.FirstOrDefault()?.Id ?? "");
        }
        private void CompareSnapshot()
        {
            foreach (var m in mods)
            {
                if (data.LastVersions.TryGetValue(m.Id, out var version) && version != m.Version) data.HistoryAdd(T("Обновлён мод: ", "Mod updated: ") + m.Name, version + " → " + m.Version);
                else if (data.LastVersions.Count > 0 && !data.LastVersions.ContainsKey(m.Id)) data.HistoryAdd(T("Добавлен мод: ", "Mod added: ") + m.Name, m.Version);
                else if (data.LastHashes.TryGetValue(m.Id, out var hash) && hash.Length > 0 && m.Hash.Length > 0 && hash != m.Hash) data.HistoryAdd(T("Изменились файлы: ", "Files changed: ") + m.Name, m.Version);
            }
            foreach (var removed in data.LastVersions.Keys.Except(mods.Select(m => m.Id)).ToList()) data.HistoryAdd(T("Удалён мод: ", "Mod removed: ") + removed);
            data.LastVersions = mods.GroupBy(m => m.Id).ToDictionary(g => g.Key, g => g.First().Version); data.LastHashes = mods.GroupBy(m => m.Id).ToDictionary(g => g.Key, g => g.First().Hash);
        }
        private void Save() { try { Storage.Write("manager.json", data); } catch (Exception e) { toast = "Save failed: " + e.Message; journal.Add("error", toast); } }
        public void RequestOpen(ModUIManager native)
        {
            waitingNative = native;
            if (!ModWarningUI.IsOpen) TakeOver();
        }
        private void TakeOver()
        {
            if (waitingNative != null) { waitingNative.panel.SetActive(false); waitingNative = null; }
            Open();
        }
        private void Open()
        {
            if (!initialized) return; TickRepairs(); TickPublishing();
            Refresh(); SetVisible(true); findings = Planner.Validate(mods, staged, Id);
        }
        private void SetVisible(bool value)
        {
            visible = value;
            if (value) { NativeShow(); NativeInvalidate(); } else NativeHide();
        }
        private void OpenNative()
        {
            if (ModUIManager.Instance == null) return;
            SetVisible(false); NativeBypass = true;
            try { ModUIManager.Instance.OpenPanel(); } finally { NativeBypass = false; }
        }
        private void Select(string id) { selectedId = id; draftNote = data != null && data.Notes.TryGetValue(id, out var note) ? note : ""; detailScroll = Vector2.zero; }
        private void Changed(Action action)
        {
            undo.Push(staged.ToList()); if (undo.Count > 30) undo.Clear(); action(); findings = Planner.Validate(mods, staged, Id);
        }
        private void Toggle(string id)
        {
            if (!InMenu || id == Id) return;
            Changed(() => { if (staged.Contains(id)) staged.Remove(id); else staged = Planner.WithDependencies(mods, staged, new[] { id }); });
        }
        private void Update()
        {
            if (!initialized) return; TickRepairs(); TickPublishing();
            if (uiAction != null)
            {
                var action = uiAction; uiAction = null;
                try { action(); } catch (Exception e) { toast = e.Message; journal.Add("error", "UI action failed: " + e.Message, e.ToString()); NativeInvalidate(); }
            }
            if (waitingNative != null && !ModWarningUI.IsOpen) TakeOver();
            if (Input.GetKeyDown(KeyCode.F6)) { if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) OpenNative(); else if (visible) SetVisible(false); else Open(); }
            if (Time.unscaledTime >= nextFlush) { nextFlush = Time.unscaledTime + 1; journal.Drain(mods); }
            if (Time.unscaledTime >= nextPoll)
            {
                nextPoll = Time.unscaledTime + 1;
                var order = EnabledOrder();
                if (!order.SequenceEqual(observed))
                {
                    observed = order; if (!visible) staged = order.ToList();
                    data.HistoryAdd(T("Изменён активный набор", "Active mod list changed"), string.Join(", ", order)); Save();
                    if (!visible && InMenu) nativePending = true;
                }
                if (nativePending && !visible && InMenu && (ModUIManager.Instance == null || !ModUIManager.Instance.panel.activeInHierarchy)) { nativePending = false; ShowRestartWarning(); }
            }
        }
        private void LateUpdate() { if (initialized) NativeTick(); }
        private void PrepareApply(bool review = false)
        {
            if (!InMenu) { toast = T("Изменять набор можно только из главного меню.", "Mod list changes are only available in the main menu."); return; }
            findings = Planner.Validate(mods, staged, Id);
            if (findings.Any(f => f.Blocks)) { reportTitle = T("Проверка набора", "Mod list validation"); report = string.Join("\n\n", findings.Select(f => f.Title + "\n" + f.Detail)); return; }
            applyTarget = staged.ToList(); checks = new List<KeyValuePair<LoadedMod, ModEnableCheck>>(); reviewOnly = review;
            foreach (var id in applyTarget.Where(id => !ModLoader.IsModEnabled(id) || (review && id == selectedId)))
            {
                var mod = ModLoader.Mods.FirstOrDefault(m => m.Manifest.ID == id);
                if (mod == null) { toast = "Mod disappeared: " + id; return; }
                var check = ModLoader.CheckEnable(mod);
                if (check.Refusal != null) { journal.Add("error", check.Refusal, source: mod.Manifest.Name); reportTitle = T("Игра отказала в активации", "Game refused activation"); report = check.Refusal; checks = null; return; }
                checks.Add(new KeyValuePair<LoadedMod, ModEnableCheck>(mod, check));
            }
            applying = true;
        }
        private void CommitApply()
        {
            if (!InMenu || checks == null || applyTarget == null) return;
            try
            {
                // CheckEnable is run again at the confirmation boundary. A changed package must not gain unreviewed capabilities.
                foreach (var pair in checks)
                {
                    var fresh = ModLoader.CheckEnable(pair.Key);
                    if (fresh.Refusal != null || fresh.Fingerprint.Combined != pair.Value.Fingerprint.Combined) throw new InvalidOperationException(fresh.Refusal ?? T("Файлы мода изменились во время подтверждения. Проверь набор снова.", "Mod files changed during review; check the list again."));
                }
                foreach (var id in EnabledOrder().Where(id => !applyTarget.Contains(id)).ToList()) ModLoader.SetModEnabled(id, false);
                foreach (var pair in checks) ModLoader.ApproveAndEnable(pair.Key, pair.Value);
                ModLoader.ReorderMods(applyTarget);
                staged = EnabledOrder(); CompleteProfileActivation(); observed = staged.ToList(); undo.Clear(); applying = false; checks = null;
                data.HistoryAdd(T("Применён набор модов", "Mod list applied"), string.Join(", ", staged)); Save(); Refresh();
                journal.Add("info", "Applied mod selection; manual restart required."); ShowRestartWarning();
            }
            catch (Exception e) { applying = false; checks = null; pendingProfile = null; pendingProfileOrder = null; toast = e.Message; journal.Add("error", "Apply failed: " + e.Message, e.StackTrace); Refresh(); staged = EnabledOrder(); }
        }
        private void ShowRestartWarning()
        {
            toast = T("Набор сохранён. Для загрузки изменений нужен перезапуск.", "Selection saved. Restart is required.");
            SetVisible(true);
        }
        private void OpenSettings()
        {
            try
            {
                var mcm = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "ProbablyMCM");
                var open = mcm?.GetType("ProbablyMCM.UI.ModSettingsWindow")?.GetMethod("Open", BindingFlags.Public | BindingFlags.Static);
                if (open != null) { SetVisible(false); open.Invoke(null, new object[] { selectedId }); return; }
                var mod = ModLoader.Mods.FirstOrDefault(m => m.Manifest.ID == selectedId);
                if (mod != null && ModConfig.Find(selectedId)?.HasEntries == true) { SetVisible(false); ModConfigWindow.Open(mod); }
                else toast = T("Мод не зарегистрировал настройки.", "This mod has no registered settings.");
            }
            catch (Exception e) { journal.Add("error", "Settings panel: " + e.Message, e.ToString()); toast = e.Message; }
        }
        private string EnvironmentReport()
        {
            var b = new StringBuilder("Probably Mod Manager 0.2.1\nGame: " + PlayerStore.VERSION + "\nApplication: " + Application.version + "\nUnity: " + Application.unityVersion + "\nSession: " + sessionId + "\n");
            b.AppendLine("Selected load order: " + string.Join(", ", EnabledOrder()));
            foreach (var m in mods) b.AppendLine(m.Id + " | " + m.Version + " | " + m.Source + " | loaded=" + m.Loaded + " | hash=" + m.Hash + " | dependencies=" + string.Join(",", m.Dependencies) + " | capabilities=" + string.Join(",", m.Capabilities));
            foreach (var n in data.Notes) if (!string.IsNullOrWhiteSpace(n.Value)) b.AppendLine("Player note " + n.Key + ": " + n.Value);
            return ReportRedactor.Redact(b.ToString(), home);
        }
        private void OnDestroy()
        {
            if (Instance != this) return;
            SavePublicationDrafts(); DisposePublicationPreview(); DisposeRepairs(); Save(); journal.Dispose(); Instance = null;
        }
        private void OnApplicationQuit() { SavePublicationDrafts(); Save(); journal.FlushAll(); }
    }
}
