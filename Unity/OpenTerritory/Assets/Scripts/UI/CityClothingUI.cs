using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace OpenTerritory
{
    // A temporary rack-side panel. The city keeps running; walking away closes it.
    public sealed class CityClothingUI : MonoBehaviour
    {
        CityContext ctx; CityClothingShop shop; Font font;
        RectTransform root, panel; Canvas canvas;
        Text wallet, itemName, feedback, buyText;
        readonly List<CityWardrobe.Garment> items = new List<CityWardrobe.Garment>();
        readonly Button[] rows = new Button[4], tabs = new Button[3];
        readonly Text[] rowNames = new Text[4], rowPrices = new Text[4];
        readonly Image[] swatches = new Image[4];
        Button buy;
        int category, anchorCategory, selected, closedFrame = -10;
        bool swallow;
        readonly Color ink = new Color(.09f, .18f, .25f), blue = new Color(.14f, .31f, .41f), mist = new Color(.84f, .90f, .88f), coral = new Color(.91f, .50f, .42f);
        public bool IsOpen { get; private set; }
        public string SelectedId => items.Count > selected ? items[selected].id : null;
        public Button BuyButton => buy;
        public RectTransform Panel => panel;
        public void Initialize(CityContext context) { ctx = context; }
        public void Open(CityClothingShop source, int startCategory)
        {
            if (!source || !source.CanBrowse()) return;
            if (!root) Build();
            shop = source; category = anchorCategory = Mathf.Clamp(startCategory, 0, 2); IsOpen = true;
            root.gameObject.SetActive(true); ctx.Combat.ReleaseMeleeInput(); ctx.Emotes.CancelInput(); ctx.Emotes.CancelPerformance();
            ctx.UI.ClearTouch(); ctx.CurrentAction = null; SetCategory(category); Layout(); ctx.RefreshCursor();
            if (EventSystem.current) EventSystem.current.SetSelectedGameObject(null);
        }
        public void Close()
        {
            if (!IsOpen) return;
            IsOpen = false; closedFrame = Time.frameCount; swallow = true;
            ctx.Wardrobe.CancelPreview(); root.gameObject.SetActive(false); ctx.UI.ClearTouch();
            if (EventSystem.current) EventSystem.current.SetSelectedGameObject(null);
            ctx.RefreshCursor();
        }
        // Runs immediately after the shared input sample, before world controls.
        public void SampleInput()
        {
            if (IsOpen)
            {
                bool leave = !shop.CanBrowse() || Vector3.Distance(ctx.Player.position + Vector3.up, shop.browsePoints[anchorCategory].position) > 4.5f;
                if (ctx.Input.MapToggle) Close();
                else if (ctx.Input.Pause || ctx.Input.Interact) { Close(); ctx.Input.Pause = ctx.Input.Interact = false; }
                else if (leave || Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.D) || ctx.Input.TouchMove.sqrMagnitude > .01f || ctx.Input.Drone || ctx.Input.HackerToggle) Close();
                else
                {
                    if (Input.GetKeyDown(KeyCode.LeftArrow)) SetCategory((category + 2) % 3);
                    if (Input.GetKeyDown(KeyCode.RightArrow)) SetCategory((category + 1) % 3);
                    if (Input.GetKeyDown(KeyCode.UpArrow)) Select((selected + items.Count - 1) % items.Count);
                    if (Input.GetKeyDown(KeyCode.DownArrow)) Select((selected + 1) % items.Count);
                    // Enter is owned here, so a focused uGUI button cannot buy twice.
                    if (Input.GetKeyDown(KeyCode.Return) && !Input.GetKey(KeyCode.LeftAlt) && !Input.GetKey(KeyCode.RightAlt)) Purchase();
                    Layout(); Refresh();
                }
            }
            if (IsOpen || swallow || Time.frameCount <= closedFrame + 1)
            {
                var i = ctx.Input; i.Look = Vector2.zero; i.Zoom = 0;
                if (IsOpen) i.Move = i.Drive = Vector2.zero;
                i.Jump = i.Run = i.Interact = i.Talk = i.TalkPressed = i.TalkHeld = i.TalkReleased = false;
                i.HackPressed = i.HackHeld = i.HackReleased = i.HackerToggle = i.HackNext = i.Drone = false;
                i.AimHeld = i.AimToggle = i.Fire = i.FireHeld = i.Reload = i.Holster = i.Fall = i.MeleePressed = i.CrouchPressed = false; i.Weapon = 0;
                swallow = Input.GetMouseButton(0) || Input.GetMouseButton(1) || Input.GetKey(KeyCode.Return);
            }
        }
        public void SetCategory(int value)
        {
            category = Mathf.Clamp(value, 0, 2); items.Clear();
            foreach (var garment in CityWardrobe.Catalog) if (garment.category == category) items.Add(garment);
            selected = Mathf.Max(0, items.FindIndex(g => g.id == ctx.Wardrobe.Equipped(category)));
            Select(selected);
        }
        public void Select(int index)
        {
            if (items.Count == 0) return;
            selected = Mathf.Clamp(index, 0, items.Count - 1); ctx.Wardrobe.Preview(items[selected].id);
            if (feedback) feedback.text = "Preview"; Refresh();
        }
        public bool Purchase()
        {
            if (!IsOpen || !shop.CanBrowse()) return false;
            bool success = ctx.Wardrobe.BuyOrWear(SelectedId, out string receipt);
            if (success) ctx.Sound("register", shop.browsePoints[anchorCategory].position, .45f);
            feedback.text = receipt; Refresh(); return success;
        }
        void Refresh()
        {
            if (!root || items.Count == 0) return;
            wallet.text = CityCommerce.Cash(ctx.Commerce.Balance);
            for (int i = 0; i < 3; i++) { tabs[i].image.color = i == category ? blue : mist; tabs[i].GetComponentInChildren<Text>().color = i == category ? Color.white : ink; }
            for (int i = 0; i < rows.Length; i++)
            {
                rows[i].gameObject.SetActive(i < items.Count); if (i >= items.Count) continue;
                var g = items[i]; rows[i].image.color = selected == i ? new Color(.72f, .84f, .85f) : new Color(.93f, .96f, .94f);
                rowNames[i].text = g.name; rowPrices[i].text = ctx.Wardrobe.Equipped(category) == g.id ? "Wearing" : ctx.Wardrobe.Owns(g.id) ? "Owned" : CityCommerce.Cash(g.price);
                swatches[i].color = g.color;
            }
            var item = items[selected]; itemName.text = item.name;
            bool wearing = ctx.Wardrobe.Equipped(category) == item.id, owned = ctx.Wardrobe.Owns(item.id);
            buyText.text = wearing ? "Wearing" : owned ? "Wear" : "Buy · " + CityCommerce.Cash(item.price);
            buy.interactable = !wearing && (owned || ctx.Commerce.Balance >= item.price);
            if (!owned && ctx.Commerce.Balance < item.price) feedback.text = "Need " + CityCommerce.Cash(item.price - ctx.Commerce.Balance);
        }
        void Layout()
        {
            // Compact ticket at the screen edge; no full-screen dimmer or empty preview area.
            panel.localScale = Vector3.one * Mathf.Min(1, (root.rect.width - 28) / 352);
            panel.sizeDelta = new Vector2(352, 430); panel.anchorMin = panel.anchorMax = new Vector2(1, .5f);
            panel.pivot = new Vector2(1, .5f); panel.anchoredPosition = new Vector2(-18, 0);
        }
        void Build()
        {
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); root = Rect("Tideline wardrobe", transform);
            canvas = root.gameObject.AddComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 65;
            var scaler = root.gameObject.AddComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 800); scaler.matchWidthOrHeight = 1; root.gameObject.AddComponent<GraphicRaycaster>();
            panel = Rect("Rack ticket", root); Paint(panel, new Color(.91f, .95f, .92f, .98f), true);
            var band = Rect("Tide band", panel); Box(band, 0, 0, 352, 68); Paint(band, blue);
            Label("Brand", panel, "T I D E L I N E", 21, Color.white, 18, 9, 266, 28, true);
            Label("Subtitle", panel, "CLOTHING · BAÍA DO SOL", 10, mist, 18, 38, 266, 17);
            var close = Button("Close wardrobe", panel, "×", Close, out _, blue, Color.white); Box((RectTransform)close.transform, 304, 12, 32, 32);
            string[] names = { "Tops", "Trousers", "Sneakers" };
            for (int i = 0; i < 3; i++) { int tab = i; tabs[i] = Button(names[i], panel, names[i], () => SetCategory(tab), out _, mist, ink); Box((RectTransform)tabs[i].transform, 16 + i * 108, 80, 104, 34); }
            for (int i = 0; i < 4; i++)
            {
                int index = i; rows[i] = Button("Garment " + i, panel, "", () => Select(index), out _, Color.white, ink);
                Box((RectTransform)rows[i].transform, 16, 124 + i * 46, 320, 42);
                var swatch = Rect("Fabric", rows[i].transform); Box(swatch, 9, 7, 25, 28); swatches[i] = Paint(swatch, coral);
                rowNames[i] = Label("Name", rows[i].transform, "", 14, ink, 44, 0, 180, 42, true);
                rowPrices[i] = Label("Price", rows[i].transform, "", 12, blue, 228, 0, 82, 42); rowPrices[i].alignment = TextAnchor.MiddleRight;
            }
            itemName = Label("Selected", panel, "", 15, ink, 18, 316, 228, 22, true);
            wallet = Label("Wallet", panel, "", 14, blue, 240, 316, 94, 22); wallet.alignment = TextAnchor.MiddleRight;
            feedback = Label("Status", panel, "Preview", 12, blue, 18, 339, 316, 20);
            buy = Button("Buy or wear", panel, "Buy", () => Purchase(), out buyText, blue, Color.white); Box((RectTransform)buy.transform, 16, 367, 320, 38);
            Label("Keys", panel, "← → Category    ↑ ↓ Piece    Enter Buy    E Close", 10, blue, 16, 407, 320, 18);
            root.gameObject.SetActive(false);
        }
        RectTransform Rect(string name, Transform parent) { var r = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>(); r.SetParent(parent, false); return r; }
        void Box(RectTransform r, float x, float y, float w, float h) { r.anchorMin = r.anchorMax = new Vector2(0, 1); r.pivot = new Vector2(0, 1); r.anchoredPosition = new Vector2(x, -y); r.sizeDelta = new Vector2(w, h); }
        Image Paint(RectTransform r, Color color, bool raycast = false) { var image = r.gameObject.AddComponent<Image>(); image.color = color; image.raycastTarget = raycast; return image; }
        Text Label(string name, Transform parent, string text, int size, Color color, float x, float y, float w, float h, bool bold = false)
        {
            var r = Rect(name, parent); Box(r, x, y, w, h); var t = r.gameObject.AddComponent<Text>(); t.font = font; t.text = text; t.fontSize = size; t.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
            t.color = color; t.alignment = TextAnchor.MiddleLeft; t.raycastTarget = false; t.supportRichText = false; return t;
        }
        Button Button(string name, Transform parent, string text, UnityEngine.Events.UnityAction action, out Text caption, Color background, Color foreground)
        {
            var r = Rect(name, parent); var image = Paint(r, background, true); var b = r.gameObject.AddComponent<Button>(); b.targetGraphic = image;
            var colors = b.colors; colors.highlightedColor = new Color(.83f, .92f, 1); colors.pressedColor = coral; colors.selectedColor = Color.white; colors.fadeDuration = .08f; b.colors = colors;
            // Keyboard selection is explicit and shared with the game input, including touch/mouse buttons.
            b.navigation = new Navigation { mode = Navigation.Mode.None }; b.onClick.AddListener(action);
            caption = Label("Caption", r, text, 14, foreground, 4, 0, 312, 38, true);
            caption.rectTransform.anchorMin = Vector2.zero; caption.rectTransform.anchorMax = Vector2.one; caption.rectTransform.offsetMin = new Vector2(4, 0); caption.rectTransform.offsetMax = new Vector2(-4, 0); caption.alignment = TextAnchor.MiddleCenter;
            return b;
        }
        void OnDisable() { if (ctx && ctx.Wardrobe) ctx.Wardrobe.CancelPreview(); }
    }
}
