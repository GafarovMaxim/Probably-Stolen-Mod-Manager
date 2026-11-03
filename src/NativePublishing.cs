using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.UI;

namespace ProbablyModManager
{
 public sealed partial class Manager
 {
  private Dictionary<string,PublicationDraft> publicationDrafts=new Dictionary<string,PublicationDraft>();
  private PublicationDraft publicationDraft;
  private PublicationState publicationState;
  private string publicationModId="",publicationRequestId="",publicationRequestAction="",publicationSignature="",publicationAppliedPreview="";
  private float publicationPollAt,publicationSaveAt=-1,publicationSentAt;
  private bool publicationConfirm,publicationReviewed,publicationWaiting,publicationChoosingTags,publicationInspectPending;
  private int publicationFilePage;
  private Sprite publicationPreviewSprite;
  private Texture2D publicationPreviewTexture;
  private string publicationPreviewKey="";
  private RectTransform publicationStatusRoot,publicationBindingRoot;
  private ScrollRect publicationFormScroll;
  private CustomUIElement publicationConnection,publicationConnectionHint,publicationIdInput,publicationTagsInput,publicationPreviewInput;
  private readonly List<KeyValuePair<CustomUIElement,Func<bool>>> publicationControls=new List<KeyValuePair<CustomUIElement,Func<bool>>>();
  private bool publicationHelperConnected;
  private string publicationStatusSignature="",publicationBindingSignature="";
  private bool PublishingBusy=>publicationWaiting||publicationState!=null&&new[]{"preparing","selecting_preview","running","authenticating","inspecting","refreshing_tags"}.Contains(publicationState.Phase);
  private void InitializePublishing()
  {
   publicationDrafts=Storage.Read<Dictionary<string,PublicationDraft>>("publishing-drafts.json")??new Dictionary<string,PublicationDraft>();
  }
  private void SavePublicationDrafts()
  {
   if(publicationDrafts==null)return;Storage.Write("publishing-drafts.json",publicationDrafts);publicationSaveAt=-1;
  }
  private void SelectPublicationMod(string id,bool rebuild=true)
  {
   if(PublishingBusy)return;publicationModId=id;
   if(!publicationDrafts.TryGetValue(id,out publicationDraft))
   {
    var mod=mods.FirstOrDefault(m=>m.Id==id);publicationDraft=new PublicationDraft{ModId=id,Title=mod?.Name??id,Description=mod?.Description??"",PreviewPath=mod==null?"":System.IO.Path.Combine(mod.Folder,"preview.png")};
    publicationDrafts[id]=publicationDraft;
   }
   publicationDraft=publicationDraft??new PublicationDraft{ModId=id};publicationDrafts[id]=publicationDraft;
   publicationConfirm=false;publicationChoosingTags=false;publicationReviewed=false;publicationFilePage=0;publicationInspectPending=true;if(rebuild)NativeInvalidate();
  }
  private void PublicationChanged(Action change)
  {
   if(PublishingBusy)return;change();publicationReviewed=false;publicationConfirm=false;publicationSaveAt=Time.unscaledTime+1.5f;RefreshPublicationUi();
  }
  private bool PublicationHelperReady(out RestartHelperState helper)
  {
   helper=RestartHelper();return RestartProtocol.HelperReady(helper,sessionId,DateTime.UtcNow)&&helper.PublishingAvailable&&helper.PublisherVersion>=2;
  }
  private bool PublicationPlanMatches()=>publicationDraft!=null&&publicationState!=null&&publicationState.Phase=="ready"&&publicationState.PreparedId.Length>0&&JsonConvert.SerializeObject(publicationDraft)==JsonConvert.SerializeObject(publicationState.Draft);
  private bool PublicationLoginAvailable()=>publicationHelperConnected&&InMenu&&!PublishingBusy&&!string.IsNullOrWhiteSpace(publicationDraft?.SteamAccount)&&PublicationHelperReady(out var helper)&&helper.PublisherVersion>=3&&PublishingProtocol.FreshState(publicationState,helper.Token,sessionId,DateTime.UtcNow)&&publicationState.SteamCmdAvailable;
  private void SendPublication(string action)
  {
   if(!InMenu||!PublicationHelperReady(out var helper)){toast=T("Подключи обновлённый помощник Probably Mod Manager.exe.","Connect the updated Probably Mod Manager.exe companion.");return;}
   if(PublishingBusy||publicationDraft==null)return;
   if(action=="login"&&!PublicationLoginAvailable())return;
   if(action=="publish"&&(!PublicationPlanMatches()||!publicationReviewed))return;
   var request=new PublicationRequest{Token=helper.Token,SessionId=sessionId,RequestId=Guid.NewGuid().ToString("N"),CreatedUtc=DateTime.UtcNow.ToString("o"),Action=action,PreparedId=action=="publish"||action=="login"&&PublicationPlanMatches()?publicationState.PreparedId:"",Draft=JsonConvert.DeserializeObject<PublicationDraft>(JsonConvert.SerializeObject(publicationDraft))};
   bool leavingConfirmation=publicationConfirm;
   SavePublicationDrafts();Storage.Write("publishing-request.json",request);publicationRequestId=request.RequestId;publicationRequestAction=action;publicationWaiting=true;publicationSentAt=Time.unscaledTime;publicationConfirm=false;
   if(leavingConfirmation)NativeInvalidate();else RefreshPublicationUi();
  }
  private void TickPublishing()
  {
   if(publicationSaveAt>=0&&Time.unscaledTime>=publicationSaveAt)SavePublicationDrafts();
   if(publicationInspectPending&&publicationDraft!=null&&!PublishingBusy&&PublicationHelperReady(out _)){publicationInspectPending=false;SendPublication("inspect");}
   if(Time.unscaledTime<publicationPollAt)return;publicationPollAt=Time.unscaledTime+.5f;
   var helper=RestartHelper();var state=Storage.Read<PublicationState>("publishing-state.json");
   if(!RestartProtocol.HelperReady(helper,sessionId,DateTime.UtcNow)||!PublishingProtocol.FreshState(state,helper.Token,sessionId,DateTime.UtcNow))
   {
    publicationState=null;
    if(publicationWaiting&&Time.unscaledTime-publicationSentAt>20){publicationWaiting=false;toast=T("Помощник не подтвердил запрос. Проверь его подключение.","Companion did not acknowledge the request. Check its connection.");}
    RefreshPublicationUi();return;
   }
   publicationState=state;
   if(state.RequestId==publicationRequestId)
   {
    publicationWaiting=false;
    ApplyPublicationInspection(state);
    if(publicationRequestAction=="pick-preview"&&state.Phase!="selecting_preview"&&state.Phase!="error"&&publicationAppliedPreview!=state.RequestId&&state.Draft!=null&&publicationDraft!=null)
    {publicationAppliedPreview=state.RequestId;publicationDraft.PreviewPath=state.Draft.PreviewPath;SavePublicationDrafts();}
    string resultId=publicationRequestAction=="publish"&&publicationDraft!=null?PublishingProtocol.ResultItemId(state,publicationRequestId,publicationDraft.ModId):"";
    if(resultId.Length>0&&publicationDraft.PublishedFileId!=resultId)
    {publicationDraft.PublishedFileId=resultId;publicationReviewed=false;SavePublicationDrafts();}
   }
   string signature=state.RequestId+"|"+state.Phase+"|"+state.Message+"|"+state.PreparedId+"|"+state.PublishedFileId;
   if(signature!=publicationSignature)
   {
    publicationSignature=signature;
    if(visible&&tab==3&&publicationChoosingTags&&publicationRequestAction=="refresh-tags"&&state.RequestId==publicationRequestId&&state.Phase!="refreshing_tags")NativeInvalidate();
   }
   if(publicationWaiting&&Time.unscaledTime-publicationSentAt>20){publicationWaiting=false;toast=T("Помощник не подтвердил запрос. Проверь его подключение.","Companion did not acknowledge the request. Check its connection.");}
   RefreshPublicationUi();
  }
  private CustomUIElement PublicationControl(CustomUIElement control,Func<bool> enabled)
  {
   publicationControls.Add(new KeyValuePair<CustomUIElement,Func<bool>>(control,enabled));control.SetInteractable(enabled());return control;
  }
  private void ResetPublicationUi()
  {
   publicationControls.Clear();publicationStatusRoot=null;publicationBindingRoot=null;publicationFormScroll=null;publicationConnection=null;publicationConnectionHint=null;publicationIdInput=null;publicationTagsInput=null;publicationPreviewInput=null;publicationStatusSignature="";publicationBindingSignature="";
   publicationHelperConnected=PublicationHelperReady(out _);
  }
  private static void SyncPublicationInput(CustomUIElement control,string value)
  {
   if(control?.IsAlive==true&&control.input!=null&&!control.input.isFocused&&control.input.text!=(value??""))control.input.SetTextWithoutNotify(value??"");
  }
  private void RefreshPublicationUi()
  {
   publicationHelperConnected=PublicationHelperReady(out _);
   if(!visible||tab!=3)return;
   if(publicationConnection?.IsAlive==true){publicationConnection.SetText(publicationHelperConnected?T("Помощник подключён","Companion connected"):T("Помощник не подключён","Companion not connected"));if(publicationConnection.text!=null)publicationConnection.text.color=publicationHelperConnected?Mint:Warning;}
   if(publicationConnectionHint?.IsAlive==true)publicationConnectionHint.SetVisible(!publicationHelperConnected);
   publicationControls.RemoveAll(control=>control.Key?.IsAlive!=true);
   foreach(var control in publicationControls)control.Key.SetInteractable(control.Value());
   SyncPublicationInput(publicationIdInput,publicationDraft?.PublishedFileId);SyncPublicationInput(publicationTagsInput,publicationDraft?.Tags);SyncPublicationInput(publicationPreviewInput,publicationDraft?.PreviewPath);
   var state=publicationState;
   string status=publicationWaiting+"|"+publicationRequestAction+"|"+publicationHelperConnected+"|"+(state==null?"":string.Join("|",state.RequestId,state.Draft?.ModId,state.Phase,state.Message,state.ErrorCode,state.Files?.Count.ToString(),state.TotalBytes.ToString(),state.PublishedUrl));
   string binding=(state?.MetadataModId??"")+"|"+(state?.DetectedPublishedFileId??"")+"|"+(state?.BindingSource??"")+"|"+(publicationDraft?.PublishedFileId??"")+"|"+PublishingBusy;
   bool layout=false;float offset=publicationFormScroll!=null?publicationFormScroll.verticalNormalizedPosition:1;
   if(publicationStatusRoot!=null&&status!=publicationStatusSignature){publicationStatusSignature=status;BuildPublicationStatus(Into(publicationStatusRoot));layout=true;}
   if(publicationBindingRoot!=null&&binding!=publicationBindingSignature){publicationBindingSignature=binding;BuildPublicationBindingHint(Into(publicationBindingRoot));layout=true;}
   if(layout){CustomUIBuilder.RebuildLayout(nativeBody);Canvas.ForceUpdateCanvases();if(publicationFormScroll!=null)publicationFormScroll.verticalNormalizedPosition=offset;}
  }
  private void BuildPublishing(CustomUIBuilder b)
  {
   ResetPublicationUi();
   if(publicationConfirm){BuildPublicationConfirmation(b);return;}
   if(publicationChoosingTags){BuildPublicationTagPicker(b);return;}
   var local=mods.Where(m=>m.Source=="Local").ToList();
   if(publicationDraft==null&&local.Count>0){SelectPublicationMod(local.First().Id,false);}
   bool connected=publicationHelperConnected;
   b.BeginRow(12);NativeLabel(b,T("Публикация локальных модов в Steam Workshop","Publish local mods to Steam Workshop"),25,Mint);b.AddFlexibleSpace();publicationConnection=NativeLabel(b,connected?T("Помощник подключён","Companion connected"):T("Помощник не подключён","Companion not connected"),18,connected?Mint:Warning);b.End();
   publicationConnectionHint=NativeLabel(b,T("Запусти обновлённый Probably Mod Manager.exe из папки игры. Публикация доступна из главного меню.","Launch the updated Probably Mod Manager.exe from the game folder. Publishing is available from the main menu."),20,Warning);publicationConnectionHint.SetVisible(!connected);
   b.BeginRow(24);b.BeginColumn(8);Size(b.CurrentContainer,310,-1,0,1);NativeLabel(b,T("ЛОКАЛЬНЫЕ МОДЫ","LOCAL MODS"),20,Mint);b.BeginScrollFlexible();StyleScroll(b.CurrentContainer.GetComponentInParent<ScrollRect>());
   foreach(var mod in local){string id=mod.Id;PublicationControl(NativeButton(b,(id==publicationModId?"› ":"")+mod.Name,()=>SelectPublicationMod(id)),()=>!PublishingBusy);}
   if(local.Count==0)NativeLabel(b,T("В папке Mods нет локальных модов.","No local mods in the Mods folder."));b.End();b.End();
   b.BeginScrollFlexible();publicationFormScroll=b.CurrentContainer.GetComponentInParent<ScrollRect>();StyleScroll(publicationFormScroll);
   if(publicationDraft!=null)
   {
    var mod=mods.FirstOrDefault(m=>m.Id==publicationModId);NativeLabel(b,mod?.Folder??publicationModId,17);NativeLabel(b,T("Название","Title"),19,Mint);
    PublicationControl(NativeInput(b,"",publicationDraft.Title,value=>PublicationChanged(()=>publicationDraft.Title=value)),()=>!PublishingBusy);
    NativeLabel(b,T("Описание страницы","Workshop description"),19,Mint);PublicationControl(NativeInput(b,"",publicationDraft.Description,value=>PublicationChanged(()=>publicationDraft.Description=value),true).SetPreferredSize(-1,160),()=>!PublishingBusy);
    b.BeginRow(10);b.BeginColumn(5);Size(b.CurrentContainer,-1,-1,1,0);NativeLabel(b,T("Видимость","Visibility"),19,Mint);
    PublicationControl(NativeSelect(b,new[]{T("Для всех","Public"),T("Для друзей","Friends only"),T("Личный","Private"),T("По ссылке","Unlisted")},Mathf.Clamp(publicationDraft.Visibility,0,3),value=>PublicationChanged(()=>publicationDraft.Visibility=value),-1,!PublishingBusy),()=>!PublishingBusy);b.End();
    b.BeginColumn(5);Size(b.CurrentContainer,-1,-1,1,0);NativeLabel(b,T("ID существующей страницы","Existing Workshop item ID"),19,Mint);b.BeginRow(8);publicationIdInput=PublicationControl(NativeInput(b,T("Пусто — создать новую страницу","Empty: create a new item"),publicationDraft.PublishedFileId,value=>PublicationChanged(()=>publicationDraft.PublishedFileId=value.Trim())),()=>!PublishingBusy);PublicationControl(NativeButton(b,T("Найти ID","Find ID"),()=>SendPublication("inspect"),120),()=>publicationHelperConnected&&!PublishingBusy&&InMenu);b.End();b.BeginColumn(4);publicationBindingRoot=b.CurrentContainer;b.End();b.End();b.End();
    NativeLabel(b,T("Превью PNG/JPG, до 1 МБ","PNG/JPG preview, up to 1 MB"),19,Mint);b.BeginRow(8);publicationPreviewInput=PublicationControl(NativeInput(b,T("Путь к изображению","Image path"),publicationDraft.PreviewPath,value=>PublicationChanged(()=>publicationDraft.PreviewPath=value)),()=>!PublishingBusy);PublicationControl(NativeButton(b,T("Выбрать…","Browse…"),()=>SendPublication("pick-preview"),170),()=>publicationHelperConnected&&!PublishingBusy&&InMenu);b.End();
    b.BeginRow(10);b.BeginColumn(5);Size(b.CurrentContainer,-1,-1,1,0);NativeLabel(b,T("Метки Steam, через запятую","Steam tags, comma separated"),19,Mint);b.BeginRow(8);publicationTagsInput=PublicationControl(NativeInput(b,"",publicationDraft.Tags,value=>PublicationChanged(()=>publicationDraft.Tags=value)),()=>!PublishingBusy);PublicationControl(NativeButton(b,T("Выбрать…","Choose…"),()=>{publicationChoosingTags=true;NativeInvalidate();},170),()=>!PublishingBusy);b.End();b.End();
    b.BeginColumn(5);Size(b.CurrentContainer,-1,-1,1,0);NativeLabel(b,T("Логин для SteamCMD","SteamCMD account name"),19,Mint);b.BeginRow(8);PublicationControl(NativeInput(b,T("Пароль вводится только в окне SteamCMD","Password is entered only in SteamCMD"),publicationDraft.SteamAccount,value=>PublicationChanged(()=>publicationDraft.SteamAccount=value.Trim())),()=>!PublishingBusy);PublicationControl(NativeButton(b,T("Войти…","Sign in…"),()=>SendPublication("login"),150),PublicationLoginAvailable);b.End();b.End();b.End();
    NativeLabel(b,T("Заметка об обновлении","Change note"),19,Mint);PublicationControl(NativeInput(b,"",publicationDraft.ChangeNote,value=>PublicationChanged(()=>publicationDraft.ChangeNote=value),true),()=>!PublishingBusy);
    NativeLabel(b,T("При отправке откроется окно SteamCMD. Вход и Steam Guard выполняются там. Подготовка пакета ничего не отправляет в Steam.","Submission opens SteamCMD. Sign-in and Steam Guard happen there. Preparing a package does not upload anything."),19);
    b.BeginRow(10);PublicationControl(NativeButton(b,T("Проверить и подготовить","Validate and prepare"),()=>SendPublication("prepare"),300),()=>publicationHelperConnected&&InMenu&&!PublishingBusy);PublicationControl(NativeButton(b,T("Просмотреть отправку…","Review submission…"),()=>{publicationConfirm=true;publicationReviewed=false;NativeInvalidate();},300),()=>publicationHelperConnected&&InMenu&&!PublishingBusy&&PublicationPlanMatches());b.End();
    b.BeginColumn(6);publicationStatusRoot=b.CurrentContainer;b.End();
   }
   b.End();b.End();RefreshPublicationUi();
  }
  private void BuildPublicationStatus(CustomUIBuilder b)
  {
   if(publicationWaiting){NativeLabel(b,publicationRequestAction=="login"?T("Открывается консоль входа SteamCMD…","Opening the SteamCMD sign-in console…"):T("Ожидание помощника…","Waiting for companion…"),20,Warning);return;}
   var state=publicationState;if(state==null||state.RequestId.Length==0||state.Draft?.ModId!=publicationModId)return;
   b.AddSeparator();NativeLabel(b,PublicationPhase(state.Phase),22,state.Phase=="error"||state.Phase=="review_required"?Warning:Mint);NativeLabel(b,state.Message,20);
   if(state.ErrorCode.Length>0)NativeLabel(b,state.ErrorCode,17,Warning);
   if(state.Files?.Count>0)NativeLabel(b,state.Files.Count+T(" файлов · "," files · ")+FormatBytes(state.TotalBytes),19);
   if(state.PublishedUrl.Length>0){NativeLabel(b,state.PublishedUrl,18);NativeButton(b,T("Копировать ссылку","Copy link"),()=>GUIUtility.systemCopyBuffer=state.PublishedUrl,250);}
   if(state.Phase=="review_required")NativeLabel(b,T("Результат отправки не подтверждён. Проверь страницу и журнал SteamCMD перед повтором, чтобы не создать дубликат.","The submission result is unconfirmed. Check the item and SteamCMD log before retrying to avoid duplicates."),19,Warning);
  }
  private void BuildPublicationConfirmation(CustomUIBuilder b)
  {
   var state=publicationState;if(!PublicationPlanMatches()){publicationConfirm=false;BuildPublishing(b);return;}
   b.AddHeader(T("Проверка перед отправкой","Review before submission"));b.BeginScrollFlexible();StyleScroll(b.CurrentContainer.GetComponentInParent<ScrollRect>());
   NativeLabel(b,state.Draft.Title,26,Mint);BuildPublicationPreview(b,state);NativeLabel(b,(state.Draft.PublishedFileId.Length==0?T("Создать новую страницу","Create a new item"):T("Обновить страницу ","Update item ")+state.Draft.PublishedFileId)+" · "+VisibilityName(state.Draft.Visibility),22,Warning);
   NativeLabel(b,T("Снимок пакета: ","Package snapshot: ")+state.PreparedId,17);NativeLabel(b,state.Draft.Description,20);NativeLabel(b,T("Превью: ","Preview: ")+state.Draft.PreviewPath,18);NativeLabel(b,T("Обновление: ","Change note: ")+state.Draft.ChangeNote,19);b.AddSeparator();
   NativeLabel(b,state.Files.Count+T(" файлов будут отправлены · "," files will be uploaded · ")+FormatBytes(state.TotalBytes),22,Mint);
   publicationFilePage=Mathf.Clamp(publicationFilePage,0,Math.Max(0,(state.Files.Count-1)/80));foreach(var file in state.Files.Skip(publicationFilePage*80).Take(80))NativeLabel(b,file.Path+" · "+FormatBytes(file.Bytes)+" · SHA256 "+file.Sha256,17);
   if(state.Files.Count>80){b.BeginRow(10);NativeButton(b,"←",()=>{publicationFilePage=Math.Max(0,publicationFilePage-1);NativeInvalidate();},70,publicationFilePage>0);NativeLabel(b,(publicationFilePage+1)+" / "+Math.Max(1,(state.Files.Count+79)/80),18);NativeButton(b,"→",()=>{publicationFilePage++;NativeInvalidate();},70,(publicationFilePage+1)*80<state.Files.Count);b.End();}
   b.AddSeparator();b.AddToggle(T("Я проверил файлы, страницу и видимость","I reviewed the files, item and visibility"),publicationReviewed,value=>{publicationReviewed=value;NativeInvalidate();});PublicationControl(b.Last,()=>!PublishingBusy);
   if(!state.SteamCmdAvailable)NativeLabel(b,T("SteamCMD не установлен у помощника. Подготовленный пакет сохранён; отправка пока недоступна.","The companion has no SteamCMD installation. The prepared package is saved; submission is unavailable."),20,Warning);
   if(string.IsNullOrWhiteSpace(state.Draft.SteamAccount))NativeLabel(b,T("Для отправки укажи логин SteamCMD в форме и подготовь пакет снова. Для просмотра пакета вход не нужен.","To submit, enter the SteamCMD account name in the form and prepare again. Reviewing a package does not require sign-in."),20,Warning);
   b.End();b.BeginRow(12,TextAnchor.MiddleRight);b.AddFlexibleSpace();NativeButton(b,T("Назад","Back"),()=>{publicationConfirm=false;NativeInvalidate();},220);
   PublicationControl(NativeButton(b,state.Draft.PublishedFileId.Length==0?T("Создать и отправить","Create and submit"):T("Отправить обновление","Submit update"),()=>SendPublication("publish"),320),()=>publicationHelperConnected&&publicationReviewed&&PublicationPlanMatches()&&publicationState.SteamCmdAvailable&&!string.IsNullOrWhiteSpace(publicationState.Draft.SteamAccount)&&InMenu&&!PublishingBusy);b.End();
  }
  private string PublicationPhase(string phase)
  {
   switch(phase){case "inspecting":return T("Поиск сохранённой привязки","Finding saved binding");case "refreshing_tags":return T("Обновление тегов Steam","Refreshing Steam tags");case "preparing":return T("Подготовка снимка","Preparing snapshot");case "ready":return T("Пакет готов к проверке","Package ready for review");case "selecting_preview":return T("Выбор изображения у помощника","Choose an image in the companion");case "authenticating":return T("Открыта консоль входа SteamCMD","SteamCMD sign-in console is open");case "running":return T("Работает SteamCMD","SteamCMD is running");case "success":return T("Отправка подтверждена SteamCMD","SteamCMD confirmed submission");case "error":return T("Не удалось выполнить операцию","Operation failed");case "review_required":return T("Нужно проверить результат вручную","Manual result review required");default:return T("Публикация","Publishing");}
  }
  private string VisibilityName(int value)=>value==0?T("Для всех","Public"):value==1?T("Для друзей","Friends only"):value==3?T("По ссылке","Unlisted"):T("Личный","Private");
  private void BuildPublicationPreview(CustomUIBuilder b,PublicationState state)
  {
   if(string.IsNullOrEmpty(state.PreviewSnapshotPath)||string.IsNullOrEmpty(state.PreviewSha256))return;
   if(publicationPreviewKey!=state.PreviewSha256)
   {
    DisposePublicationPreview();
    try
    {
     var path=System.IO.Path.GetFullPath(state.PreviewSnapshotPath);var root=System.IO.Path.GetFullPath(System.IO.Path.Combine(Storage.Root,"publishing","staging"))+System.IO.Path.DirectorySeparatorChar;
     if(!path.StartsWith(root,StringComparison.OrdinalIgnoreCase)||!System.IO.File.Exists(path)||new System.IO.FileInfo(path).Length>1024*1024)return;
     var bytes=System.IO.File.ReadAllBytes(path);using(var sha=System.Security.Cryptography.SHA256.Create())if(BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","")!=state.PreviewSha256)return;
     publicationPreviewTexture=new Texture2D(2,2,TextureFormat.RGBA32,false);
     if(!publicationPreviewTexture.LoadImage(bytes)){DisposePublicationPreview();return;}
     publicationPreviewSprite=Sprite.Create(publicationPreviewTexture,new Rect(0,0,publicationPreviewTexture.width,publicationPreviewTexture.height),new Vector2(.5f,.5f));publicationPreviewKey=state.PreviewSha256;
    }
    catch{DisposePublicationPreview();}
   }
   if(publicationPreviewSprite!=null){b.AddImage(publicationPreviewSprite,-1,220);if(b.Last.image!=null){b.Last.image.preserveAspect=true;b.Last.image.raycastTarget=false;}}
  }
  private void DisposePublicationPreview(){if(publicationPreviewSprite!=null)Destroy(publicationPreviewSprite);if(publicationPreviewTexture!=null)Destroy(publicationPreviewTexture);publicationPreviewSprite=null;publicationPreviewTexture=null;publicationPreviewKey="";}
  private static string FormatBytes(long value)=>value>=1024*1024?(value/(1024d*1024)).ToString("0.0")+" MB":value>=1024?(value/1024d).ToString("0.0")+" KB":value+" B";
 }
}
