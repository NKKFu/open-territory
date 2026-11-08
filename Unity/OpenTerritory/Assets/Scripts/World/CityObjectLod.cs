using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace OpenTerritory
{
    [Serializable]
    public sealed class CityLodPart
    {
        public MeshFilter filter;
        public Renderer renderer;
        public Mesh full, medium, far;
        public int lastVisibleLevel = 2;
        public ShadowCastingMode shadows;
        [NonSerialized] public bool externalMesh;
    }

    // Mesh swaps keep the original articulated transforms, materials, property blocks,
    // renderer.enabled state and every collider. Generated assets are shared and immutable.
    public sealed class CityObjectLod : MonoBehaviour
    {
        public string category;
        public CityLodPart[] parts = new CityLodPart[0];
        public Bounds localBounds;
        public float fullDistance = 18, cullDistance = 380;
        public float mediumHeight = .10f, farHeight = .035f;
        public int CurrentLevel { get; private set; } = -1;

        public int SelectLevel(Camera camera)
        {
            Vector3 cameraLocal = transform.InverseTransformPoint(camera.transform.position);
            float edgeDistance = Vector3.Distance(camera.transform.position, transform.TransformPoint(localBounds.ClosestPoint(cameraLocal)));
            if (edgeDistance <= fullDistance) return 0; // Includes interiors and the occupied vehicle.
            if (edgeDistance > cullDistance) return 3;
            Vector3 scale = transform.lossyScale;
            float size = Mathf.Max(localBounds.size.x * Mathf.Abs(scale.x), localBounds.size.y * Mathf.Abs(scale.y), localBounds.size.z * Mathf.Abs(scale.z));
            float distance = Vector3.Distance(camera.transform.position, transform.TransformPoint(localBounds.center));
            float height = camera.orthographic ? size / (2 * camera.orthographicSize)
                : size / (2 * Mathf.Max(.1f, distance) * Mathf.Tan(camera.fieldOfView * Mathf.Deg2Rad * .5f));
            height *= QualitySettings.lodBias;
            // Ten percent hysteresis around both boundaries prevents flicker while orbiting.
            float medium = mediumHeight * (CurrentLevel == 0 ? .90f : 1.10f);
            float far = farHeight * (CurrentLevel <= 1 ? .90f : 1.10f);
            return height >= medium ? 0 : height >= far ? 1 : 2;
        }

        public void ApplyLevel(int level)
        {
            if (level == CurrentLevel) return;
            foreach (var part in parts)
            {
                if (!part.renderer || !part.filter) continue;
                Mesh current = part.filter.sharedMesh;
                // Gameplay owns mutable meshes (kinetic mural, damage, etc.). Never overwrite them.
                if (current != part.full && current != part.medium && current != part.far) part.externalMesh = true;
                if (!part.externalMesh) part.filter.sharedMesh = level == 0 ? part.full : level == 1 ? part.medium : part.far;
                part.renderer.forceRenderingOff = level > part.lastVisibleLevel;
                part.renderer.shadowCastingMode = level >= 2 ? ShadowCastingMode.Off : part.shadows;
            }
            CurrentLevel = level;
        }

        void OnDisable()
        {
            ApplyLevel(0);
            CurrentLevel = -1;
        }
    }
}
