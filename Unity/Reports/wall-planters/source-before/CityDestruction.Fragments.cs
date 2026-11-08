#if !CITY_PERFORMANCE_REFERENCE
using System.Collections.Generic;
using UnityEngine;

namespace OpenTerritory
{
    public sealed partial class CityDestruction
    {
        sealed class Fragment
        {
            public Transform root;
            public Rigidbody body;
            public MeshRenderer renderer;
            public MeshFilter filter;
            public BoxCollider shape;
            public float until;
        }
        readonly Stack<Fragment> availableFragments = new Stack<Fragment>(DebrisLimit);
        Transform fragmentRoot;
        const int DebrisIgnoreMask = (1 << CityContext.PlayerLayer) | (1 << CityContext.ActorLayer) | (1 << CityContext.VehicleLayer);

        void PrepareFragmentPool()
        {
            // Keep physical debris in world space, independent of the service's transform.
            fragmentRoot = new GameObject("Estilhaços • pool").transform;
            for (int i = 0; i < DebrisLimit; i++)
            {
                var go = new GameObject("Estilhaço • " + i); go.SetActive(false); go.transform.SetParent(fragmentRoot, false);
                var filter = go.AddComponent<MeshFilter>(); filter.sharedMesh = cube;
                var renderer = go.AddComponent<MeshRenderer>(); renderer.sharedMaterial = fragmentMaterial; renderer.enabled = false;
                var shape = go.AddComponent<BoxCollider>(); shape.excludeLayers = DebrisIgnoreMask; shape.enabled = false;
                var body = go.AddComponent<Rigidbody>(); body.mass = .35f;
                body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative; body.detectCollisions = false; body.Sleep();
                availableFragments.Push(new Fragment { root = go.transform, body = body, renderer = renderer, filter = filter, shape = shape });
            }
        }
        void EmitFragments(CityDestructible prop, Bounds bounds, Vector3 impulse)
        {
            // Reading Mesh.colors32 creates a managed copy. Read it once per break,
            // rather than twice for every fragment; LOD/gameplay can swap the source mesh.
            var filter = prop.visuals.Length > 0 && prop.visuals[0] ? prop.visuals[0].GetComponent<MeshFilter>() : null;
            var colors = filter && filter.sharedMesh && prop.category != "tree" ? filter.sharedMesh.colors32 : null;
            int pieces = Mathf.Clamp(Mathf.CeilToInt(bounds.size.magnitude * 1.5f), 4, 9);
            for (int i = 0; i < pieces; i++)
            {
                if (fragments.Count >= DebrisLimit) RemoveFragment();
                var fragment = availableFragments.Pop();
                bool fallingTree = i == 0 && prop.category == "tree" && prop.fallingMesh && prop.fallingMaterial;
                Vector3 position = new Vector3(
                    Mathf.Lerp(bounds.min.x, bounds.max.x, .18f + .64f * ((i * 37 % 13) / 12f)),
                    Mathf.Lerp(bounds.min.y, bounds.max.y, .14f + .7f * ((i * 7 % 11) / 10f)),
                    Mathf.Lerp(bounds.min.z, bounds.max.z, .15f + .7f * ((i * 5 % 9) / 8f)));
                Quaternion rotation = prop.transform.rotation * Quaternion.Euler(i * 31, i * 47, i * 19);
                fragment.root.SetPositionAndRotation(position, rotation);
                fragment.root.localScale = new Vector3(Mathf.Clamp(bounds.size.x / 4, .07f, .48f), Mathf.Clamp(bounds.size.y / 5, .06f, .30f), Mathf.Clamp(bounds.size.z / 4, .07f, .44f));
                Color color = new Color(.48f, .39f, .29f);
                if (prop.visuals.Length > 0 && prop.visuals[i % prop.visuals.Length] && prop.visuals[i % prop.visuals.Length].sharedMaterial)
                    color = prop.visuals[i % prop.visuals.Length].sharedMaterial.color;
                if (colors != null && colors.Length > 0) color *= (Color)colors[(i * 24) % colors.Length];
                if (prop.category == "fence") color = i % 3 == 0 ? new Color(.84f, .79f, .70f) : new Color(.69f, .66f, .57f);
                if (prop.category == "tree")
                {
                    color = i % 3 == 0 ? new Color(.25f, .40f, .19f) : new Color(.38f, .24f, .12f);
                    fragment.root.localScale = i % 3 == 0 ? Vector3.one * .26f : new Vector3(.12f, .55f, .13f);
                }
                fragment.filter.sharedMesh = fallingTree ? prop.fallingMesh : cube;
                fragment.renderer.sharedMaterial = fallingTree ? prop.fallingMaterial : fragmentMaterial;
                fragment.shape.center = Vector3.zero; fragment.shape.size = Vector3.one; fragment.body.mass = .35f;
                if (fallingTree)
                {
                    position = prop.transform.position; rotation = prop.transform.rotation;
                    fragment.root.SetPositionAndRotation(position, rotation); fragment.root.localScale = prop.transform.lossyScale;
                    var treeBounds = prop.fallingMesh.bounds;
                    fragment.shape.center = new Vector3(treeBounds.center.x, treeBounds.min.y + treeBounds.size.y * .4f, treeBounds.center.z);
                    fragment.shape.size = new Vector3(Mathf.Min(.6f, treeBounds.size.x * .4f), treeBounds.size.y * .8f, Mathf.Min(.6f, treeBounds.size.z * .4f));
                    fragment.body.mass = 55; color = Color.white;
                }
                tint.Clear(); tint.SetColor("_Color", color); fragment.renderer.SetPropertyBlock(tint);
                fragment.renderer.enabled = true; fragment.shape.excludeLayers = DebrisIgnoreMask; fragment.shape.enabled = true;
                fragment.body.detectCollisions = true;
                fragment.root.gameObject.SetActive(true);
                fragment.body.position = position; fragment.body.rotation = rotation;
                fragment.body.linearVelocity = Vector3.ClampMagnitude(impulse, 9) + new Vector3(Mathf.Sin(i * 2.4f) * 1.7f, 1.2f + i * .11f, Mathf.Cos(i * 1.6f) * 1.5f);
                fragment.body.angularVelocity = new Vector3(i + 1, 2 - i, 3) * .7f; fragment.body.WakeUp();
                if (fallingTree)
                {
                    var direction = CityPhysics.Flat(impulse); if (direction.sqrMagnitude < .01f) direction = prop.transform.forward;
                    fragment.body.linearVelocity = Vector3.ClampMagnitude(impulse, 4) * .35f + Vector3.up * .25f;
                    fragment.body.angularVelocity = Vector3.Cross(Vector3.up, direction.normalized) * 1.35f;
                }
                fragment.until = ctx.Clock + 8; fragments.Enqueue(fragment);
            }
        }
        void RemoveFragment()
        {
            var item = fragments.Dequeue();
            item.body.linearVelocity = item.body.angularVelocity = Vector3.zero; item.body.Sleep();
            item.body.detectCollisions = false; item.shape.enabled = false; item.renderer.enabled = false;
            item.renderer.SetPropertyBlock(null); item.root.gameObject.SetActive(false); item.until = 0;
            availableFragments.Push(item);
        }
        void DisposeFragmentPool()
        {
            fragments.Clear(); availableFragments.Clear();
            if (fragmentRoot) Destroy(fragmentRoot.gameObject);
        }
    }
}
#endif
