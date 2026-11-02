using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;
using ProbablyModManager;

namespace ProbablyModManager.Launcher;

internal static class Program
{
 private static readonly JsonSerializerOptions JsonOptions=new(){IncludeFields=true};
 private static readonly string GameFile=Path.Combine(AppContext.BaseDirectory,"Probably Stolen.exe");
 private static readonly string DataRoot=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"AppData","LocalLow","Questing Goose Studio","Probably Stolen","ModData","probably_mod_manager");
 private static string activeData=DataRoot;
 [STAThread]
 private static async Task Main(string[] args)
 {
  if(args.Contains("--test-child")){await Task.Delay(1200);return;}
  if(args.Contains("--publisher-self-test")){await PublishingEngine.SelfTest(args.Last());return;}
  if(args.Contains("--self-test")){await SelfTest(args.Last());return;}
  using var single=new Mutex(true,"Local\\ProbablyModManager.ManualRestart",out bool owns);if(!owns)return;
  try
  {
   if(!File.Exists(GameFile))throw new FileNotFoundException("Place Probably Mod Manager.exe next to Probably Stolen.exe.");
   Directory.CreateDirectory(DataRoot);
   using var publisher=new PublishingEngine(AppContext.BaseDirectory,DataRoot);
   var game=FindGame();if(game==null&&!args.Contains("--serve"))LaunchSteam();
   var state=new RestartHelperState{Token=Guid.NewGuid().ToString("N"),Phase="waiting",PublishingAvailable=true,PublisherVersion=3};var handled=new HashSet<string>();
   var startup=DateTime.UtcNow;
   while(true)
   {
    game?.Dispose();game=FindGame();
    if(game==null){state.Phase="waiting";Heartbeat(state);publisher.Poll(state.Token,state.SessionId);if((DateTime.UtcNow-startup).TotalSeconds>90&&!publisher.IsBusy)break;await Task.Delay(500);continue;}
    var session=Read<RestartSession>("restart-session.json");state.SessionId=session?.SessionId??"";state.Phase="ready";state.RequestId="";state.Message="";Heartbeat(state);publisher.Poll(state.Token,state.SessionId);
    var request=Read<ManualRestartRequest>("restart-request.json");
    if(!publisher.IsBusy&&RestartProtocol.ValidRequest(request!,state,DateTime.UtcNow)&&handled.Add(request!.RequestId))
    {
     bool exited=await WaitForExplicitExit(game,request,state);
     if(exited){await Task.Delay(2000);LaunchSteam();startup=DateTime.UtcNow;continue;}
    }
    if(game.HasExited&&!FindAnyGame()&&!publisher.IsBusy)break;
    await Task.Delay(400);
   }
  }
  catch(Exception ex)
  {
   try{Directory.CreateDirectory(DataRoot);File.WriteAllText(Path.Combine(DataRoot,"restart-helper-error.txt"),DateTime.UtcNow.ToString("o")+"\n"+ex);}catch{}
   if(!args.Contains("--serve"))MessageBox.Show(ex.Message,"Probably Mod Manager",MessageBoxButtons.OK,MessageBoxIcon.Warning);
  }
 }
 private static Process? FindGame()
 {
  var matches=new List<Process>();foreach(var process in Process.GetProcessesByName("Probably Stolen"))
  {try{if(!process.HasExited&&string.Equals(Path.GetFullPath(process.MainModule!.FileName),Path.GetFullPath(GameFile),StringComparison.OrdinalIgnoreCase))matches.Add(process);else process.Dispose();}catch{process.Dispose();}}
  if(matches.Count==1)return matches[0];foreach(var p in matches)p.Dispose();return null;
 }
 private static bool FindAnyGame(){using var game=FindGame();return game!=null;}
 private static T? Read<T>(string file) where T:class
 {
  try{string path=Path.Combine(activeData,file);if(!File.Exists(path)||new FileInfo(path).Length>8192)return null;return JsonSerializer.Deserialize<T>(File.ReadAllText(path),JsonOptions);}catch{return null;}
 }
 private static void Write<T>(string file,T value)
 {
  Directory.CreateDirectory(activeData);string path=Path.Combine(activeData,file),temp=path+".helper.tmp";File.WriteAllText(temp,JsonSerializer.Serialize(value,JsonOptions));File.Move(temp,path,true);
 }
 private static void Heartbeat(RestartHelperState state){state.HeartbeatUtc=DateTime.UtcNow.ToString("o");Write("restart-helper.json",state);}
 private static async Task<bool> WaitForExplicitExit(Process game,ManualRestartRequest request,RestartHelperState state)
 {
  state.Phase="armed";state.RequestId=request.RequestId;var deadline=DateTime.UtcNow.AddSeconds(45);
  while(DateTime.UtcNow<deadline)
  {
   var current=Read<ManualRestartRequest>("restart-request.json");
   if(current!=null&&(current.Cancelled||current.RequestId!=request.RequestId||current.SessionId!=request.SessionId||current.Token!=request.Token))return false;
   if(game.HasExited)return current!=null&&!current.Cancelled&&current.RequestId==request.RequestId;
   Heartbeat(state);await Task.Delay(200);
  }
  state.Phase="error";state.Message="The game did not exit; no process was terminated and no relaunch was attempted.";Heartbeat(state);return false;
 }
 private static void LaunchSteam()
 {
  var folder=Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam","InstallPath",null) as string;
  string steam=folder==null?"":Path.Combine(folder,"steam.exe");
  if(File.Exists(steam))Process.Start(new ProcessStartInfo(steam,"-applaunch 4348910"){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden});
  else Process.Start(new ProcessStartInfo("steam://rungameid/4348910"){UseShellExecute=true,WindowStyle=ProcessWindowStyle.Hidden});
 }
 private static async Task SelfTest(string root)
 {
  activeData=Path.GetFullPath(root);Directory.CreateDirectory(activeData);
  var state=new RestartHelperState{Token=Guid.NewGuid().ToString("N"),SessionId="test-session",Phase="ready",HeartbeatUtc=DateTime.UtcNow.ToString("o")};
  var request=new ManualRestartRequest{Token=state.Token,SessionId=state.SessionId,RequestId=Guid.NewGuid().ToString("N"),CreatedUtc=DateTime.UtcNow.ToString("o")};
  string self=Environment.ProcessPath!;
  using(var normal=Process.Start(new ProcessStartInfo(self,"--test-child"){UseShellExecute=false,CreateNoWindow=true})!){await normal.WaitForExitAsync();if(RestartProtocol.ValidRequest(null!,state,DateTime.UtcNow))throw new Exception("Normal exit triggered a restart");}
  Write("restart-request.json",request);
  using(var child=Process.Start(new ProcessStartInfo(self,"--test-child"){UseShellExecute=false,CreateNoWindow=true})!){if(!await WaitForExplicitExit(child,request,state))throw new Exception("Explicit request did not wait for real process exit");}
  Write("restart-request.json",request);using(var child=Process.Start(new ProcessStartInfo(self,"--test-child"){UseShellExecute=false,CreateNoWindow=true})!){request.Cancelled=true;Write("restart-request.json",request);if(await WaitForExplicitExit(child,request,state))throw new Exception("Cancelled request triggered relaunch");await child.WaitForExitAsync();}
  File.WriteAllText(Path.Combine(activeData,"self-test-result.txt"),"PASS: ordinary exit has no restart request; explicit request waits for the exact child process to exit; cancelled request never relaunches. Steam was not launched during these tests.");
 }
}
