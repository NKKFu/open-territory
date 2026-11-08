using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace OpenTerritory
{
    public sealed class CityEmoteWheel : MonoBehaviour
    {
        sealed class BubbleView { public Transform Actor; public RectTransform Rect; public CityEmojiGraphic Icon; public CanvasGroup Fade; public float Until; public bool Resident; }
        CityContext ctx; CityEmotes owner;
        RectTransform root, wheel;
        GameObject selectionLayer;
        CityEmojiGraphic ring;
        Text choice;
        Font font;
        readonly List<BubbleView> bubbles = new List<BubbleView>(4);
        readonly Text[] captions = new Text[6];
        readonly Color ink = new Color(.90f, .97f, .94f), muted = new Color(.66f, .80f, .80f);
        public void Initialize(CityContext context, CityEmotes emotes)
        {
            ctx = context; owner = emotes; font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            root = Rect("Expressões da Baía", transform, Vector2.zero, Vector2.zero);
            var canvas = root.gameObject.AddComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 32;
            var scaler = root.gameObject.AddComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 800); scaler.matchWidthOrHeight = 1;
            // No raycaster: all selection goes through the held Q input; world buttons are hidden.
            selectionLayer = Rect("Roda de emojis", root, Vector2.zero, Vector2.zero).gameObject;
            wheel = Rect("Seletor circular", selectionLayer.transform, Vector2.zero, new Vector2(460, 460));
            ring = wheel.gameObject.AddComponent<CityEmojiGraphic>(); ring.Ring = true; ring.raycastTarget = false;
            Label("Título", wheel, "EXPRESSE-SE", new Vector2(0, 262), new Vector2(400, 30), 18, ink);
            for (int i = 0; i < 6; i++)
            {
                float angle = (90 - i * 60) * Mathf.Deg2Rad;
                Vector2 at = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * 156;
                var icon = Rect(CityEmotes.Names[i] + " · emoji", wheel, at + Vector2.up * 10, new Vector2(58, 58));
                var graphic = icon.gameObject.AddComponent<CityEmojiGraphic>(); graphic.Emote = (CityEmote)i; graphic.raycastTarget = false;
                captions[i] = Label(CityEmotes.Names[i], wheel, CityEmotes.Names[i], at + Vector2.down * 34, new Vector2(120, 28), 17, ink);
            }
            Label("Tecla segurada", wheel, "Q", new Vector2(0, 37), new Vector2(80, 25), 18, muted);
            choice = Label("Emoção selecionada", wheel, "Escolha", new Vector2(0, 5), new Vector2(140, 30), 21, ink);
            Label("Soltar", wheel, "Solte para usar", new Vector2(0, -28), new Vector2(140, 24), 12, muted);
            Label("Instruções", wheel, "Mova o mouse • Solte Q para usar\nVolte ao centro para cancelar", new Vector2(0, -266), new Vector2(440, 52), 16, ink);
            Show(false);
        }
        static RectTransform Rect(string name, Transform parent, Vector2 at, Vector2 size)
        {
            var r = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>(); r.SetParent(parent, false);
            r.anchorMin = r.anchorMax = r.pivot = new Vector2(.5f, .5f); r.anchoredPosition = at; r.sizeDelta = size; return r;
        }
        Text Label(string name, Transform parent, string text, Vector2 at, Vector2 size, int pixels, Color tint)
        {
            var r = Rect(name, parent, at, size); var label = r.gameObject.AddComponent<CityEnglishText>();
            label.font = font; label.text = text; label.fontSize = pixels; label.color = tint; label.alignment = TextAnchor.MiddleCenter;
            label.raycastTarget = false; label.supportRichText = false;
            var shadow = label.gameObject.AddComponent<Shadow>(); shadow.effectColor = new Color(.01f, .04f, .06f, .9f); shadow.effectDistance = new Vector2(1, -1);
            return label;
        }
        public void Show(bool value)
        {
            if (!selectionLayer) return;
            selectionLayer.SetActive(value);
            if (value) { Fit(); Highlight(CityEmote.None); }
        }
        void Fit()
        {
            float width = Screen.width / Mathf.Max(.001f, root.GetComponent<Canvas>().scaleFactor);
            float height = Screen.height / Mathf.Max(.001f, root.GetComponent<Canvas>().scaleFactor);
            wheel.localScale = Vector3.one * Mathf.Min(1, (width - 28) / 470, (height - 24) / 600);
        }
        public static CityEmote PickDirection(Vector2 point)
        {
            if (point.sqrMagnitude < 80 * 80) return CityEmote.None;
            float angle = Mathf.Repeat(90 - Mathf.Atan2(point.y, point.x) * Mathf.Rad2Deg + 30, 360);
            return (CityEmote)Mathf.FloorToInt(angle / 60);
        }
        public CityEmote Pick(Vector2 screenPoint)
        {
            if (screenPoint.x < 0 || screenPoint.y < 0 || screenPoint.x >= Screen.width || screenPoint.y >= Screen.height) return CityEmote.None;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(wheel, screenPoint, null, out var local);
            return PickDirection(local);
        }
        public void Highlight(CityEmote value)
        {
            if (ring.Selection != (int)value) { ring.Selection = (int)value; ring.SetVerticesDirty(); }
            choice.text = value == CityEmote.None ? "Escolha" : CityEmotes.Names[(int)value];
            for (int i = 0; i < captions.Length; i++) captions[i].color = i == (int)value ? Color.white : ink;
        }
        public void Bubble(Transform actor, CityEmote emote, float until, bool resident)
        {
            var bubble = bubbles.Find(b => b.Actor == actor) ?? bubbles.Find(b => !b.Actor || b.Until <= ctx.Clock);
            if (bubble == null)
            {
                if (bubbles.Count >= 4) { bubble = bubbles[1]; }
                else
                {
                    var rect = Rect("Emoji no mundo", root, Vector2.zero, new Vector2(50, 50));
                    bubble = new BubbleView { Rect = rect, Icon = rect.gameObject.AddComponent<CityEmojiGraphic>(), Fade = rect.gameObject.AddComponent<CanvasGroup>() };
                    bubble.Icon.raycastTarget = false; bubble.Fade.blocksRaycasts = false; bubbles.Add(bubble);
                }
            }
            bubble.Actor = actor; bubble.Until = until; bubble.Resident = resident;
            bubble.Icon.Emote = emote; bubble.Icon.SetVerticesDirty(); bubble.Rect.gameObject.SetActive(true);
            // Keep expression icons behind the wheel when it is open.
            bubble.Rect.SetAsFirstSibling();
        }
        public void HideBubble(Transform actor) { foreach (var bubble in bubbles) if (bubble.Actor == actor) { bubble.Until = 0; bubble.Rect.gameObject.SetActive(false); } }
        void LateUpdate()
        {
            if (!ctx || !ctx.Ready) return;
            if (owner.WheelOpen) Fit();
            for (int index = 0; index < bubbles.Count; index++)
            {
                var b = bubbles[index];
                bool show = b.Actor && b.Actor.gameObject.activeInHierarchy && b.Until > ctx.Clock && !ctx.Paused && !owner.WheelOpen;
                if (show && b.Resident)
                {
                    var resident = ctx.Residents.Find(b.Actor);
                    show = resident != null && !resident.Down && resident.EmoteUntil > ctx.Clock && resident.FearUntil <= ctx.Clock;
                }
                Vector3 point = b.Actor ? b.Actor.position + Vector3.up * 2.85f : Vector3.zero;
                if (show)
                {
                    var screen = ctx.Camera.WorldToScreenPoint(point);
                    show = screen.z > .1f && screen.x > 28 && screen.y > 28 && screen.x < Screen.width - 28 && screen.y < Screen.height - 28 &&
                        ctx.Visible(ctx.Camera.transform.position, b.Actor.position + Vector3.up * 1.7f);
                    if (show)
                    {
                        RectTransformUtility.ScreenPointToLocalPointInRectangle(root, screen, null, out var at);
                        for (int attempt = 0; attempt < 4; attempt++)
                        {
                            bool overlap = false;
                            for (int previous = 0; previous < index; previous++)
                            {
                                var other = bubbles[previous]; if (!other.Rect.gameObject.activeSelf) continue;
                                Vector2 delta = at - other.Rect.anchoredPosition;
                                if (Mathf.Abs(delta.x) < 54 && Mathf.Abs(delta.y) < 54) { overlap = true; break; }
                            }
                            if (!overlap) break;
                            at.x += index % 2 == 0 ? -56 : 56;
                        }
                        at.x = Mathf.Clamp(at.x, root.rect.xMin + 28, root.rect.xMax - 28); b.Rect.anchoredPosition = at;
                        b.Fade.alpha = Mathf.Clamp01((b.Until - ctx.Clock) / .35f);
                    }
                }
                b.Rect.gameObject.SetActive(show);
            }
        }
    }
}
