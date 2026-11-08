var ctx = OpenTerritory.CityContext.Instance;
if (!ctx || !ctx.Ready) return new { error = "City must be ready in Play Mode" };
var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
var optionsField = typeof(OpenTerritory.CityUI).GetField("options", flags);
var reviewField = typeof(OpenTerritory.CityUI).GetField("reviewRun", flags);
string originalOptions = UnityEngine.JsonUtility.ToJson(optionsField.GetValue(ctx.UI));
bool originalReview = (bool)reviewField.GetValue(ctx.UI);
bool wasPaused = ctx.Paused, wasFollowing = ctx.Follow.enabled;
var camera = ctx.Camera; var position = camera.transform.position; var rotation = camera.transform.rotation;
var previousTarget = camera.targetTexture; var previousActive = UnityEngine.RenderTexture.active;
var directory = System.IO.Path.GetFullPath("../Reports/ssao");
var checks = new System.Collections.Generic.List<object>();
bool passed = true;
System.Action<string, bool> check = (name, ok) => { checks.Add(new { name, passed = ok }); passed &= ok; };
var view = ctx.UI.GetComponent<OpenTerritory.CityCanvasUI>();
var toggle = System.Array.Find(ctx.UI.GetComponentsInChildren<UnityEngine.UI.Toggle>(true), t => t.name == "Oclusão ambiente");
var buttons = ctx.UI.GetComponentsInChildren<UnityEngine.UI.Button>(true);
var balanced = System.Array.Find(buttons, b => b.name == "Qualidade Equilibrada");
var low = System.Array.Find(buttons, b => b.name == "Qualidade Leve");
var high = System.Array.Find(buttons, b => b.name == "Qualidade Alta");
reviewField.SetValue(ctx.UI, true);
UnityEngine.RenderTexture target = null;
try
{
    ctx.UI.SetSettings(true); balanced.onClick.Invoke();
    var effect = camera.GetComponent<OpenTerritory.CityAmbientOcclusion>();
    toggle.isOn = true; UnityEngine.Canvas.ForceUpdateCanvases();
    check("balanced-enables-SSAO-on-game-camera", effect && effect.enabled && toggle.interactable);
    var events = UnityEngine.EventSystems.EventSystem.current;
    var click = new UnityEngine.EventSystems.PointerEventData(events) { button = UnityEngine.EventSystems.PointerEventData.InputButton.Left };
    UnityEngine.EventSystems.ExecuteEvents.Execute(toggle.gameObject, click, UnityEngine.EventSystems.ExecuteEvents.pointerClickHandler);
    check("pointer-disables-SSAO-immediately", !toggle.isOn && !effect.enabled);
    events.SetSelectedGameObject(toggle.gameObject);
    UnityEngine.EventSystems.ExecuteEvents.Execute(toggle.gameObject, new UnityEngine.EventSystems.BaseEventData(events), UnityEngine.EventSystems.ExecuteEvents.submitHandler);
    check("keyboard-submit-reenables-SSAO", toggle.isOn && effect.enabled);
    low.onClick.Invoke();
    check("light-quality-disables-effect-and-toggle", !effect.enabled && !toggle.interactable && toggle.isOn);
    check("light-quality-releases-depth-request", (camera.depthTextureMode & UnityEngine.DepthTextureMode.Depth) == 0);
    high.onClick.Invoke(); check("high-quality-restores-SSAO-preference", effect.enabled && toggle.interactable && toggle.isOn);
    toggle.isOn = false; low.onClick.Invoke(); balanced.onClick.Invoke();
    check("quality-change-retains-disabled-preference", !effect.enabled && !toggle.isOn);
    var saved = UnityEngine.JsonUtility.FromJson<OpenTerritory.CityUI.Options>(UnityEngine.JsonUtility.ToJson(optionsField.GetValue(ctx.UI)));
    check("SSAO-preference-serializes", !saved.ambientOcclusion);
    var legacy = new OpenTerritory.CityUI.Options(); UnityEngine.JsonUtility.FromJsonOverwrite("{\"quality\":1}", legacy);
    check("older-options-default-to-enabled", legacy.ambientOcclusion);
    toggle.isOn = true;
    UnityEngine.Canvas.ForceUpdateCanvases();
    var corners = new UnityEngine.Vector3[4]; var parentCorners = new UnityEngine.Vector3[4];
    ((UnityEngine.RectTransform)toggle.transform).GetWorldCorners(corners);
    ((UnityEngine.RectTransform)toggle.transform.parent).GetWorldCorners(parentCorners);
    check("SSAO-row-fits-image-page-above-footer", corners[0].y >= parentCorners[0].y && corners[1].y <= parentCorners[1].y);
    check("settings-pause-city-with-SSAO-active", ctx.Paused && UnityEngine.Time.timeScale == 0 && effect.enabled);
    check("image-toggle-label-is-readable", toggle.GetComponentInChildren<UnityEngine.UI.Text>().fontSize >= 17 && toggle.GetComponentInChildren<UnityEngine.UI.Text>().preferredWidth < ((UnityEngine.RectTransform)toggle.transform).rect.width - 40);
    ctx.UI.SetSettings(false); ctx.Pause(true); ctx.Follow.enabled = false;
    target = UnityEngine.RenderTexture.GetTemporary(1280, 800, 24, UnityEngine.RenderTextureFormat.ARGB32, UnityEngine.RenderTextureReadWrite.Default, 2);
    camera.targetTexture = target;
    UnityEngine.Color32[] off = null, on = null;
    for (int enabled = 0; enabled < 2; enabled++)
    {
        effect.enabled = enabled == 1; camera.Render(); UnityEngine.RenderTexture.active = target;
        var image = new UnityEngine.Texture2D(1280, 800, UnityEngine.TextureFormat.RGB24, false);
        image.ReadPixels(new UnityEngine.Rect(0, 0, 1280, 800), 0, 0); image.Apply();
        System.IO.File.WriteAllBytes(System.IO.Path.Combine(directory, "city-" + (enabled == 1 ? "on" : "off") + ".png"), image.EncodeToPNG());
        if (enabled == 1) on = image.GetPixels32(); else off = image.GetPixels32();
        UnityEngine.Object.DestroyImmediate(image);
    }
    int darkened = 0, brightened = 0, brightenedInterior = 0;
    for (int pixel = 0; pixel < off.Length; pixel++)
    {
        int difference = (int)off[pixel].r + off[pixel].g + off[pixel].b - on[pixel].r - on[pixel].g - on[pixel].b;
        if (difference > 6) darkened++;
        if (difference < -6)
        {
            brightened++;
            // Enabling an image effect changes Unity's MSAA resolve at a few
            // silhouettes. Check lighting away from these mixed edge pixels.
            bool uniform = pixel % 1280 > 0 && pixel % 1280 < 1279 && pixel >= 1280 && pixel < off.Length - 1280;
            if (uniform) for (int y = -1; y <= 1; y++) for (int x = -1; x <= 1; x++)
            {
                var neighbor = off[pixel + y * 1280 + x]; var center = off[pixel];
                uniform &= System.Math.Abs(neighbor.r - center.r) + System.Math.Abs(neighbor.g - center.g) + System.Math.Abs(neighbor.b - center.b) < 18;
            }
            if (uniform) brightenedInterior++;
        }
    }
    checks.Add(new { name = "native-city-contact-shading-visible", passed = darkened > 100 && brightenedInterior == 0, darkened, brightened, brightenedInterior }); passed &= darkened > 100 && brightenedInterior == 0;
    check("no-automatic-SSAO-probe-in-normal-play", System.Array.TrueForAll(UnityEngine.Object.FindObjectsByType<UnityEngine.MonoBehaviour>(UnityEngine.FindObjectsSortMode.None), p => p.GetType().Name != "CityAmbientOcclusionProbe"));
    return new { passed, checks, screenWidth = UnityEngine.Screen.width, screenHeight = UnityEngine.Screen.height, fieldOfView = camera.fieldOfView };
}
finally
{
    camera.targetTexture = previousTarget; UnityEngine.RenderTexture.active = previousActive;
    if (target) UnityEngine.RenderTexture.ReleaseTemporary(target);
    camera.transform.SetPositionAndRotation(position, rotation); ctx.Follow.enabled = wasFollowing;
    var options = (OpenTerritory.CityUI.Options)optionsField.GetValue(ctx.UI);
    UnityEngine.JsonUtility.FromJsonOverwrite(originalOptions, options);
    typeof(OpenTerritory.CityUI).GetMethod("Apply", flags).Invoke(ctx.UI, null); view.Bind(options);
    reviewField.SetValue(ctx.UI, originalReview); ctx.UI.SetSettings(wasPaused);
}
