using UnityEngine;
using UnityEngine.UI;

namespace OpenTerritory
{
    public sealed partial class CityCanvasUI
    {
        RectTransform hudSafeArea, weaponHud, vehicleHud;
        CityVignetteGraphic vignette;
        CityHudGraphic weaponIcon, leftSignalIcon, rightSignalIcon, headlightIcon;
        Text weaponName, weaponAmmo, vehicleSpeed;
        string hudWeapon;
        readonly Color hudDim = new Color(.38f, .48f, .49f);
        readonly Color hudAmber = new Color(1, .77f, .39f);

        public bool WeaponHudVisible => weaponHud && weaponHud.gameObject.activeInHierarchy;
        public bool VehicleHudVisible => vehicleHud && vehicleHud.gameObject.activeInHierarchy;
        public bool VignetteVisible => vignette && vignette.isActiveAndEnabled;

        void BuildStatusHud()
        {
            var edge = Rect("Vinheta suave", root); Stretch(edge);
            vignette = edge.gameObject.AddComponent<CityVignetteGraphic>();
            vignette.color = new Color(.015f, .025f, .035f, .24f); vignette.raycastTarget = false;
            hudSafeArea = Rect("Instrumentos · área segura", root); Stretch(hudSafeArea);

            weaponHud = HudPanel("Arma na mão", 212, 68);
            weaponIcon = HudIcon("Silhueta da arma", weaponHud, CityHudSymbol.Pulse, 12, 18, 56, 32, ink);
            weaponName = Label("Arma equipada", weaponHud, "", 15, ink); weaponName.fontStyle = FontStyle.Bold;
            HudBox(weaponName.rectTransform, 78, 10, 124, 23);
            weaponAmmo = Label("Munição", weaponHud, "", 14, muted); HudBox(weaponAmmo.rectTransform, 78, 35, 124, 22);

            vehicleHud = HudPanel("Instrumentos do carro", 166, 94);
            leftSignalIcon = HudIcon("Seta esquerda", vehicleHud, CityHudSymbol.LeftSignal, 16, 12, 22, 18, hudDim);
            headlightIcon = HudIcon("Faróis", vehicleHud, CityHudSymbol.Headlight, 70, 11, 26, 20, hudDim);
            rightSignalIcon = HudIcon("Seta direita", vehicleHud, CityHudSymbol.RightSignal, 128, 12, 22, 18, hudDim);
            vehicleSpeed = Label("Velocidade", vehicleHud, "0", 38, ink, TextAnchor.MiddleRight);
            vehicleSpeed.fontStyle = FontStyle.Bold; HudBox(vehicleSpeed.rectTransform, 10, 36, 96, 49);
            var unit = Label("Unidade", vehicleHud, "km/h", 13, muted); HudBox(unit.rectTransform, 112, 58, 44, 21);
            weaponHud.gameObject.SetActive(false); vehicleHud.gameObject.SetActive(false);
        }

        void HudBox(RectTransform rect, float x, float y, float width, float height)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0, 1); rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(x, -y); rect.sizeDelta = new Vector2(width, height);
        }
        RectTransform HudPanel(string name, float width, float height)
        {
            var rect = Rect(name, hudSafeArea); rect.anchorMin = rect.anchorMax = new Vector2(1, 0);
            rect.pivot = new Vector2(1, 0); rect.sizeDelta = new Vector2(width, height);
            var background = rect.gameObject.AddComponent<CityHudGraphic>(); background.Symbol = CityHudSymbol.Panel;
            background.color = new Color(.035f, .09f, .12f, .83f); background.raycastTarget = false;
            return rect;
        }
        CityHudGraphic HudIcon(string name, Transform parent, CityHudSymbol symbol, float x, float y, float width, float height, Color tint)
        {
            var rect = Rect(name, parent); HudBox(rect, x, y, width, height);
            var icon = rect.gameObject.AddComponent<CityHudGraphic>(); icon.Symbol = symbol;
            icon.color = tint; icon.raycastTarget = false; return icon;
        }
        void RefreshStatusHud()
        {
            bool world = !ctx.Paused && !owner.MapOpen;
            vignette.enabled = options.vignette && !owner.MapOpen;
            var car = ctx.Vehicles.Active;
            bool inCar = world && car != null && ctx.Motor.Riding == car.Root && car.Data.kind != "bike" && !ctx.Motor.Remote;
            bool armed = world && !inCar && ctx.Combat.WeaponVisible && !(ctx.Emotes && ctx.Emotes.WheelOpen);
            weaponHud.gameObject.SetActive(armed); vehicleHud.gameObject.SetActive(inCar);
            if (!armed && !inCar) return;

            // Keep the instruments inside display cutouts and above the existing touch cluster.
            var safe = Screen.safeArea; float scale = Mathf.Max(.001f, canvas.scaleFactor);
            hudSafeArea.offsetMin = new Vector2(safe.xMin, safe.yMin) / scale;
            hudSafeArea.offsetMax = new Vector2(safe.xMax - Screen.width, safe.yMax - Screen.height) / scale;
            float bottom = owner.TouchEnabled ? 252 : 24;
            bottom = Mathf.Min(bottom, Mathf.Max(12, hudSafeArea.rect.height - 106));
            weaponHud.anchoredPosition = vehicleHud.anchoredPosition = new Vector2(-24, bottom);

            if (armed)
            {
                string id = ctx.Combat.Equipped;
                if (hudWeapon != id)
                {
                    hudWeapon = id; weaponName.text = ctx.Combat.Definition(id)?.Name ?? "";
                    weaponIcon.Symbol = id == "carbine" ? CityHudSymbol.Carbine : id == "sidearm" ? CityHudSymbol.Sidearm : CityHudSymbol.Pulse;
                    weaponIcon.SetVerticesDirty();
                }
                string ammo = ctx.Combat.Reloading ? "Recarregando…" : ctx.Combat.Ammo(id) + " / " + ctx.Combat.Reserve(id);
                if (weaponAmmo.text != ammo) weaponAmmo.text = ammo;
                weaponAmmo.color = ctx.Combat.Reloading ? accent : ctx.Combat.Ammo(id) == 0 ? hudAmber : muted;
            }
            if (inCar)
            {
                // Ground speed remains correct when reversing or sliding sideways.
                string speed = Mathf.RoundToInt(CityPhysics.Flat(car.Body.linearVelocity).magnitude * 3.6f).ToString();
                if (vehicleSpeed.text != speed) vehicleSpeed.text = speed;
                leftSignalIcon.color = car.LeftIndicatorLit ? hudAmber : hudDim;
                rightSignalIcon.color = car.RightIndicatorLit ? hudAmber : hudDim;
                headlightIcon.color = car.HeadlightsOn ? accent : hudDim;
            }
        }
    }
}
