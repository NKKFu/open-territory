using System;
using UnityEngine;
using UnityEngine.EventSystems;
namespace OpenTerritory
{
    // Pointer ownership keeps a second finger from releasing a held control.
    public sealed class CityPointerControl : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IDragHandler
    {
        public Action Pressed, Released;
        public Action<PointerEventData> Drag;
        int? pointer;
        public void OnPointerDown(PointerEventData e){if(pointer.HasValue)return;pointer=e.pointerId;Pressed?.Invoke();Drag?.Invoke(e);}
        public void OnDrag(PointerEventData e){if(pointer==e.pointerId)Drag?.Invoke(e);}
        public void OnPointerUp(PointerEventData e){if(pointer==e.pointerId)Release();}
        public void Release(){if(!pointer.HasValue)return;pointer=null;Released?.Invoke();}
        void OnDisable(){Release();}
    }
}
