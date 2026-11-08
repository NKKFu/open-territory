using UnityEngine;

namespace OpenTerritory
{
    // Baked metadata only: ground cover has no simulation, colliders or runtime mesh generation.
    public sealed class CityGroundCoverInfo : MonoBehaviour
    {
        public string sourceHash;
        public int grassTufts, shrubs, grassChunks, shrubChunks;
        public int groveShrubs;
        public Vector3[] groveShrubSamples;
        public int scatteredShrubs;
        public Vector3[] scatteredShrubSamples;
        public Vector3[] grassSamples, shrubSamples;
    }
}
