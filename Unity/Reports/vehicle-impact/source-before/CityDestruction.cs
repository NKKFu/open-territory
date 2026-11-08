using System.Collections.Generic;
using UnityEngine;

namespace OpenTerritory
{
    // All damage routes resolve an actual collider. A nearby prop behind a wall
    // is never substituted for a non-destructible hit.
    public sealed partial class CityDestruction : MonoBehaviour
    {
        public const int DebrisLimit = 72;
        readonly Queue<Fragment> fragments = new Queue<Fragment>(DebrisLimit);
        readonly Dictionary<Collider, CityDestructible> byCollider = new Dictionary<Collider, CityDestructible>();
        readonly HashSet<CityDestructible> impacted = new HashSet<CityDestructible>();
        readonly List<CityDestructible> props = new List<CityDestructible>();
        readonly HashSet<CityDestructible> registered = new HashSet<CityDestructible>();
        CityContext ctx;
        CityLighting lighting;
        MaterialPropertyBlock tint;
        Material fragmentMaterial;
        Mesh cube;
        float lastCrime = -100;
        public IReadOnlyList<CityDestructible> Props => props;
        public bool CanDamage => ctx && ctx.Ready && !ctx.Paused;
        public int BrokenCount { get; private set; }
        public int FragmentCount => fragments.Count;

