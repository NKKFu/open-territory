using UnityEngine;
using UnityEngine.EventSystems;

namespace OpenTerritory
{
    public sealed class CityMapSurface : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerClickHandler,
        IBeginDragHandler, IDragHandler, IEndDragHandler, IScrollHandler
    {
        public CityMapUI Map;
        int? pointer, released;
        bool dragged;
        Vector2 previous;
        Vector2 Local(PointerEventData e)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)transform, e.position, e.pressEventCamera, out var p);
            return p;
        }
        public void OnPointerDown(PointerEventData e)
        {
            if (pointer.HasValue) return;
            pointer = e.pointerId; released = null; dragged = false; previous = Local(e);
        }
        public void OnBeginDrag(PointerEventData e) { if (pointer == e.pointerId) dragged = true; }
        public void OnDrag(PointerEventData e)
        {
            if (pointer != e.pointerId || e.button != PointerEventData.InputButton.Left) return;
            var p = Local(e); Map.Pan(p - previous); previous = p; dragged = true;
        }
        public void OnEndDrag(PointerEventData e) { if (pointer == e.pointerId) pointer = null; }
        public void OnPointerUp(PointerEventData e) { if (pointer == e.pointerId) { released = pointer; pointer = null; } }
        public void OnPointerClick(PointerEventData e)
        {
            if (released != e.pointerId) return;
            released = null;
            if (dragged) return;
            if (e.button == PointerEventData.InputButton.Left) Map.Pick(Local(e));
            else if (e.button == PointerEventData.InputButton.Right) Map.ClearMarker();
        }
        public void OnScroll(PointerEventData e) { Map.ZoomAt(Mathf.Pow(1.18f, e.scrollDelta.y), Local(e)); }
        void OnDisable() { pointer = released = null; dragged = false; }
    }
}
