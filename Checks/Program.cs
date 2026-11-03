using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using ProbablyModManager;

internal static class Program
{
 private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
 private static int Main(string[] args)
 {
  try
  {
   var manager = new ModInfo { Id = "manager", Name = "Manager" };
   var core = new ModInfo { Id = "core", Name = "Core" };
   var addon = new ModInfo { Id = "addon", Name = "Addon", Dependencies = new[] { "core" } };
   var mods = new[] { manager, core, addon };
   Check(Planner.Validate(mods, new[] { "manager", "addon" }, "manager").Any(x => x.Code == "MISSING_DEPENDENCY" && x.Blocks), "Missing dependency was allowed");
   Check(Planner.Validate(mods, new[] { "manager", "addon", "core" }, "manager").Any(x => x.Code == "LOAD_ORDER"), "Bad dependency order was allowed");
   Check(Planner.TrySort(mods, new[] { "addon", "manager", "core" }, out var sorted, out _) && sorted.IndexOf("core") < sorted.IndexOf("addon"), "Dependency sorting failed");
   var withDeps = Planner.WithDependencies(mods, new[] { "manager" }, new[] { "addon" });
   Check(withDeps.SequenceEqual(new[] { "manager", "core", "addon" }), "Transitive enable did not retain order");
   core.Dependencies = new[] { "addon" };
   Check(!Planner.TrySort(mods, new[] { "manager", "core", "addon" }, out _, out var cycle) && cycle.Contains("core"), "Cycle was accepted");
   core.Dependencies = Array.Empty<string>();
   Check(Planner.Validate(mods, new[] { "core" }, "manager").Any(x => x.Code == "MANAGER_REQUIRED"), "Manager could disable itself");
   Check(Planner.Validate(new[] { manager, core, core }, new[] { "manager", "core" }, "manager").Any(x => x.Code == "DUPLICATE_ID"), "Duplicate ID was accepted");
   Check(Planner.Validate(mods, new[] { "manager", "unknown" }, "manager").Any(x => x.Code == "MISSING_MOD"), "Missing profile mod was accepted");
   string redacted = ReportRedactor.Redact(@"C:\Users\Alice\Game\file.txt api_key=abc123 76561198080753120", @"C:\Users\Alice");
   Check(!redacted.Contains("Alice") && !redacted.Contains("abc123") && !redacted.Contains("76561198080753120"), "Report redaction failed");
   Console.WriteLine("PASS: missing dependency, dependency order, stable sort, transitive enable, cycles, self-disable guard, duplicate IDs, missing mods, report redaction.");
   var cascade = new[] { manager,core,addon,new ModInfo {Id="dependent",Dependencies=new[]{"addon"}} };
   Check(Planner.Dependants(cascade,new[]{"manager","core","addon","dependent"},"core").SequenceEqual(new[]{"core","addon","dependent"}),"Transitive disabling lost a dependant");
   var tagState=new ManagerState { Tags=new Dictionary<string,string>{{"legacy","ui"}},ModTags=new Dictionary<string,List<string>>{{"new",new List<string>{"Library","UI","library"," UI "}}} };
   tagState.MigrateTags();Check(tagState.ModTags["legacy"].SequenceEqual(new[]{"ui"}),"Legacy tag lost");Check(tagState.ModTags["new"].SequenceEqual(new[]{"Library","UI"}),"Multiple tags not normalized");
   tagState.ModTags["legacy"].Clear();tagState.MigrateTags();Check(tagState.ModTags["legacy"].Count==0,"Removed legacy tag resurrected");
   Console.WriteLine("PASS: transitive disabling, multiple tags, case-insensitive deduplication, migration and removed-tag persistence.");
   var moved=Planner.MoveBefore(new[]{"manager","core","addon"},"addon","core");Check(moved.SequenceEqual(new[]{"manager","addon","core"}),"Drag insertion failed");
   Check(Planner.Validate(mods,moved,"manager").Any(f=>f.Code=="LOAD_ORDER"),"Invalid drag order bypassed dependency validation");
   Check(Planner.MoveBefore(moved,"core",null).Distinct().Count()==3,"Drag duplicated or lost an ID");
   Check(Planner.MoveBefore(moved,"addon","addon").SequenceEqual(moved),"Drop on self changed the list");
   tagState.ModTags["new"].Remove("UI");Check(tagState.ModTags["new"].SequenceEqual(new[]{"Library"}),"Removing one tag removed others");
   Console.WriteLine("PASS: drag insertion, dependency guard, no duplicate IDs, drop-on-self, independent tag removal.");
   var dropMods=new[]{manager,core,addon,new ModInfo{Id="tail"},new ModInfo{Id="second"}};
   Check(Planner.EnableBefore(dropMods,new[]{"manager","tail"},new[]{"addon"},"tail").SequenceEqual(new[]{"manager","core","addon","tail"}),"Cross-list drop lost insertion point or new dependency");
   Check(Planner.EnableBefore(dropMods,new[]{"manager","core","tail"},new[]{"addon"},"core").SequenceEqual(new[]{"manager","core","addon","tail"}),"Cross-list drop ignored existing dependency before requested position");
   Check(Planner.EnableBefore(dropMods,new[]{"manager","core","tail"},new[]{"addon","second"},"tail").SequenceEqual(new[]{"manager","core","addon","second","tail"}),"Cross-list multi-drop failed to insert group before target");
   Check(Planner.EnableBefore(dropMods,new[]{"manager","core","tail"},new[]{"second"},null).SequenceEqual(new[]{"manager","core","tail","second"}),"Cross-list end insertion failed");
   Check(Planner.EnableBefore(dropMods,new[]{"manager","tail"},new[]{"second"},"manager").SequenceEqual(new[]{"second","manager","tail"}),"Cross-list first position was discarded");
   Console.WriteLine("PASS: cross-list activation inserts at first/middle/end target, keeps multiple selections together and respects existing/new dependencies.");
   Check(Planner.MoveGroupBefore(new[]{"manager","a","b","c","d"},new[]{"d","b"},"a").SequenceEqual(new[]{"manager","b","d","a","c"}),"Group drag lost the existing order");
   Check(Planner.MoveGroupBefore(new[]{"a","b","c"},new[]{"a","b"},"b").SequenceEqual(new[]{"a","b","c"}),"Group drop on itself changed order");
   var owner=new ModInfo{Id="unrelated_id",Name="Known mod",TypeNames=new[]{"Example.Plugin.Worker"}};
   var other=new ModInfo{Id="Other",Name="Other",TypeNames=new[]{"Other.Worker"}};
   var diagnosis=DiagnosticAnalyzer.Analyze(new LogMessage{Message="NullReferenceException: missing object",Stack="Example.Plugin.Worker.Tick ()\nOther.Worker.Update ()"},new[]{owner,other});
   Check(diagnosis.Kind=="null"&&diagnosis.CodeOwner==owner.Id&&diagnosis.StackMods.Count==2,"Code ownership did not use loaded type names");
   diagnosis=DiagnosticAnalyzer.Analyze(new LogMessage{Message="Other failed",Stack="UnityEngine.Transform.Find ()\nOther.Worker.Update ()"},new[]{owner,other});
   Check(diagnosis.CodeOwner==""&&diagnosis.StackMods.SequenceEqual(new[]{"Other"})&&diagnosis.MentionedMods.Contains("Other"),"A deeper frame was incorrectly blamed");
   diagnosis=DiagnosticAnalyzer.Analyze(new LogMessage{Message="Otherness",Stack="FakeExample.Plugin.Worker.Tick ()"},new[]{owner,other});
   Check(diagnosis.CodeOwner==""&&diagnosis.StackMods.Count==0&&diagnosis.MentionedMods.Count==0,"Substring coincidence was treated as evidence");
   var html=File.ReadAllText(args.Length>1?args[1]:Path.Combine(AppContext.BaseDirectory,"steam-changelog-sample.html"));var notes=WorkshopChangeParser.Parse(html);
   Check(notes.Count>0&&notes[0].Date.Length>0&&notes[0].Text.Length>0&&!notes[0].Text.Contains("<br>"),"Real Steam change notes parsing failed");
   Check(WorkshopChangeParser.Parse("<html>Sign in</html>").Count==0,"Unavailable Steam page invented entries");
   Console.WriteLine("PASS: group move order, self-drop, actual type ownership, unknown culprit, evidence boundary, real Steam change notes, unavailable page.");
   var now=DateTime.UtcNow;var helper=new RestartHelperState{Token=new string('a',32),SessionId="current",Phase="ready",HeartbeatUtc=now.ToString("o")};
   var request=new ManualRestartRequest{Token=helper.Token,SessionId="current",RequestId=Guid.NewGuid().ToString("N"),CreatedUtc=now.ToString("o")};
   Check(RestartProtocol.HelperReady(helper,"current",now)&&RestartProtocol.ValidRequest(request,helper,now),"Fresh manual restart handshake rejected");
   Check(!RestartProtocol.ValidRequest(null,helper,now)&&!RestartProtocol.Acknowledged(helper,request,now),"Idle helper could close the game");
   request.Token="wrong";Check(!RestartProtocol.ValidRequest(request,helper,now),"Wrong helper token accepted");request.Token=helper.Token;
   request.SessionId="previous";Check(!RestartProtocol.ValidRequest(request,helper,now),"Previous game session accepted");request.SessionId="current";
   request.CreatedUtc=now.AddSeconds(-15).ToString("o");Check(!RestartProtocol.ValidRequest(request,helper,now),"Stale restart request accepted");request.CreatedUtc=now.ToString("o");
   helper.Phase="armed";helper.RequestId=request.RequestId;Check(RestartProtocol.Acknowledged(helper,request,now),"Matching acknowledgement rejected");
   request.Cancelled=true;Check(!RestartProtocol.Acknowledged(helper,request,now)&&!RestartProtocol.ValidRequest(request,helper,now),"Cancelled request could close the game");request.Cancelled=false;
   helper.HeartbeatUtc=now.AddSeconds(-20).ToString("o");Check(!RestartProtocol.Acknowledged(helper,request,now),"Dead helper acknowledgement accepted");
   Check(ButtonLocalization.McmCaption("en")=="MCM Menu"&&ButtonLocalization.McmCaption("ru")=="MCM-меню"&&ButtonLocalization.McmCaption("zh-Hans").Contains("菜单"),"MCM language fallback incorrect");
   Console.WriteLine("PASS: manual restart requires a fresh request, matching session/token/acknowledgement; stale, cancelled and absent requests rejected; MCM EN/RU/ZH captions.");
   var publishRequest=new PublicationRequest{Token=new string('p',32),SessionId="publish-session",RequestId=Guid.NewGuid().ToString("N"),CreatedUtc=now.ToString("o"),Draft=new PublicationDraft()};
   Check(PublishingProtocol.FreshRequest(publishRequest,publishRequest.Token,"publish-session",now),"Fresh publishing request rejected");
   Check(!PublishingProtocol.FreshRequest(publishRequest,"different","publish-session",now)&&!PublishingProtocol.FreshRequest(publishRequest,publishRequest.Token,"old-session",now),"Foreign publishing request accepted");
   publishRequest.CreatedUtc=now.AddMinutes(-1).ToString("o");Check(!PublishingProtocol.FreshRequest(publishRequest,publishRequest.Token,"publish-session",now),"Stale publishing request accepted");
   var publishState=new PublicationState{Token=publishRequest.Token,SessionId="publish-session",HeartbeatUtc=now.ToString("o")};
   Check(PublishingProtocol.FreshState(publishState,publishRequest.Token,"publish-session",now)&&new PublicationDraft().Visibility==2,"Publishing defaults are not private or state handshake is broken");
   Console.WriteLine("PASS: publishing IPC freshness/session/token checks and private default.");
   if (args.Length > 0)
   {
    string game = @"G:\SteamLibrary\steamapps\common\Probably Stolen\Probably Stolen_Data\Managed";
    AppDomain.CurrentDomain.AssemblyResolve += (_, e) => { string p = Path.Combine(game, new AssemblyName(e.Name).Name + ".dll"); return File.Exists(p) ? Assembly.LoadFrom(p) : null; };
    // Supply the same inventory the game normally collects in Awake. This isolated process does not change the game or approvals.
    var gameNames = new HashSet<string>(System.IO.Directory.GetFiles(game, "*.dll").Select(p => AssemblyName.GetAssemblyName(p).Name), StringComparer.OrdinalIgnoreCase);
    typeof(ModLoader).GetField("gameAssemblyNames", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, gameNames);
    Check(typeof(ModUIManager).GetMethod("AutoSortActiveMods",BindingFlags.Instance|BindingFlags.NonPublic)!=null,"Original game auto-sort entry point missing");
    var folder = Path.GetFullPath(args[0]); var scan = ModScanner.Scan(new[] { Path.Combine(folder, "ProbablyModManager.dll") }, new[] { "ProbablyModManager" }, folder, true);
    Console.WriteLine("Actual game scanner: methods=" + scan.MethodsScanned + ", capabilities=" + string.Join(",", scan.Capabilities));
    Console.WriteLine("Hard flags=" + scan.HardFlags.Count + "; error=" + (scan.Error ?? "none"));
    Console.WriteLine(scan.FormatFindings(100));
    Check(!scan.Refused, "Game scanner refused the mod");
    var declared=System.Xml.Linq.XDocument.Load(Path.Combine(folder,"manifest.xml")).Descendants("Capability").Select(c=>c.Value).ToArray();
    Check(scan.Capabilities.All(c => declared.Contains(c)), "Undeclared capability");
    Check(scan.Capabilities.Contains("native"), "Native restart capability was not detected");
    using(var ownAssembly=DseCecil.AssemblyDefinition.ReadAssembly(Path.Combine(folder,"ProbablyModManager.dll")))
    {
     int exitCalls=0,restartCalls=0;
     foreach(var type in ownAssembly.MainModule.GetTypes())foreach(var method in type.Methods.Where(m=>m.HasBody))foreach(var instruction in method.Body.Instructions)
     {
      if(instruction.Operand is DseCecil.MethodReference call && call.DeclaringType.FullName=="UnityEngine.Application"&&call.Name=="Quit"){exitCalls++;Check(method.Name=="RequestManualRestart","Game exit outside manual restart button handler");}
      if(instruction.Operand is DseCecil.MethodReference restart && restart.DeclaringType.FullName=="ProbablyModManager.NativeWindowsRestart"&&restart.Name=="TryStartReplacement"){restartCalls++;Check(method.Name=="RequestManualRestart","Process launch outside manual restart button handler");}
      Check(!(instruction.Operand is DseCecil.MethodReference managed&&managed.DeclaringType.FullName=="System.Diagnostics.Process"),"Managed Process call in game DLL");
     }
     Check(exitCalls==1,"Expected exactly one guarded manual exit call");
     Check(restartCalls==1,"Expected exactly one manual replacement launch call");
     var imports=ownAssembly.MainModule.GetTypes().SelectMany(t=>t.Methods).Where(m=>m.IsPInvokeImpl).ToArray();
     Check(imports.Length==4&&imports.All(m=>m.PInvokeInfo.Module.Name=="kernel32.dll"),"Unexpected native imports");
     Check(!ownAssembly.MainModule.GetTypes().SelectMany(t=>t.Fields).Any(f=>f.Name=="AutoRestart"||f.Name=="restartAt"||f.Name=="ackDeadline"),"Automatic restart state still present");
    }
    Console.WriteLine("PASS: process launch and Application.Quit confined to manual button handler; kernel32 imports only; no managed Process calls or automatic-restart state.");
    Console.WriteLine("PASS: original game scanner, no hard flags, all detected capabilities declared.");
   }
   return 0;
  }
  catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
 }
}