        public void Initialize(CityContext context)
        {
            ctx = context; tint = new MaterialPropertyBlock();
            var importedShapes=new Dictionary<int,List<Collider>>();
            foreach(var shape in FindObjectsByType<Collider>(FindObjectsInactive.Include,FindObjectsSortMode.None))
            {
                if(!shape.name.StartsWith("Colisão-")||!int.TryParse(shape.name.Substring(8),out int id))continue;
                if(!importedShapes.TryGetValue(id,out var shapes)){shapes=new List<Collider>();importedShapes[id]=shapes;}shapes.Add(shape);
            }
            foreach (var prop in FindObjectsByType<CityDestructible>(FindObjectsInactive.Include, FindObjectsSortMode.None)) Register(prop);
            foreach (var data in ctx.Data.destructibles)
            {
                var root = ctx.Node(data.id); if (!root || !root.gameObject.activeInHierarchy) continue;
                var prop = root.GetComponent<CityDestructible>();
                if (!prop)
                {
                    prop = root.gameObject.AddComponent<CityDestructible>(); prop.category = data.type; prop.nodeId = data.id;
                    prop.durability = data.type == "lamp" || data.type == "signal" ? 95 : data.type == "bench" ? 66 : 36;
                    // Imported collisions can be independent from visual roots.
                    if(importedShapes.TryGetValue(data.solidIndex,out var shapes))
                    {
                        Collider nearest=null;float best=float.MaxValue;Vector3 center=root.position+Vector3.up*.6f;
                        var visuals=root.GetComponentsInChildren<Renderer>(true);
                        if(visuals.Length>0){var bounds=visuals[0].bounds;foreach(var r in visuals)bounds.Encapsulate(r.bounds);center=bounds.center;}
                        foreach(var shape in shapes){float score=(shape.bounds.center-center).sqrMagnitude;if(score<best){best=score;nearest=shape;}}
                        if(nearest)nearest.transform.SetParent(root,true);
                    }
                }
                Register(prop);
            }
            foreach (var place in ctx.Data.places)
                if (place.door != null) RegisterDoor(ctx.Node(place.door.id), place.door.id);
            var catalog = FindFirstObjectByType<CityHomeCatalog>();
            if (catalog) foreach (var door in catalog.doors) RegisterDoor(door.leaf, 0);
            foreach (var device in ctx.Data.devices)
                if (device.type == "phone" || device.type == "harbor-radio")
                {
                    var root = ctx.Node(device.node); if (!root || !root.gameObject.activeInHierarchy || root.GetComponentInParent<CityDestructible>() || root.GetComponentsInChildren<Renderer>(true).Length==0) continue;
                    var prop = root.gameObject.AddComponent<CityDestructible>(); prop.category = device.type; prop.nodeId = device.node; prop.durability = 32; Register(prop);
                }
            lighting = FindFirstObjectByType<CityLighting>();
            if (lighting) foreach (var fixture in lighting.fixtures)
            {
                if (!fixture.anchor || !fixture.anchor.gameObject.activeInHierarchy) continue;
                var prop = fixture.anchor.GetComponentInParent<CityDestructible>();
                if (!prop)
                {
                    prop = fixture.anchor.gameObject.AddComponent<CityDestructible>(); prop.category = fixture.place >= 0 ? "light" : "lamp";
                    prop.place = fixture.place; prop.nodeId = fixture.node; prop.durability = fixture.place >= 0 ? 18 : 95;
                }
                // Bulbs and lamp heads must be hittable, too, not just the pole.
                if (fixture.lens && fixture.lens.transform.IsChildOf(prop.transform)) AddVisualHitbox(prop, fixture.lens);
                Register(prop);
            }
            var sample = GameObject.CreatePrimitive(PrimitiveType.Cube); cube = sample.GetComponent<MeshFilter>().sharedMesh; Destroy(sample);
            fragmentMaterial = new Material(Shader.Find("OpenTerritory/CitySurface")) { name = "Fragmentos • pigmento" };
            fragmentMaterial.SetColor("_Color", Color.white); fragmentMaterial.SetFloat("_Glossiness", .12f);
            PrepareFragmentPool();
        }
        void RegisterDoor(Transform leaf, int id)
        {
            if (!leaf) return;
            var prop = leaf.GetComponent<CityDestructible>();
            if (!prop) { prop = leaf.gameObject.AddComponent<CityDestructible>(); prop.category = "door"; prop.durability = 74; prop.nodeId = id; }
            Register(prop);
        }
        void AddVisualHitbox(CityDestructible prop, Renderer renderer)
        {
            var filter = renderer.GetComponent<MeshFilter>(); if (!filter || !filter.sharedMesh) return;
            if (renderer.GetComponent<Collider>()) return;
            var box = renderer.gameObject.AddComponent<BoxCollider>(); box.center = filter.sharedMesh.bounds.center;
            var size = filter.sharedMesh.bounds.size; size.x = Mathf.Max(.09f, size.x); size.y = Mathf.Max(.09f, size.y); size.z = Mathf.Max(.09f, size.z); box.size = size;
            renderer.gameObject.layer = CityContext.WorldLayer;
        }
        public void Register(CityDestructible prop)
        {
            if (!prop || !prop.gameObject.activeInHierarchy) return;
            if (registered.Add(prop)) { props.Add(prop); prop.Bind(this); }
            prop.shapes = prop.GetComponentsInChildren<Collider>(true);
            foreach (var shape in prop.shapes) if (shape) byCollider[shape] = prop;
        }
        public CityDestructible Resolve(Collider shape)
        {
            if (!shape) return null;
            if (byCollider.TryGetValue(shape, out var prop)) return prop;
            prop = shape.GetComponentInParent<CityDestructible>();
            if (prop && prop.owner == this) { byCollider[shape] = prop; return prop; }
            return null;
        }
        public bool Hit(Collider shape, float damage, Vector3 point, Vector3 impulse, CityPropDamageKind kind, bool playerCaused = true)
        {
            var prop = Resolve(shape); return prop && prop.Apply(damage, point, impulse, kind, playerCaused);
        }
        public void VehicleImpact(CityVehicles.Vehicle car, Collision collision)
        {
            if (!CanDamage || car == null) return;
            impacted.Clear();
            for (int i = 0; i < collision.contactCount; i++)
            {
                var contact = collision.GetContact(i);
                var prop = Resolve(contact.otherCollider) ?? Resolve(contact.thisCollider);
                if (!prop || prop.Broken || !impacted.Add(prop) || ctx.Clock - prop.lastVehicleHit < .16f) continue;
                float speed = Mathf.Abs(Vector3.Dot(collision.relativeVelocity, contact.normal));
                if (speed < 2.5f) continue;
                prop.lastVehicleHit = ctx.Clock;
                Vector3 direction = (prop.transform.position - car.Position); direction.y = .12f; direction.Normalize();
                prop.Apply(speed * speed * 3.8f, contact.point, direction * Mathf.Min(speed, 9), CityPropDamageKind.Vehicle,
                    ctx.Motor.Riding == car.Root || ctx.Vehicles.RemoteVehicle == car);
            }
        }
        void Crime(Vector3 point, bool playerCaused)
        {
            if (!playerCaused || ctx.Clock - lastCrime < .8f) return;
            lastCrime = ctx.Clock; ctx.Police.Report("dano", point, null, false);
        }
        internal void MarkDamage(CityDestructible prop, Vector3 point, bool playerCaused)
        {
            float wear = 1 - prop.Health / Mathf.Max(1, prop.durability);
            foreach (var renderer in prop.visuals)
            {
                if (!renderer) continue; renderer.GetPropertyBlock(tint);
                Color original=renderer.sharedMaterial?renderer.sharedMaterial.color:Color.white;
                tint.SetColor("_Color", original*Color.Lerp(Color.white, new Color(.49f, .44f, .39f), wear * .65f)); renderer.SetPropertyBlock(tint);
            }
            ctx.Sound("impact", point, .42f); Crime(point, playerCaused);
        }
        internal void Shatter(CityDestructible prop, Vector3 point, Vector3 impulse, bool playerCaused)
        {
            BrokenCount++;
            foreach (var shape in prop.shapes) if (shape) shape.enabled = false;
#if CITY_PERFORMANCE_REFERENCE
            var referenceLighting = FindFirstObjectByType<CityLighting>(); if (referenceLighting) referenceLighting.BreakFixture(prop.transform);
#else
            if (lighting) lighting.BreakFixture(prop.transform);
#endif
            ctx.Motor.OnSeatDestroyed(prop.nodeId, prop.transform);
            ctx.Residents.OnFurnitureDestroyed(prop.transform);
            foreach (var audio in prop.GetComponentsInChildren<AudioSource>()) audio.Stop();
            ctx.Sound("impact", point, .85f); Crime(point, playerCaused);
            // Sample actual visible pieces, so a bed sheds wood/fabric and a TV
            // sheds its dark casing. Bodies have a global budget and short life.
            Bounds bounds = new Bounds(prop.transform.position, Vector3.one * .25f); bool first = true;
            foreach (var r in prop.visuals) if (r) { if (first) { bounds = r.bounds; first = false; } else bounds.Encapsulate(r.bounds); }
            EmitFragments(prop, bounds, impulse);
            prop.gameObject.SetActive(false); ctx.RefreshAction();
        }
        void Update()
        {
            if (!CanDamage) return;
            while (fragments.Count > 0 && fragments.Peek().until <= ctx.Clock) RemoveFragment();
        }
        void OnDestroy()
        {
            DisposeFragmentPool();
            if (fragmentMaterial) Destroy(fragmentMaterial);
        }
    }
}
