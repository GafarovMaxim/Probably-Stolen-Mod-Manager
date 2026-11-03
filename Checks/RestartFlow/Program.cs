using System;
using System.Collections.Generic;
using System.Linq;
using ProbablyModManager;
using UnityEngine;

internal static class Harness
{
 internal static readonly List<string> Events = new List<string>();
 internal static bool StorageThrows, DraftThrows, FlushThrows, QuitThrows;
 internal static bool NativeSucceeds = true;

 internal static Manager Reset()
 {
  Events.Clear();
  StorageThrows = DraftThrows = FlushThrows = QuitThrows = false;
  NativeSucceeds = true;
  Application.platform = RuntimePlatform.WindowsPlayer;
  return new Manager();
 }

 internal static void Require(bool condition, string message)
 {
  if (!condition) throw new Exception(message + ": " + string.Join(",", Events));
 }

 internal static void NoLaunchOrQuit(string label) =>
  Require(!Events.Contains("start") && !Events.Contains("quit"), label);

 internal static void SuccessfulOrder(string label) =>
  Require(Events.SequenceEqual(new[] { "save", "drafts", "flush", "start", "quit" }), label);
}

internal static class Program
{
 private static void CheckGuard(string label, Action<Manager> block, Action<Manager> unblock)
 {
  var manager = Harness.Reset();
  block(manager);
  manager.ClickRestart();
  Harness.Require(Harness.Events.Count == 0, label + " must have no side effects");
  unblock(manager);
  manager.ClickRestart();
  Harness.SuccessfulOrder(label + " must not latch the restart guard");
 }

 private static void CheckSaveFailure(string label, Action<bool> setFailure)
 {
  var manager = Harness.Reset();
  setFailure(true);
  manager.ClickRestart();
  Harness.NoLaunchOrQuit(label + " must preserve the current process");
  Harness.Require(Harness.Events.Contains("error") && Harness.Events.Contains("invalidate"), label + " must report failure");
  setFailure(false);
  Harness.Events.Clear();
  manager.ClickRestart();
  Harness.SuccessfulOrder(label + " must allow retry");
 }

 private static void Main()
 {
  CheckGuard("Outside main menu", m => m.InMenu = false, m => m.InMenu = true);
  CheckGuard("Applying changes", m => m.applying = true, m => m.applying = false);
  CheckGuard("Publishing busy", m => m.PublishingBusy = true, m => m.PublishingBusy = false);
  CheckGuard("Wrong platform", _ => Application.platform = RuntimePlatform.WindowsEditor, _ => Application.platform = RuntimePlatform.WindowsPlayer);

  CheckSaveFailure("Storage failure", value => Harness.StorageThrows = value);
  CheckSaveFailure("Draft save failure", value => Harness.DraftThrows = value);
  CheckSaveFailure("Journal flush failure", value => Harness.FlushThrows = value);

  var manager = Harness.Reset();
  Harness.NativeSucceeds = false;
  manager.ClickRestart();
  Harness.Require(Harness.Events.SequenceEqual(new[] { "save", "drafts", "flush", "start", "error", "invalidate" }), "Native failure must not quit");
  Harness.Require(manager.LastToast.Contains("simulated native failure"), "Native failure must be explained");
  Harness.NativeSucceeds = true;
  Harness.Events.Clear();
  manager.ClickRestart();
  Harness.SuccessfulOrder("Native failure must allow retry");
  manager.ClickRestart();
  Harness.SuccessfulOrder("Repeated click after success must not start again");

  manager = Harness.Reset();
  Harness.QuitThrows = true;
  manager.ClickRestart();
  Harness.Require(Harness.Events.SequenceEqual(new[] { "save", "drafts", "flush", "start", "quit", "error", "invalidate" }), "Quit exception must follow successful native start");
  Harness.Require(manager.LastToast.Contains("Replacement process started"), "Quit failure must report the existing replacement");
  int eventCount = Harness.Events.Count;
  Harness.QuitThrows = false;
  manager.ClickRestart();
  Harness.Require(Harness.Events.Count == eventCount, "Quit exception must keep duplicate launch blocked");

  Console.WriteLine("PASS: actual restart handler guards; storage/draft/flush and native failure retries; save/flush/start/quit order; repeated-click and throwing-Quit duplicate protection. All collaborators mocked; no game or processes launched.");
 }
}

namespace UnityEngine
{
 internal enum RuntimePlatform { WindowsPlayer, WindowsEditor }
 internal static class Application
 {
  internal static RuntimePlatform platform;
  internal static void Quit()
  {
   Harness.Events.Add("quit");
   if (Harness.QuitThrows) throw new InvalidOperationException("simulated quit failure");
  }
 }
}

namespace ProbablyModManager
{
 public sealed partial class Manager
 {
  internal bool InMenu = true, applying, PublishingBusy;
  private readonly string sessionId = "test-session";
  private readonly object data = new object();
  private readonly FakeJournal journal = new FakeJournal();
  private string toast;
  internal string LastToast => toast;
  internal void ClickRestart() => RequestManualRestart();
  private static string T(string russian, string english) => english;
  private void NativeInvalidate() => Harness.Events.Add("invalidate");
  private void SavePublicationDrafts()
  {
   Harness.Events.Add("drafts");
   if (Harness.DraftThrows) throw new InvalidOperationException("simulated draft failure");
  }
 }

 internal sealed class FakeJournal
 {
  internal void FlushAll()
  {
   Harness.Events.Add("flush");
   if (Harness.FlushThrows) throw new InvalidOperationException("simulated journal failure");
  }
  internal void Add(string level, string message, string detail = null) => Harness.Events.Add("error");
 }

 internal static class Storage
 {
  internal static void Write(string path, object value)
  {
   Harness.Require(path == "manager.json", "Handler must save manager configuration");
   Harness.Events.Add("save");
   if (Harness.StorageThrows) throw new InvalidOperationException("simulated storage failure");
  }
  internal static T Read<T>(string path) => default(T);
 }

 internal static class NativeWindowsRestart
 {
  internal static bool TryStartReplacement(out string error)
  {
   Harness.Events.Add("start");
   error = Harness.NativeSucceeds ? null : "simulated native failure";
   return Harness.NativeSucceeds;
  }
 }

 internal sealed class RestartSession { public string SessionId, StartedUtc; }
 internal sealed class RestartHelperState { }
}
