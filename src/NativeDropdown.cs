using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ProbablyModManager
{
 public sealed partial class Manager
 {
  private Action nativeDropdownClosed;
  // Keep the game's fonts and button events; draw the selector art without font-dependent symbols.
  private CustomUIElement NativeSelect(CustomUIBuilder b,IList<string> options,int index,Action<int> changed,float width=-1,bool enabled=true)
  {
   var choices=new List<string>(options);int selected=Mathf.Clamp(index,0,Mathf.Max(0,choices.Count-1));CustomUIElement header=null;
   RectTransform arrow=null;
   header=NativeButton(b,choices.Count>0?choices[selected]:"—",()=>OpenNativeSelect(header,arrow,choices,selected,value=>
   {
    selected=value;if(header?.IsAlive==true)header.SetText(choices[value]);changed?.Invoke(value);
   }),width,enabled&&choices.Count>0);
   StyleDropdownButton(header,true,false);arrow=DrawDropdownChevron(header.Rect);
   return header;
  }
  private void StyleDropdownButton(CustomUIElement element,bool header,bool selected)
  {
   foreach(var image in element.root.GetComponentsInChildren<Image>(true))image.enabled=false;
   var background=element.root.GetComponent<Image>()??element.root.AddComponent<Image>();background.enabled=true;background.sprite=null;background.type=Image.Type.Simple;background.color=Color.white;background.raycastTarget=true;
   if(element.button!=null)
   {
    element.button.targetGraphic=background;element.button.transition=Selectable.Transition.ColorTint;
    var colors=element.button.colors;colors.normalColor=header?Mint:selected?new Color32(61,86,90,255):new Color32(43,37,54,255);
    colors.highlightedColor=header?new Color32(174,216,210,255):new Color32(68,92,96,255);colors.selectedColor=colors.normalColor;
    colors.pressedColor=header?new Color32(112,162,158,255):new Color32(85,120,121,255);colors.disabledColor=header?new Color32(81,104,105,255):new Color32(39,35,47,255);colors.fadeDuration=.09f;element.button.colors=colors;
   }
   if(element.text!=null)
   {
    element.text.color=header?new Color32(40,40,51,255):selected?new Color32(185,225,215,255):Muted;
    element.text.alignment=TextAlignmentOptions.MidlineLeft;element.text.enableAutoSizing=false;element.text.fontSize=20;element.text.enableWordWrapping=false;element.text.overflowMode=TextOverflowModes.Ellipsis;
    var label=element.text.rectTransform;var layout=label.GetComponent<LayoutElement>()??label.gameObject.AddComponent<LayoutElement>();layout.ignoreLayout=true;
    label.anchorMin=Vector2.zero;label.anchorMax=Vector2.one;label.offsetMin=new Vector2(header?12:20,2);label.offsetMax=new Vector2(header?-42:-12,-2);
   }
  }
  private static RectTransform DropdownStroke(RectTransform parent,string name,Vector2 position,Vector2 size,float angle,Color color)
  {
   var item=new GameObject(name,typeof(RectTransform),typeof(Image));var rect=(RectTransform)item.transform;rect.SetParent(parent,false);rect.anchorMin=rect.anchorMax=rect.pivot=new Vector2(.5f,.5f);rect.anchoredPosition=position;rect.sizeDelta=size;rect.localRotation=Quaternion.Euler(0,0,angle);var image=item.GetComponent<Image>();image.color=color;image.raycastTarget=false;return rect;
  }
  private static RectTransform DrawDropdownChevron(RectTransform header)
  {
   var node=new GameObject("dropdown arrow",typeof(RectTransform));var rect=(RectTransform)node.transform;rect.SetParent(header,false);rect.anchorMin=rect.anchorMax=new Vector2(1,.5f);rect.sizeDelta=new Vector2(16,12);rect.anchoredPosition=new Vector2(-21,0);
   var color=new Color32(42,59,64,255);DropdownStroke(rect,"left",new Vector2(-3,1),new Vector2(8,2),-45,color);DropdownStroke(rect,"right",new Vector2(3,1),new Vector2(8,2),45,color);return rect;
  }
  private void OpenNativeSelect(CustomUIElement header,RectTransform arrow,IList<string> choices,int selected,Action<int> changed)
  {
   if(header?.IsAlive!=true||choices.Count==0)return;CloseContext();
   contextOverlay=new GameObject("PMM dropdown overlay",typeof(RectTransform),typeof(Image),typeof(Button));
   var root=(RectTransform)contextOverlay.transform;root.SetParent(nativeWindow.root.transform.parent,false);root.anchorMin=Vector2.zero;root.anchorMax=Vector2.one;root.offsetMin=root.offsetMax=Vector2.zero;
   contextOverlay.GetComponent<Image>().color=Color.clear;contextOverlay.GetComponent<Button>().onClick.AddListener(CloseContext);
   var panel=new GameObject("PMM dropdown choices",typeof(RectTransform),typeof(Image),typeof(Outline));var rect=(RectTransform)panel.transform;rect.SetParent(root,false);rect.anchorMin=rect.anchorMax=rect.pivot=new Vector2(0,1);
   float desiredWidth=header.Rect.rect.width;
   if(header.text!=null)foreach(var label in choices)desiredWidth=Mathf.Max(desiredWidth,header.text.GetPreferredValues(label).x+48);
   float width=Mathf.Min(Mathf.Max(190,desiredWidth),Mathf.Min(480,root.rect.width-24));
   int visibleRows=Mathf.Min(8,choices.Count);float height=Mathf.Min(visibleRows*36+Mathf.Max(0,visibleRows-1)*2+12,root.rect.height-24);bool needsScroll=choices.Count>visibleRows||height<choices.Count*38+10;rect.sizeDelta=new Vector2(width,height);
   var canvas=root.GetComponentInParent<Canvas>();var camera=canvas.renderMode==RenderMode.ScreenSpaceOverlay?null:canvas.worldCamera;
   var bottom=RectTransformUtility.WorldToScreenPoint(camera,header.Rect.TransformPoint(new Vector2(header.Rect.rect.xMin,header.Rect.rect.yMin)));
   RectTransformUtility.ScreenPointToLocalPointInRectangle(root,bottom,camera,out var local);float y=local.y-root.rect.yMax;
   if(y-height< -root.rect.height+12)y+=header.Rect.rect.height+height;
   rect.anchoredPosition=new Vector2(Mathf.Clamp(local.x-root.rect.xMin,12,Mathf.Max(12,root.rect.width-width-12)),Mathf.Clamp(y,-root.rect.height+height+12,-12));
   panel.GetComponent<Image>().color=new Color32(35,30,45,255);panel.GetComponent<Outline>().effectColor=new Color32(112,155,153,255);panel.GetComponent<Outline>().effectDistance=new Vector2(1,-1);
   var builder=CustomUIManager.Instance.BuildInto(rect,true);var layout=rect.GetComponent<VerticalLayoutGroup>();layout.padding=new RectOffset(6,6,6,6);layout.spacing=2;
   if(needsScroll){builder.BeginScroll(height-12);var scroll=builder.CurrentContainer.GetComponentInParent<ScrollRect>();StyleScroll(scroll);if(scroll?.verticalScrollbar!=null)((RectTransform)scroll.verticalScrollbar.transform).SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal,10);}
   for(int i=0;i<choices.Count;i++)
   {
    int value=i;var item=NativeButton(builder,choices[i],()=>{CloseContext();changed(value);});var rowLayout=item.root.GetComponent<LayoutElement>();rowLayout.minHeight=rowLayout.preferredHeight=36;rowLayout.flexibleHeight=0;item.root.name="PMM option "+i;StyleDropdownButton(item,false,i==selected);
    if(i==selected){var mark=DropdownStroke(item.Rect,"selected",Vector2.zero,new Vector2(3,22),0,Mint);mark.anchorMin=mark.anchorMax=new Vector2(0,.5f);mark.anchoredPosition=new Vector2(5,0);}
   }
   if(needsScroll)builder.End();
   if(arrow!=null){arrow.localRotation=Quaternion.Euler(0,0,180);nativeDropdownClosed=()=>{if(arrow!=null)arrow.localRotation=Quaternion.identity;};}
   root.SetAsLastSibling();CustomUIBuilder.RebuildLayout(rect);
  }
 }
}
