using System;
using System.Collections.Generic;
namespace ProbablyModManager
{
 public sealed class PublicationDraft
 {
  public string ModId="",Title="",Description="",ChangeNote="",PreviewPath="",PublishedFileId="",Tags="",SteamAccount="",Language="english";
  public int Visibility=2;
 }
 public sealed class PublicationFile
 {
  public string Path="",Sha256="";
  public long Bytes;
 }
 public sealed class WorkshopTag
 {
  public string Id="",Name="",Label="";
 }
 public sealed class PublicationRequest
 {
  public string Token="",SessionId="",RequestId="",CreatedUtc="",Action="prepare",PreparedId="";
  public PublicationDraft Draft=new PublicationDraft();
 }
 public sealed class PublicationState
 {
  public int ProtocolVersion=1;
  public string Token="",SessionId="",HeartbeatUtc="",RequestId="",Phase="idle",Message="",ErrorCode="",PreparedId="",PublishedFileId="",PublishedUrl="";
  public string PreviewSnapshotPath="",PreviewSha256="";
  public string MetadataModId="",DetectedPublishedFileId="",BindingSource="",DetectedTags="",CatalogUpdatedUtc="",CatalogMessage="";
  public List<WorkshopTag> AvailableTags=new List<WorkshopTag>();
  public bool SteamCmdAvailable;
  public long TotalBytes;
  public List<PublicationFile> Files=new List<PublicationFile>();
  public PublicationDraft Draft=new PublicationDraft();
 }
 public static class PublishingProtocol
 {
  public static bool FreshRequest(PublicationRequest request,string token,string session,DateTime now)=>request!=null&&request.Draft!=null&&request.Token==token&&request.SessionId==session&&Guid.TryParseExact(request.RequestId,"N",out _)&&RestartProtocol.Recent(request.CreatedUtc,now,15);
  public static bool FreshState(PublicationState state,string token,string session,DateTime now)=>state!=null&&state.Token==token&&state.SessionId==session&&RestartProtocol.Recent(state.HeartbeatUtc,now,8);
 }
}
