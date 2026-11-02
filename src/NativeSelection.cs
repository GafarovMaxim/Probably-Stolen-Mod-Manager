using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace ProbablyModManager
{
 public sealed class NativeContextTarget:MonoBehaviour,IPointerClickHandler
 {
  public Action<Vector2> Open;
  public void OnPointerClick(PointerEventData e){if(e.button==PointerEventData.InputButton.Right)Open?.Invoke(e.position);}
 }
 [DefaultExecutionOrder(10000)]
 public sealed class NativeScrollbarGrip:MonoBehaviour
 {
  public Scrollbar Bar;
  private void LateUpdate(){if(Bar!=null&&Bar.size<.08f)Bar.size=.08f;}
 }
 public sealed partial class Manager
 {
  private string selectionAnchor="";
  private GameObject contextOverlay;
  private bool ControlHeld=>Input.GetKey(KeyCode.LeftControl)||Input.GetKey(KeyCode.RightControl);
  private bool ShiftHeld=>Input.GetKey(KeyCode.LeftShift)||Input.GetKey(KeyCode.RightShift);
  private void HighlightSelection(){foreach(var row in nativeRows.Where(r=>r!=null))row.SetSelected(selectedRows.Contains(row.ModId));}
  private void SelectRows(string id,bool ctrl,bool shift)
  {
   var clicked=nativeRows.FirstOrDefault(r=>r!=null&&r.ModId==id);
   var visibleRows=nativeRows.Where(r=>r!=null&&r.gameObject.activeInHierarchy&&(clicked==null||r.Column==clicked.Column)).Select(r=>r.ModId).ToList();
   int anchor=visibleRows.IndexOf(selectionAnchor),index=visibleRows.IndexOf(id);
   if(shift&&anchor>=0&&index>=0){if(!ctrl)selectedRows.Clear();foreach(var item in visibleRows.Skip(Math.Min(anchor,index)).Take(Math.Abs(index-anchor)+1))selectedRows.Add(item);}
   else if(ctrl){if(!selectedRows.Add(id))selectedRows.Remove(id);selectionAnchor=id;}
   else{selectedRows.Clear();selectedRows.Add(id);selectionAnchor=id;}
   Select(id);HighlightSelection();detailDirty=true;
  }
  private void SelectAllRows(){selectedRows.Clear();foreach(var row in nativeRows.Where(r=>r!=null&&r.gameObject.activeInHierarchy))selectedRows.Add(row.ModId);HighlightSelection();detailDirty=true;}
  public void NativeRowContext(string id,Vector2 point)
  {
   Act(()=>{if(!selectedRows.Contains(id))SelectRows(id,false,false);Select(id);var ids=selectedRows.ToList();
    ShowContext(point,new[]{
     new KeyValuePair<string,Action>(T("Включить выбранные","Enable selected")+" ("+ids.Count+")",()=>NativeSetSelection(ids,true)),
     new KeyValuePair<string,Action>(T("Выключить выбранные","Disable selected")+" ("+ids.Count+")",()=>NativeSetSelection(ids,false)),
     new KeyValuePair<string,Action>(T("Метка: на проверке","Tag: to review"),()=>{foreach(var item in ids)data.ModTags[item]=Planner.CleanTags(GetTags(item).Concat(new[]{"review"}));Save();NativeInvalidate();}),
     new KeyValuePair<string,Action>(T("Выбрать всё видимое","Select all visible"),SelectAllRows),
     new KeyValuePair<string,Action>(T("Снять выделение","Clear selection"),()=>{selectedRows.Clear();HighlightSelection();detailDirty=true;}),
     new KeyValuePair<string,Action>(T("Чейнджлог Steam","Steam change notes"),()=>OpenChangelog(id)),
     new KeyValuePair<string,Action>(T("Копировать ID","Copy IDs"),()=>GUIUtility.systemCopyBuffer=string.Join("\n",ids))});});
  }
  private void NativeSetSelection(IEnumerable<string> source,bool enable)
  {
   if(!InMenu)return;var ids=source.Where(id=>id!=Id).Distinct().ToList();var order=EnabledOrder();
   if(enable){NativeApplyOrder(Planner.WithDependencies(mods,order,ids));return;}
   var remove=new HashSet<string>(ids.SelectMany(id=>Planner.Dependants(mods,order,id)));
   if(remove.Contains(Id)){toast=T("Этот набор нужен самому менеджеру. Измени его через штатное меню.","The manager depends on this selection. Change it using the original menu.");return;}
   var next=order.Where(id=>!remove.Contains(id)).ToList();
   if(remove.Except(ids).Any())
   {confirmationText=T("Также будут выключены зависимые моды:\n\n","Dependent mods will also be disabled:\n\n")+string.Join("\n",remove.Select(id=>mods.FirstOrDefault(m=>m.Id==id)?.Name??id));nativeConfirmation=()=>NativeApplyOrder(next);NativeInvalidate();}
   else NativeApplyOrder(next);
  }
  private void CloseContext(){nativeDropdownClosed?.Invoke();nativeDropdownClosed=null;if(contextOverlay!=null){contextOverlay.SetActive(false);Destroy(contextOverlay);contextOverlay=null;}}
  private void ShowContext(Vector2 point,IEnumerable<KeyValuePair<string,Action>> entries)
  {
   CloseContext();var items=entries.ToList();contextOverlay=new GameObject("PMM context menu",typeof(RectTransform),typeof(Image),typeof(Button));var root=(RectTransform)contextOverlay.transform;root.SetParent(nativeWindow.root.transform.parent,false);root.anchorMin=Vector2.zero;root.anchorMax=Vector2.one;root.offsetMin=root.offsetMax=Vector2.zero;contextOverlay.GetComponent<Image>().color=Color.clear;contextOverlay.GetComponent<Button>().onClick.AddListener(CloseContext);
   var panel=new GameObject("popup",typeof(RectTransform),typeof(Image),typeof(Outline));var rect=(RectTransform)panel.transform;rect.SetParent(root,false);rect.anchorMin=rect.anchorMax=rect.pivot=new Vector2(0,1);rect.sizeDelta=new Vector2(390,items.Count*46+20);
   RectTransformUtility.ScreenPointToLocalPointInRectangle(root,point,nativeWindow.root.GetComponentInParent<Canvas>().renderMode==RenderMode.ScreenSpaceOverlay?null:nativeWindow.root.GetComponentInParent<Canvas>().worldCamera,out var local);
   rect.anchoredPosition=new Vector2(Mathf.Clamp(local.x-root.rect.xMin,0,Mathf.Max(0,root.rect.width-rect.sizeDelta.x)),Mathf.Clamp(local.y-root.rect.yMax,-Mathf.Max(0,root.rect.height-rect.sizeDelta.y),0));
   panel.GetComponent<Image>().color=new Color32(41,35,52,255);panel.GetComponent<Outline>().effectColor=Mint;panel.GetComponent<Outline>().effectDistance=new Vector2(1,-1);
   var b=CustomUIManager.Instance.BuildInto(rect,true);var layout=rect.GetComponent<VerticalLayoutGroup>();if(layout!=null){layout.padding=new RectOffset(10,10,10,10);layout.spacing=6;}
   foreach(var item in items){var action=item.Value;NativeButton(b,item.Key,()=>{CloseContext();action();});}root.SetAsLastSibling();CustomUIBuilder.RebuildLayout(rect);
  }
  private void ProfileContext(Profile profile,Vector2 point)
  {
   ShowContext(point,new[]{new KeyValuePair<string,Action>(T("Активировать профиль","Activate profile"),()=>NativeActivateProfile(profile)),
    new KeyValuePair<string,Action>(T("Дублировать","Duplicate"),()=>{if(data.Profiles.Count>=100){toast="Profile limit: 100";return;}string name=profile.Name+" (copy)";while(data.Profiles.Any(p=>p.Name==name))name+=" copy";data.Profiles.Add(new Profile{Name=name,GameVersion=profile.GameVersion,Order=profile.Order.ToList(),Versions=new Dictionary<string,string>(profile.Versions)});profileIndex=data.Profiles.Count-1;Save();NativeInvalidate();}),
    new KeyValuePair<string,Action>(T("Экспорт JSON","Export JSON"),()=>{reportTitle=profile.Name;report=Newtonsoft.Json.JsonConvert.SerializeObject(profile,Newtonsoft.Json.Formatting.Indented);NativeInvalidate();}),
    new KeyValuePair<string,Action>(T("Удалить профиль","Delete profile"),()=>DeleteProfile(profile))});
  }
  private void DeleteProfile(Profile profile){data.Profiles.Remove(profile);if(string.Equals(data.ActiveProfileName,profile.Name,StringComparison.Ordinal))data.ActiveProfileName="";if(pendingProfile==profile){pendingProfile=null;pendingProfileOrder=null;}profileIndex=Mathf.Clamp(profileIndex,0,Math.Max(0,data.Profiles.Count-1));Save();NativeInvalidate();toast=T("Профиль удалён; активный набор не изменён","Profile deleted; active selection unchanged");}
 }
}
