using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ProbablyModManager
{
 public sealed partial class Manager
 {
  private Action uiAction,nativeConfirmation;
  private CustomUIWindow nativeWindow;
  private RectTransform nativeBody,nativeDetail;
  private CustomUIElement nativeStatus,nativeRestartWarning,nativeRestartButton;
  private bool nativeDirty,detailDirty;
  private string nativeTagDraft="",confirmationText="";
  private Vector2 detailScroll;
  private float nativeWidth=1440,nativeHeight=640;
  private readonly List<NativeModRow> nativeRows=new List<NativeModRow>();
  private readonly Dictionary<bool,ScrollRect> nativeScrolls=new Dictionary<bool,ScrollRect>();
  private NativeModRow nativeDragging;
  private float nativeDumpAt=-1;
  private int nativeRenderedTab=-1,nativeRestoreFrame;
  private bool nativeRenderedDual;
  private readonly List<KeyValuePair<ScrollRect,float>> nativeRestoreScroll=new List<KeyValuePair<ScrollRect,float>>();
  private static readonly Color Mint=new Color32(138,191,187,255),Muted=new Color32(196,196,176,255),Warning=new Color32(242,196,119,255);
  private void Act(Action action){uiAction=action;}
  private void NativeInvalidate(){nativeDirty=true;}
  private CustomUIBuilder Into(RectTransform root){CustomUIBuilder.ClearContainer(root);return CustomUIManager.Instance.BuildInto(root,true);}
  private static void Size(RectTransform root,float width=-1,float height=-1,float flexWidth=0,float flexHeight=0)
  {var l=root.GetComponent<LayoutElement>()??root.gameObject.AddComponent<LayoutElement>();l.ignoreLayout=false;if(width>0)l.minWidth=l.preferredWidth=width;if(height>0)l.minHeight=l.preferredHeight=height;l.flexibleWidth=flexWidth;l.flexibleHeight=flexHeight;}
  private CustomUIElement NativeButton(CustomUIBuilder b,string text,Action action,float width=-1,bool enabled=true)
  {
   b.AddButton(text,()=>Act(action));var el=b.Last;el.SetPreferredSize(width,40).SetFlexible(width>0?0:1,0).SetInteractable(enabled);if(width>0)el.SetFixedWidth(width);
   if(el.text!=null){el.text.enableWordWrapping=false;el.text.enableAutoSizing=true;el.text.fontSizeMin=16;el.text.fontSizeMax=24;el.text.overflowMode=TextOverflowModes.Ellipsis;el.text.richText=false;}return el;
  }
  private CustomUIElement NativeLabel(CustomUIBuilder b,string text,int fontSize=21,Color? color=null)
  {b.AddLabel(text);var el=b.Last;if(el.text!=null){el.text.fontSize=fontSize;el.text.enableAutoSizing=false;el.text.enableWordWrapping=true;el.text.richText=false;el.text.color=color??Muted;}return el;}
  private CustomUIElement NativeInput(CustomUIBuilder b,string placeholder,string value,Action<string> changed,bool multiline=false)
  {b.AddInput(placeholder,value,null,null,changed);var el=b.Last;el.SetPreferredSize(-1,multiline?120:40).SetFlexible(1,0);if(el.input!=null){el.input.lineType=multiline?TMP_InputField.LineType.MultiLineNewline:TMP_InputField.LineType.SingleLine;el.input.characterLimit=multiline?500000:250;}return el;}
  private void NativeShow()
  {
   var manager=CustomUIManager.Instance;if(manager==null){toast="CustomUIManager unavailable";return;}
   if(nativeWindow==null||!nativeWindow.IsAlive)
   {
    var b=manager.CreateWindow("probably_mod_manager",T("Модификации","Modding"),InMenu?"menu":"meta",manager.optionUIPrefab,CustomUIManager.Style.Meta).SetCloseOnEscape();nativeWindow=b.Handle;
    // The options prefab has a full-screen root and a separately sized frame panel.
    // Resize the panel, not the stretched overlay root.
    nativeWindow.Rect.anchorMin=Vector2.zero;nativeWindow.Rect.anchorMax=Vector2.one;nativeWindow.Rect.offsetMin=nativeWindow.Rect.offsetMax=Vector2.zero;
    var framePanel=nativeWindow.ContentRoot.parent as RectTransform;
    if(framePanel!=null)
    {
     framePanel.anchorMin=framePanel.anchorMax=new Vector2(.5f,.5f);framePanel.pivot=new Vector2(.5f,.5f);framePanel.sizeDelta=new Vector2(1580,960);framePanel.anchoredPosition=Vector2.zero;
     var frameImage=framePanel.GetComponent<Image>();
     var vanillaFrame=ModUIManager.Instance?.panel?.GetComponentsInChildren<Image>(true).Where(img=>img.sprite!=null&&img.rectTransform.rect.width>900&&img.rectTransform.rect.height>700).OrderByDescending(img=>img.rectTransform.rect.width).FirstOrDefault();
     if(frameImage!=null&&vanillaFrame!=null){frameImage.sprite=vanillaFrame.sprite;frameImage.type=vanillaFrame.type;frameImage.color=vanillaFrame.color;}
    }
    b.OnClose(()=>{visible=false;if(blocker!=null)blocker.SetActive(false);});
    b.BeginRow(8,TextAnchor.MiddleLeft);var labels=new[]{T("Моды","Mods"),T("Профили","Profiles"),T("Журнал","Log"),T("Публикация","Publish")};
    for(int i=0;i<labels.Length;i++){int page=i;NativeButton(b,labels[i],()=>{tab=page;if(page!=3){publicationConfirm=false;publicationChoosingTags=false;}NativeInvalidate();},155);}
    b.AddFlexibleSpace();NativeButton(b,Russian?"EN":"RU",()=>{data.Russian=!data.Russian;Save();NativeRecreate();},60);NativeButton(b,T("Штатное меню","Original menu"),OpenNative,205);b.End();b.AddSeparator(null,3);
    b.BeginColumn(6);nativeBody=b.CurrentContainer;Size(nativeBody,-1,-1,1,1);b.End();b.AddSeparator(null,3);
    b.BeginRow(10,TextAnchor.MiddleLeft);nativeRestartWarning=NativeLabel(b,"",20,Warning);nativeRestartWarning.SetFlexible(1,0);b.End();
    b.BeginRow(10,TextAnchor.MiddleLeft);nativeStatus=NativeLabel(b,"Probably Mod Manager 0.2.6",18);nativeStatus.SetFlexible(1,0);nativeRestartButton=NativeButton(b,T("Перезапустить игру","Restart game"),RequestManualRestart,245,InMenu);b.End();nativeWindow.Show();Canvas.ForceUpdateCanvases();nativeWidth=Mathf.Max(1100,nativeWindow.ContentRoot.rect.width);nativeHeight=Mathf.Max(480,nativeWindow.ContentRoot.rect.height-178);nativeDirty=true;
   }
   nativeWindow.Show();
   if(blocker==null||blocker.transform.parent!=nativeWindow.root.transform.parent)
   {if(blocker!=null)Destroy(blocker);blocker=new GameObject("PMM input backdrop",typeof(RectTransform),typeof(Image));blocker.transform.SetParent(nativeWindow.root.transform.parent,false);var r=(RectTransform)blocker.transform;r.anchorMin=Vector2.zero;r.anchorMax=Vector2.one;r.offsetMin=r.offsetMax=Vector2.zero;blocker.GetComponent<Image>().color=new Color(0,0,0,.14f);}
   blocker.SetActive(true);blocker.transform.SetSiblingIndex(nativeWindow.root.transform.GetSiblingIndex());nativeWindow.BringToFront();
  }
  private void NativeHide(){CloseContext();changesWindow?.Hide();nativeWindow?.Hide();if(blocker!=null)blocker.SetActive(false);}
  private void NativeRecreate(){if(nativeWindow!=null&&nativeWindow.IsAlive){nativeWindow.onClose=null;nativeWindow.Close();}nativeWindow=null;NativeShow();}
  private void NativeTick()
  {
   if(!visible)return;if(nativeWindow==null||!nativeWindow.IsAlive)NativeShow();if(nativeBody==null)return;
   if(tab==2&&nativeLogSearchAt>=0&&Time.unscaledTime>=nativeLogSearchAt){nativeLogSearchAt=-1;RefreshJournalList(true);}
   bool typing=UnityEngine.EventSystems.EventSystem.current!=null&&UnityEngine.EventSystems.EventSystem.current.currentSelectedGameObject!=null&&UnityEngine.EventSystems.EventSystem.current.currentSelectedGameObject.GetComponent<TMP_InputField>()!=null;
   if(tab==2&&!typing&&!journalPaused&&journal.Total!=nativeLogTotal&&Time.unscaledTime>=nativeNextLogRefresh){nativeNextLogRefresh=Time.unscaledTime+2;RefreshJournalList();}
   UpdateJournalRepeats();TickPreview();if(!typing&&tab==0&&ControlHeld&&Input.GetKeyDown(KeyCode.A))SelectAllRows();if(contextOverlay!=null&&Input.GetKeyDown(KeyCode.Escape))CloseContext();
   if(nativeDirty&&nativeDragging==null)
   {
    var offsets=nativeRenderedTab==tab&&nativeRenderedDual==dual?nativeBody.GetComponentsInChildren<ScrollRect>(false).Where(s=>s.gameObject.activeInHierarchy).Select(s=>s.verticalNormalizedPosition).ToArray():Array.Empty<float>();
    CloseContext();nativeDirty=false;nativeRows.Clear();nativeScrolls.Clear();nativeDetail=null;journalListRoot=null;journalDetailRoot=null;journalRepeats=null;nativePreview=null;var b=Into(nativeBody);
    if(applying)BuildApproval(b);else if(report.Length>0)BuildReport(b);else if(nativeConfirmation!=null)BuildConfirmation(b);else if(quickProfileDialog)BuildQuickProfileDialog(b);else switch(tab){case 0:BuildMods(b);break;case 1:BuildProfiles(b);break;case 2:BuildJournal(b);break;case 3:BuildPublishing(b);break;default:tab=0;BuildMods(b);break;}
    foreach(var styled in nativeBody.GetComponentsInChildren<ScrollRect>(true))StyleScroll(styled);CustomUIBuilder.RebuildLayout(nativeWindow.ContentRoot);nativeDumpAt=Time.unscaledTime+1;
    nativeRestoreScroll.Clear();var scrolls=nativeBody.GetComponentsInChildren<ScrollRect>(false).Where(s=>s.gameObject.activeInHierarchy).ToArray();for(int i=0;i<Math.Min(offsets.Length,scrolls.Length);i++)nativeRestoreScroll.Add(new KeyValuePair<ScrollRect,float>(scrolls[i],offsets[i]));nativeRestoreFrame=Time.frameCount+2;nativeRenderedTab=tab;nativeRenderedDual=dual;
   }
   if(detailDirty&&nativeDetail!=null&&nativeDragging==null){detailDirty=false;BuildDetail(Into(nativeDetail));CustomUIBuilder.RebuildLayout(nativeWindow.ContentRoot);}
   if(nativeDumpAt>=0&&Time.unscaledTime>=nativeDumpAt){nativeDumpAt=-1;NativeDump();}
   if(nativeRestoreScroll.Count>0&&Time.frameCount>=nativeRestoreFrame){foreach(var pair in nativeRestoreScroll)if(pair.Key!=null)pair.Key.verticalNormalizedPosition=pair.Value;nativeRestoreScroll.Clear();}
   if(nativeRestartWarning?.IsAlive==true)nativeRestartWarning.SetText(ModLoader.RestartRequired?T("Изменения сохранены. Перезапусти игру вручную, чтобы применить их.","Changes saved. Restart the game manually to apply them."):T("Двойной клик — вкл./выкл. • Ctrl/Shift — выделение · ПКМ — действия","Double-click to enable/disable • Ctrl/Shift: select · Right-click: actions"));
   if(nativeRestartButton?.IsAlive==true){nativeRestartButton.SetVisible(!nativeRestartStarting&&!publicationConfirm);nativeRestartButton.SetInteractable(InMenu&&!applying&&!nativeRestartStarting&&!PublishingBusy&&Application.platform==RuntimePlatform.WindowsPlayer);}
   if(nativeStatus?.IsAlive==true)nativeStatus.SetText(journal.Error.Length>0?"Log: "+journal.Error:toast.Length>0?toast:"Probably Mod Manager 0.2.6 · "+PlayerStore.VERSION+" / API "+Application.version);
  }
  private void BuildMods(CustomUIBuilder b)
  {
   // Fixed controls total 1190 px; six 8 px gaps leave 212 px of room in a 1450 px row.
   b.BeginRow(8,TextAnchor.MiddleLeft);NativeButton(b,dual?T("Два списка","Two lists"):T("Общий список","Single list"),()=>{dual=!dual;NativeInvalidate();},180);
   NativeSelect(b,new[]{T("Все моды","All mods"),T("С проблемами","With issues"),T("С метками","Tagged")},modFilter,value=>{modFilter=value;ApplyNativeFilter();},190);BuildQuickProfiles(b);b.AddFlexibleSpace();var counts=NativeLabel(b,mods.Count+T(" установлено / "," installed / ")+EnabledOrder().Count+T(" включено"," enabled"),18);counts.SetFixedWidth(220);if(counts.text!=null)counts.text.enableWordWrapping=false;NativeButton(b,T("Автосортировка","Auto-sort mods"),NativeAutoSort,260,InMenu);b.End();b.AddSpace(5);
   b.BeginRow(24);float leftWidth=nativeWidth*.59f;b.BeginColumn(8);Size(b.CurrentContainer,leftWidth,-1,0,1);b.BeginRow(12);
   if(dual){BuildModColumn(b,false,leftWidth/2-6);BuildModColumn(b,true,leftWidth/2-6);}else BuildModColumn(b,null,leftWidth);
   b.End();NativeLabel(b,T("Состояние списка сохраняется сразу. Разрешения модов проверяет игра.","List changes are saved immediately. The game checks mod capabilities."),18);b.End();
   b.BeginScrollFlexible();var scroll=b.CurrentContainer.GetComponentInParent<ScrollRect>();StyleScroll(scroll);Size(scroll.transform as RectTransform,nativeWidth-leftWidth-24,-1,0,1);b.BeginColumn(8);nativeDetail=b.CurrentContainer;BuildDetail(new CustomUIBuilder(CustomUIManager.Instance,b.Handle,nativeDetail));b.End();b.End();b.End();
  }
  private void BuildModColumn(CustomUIBuilder b,bool? enabled,float width)
  {
   b.BeginColumn(8);Size(b.CurrentContainer,width,-1,0,1);NativeLabel(b,enabled==null?T("ВСЕ МОДЫ","ALL MODS"):enabled.Value?T("АКТИВНЫЕ","ACTIVE"):T("НЕАКТИВНЫЕ","INACTIVE"),21,Mint);
   string query=enabled==null?searchAll:enabled.Value?searchOn:searchOff;NativeInput(b,T("Поиск по названию, ID, метке…","Search name, ID or tag…"),query,text=>{if(enabled==null)searchAll=text;else if(enabled.Value)searchOn=text;else searchOff=text;ApplyNativeFilter();});
   b.BeginPanel(-1,-1,TextAnchor.UpperLeft,4,0);Size(b.CurrentContainer,-1,-1,1,1);var drop=b.CurrentContainer.gameObject.AddComponent<NativeDropZone>();drop.Owner=this;drop.EnabledColumn=enabled;
   b.BeginScrollFlexible();var content=b.CurrentContainer;var scroll=content.GetComponentInParent<ScrollRect>();StyleScroll(scroll);if(enabled!=null)nativeScrolls[enabled.Value]=scroll;var group=content.GetComponent<VerticalLayoutGroup>();if(group!=null){group.spacing=5;group.childControlHeight=true;group.childForceExpandHeight=false;}
   IEnumerable<ModInfo> list=enabled==true?EnabledOrder().Select(id=>mods.FirstOrDefault(m=>m.Id==id)).Where(m=>m!=null):enabled==false?mods.Where(m=>!ModLoader.IsModEnabled(m.Id)):mods;
   foreach(var mod in list)BuildModRow(content,mod,enabled);b.End();b.End();b.End();ApplyNativeFilter();
  }
  private void BuildModRow(RectTransform parent,ModInfo mod,bool? column)
  {
   var gameUI=ModUIManager.Instance;if(gameUI==null||gameUI.modElementPrefab==null)return;var go=Instantiate(gameUI.modElementPrefab,parent);go.name="pmm_row_"+mod.Id;
   var old=go.GetComponent<ModElement>();var text=old!=null?old.modNameTMP:go.GetComponentInChildren<TextMeshProUGUI>();if(old!=null){old.enabled=false;Destroy(old);}var button=go.GetComponent<Button>();if(button!=null)button.onClick=new Button.ButtonClickedEvent();
   var group=go.GetComponent<HorizontalLayoutGroup>()??go.AddComponent<HorizontalLayoutGroup>();group.padding=new RectOffset(8,8,3,3);group.spacing=8;group.childControlWidth=true;group.childControlHeight=true;group.childForceExpandWidth=false;group.childForceExpandHeight=false;group.childAlignment=TextAnchor.MiddleLeft;
   if(text!=null){if(text.gameObject!=go){text.gameObject.SetActive(false);Destroy(text.gameObject);}else text.enabled=false;}
   var content=new GameObject("name_and_tags",typeof(RectTransform),typeof(VerticalLayoutGroup),typeof(LayoutElement));content.transform.SetParent(go.transform,false);var cg=content.GetComponent<VerticalLayoutGroup>();cg.childControlWidth=true;cg.childControlHeight=true;cg.childForceExpandWidth=true;cg.childForceExpandHeight=false;cg.spacing=1;content.GetComponent<LayoutElement>().flexibleWidth=1;
   var rowBuilder=CustomUIManager.Instance.BuildInto((RectTransform)content.transform,true);var title=NativeLabel(rowBuilder,mod.Name,22,Mint);title.SetPreferredSize(-1,30).SetFlexible(1,0);text=title.text;
   if(text!=null){text.enableWordWrapping=false;text.enableAutoSizing=true;text.fontSizeMin=16;text.fontSizeMax=22;text.overflowMode=TextOverflowModes.Ellipsis;text.alignment=TextAlignmentOptions.MidlineLeft;text.raycastTarget=false;text.maxVisibleCharacters=int.MaxValue;text.maxVisibleLines=int.MaxValue;
    if(text.font!=null&&!mod.Name.Any(c=>char.IsLetterOrDigit(c)&&text.font.HasCharacter(c,true,true)))text.text=mod.Id;
   }
   var tags=GetTags(mod.Id);if(tags.Count>0){var tagLabel=NativeLabel(rowBuilder,string.Join(" · ",tags.Select(TagLabel)),16,Warning);tagLabel.SetPreferredSize(-1,22).SetFlexible(1,0);if(tagLabel.text!=null){tagLabel.text.enableWordWrapping=false;tagLabel.text.overflowMode=TextOverflowModes.Ellipsis;tagLabel.text.raycastTarget=false;}}
   if(button!=null&&go.GetComponent<Image>()!=null)button.targetGraphic=go.GetComponent<Image>();
   var source=new GameObject("source",typeof(RectTransform),typeof(Image),typeof(LayoutElement));source.transform.SetParent(go.transform,false);source.transform.SetSiblingIndex(0);source.GetComponent<Image>().sprite=mod.Source=="Workshop"?CustomUIManager.Instance.spriteSteam:CustomUIManager.Instance.spriteFile;source.GetComponent<Image>().preserveAspect=true;source.GetComponent<Image>().raycastTarget=false;var sl=source.GetComponent<LayoutElement>();sl.minWidth=sl.preferredWidth=22;sl.minHeight=sl.preferredHeight=22;
   Size(go.transform as RectTransform,-1,tags.Count>0?59:39,1,0);var row=go.AddComponent<NativeModRow>();row.Owner=this;row.ModId=mod.Id;row.Column=column;row.Background=go.GetComponent<Image>();row.InitialColor=row.Background!=null?row.Background.color:Color.white;row.Text=text;row.SetSelected(selectedRows.Contains(mod.Id));nativeRows.Add(row);
  }
  private void ApplyNativeFilter()
  {
   foreach(var row in nativeRows.Where(r=>r!=null)){var m=mods.FirstOrDefault(x=>x.Id==row.ModId);if(m==null)continue;var query=row.Column==null?searchAll:row.Column.Value?searchOn:searchOff;var hay=m.Name+" "+m.Id+" "+m.Author+" "+string.Join(" ",GetTags(m.Id).Select(TagLabel));bool issue=m.LoadError.Length>0||m.GameVersions.Length==0||findings.Any(f=>f.ModId==m.Id);row.gameObject.SetActive(hay.IndexOf(query,StringComparison.OrdinalIgnoreCase)>=0&&(modFilter!=1||issue)&&(modFilter!=2||GetTags(m.Id).Count>0));}
  }
  private void BuildDetail(CustomUIBuilder b)
  {
   var mod=mods.FirstOrDefault(m=>m.Id==selectedId);if(mod==null){NativeLabel(b,T("Выбери мод в списке","Select a mod"));return;}
   b.AddHeader(mod.Name);if(b.Last.text!=null){b.Last.text.fontSize=27;b.Last.text.richText=false;b.Last.text.enableWordWrapping=true;}BuildPreview(b,mod);NativeLabel(b,mod.Id,17);NativeLabel(b,T("Автор: ","Author: ")+mod.Author+"\n"+T("Версия: ","Version: ")+mod.Version,21);
   NativeLabel(b,T("Сейчас: ","Now: ")+(mod.Loaded?T("загружен","loaded"):T("не загружен","not loaded"))+" · "+T("В списке: ","Selected: ")+(ModLoader.IsModEnabled(mod.Id)?T("активен","enabled"):T("выключен","disabled")),20,Mint);
   b.BeginRow(8);NativeButton(b,T("Настройки / MCM","Settings / MCM"),OpenSettings,220);NativeButton(b,T("События мода","Mod events"),()=>{tab=2;logSearch=mod.Name;previousSession=false;NativeInvalidate();},200);b.End();
   if(mod.Source=="Local")NativeButton(b,T("Опубликовать в Steam…","Publish to Steam…"),()=>{SelectPublicationMod(mod.Id);tab=3;NativeInvalidate();},-1,InMenu&&!PublishingBusy);
   if(!string.IsNullOrEmpty(mod.LoadError))NativeLabel(b,mod.LoadError,20,Warning);
   if(ModLoader.IsModEnabled(mod.Id)&&!mod.Loaded)NativeButton(b,T("Проверить активацию","Review activation"),()=>{PrepareApply(true);NativeInvalidate();},290,InMenu);
   if(selectedRows.Count>1){NativeLabel(b,T("Выделено модов: ","Selected mods: ")+selectedRows.Count,21,Warning);NativeLabel(b,T("ПКМ — действия с группой. Перетаскивание переносит всю группу.","Right-click for group actions. Drag moves the whole group."),18);}
   NativeButton(b,T("Открыть страницу мода в Steam","Open mod page in Steam"),()=>OpenWorkshopPage(mod.Id),-1,WorkshopPageId(mod.Id)!=0);
   NativeButton(b,T("История обновлений · Steam","Change notes · Steam"),()=>OpenChangelog(mod.Id),-1,WorkshopId(mod.Id)!=0);b.AddSeparator();NativeLabel(b,FormatDescription(mod.Description),21);NativeLabel(b,T("Требует: ","Requires: ")+(mod.Dependencies.Length>0?string.Join(", ",mod.Dependencies):"—"),19);NativeLabel(b,T("Нужен для: ","Required by: ")+string.Join(", ",mods.Where(m=>m.Dependencies.Contains(mod.Id)).Select(m=>m.Name)),19);NativeLabel(b,T("Версии игры: ","Game versions: ")+(mod.GameVersions.Length==0?T("не указаны","unspecified"):string.Join(", ",mod.GameVersions)),19);
   b.AddSeparator();NativeLabel(b,T("МЕТКИ","TAGS"),21,Mint);BuildTagChips(b,GetTags(mod.Id),nativeWidth*.38f,true,mod.Id);b.BeginRow(8);NativeInput(b,T("Новая метка…","New tag…"),nativeTagDraft,value=>nativeTagDraft=value);NativeButton(b,T("Добавить","Add"),()=>AddTag(mod.Id,nativeTagDraft),145);b.End();
   b.BeginRow(8);foreach(var tag in new[]{"review","library","ui"}){string key=tag;NativeButton(b,TagLabel(tag),()=>AddTag(mod.Id,key),145);}b.End();NativeLabel(b,T("Заметка","Note"),19);NativeInput(b,T("Личная заметка…","Personal note…"),draftNote,value=>draftNote=value,true);NativeButton(b,T("Сохранить заметку","Save note"),()=>{data.Notes[mod.Id]=draftNote;Save();toast=T("Заметка сохранена","Note saved");},270);b.AddSeparator();NativeLabel(b,T("Разрешения: ","Capabilities: ")+(mod.Capabilities.Length==0?"—":string.Join(", ",mod.Capabilities.Select(ModCapability.DisplayName))),19);
  }
  private static string FormatDescription(string text)=>System.Text.RegularExpressions.Regex.Replace(text??"",@"\[/?(?:h[1-3]|b|i|u|list|\*|url(?:=[^\]]*)?)\]","");
  private string TagLabel(string value)=>value=="review"?T("На проверке","To review"):value=="library"?T("Библиотека","Library"):value=="ui"?T("Интерфейс","Interface"):value;
  private List<string> GetTags(string id)=>data.ModTags.TryGetValue(id,out var tags)?tags:new List<string>();
  private void AddTag(string id,string tag)
  {
   var additions=(tag??"").Split(new[]{',',';'},StringSplitOptions.RemoveEmptyEntries).Select(t=>t.Trim()).Select(t=>new[]{"review","library","ui"}.FirstOrDefault(key=>TagLabel(key).Equals(t,StringComparison.OrdinalIgnoreCase))??t);
   data.ModTags[id]=Planner.CleanTags(GetTags(id).Concat(additions));nativeTagDraft="";Save();NativeInvalidate();
  }
  private void BuildTagChips(CustomUIBuilder b,IEnumerable<string> source,float width,bool removable,string id="")
  {
   var tags=source.ToList();if(tags.Count==0){NativeLabel(b,T("Пока нет меток","No tags yet"),18);return;}float used=0;b.BeginRow(6);
   foreach(var tag in tags){float size=Mathf.Clamp(TagLabel(tag).Length*12+44,90,240);if(used+size>width&&used>0){b.End();b.BeginRow(6);used=0;}string captured=tag;if(removable)NativeButton(b,TagLabel(tag)+" ×",()=>{data.ModTags[id]=GetTags(id).Where(t=>!t.Equals(captured,StringComparison.OrdinalIgnoreCase)).ToList();Save();NativeInvalidate();},size);else NativeLabel(b,TagLabel(tag),17,Warning).SetFixedWidth(size);used+=size+6;}b.End();
  }
  private static void StyleScroll(ScrollRect scroll)
  {
   if(scroll==null)return;scroll.horizontal=false;scroll.scrollSensitivity=38;
   if(scroll.verticalScrollbar!=null){scroll.verticalScrollbarVisibility=ScrollRect.ScrollbarVisibility.Permanent;StyleScrollbar(scroll.verticalScrollbar);}
  }
  private static void StyleScrollbar(Scrollbar bar)
  {
   if(bar==null||bar.GetComponent<NativeScrollbarGrip>()!=null)return;
   var grip=bar.gameObject.AddComponent<NativeScrollbarGrip>();grip.Bar=bar;
   var rail=bar.GetComponent<Image>();if(rail!=null){rail.sprite=null;rail.color=new Color32(28,25,36,255);rail.raycastTarget=true;var outline=rail.gameObject.GetComponent<Outline>()??rail.gameObject.AddComponent<Outline>();outline.effectColor=new Color32(83,113,113,255);outline.effectDistance=new Vector2(1,-1);}
   var rect=bar.transform as RectTransform;if(rect!=null)rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal,16);
   if(bar.targetGraphic is Image knob)
   {
    knob.sprite=null;knob.color=Color.white;var colors=bar.colors;colors.normalColor=Mint;colors.highlightedColor=new Color32(182,227,218,255);colors.pressedColor=new Color32(240,201,129,255);colors.selectedColor=Mint;colors.disabledColor=new Color32(71,86,88,255);colors.fadeDuration=.12f;bar.colors=colors;bar.transition=UnityEngine.UI.Selectable.Transition.ColorTint;
    for(int i=-1;i<=1;i++){var line=new GameObject("grip",typeof(RectTransform),typeof(Image));var r=(RectTransform)line.transform;r.SetParent(knob.transform,false);r.anchorMin=r.anchorMax=r.pivot=new Vector2(.5f,.5f);r.sizeDelta=new Vector2(8,1.5f);r.anchoredPosition=new Vector2(0,i*4);line.GetComponent<Image>().color=new Color32(47,67,70,255);line.GetComponent<Image>().raycastTarget=false;}
   }
  }
  private void NativeDump()
  {
   try{var s=new System.Text.StringBuilder();foreach(var r in nativeWindow.root.GetComponentsInChildren<RectTransform>(true).Take(35))s.AppendLine(r.name+" parent="+r.parent?.name+" rect="+r.rect+" anchors="+r.anchorMin+"/"+r.anchorMax+" delta="+r.sizeDelta+" scale="+r.localScale);
   foreach(var row in nativeRows.Where(r=>r!=null).Take(3)){s.AppendLine("ROW "+row.ModId+" rect="+((RectTransform)row.transform).rect);foreach(var t in row.GetComponentsInChildren<TMP_Text>(true))s.AppendLine("TEXT '"+t.text+"' rect="+t.rectTransform.rect+" font="+t.font?.name+" size="+t.fontSize+" color="+t.color+" culled="+t.canvasRenderer.cull+" local="+t.rectTransform.anchoredPosition+" active="+t.gameObject.activeInHierarchy+" parent="+t.transform.parent.name+" parentRect="+((RectTransform)t.transform.parent).rect+" ignore="+t.GetComponent<LayoutElement>()?.ignoreLayout);}
   foreach(var caption in nativeWindow.root.GetComponentsInChildren<TMP_Text>(false).Where(t=>t.gameObject.name=="PMM dropdown caption")){caption.ForceMeshUpdate();s.AppendLine("DROPDOWN '"+caption.text+"' rect="+caption.rectTransform.rect+" font="+caption.font?.name+" size="+caption.fontSize+" visibleGlyphs="+caption.textInfo.characterInfo.Take(caption.textInfo.characterCount).Count(c=>c.isVisible)+" culled="+caption.canvasRenderer.cull+" alpha="+caption.canvasRenderer.GetAlpha());}
   Storage.WriteText("native-layout.txt",s.ToString());}catch(Exception e){journal.Add("warning","Layout diagnostics: "+e.Message);}
  }
 }
}
