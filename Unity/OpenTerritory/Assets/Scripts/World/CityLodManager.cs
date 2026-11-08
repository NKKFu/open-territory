using UnityEngine;

namespace OpenTerritory
{
    [DefaultExecutionOrder(300)]
    public sealed class CityLodManager : MonoBehaviour
    {
        public CityObjectLod[] objects = new CityObjectLod[0];
        float next;
        Camera main;
        void LateUpdate()
        {
            if (Time.unscaledTime < next) return;
            next = Time.unscaledTime + .12f;
            if (!main || !main.isActiveAndEnabled) main = Camera.main;
            if (!main) return;
            foreach (var item in objects)
                if (item && item.isActiveAndEnabled) item.ApplyLevel(item.SelectLevel(main));
        }
        void OnDisable()
        {
            foreach (var item in objects) if (item) item.ApplyLevel(0);
        }
    }
}
