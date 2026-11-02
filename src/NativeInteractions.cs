using System;
using System.Linq;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ProbablyModManager
{
 public sealed class NativeModRow:MonoBehaviour,IPointerClickHandler,IBeginDragHandler,IDragHandler,IEndDragHandler
 {
  public Manager Owner;public string ModId;public bool? Column;public Image Background;public Color InitialColor;public TMP_Text Text;
  private Transform originalParent;private int originalIndex;private Canvas canvas;private CanvasGroup group;private GameObject placeholder;private bool dragging;private float ignoreClickUntil;private PointerEventData lastEvent;
  public void SetSelected(bool value){if(Background!=null)Background.color=value?new Color32(70,101,103,255):InitialColor;}
  public void OnPointerClick(PointerEventData e){if(dragging||Time.unscaledTime<ignoreClickUntil)return;if(e.button==PointerEventData.InputButton.Right){Owner.NativeRowContext(ModId,e.position);return;}if(e.button==PointerEventData.InputButton.Left)Owner.NativeRowClick(ModId,e.clickCount>=2);}
  public void OnBeginDrag(PointerEventData e)
  {
   if(e.button!=PointerEventData.InputButton.Left||!Owner.NativeCanDrag)return;
   dragging=true;originalParent=transform.parent;originalIndex=transform.GetSiblingIndex();canvas=GetComponentInParent<Canvas>().rootCanvas;group=GetComponent<CanvasGroup>()??gameObject.AddComponent<CanvasGroup>();var rect=(RectTransform)transform;var size=rect.rect.size;
   placeholder=new GameObject("PMM insertion",typeof(RectTransform),typeof(LayoutElement),typeof(Image));placeholder.transform.SetParent(originalParent,false);placeholder.transform.SetSiblingIndex(originalIndex);var le=placeholder.GetComponent<LayoutElement>();le.minHeight=le.preferredHeight=size.y;le.flexibleWidth=1;placeholder.GetComponent<Image>().color=new Color(0.54f,.75f,.73f,.18f);placeholder.GetComponent<Image>().raycastTarget=false;
   transform.SetParent(canvas.transform,true);rect.sizeDelta=size;group.blocksRaycasts=false;group.alpha=.8f;Owner.NativeDragBegin(this);OnDrag(e);
  }
  public void OnDrag(PointerEventData e)
  {
   if(!dragging)return;lastEvent=e;RectTransformUtility.ScreenPointToLocalPointInRectangle(canvas.transform as RectTransform,e.position,e.pressEventCamera,out var point);((RectTransform)transform).position=canvas.transform.TransformPoint(point);Owner.NativeDragFeedback(this,e,placeholder);
  }
  private void Update(){if(dragging&&lastEvent!=null)Owner.NativeDragFeedback(this,lastEvent,placeholder);}
  public void OnEndDrag(PointerEventData e)
  {
   if(!dragging)return;dragging=false;ignoreClickUntil=Time.unscaledTime+.2f;group.blocksRaycasts=true;group.alpha=1;transform.SetParent(originalParent,false);transform.SetSiblingIndex(originalIndex);if(placeholder!=null)Destroy(placeholder);Owner.NativeDragEnd();
  }
 }
 public sealed class NativeDropZone:MonoBehaviour,IDropHandler
 {
  public Manager Owner;public bool? EnabledColumn;
  public void OnDrop(PointerEventData e){var row=e.pointerDrag!=null?e.pointerDrag.GetComponent<NativeModRow>():null;if(row!=null&&row.Owner==Owner)Owner.NativeDropRequest(row.ModId,EnabledColumn,e.position,e.pressEventCamera);}
 }
 public sealed partial class Manager
 {
  public bool NativeCanDrag=>InMenu&&!applying;
  public void NativeRowClick(string id,bool twice)
  {
   bool ctrl=ControlHeld,shift=ShiftHeld;Act(()=>{if(!ctrl&&!shift&&selectedRows.Contains(id)&&selectedRows.Count>1){Select(id);detailDirty=true;}else SelectRows(id,ctrl,shift);if(twice&&!ctrl&&!shift)NativeSetSelection(selectedRows.ToList(),!ModLoader.IsModEnabled(id));});
  }
  public void NativeDragBegin(NativeModRow row){if(!selectedRows.Contains(row.ModId))SelectRows(row.ModId,false,false);nativeDragging=row;}
  public void NativeDragEnd(){nativeDragging=null;}
  private string NativeBefore(bool enabled,Vector2 position,Camera camera,string exclude)
  {
   foreach(var row in nativeRows.Where(r=>r!=null&&r.gameObject.activeInHierarchy&&r.Column==enabled&&r.ModId!=exclude&&!selectedRows.Contains(r.ModId)).OrderBy(r=>r.transform.GetSiblingIndex()))
    if(position.y>RectTransformUtility.WorldToScreenPoint(camera,((RectTransform)row.transform).TransformPoint(((RectTransform)row.transform).rect.center)).y)return row.ModId;
   return null;
  }
  public void NativeDragFeedback(NativeModRow row,PointerEventData e,GameObject placeholder)
  {
   foreach(var pair in nativeScrolls)
   {
    var scroll=pair.Value;if(scroll==null||!RectTransformUtility.RectangleContainsScreenPoint(scroll.viewport,e.position,e.pressEventCamera))continue;
    RectTransformUtility.ScreenPointToLocalPointInRectangle(scroll.viewport,e.position,e.pressEventCamera,out var local);var rect=scroll.viewport.rect;
    if(local.y>rect.yMax-35)scroll.verticalNormalizedPosition=Mathf.Clamp01(scroll.verticalNormalizedPosition+Time.unscaledDeltaTime*1.3f);
    else if(local.y<rect.yMin+35)scroll.verticalNormalizedPosition=Mathf.Clamp01(scroll.verticalNormalizedPosition-Time.unscaledDeltaTime*1.3f);
    if(placeholder==null)return;placeholder.transform.SetParent(scroll.content,false);var before=NativeBefore(pair.Key,e.position,e.pressEventCamera,row.ModId);var target=nativeRows.FirstOrDefault(r=>r!=null&&r.ModId==before&&r.Column==pair.Key);int index=target!=null?target.transform.GetSiblingIndex():scroll.content.childCount;if(target!=null&&placeholder.transform.GetSiblingIndex()<index)index--;placeholder.transform.SetSiblingIndex(index);return;
   }
  }
  public void NativeDropRequest(string id,bool? target,Vector2 position,Camera camera)
  {
   if(target==null)return;var before=NativeBefore(target.Value,position,camera,id);
   Act(()=>{bool enabled=ModLoader.IsModEnabled(id);if(target.Value!=enabled){if(target.Value)NativeApplyOrder(Planner.EnableBefore(mods,EnabledOrder(),selectedRows.Where(item=>item!=Id),before));else NativeSetSelection(selectedRows.ToList(),false);return;}if(!enabled)return;
    var order=Planner.MoveGroupBefore(EnabledOrder(),selectedRows,before);if(!order.SequenceEqual(EnabledOrder()))NativeApplyOrder(order);
   });
  }
  private void NativeToggle(string id)
  {
   if(!InMenu)return;if(id==Id){toast=T("Сам менеджер отключается через штатное меню.","Disable this manager through the original menu.");return;}
   var order=EnabledOrder();
   if(order.Contains(id))
   {
    var remove=Planner.Dependants(mods,order,id);var next=order.Where(item=>!remove.Contains(item)).ToList();
    if(remove.Count>1){confirmationText=T("От этого мода зависят другие моды. Выключить весь набор?\n\n","Other enabled mods depend on this mod. Disable them together?\n\n")+string.Join("\n",remove.Select(item=>mods.FirstOrDefault(m=>m.Id==item)?.Name??item));nativeConfirmation=()=>NativeApplyOrder(next);NativeInvalidate();return;}
    NativeApplyOrder(next);
   }
   else NativeApplyOrder(Planner.WithDependencies(mods,order,new[]{id}));
  }
  private void NativeApplyOrder(System.Collections.Generic.List<string> order)
  {
   if(order.SequenceEqual(EnabledOrder()))return;nativeAccepted=false;staged=order;PrepareApply();
   if(applying&&checks!=null&&!checks.Any(p=>p.Value.NeedsPrompt))CommitApply();NativeInvalidate();
  }
  private void NativeAutoSort()
  {
   if(!InMenu)return;var order=EnabledOrder();
   if(!Planner.TrySort(mods,order,out var expected,out var cycle)){reportTitle=T("Цикл зависимостей","Dependency cycle");report=cycle;NativeInvalidate();return;}
   try
   {
    var native=ModUIManager.Instance;if(native==null)return;NativeBypass=true;
    try{native.OpenPanel();native.panel.SetActive(false);}finally{NativeBypass=false;}
    var method=typeof(ModUIManager).GetMethod("AutoSortActiveMods",BindingFlags.Instance|BindingFlags.NonPublic);
    if(method==null)throw new MissingMethodException("The game's AutoSortActiveMods method is unavailable.");
    method.Invoke(native,null);staged=EnabledOrder();observed=staged.ToList();findings=Planner.Validate(mods,staged,Id);Refresh();
    data.HistoryAdd(T("Выполнена штатная автосортировка","Game auto-sort executed"),string.Join(", ",staged));Save();
    toast=T("Автосортировка выполнена штатной функцией игры.","Sorted using the game's own auto-sort function.");
    if(!staged.SequenceEqual(order))ShowRestartWarning();NativeInvalidate();
   }
   catch(Exception ex){toast=ex.InnerException?.Message??ex.Message;journal.Add("error","Game auto-sort failed",ex.ToString());NativeInvalidate();}
  }
 }
}
