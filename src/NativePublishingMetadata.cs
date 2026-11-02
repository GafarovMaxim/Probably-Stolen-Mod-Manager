using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace ProbablyModManager
{
 public sealed partial class Manager
 {
  private string publicationAppliedInspection="";
  private CustomUIElement publicationTagCount;
  private void ApplyPublicationInspection(PublicationState state)
  {
   if(publicationRequestAction!="inspect"||state.RequestId!=publicationRequestId||publicationAppliedInspection==state.RequestId||state.Phase=="inspecting"||publicationDraft==null||state.MetadataModId!=publicationDraft.ModId)return;
   publicationAppliedInspection=state.RequestId;bool changed=false;
   if(string.IsNullOrWhiteSpace(publicationDraft.PublishedFileId)&&!string.IsNullOrWhiteSpace(state.DetectedPublishedFileId)){publicationDraft.PublishedFileId=state.DetectedPublishedFileId;changed=true;}
   if(string.IsNullOrWhiteSpace(publicationDraft.Tags)&&!string.IsNullOrWhiteSpace(state.DetectedTags)){publicationDraft.Tags=state.DetectedTags;changed=true;}
   if(changed){publicationReviewed=false;SavePublicationDrafts();}
  }
  private void BuildPublicationBindingHint(CustomUIBuilder b)
  {
   var state=publicationState;if(state==null||state.MetadataModId!=publicationModId||state.DetectedPublishedFileId.Length==0)return;
   NativeLabel(b,T("Сохранённая привязка: ","Saved binding: ")+state.DetectedPublishedFileId+" · "+state.BindingSource,16,Mint);
   if(publicationDraft.PublishedFileId!=state.DetectedPublishedFileId)PublicationControl(NativeButton(b,T("Использовать найденный ID","Use detected ID"),()=>PublicationChanged(()=>publicationDraft.PublishedFileId=state.DetectedPublishedFileId)),()=>!PublishingBusy);
  }
  private List<string> SelectedPublicationTags()=>(publicationDraft?.Tags??"").Split(new[]{',',';'},StringSplitOptions.RemoveEmptyEntries).Select(t=>t.Trim()).Where(t=>t.Length>0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
  private void SetPublicationTag(string name,bool enabled)
  {
   PublicationChanged(()=>{var selected=SelectedPublicationTags();selected.RemoveAll(t=>t.Equals(name,StringComparison.OrdinalIgnoreCase));if(enabled)selected.Add(name);publicationDraft.Tags=string.Join(", ",selected);});
   publicationTagCount?.SetText(T("Выбрано: ","Selected: ")+SelectedPublicationTags().Count);
  }
  private void BuildPublicationTagPicker(CustomUIBuilder b)
  {
   b.AddHeader(T("Теги мастерской Probably Stolen","Probably Stolen Workshop tags"));b.BeginRow(12);
   publicationTagCount=NativeLabel(b,T("Выбрано: ","Selected: ")+SelectedPublicationTags().Count,20,Mint);b.AddFlexibleSpace();
   PublicationControl(NativeButton(b,T("Обновить из Steam","Refresh from Steam"),()=>SendPublication("refresh-tags"),260),()=>publicationHelperConnected&&!PublishingBusy&&InMenu);b.End();
   var state=publicationState;NativeLabel(b,T("Выбери несколько тегов. Свои теги также можно оставить в поле формы.","Choose multiple tags. Custom tags can also be retained in the form field."),19);
   if(!string.IsNullOrEmpty(state?.CatalogUpdatedUtc))NativeLabel(b,T("Каталог: ","Catalog: ")+state.CatalogUpdatedUtc,16);
   if(!string.IsNullOrEmpty(state?.CatalogMessage))NativeLabel(b,state.CatalogMessage,18,Warning);
   b.BeginScrollFlexible();StyleScroll(b.CurrentContainer.GetComponentInParent<ScrollRect>());
   var tags=state?.AvailableTags??new List<WorkshopTag>();var selectedTags=SelectedPublicationTags();
   if(tags.Count==0)NativeLabel(b,T("Каталог пока недоступен. Подключи помощник и нажми обновление.","The catalog is unavailable. Connect the companion and refresh it."),21,Warning);
   else
   {
    b.BeginGrid(3,Mathf.Max(240,(nativeWidth-60)/3),44,6);
    foreach(var tag in tags.OrderBy(t=>t.Label,StringComparer.OrdinalIgnoreCase))
    {
     string name=tag.Name;b.AddToggle(string.IsNullOrWhiteSpace(tag.Label)?name:tag.Label,selectedTags.Contains(name,StringComparer.OrdinalIgnoreCase),value=>SetPublicationTag(name,value));PublicationControl(b.Last,()=>!PublishingBusy);if(b.Last.text!=null)b.Last.text.richText=false;
    }
    b.End();
   }
   b.BeginColumn(6);publicationStatusRoot=b.CurrentContainer;b.End();
   b.End();b.BeginRow(12,TextAnchor.MiddleRight);b.AddFlexibleSpace();PublicationControl(NativeButton(b,T("Готово","Done"),()=>{publicationChoosingTags=false;SavePublicationDrafts();NativeInvalidate();},240),()=>!PublishingBusy);b.End();RefreshPublicationUi();
  }
 }
}
