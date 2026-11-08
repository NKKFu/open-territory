using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace OpenTerritory.Editor
{
    public static class CityHomeLayoutBuilder
    {
        public const float HallHalf = 1.78f;
        [MenuItem("Open Territory/Ajustar corredores e cozinhas das casas")]
        public static void Fit()
        {
            if (Application.isPlaying) throw new InvalidOperationException("Saia do Play Mode antes de ajustar interiores.");
            var homes = UnityEngine.Object.FindFirstObjectByType<CityHomeCatalog>();
            var registry = UnityEngine.Object.FindFirstObjectByType<WorldRegistry>();
            var world = JsonUtility.FromJson<WorldData>(registry.manifest.text);
            Undo.RecordObject(homes, "Ajustar interiores");
            foreach (var home in homes.homes)
            {
                if (home.units == 1)
                {
                    var p = world.places[home.place]; float w = p.lotWidth * .5f - .24f;
                    foreach (Transform t in home.root)
                    {
                        if (t.name == "Parede • passagens e portas")
                        { Undo.RecordObject(t, "Alargar corredor"); var at = t.localPosition; at.x = Mathf.Sign(at.x) * HallHalf; t.localPosition = at; }
                        if (t.name == "Divisória entre os cômodos")
                        { Undo.RecordObject(t, "Ajustar divisória"); var at = t.localPosition; at.x = Mathf.Sign(at.x) * (w + HallHalf) * .5f; t.localPosition = at; var scale = t.localScale; scale.x = w - HallHalf; t.localScale = scale; }
                    }
                    foreach (var room in homes.rooms.Where(r => r.place == home.place))
                    {
                        var at = home.root.InverseTransformPoint(room.center); int side = at.x < 0 ? -1 : 1;
                        at.x = side * (w + HallHalf) * .5f; room.center = home.root.TransformPoint(at); room.size.x = w - HallHalf;
                        at = home.root.InverseTransformPoint(room.entrance); at.x = side * HallHalf; room.entrance = home.root.TransformPoint(at);
                        var floor = home.root.Find("Piso • " + room.name);
                        Undo.RecordObject(floor, "Ajustar piso do cômodo"); at = floor.localPosition; at.x = side * (w + HallHalf) * .5f; floor.localPosition = at;
                        var scale = floor.localScale; scale.x = room.size.x - .08f; floor.localScale = scale;
                    }
                }
                foreach (var kitchen in home.root.GetComponentsInChildren<Transform>().Where(t => t.name == "Cozinha • preparo e refeições"))
                    foreach (Transform t in kitchen)
                        if (t.name == "counter" || home.units == 1 && (t.name == "oven" || t.name == "fridge"))
                        { Undo.RecordObject(t, "Orientar cozinha"); t.localRotation = Quaternion.Euler(0, 180, 0); }
            }
            Physics.SyncTransforms(); EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Debug.Log("HOME_LAYOUT_FITTED houses=" + homes.homes.Count(h => h.units == 1) + " kitchens=" + homes.homes.Sum(h => h.units));
        }
    }
}
