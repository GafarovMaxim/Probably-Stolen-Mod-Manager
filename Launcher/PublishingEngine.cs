using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml;
using System.Xml.Linq;
using ProbablyModManager;

namespace ProbablyModManager.Launcher;

internal sealed partial class PublishingEngine : IDisposable
{
 private const uint AppId=4348910;
 private const long MaximumBytes=512L*1024*1024;
 private static readonly JsonSerializerOptions Json=new(){IncludeFields=true,WriteIndented=true};
 private static readonly HashSet<string> AssetRoots=new(StringComparer.OrdinalIgnoreCase){"Sprites","Textures","Sounds","Localization","Paper","Assets","AssetBundles"};
 private static readonly HashSet<string> ExcludedDirs=new(StringComparer.OrdinalIgnoreCase){"Source","src","bin","obj","ModData","Logs","sessions","exports","profiles","profile-backups","publishing","saves","Backups",".git",".ps-workshop-uploader","node_modules","Library"};
 private static readonly HashSet<string> AssetExtensions=new(StringComparer.OrdinalIgnoreCase){".png",".jpg",".jpeg",".ogg",".wav",".mp3",".csv",".txt",".json",".xml",".assetbundle",".bundle",".bytes",".ttf",".otf"};
 private readonly string gameRoot,dataRoot,publishingRoot,steamCmd;
 private readonly object gate=new();
 private readonly HashSet<string> handled=new(StringComparer.Ordinal);
 private PublicationState state=new();
 private PreparedPlan? plan;
 private Task? operation;
 private bool disposed;
 public bool IsBusy {get {lock(gate)return operation!=null&&!operation.IsCompleted;}}
 private sealed class PreparedPlan
 {
  public string Id="",SessionId="",Root="",Content="",Preview="",PreviewHash="",Vdf="",DraftJson="",Intent="";
  public PublicationDraft Draft=new();
  public List<PublicationFile> Files=new();
 }
 private sealed class Attempt
 {
  public string PlanId="",RequestId="",StartedUtc="",Result="started",PublishedFileId="",ModId="";
 }
 internal PublishingEngine(string gameRoot,string dataRoot)
 {
  this.gameRoot=Path.GetFullPath(gameRoot);this.dataRoot=Path.GetFullPath(dataRoot);
  publishingRoot=Path.Combine(this.dataRoot,"publishing");steamCmd=Path.Combine(this.gameRoot,"Modding","ProbablyModManager","Tools","SteamCMD","steamcmd.exe");
  LoadCatalog();
 }
 public void Poll(string token,string session)
 {
  if(disposed)return;
  lock(gate)
  {
   if(operation?.IsCompleted==true)operation=null;
   if(state.SessionId!=session&&!IsBusy){state=new PublicationState{Token=token,SessionId=session};ApplyCatalog();plan=null;}
   state.Token=token;state.SessionId=session;state.HeartbeatUtc=DateTime.UtcNow.ToString("o");state.SteamCmdAvailable=File.Exists(steamCmd);
   var request=Read<PublicationRequest>(Path.Combine(dataRoot,"publishing-request.json"),256*1024);
   if(PublishingProtocol.FreshRequest(request!,token,session,DateTime.UtcNow)&&handled.Add(request!.RequestId))
   {
    if(handled.Count>4096){var last=request.RequestId;handled.Clear();handled.Add(last);}
    if(IsBusy){state.Message="An operation is already running. SteamCMD uploads cannot be cancelled after submission.";}
    else
    {
     state.RequestId=request.RequestId;
     switch(request.Action)
     {
      case "prepare": Start("preparing",()=>Prepare(request));break;
      case "publish": Start("running",()=>Publish(request));break;
      case "login": Start("authenticating",()=>Authenticate(request));break;
      case "pick-preview": Start("selecting_preview",()=>PickPreview(request));break;
      case "inspect": Start("inspecting",()=>Inspect(request));break;
      case "refresh-tags": Start("refreshing_tags",RefreshTags);break;
      case "cancel":plan=null;state=new PublicationState{Token=token,SessionId=session,RequestId=request.RequestId,Phase="idle",Message="Prepared publication cancelled. Nothing was uploaded."};ApplyCatalog();break;
      default:Fail("invalid_action","Unsupported publication action.");break;
     }
    }
   }
   SaveState();
  }
 }
 private void Start(string phase,Func<Task> run)
 {
  state.Phase=phase;state.Message="";state.ErrorCode="";
  operation=Task.Run(async()=>{try{await run();}catch(Exception e){lock(gate){if(!RecoverUncertainAttempt(e))Fail("operation_failed",e.Message);}}});
 }
 private Task Prepare(PublicationRequest request)
 {
  var prepared=BuildPlan(request.Draft,request.SessionId);
  lock(gate)
  {
   plan=prepared;state.Phase="ready";state.PreparedId=prepared.Id;state.Files=prepared.Files;state.TotalBytes=prepared.Files.Sum(f=>f.Bytes);state.Draft=Clone(prepared.Draft);state.PreviewSnapshotPath=prepared.Preview;state.PreviewSha256=prepared.PreviewHash;
   state.PublishedFileId="";state.PublishedUrl="";state.Message="Local snapshot prepared. Review every file and visibility before confirming publication. Nothing has been uploaded.";
  }
  return Task.CompletedTask;
 }
 private PreparedPlan BuildPlan(PublicationDraft draft,string session)
 {
  ValidateDraft(draft);
  var source=FindSource(draft.ModId);var manifest=LoadManifest(Path.Combine(source,"manifest.xml"));
  if(string.IsNullOrWhiteSpace(draft.PublishedFileId))
  {
   var binding=ReadBinding(source,draft.ModId);
   if(binding.Id.Length==0)binding=ReadOwnBinding(draft.ModId);
   if(binding.Id.Length>0)throw new InvalidOperationException("This local mod is already bound to Workshop item "+binding.Id+" ("+binding.Source+"). Set this PublishedFileId explicitly to update it; a duplicate item will not be created.");
  }
  string intent=HashText(draft.ModId+"\n"+(string.IsNullOrWhiteSpace(draft.PublishedFileId)?"new":draft.PublishedFileId));
  string prior=Path.Combine(publishingRoot,"attempts",intent+".json");
  var attempt=Read<Attempt>(prior,8192);
  if(attempt!=null&&string.IsNullOrWhiteSpace(draft.PublishedFileId))throw new InvalidOperationException("This local mod already has a publication attempt. Review Steam and specify its existing PublishedFileId before updating; an automatic duplicate will not be created. Known ID: "+attempt.PublishedFileId);
  var result=new PreparedPlan{Id=Guid.NewGuid().ToString("N"),SessionId=session,Draft=Clone(draft),Intent=intent};
  result.Root=Path.Combine(publishingRoot,"staging",result.Id);result.Content=Path.Combine(result.Root,"content");result.Vdf=Path.Combine(result.Root,"workshop.vdf");
  EnsureNoReparse(dataRoot,publishingRoot);Directory.CreateDirectory(result.Content);EnsureNoReparse(dataRoot,result.Content);
  var files=new HashSet<string>(StringComparer.OrdinalIgnoreCase){"manifest.xml"};
  foreach(var assembly in manifest.Elements("Assemblies").Elements("Assembly"))
  {
   string relative=assembly.Value.Trim();ValidateRelative(relative);if(relative.Split(new[]{'/','\\'}).Any(p=>ExcludedDirs.Contains(p)||p.StartsWith(".",StringComparison.Ordinal)))throw new InvalidOperationException("Declared assembly is inside a private/excluded folder: "+relative);if(!relative.EndsWith(".dll",StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Declared assembly must be a DLL: "+relative);
   string name=Path.GetFileName(relative);if(IsGameAssembly(name))throw new InvalidOperationException("Game/framework DLL cannot be published: "+relative);files.Add(relative);
  }
  foreach(string root in AssetRoots)
  {
   string folder=Path.Combine(source,root);if(!Directory.Exists(folder))continue;
   foreach(string path in EnumerateAssets(folder))files.Add(Path.GetRelativePath(source,path));
  }
  foreach(string path in Directory.GetFiles(source))
  {
   string name=Path.GetFileName(path);if(name.Equals("preview.png",StringComparison.OrdinalIgnoreCase)||name.Equals("preview.jpg",StringComparison.OrdinalIgnoreCase)||name.Equals("preview.jpeg",StringComparison.OrdinalIgnoreCase)||IsReleaseDocument(name))files.Add(name);
  }
  long total=0;
  foreach(string relative in files.OrderBy(f=>f,StringComparer.OrdinalIgnoreCase))
  {
   ValidateRelative(relative);string origin=Inside(source,relative);EnsureNoReparse(source,origin);
   if(!File.Exists(origin))throw new FileNotFoundException("Declared publication file not found: "+relative);
   var info=new FileInfo(origin);total+=info.Length;if(total>MaximumBytes)throw new InvalidOperationException("Publication snapshot exceeds 512 MiB.");
   string target=Inside(result.Content,relative);Directory.CreateDirectory(Path.GetDirectoryName(target)!);File.Copy(origin,target,false);
   result.Files.Add(Describe(target,relative));
  }
  string preview=Path.GetFullPath(draft.PreviewPath);EnsureNoReparse(Path.GetPathRoot(preview)!,preview);
  result.Preview=Path.Combine(result.Root,"preview"+Path.GetExtension(preview).ToLowerInvariant());File.Copy(preview,result.Preview,false);ValidatePreview(result.Preview);result.PreviewHash=HashFile(result.Preview);
  result.DraftJson=JsonSerializer.Serialize(result.Draft,Json);
  EnsureNoReparse(dataRoot,result.Vdf);File.WriteAllText(result.Vdf,MakeVdf(result),new UTF8Encoding(false));
  Write(Path.Combine(result.Root,"plan.json"),result);
  return result;
 }
 private string FindSource(string modId)
 {
  string mods=Path.Combine(gameRoot,"Mods");EnsureNoReparse(gameRoot,mods);
  var found=new List<string>();
  foreach(string folder in Directory.GetDirectories(mods))
  {
   if((File.GetAttributes(folder)&FileAttributes.ReparsePoint)!=0)continue;
   string file=Path.Combine(folder,"manifest.xml");if(!File.Exists(file))continue;
   try{EnsureNoReparse(folder,file);if(LoadManifest(file).Element("ID")?.Value.Trim()==modId)found.Add(folder);}catch(XmlException){}catch(InvalidOperationException){}
  }
  if(found.Count!=1)throw new InvalidOperationException("Select exactly one installed local mod with this ID. Found: "+found.Count);
  EnsureNoReparse(gameRoot,found[0]);return found[0];
 }
 private static XElement LoadManifest(string path)
 {
  using var reader=XmlReader.Create(path,new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=1024*1024});
  var root=XDocument.Load(reader).Root??throw new InvalidOperationException("Manifest is empty.");
  if(root.Name.LocalName!="Manifest"&&root.Name.LocalName!="Mod")throw new InvalidOperationException("Unsupported manifest root.");return root;
 }
 private static IEnumerable<string> EnumerateAssets(string folder)
 {
  if((File.GetAttributes(folder)&FileAttributes.ReparsePoint)!=0)throw new InvalidOperationException("Symbolic links/junctions cannot be published: "+Path.GetFileName(folder));
  foreach(string file in Directory.GetFiles(folder))
  {
   if((File.GetAttributes(file)&FileAttributes.ReparsePoint)!=0)throw new InvalidOperationException("Linked file cannot be published: "+Path.GetFileName(file));
   if(AssetExtensions.Contains(Path.GetExtension(file))&&!Path.GetFileName(file).StartsWith(".",StringComparison.Ordinal)&&!SensitiveName(Path.GetFileName(file)))yield return file;
  }
  foreach(string sub in Directory.GetDirectories(folder))if(!ExcludedDirs.Contains(Path.GetFileName(sub))&&!Path.GetFileName(sub).StartsWith(".",StringComparison.Ordinal))foreach(string file in EnumerateAssets(sub))yield return file;
 }
 private static bool SensitiveName(string name)=>Regex.IsMatch(name,@"^(manager|restart-.+|publishing-.+|bridge|session-.+|profile.+)\.(json|jsonl|log)$",RegexOptions.IgnoreCase);
 private static bool IsReleaseDocument(string name)
 {
  string extension=Path.GetExtension(name);return new[]{"README","LICENSE","COPYING","NOTICE"}.Any(prefix=>name.Equals(prefix,StringComparison.OrdinalIgnoreCase)||name.StartsWith(prefix+".",StringComparison.OrdinalIgnoreCase))&&(extension.Length==0||new[]{".md",".txt",".rst"}.Contains(extension,StringComparer.OrdinalIgnoreCase));
 }
 private static bool IsGameAssembly(string name)=>name.Equals("Assembly-CSharp.dll",StringComparison.OrdinalIgnoreCase)||name.StartsWith("Unity",StringComparison.OrdinalIgnoreCase)||name.StartsWith("System.",StringComparison.OrdinalIgnoreCase)||name.Equals("mscorlib.dll",StringComparison.OrdinalIgnoreCase)||name.Equals("netstandard.dll",StringComparison.OrdinalIgnoreCase)||name.Equals("0Harmony.dll",StringComparison.OrdinalIgnoreCase)||name.Equals("Newtonsoft.Json.dll",StringComparison.OrdinalIgnoreCase)||name.Contains("steamworks",StringComparison.OrdinalIgnoreCase);
 private static void ValidateDraft(PublicationDraft draft)
 {
  if(draft==null)throw new InvalidOperationException("Publication draft is missing.");
  if(!Regex.IsMatch(draft.ModId??"",@"^[A-Za-z0-9_.-]{1,160}$"))throw new InvalidOperationException("Invalid local mod ID.");
  if(string.IsNullOrWhiteSpace(draft.Title)||Encoding.UTF8.GetByteCount(draft.Title)>128)throw new InvalidOperationException("Title must contain 1–128 UTF-8 bytes.");
  if(string.IsNullOrWhiteSpace(draft.Description)||Encoding.UTF8.GetByteCount(draft.Description)>7999)throw new InvalidOperationException("Description must contain 1–7999 UTF-8 bytes.");
  if(Encoding.UTF8.GetByteCount(draft.ChangeNote??"")>7999)throw new InvalidOperationException("Change note is too long.");
  if(draft.Visibility<0||draft.Visibility>3)throw new InvalidOperationException("Invalid Workshop visibility.");
  if(!string.IsNullOrWhiteSpace(draft.PublishedFileId)&&(!ulong.TryParse(draft.PublishedFileId,out var id)||id==0))throw new InvalidOperationException("PublishedFileId must be a positive numeric ID, or empty for a new item.");
  ValidateSteamAccount(draft.SteamAccount,false);
  var tags=(draft.Tags??"").Split(new[]{',',';'},StringSplitOptions.RemoveEmptyEntries).Select(t=>t.Trim()).Where(t=>t.Length>0).ToArray();
  if(tags.Length>20||tags.Any(t=>Encoding.UTF8.GetByteCount(t)>64||t.Any(char.IsControl)))throw new InvalidOperationException("At most 20 tags, each at most 64 UTF-8 bytes, are supported.");
  string preview=Path.GetFullPath(draft.PreviewPath??"");string extension=Path.GetExtension(preview);
  if(!new[]{".png",".jpg",".jpeg"}.Contains(extension,StringComparer.OrdinalIgnoreCase)||!File.Exists(preview)||new FileInfo(preview).Length>1024*1024||new FileInfo(preview).Length==0)throw new InvalidOperationException("Choose a non-empty PNG/JPG preview no larger than 1 MiB.");
  ValidatePreview(preview);
  foreach(string value in new[]{draft.Title,draft.Description,draft.ChangeNote??"",draft.PreviewPath??""})if(value.IndexOf('\0')>=0)throw new InvalidOperationException("NUL characters are not supported.");
 }
 private static void ValidatePreview(string path)
 {
  if(!File.Exists(path)||new FileInfo(path).Length>1024*1024)throw new InvalidOperationException("Preview must be at most 1 MiB.");
  try{using var image=System.Drawing.Image.FromFile(path);if(image.RawFormat.Guid!=System.Drawing.Imaging.ImageFormat.Png.Guid&&image.RawFormat.Guid!=System.Drawing.Imaging.ImageFormat.Jpeg.Guid)throw new InvalidOperationException("Preview must actually be a PNG/JPG image.");if(image.Width<=0||image.Height<=0||image.Width>8192||image.Height>8192)throw new InvalidOperationException("Preview dimensions must be within 8192×8192.");}
  catch(ArgumentException){throw new InvalidOperationException("Preview is not a readable PNG/JPG image.");}
  catch(OutOfMemoryException){throw new InvalidOperationException("Preview is not a readable PNG/JPG image.");}
 }
 private static string MakeVdf(PreparedPlan plan)
 {
  var d=plan.Draft;var b=new StringBuilder("\"workshopitem\"\n{\n");
  void Pair(string key,string value)=>b.Append("  \"").Append(key).Append("\" \"").Append(VdfEscape(value)).Append("\"\n");
  Pair("appid",AppId.ToString());Pair("publishedfileid",string.IsNullOrWhiteSpace(d.PublishedFileId)?"0":d.PublishedFileId);Pair("contentfolder",plan.Content);Pair("previewfile",plan.Preview);Pair("visibility",d.Visibility.ToString());Pair("title",d.Title);Pair("description",d.Description);Pair("changenote",d.ChangeNote??"");
  var tags=(d.Tags??"").Split(new[]{',',';'},StringSplitOptions.RemoveEmptyEntries).Select(t=>t.Trim()).Where(t=>t.Length>0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
  if(tags.Count>0){b.Append("  \"tags\"\n  {\n");for(int i=0;i<tags.Count;i++)b.Append("    \"").Append(i).Append("\" \"").Append(VdfEscape(tags[i])).Append("\"\n");b.Append("  }\n");}
  return b.Append("}\n").ToString();
 }
 private static string VdfEscape(string value)=>(value??"").Replace("\\","\\\\").Replace("\"","\\\"").Replace("\r","").Replace("\n","\\n");
 private async Task Publish(PublicationRequest request)
 {
  PreparedPlan current;lock(gate){current=plan??throw new InvalidOperationException("Prepare and review a local snapshot first.");}
  if(request.PreparedId!=current.Id||request.SessionId!=current.SessionId||JsonSerializer.Serialize(request.Draft,Json)!=current.DraftJson)throw new InvalidOperationException("The draft or prepared plan changed. Prepare and review it again.");
  if(string.IsNullOrWhiteSpace(current.Draft.SteamAccount))throw new InvalidOperationException("Enter your SteamCMD account name and prepare the package again before submitting. Local preparation does not require sign-in.");
  if(!File.Exists(steamCmd))throw new FileNotFoundException("SteamCMD is not installed in the helper Tools/SteamCMD directory.");
  EnsureNoReparse(gameRoot,steamCmd);
  VerifyPlan(current);
  string attemptPath=Path.Combine(publishingRoot,"attempts",current.Intent+".json");EnsureNoReparse(dataRoot,attemptPath);Directory.CreateDirectory(Path.GetDirectoryName(attemptPath)!);EnsureNoReparse(dataRoot,attemptPath);
  var attempt=new Attempt{PlanId=current.Id,RequestId=request.RequestId,StartedUtc=DateTime.UtcNow.ToString("o"),PublishedFileId=current.Draft.PublishedFileId,ModId=current.Draft.ModId};
  // A persistent marker is written before launching SteamCMD; an uncertain new-item attempt is never silently retried.
  if(string.IsNullOrWhiteSpace(current.Draft.PublishedFileId))
  {using var marker=new FileStream(attemptPath,FileMode.CreateNew,FileAccess.Write,FileShare.Read);JsonSerializer.Serialize(marker,attempt,Json);}
  else{var earlier=Read<Attempt>(attemptPath,8192);if(earlier?.PlanId==current.Id)throw new InvalidOperationException("This prepared plan has already been submitted. Prepare a new update after reviewing Steam.");Write(attemptPath,attempt);}
  string log=Path.Combine(Path.GetDirectoryName(steamCmd)!,"workshopbuilds","depot_build_"+AppId+".log");long logOffset=File.Exists(log)?new FileInfo(log).Length:0;DateTime began=DateTime.UtcNow;
  var start=new ProcessStartInfo(steamCmd){UseShellExecute=false,WorkingDirectory=Path.GetDirectoryName(steamCmd)!,CreateNoWindow=false,WindowStyle=ProcessWindowStyle.Normal};
  foreach(string arg in new[]{"+login",current.Draft.SteamAccount,"+workshop_build_item",current.Vdf,"+quit"})start.ArgumentList.Add(arg);
  lock(gate){state.Phase="running";state.Message="SteamCMD is open. Complete login/Steam Guard in its console if requested. Upload progress is indeterminate; do not submit another copy. Upload cannot be cancelled once Steam starts it.";}
  using var process=Process.Start(start)??throw new InvalidOperationException("Could not start SteamCMD.");await process.WaitForExitAsync();
  string id=ReadPublishedId(current.Vdf),fragment=ReadNewLog(log,logOffset,began);bool success=ExplicitSuccess(fragment,id);
  attempt.PublishedFileId=id;attempt.Result=success?"success":"review_required";Write(attemptPath,attempt);
  lock(gate)
  {
   state.PublishedFileId=id;state.PublishedUrl=id.Length>0?"https://steamcommunity.com/sharedfiles/filedetails/?id="+id:"";
   state.Phase=success?"success":"review_required";state.ErrorCode=success?"":"steam_result_unconfirmed";
   state.Message=success?"SteamCMD confirmed publication in its new Workshop build log. Review the Workshop page and any legal agreement before making it public.":"SteamCMD exited (code "+process.ExitCode+") without a verifiable success record for this item. Review its console and Workshop build log. Do not create a second item; use the known PublishedFileId for an explicit update.";
   plan=null;
  }
 }
 private static string ReadPublishedId(string vdf)
 {
  var match=Regex.Match(File.ReadAllText(vdf),"\"publishedfileid\"\\s+\"([0-9]+)\"",RegexOptions.IgnoreCase);return match.Success&&ulong.TryParse(match.Groups[1].Value,out var id)&&id>0?match.Groups[1].Value:"";
 }
 private static string ReadNewLog(string path,long offset,DateTime began)
 {
  if(!File.Exists(path)||File.GetLastWriteTimeUtc(path)<began.AddSeconds(-2))return "";
  using var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite);if(file.Length>=offset)file.Seek(offset,SeekOrigin.Begin);
  if(file.Length-file.Position>2*1024*1024)return "";using var reader=new StreamReader(file);return reader.ReadToEnd();
 }
 private static bool ExplicitSuccess(string log,string id)
 {
  if(id.Length==0)return false;
  return Regex.IsMatch(log,@"\bSuccess\.\s+(?:Published|Updated)\s+item\s+"+Regex.Escape(id)+@"\b",RegexOptions.IgnoreCase)||
   Regex.IsMatch(log,@"(?im)^.*\b(?:PublishedFileId|Published File|Workshop item)\s*[:=#]?\s*"+Regex.Escape(id)+@"\b[^\r\n]*\b(?:successfully|success\s*[:=]\s*(?:true|1))\b");
 }
 private static void VerifyPlan(PreparedPlan current)
 {
  EnsureNoReparse(current.Root,current.Content);EnsureNoReparse(current.Root,current.Preview);
  var actual=SnapshotFiles(current.Content).Select(p=>Path.GetRelativePath(current.Content,p)).OrderBy(p=>p,StringComparer.OrdinalIgnoreCase).ToList();
  if(!actual.SequenceEqual(current.Files.Select(f=>f.Path).OrderBy(p=>p,StringComparer.OrdinalIgnoreCase),StringComparer.OrdinalIgnoreCase))throw new InvalidOperationException("Snapshot file list changed after preparation.");
  foreach(var expected in current.Files){string path=Inside(current.Content,expected.Path);EnsureNoReparse(current.Content,path);if(!File.Exists(path)||new FileInfo(path).Length!=expected.Bytes||HashFile(path)!=expected.Sha256)throw new InvalidOperationException("Snapshot file changed: "+expected.Path);}
  if(HashFile(current.Preview)!=current.PreviewHash||File.ReadAllText(current.Vdf)!=MakeVdf(current))throw new InvalidOperationException("Preview or Workshop configuration changed after review.");
 }
 private static IEnumerable<string> SnapshotFiles(string folder)
 {
  if((File.GetAttributes(folder)&FileAttributes.ReparsePoint)!=0)throw new InvalidOperationException("Snapshot contains a linked directory.");
  foreach(string file in Directory.GetFiles(folder)){if((File.GetAttributes(file)&FileAttributes.ReparsePoint)!=0)throw new InvalidOperationException("Snapshot contains a linked file.");yield return file;}
  foreach(string sub in Directory.GetDirectories(folder))foreach(string file in SnapshotFiles(sub))yield return file;
 }
 private Task PickPreview(PublicationRequest request)
 {
  var done=new TaskCompletionSource<string?>();var thread=new Thread(()=>{try{using var picker=new OpenFileDialog{Title="Choose Workshop preview (PNG/JPG, at most 1 MiB)",Filter="Preview images|*.png;*.jpg;*.jpeg",CheckFileExists=true,Multiselect=false};done.SetResult(picker.ShowDialog()==DialogResult.OK?picker.FileName:null);}catch(Exception e){done.SetException(e);}});thread.SetApartmentState(ApartmentState.STA);thread.Start();
  return Finish();
  async Task Finish(){string? picked=await done.Task;lock(gate){plan=null;state.PreparedId="";state.PreviewSnapshotPath="";state.PreviewSha256="";state.Draft=Clone(request.Draft);if(picked!=null)state.Draft.PreviewPath=picked;state.Phase="idle";state.Message=picked==null?"Preview selection cancelled.":"Preview selected. Prepare a snapshot before publishing.";}}
 }
 private static string Inside(string root,string relative)
 {
  ValidateRelative(relative);string path=Path.GetFullPath(Path.Combine(root,relative));string prefix=Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
  if(!path.StartsWith(prefix,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Path escapes the publication root.");return path;
 }
 private static void ValidateRelative(string relative)
 {
  if(string.IsNullOrWhiteSpace(relative)||Path.IsPathRooted(relative)||relative.IndexOf(':')>=0||relative.Split(new[]{'/','\\'}).Any(p=>p==".."||p=="."||p.Length==0))throw new InvalidOperationException("Invalid relative publication path: "+relative);
 }
 private static void EnsureNoReparse(string root,string path)
 {
  string fullRoot=Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar),full=Path.GetFullPath(path);
  if(!full.Equals(fullRoot,StringComparison.OrdinalIgnoreCase)&&!full.StartsWith(fullRoot+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Path is outside its allowed root.");
  string? cursor=full;while(cursor!=null){try{if((File.GetAttributes(cursor)&FileAttributes.ReparsePoint)!=0)throw new InvalidOperationException("Symbolic links/junctions cannot be published: "+Path.GetFileName(cursor));}catch(FileNotFoundException){}catch(DirectoryNotFoundException){}if(cursor.TrimEnd(Path.DirectorySeparatorChar).Equals(fullRoot,StringComparison.OrdinalIgnoreCase))break;cursor=Path.GetDirectoryName(cursor);}
 }
 private static PublicationFile Describe(string path,string relative)=>new(){Path=relative,Sha256=HashFile(path),Bytes=new FileInfo(path).Length};
 private static string HashFile(string path){using var stream=File.OpenRead(path);return Convert.ToHexString(SHA256.HashData(stream));}
 private static string HashText(string value)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
 private static PublicationDraft Clone(PublicationDraft value)=>JsonSerializer.Deserialize<PublicationDraft>(JsonSerializer.Serialize(value,Json),Json)!;
 private static T? Read<T>(string path,long limit)where T:class{try{if(!File.Exists(path)||new FileInfo(path).Length>limit)return null;return JsonSerializer.Deserialize<T>(File.ReadAllText(path),Json);}catch{return null;}}
 private static void Write<T>(string path,T value)
 {
  EnsureNoReparse(Path.GetPathRoot(path)!,path);Directory.CreateDirectory(Path.GetDirectoryName(path)!);EnsureNoReparse(Path.GetPathRoot(path)!,path);
  string temp=path+".publishing."+Guid.NewGuid().ToString("N")+".tmp";using(var file=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None))JsonSerializer.Serialize(file,value,Json);File.Move(temp,path,true);
 }
 private void SaveState(){try{state.HeartbeatUtc=DateTime.UtcNow.ToString("o");state.SteamCmdAvailable=File.Exists(steamCmd);Write(Path.Combine(dataRoot,"publishing-state.json"),state);}catch{}}
 private void Fail(string code,string message){state.Phase="error";state.ErrorCode=code;state.Message=message;state.PreparedId="";state.PreviewSnapshotPath="";state.PreviewSha256="";plan=null;}
 private bool RecoverUncertainAttempt(Exception error)
 {
  if(state.Phase!="running"||plan==null)return false;
  string path=Path.Combine(publishingRoot,"attempts",plan.Intent+".json");var attempt=Read<Attempt>(path,8192);if(attempt?.PlanId!=plan.Id)return false;
  string id="";try{id=ReadPublishedId(plan.Vdf);}catch{}
  attempt.Result="review_required";attempt.PublishedFileId=id;try{Write(path,attempt);}catch{}
  state.Phase="review_required";state.ErrorCode="steam_result_unconfirmed";state.PreparedId="";state.PreviewSnapshotPath="";state.PreviewSha256="";state.PublishedFileId=id;state.PublishedUrl=id.Length>0?"https://steamcommunity.com/sharedfiles/filedetails/?id="+id:"";
  state.Message="A persistent publication attempt exists, but its result could not be confirmed. Review Steam before preparing an update; no automatic duplicate will be created. "+error.Message;plan=null;return true;
 }
 public void Dispose(){disposed=true;/* An active SteamCMD upload is intentionally never killed. */}
 internal static async Task SelfTest(string root)
 {
  string resultRoot=Path.GetFullPath(root);root=Path.Combine(resultRoot,"run-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);string game=Path.Combine(root,"game"),data=Path.Combine(root,"data"),mod=Path.Combine(game,"Mods","example");Directory.CreateDirectory(mod);Directory.CreateDirectory(Path.Combine(mod,"Localization"));Directory.CreateDirectory(Path.Combine(mod,"Source"));Directory.CreateDirectory(Path.Combine(mod,".ps-workshop-uploader"));
  File.WriteAllText(Path.Combine(mod,"manifest.xml"),"<Manifest><ID>example.mod</ID><Assemblies><Assembly>Example.dll</Assembly></Assemblies></Manifest>");File.WriteAllBytes(Path.Combine(mod,"Example.dll"),new byte[]{1,2,3});File.WriteAllText(Path.Combine(mod,"Localization","en.csv"),"key,value");File.WriteAllText(Path.Combine(mod,"Source","private.cs"),"private");File.WriteAllText(Path.Combine(mod,"manager.json"),"private");File.WriteAllText(Path.Combine(mod,".ps-workshop-uploader","state.xml"),"private");using(var bitmap=new System.Drawing.Bitmap(1,1))bitmap.Save(Path.Combine(mod,"preview.png"),System.Drawing.Imaging.ImageFormat.Png);
  using var engine=new PublishingEngine(game,data);var draft=new PublicationDraft{ModId="example.mod",Title="Example \"title\"",Description="Example\ntext",PreviewPath=Path.Combine(mod,"preview.png"),SteamAccount="test_user",Visibility=2};
  File.WriteAllText(Path.Combine(mod,"LICENSE"),"Example license");File.WriteAllText(Path.Combine(mod,"NOTICE.md"),"Example notice");
  var prepared=engine.BuildPlan(draft,"session");
  Assert(prepared.Files.Any(f=>f.Path=="manifest.xml")&&prepared.Files.Any(f=>f.Path=="Example.dll")&&prepared.Files.Any(f=>f.Path.Replace('\\','/')=="Localization/en.csv"),"Required files missing");Assert(!prepared.Files.Any(f=>f.Path.Contains("Source")||f.Path.Contains("manager.json")||f.Path.Contains(".ps-workshop")),"Private file leaked");
  Assert(prepared.Files.Any(f=>f.Path=="LICENSE")&&prepared.Files.Any(f=>f.Path=="NOTICE.md"),"Release license/notice omitted");
  string vdf=File.ReadAllText(prepared.Vdf);Assert(vdf.Contains("\"visibility\" \"2\"")&&vdf.Contains("\"appid\" \"4348910\"")&&vdf.Contains("\\\"title\\\"")&&vdf.Contains("Example\\ntext"),"VDF escaping/private visibility incorrect");VerifyPlan(prepared);
  await AuthenticationSelfTest(engine,prepared);
  File.AppendAllText(Path.Combine(prepared.Content,"Example.dll"),"changed");AssertThrows(()=>VerifyPlan(prepared),"Changed snapshot accepted");AssertThrows(()=>Inside(mod,"../private"),"Traversal accepted");AssertThrows(()=>ValidateRelative("C:\\private"),"Absolute path accepted");
  var extraFile=engine.BuildPlan(draft,"session");File.WriteAllText(Path.Combine(extraFile.Content,"unreviewed.txt"),"not reviewed");AssertThrows(()=>VerifyPlan(extraFile),"Extra snapshot file accepted");
  var previewChanged=engine.BuildPlan(draft,"session");File.AppendAllText(previewChanged.Preview,"changed");AssertThrows(()=>VerifyPlan(previewChanged),"Changed preview accepted");
  var vdfChanged=engine.BuildPlan(draft,"session");File.AppendAllText(vdfChanged.Vdf,"changed");AssertThrows(()=>VerifyPlan(vdfChanged),"Changed VDF accepted");
  string dtd=Path.Combine(root,"dtd.xml");File.WriteAllText(dtd,"<!DOCTYPE Manifest [<!ENTITY x SYSTEM 'file:///must-not-read'>]><Manifest><ID>&x;</ID></Manifest>");bool rejectedDtd=false;try{LoadManifest(dtd);}catch(XmlException){rejectedDtd=true;}Assert(rejectedDtd,"Manifest DTD was accepted");
  Assert(!ExplicitSuccess("old success","")&&!ExplicitSuccess("Success. Published item 999","123")&&ExplicitSuccess("Success. Published item 123","123"),"Steam result attribution incorrect");
  Directory.CreateDirectory(Path.Combine(data,"publishing","attempts"));Write(Path.Combine(data,"publishing","attempts",prepared.Intent+".json"),new Attempt{PlanId=prepared.Id});AssertThrows(()=>engine.BuildPlan(draft,"session"),"Uncertain new publication retried");
  var state=new PublicationState{Token=new string('a',32),SessionId="session",HeartbeatUtc=DateTime.UtcNow.ToString("o")};var request=new PublicationRequest{Token=state.Token,SessionId=state.SessionId,RequestId=Guid.NewGuid().ToString("N"),CreatedUtc=DateTime.UtcNow.ToString("o")};Assert(PublishingProtocol.FreshRequest(request,state.Token,state.SessionId,DateTime.UtcNow),"Fresh request rejected");
  Assert(!PublishingProtocol.FreshRequest(request,"wrong-token",state.SessionId,DateTime.UtcNow)&&!PublishingProtocol.FreshRequest(request,state.Token,"wrong-session",DateTime.UtcNow),"Wrong session/token accepted");
  request.CreatedUtc=DateTime.UtcNow.AddMinutes(-1).ToString("o");Assert(!PublishingProtocol.FreshRequest(request,state.Token,state.SessionId,DateTime.UtcNow),"Stale publication accepted");
  engine.state=state;engine.plan=prepared;request.CreatedUtc=DateTime.UtcNow.ToString("o");request.RequestId=Guid.NewGuid().ToString("N");request.Action="cancel";Write(Path.Combine(data,"publishing-request.json"),request);engine.Poll(state.Token,state.SessionId);Assert(engine.plan==null&&engine.state.Phase=="idle"&&engine.state.PreviewSnapshotPath.Length==0,"Cancellation retained prepared publication");
  await MetadataSelfTest(root,engine,draft,mod);
  File.WriteAllText(Path.Combine(resultRoot,"publisher-self-test-result.txt"),"PASS: snapshot whitelist/private-data exclusion including license/notice; fixed app ID/private visibility; VDF escaping; path traversal and DTD rejection; changed/added snapshot files, preview and VDF rejected; exact Steam-result ID matching; persistent new-item replay protection; fresh session/token checks; prepared cancellation; metadata binding/app/mod/root/DTD checks; success/review_required item ID recovery with warning and duplicate protection; inspection clears unrelated item links; SSR app/admin filtering; embedded 29-tag seed; catalog survives cancellation/session changes; login-only arguments, strict account validation, interactive busy lifetime, preserved valid plan on login failure/closure, invalid-plan rejection after login. No Steam process was started.");await Task.CompletedTask;
 }
 private static void Assert(bool value,string message){if(!value)throw new InvalidOperationException(message);}
 private static void AssertThrows(Action action,string message){try{action();}catch(InvalidOperationException){return;}throw new InvalidOperationException(message);}
}
