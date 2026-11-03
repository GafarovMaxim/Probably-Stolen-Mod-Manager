using System;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Networking;

namespace ProbablyModManager
{
 public sealed partial class Manager
 {
  private Image nativePreview;
  private CustomUIElement nativePreviewHint;
  private string previewModId="";
  private float nextPreviewPoll;
  private CustomUIWindow changesWindow;
  private RectTransform changesRoot;
  private ulong changesId;
  private int changesPage=1;
  private bool changesPending;
  private readonly Dictionary<string,List<WorkshopChange>> changesCache=new Dictionary<string,List<WorkshopChange>>();
  private ulong WorkshopId(string id){var loaded=ModLoader.Mods.FirstOrDefault(m=>m.Manifest.ID==id);return loaded==null?0:loaded.WorkshopId!=0?loaded.WorkshopId:loaded.ShadowedWorkshopId;}
  private ulong WorkshopPageId(string id)
  {
   ulong workshop=WorkshopId(id);if(workshop!=0)return workshop;
   return publicationDrafts.TryGetValue(id,out var draft)&&ulong.TryParse(draft?.PublishedFileId,System.Globalization.NumberStyles.None,System.Globalization.CultureInfo.InvariantCulture,out workshop)?workshop:0;
  }
  private void OpenWorkshopPage(string id)
  {
   ulong workshop=WorkshopPageId(id);if(workshop==0)return;
   try{ModWorkshop.OpenItemPage(workshop);}catch(Exception ex){toast=T("Не удалось открыть страницу Steam: ","Could not open the Steam page: ")+ex.Message;journal.Add("warning",toast);}
  }
  private void BuildPreview(CustomUIBuilder b,ModInfo mod)
  {
   previewModId=mod.Id;b.AddImage((Sprite)null,-1,205);nativePreview=b.Last.image;
   if(nativePreview!=null){nativePreview.preserveAspect=true;nativePreview.color=new Color(1,1,1,0);nativePreview.raycastTarget=false;}
   nativePreviewHint=NativeLabel(b,WorkshopId(mod.Id)!=0?T("Загрузка превью Steam…","Loading Steam preview…"):T("Локальное превью","Local preview"),17);
   if(WorkshopId(mod.Id)!=0)ModWorkshop.RequestDetails(new[]{WorkshopId(mod.Id)});nextPreviewPoll=0;TickPreview();
  }
  private void TickPreview()
  {
   if(tab!=0||nativePreview==null||Time.unscaledTime<nextPreviewPoll)return;nextPreviewPoll=Time.unscaledTime+.6f;
   var loaded=ModLoader.Mods.FirstOrDefault(m=>m.Manifest.ID==previewModId);var vanilla=ModUIManager.Instance;if(loaded==null||vanilla==null)return;
   try
   {
    // Reuse the game's preview downloader and cache, including its local fallback.
    var method=typeof(ModUIManager).GetMethod("GetPreview",BindingFlags.Instance|BindingFlags.NonPublic);
    var sprite=method?.Invoke(vanilla,new object[]{loaded}) as Sprite;
    if(sprite!=null){nativePreview.sprite=sprite;nativePreview.color=Color.white;nativePreviewHint?.SetText(WorkshopId(previewModId)!=0?T("Превью Workshop","Workshop preview"):T("Превью мода","Mod preview"));}
    else if(!ModWorkshop.IsDetailsPending(WorkshopId(previewModId)))nativePreviewHint?.SetText(T("Превью недоступно","Preview unavailable"));
   }
   catch{nativePreviewHint?.SetText(T("Превью недоступно","Preview unavailable"));nextPreviewPoll=Time.unscaledTime+20;}
  }
  private void OpenChangelog(string id)
  {
   ulong workshop=WorkshopPageId(id);if(workshop==0){toast=T("У локального мода нет страницы Steam Workshop","This local mod has no Steam Workshop page");return;}
   changesWindow?.Close();changesId=workshop;changesPage=1;
   var b=CustomUIManager.Instance.CreateWindow("pmm_changelog",T("История обновлений · ","Change notes · ")+(mods.FirstOrDefault(m=>m.Id==id)?.Name??id),InMenu?"menu":"meta",CustomUIManager.Instance.optionUIPrefab,CustomUIManager.Style.Meta).SetCloseOnEscape();changesWindow=b.Handle;
   changesWindow.Rect.anchorMin=Vector2.zero;changesWindow.Rect.anchorMax=Vector2.one;changesWindow.Rect.offsetMin=changesWindow.Rect.offsetMax=Vector2.zero;
   var panel=changesWindow.ContentRoot.parent as RectTransform;if(panel!=null){panel.anchorMin=panel.anchorMax=panel.pivot=new Vector2(.5f,.5f);panel.sizeDelta=new Vector2(1050,780);panel.anchoredPosition=Vector2.zero;}
   changesRoot=changesWindow.ContentRoot;changesWindow.Show();LoadChangelog();
  }
  private void LoadChangelog(bool force=false)
  {
   if(changesWindow==null||!changesWindow.IsAlive)return;string key=changesId+"/"+changesPage;
   if(!force&&changesCache.TryGetValue(key,out var entries)){DrawChangelog(entries,"");return;}
   changesPending=true;DrawChangelog(null,T("Загрузка из Steam…","Loading from Steam…"));StartCoroutine(FetchChangelog(changesId,changesPage));
  }
  private IEnumerator FetchChangelog(ulong id,int page)
  {
   using(var request=UnityWebRequest.Get("https://steamcommunity.com/sharedfiles/filedetails/changelog/"+id+"?p="+page+"&l="+(Russian?"russian":"english")))
   {
    request.timeout=20;yield return request.SendWebRequest();
    List<WorkshopChange> entries=null;string error="";
    if(request.result!=UnityWebRequest.Result.Success)error=T("Не удалось загрузить Steam: ","Could not load Steam: ")+request.error;
    else if(request.downloadHandler.data.Length>2*1024*1024)error=T("Ответ Steam слишком большой","Steam response is too large");
    else{entries=WorkshopChangeParser.Parse(request.downloadHandler.text);if(entries.Count==0)error=T("На этой странице нет доступных записей. Автор мог не публиковать заметки, либо Steam изменил формат страницы.","No readable entries on this page. The author may not have posted change notes, or Steam may have changed the page format.");else changesCache[id+"/"+page]=entries;}
    if(changesId==id&&changesPage==page&&changesWindow?.IsAlive==true){changesPending=false;DrawChangelog(entries,error);}
   }
  }
  private void DrawChangelog(List<WorkshopChange> entries,string status)
  {
   if(changesRoot==null)return;var b=Into(changesRoot);NativeLabel(b,T("Заметки автора в Steam · онлайн · страница ","Author's Steam notes · online · page ")+changesPage,19,Mint);
   b.BeginScrollFlexible();StyleScroll(b.CurrentContainer.GetComponentInParent<ScrollRect>());
   if(!string.IsNullOrEmpty(status))NativeLabel(b,status,22,Warning);
   foreach(var entry in entries??new List<WorkshopChange>()){NativeLabel(b,entry.Date,23,Mint);NativeLabel(b,entry.Text,21);b.AddSeparator();}b.End();
   b.BeginRow(10);NativeButton(b,"←",()=>{changesPage--;LoadChangelog();},80,!changesPending&&changesPage>1);NativeButton(b,"→",()=>{changesPage++;LoadChangelog();},80,!changesPending&&entries?.Count>0);NativeButton(b,T("Обновить","Refresh"),()=>LoadChangelog(true),180,!changesPending);NativeButton(b,T("Страница Steam","Steam page"),()=>ModWorkshop.OpenItemPage(changesId),230);b.End();CustomUIBuilder.RebuildLayout(changesRoot);
  }
 }
}
