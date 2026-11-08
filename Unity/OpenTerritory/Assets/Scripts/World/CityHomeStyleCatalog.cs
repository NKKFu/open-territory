using System;
using UnityEngine;

namespace OpenTerritory
{
    // Authored decoration only. No generation or tests run in normal gameplay.
    public sealed class CityHomeStyleCatalog : MonoBehaviour
    {
        [Serializable] public sealed class Unit
        {
            public int place, unit, palette, rugPattern, art;
            public string combination;
            public Transform root;
        }
        public Unit[] units = Array.Empty<Unit>();
    }
}
