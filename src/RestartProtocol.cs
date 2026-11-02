using System;
namespace ProbablyModManager
{
 public sealed class RestartSession
 {
  public string SessionId="",StartedUtc="";
 }
 public sealed class RestartHelperState
 {
  public string Token="",SessionId="",HeartbeatUtc="",Phase="",RequestId="",Message="";
  public bool PublishingAvailable;
  public int PublisherVersion;
 }
 public sealed class ManualRestartRequest
 {
  public string Token="",SessionId="",RequestId="",CreatedUtc="";
  public bool Cancelled;
 }
 public static class RestartProtocol
 {
  public static bool Recent(string value,DateTime now,int seconds)
  {
   if(!DateTime.TryParse(value,null,System.Globalization.DateTimeStyles.RoundtripKind,out var time))return false;
   var age=(now.ToUniversalTime()-time.ToUniversalTime()).TotalSeconds;return age>=-2&&age<=seconds;
  }
  public static bool HelperReady(RestartHelperState state,string session,DateTime now)=>state!=null&&state.Phase=="ready"&&!string.IsNullOrEmpty(state.Token)&&state.Token.Length>=32&&state.SessionId==session&&Recent(state.HeartbeatUtc,now,6);
  public static bool ValidRequest(ManualRestartRequest request,RestartHelperState state,DateTime now)=>request!=null&&!request.Cancelled&&state!=null&&request.Token==state.Token&&request.SessionId==state.SessionId&&Guid.TryParseExact(request.RequestId,"N",out _)&&Recent(request.CreatedUtc,now,5);
  public static bool Acknowledged(RestartHelperState state,ManualRestartRequest request,DateTime now)=>state!=null&&request!=null&&!request.Cancelled&&state.Phase=="armed"&&state.Token==request.Token&&state.RequestId==request.RequestId&&state.SessionId==request.SessionId&&Recent(state.HeartbeatUtc,now,6);
 }
}
