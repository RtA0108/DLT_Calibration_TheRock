using UnityEngine;
using UnityEngine.EventSystems;

// 패치는 마커들보다 아래 층(PatchLayer)에 따로 있어서, 패치를 누르고 끄는 입력을 주인 마커에 넘겨준다.
public class PatchDragForwarder : MonoBehaviour, IPointerDownHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    public Marker marker;

    public void OnPointerDown(PointerEventData eventData) { if (marker != null) marker.OnPointerDown(eventData); }
    public void OnBeginDrag(PointerEventData eventData) { if (marker != null) marker.OnBeginDrag(eventData); }
    public void OnDrag(PointerEventData eventData) { if (marker != null) marker.OnDrag(eventData); }
    public void OnEndDrag(PointerEventData eventData) { if (marker != null) marker.OnEndDrag(eventData); }
}
