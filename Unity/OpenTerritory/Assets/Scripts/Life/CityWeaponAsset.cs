using UnityEngine;

namespace OpenTerritory
{
    // Authored sockets travel with the imported art, including NPC instances.
    public sealed class CityWeaponAsset : MonoBehaviour
    {
        public string SourceModel, Author, SourceUrl, License, SourceSha256;
        public int SourceTriangles;
        public Transform Action, Magazine, Muzzle, Ejection, RightGrip, SupportGrip;
        public Renderer EmissionRenderer;
    }
}
