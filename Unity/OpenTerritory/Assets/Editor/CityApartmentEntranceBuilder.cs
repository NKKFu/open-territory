using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace OpenTerritory.Editor
{
    // The apartment shell replaced a donor facade, but its 1.40 m opening did
    // not include the retained glazed sidelights. Fit that shell to the entire
    // existing assembly, without moving the entrance, windows or navigation.
    public static class CityApartmentEntranceBuilder
    {
        const string UndoName = "Alinhar entradas dos apartamentos";
        const float Floor = .18f, WallTop = 4.32f, LeafTop = 3.3f, Jamb = .30f;

        [MenuItem("Open Territory/Alinhar entradas dos apartamentos")]
        public static void BakeActiveScene()
        {
            var registry = UnityEngine.Object.FindFirstObjectByType<WorldRegistry>();
            if (!registry || !registry.manifest) throw new InvalidOperationException("A cena da cidade não está carregada.");
            Fit(JsonUtility.FromJson<WorldData>(registry.manifest.text), registry);
        }

        public static void Fit(WorldData world, WorldRegistry registry)
        {
            if (Application.isPlaying) throw new InvalidOperationException("Saia do Play Mode antes de alinhar entradas.");
            var catalog = UnityEngine.Object.FindFirstObjectByType<CityHomeCatalog>();
            if (!catalog) throw new InvalidOperationException("Interiores dos apartamentos ausentes.");
            var nodes = registry.nodes.Where(n => n.transform).ToDictionary(n => n.id, n => n.transform);
            var restored = new HashSet<int>();
            foreach (var home in catalog.homes.Where(h => h.units > 1))
            {
                var place = world.places[home.place]; var leaf = nodes[place.door.id];
                var panels = world.solids.Select((s, slot) => new { solid = s, slot }).Where(s =>
                    nodes.TryGetValue(s.solid.owner, out var t) && t.name.StartsWith("Doorway sidelight-" + place.door.id + "-")).ToArray();
                if (panels.Length != 2) throw new InvalidOperationException("Painéis laterais ausentes: " + place.name);
                float width = place.door.width + panels.Sum(p => p.solid.w), half = width * .5f;
                float facadeZ = place.lotDepth * .5f;
                var middle = home.root.InverseTransformPoint(new Vector3(place.door.x, place.elevation, place.door.z));
                var lateral = home.root.Cast<Transform>().Where(t => t.name == "Entrada • lateral").OrderBy(t => t.localPosition.x).ToArray();
                var lintel = home.root.Find("Entrada • verga");
                var frontWalls = home.root.Cast<Transform>().Where(t => t.name == "Fachada • janelas abertas" &&
                    Mathf.Abs(t.localPosition.z - facadeZ) < .01f && Mathf.Abs(t.localPosition.y - Floor) < .01f && Mathf.Abs(t.localPosition.x) > 1).ToArray();
                if (lateral.Length != 2 || !lintel || frontWalls.Length != 2)
                    throw new InvalidOperationException("Fachada de apartamento inesperada: " + place.name);

                foreach (var wall in frontWalls)
                {
                    int side = wall.localPosition.x < middle.x ? -1 : 1;
                    var end = wall.Cast<Transform>().Where(t => t.name == "Parede").OrderBy(t => Mathf.Abs(home.root.InverseTransformPoint(t.position).x - middle.x)).First();
                    var center = home.root.InverseTransformPoint(end.position);
                    float outer = center.x + side * end.localScale.x * .5f, inner = middle.x + side * (half + Jamb);
                    if ((outer - inner) * side <= 0) throw new InvalidOperationException("Entrada invadiria uma janela: " + place.name);
                    Undo.RecordObject(end, UndoName);
                    var position = end.localPosition; position.x = (outer + inner) * .5f - wall.localPosition.x; end.localPosition = position;
                    var scale = end.localScale; scale.x = Mathf.Abs(outer - inner); end.localScale = scale;
                }
                for (int i = 0; i < lateral.Length; i++)
                {
                    Undo.RecordObject(lateral[i], UndoName);
                    lateral[i].localPosition = new Vector3(middle.x + (i == 0 ? -1 : 1) * (half + Jamb * .5f), (Floor + WallTop) * .5f, facadeZ);
                    lateral[i].localScale = new Vector3(Jamb, WallTop - Floor, .24f);
                }
                Undo.RecordObject(lintel, UndoName);
                lintel.localPosition = new Vector3(middle.x, (LeafTop + WallTop) * .5f, facadeZ);
                lintel.localScale = new Vector3(width, WallTop - LeafTop, .24f);

                // A deep casing bridges the donor's door plane and the new wall.
                var casing = home.root.Find("Entrada • moldura alinhada");
                if (!casing)
                {
                    var go = new GameObject("Entrada • moldura alinhada"); Undo.RegisterCreatedObjectUndo(go, UndoName);
                    casing = go.transform; casing.SetParent(home.root, false);
                }
                var donor = leaf.GetComponentsInChildren<MeshFilter>().First();
                var baseMesh = home.root.Find("Base do edifício").GetComponent<MeshFilter>();
                float depth = middle.z - facadeZ + .20f, z = (middle.z + facadeZ) * .5f;
                for (int side = -1; side <= 1; side += 2)
                    Block(casing, side < 0 ? "Jamb left" : "Jamb right", donor,
                        new Vector3(middle.x + side * (half + .06f), (Floor + LeafTop) * .5f, z), new Vector3(.12f, LeafTop - Floor, depth));
                Block(casing, "Header", donor, new Vector3(middle.x, LeafTop + .06f, z), new Vector3(width + .24f, .12f, depth));
                var threshold = Block(casing, "Threshold", baseMesh, new Vector3(middle.x, Floor * .5f, z + .10f), new Vector3(width + .24f, Floor, depth + .30f));
                threshold.gameObject.layer = CityContext.GroundLayer;
                if (!threshold.GetComponent<BoxCollider>()) Undo.AddComponent<BoxCollider>(threshold.gameObject);

                // Preparing the replacement apartment shell removed its donor
                // colliders, including the fixed panes. Restore those panes.
                foreach (var panel in panels)
                {
                    var s = panel.solid; var t = nodes[s.owner]; s.active = true; restored.Add(panel.slot);
                    Undo.RecordObject(t.gameObject, UndoName); t.gameObject.layer = CityContext.WorldLayer;
                    var box = t.GetComponent<BoxCollider>(); if (!box) box = Undo.AddComponent<BoxCollider>(t.gameObject);
                    Undo.RecordObject(box, UndoName);
                    box.center = t.InverseTransformPoint(new Vector3(s.x, (s.bottom + s.top) * .5f, s.z));
                    box.size = new Vector3(s.w, s.top - s.bottom, s.d);
                }
                var lod = home.root.GetComponent<CityObjectLod>();
                if (lod)
                {
                    Undo.RecordObject(lod, UndoName);
                    var parts = lod.parts.ToList();
                    foreach (var filter in casing.GetComponentsInChildren<MeshFilter>())
                        if (!parts.Any(p => p.filter == filter)) parts.Add(new CityLodPart { filter = filter, renderer = filter.GetComponent<Renderer>(),
                            full = filter.sharedMesh, medium = filter.sharedMesh, far = filter.sharedMesh, shadows = filter.GetComponent<Renderer>().shadowCastingMode });
                    lod.parts = parts.ToArray();
                }
            }
            Undo.RecordObject(catalog, UndoName); catalog.removedSolids = catalog.removedSolids.Where(slot => !restored.Contains(slot)).ToArray();
            Physics.SyncTransforms(); EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Debug.Log("APARTMENT_ENTRANCES_ALIGNED apartments=" + catalog.homes.Count(h => h.units > 1) + " fixedPanels=" + restored.Count);
        }

        static Transform Block(Transform parent, string name, MeshFilter source, Vector3 position, Vector3 size)
        {
            var t = parent.Find(name);
            if (!t)
            {
                var go = new GameObject(name); Undo.RegisterCreatedObjectUndo(go, UndoName); t = go.transform; t.SetParent(parent, false);
                go.AddComponent<MeshFilter>().sharedMesh = source.sharedMesh;
                var renderer = go.AddComponent<MeshRenderer>(); renderer.sharedMaterials = source.GetComponent<Renderer>().sharedMaterials;
                renderer.shadowCastingMode = source.GetComponent<Renderer>().shadowCastingMode;
            }
            Undo.RecordObject(t, UndoName); t.localPosition = position; t.localScale = size; return t;
        }
    }
}
