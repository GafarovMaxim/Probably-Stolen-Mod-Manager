using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using UnityEngine;

namespace ProbablyModManager
{
    internal sealed class Journal : IDisposable
    {
        private readonly ConcurrentQueue<LogMessage> queue = new ConcurrentQueue<LogMessage>();
        private readonly Dictionary<string, LogMessage> grouped = new Dictionary<string, LogMessage>();
        private int queued, dropped, part;
        private StreamWriter writer;
        private readonly List<string> sessionFiles = new List<string>();
        private readonly string session = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
        public readonly List<LogMessage> Rows = new List<LogMessage>();
        public string Error = "", Bootstrap = "";
        public string Session => session;
        public long Total;
        public void Attach() { Application.logMessageReceivedThreaded += Receive; }
        public void Dispose() { Application.logMessageReceivedThreaded -= Receive; FlushAll(); writer?.Dispose(); writer = null; }
        public void FlushAll() { while (!queue.IsEmpty) Drain(); writer?.Flush(); }
        private void Receive(string message, string stack, LogType type)
        {
            if (Interlocked.Increment(ref queued) > 12000) { Interlocked.Decrement(ref queued); Interlocked.Increment(ref dropped); return; }
            queue.Enqueue(new LogMessage { Utc = DateTime.UtcNow.ToString("o"), Message = Limit(message, 24000), Stack = Limit(stack, 48000), Level = type == LogType.Warning ? "warning" : type == LogType.Error || type == LogType.Exception || type == LogType.Assert ? "error" : "info" });
        }
        public void Add(string level, string message, string stack = "", string source = "Probably Mod Manager")
        {
            Interlocked.Increment(ref queued);
            queue.Enqueue(new LogMessage { Utc = DateTime.UtcNow.ToString("o"), Level = level, Message = message, Stack = stack, Source = source });
        }
        private static string Limit(string s, int size) => s == null ? "" : s.Length > size ? s.Substring(0, size) + "\n[truncated]" : s;
        public void ImportBootstrap(string path, string marker)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
                using (var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    if (file.Length > 1024 * 1024) file.Seek(-1024 * 1024, SeekOrigin.End);
                    using (var reader = new StreamReader(file))
                    {
                        var all = reader.ReadToEnd(); var index = all.IndexOf(marker, StringComparison.Ordinal);
                        if (index >= 0) Bootstrap = all.Substring(0, index);
                    }
                }
            }
            catch (Exception ex) { Error = "Startup log: " + ex.Message; }
        }
        public void Drain(IReadOnlyList<ModInfo> mods = null)
        {
            int lost = Interlocked.Exchange(ref dropped, 0);
            if (lost > 0) Add("warning", "Log queue overflow: " + lost + " messages were dropped.");
            var changed = false;
            for (int i = 0; i < 350 && queue.TryDequeue(out var item); i++)
            {
                Interlocked.Decrement(ref queued); Total++; changed = true;
                if (string.IsNullOrEmpty(item.Source) && mods != null)
                {
                    var diagnosis = DiagnosticAnalyzer.Analyze(item, mods);
                    item.Source = diagnosis.CodeOwner.Length > 0 ? (mods.FirstOrDefault(m=>m.Id==diagnosis.CodeOwner)?.Name ?? diagnosis.CodeOwner) + " [" + diagnosis.CodeOwner + "; first stack frame code owner; root cause unconfirmed]" : diagnosis.StackMods.Count > 0 ? string.Join(", ",diagnosis.StackMods.Select(id=>(mods.FirstOrDefault(m=>m.Id==id)?.Name??id)+" ["+id+"]")) + " [code in stack; root cause unconfirmed]" : "Unattributed";
                }
                var key = item.Level + "\n" + item.Message + "\n" + item.Stack;
                if (grouped.TryGetValue(key, out var previous)) { previous.Count++; previous.LastUtc = item.Utc; }
                else
                {
                    item.LastUtc = item.Utc; grouped[key] = item; Rows.Add(item);
                    if (Rows.Count > 2500) { var old = Rows[0]; grouped.Remove(old.Level + "\n" + old.Message + "\n" + old.Stack); Rows.RemoveAt(0); }
                }
                try
                {
                    if (writer == null || writer.BaseStream.Position > 8 * 1024 * 1024)
                    {
                        writer?.Dispose(); System.IO.Directory.CreateDirectory(Path.Combine(Storage.Root, "sessions"));
                        var path = Path.Combine(Storage.Root, "sessions", "session-" + session + "-" + (part++).ToString("000") + ".jsonl");
                        sessionFiles.Add(path); writer = new StreamWriter(new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read), new UTF8Encoding(false));
                        Prune();
                    }
                    writer.WriteLine(JsonConvert.SerializeObject(item));
                }
                catch (Exception ex) { Error = ex.Message; writer?.Dispose(); writer = null; }
            }
            if (changed) { try { writer?.Flush(); } catch (Exception ex) { Error = ex.Message; } }
        }
        private void Prune()
        {
            var files = System.IO.Directory.GetFiles(Path.Combine(Storage.Root, "sessions"), "session-*.jsonl").OrderByDescending(File.GetLastWriteTimeUtc).ToList();
            foreach (var path in files.Skip(24)) if (!sessionFiles.Contains(path)) File.Delete(path);
        }
        public string MessageReport(LogMessage row, string environment, string home)
        {
            var b = new StringBuilder(environment).AppendLine().AppendLine("SELECTED EVENT").AppendLine(row.Utc + " .. " + row.LastUtc + " | " + row.Level + " | repeats=" + row.Count).AppendLine(row.Source).AppendLine(row.Message).AppendLine(row.Stack).AppendLine().AppendLine("PRECEDING CONTEXT (captured events, grouped)");
            foreach (var item in Rows.Take(Rows.IndexOf(row)).Reverse().Take(12).Reverse()) b.AppendLine(item.Utc + " [" + item.Level + "] " + item.Message);
            return ReportRedactor.Redact(b.ToString(), home);
        }
        public string Export(string environment, IEnumerable<HistoryItem> history, string home)
        {
            FlushAll();
            var target = Path.Combine(Storage.Root, "exports"); System.IO.Directory.CreateDirectory(target);
            var filename = Path.Combine(target, "report-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 6) + ".zip");
            using (var zip = ZipFile.Open(filename, ZipArchiveMode.Create))
            {
                void Entry(string name, string text) { using (var stream = new StreamWriter(zip.CreateEntry(name).Open(), new UTF8Encoding(false))) stream.Write(ReportRedactor.Redact(text, home)); }
                Entry("environment.txt", environment);
                Entry("history.json", JsonConvert.SerializeObject(history, Formatting.Indented));
                Entry("startup-log.txt", Bootstrap);
                foreach (var path in sessionFiles.Where(File.Exists))
                {
                    using (var read = new StreamReader(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)))
                    using (var write = new StreamWriter(zip.CreateEntry(Path.GetFileName(path)).Open(), new UTF8Encoding(false)))
                    {
                        string line;
                        while ((line = read.ReadLine()) != null)
                        {
                            try
                            {
                                var item = JsonConvert.DeserializeObject<LogMessage>(line);
                                if (item == null) continue;
                                item.Message = ReportRedactor.Redact(item.Message, home); item.Stack = ReportRedactor.Redact(item.Stack, home); item.Source = ReportRedactor.Redact(item.Source, home);
                                write.WriteLine(JsonConvert.SerializeObject(item));
                            }
                            catch { write.WriteLine(JsonConvert.SerializeObject(new { Level = "warning", Message = "Unreadable log line omitted from export" })); }
                        }
                    }
                }
                Entry("readable-log.txt", string.Join("\n\n", Rows.Select(x => x.Utc + " [" + x.Level + "] ×" + x.Count + " " + x.Message + "\n" + x.Stack)));
            }
            return filename;
        }
        public List<LogMessage> Previous()
        {
            var path = System.IO.Directory.Exists(Path.Combine(Storage.Root, "sessions")) ? System.IO.Directory.GetFiles(Path.Combine(Storage.Root, "sessions"), "session-*.jsonl").Where(p => !sessionFiles.Contains(p)).OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault() : null;
            var result = new List<LogMessage>(); if (path == null) return result;
            foreach (var line in File.ReadLines(path)) { try { var row = JsonConvert.DeserializeObject<LogMessage>(line); if (row != null) result.Add(row); if (result.Count > 2500) result.RemoveAt(0); } catch { } }
            return result;
        }
    }
}
