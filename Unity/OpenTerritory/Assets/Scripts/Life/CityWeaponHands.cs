using System.Collections.Generic;
using UnityEngine;

namespace OpenTerritory
{
    // Sculpted closed hands replace the open blocks only while holding a gun.
    // Their pivots match the palm sockets used by the two-bone arm solver.
    public sealed class CityWeaponHands
    {
        readonly List<Renderer> originals = new List<Renderer>();
        readonly GameObject[] hands = new GameObject[2];
        readonly Mesh[] meshes = new Mesh[2];
        bool visible;

        public CityWeaponHands(CityRig rig)
        {
            for (int side = 0; side < 2; side++)
            {
                var node = rig.Hands[side]; Material skin = null;
                foreach (Transform child in node)
                {
                    var renderer = child.GetComponent<MeshRenderer>();
                    if (!renderer) continue;
                    originals.Add(renderer); if (!skin) skin = renderer.sharedMaterial;
                }
                var vertices = new List<Vector3>(); var triangles = new List<int>();
                float mirror = side == 1 ? 1 : -1;
                void Box(Vector3 centre, Vector3 size, float tilt = 0)
                {
                    int start = vertices.Count; var rotation = Quaternion.Euler(tilt, 0, 0);
                    foreach (var corner in new[] { new Vector3(-1,-1,-1), new Vector3(1,-1,-1), new Vector3(1,1,-1), new Vector3(-1,1,-1),
                        new Vector3(-1,-1,1), new Vector3(1,-1,1), new Vector3(1,1,1), new Vector3(-1,1,1) })
                    { var p = centre + rotation * Vector3.Scale(corner, size * .5f); p.x *= mirror; vertices.Add(p); }
                    int[] faces = {0,2,1,0,3,2,4,5,6,4,6,7,0,1,5,0,5,4,3,7,6,3,6,2,0,4,7,0,7,3,1,2,6,1,6,5};
                    for (int i = 0; i < faces.Length; i += 3)
                    { triangles.Add(start + faces[i]); triangles.Add(start + faces[i + (mirror > 0 ? 1 : 2)]); triangles.Add(start + faces[i + (mirror > 0 ? 2 : 1)]); }
                }
                Box(new Vector3(.050f, -.004f, -.022f), new Vector3(.068f, .125f, .127f));
                // Three bent fingers, a separate trigger finger and the thumb.
                for (int finger = 0; finger < 3; finger++)
                    Box(new Vector3(-.013f, -.043f + finger * .031f, .052f), new Vector3(.112f, .027f, .046f), -8);
                Box(new Vector3(-.026f, .057f, .078f), new Vector3(.034f, .026f, .102f), -8);
                Box(new Vector3(-.052f, .035f, -.008f), new Vector3(.029f, .051f, .103f), 28);
                // Wrist meets the sleeve without leaving a floating fist.
                Box(new Vector3(.021f, -.004f, -.081f), new Vector3(.104f, .108f, .065f));
                var mesh = new Mesh { name = side == 1 ? "Right gripping hand" : "Left support hand" };
                mesh.SetVertices(vertices); mesh.SetTriangles(triangles, 0); mesh.RecalculateNormals(); mesh.RecalculateBounds(); meshes[side] = mesh;
                var go = new GameObject(mesh.name); go.layer = node.gameObject.layer; go.transform.SetParent(node, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh; go.AddComponent<MeshRenderer>().sharedMaterial = skin;
                go.SetActive(false); hands[side] = go;
            }
        }
        public void Show(bool show, bool support = true)
        {
            if (visible != show) { foreach (var renderer in originals) if (renderer) renderer.enabled = !show; visible = show; }
            if (hands[1]) hands[1].SetActive(show);
            if (hands[0]) hands[0].SetActive(show && support);
            // A free support hand keeps its original appearance.
            if (show && !support)
            {
                foreach (var renderer in originals) if (renderer && renderer.transform.parent == hands[0].transform.parent) renderer.enabled = true;
            }
            else if (show)
            {
                foreach (var renderer in originals) if (renderer) renderer.enabled = false;
            }
        }
        public void Dispose()
        {
            Show(false); foreach (var hand in hands) if (hand) Object.Destroy(hand);
            foreach (var mesh in meshes) if (mesh) Object.Destroy(mesh);
        }
    }
}
