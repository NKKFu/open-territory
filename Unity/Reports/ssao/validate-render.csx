// Execute as an MCP execute_code method body in Play Mode. No normal-game probe.
if (!UnityEditor.EditorApplication.isPlaying) return new { error = "Play Mode required" };
var context = OpenTerritory.CityContext.Instance;
if (context) context.Pause(true);
var directory = System.IO.Path.GetFullPath("../Reports/ssao");
System.IO.Directory.CreateDirectory(directory);
var root = new UnityEngine.GameObject("SSAO validation only");
root.transform.position = new UnityEngine.Vector3(10000, 0, 0);
var cameraObject = new UnityEngine.GameObject("SSAO test camera");
cameraObject.transform.SetParent(root.transform, false);
var camera = cameraObject.AddComponent<UnityEngine.Camera>();
camera.enabled = false;
camera.cullingMask = 1 << 31;
camera.clearFlags = UnityEngine.CameraClearFlags.SolidColor;
camera.backgroundColor = new UnityEngine.Color(.3f, .5f, .7f);
camera.nearClipPlane = .1f;
camera.farClipPlane = 120;
camera.fieldOfView = 60;
camera.allowHDR = false;
camera.allowMSAA = true;
camera.transform.localPosition = new UnityEngine.Vector3(3, 2.8f, -4);
camera.transform.LookAt(root.transform.position + UnityEngine.Vector3.up * .5f);
var effect = cameraObject.AddComponent<OpenTerritory.CityAmbientOcclusion>();
var material = new UnityEngine.Material(UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Shader>("Assets/Shaders/CitySurface.shader"));
material.SetFloat("_Unlit", 1);
material.SetColor("_Color", new UnityEngine.Color(.7f, .7f, .7f));
System.Func<string, UnityEngine.Vector3, UnityEngine.Vector3, UnityEngine.GameObject> cube = (name, position, scale) => {
    var obj = UnityEngine.GameObject.CreatePrimitive(UnityEngine.PrimitiveType.Cube);
    obj.name = name; obj.layer = 31;
    obj.transform.SetParent(root.transform, false);
    obj.transform.localPosition = position; obj.transform.localScale = scale;
    obj.GetComponent<UnityEngine.Renderer>().sharedMaterial = material;
    return obj;
};
cube("Floor", new UnityEngine.Vector3(0, -.1f, 0), new UnityEngine.Vector3(20, .2f, 20));
var box = cube("Contact box", new UnityEngine.Vector3(0, .5f, 0), UnityEngine.Vector3.one);
var wall = cube("Corner wall", new UnityEngine.Vector3(-1.5f, 1, 0), new UnityEngine.Vector3(.2f, 2, 4));
var checks = new System.Collections.Generic.List<object>();
bool passed = true;
var previousActive = UnityEngine.RenderTexture.active;
int previousAA = UnityEngine.QualitySettings.antiAliasing;
UnityEngine.RenderTexture target = null;
System.Func<bool, string, UnityEngine.Color32[]> capture = (enabled, name) => {
    effect.enabled = enabled;
    camera.Render(); UnityEngine.RenderTexture.active = target;
    var image = new UnityEngine.Texture2D(target.width, target.height, UnityEngine.TextureFormat.RGB24, false);
    image.ReadPixels(new UnityEngine.Rect(0, 0, target.width, target.height), 0, 0); image.Apply();
    if (name != null) System.IO.File.WriteAllBytes(System.IO.Path.Combine(directory, name + ".png"), image.EncodeToPNG());
    var pixels = image.GetPixels32(); UnityEngine.Object.DestroyImmediate(image); return pixels;
};
try
{
    foreach (int aa in new int[] { 1, 2, 4 })
    {
        UnityEngine.QualitySettings.antiAliasing = aa == 1 ? 0 : aa;
        target = UnityEngine.RenderTexture.GetTemporary(960, 540, 24, UnityEngine.RenderTextureFormat.ARGB32, UnityEngine.RenderTextureReadWrite.Default, aa);
        camera.targetTexture = target;
        var before = capture(false, "corner-msaa" + aa + "-off");
        var after = capture(true, "corner-msaa" + aa + "-on");
        var repeat = capture(true, null);
        int darkened = 0, brightened = 0, skyChanged = 0, skyPixels = 0, shimmer = 0, excessive = 0;
        for (int pixel = 0; pixel < before.Length; pixel++)
        {
            var a = before[pixel]; var b = after[pixel]; var c = repeat[pixel];
            int difference = (int)a.r + a.g + a.b - b.r - b.g - b.b;
            if (difference > 6) darkened++;
            if (difference < -6) brightened++;
            // Exclude MSAA silhouette pixels mixed with the background.
            if (a.r == before[before.Length - 1].r && a.g == before[before.Length - 1].g && a.b == before[before.Length - 1].b) { skyPixels++; if (System.Math.Abs(difference) > 3) skyChanged++; }
            if (b.r != c.r || b.g != c.g || b.b != c.b) shimmer++;
            if (a.r > 0 && b.r < a.r * .64f - 2) excessive++;
        }
        bool ok = darkened > 100 && brightened == 0 && skyPixels > 1000 && skyChanged == 0 && shimmer == 0 && excessive == 0;
        passed &= ok;
        checks.Add(new { name = "contact-occlusion-MSAA-" + aa, passed = ok, darkened, brightened, skyPixels, skyChanged, shimmer, excessive });
        camera.targetTexture = null; UnityEngine.RenderTexture.ReleaseTemporary(target); target = null;
    }
    target = UnityEngine.RenderTexture.GetTemporary(960, 540, 24);
    camera.targetTexture = target;
    box.SetActive(false); wall.SetActive(false);
    var flatBefore = capture(false, "flat-off"); var flatAfter = capture(true, "flat-on");
    int flatChanges = 0;
    for (int pixel = 0; pixel < flatBefore.Length; pixel++)
        if (System.Math.Abs(flatBefore[pixel].r - flatAfter[pixel].r) > 2) flatChanges++;
    checks.Add(new { name = "flat-surfaces-unchanged", passed = flatChanges == 0, changed = flatChanges }); passed &= flatChanges == 0;
    box.SetActive(true); wall.SetActive(true);
    var glass = UnityEngine.GameObject.CreatePrimitive(UnityEngine.PrimitiveType.Quad);
    glass.layer = 31; glass.transform.SetParent(root.transform, false);
    glass.transform.position = camera.transform.position + camera.transform.forward;
    glass.transform.rotation = camera.transform.rotation; glass.transform.localScale = UnityEngine.Vector3.one * 10;
    var glassMaterial = new UnityEngine.Material(UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Shader>("Assets/Shaders/CityGlass.shader"));
    glassMaterial.SetFloat("_Unlit", 1); glassMaterial.SetColor("_Color", new UnityEngine.Color(.85f, .2f, .15f, 1));
    glass.GetComponent<UnityEngine.Renderer>().sharedMaterial = glassMaterial;
    var glassBefore = capture(false, null); var glassAfter = capture(true, "transparent-on");
    int transparentChanges = 0;
    for (int pixel = 0; pixel < glassBefore.Length; pixel++)
        if (System.Math.Abs(glassBefore[pixel].r - glassAfter[pixel].r) > 2) transparentChanges++;
    checks.Add(new { name = "transparent-pass-unaffected", passed = transparentChanges == 0, changed = transparentChanges }); passed &= transparentChanges == 0;
    UnityEngine.Object.DestroyImmediate(glassMaterial); glass.SetActive(false);
    camera.targetTexture = null; UnityEngine.RenderTexture.ReleaseTemporary(target); target = null;
    target = UnityEngine.RenderTexture.GetTemporary(3840, 2160, 24);
    camera.targetTexture = target; effect.enabled = true; camera.Render();
    var effectMaterial = (UnityEngine.Material)typeof(OpenTerritory.CityAmbientOcclusion).GetField("material", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(effect);
    var buffer = effectMaterial.GetVector("_AOBufferSize");
    bool capped = buffer.z == 960 && buffer.w == 540;
    checks.Add(new { name = "4K-buffer-capped-at-960x540", passed = capped, width = buffer.z, height = buffer.w }); passed &= capped;
    effect.enabled = false; camera.depthTextureMode = UnityEngine.DepthTextureMode.DepthNormals;
    effect.enabled = true; effect.enabled = false;
    bool retained = camera.depthTextureMode == UnityEngine.DepthTextureMode.DepthNormals;
    checks.Add(new { name = "disable-releases-owned-depth-and-preserves-other-flags", passed = retained }); passed &= retained;
    camera.depthTextureMode = UnityEngine.DepthTextureMode.Depth;
    effect.enabled = true; effect.enabled = false;
    retained = camera.depthTextureMode == UnityEngine.DepthTextureMode.Depth;
    checks.Add(new { name = "preexisting-depth-request-retained", passed = retained }); passed &= retained;
    return new { passed, checks, graphicsAPI = UnityEngine.SystemInfo.graphicsDeviceType.ToString(), gpu = UnityEngine.SystemInfo.graphicsDeviceName };
}
finally
{
    camera.targetTexture = null;
    UnityEngine.RenderTexture.active = previousActive;
    if (target) UnityEngine.RenderTexture.ReleaseTemporary(target);
    UnityEngine.QualitySettings.antiAliasing = previousAA;
    UnityEngine.Object.DestroyImmediate(root);
    UnityEngine.Object.DestroyImmediate(material);
}
