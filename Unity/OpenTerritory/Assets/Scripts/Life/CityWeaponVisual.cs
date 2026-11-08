using System;
using UnityEngine;

namespace OpenTerritory
{
    // Local CC0 models share meshes and materials. Only the hierarchy and
    // emission property block belong to each wearer.
    public sealed class CityWeaponVisual
    {
        public Transform Root { get; private set; }
        public Transform Muzzle { get; private set; }
        public Transform Ejection { get; private set; }
        public Transform Action { get; private set; }
        public Transform Magazine { get; private set; }
        public Vector3 RightGrip { get; private set; }
        public Vector3 SupportGrip { get; private set; }
        public Quaternion SupportRotation => id == "carbine" ? Quaternion.Euler(0, 0, 78) : Quaternion.identity;
        public int Triangles { get; private set; }
        public int RendererCount { get; private set; }
        public string SourceAsset { get; private set; }
        public int SourceTriangles { get; private set; }
        readonly string id;
        readonly Renderer emitter;
        readonly MaterialPropertyBlock emission;
        static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");
        readonly Vector3 actionHome, magazineHome;
        float shotAt = -100;

        public CityWeaponVisual(CityWeaponDefinition definition, Transform hand)
        {
            id = definition.Id;
            var prefab = Resources.Load<GameObject>("Weapons/" + id);
            if (!prefab) throw new InvalidOperationException("Missing local weapon asset: " + id);
            Root = UnityEngine.Object.Instantiate(prefab, hand, false).transform;
            Root.name = definition.Name;
            var asset = Root.GetComponent<CityWeaponAsset>();
            if (!asset || !asset.Action || !asset.Magazine || !asset.Muzzle || !asset.Ejection || !asset.RightGrip || !asset.SupportGrip)
                throw new InvalidOperationException("Incomplete weapon sockets: " + id);
            Action = asset.Action; Magazine = asset.Magazine; Muzzle = asset.Muzzle; Ejection = asset.Ejection;
            RightGrip = asset.RightGrip.localPosition; SupportGrip = asset.SupportGrip.localPosition;
            SourceAsset = asset.Author + "/" + asset.SourceModel; SourceTriangles = asset.SourceTriangles;
            emitter = asset.EmissionRenderer;
            if (emitter) emission = new MaterialPropertyBlock();
            foreach (var renderer in Root.GetComponentsInChildren<MeshRenderer>(true))
            {
                RendererCount++;
                var mesh = renderer.GetComponent<MeshFilter>().sharedMesh;
                for (int submesh = 0; submesh < mesh.subMeshCount; submesh++) Triangles += (int)mesh.GetIndexCount(submesh) / 3;
            }
            actionHome = Action.localPosition; magazineHome = Magazine.localPosition;
            Root.gameObject.SetActive(false);
        }
        public void Kick(float clock) { shotAt = clock; }
        public void Animate(float clock, bool reloading, float reloadProgress, int ammo)
        {
            float age = clock - shotAt, travel = age >= 0 ? Mathf.Clamp01(1 - age / .12f) : 0;
            float locked = ammo == 0 && !reloading && id == "sidearm" ? .032f : 0;
            Action.localPosition = actionHome + Vector3.back * (id == "pulse" ? travel * .015f : Mathf.Max(locked, travel * (id == "carbine" ? .05f : .046f)));
            float drop = reloading ? Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.12f, .3f, reloadProgress)) * (1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.58f, .82f, reloadProgress))) : 0;
            Magazine.localPosition = magazineHome + new Vector3(-.022f * drop, -.17f * drop, -.025f * drop);
            Magazine.localRotation = Quaternion.Euler(0, 0, -14 * drop);
            if (emitter)
            {
                emission.SetColor(EmissionColor, new Color(.06f, .75f, .65f) * (reloading ? Mathf.Lerp(.15f, 1.3f, reloadProgress) : Mathf.Lerp(1.3f, 3.8f, Mathf.Exp(-Mathf.Max(0, age) * 15))));
                emitter.SetPropertyBlock(emission);
            }
        }
        public void Dispose() { if (Root) UnityEngine.Object.Destroy(Root.gameObject); }
    }
}
