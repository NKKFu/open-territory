using System;
using UnityEngine;

namespace OpenTerritory
{
    // Baked in the editor. This component contains no generation or automatic QA.
    public sealed class CityHomeCatalog : MonoBehaviour
    {
        [Serializable] public sealed class Home
        {
            public int place, units = 1;
            public string name;
            public Transform root;
            public Vector3[] stairRoute = Array.Empty<Vector3>();
        }
        [Serializable] public sealed class Room
        {
            public int place, unit;
            public string name;
            public Vector3 center, size, entrance;
        }
        [Serializable] public sealed class InteriorDoor
        {
            public Transform leaf;
            public float width = 1.4f, height = 3.05f;
        }
        public Home[] homes = Array.Empty<Home>();
        public Room[] rooms = Array.Empty<Room>();
        public InteriorDoor[] doors = Array.Empty<InteriorDoor>();
        public int[] removedSolids = Array.Empty<int>();
        public void Apply(WorldData data)
        {
            // These are manifest array slots. Donor solid IDs are not unique.
            foreach(int slot in removedSolids)if(slot>=0&&slot<data.solids.Length)data.solids[slot].active=false;
        }
    }
}
