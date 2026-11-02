using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using ProbablyModManager;

namespace ProbablyModManager.Launcher;

internal sealed partial class PublishingEngine
{
 private sealed class Binding
 {
  internal string Id="",Source="",Tags="",Message="";
 }
 private sealed class TagCatalog
 {
  public uint AppId=4348910;
  public string UpdatedUtc="",Message="";
  public List<WorkshopTag> Tags=new();
 }
 private TagCatalog catalog=new();
 private const int MaximumCatalogBytes=4*1024*1024;

 private Task Inspect(PublicationRequest request)
 {
  string source=FindSource(request.Draft.ModId);
  var binding=ReadBinding(source,request.Draft.ModId);
  if(binding.Id.Length==0)
  {
   var own=ReadOwnBinding(request.Draft.ModId);
   if(own.Id.Length>0){own.Tags=binding.Tags;binding=own;}
  }
  lock(gate)
  {
   plan=null;state.PreparedId="";state.PreviewSnapshotPath="";state.PreviewSha256="";
   state.Files=new();state.TotalBytes=0;state.Draft=Clone(request.Draft);state.MetadataModId=request.Draft.ModId;
   state.DetectedPublishedFileId=binding.Id;state.BindingSource=binding.Source;state.DetectedTags=binding.Tags;
   state.Phase="idle";state.Message=binding.Message.Length>0?binding.Message:binding.Id.Length>0?"Saved Workshop binding detected. No Steam item was created or changed.":"No valid saved Workshop binding found for this local mod. No item was selected by title.";
  }
  return Task.CompletedTask;
 }
 private static Binding ReadBinding(string source,string modId)
 {
  var result=new Binding();string folder=Path.Combine(source,".ps-workshop-uploader"),path=Path.Combine(folder,"state.xml");
  try
  {
   EnsureNoReparse(source,path);
   if(File.Exists(path))
   {
    var root=LoadMetadataXml(path);
    if(root.Name=="WorkshopBinding"&&root.Attribute("modId")?.Value==modId&&root.Attribute("appId")?.Value==AppId.ToString()&&PositiveId(root.Element("ItemId")?.Value,out string id))
    {result.Id=id;result.Source=".ps-workshop-uploader/state.xml";}
    else result.Message="Saved uploader binding was ignored: its root, app ID, mod ID or Workshop item ID does not match this local mod.";
   }
   string upload=Path.Combine(folder,"upload.xml");EnsureNoReparse(source,upload);
   if(File.Exists(upload))
   {
    var root=LoadMetadataXml(upload);
    if(root.Name=="WorkshopUpload"&&(root.Attribute("modId")==null||root.Attribute("modId")!.Value==modId)&&(root.Attribute("appId")==null||root.Attribute("appId")!.Value==AppId.ToString()))
    {
     string tags=root.Element("Tags")?.Value??root.Attribute("Tags")?.Value??"";
     var values=tags.Split(new[]{',',';'},StringSplitOptions.RemoveEmptyEntries).Select(t=>t.Trim()).Where(t=>t.Length>0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
     if(values.Count<=20&&values.All(t=>Encoding.UTF8.GetByteCount(t)<=64&&!t.Any(char.IsControl)))result.Tags=string.Join(", ",values);
    }
   }
  }
  catch(Exception e)when(e is IOException||e is UnauthorizedAccessException||e is XmlException||e is InvalidOperationException)
  {result.Message="Some saved uploader metadata was ignored because it is invalid or unsafe: "+e.Message;}
  return result;
 }
 private Binding ReadOwnBinding(string modId)
 {
  // The intent hash and explicit mod ID must both agree; never infer identity from a title.
  string path=Path.Combine(publishingRoot,"attempts",HashText(modId+"\nnew")+".json");
  try
  {
   EnsureNoReparse(dataRoot,path);var attempt=Read<Attempt>(path,8192);
   if(attempt?.Result=="success"&&attempt.ModId==modId&&PositiveId(attempt.PublishedFileId,out string id))return new Binding{Id=id,Source="Probably Mod Manager confirmed publication"};
  }
  catch(Exception e)when(e is IOException||e is UnauthorizedAccessException||e is InvalidOperationException){}
  return new Binding();
 }
 private static bool PositiveId(string? value,out string id)
 {
  id="";if(value==null||!Regex.IsMatch(value,@"^[0-9]{1,20}$")||!ulong.TryParse(value,out ulong numeric)||numeric==0)return false;
  id=numeric.ToString(System.Globalization.CultureInfo.InvariantCulture);return true;
 }
 private static XElement LoadMetadataXml(string path)
 {
  if(new FileInfo(path).Length>1024*1024)throw new InvalidOperationException("Uploader metadata exceeds 1 MiB.");
  using var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);
  using var reader=XmlReader.Create(stream,new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=1024*1024});
  return XDocument.Load(reader).Root??throw new InvalidOperationException("Uploader metadata has no root.");
 }
 private void LoadCatalog()
 {
  string path=Path.Combine(publishingRoot,"workshop-tags.json");
  try
  {
   EnsureNoReparse(dataRoot,path);var cached=Read<TagCatalog>(path,MaximumCatalogBytes);
   if(cached!=null&&cached.AppId==AppId&&ValidTags(cached.Tags)&&DateTime.TryParse(cached.UpdatedUtc,out _)){catalog=cached;catalog.Message="Workshop tags loaded from the local cache.";ApplyCatalog();return;}
  }
  catch(Exception e)when(e is IOException||e is UnauthorizedAccessException||e is InvalidOperationException){}
  try
  {
   catalog=new TagCatalog{Tags=ParseCatalog(ReadSeed()),UpdatedUtc="2026-11-02T00:00:00Z",Message="Bundled Workshop tag catalog. Refresh to check the latest Steam categories."};
  }
  catch(Exception e){catalog=new TagCatalog{Message="Workshop tag catalog unavailable: "+e.Message};}
  ApplyCatalog();
 }
 private static string ReadSeed()
 {
  using var stream=typeof(PublishingEngine).Assembly.GetManifestResourceStream("ProbablyModManager.WorkshopTagsSeed")??throw new InvalidOperationException("Bundled Workshop tag resource is missing.");
  using var reader=new StreamReader(stream,Encoding.UTF8);return reader.ReadToEnd();
 }
 private void ApplyCatalog()
 {
  state.AvailableTags=catalog.Tags.Select(t=>new WorkshopTag{Id=t.Id,Name=t.Name,Label=t.Label}).ToList();state.CatalogUpdatedUtc=catalog.UpdatedUtc;state.CatalogMessage=catalog.Message;
 }
 private async Task RefreshTags()
 {
  try
  {
   using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(20));using var client=new HttpClient();
   client.DefaultRequestHeaders.UserAgent.ParseAdd("ProbablyModManager/0.1 (Workshop category catalog)");
   using var response=await client.GetAsync("https://steamcommunity.com/app/4348910/workshop/?l=english",HttpCompletionOption.ResponseHeadersRead,timeout.Token);response.EnsureSuccessStatusCode();
   if(response.Content.Headers.ContentLength>MaximumCatalogBytes)throw new InvalidOperationException("Steam catalog page exceeds 4 MiB.");
   using var source=await response.Content.ReadAsStreamAsync(timeout.Token);using var output=new MemoryStream();byte[] buffer=new byte[32768];int count;
   while((count=await source.ReadAsync(buffer,timeout.Token))>0){if(output.Length+count>MaximumCatalogBytes)throw new InvalidOperationException("Steam catalog page exceeds 4 MiB.");await output.WriteAsync(buffer.AsMemory(0,count),timeout.Token);}
   var fresh=new TagCatalog{Tags=ParseCatalog(Encoding.UTF8.GetString(output.ToArray())),UpdatedUtc=DateTime.UtcNow.ToString("o"),Message="Workshop categories refreshed from Steam."};
   string path=Path.Combine(publishingRoot,"workshop-tags.json");EnsureNoReparse(dataRoot,path);Write(path,fresh);
   lock(gate){catalog=fresh;ApplyCatalog();state.Phase="idle";state.Message=fresh.Message;}
  }
  catch(Exception e)
  {
   lock(gate){catalog.Message="Could not refresh Steam categories; the previous catalog is retained. "+e.Message;ApplyCatalog();state.Phase="idle";state.Message=catalog.Message;}
  }
 }
 private static List<WorkshopTag> ParseCatalog(string html)
 {
  if(Encoding.UTF8.GetByteCount(html)>MaximumCatalogBytes)throw new InvalidOperationException("Steam catalog page exceeds 4 MiB.");
  string? payload=null;
  foreach(Match script in Regex.Matches(html,@"<script\b(?<attributes>[^>]*)>(?<json>[\s\S]*?)</script\s*>",RegexOptions.IgnoreCase,TimeSpan.FromSeconds(2)))
   if(Regex.IsMatch(script.Groups["attributes"].Value,@"\bid\s*=\s*[""']valve-ssr-data[""']",RegexOptions.IgnoreCase)){payload=script.Groups["json"].Value;break;}
  if(payload==null)throw new InvalidOperationException("Steam Workshop category data was not found.");
  using var document=JsonDocument.Parse(payload,new JsonDocumentOptions{MaxDepth=64});
  if(!document.RootElement.TryGetProperty("loaderData",out var loaders)||loaders.ValueKind!=JsonValueKind.Array)throw new InvalidOperationException("Steam Workshop loader data is missing.");
  foreach(var loader in loaders.EnumerateArray())
  {
   if(loader.ValueKind!=JsonValueKind.String)continue;string? text=loader.GetString();if(string.IsNullOrWhiteSpace(text))continue;
   using var data=JsonDocument.Parse(text,new JsonDocumentOptions{MaxDepth=64});var root=data.RootElement;
   if(root.ValueKind!=JsonValueKind.Object||!root.TryGetProperty("workshopConfig",out var config)||!config.TryGetProperty("appid",out var app)||Scalar(app)!=AppId.ToString())continue;
   if(!root.TryGetProperty("declaredTags",out var declared)||!declared.TryGetProperty("declared_tags",out var groups)||groups.ValueKind!=JsonValueKind.Array)continue;
   var tags=new List<WorkshopTag>();
   foreach(var group in groups.EnumerateArray())
   {
    if(!group.TryGetProperty("tags",out var entries)||entries.ValueKind!=JsonValueKind.Array)continue;
    foreach(var tag in entries.EnumerateArray())
    {
     if(tag.TryGetProperty("admin_only",out var admin)&&(admin.ValueKind==JsonValueKind.True||Scalar(admin)=="1"||Scalar(admin).Equals("true",StringComparison.OrdinalIgnoreCase)))continue;
     if(!tag.TryGetProperty("id",out var id)||!tag.TryGetProperty("name",out var name))continue;
     string rawName=Scalar(name),label=tag.TryGetProperty("display_name",out var display)?Scalar(display):rawName;
     tags.Add(new WorkshopTag{Id=Scalar(id),Name=rawName,Label=string.IsNullOrWhiteSpace(label)?rawName:label});
    }
   }
   tags=tags.GroupBy(t=>t.Id,StringComparer.Ordinal).Select(g=>g.First()).ToList();
   if(ValidTags(tags))return tags;
  }
  throw new InvalidOperationException("No declared Workshop categories for app 4348910 were found.");
 }
 private static string Scalar(JsonElement value)=>value.ValueKind==JsonValueKind.String?value.GetString()??"":value.ValueKind==JsonValueKind.Number?value.GetRawText():value.ValueKind==JsonValueKind.True?"true":value.ValueKind==JsonValueKind.False?"false":"";
 private static bool ValidTags(List<WorkshopTag>? tags)=>tags!=null&&tags.Count>0&&tags.Count<=1000&&tags.All(t=>t!=null&&PositiveId(t.Id,out _)&&!string.IsNullOrWhiteSpace(t.Name)&&Encoding.UTF8.GetByteCount(t.Name)<=64&&!t.Name.Any(char.IsControl)&&t.Label!=null&&t.Label.Length<=512&&!t.Label.Any(char.IsControl))&&tags.Select(t=>t.Id).Distinct(StringComparer.Ordinal).Count()==tags.Count;

 private static async Task MetadataSelfTest(string root,PublishingEngine engine,PublicationDraft draft,string mod)
 {
  string folder=Path.Combine(mod,".ps-workshop-uploader"),bindingPath=Path.Combine(folder,"state.xml"),uploadPath=Path.Combine(folder,"upload.xml");
  File.WriteAllText(bindingPath,"<WorkshopBinding modId='example.mod' appId='4348910'><ItemId>3830305634</ItemId></WorkshopBinding>");File.WriteAllText(uploadPath,"<WorkshopUpload><Tags>Mod, Utilities</Tags></WorkshopUpload>");
  var binding=ReadBinding(mod,draft.ModId);Assert(binding.Id=="3830305634"&&binding.Tags=="Mod, Utilities"&&binding.Source.EndsWith("state.xml"),"Valid uploader binding/tags not detected");
  AssertThrows(()=>engine.BuildPlan(draft,"session"),"Existing Workshop binding allowed duplicate creation");
  foreach(string invalid in new[]{"<Wrong modId='example.mod' appId='4348910'><ItemId>3830305634</ItemId></Wrong>","<WorkshopBinding modId='other' appId='4348910'><ItemId>3830305634</ItemId></WorkshopBinding>","<WorkshopBinding modId='example.mod' appId='1'><ItemId>3830305634</ItemId></WorkshopBinding>","<WorkshopBinding modId='example.mod' appId='4348910'><ItemId>0</ItemId></WorkshopBinding>","<!DOCTYPE WorkshopBinding [<!ENTITY x SYSTEM 'file:///must-not-read'>]><WorkshopBinding modId='example.mod' appId='4348910'><ItemId>&x;</ItemId></WorkshopBinding>"})
  {File.WriteAllText(bindingPath,invalid);Assert(ReadBinding(mod,draft.ModId).Id.Length==0,"Invalid binding accepted");}
  var tags=ParseCatalog(ReadSeed());Assert(tags.Count==29&&tags.Any(t=>t.Name=="Mod")&&tags.Any(t=>t.Name=="Utilities"),"Embedded tag seed does not contain 29 expected Steam categories");
  string Fixture(uint appId)=>"<script id='valve-ssr-data'>"+JsonSerializer.Serialize(new{loaderData=new[]{JsonSerializer.Serialize(new{workshopConfig=new{appid=appId},declaredTags=new{declared_tags=new[]{new{tags=new[]{new{id="1",name="Mod",display_name="Mods",admin_only=false},new{id="2",name="Admin",display_name="Admin",admin_only=true}}}}}})}})+"</script>";
  var filtered=ParseCatalog(Fixture(AppId));Assert(filtered.Count==1&&filtered[0].Name=="Mod"&&filtered[0].Label=="Mods","Admin category was exposed or raw/display names confused");AssertThrows(()=>ParseCatalog(Fixture(1)),"Wrong Steam app accepted");
  engine.catalog=new TagCatalog{Tags=tags,UpdatedUtc=DateTime.UtcNow.ToString("o")};engine.ApplyCatalog();engine.Poll("new-token","new-session");Assert(engine.state.AvailableTags.Count==29,"Session reset lost Workshop categories");
  var cancel=new PublicationRequest{Token="new-token",SessionId="new-session",RequestId=Guid.NewGuid().ToString("N"),CreatedUtc=DateTime.UtcNow.ToString("o"),Action="cancel"};Write(Path.Combine(engine.dataRoot,"publishing-request.json"),cancel);engine.Poll(cancel.Token,cancel.SessionId);Assert(engine.state.AvailableTags.Count==29,"Cancellation lost Workshop categories");
  await Task.CompletedTask;
 }
}
