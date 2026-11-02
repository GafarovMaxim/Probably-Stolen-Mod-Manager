using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ProbablyModManager;

namespace ProbablyModManager.Launcher;

internal sealed partial class PublishingEngine
{
 private static void ValidateSteamAccount(string? account,bool required)
 {
  if(string.IsNullOrEmpty(account))
  {
   if(required)throw new InvalidOperationException("Enter the Steam account login name before opening SteamCMD sign-in.");
   return;
  }
  if(!Regex.IsMatch(account,@"\A[A-Za-z0-9_.-]{1,64}\z"))throw new InvalidOperationException("Enter the Steam account login name for SteamCMD, not a password. Use 1–64 letters, digits, underscores, dots or hyphens.");
 }

 // Deliberately accepts only an account name. Passwords and Steam Guard codes belong in SteamCMD's visible console.
 private static ProcessStartInfo LoginStartInfo(string executable,string account)
 {
  ValidateSteamAccount(account,true);
  var start=new ProcessStartInfo(executable){UseShellExecute=false,WorkingDirectory=Path.GetDirectoryName(executable)!,CreateNoWindow=false,WindowStyle=ProcessWindowStyle.Normal};
  foreach(string arg in new[]{"+login",account,"+quit"})start.ArgumentList.Add(arg);
  return start;
 }

 private async Task Authenticate(PublicationRequest request)
 {
  try
  {
   var start=LoginStartInfo(steamCmd,request.Draft.SteamAccount);
   if(!File.Exists(steamCmd))throw new FileNotFoundException("SteamCMD is not installed in the helper Tools/SteamCMD directory.");
   EnsureNoReparse(gameRoot,steamCmd);
   lock(gate){state.Message="SteamCMD sign-in is open. Complete login and Steam Guard in its console. This action does not upload a Workshop item. Keep the console open until SteamCMD finishes.";}
   using var process=Process.Start(start)??throw new InvalidOperationException("Could not start SteamCMD.");
   await process.WaitForExitAsync();
   lock(gate){FinishAuthentication("SteamCMD sign-in console closed (exit code "+process.ExitCode+"). Login and Steam Guard must be completed in that console. Closing the console does not verify authentication. No Workshop upload was requested.","");}
  }
  catch(Exception error)
  {
   // Keep login failures separate from publication failures: they must not discard a reviewed snapshot.
   lock(gate){FinishAuthentication("SteamCMD sign-in could not be completed: "+error.Message+" Complete login and Steam Guard in the SteamCMD console when available. No Workshop upload was requested.","steam_login_failed");}
  }
 }

 private void FinishAuthentication(string message,string errorCode)
 {
  bool valid=false;
  if(plan!=null&&plan.SessionId==state.SessionId)
  {
   try{VerifyPlan(plan);valid=true;}
   catch(Exception error)when(error is IOException||error is UnauthorizedAccessException||error is InvalidOperationException){}
  }
  if(!valid)
  {
   if(plan!=null)message+=" The previous prepared snapshot is no longer valid. Prepare and review it again.";
   plan=null;state.PreparedId="";state.PreviewSnapshotPath="";state.PreviewSha256="";state.Files=new();state.TotalBytes=0;
  }
  state.Phase=valid?"ready":"idle";state.Message=message;state.ErrorCode=errorCode;
 }

 private static async Task AuthenticationSelfTest(PublishingEngine engine,PreparedPlan prepared)
 {
  var start=LoginStartInfo(engine.steamCmd,"example_user-1.2");
  Assert(start.ArgumentList.SequenceEqual(new[]{"+login","example_user-1.2","+quit"}),"Sign-in arguments contain an upload, password or extra command");
  Assert(start.FileName==engine.steamCmd&&!start.UseShellExecute&&!start.CreateNoWindow&&start.WindowStyle==ProcessWindowStyle.Normal&&!start.RedirectStandardInput&&!start.RedirectStandardOutput&&!start.RedirectStandardError,"Sign-in console must be visible and interactive");
  foreach(string invalid in new[]{""," ","user name","+quit","name\n","name\r","name;quit","name\"",new string('a',65),"пароль"})AssertThrows(()=>LoginStartInfo(engine.steamCmd,invalid),"Invalid sign-in account accepted");
  AssertThrows(()=>ValidateSteamAccount("name\n",false),"Draft account validation accepted a trailing newline");
  ValidateSteamAccount("",false);ValidateSteamAccount(new string('a',64),true);
  engine.plan=prepared;engine.state=new PublicationState{SessionId=prepared.SessionId,PreparedId=prepared.Id,PreviewSnapshotPath=prepared.Preview,PreviewSha256=prepared.PreviewHash,Draft=Clone(prepared.Draft),Files=prepared.Files,TotalBytes=prepared.Files.Sum(f=>f.Bytes)};
  string reviewed=prepared.DraftJson;
  engine.FinishAuthentication("Console closed; authentication unverified.","");
  Assert(engine.plan==prepared&&engine.state.Phase=="ready"&&engine.state.PreparedId==prepared.Id&&engine.state.PreviewSnapshotPath==prepared.Preview&&prepared.DraftJson==reviewed,"Sign-in closure discarded or changed a valid reviewed plan");
  await engine.Authenticate(new PublicationRequest{SessionId=prepared.SessionId,Draft=new PublicationDraft{SteamAccount=""}});
  Assert(engine.plan==prepared&&engine.state.Phase=="ready"&&engine.state.ErrorCode=="steam_login_failed"&&engine.state.Message.Contains("Enter the Steam account login name"),"Invalid sign-in lost prepared plan or unclear missing-account error");
  Assert(!File.Exists(engine.steamCmd),"Authentication self-test requires an isolated fixture without SteamCMD");
  await engine.Authenticate(new PublicationRequest{SessionId=prepared.SessionId,Draft=new PublicationDraft{SteamAccount="test_user"}});
  Assert(engine.plan==prepared&&engine.state.Phase=="ready"&&engine.state.ErrorCode=="steam_login_failed","Missing SteamCMD discarded valid plan");
  Assert(!Directory.Exists(Path.Combine(engine.publishingRoot,"attempts")),"Sign-in wrote a publication attempt");
  var consoleClosed=new TaskCompletionSource();engine.Start("authenticating",()=>consoleClosed.Task);
  Assert(engine.IsBusy&&engine.state.Phase=="authenticating","Sign-in did not remain busy while interactive operation was open");
  consoleClosed.SetResult();await engine.operation!;Assert(!engine.IsBusy,"Sign-in remained busy after interactive operation ended");
  engine.state.SessionId="other-session";engine.FinishAuthentication("Console closed; authentication unverified.","");Assert(engine.plan==null&&engine.state.Phase=="idle","Sign-in retained a snapshot from a different session");engine.state.SessionId=prepared.SessionId;
  engine.plan=null;engine.FinishAuthentication("Console closed; authentication unverified.","");Assert(engine.state.Phase=="idle"&&engine.state.PreparedId.Length==0,"Sign-in without a prepared plan became ready");
  var invalidPlan=engine.BuildPlan(prepared.Draft,prepared.SessionId);File.AppendAllText(invalidPlan.Vdf,"changed");engine.plan=invalidPlan;engine.FinishAuthentication("Console closed; authentication unverified.","");
  Assert(engine.plan==null&&engine.state.Phase=="idle","Sign-in retained a changed prepared snapshot");
 }
}
