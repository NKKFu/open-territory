using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace OpenTerritory
{
    // A requested city chart, never a permanent HUD. Simulation keeps running.
    public sealed class CityMapUI : MonoBehaviour
    {
        public sealed class Place { public string name, kind; public Vector2 point; public bool bus, overview; }
        readonly List<Place> places = new List<Place>();
        readonly List<Text> placeLabels = new List<Text>();
        readonly List<Rect> labelBounds = new List<Rect>();
        readonly List<RectTransform> labelObstacles = new List<RectTransform>();
        readonly Vector3[] controlCorners = new Vector3[4];
        CityContext ctx;
        CityUI owner;
        Font font;
        RectTransform root, viewport, content, sidebar, footer, zoomControls;
        CityMapSymbols symbols;
        Texture2D terrain;
        Text selection, distance, zoomText, scaleText, help, legend;
        Button clear, stops, zoomIn, zoomOut, compactClear;
        Button guideToggle, guideClose;
        RectTransform scaleBar;
        GameObject chart;
        bool built, swallow, compactGuide;
        int closedFrame = -10;
        Vector2 center;
        float zoom = 1, fitScale = 1;
        Vector2 lastSize;
        readonly Color ink = new Color(.90f, .94f, .91f), muted = new Color(.64f, .76f, .77f);
        readonly Color panel = new Color(.035f, .105f, .14f), buttonColor = new Color(.09f, .21f, .25f), gold = new Color(.99f, .79f, .47f);
        public bool IsOpen { get; private set; }
        public bool ShowStops { get; private set; } = true;
        public Vector2? Marker { get; private set; }
        public string MarkerName { get; private set; }
        public IReadOnlyList<Place> Places => places;
        public Rect WorldBounds { get; private set; }
        public Vector2 Center => center;
        public float Zoom => zoom;
        public float Scale => fitScale * zoom;
        public RectTransform Viewport => viewport;
        public Vector2 PlayerPoint => new Vector2(ctx.Player.position.x, ctx.Player.position.z);
        public float PlayerYaw => ctx.Player.eulerAngles.y;

        public void Initialize(CityUI ui, CityContext context) { owner = ui; ctx = context; }

        // Called immediately after Poll, before any game system can consume M or a map click.
        public void SampleInput()
        {
            bool wasOpen = IsOpen;
            var input = ctx.Input;
            if (ctx.Paused) { if (IsOpen) SetOpen(false); }
            else if (input.MapToggle || IsOpen && input.Pause)
            {
                SetOpen(!IsOpen); input.Pause = false;
            }
            if (IsOpen)
            {
                Pan(-input.Move * (420 * Time.unscaledDeltaTime));
                if (UnityEngine.Input.GetKeyDown(KeyCode.Equals) || UnityEngine.Input.GetKeyDown(KeyCode.KeypadPlus)) ZoomAt(1.25f, Vector2.zero);
                if (UnityEngine.Input.GetKeyDown(KeyCode.Minus) || UnityEngine.Input.GetKeyDown(KeyCode.KeypadMinus)) ZoomAt(.8f, Vector2.zero);
                if (UnityEngine.Input.GetKeyDown(KeyCode.C)) LocatePlayer();
                if (UnityEngine.Input.GetKeyDown(KeyCode.Home)) FitCity();
                if (UnityEngine.Input.GetKeyDown(KeyCode.Delete) || UnityEngine.Input.GetKeyDown(KeyCode.Backspace)) ClearMarker();
            }
            if (wasOpen || IsOpen || swallow || Time.frameCount <= closedFrame + 1)
            {
                Suppress(input);
                swallow = UnityEngine.Input.GetMouseButton(0) || UnityEngine.Input.GetMouseButton(1) || UnityEngine.Input.GetKey(CityInput.HackKey);
            }
        }

        static void Suppress(CityInput i)
        {
            i.Move = i.Drive = i.Look = Vector2.zero; i.Zoom = 0;
            i.CrouchPressed = false;
            i.Run = i.Jump = i.Interact = i.Talk = i.TalkPressed = i.TalkHeld = i.TalkReleased = false;
            i.HackPressed = i.HackHeld = i.HackReleased = i.HackerToggle = i.HackNext = i.Drone = false;
            i.Brake = i.Handbrake = i.Horn = i.LeftSignal = i.RightSignal = i.Emergency = false;
            i.AimHeld = i.AimToggle = i.Fire = i.FireHeld = i.Reload = i.Holster = i.Fall = i.MeleePressed = false;
            i.Weapon = 0;
        }

        public void SetOpen(bool value)
        {
            if (!ctx || value && ctx.Paused || value == IsOpen) return;
            if (value && !built) Build();
            IsOpen = value;
            if (value && ctx.Clothing) ctx.Clothing.Close();
            owner.ClearTouch();
            if (ctx.Emotes) { ctx.Emotes.CancelInput(); ctx.Emotes.CancelPerformance(); }
            if (ctx.Combat) ctx.Combat.ReleaseMeleeInput();
            chart.SetActive(value);
            if (value) { Layout(); Refresh(); }
            else { closedFrame = Time.frameCount; swallow = true; compactGuide = false; }
            // Do not carry selected buttons back into the running game.
            if (EventSystem.current) EventSystem.current.SetSelectedGameObject(null);
            ctx.RefreshCursor();
        }

        void Build()
        {
            built = true; font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var g = ctx.Data.ground;
            WorldBounds = ReferenceMap.Active ? UnityEngine.Rect.MinMaxRect(ReferenceMap.Layout.minX, ReferenceMap.Layout.minZ, ReferenceMap.Layout.maxX, ReferenceMap.Layout.maxZ) :
                g != null ? new Rect(g.x, g.z, (g.width - 1) * g.step, (g.height - 1) * g.step) : new Rect(-400, -400, 800, 800);
            center = WorldBounds.center;
            root = Rect("Mapa de Baía do Sol", transform); chart = root.gameObject;
            var canvas = root.gameObject.AddComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 80;
            var scaler = root.gameObject.AddComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 800); scaler.matchWidthOrHeight = 1;
            root.gameObject.AddComponent<GraphicRaycaster>(); Paint(root, panel, true);

            viewport = Rect("Carta navegável", root); Stretch(viewport, 16, 292, 88, 96);
            Paint(viewport, new Color(.18f, .35f, .44f), true); viewport.gameObject.AddComponent<RectMask2D>();
            viewport.gameObject.AddComponent<CityMapSurface>().Map = this;
            content = Rect("Terreno e ruas", viewport); content.anchorMin = content.anchorMax = new Vector2(.5f, .5f); content.sizeDelta = WorldBounds.size;
            terrain = CityMapGraphic.Terrain(ctx.Data);
            var image = content.gameObject.AddComponent<RawImage>(); image.texture = terrain; image.raycastTarget = false;
            var roads = Rect("Ruas e imóveis", content); Stretch(roads); roads.gameObject.AddComponent<CityMapGraphic>().Initialize(ctx.Data, WorldBounds);
            var symbolRect = Rect("Posições", viewport); Stretch(symbolRect);
            symbols = symbolRect.gameObject.AddComponent<CityMapSymbols>(); symbols.Map = this; symbols.raycastTarget = false;
            AddPlaces();
            foreach (var place in places)
            {
                var label = Label("Local · " + place.name, viewport, place.name + (place.kind == "armory" ? " · Armaria" : ""), 13, ink);
                label.fontStyle = FontStyle.Bold;
                label.gameObject.AddComponent<Shadow>().effectColor = new Color(.025f, .10f, .13f, .9f);
                label.rectTransform.sizeDelta = new Vector2(184, 24); label.rectTransform.pivot = new Vector2(0, .5f);
                placeLabels.Add(label);
            }

            var eyebrow = Label("Tipo de mapa", root, "EXPLORAR  /  MAPA DA CIDADE", 11, gold); Box(eyebrow.rectTransform, 24, 13, 340, 18);
            var title = Label("Baía do Sol", root, "BAÍA DO SOL", 30, ink); title.fontStyle = FontStyle.Bold; Box(title.rectTransform, 22, 31, 350, 39);
            var close = MakeButton("Fechar mapa", root, "Fechar  ·  M", () => SetOpen(false));
            var cr = (RectTransform)close.transform; cr.anchorMin = cr.anchorMax = Vector2.one; cr.pivot = Vector2.one;
            cr.anchoredPosition = new Vector2(-20, -22); cr.sizeDelta = new Vector2(132, 44);

            var north = Label("Norte", viewport, "N\n↑", 22, ink, TextAnchor.UpperCenter); Box(north.rectTransform, 14, 12, 28, 60);
            labelObstacles.Add(north.rectTransform);
            zoomControls = Rect("Controles do mapa", viewport); zoomControls.anchorMin = zoomControls.anchorMax = Vector2.one;
            zoomControls.pivot = Vector2.one; zoomControls.anchoredPosition = new Vector2(-12, -12); zoomControls.sizeDelta = new Vector2(44, 144);
            labelObstacles.Add(zoomControls);
            zoomIn = MakeButton("Aproximar mapa", zoomControls, "+", () => ZoomAt(1.35f, Vector2.zero)); Box((RectTransform)zoomIn.transform, 0, 0, 44, 44);
            zoomOut = MakeButton("Afastar mapa", zoomControls, "−", () => ZoomAt(1 / 1.35f, Vector2.zero)); Box((RectTransform)zoomOut.transform, 0, 50, 44, 44);
            zoomText = Label("Zoom", zoomControls, "", 12, ink, TextAnchor.MiddleCenter); Box(zoomText.rectTransform, 0, 100, 44, 28);
            compactClear = MakeButton("Limpar marca no mapa compacto", viewport, "Limpar marca", ClearMarker);
            var compactRect = (RectTransform)compactClear.transform; compactRect.anchorMin = compactRect.anchorMax = Vector2.one; compactRect.pivot = Vector2.one;
            compactRect.anchoredPosition = new Vector2(-12, -150); compactRect.sizeDelta = new Vector2(132, 36); compactClear.gameObject.SetActive(false);
            labelObstacles.Add(compactRect);

            var locate = MakeButton("Minha posição", viewport, "Minha posição · C", LocatePlayer);
            Bottom((RectTransform)locate.transform, 12, 12, 174, 40);
            var fit = MakeButton("Ver toda a baía", viewport, "Toda a baía", FitCity); Bottom((RectTransform)fit.transform, 194, 12, 122, 40);
            guideToggle = MakeButton("Abrir locais", viewport, "Locais", ToggleGuide); Bottom((RectTransform)guideToggle.transform, 12, 62, 110, 36);
            labelObstacles.Add((RectTransform)locate.transform); labelObstacles.Add((RectTransform)fit.transform); labelObstacles.Add((RectTransform)guideToggle.transform);
            scaleBar = Rect("Escala", viewport); scaleBar.anchorMin = scaleBar.anchorMax = new Vector2(1, 0); scaleBar.pivot = Vector2.one;
            scaleBar.anchoredPosition = new Vector2(-16, 30); scaleBar.sizeDelta = new Vector2(100, 3); Paint(scaleBar, ink);
            scaleText = Label("Distância da escala", viewport, "", 12, ink, TextAnchor.LowerRight);
            scaleText.rectTransform.anchorMin = scaleText.rectTransform.anchorMax = new Vector2(1, 0); scaleText.rectTransform.pivot = Vector2.one;
            scaleText.rectTransform.anchoredPosition = new Vector2(-16, 36); scaleText.rectTransform.sizeDelta = new Vector2(100, 24);
            labelObstacles.Add(scaleText.rectTransform);

            sidebar = Rect("Guia da cidade", root); sidebar.anchorMin = new Vector2(1, 0); sidebar.anchorMax = Vector2.one;
            sidebar.offsetMin = new Vector2(-276, 96); sidebar.offsetMax = new Vector2(-16, -88); Paint(sidebar, new Color(.055f, .14f, .18f), true);
            var guide = Label("Locais", sidebar, "CONHEÇA A BAÍA", 12, gold); Box(guide.rectTransform, 16, 10, 190, 22);
            guideClose = MakeButton("Fechar locais", sidebar, "×", ToggleGuide); Box((RectTransform)guideClose.transform, 216, 9, 32, 30);
            var destinations = places.FindAll(p => !p.bus);
            destinations.Sort((a, b) => string.Compare(CityEnglish.Text(a.name), CityEnglish.Text(b.name), CultureInfo.GetCultureInfo("en-US"), CompareOptions.IgnoreCase));
            var count = Label("Quantidade de locais", sidebar, destinations.Count + " locais · role ou arraste", 11, muted); Box(count.rectTransform, 16, 32, 230, 18);
            var list = Rect("Lista de locais", sidebar); Stretch(list, 12, 24, 58, 190);
            Paint(list, new Color(.055f, .14f, .18f), true); list.gameObject.AddComponent<RectMask2D>();
            var entries = Rect("Destinos", list); entries.anchorMin = new Vector2(0, 1); entries.anchorMax = Vector2.one; entries.pivot = new Vector2(0, 1);
            entries.sizeDelta = new Vector2(0, Mathf.Max(0, destinations.Count * 52 - 6));
            var scroll = list.gameObject.AddComponent<ScrollRect>(); scroll.viewport = list; scroll.content = entries;
            scroll.horizontal = false; scroll.vertical = true; scroll.movementType = ScrollRect.MovementType.Clamped; scroll.scrollSensitivity = 34;
            var track = Rect("Rolagem dos locais", sidebar); Stretch(track, 248, 6, 58, 190); Paint(track, buttonColor, true);
            var handle = Rect("Posição na lista", track); Stretch(handle); var handleImage = Paint(handle, gold, true);
            var scrollbar = track.gameObject.AddComponent<Scrollbar>(); scrollbar.handleRect = handle; scrollbar.targetGraphic = handleImage;
            scrollbar.direction = Scrollbar.Direction.BottomToTop; scrollbar.navigation = new Navigation { mode = Navigation.Mode.None };
            scroll.verticalScrollbar = scrollbar;
            for (int index = 0; index < destinations.Count; index++)
            {
                var entry = destinations[index];
                var b = MakeButton("Visitar no mapa · " + entry.name, entries, entry.name, () => SelectPlace(entry));
                var br = (RectTransform)b.transform; br.anchorMin = new Vector2(0, 1); br.anchorMax = Vector2.one; br.pivot = new Vector2(0, 1);
                br.anchoredPosition = new Vector2(0, -index * 52); br.sizeDelta = new Vector2(0, 46);
                var caption = b.GetComponentInChildren<Text>(); caption.fontSize = 14; caption.alignment = TextAnchor.MiddleLeft; Stretch(caption.rectTransform, 10, 6, 2, 21);
                var category = Label("Tipo de local", br, Category(entry.kind), 11, muted); Stretch(category.rectTransform, 10, 6, 24, 4);
            }
            stops = MakeButton("Mostrar paradas", sidebar, "Paradas de ônibus · ligadas", ToggleStops); Bottom((RectTransform)stops.transform, 12, 145, 236, 34);
            stops.GetComponentInChildren<Text>().fontSize = 13;
            selection = Label("Ponto escolhido", sidebar, "Seu próximo passeio", 17, ink); selection.fontStyle = FontStyle.Bold; Bottom(selection.rectTransform, 16, 98, 228, 40);
            distance = Label("Distância do ponto", sidebar, "Clique na carta para marcar um lugar.", 13, muted); Bottom(distance.rectTransform, 16, 55, 228, 40);
            clear = MakeButton("Remover marcador", sidebar, "Remover marcador", ClearMarker); Bottom((RectTransform)clear.transform, 12, 10, 236, 36);

            footer = Rect("Como explorar o mapa", root); footer.anchorMin = Vector2.zero; footer.anchorMax = new Vector2(1, 0); footer.pivot = Vector2.zero; footer.sizeDelta = new Vector2(0, 80);
            help = Label("Controles do mapa", footer, "Clique para marcar  ·  Arraste ou use WASD para mover  ·  Roda / + − para zoom\nC · sua posição     Home · toda a baía     Del · remover marca     M / Esc · voltar", 14, ink);
            Stretch(help.rectTransform, 24, 24, 2, 29);
            legend = Label("Legenda", footer, "", 12, muted); Stretch(legend.rectTransform, 24, 24, 51, 7);
            chart.SetActive(false);
        }

        void AddPlaces()
        {
            var catalog = UnityEngine.Object.FindFirstObjectByType<CityHomeCatalog>();
            if(catalog)foreach(var home in catalog.homes)
                if(home.units>1&&home.root)AddPlace(home.name,home.root.position,"apartments");
            foreach (var cemetery in FindObjectsByType<CityCemetery>(FindObjectsSortMode.None))
                AddPlace(CityCemetery.DisplayName, cemetery.transform.position, "cemetery", true);
            foreach (var park in FindObjectsByType<CityPark>(FindObjectsSortMode.None))
                AddPlace(CityPark.DisplayName, park.picnicCenter, "park", true);
            foreach (var shop in FindObjectsByType<CityCarDealership>(FindObjectsSortMode.None))
                AddPlace(CityCarDealership.DisplayName, shop.transform.position, "car-dealership", true);
            foreach (var house in FindObjectsByType<CityBlackthornHouse>(FindObjectsSortMode.None))
                AddPlace(CityBlackthornHouse.DisplayName, house.transform.position, "occult", true);
            foreach (var shop in FindObjectsByType<CityClothingShop>(FindObjectsSortMode.None))
                AddPlace(CityClothingShop.DisplayName, shop.transform.position, "clothing", true);
            AddPlace("Fonte da praça", ctx.Data.fountain, "square", true);
            AddPlace("Galpão da Maré", ctx.Data.fish.position, "fish", true);
            AddPlace("Cinema ao ar livre", ctx.Data.cinema.projector, "cinema", true);
            if (ReferenceMap.Active) AddPlace("Porto da baía", ReferenceMap.Layout.harborRadio, "harbor", true);
            foreach (var p in ctx.Data.places)
            {
                // Public destinations come from the city manifest, including new shop types.
                if (p.kind == "home" || p.kind == "residence" || string.IsNullOrWhiteSpace(p.name) || p.name.StartsWith("#")) continue;
                string name = p.kind == "hospital" ? "Hospital da Baía" : DisplayName(p.name);
                AddPlace(name, new Vector3(p.x, 0, p.z), p.kind, p.kind == "armory");
            }
            if (ctx.Data.docks.Length > 0)
                AddPlace("Marina", new Vector3(ctx.Data.docks[0].x, 0, ctx.Data.docks[0].z), "marina");
            else
            {
                var marina = System.Array.Find(ctx.Data.stops, p => p.name == "Marina");
                if (marina != null) AddPlace("Marina", new Vector3(marina.x, 0, marina.z), "marina");
            }
            foreach (var service in ctx.Data.services)
            {
                string name = service.kind == "bank" ? "Caixa · " + NearestBuilding(service.position) :
                    service.kind == "range-card" ? "Estande de tiro" : service.kind == "street-music" ? "Roda do Ipê" :
                    service.kind == "harbor-crane" ? "Guindaste do porto" : service.kind == "harbor-drone" ? "Estação de drones" :
                    service.kind == "harbor-weather" ? "Posto meteorológico" : service.kind == "district-guide" ? "Guia da cidade" : null;
                if (name != null) AddPlace(name, service.position, service.kind);
            }
            foreach (var stop in ctx.Data.stops) places.Add(new Place { name = "Parada · " + stop.name, point = new Vector2(stop.x, stop.z), bus = true });
        }
        void AddPlace(string name, Vector3 p, string kind, bool overview = false)
        {
            var point = new Vector2(p.x, p.z);
            if (places.Exists(place => place.name == name && (place.point - point).sqrMagnitude < 16)) return;
            places.Add(new Place { name = name, point = point, kind = kind, overview = overview });
        }
        static string DisplayName(string name) => CultureInfo.GetCultureInfo("en-US").TextInfo.ToTitleCase(name.ToLowerInvariant())
            .Replace(" Da ", " da ").Replace(" Do ", " do ").Replace(" Das ", " das ").Replace(" Dos ", " dos ").Replace(" De ", " de ");
        string NearestBuilding(Vector3 point)
        {
            Place nearest = null; float best = float.MaxValue;
            foreach (var place in places)
            {
                if (place.kind == "bank" || place.kind == "district-guide") continue;
                float distance = (place.point - new Vector2(point.x, point.z)).sqrMagnitude;
                if (distance < best) { best = distance; nearest = place; }
            }
            return nearest == null ? "Baía" : nearest.name;
        }
        static string Category(string kind)
        {
            switch (kind)
            {
                case "park": return "Park and gardens";
                case "apartments": return "Apartamentos · 4 unidades";
                case "armory": return "Armaria · armas e munição";
                case "bakery": return "Padaria";
                case "flower": return "Floricultura";
                case "gallery": return "Galeria de arte";
                case "book": return "Livraria";
                case "cafe": return "Café";
                case "restaurant": return "Restaurante";
                case "market": return "Mercado";
                case "station": return "Posto de combustível";
                case "hospital": return "Hospital";
                case "police": return "Delegacia";
                case "fire": return "Bombeiros";
                case "bank": return "Caixa eletrônico";
                case "range-card": return "Prática de tiro";
                case "street-music": return "Música ao vivo";
                case "square": return "Praça";
                case "fish": return "Peixaria";
                case "cinema": return "Cinema ao ar livre";
                case "cemetery": return "Cemitério e capela";
                case "clothing": return "Clothing & wardrobe";
                case "marina": return "Píer e embarcações";
                case "harbor": return "Porto e rádio";
                case "harbor-crane": return "Guindaste";
                case "harbor-drone": return "Drones";
                case "harbor-weather": return "Previsão do tempo";
                case "district-guide": return "Mapa da cidade";
                default: return "Local da cidade";
            }
        }
        void SelectPlace(Place place)
        {
            Focus(place.point, 4.5f); Mark(place.point, place.name);
            if (root.rect.width < 1000) { compactGuide = false; Layout(); }
        }
        void ToggleGuide() { compactGuide = !compactGuide; Layout(); }

        void Layout()
        {
            Canvas.ForceUpdateCanvases();
            bool wide = root.rect.width >= 1000;
            sidebar.gameObject.SetActive(wide || compactGuide);
            guideToggle.gameObject.SetActive(!wide); guideClose.gameObject.SetActive(!wide);
            help.text = wide ? "Clique para marcar  ·  Arraste ou use WASD para mover  ·  Roda / + − para zoom\nC · sua posição     Home · toda a baía     Del · remover marca     M / Esc · voltar" :
                "Arraste para mover · Roda / + − para zoom\nToque para marcar · M / Esc para voltar";
            Stretch(viewport, 16, wide ? 292 : 16, 88, 96);
            Canvas.ForceUpdateCanvases();
            lastSize = root.rect.size;
            fitScale = Mathf.Max(.001f, Mathf.Min((viewport.rect.width - 48) / WorldBounds.width, (viewport.rect.height - 48) / WorldBounds.height));
            ApplyView();
        }
        void ApplyView()
        {
            var half = viewport.rect.size / (2 * Scale);
            center.x = half.x * 2 >= WorldBounds.width ? WorldBounds.center.x : Mathf.Clamp(center.x, WorldBounds.xMin + half.x, WorldBounds.xMax - half.x);
            center.y = half.y * 2 >= WorldBounds.height ? WorldBounds.center.y : Mathf.Clamp(center.y, WorldBounds.yMin + half.y, WorldBounds.yMax - half.y);
            content.localScale = Vector3.one * Scale; content.anchoredPosition = (WorldBounds.center - center) * Scale;
            if (symbols) symbols.SetVerticesDirty();
            Refresh();
        }
        public Vector2 WorldToLocal(Vector2 point) => viewport.rect.center + (point - center) * Scale;
        public Vector2 LocalToWorld(Vector2 point) => center + (point - viewport.rect.center) / Scale;
        public void Pan(Vector2 delta) { if (!IsOpen || delta.sqrMagnitude == 0) return; center -= delta / Scale; ApplyView(); }
        public void ZoomAt(float factor, Vector2 local)
        {
            if (!IsOpen || factor <= 0 || float.IsNaN(factor) || float.IsInfinity(factor)) return;
            var anchor = LocalToWorld(local); zoom = Mathf.Clamp(zoom * factor, 1, 10);
            center = anchor - (local - viewport.rect.center) / Scale; ApplyView();
        }
        public void FitCity() { if (!IsOpen) return; zoom = 1; center = WorldBounds.center; ApplyView(); }
        public void Focus(Vector2 point, float level) { if (!IsOpen) return; center = point; zoom = Mathf.Clamp(level, 1, 10); ApplyView(); }
        public void LocatePlayer() { Focus(PlayerPoint, Mathf.Max(3, zoom)); }
        public void Pick(Vector2 local)
        {
            if (!IsOpen || !viewport.rect.Contains(local)) return;
            var point = LocalToWorld(local);
            if (!WorldBounds.Contains(point)) return;
            Place nearest = null; float best = 18 * 18;
            foreach (var p in places)
            {
                if (p.bus && !ShowStops) continue;
                float d = (WorldToLocal(p.point) - local).sqrMagnitude;
                if (d < best) { best = d; nearest = p; }
            }
            Mark(nearest == null ? point : nearest.point, nearest == null ? "Ponto marcado" : nearest.name);
        }
        public void Mark(Vector2 point, string name)
        {
            if (!IsOpen || !WorldBounds.Contains(point)) return;
            Marker = point; MarkerName = name; Refresh();
        }
        public void ClearMarker() { Marker = null; MarkerName = null; if (built) Refresh(); }
        public void ToggleStops() { ShowStops = !ShowStops; Refresh(); }
        public float MarkerDistance => Marker.HasValue ? Vector2.Distance(PlayerPoint, Marker.Value) : 0;
        static string Meters(float value) => value >= 1000 ? (value / 1000).ToString("0.0", System.Globalization.CultureInfo.GetCultureInfo("en-US")) + " km" : Mathf.RoundToInt(value) + " m";

        void Refresh()
        {
            if (!built) return;
            zoomText.text = zoom.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + "×";
            zoomIn.interactable = zoom < 9.999f; zoomOut.interactable = zoom > 1.001f;
            selection.text = Marker.HasValue ? MarkerName : "Seu próximo passeio";
            distance.text = Marker.HasValue ? Meters(MarkerDistance) + " de você\nDistância em linha reta" : "Clique na carta para marcar um lugar.";
            clear.interactable = Marker.HasValue;
            bool compact = root.rect.width < 1000;
            compactClear.gameObject.SetActive(compact && Marker.HasValue);
            legend.text = compact ? Marker.HasValue ? MarkerName + " · " + Meters(MarkerDistance) + " de você · Distância em linha reta" :
                "▲ Você    ● Locais    ● Paradas    ◉ Seu marcador" :
                "▲ Você     ● Locais     ● Paradas     ◉ Seu marcador     /     A cidade continua em movimento";
            stops.GetComponentInChildren<Text>().text = "Paradas de ônibus · " + (ShowStops ? "ligadas" : "ocultas");
            float meters = Scale * 500 <= 120 ? 500 : Scale * 200 <= 120 ? 200 : Scale * 100 <= 120 ? 100 : 50;
            scaleBar.sizeDelta = new Vector2(meters * Scale, 3); scaleText.text = Meters(meters);
            labelBounds.Clear();
            foreach (var obstacle in labelObstacles)
            {
                if (!obstacle.gameObject.activeInHierarchy) continue;
                obstacle.GetWorldCorners(controlCorners);
                var min = (Vector2)viewport.InverseTransformPoint(controlCorners[0]); var max = (Vector2)viewport.InverseTransformPoint(controlCorners[2]);
                labelBounds.Add(UnityEngine.Rect.MinMaxRect(min.x - 4, min.y - 4, max.x + 4, max.y + 4));
            }
            // Selected destinations get their name even in the dense town center.
            for (int i = 0; i < places.Count; i++) if (Selected(places[i])) PositionLabel(i, true);
            for (int i = 0; i < places.Count; i++) if (!Selected(places[i]) && places[i].kind == "armory") PositionLabel(i, false);
            for (int i = 0; i < places.Count; i++)
            {
                if (!Selected(places[i]) && places[i].kind != "armory") PositionLabel(i, false);
            }
            if (symbols) symbols.SetVerticesDirty();
        }
        bool Selected(Place place) => Marker.HasValue && MarkerName == place.name && (Marker.Value - place.point).sqrMagnitude < .01f;
        void PositionLabel(int index, bool selected)
        {
            var place = places[index]; var label = placeLabels[index]; var point = WorldToLocal(place.point);
            float width = Mathf.Min(224, label.preferredWidth + 8);
            label.gameObject.SetActive(false);
            if (place.bus && !ShowStops || zoom < 2.4f && !place.overview && !selected || !viewport.rect.Contains(point)) return;
            for (int attempt = 0; attempt < 6; attempt++)
            {
                float dy = attempt < 2 ? 0 : attempt < 4 ? 28 : -28;
                var position = point + new Vector2(attempt % 2 == 0 ? 12 : -width - 12, dy);
                var area = new Rect(position.x, position.y - 12, width, 24);
                if (!viewport.rect.Contains(area.min) || !viewport.rect.Contains(area.max)) continue;
                bool blocked = false;
                foreach (var occupied in labelBounds) if (occupied.Overlaps(area)) { blocked = true; break; }
                if (blocked) continue;
                label.gameObject.SetActive(true); label.rectTransform.sizeDelta = area.size; label.rectTransform.anchoredPosition = position;
                area.xMin -= 6; area.xMax += 6; area.yMin -= 3; area.yMax += 3; labelBounds.Add(area); return;
            }
        }
        void LateUpdate()
        {
            if (!IsOpen) return;
            if (root.rect.size != lastSize) Layout();
            Refresh();
        }
        void OnDisable() { if (IsOpen) SetOpen(false); }
        void OnDestroy() { if (terrain && terrain != Texture2D.whiteTexture) Destroy(terrain); }

        RectTransform Rect(string name, Transform parent)
        {
            var r = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>(); r.SetParent(parent, false); return r;
        }
        static void Stretch(RectTransform r, float left = 0, float right = 0, float top = 0, float bottom = 0)
        { r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = new Vector2(left, bottom); r.offsetMax = new Vector2(-right, -top); }
        static void Box(RectTransform r, float x, float y, float w, float h)
        { r.anchorMin = r.anchorMax = new Vector2(0, 1); r.pivot = new Vector2(0, 1); r.anchoredPosition = new Vector2(x, -y); r.sizeDelta = new Vector2(w, h); }
        static void Bottom(RectTransform r, float x, float y, float w, float h)
        { r.anchorMin = r.anchorMax = Vector2.zero; r.pivot = Vector2.zero; r.anchoredPosition = new Vector2(x, y); r.sizeDelta = new Vector2(w, h); }
        static Image Paint(RectTransform r, Color c, bool hit = false)
        { var i = r.gameObject.AddComponent<Image>(); i.color = c; i.raycastTarget = hit; return i; }
        Text Label(string name, Transform parent, string text, int size, Color color, TextAnchor alignment = TextAnchor.MiddleLeft)
        {
            var r = Rect(name, parent); var t = r.gameObject.AddComponent<CityEnglishText>(); t.font = font; t.fontSize = size; t.color = color;
            t.text = text; t.alignment = alignment; t.raycastTarget = false; t.supportRichText = false; return t;
        }
        Button MakeButton(string name, Transform parent, string text, UnityEngine.Events.UnityAction action)
        {
            var r = Rect(name, parent); var image = Paint(r, buttonColor, true); var b = r.gameObject.AddComponent<Button>(); b.targetGraphic = image;
            var c = b.colors; c.highlightedColor = new Color(.65f, .89f, .91f); c.selectedColor = c.highlightedColor;
            c.pressedColor = gold; c.disabledColor = new Color(.47f, .55f, .56f, .65f); c.fadeDuration = .09f; b.colors = c;
            b.navigation = new Navigation { mode = Navigation.Mode.None }; b.onClick.AddListener(action);
            var label = Label("Texto", r, text, 16, ink, TextAnchor.MiddleCenter); Stretch(label.rectTransform, 5, 5, 1, 1); return b;
        }
    }
}
