using UnityEngine;
using UnityEngine.UI;

namespace OpenTerritory
{
    // A static alpha mesh needs no post-processing package, render texture or per-frame allocation.
    public sealed class CityVignetteGraphic : Graphic
    {
        public static float EdgeOpacity(Vector2 uv)
        {
            Vector2 p = Vector2.Scale(uv * 2 - Vector2.one, new Vector2(.95f, .85f));
            float t = Mathf.InverseLerp(.35f, 1.25f, p.magnitude);
            return Mathf.SmoothStep(0, 1, t) * Mathf.SmoothStep(0, 1, t);
        }
        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear(); var rect = rectTransform.rect;
            const int columns = 48, rows = 32;
            for (int y = 0; y <= rows; y++)
                for (int x = 0; x <= columns; x++)
                {
                    var uv = new Vector2((float)x / columns, (float)y / rows);
                    var tint = color; tint.a *= EdgeOpacity(uv);
                    mesh.AddVert(new Vector3(rect.xMin + rect.width * uv.x, rect.yMin + rect.height * uv.y, 0), tint, Vector2.zero);
                }
            for (int y = 0; y < rows; y++)
                for (int x = 0; x < columns; x++)
                {
                    int i = y * (columns + 1) + x;
                    mesh.AddTriangle(i, i + columns + 1, i + 1);
                    mesh.AddTriangle(i + 1, i + columns + 1, i + columns + 2);
                }
        }
    }
}
