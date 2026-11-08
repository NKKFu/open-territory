using System.Collections.Generic;
using UnityEngine;

namespace OpenTerritory
{
    // All damage routes resolve an actual collider. A nearby prop behind a wall
    // is never substituted for a non-destructible hit.
    public sealed class CityDestruction : MonoBehaviour
    {
        public const int DebrisLimit = 72;
        sealed class Fragment { public Rigidbody body; public float until; }
        readonly Queue<Fragment> fragments = new Queue<Fragment>();
        readonly Dictionary<Collider, CityDestructible> byCollider = new Dictionary<Collider, CityDestructible>();
        readonly HashSet<CityDestructible> impacted = new HashSet<CityDestructible>();
        readonly List<CityDestructible> props = new List<CityDestructible>();
        readonly HashSet<CityDestructible> registered = new HashSet<CityDestructible>();
        CityContext ctx;
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
                    var solid = GameObject.Find("Colisão-" + data.solidIndex);
                    if (solid) solid.transform.SetParent(root, true);
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
                    var root = ctx.Node(device.node); if (!root || !root.gameObject.activeInHierarchy || root.GetComponentInParent<CityDestructible>()) continue;
                    var prop = root.gameObject.AddComponent<CityDestructible>(); prop.category = device.type; prop.nodeId = device.node; prop.durability = 32; Register(prop);
                }
            var lighting = FindFirstObjectByType<CityLighting>();
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
            var lighting = FindFirstObjectByType<CityLighting>(); if (lighting) lighting.BreakFixture(prop.transform);
            ctx.Motor.OnSeatDestroyed(prop.nodeId, prop.transform);
            ctx.Residents.OnFurnitureDestroyed(prop.transform);
            foreach (var audio in prop.GetComponentsInChildren<AudioSource>()) audio.Stop();
            ctx.Sound("impact", point, .85f); Crime(point, playerCaused);
            // Sample actual visible pieces, so a bed sheds wood/fabric and a TV
            // sheds its dark casing. Bodies have a global budget and short life.
            Bounds bounds = new Bounds(prop.transform.position, Vector3.one * .25f); bool first = true;
            foreach (var r in prop.visuals) if (r) { if (first) { bounds = r.bounds; first = false; } else bounds.Encapsulate(r.bounds); }
            int pieces = Mathf.Clamp(Mathf.CeilToInt(bounds.size.magnitude * 1.5f), 4, 9);
            for (int i = 0; i < pieces; i++)
            {
                while (fragments.Count >= DebrisLimit) RemoveFragment();
                var piece = new GameObject("Estilhaço • " + prop.category); piece.transform.position = new Vector3(
                    Mathf.Lerp(bounds.min.x, bounds.max.x, .18f + .64f * ((i * 37 % 13) / 12f)),
                    Mathf.Lerp(bounds.min.y, bounds.max.y, .14f + .7f * ((i * 7 % 11) / 10f)),
                    Mathf.Lerp(bounds.min.z, bounds.max.z, .15f + .7f * ((i * 5 % 9) / 8f)));
                piece.transform.rotation = prop.transform.rotation * Quaternion.Euler(i * 31, i * 47, i * 19);
                piece.transform.localScale = new Vector3(Mathf.Clamp(bounds.size.x / 4, .07f, .48f), Mathf.Clamp(bounds.size.y / 5, .06f, .30f), Mathf.Clamp(bounds.size.z / 4, .07f, .44f));
                piece.AddComponent<MeshFilter>().sharedMesh = cube;
                var renderer = piece.AddComponent<MeshRenderer>(); renderer.sharedMaterial = fragmentMaterial;
                Color color = new Color(.48f, .39f, .29f);
                if (prop.visuals.Length > 0 && prop.visuals[i % prop.visuals.Length] && prop.visuals[i % prop.visuals.Length].sharedMaterial)
                    color = prop.visuals[i % prop.visuals.Length].sharedMaterial.color;
                var meshFilter = prop.visuals.Length > 0 && prop.visuals[0] ? prop.visuals[0].GetComponent<MeshFilter>() : null;
                if (meshFilter && meshFilter.sharedMesh && meshFilter.sharedMesh.colors32.Length > 0)
                { var colors = meshFilter.sharedMesh.colors32; color *= (Color)colors[(i * 24) % colors.Length]; }
                tint.Clear(); tint.SetColor("_Color", color); renderer.SetPropertyBlock(tint);
                var shape = piece.AddComponent<BoxCollider>();
                shape.excludeLayers = (1 << CityContext.PlayerLayer) | (1 << CityContext.ActorLayer) | (1 << CityContext.VehicleLayer);
                var body = piece.AddComponent<Rigidbody>(); body.mass = .35f; body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
                body.linearVelocity = Vector3.ClampMagnitude(impulse, 9) + new Vector3(Mathf.Sin(i * 2.4f) * 1.7f, 1.2f + i * .11f, Mathf.Cos(i * 1.6f) * 1.5f);
                body.angularVelocity = new Vector3(i + 1, 2 - i, 3) * .7f;
                fragments.Enqueue(new Fragment { body = body, until = ctx.Clock + 8 });
            }
            prop.gameObject.SetActive(false); ctx.RefreshAction();
        }
        void RemoveFragment() { var item = fragments.Dequeue(); if (item.body) Destroy(item.body.gameObject); }
        void Update()
        {
            if (!CanDamage) return;
            while (fragments.Count > 0 && fragments.Peek().until <= ctx.Clock) RemoveFragment();
        }
        void OnDestroy() { while (fragments.Count > 0) RemoveFragment(); if (fragmentMaterial) Destroy(fragmentMaterial); }
    }
}
