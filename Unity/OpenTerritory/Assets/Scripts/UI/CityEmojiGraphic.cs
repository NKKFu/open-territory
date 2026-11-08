using UnityEngine;
using UnityEngine.UI;

namespace OpenTerritory
{
    // Small original mesh icons keep the same appearance on systems without emoji fonts.
    public sealed class CityEmojiGraphic : MaskableGraphic
    {
        public CityEmote Emote = CityEmote.Happy;
        public bool Ring;
        public int Selection = -1;
        static readonly Color Ink = new Color(.12f, .18f, .22f);
        static readonly Color Gold = new Color(1, .76f, .29f);
        static readonly Color Coral = new Color(1, .47f, .36f);
        VertexHelper mesh;
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear(); mesh = vh;
            if (Ring)
            {
                for (int i = 0; i < 6; i++)
                {
                    float start = 90 - i * 60 - 28.5f, end = start + 57;
                    var fill = i == Selection ? new Color(.24f, .65f, .63f, .98f) : new Color(.035f, .105f, .14f, .94f);
                    Arc(Vector2.zero, 80, i == Selection ? 225 : 221, start, end, fill);
                    Arc(Vector2.zero, 219, i == Selection ? 225 : 221, start, end,
                        i == Selection ? new Color(.79f, .98f, .88f) : new Color(.43f, .68f, .68f, .55f));
                }
                Disc(Vector2.zero, 74, new Color(.035f, .10f, .13f, .95f)); return;
            }
            // Draw in a stable 64 x 64 coordinate space.
            if (Emote == CityEmote.Dance) { Dancer(); return; }
            if (Emote == CityEmote.Clap) { Hands(false); return; }
            if (Emote == CityEmote.Wave) { Hands(true); return; }
            Disc(new Vector2(0, -1), 28, new Color(.64f, .36f, .12f));
            Disc(new Vector2(0, 1), 28, Gold);
            Disc(new Vector2(-18, 13), 5, new Color(1, .9f, .5f, .6f));
            if (Emote == CityEmote.Laugh)
            {
                Line(new Vector2(-16, 7), new Vector2(-10, 12), 3.2f, Ink); Line(new Vector2(-10, 12), new Vector2(-4, 7), 3.2f, Ink);
                Line(new Vector2(4, 7), new Vector2(10, 12), 3.2f, Ink); Line(new Vector2(10, 12), new Vector2(16, 7), 3.2f, Ink);
                Disc(new Vector2(0, -7), 10, Ink); Disc(new Vector2(0, -12), 6, Coral);
                Line(new Vector2(-6, -2), new Vector2(6, -2), 3, Color.white); return;
            }
            Disc(new Vector2(-10, 9), 3.1f, Ink); Disc(new Vector2(10, 9), 3.1f, Ink);
            if (Emote == CityEmote.Sad)
            {
                Line(new Vector2(-15, 16), new Vector2(-7, 19), 2.5f, Ink);
                Line(new Vector2(7, 19), new Vector2(15, 16), 2.5f, Ink);
                Curve(new Vector2(0, -17), 11, 25, 155, 3.2f, Ink);
                Disc(new Vector2(17, -5), 4, new Color(.35f, .77f, 1));
                Triangle(new Vector2(13, -4), new Vector2(21, -4), new Vector2(17, 4), new Color(.35f, .77f, 1));
            }
            else
            {
                Disc(new Vector2(-17, -3), 4, new Color(1, .49f, .32f, .7f));
                Disc(new Vector2(17, -3), 4, new Color(1, .49f, .32f, .7f));
                Curve(new Vector2(0, 1), 14, 205, 335, 3.8f, Ink);
            }
        }
        Vector2 Point(Vector2 value) => rectTransform.rect.center + value * (Mathf.Min(rectTransform.rect.width, rectTransform.rect.height) / 64);
        void Vertex(Vector2 p, Color c) { mesh.AddVert(Ring ? p + rectTransform.rect.center : Point(p), c * color, Vector2.zero); }
        void Triangle(Vector2 a, Vector2 b, Vector2 c, Color tint)
        { int at = mesh.currentVertCount; Vertex(a, tint); Vertex(b, tint); Vertex(c, tint); mesh.AddTriangle(at, at + 1, at + 2); }
        void Quad(Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color tint)
        { int at = mesh.currentVertCount; Vertex(a, tint); Vertex(b, tint); Vertex(c, tint); Vertex(d, tint); mesh.AddTriangle(at, at + 1, at + 2); mesh.AddTriangle(at, at + 2, at + 3); }
        static Vector2 Polar(float radius, float degrees) => new Vector2(Mathf.Cos(degrees * Mathf.Deg2Rad), Mathf.Sin(degrees * Mathf.Deg2Rad)) * radius;
        void Arc(Vector2 at, float inner, float outer, float from, float to, Color tint)
        {
            int steps = Mathf.Max(4, Mathf.CeilToInt((to - from) / 8));
            for (int s = 0; s < steps; s++)
            {
                float a = Mathf.Lerp(from, to, (float)s / steps), b = Mathf.Lerp(from, to, (float)(s + 1) / steps);
                Quad(at + Polar(inner, a), at + Polar(outer, a), at + Polar(outer, b), at + Polar(inner, b), tint);
            }
        }
        void Disc(Vector2 at, float radius, Color tint) => Arc(at, 0, radius, 0, 360, tint);
        void Curve(Vector2 at, float radius, float from, float to, float width, Color tint) => Arc(at, radius - width * .5f, radius + width * .5f, from, to, tint);
        void Line(Vector2 a, Vector2 b, float width, Color tint)
        {
            Vector2 direction = (b - a).normalized, side = new Vector2(-direction.y, direction.x) * width * .5f;
            Quad(a - side, a + side, b + side, b - side, tint);
            Disc(a, width * .5f, tint); Disc(b, width * .5f, tint);
        }
        void Dancer()
        {
            Disc(new Vector2(0, 20), 7, Gold);
            Line(new Vector2(0, 9), new Vector2(-4, -8), 9, Coral);
            Line(new Vector2(-2, 7), new Vector2(-18, 1), 5, Gold); Line(new Vector2(-18, 1), new Vector2(-23, 13), 5, Gold);
            Line(new Vector2(2, 6), new Vector2(17, 14), 5, Gold); Line(new Vector2(17, 14), new Vector2(21, 25), 5, Gold);
            Line(new Vector2(-4, -8), new Vector2(-18, -17), 6, new Color(.40f, .79f, .91f));
            Line(new Vector2(-18, -17), new Vector2(-12, -27), 6, new Color(.40f, .79f, .91f));
            Line(new Vector2(-4, -8), new Vector2(9, -17), 6, new Color(.40f, .79f, .91f));
            Line(new Vector2(9, -17), new Vector2(19, -12), 6, new Color(.40f, .79f, .91f));
        }
        void Hands(bool wave)
        {
            if (wave)
            {
                Line(new Vector2(-6, -24), new Vector2(8, 2), 16, Gold);
                for (int i = 0; i < 4; i++) Line(new Vector2(-3 + i * 6, 0), new Vector2(-10 + i * 7, 21 + (i == 1 ? 6 : 0)), 5, Gold);
                Line(new Vector2(-3, -7), new Vector2(-16, 3), 7, Gold);
                Curve(new Vector2(4, 8), 26, -15, 45, 2.5f, new Color(.69f, .94f, .95f));
                Curve(new Vector2(4, 8), 32, -15, 40, 2, new Color(.69f, .94f, .95f)); return;
            }
            Line(new Vector2(-20, -22), new Vector2(-6, 4), 13, new Color(.93f, .57f, .24f));
            Line(new Vector2(19, -23), new Vector2(5, 4), 13, Gold);
            for (int i = 0; i < 3; i++)
            {
                Line(new Vector2(-7 + i * 4, 1), new Vector2(-12 + i * 4, 20), 4.5f, new Color(.93f, .57f, .24f));
                Line(new Vector2(1 + i * 4, 0), new Vector2(-4 + i * 4, 19), 4.5f, Gold);
            }
            Line(new Vector2(-19, 14), new Vector2(-25, 20), 2.5f, new Color(.69f, .94f, .95f));
            Line(new Vector2(16, 20), new Vector2(21, 27), 2.5f, new Color(.69f, .94f, .95f));
            Line(new Vector2(1, 25), new Vector2(0, 31), 2.5f, new Color(.69f, .94f, .95f));
        }
    }
}
