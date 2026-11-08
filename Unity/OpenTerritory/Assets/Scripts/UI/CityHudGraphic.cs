using UnityEngine;
using UnityEngine.UI;

namespace OpenTerritory
{
    public enum CityHudSymbol { Panel, Pulse, Sidearm, Carbine, LeftSignal, RightSignal, Headlight }

    // Original low-poly silhouettes, independent of icon fonts and texture downloads.
    public sealed class CityHudGraphic : Graphic
    {
        public CityHudSymbol Symbol;
        VertexHelper mesh;
        Color tint;
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear(); mesh = vh; tint = color;
            if (Symbol == CityHudSymbol.Panel)
            {
                var r = rectTransform.rect; float cut = 10;
                vh.AddVert(new Vector2(r.xMin, r.yMin), color, Vector2.zero);
                vh.AddVert(new Vector2(r.xMin, r.yMax - cut), color, Vector2.zero);
                vh.AddVert(new Vector2(r.xMin + cut, r.yMax), color, Vector2.zero);
                vh.AddVert(new Vector2(r.xMax, r.yMax), color, Vector2.zero);
                vh.AddVert(new Vector2(r.xMax, r.yMin), color, Vector2.zero);
                vh.AddTriangle(0, 1, 2); vh.AddTriangle(0, 2, 3); vh.AddTriangle(0, 3, 4); return;
            }
            if (Symbol == CityHudSymbol.LeftSignal || Symbol == CityHudSymbol.RightSignal)
            {
                bool left = Symbol == CityHudSymbol.LeftSignal;
                Triangle(new Vector2(left ? 0 : 1, .5f), new Vector2(.52f, .98f), new Vector2(.52f, .02f));
                Quad(left ? .45f : 0, .32f, .55f, .36f); return;
            }
            if (Symbol == CityHudSymbol.Headlight)
            {
                Line(new Vector2(.43f, .12f), new Vector2(.43f, .88f), .07f);
                Line(new Vector2(.43f, .88f), new Vector2(.70f, .78f), .07f);
                Line(new Vector2(.70f, .78f), new Vector2(.84f, .5f), .07f);
                Line(new Vector2(.84f, .5f), new Vector2(.70f, .22f), .07f);
                Line(new Vector2(.70f, .22f), new Vector2(.43f, .12f), .07f);
                for (int i = 0; i < 3; i++) Line(new Vector2(.04f, .18f + i * .26f), new Vector2(.30f, .30f + i * .26f), .055f);
                return;
            }
            bool rifle = Symbol == CityHudSymbol.Carbine, pulse = Symbol == CityHudSymbol.Pulse;
            Quad(.08f, .47f, rifle ? .60f : .64f, .26f);
            Quad(rifle ? .68f : .72f, .53f, rifle ? .28f : .18f, .12f);
            Quad(rifle ? .24f : .19f, .12f, .15f, .40f);
            if (rifle) { Quad(0, .42f, .18f, .34f); Quad(.47f, .12f, .13f, .38f); Quad(.34f, .76f, .16f, .08f); }
            else { Line(new Vector2(.34f, .47f), new Vector2(.48f, .31f), .035f); Line(new Vector2(.48f, .31f), new Vector2(.51f, .47f), .035f); }
            if (pulse) { tint = new Color(.37f, .81f, .75f, color.a); Quad(.39f, .54f, .24f, .09f); }
        }
        Vector2 Point(Vector2 p) { var r = rectTransform.rect; return new Vector2(r.xMin + p.x * r.width, r.yMin + p.y * r.height); }
        void Triangle(Vector2 a, Vector2 b, Vector2 c)
        {
            int i = mesh.currentVertCount;
            mesh.AddVert(Point(a), tint, Vector2.zero); mesh.AddVert(Point(b), tint, Vector2.zero); mesh.AddVert(Point(c), tint, Vector2.zero); mesh.AddTriangle(i, i + 1, i + 2);
        }
        void Quad(float x, float y, float width, float height)
        { var a = new Vector2(x, y); var b = new Vector2(x, y + height); var c = new Vector2(x + width, y + height); var d = new Vector2(x + width, y); Triangle(a, b, c); Triangle(a, c, d); }
        void Line(Vector2 a, Vector2 b, float width)
        {
            var direction = b - a; var normal = new Vector2(-direction.y, direction.x).normalized * width * .5f;
            Triangle(a - normal, a + normal, b + normal); Triangle(a - normal, b + normal, b - normal);
        }
    }
}
