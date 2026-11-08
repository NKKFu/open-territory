using System;
using System.Collections.Generic;
using UnityEngine;

namespace OpenTerritory
{
    // Only the protagonist's renderers are changed. Shared city/NPC materials stay intact.
    public sealed class CityWardrobe : MonoBehaviour
    {
        public sealed class Garment
        {
            public string id, name; public int category, price, cut; public Color color, trim;
            public Garment(string id, string name, int category, int price, string color, string trim, int cut = 0)
            { this.id = id; this.name = name; this.category = category; this.price = price; this.cut = cut; ColorUtility.TryParseHtmlString(color, out this.color); ColorUtility.TryParseHtmlString(trim, out this.trim); }
        }
        public static readonly Garment[] Catalog = {
            new Garment("field-jacket", "Field jacket", 0, 0, "#E9B857", "#D3A74E"),
            new Garment("coast-tee", "Coast tee", 0, 65, "#2DACA7", "#207B80", 1),
            new Garment("sunset-tee", "Sunset tee", 0, 65, "#E87F6C", "#B75D52", 1),
            new Garment("navy-overshirt", "Navy overshirt", 0, 120, "#344B72", "#223754", 2),
            new Garment("original-trousers", "Slate trousers", 1, 0, "#3C5862", "#3C5862"),
            new Garment("indigo-denim", "Indigo denim", 1, 85, "#344F83", "#344F83"),
            new Garment("sand-chinos", "Sand chinos", 1, 90, "#B9A482", "#B9A482"),
            new Garment("charcoal-trousers", "Charcoal trousers", 1, 95, "#3D414E", "#3D414E"),
            new Garment("original-sneakers", "Trail sneakers", 2, 0, "#38494E", "#C8C9B4"),
            new Garment("cloud-sneakers", "Cloud sneakers", 2, 75, "#E4E9E4", "#728E99"),
            new Garment("coral-sneakers", "Coral sneakers", 2, 80, "#CF6B60", "#F1DFCB"),
            new Garment("ocean-sneakers", "Ocean sneakers", 2, 80, "#355A85", "#BBD1CF")
        };
        [Serializable] sealed class Saved { public int version = 1; public string[] owned, equipped; }
        sealed class Part { public Renderer renderer; public int material; public Vector3 scale, position; }
        readonly HashSet<string> owned = new HashSet<string>();
        readonly List<Part> parts = new List<Part>();
        readonly string[] equipped = new string[3];
        MaterialPropertyBlock block;
        CityContext ctx;
        Garment preview;
        public string PreviewId => preview == null ? null : preview.id;
        public string Equipped(int category) => category >= 0 && category < 3 ? equipped[category] : null;
        public bool Owns(string id) => owned.Contains(id);
        public static Garment Find(string id) { foreach (var g in Catalog) if (g.id == id) return g; return null; }
        public void Initialize(CityContext context)
        {
            ctx = context; block = new MaterialPropertyBlock();
            foreach (var r in ctx.Player.GetComponentsInChildren<Renderer>(true))
            {
                string name = r.sharedMaterial ? r.sharedMaterial.name : "";
                if (!name.StartsWith("material-") || !int.TryParse(name.Substring(9), out int material)) continue;
                if (material >= 260 && material <= 271)
                    parts.Add(new Part { renderer = r, material = material, scale = r.transform.localScale, position = r.transform.localPosition });
            }
            Defaults(); Apply();
        }
        void Defaults()
        {
            owned.Clear();
            for (int i = 0; i < 3; i++) foreach (var g in Catalog) if (g.category == i && g.price == 0) { owned.Add(g.id); equipped[i] = g.id; break; }
            preview = null;
        }
        public void Preview(string id) { preview = Find(id); Apply(); }
        public void CancelPreview() { preview = null; Apply(); }
        public bool BuyOrWear(string id, out string receipt)
        {
            var g = Find(id);
            if (g == null || !ctx || ctx.Paused) { receipt = "Unavailable"; return false; }
            if (owned.Contains(id))
            {
                equipped[g.category] = id; preview = null; Apply(); ctx.Commerce.Checkpoint(); receipt = "Wearing " + g.name; return true;
            }
            bool paid = ctx.Commerce.TryPurchase(g.price, () => { owned.Add(id); equipped[g.category] = id; preview = null; Apply(); });
            receipt = paid ? "Wearing " + g.name : "Need " + CityCommerce.Cash(g.price - ctx.Commerce.Balance);
            return paid;
        }
        public string ExportJson()
        {
            var ids = new List<string>(owned); ids.Sort(StringComparer.Ordinal);
            return JsonUtility.ToJson(new Saved { owned = ids.ToArray(), equipped = (string[])equipped.Clone() });
        }
        public void RestoreJson(string json)
        {
            Defaults();
            try
            {
                var saved = string.IsNullOrEmpty(json) ? null : JsonUtility.FromJson<Saved>(json);
                if (saved != null && saved.version == 1)
                {
                    if (saved.owned != null) foreach (string id in saved.owned) if (Find(id) != null) owned.Add(id);
                    if (saved.equipped != null) for (int i = 0; i < Mathf.Min(3, saved.equipped.Length); i++)
                    { var g = Find(saved.equipped[i]); if (g != null && g.category == i && owned.Contains(g.id)) equipped[i] = g.id; }
                }
            }
            catch (ArgumentException) { /* An old/corrupt wardrobe keeps the original outfit. */ }
            Apply();
        }
        void LateUpdate() { if (ctx && ctx.Ready) ApplyVisibility(); }
        Garment Current(int category) => preview != null && preview.category == category ? preview : Find(equipped[category]);
        bool Hidden(Part part, Garment top)
        {
            // Tee: remove jacket pockets, shoulder trim and pack; overshirt keeps its pockets.
            if (top.cut == 1 && part.renderer.transform.parent.name == "costume-details") return true;
            return top.cut != 0 && part.material >= 265 && part.material <= 267;
        }
        void ApplyVisibility()
        {
            var top = Current(0); if (top == null) return;
            foreach (var part in parts) if (part.renderer) part.renderer.forceRenderingOff = Hidden(part, top);
        }
        void Apply()
        {
            if (block == null) return;
            var top = Current(0); var pants = Current(1); var shoes = Current(2);
            if (top == null || pants == null || shoes == null) return;
            Color skin = new Color(.910f, .722f, .588f);
            foreach (var part in parts)
            {
                var r = part.renderer; if (!r) continue;
                Color color = r.sharedMaterial.color;
                bool lowerSleeve = r.name == "Mesh-4018" || r.name == "Mesh-4024";
                bool cuff = r.name == "Mesh-4019" || r.name == "Mesh-4025";
                if (part.material == 260 && top.price > 0) color = top.color;
                if (part.material == 261 && top.price > 0) color = top.trim;
                if (top.cut == 1 && (lowerSleeve || cuff)) color = skin;
                if (part.material == 271 && pants.price > 0) color = pants.color;
                if (part.material == 268 && shoes.price > 0) color = shoes.color;
                if ((part.material == 269 || part.material == 270) && shoes.price > 0) color = shoes.trim;
                r.GetPropertyBlock(block); block.SetColor("_Color", color); r.SetPropertyBlock(block);
                if (r.name == "Mesh-4017" || r.name == "Mesh-4023")
                {
                    float sleeve = top.cut == 1 ? .50f : 1;
                    r.transform.localScale = Vector3.Scale(part.scale, new Vector3(1, sleeve, 1));
                    r.transform.localPosition = new Vector3(part.position.x, part.position.y * sleeve, part.position.z);
                }
            }
            ApplyVisibility();
        }
    }
}
