using System;
using UnityEngine;

namespace OpenTerritory
{
    // Geometry and openings are authored in the Editor; runtime only changes the glazing glow.
    public sealed class CityWindows : MonoBehaviour
    {
        [Serializable] public sealed class Window
        {
            public int place;
            public Vector3 center, outward;
            public float width, height;
        }
        [Serializable] public sealed class Glazing { public int place; public Renderer renderer; }
        public Window[] windows = Array.Empty<Window>();
        public Glazing[] glazing = Array.Empty<Glazing>();
        MaterialPropertyBlock block;
        public void Tick(float[] levels)
        {
            if (block == null) block = new MaterialPropertyBlock();
            foreach (var pane in glazing)
            {
                if (!pane.renderer || pane.place >= levels.Length) continue;
                pane.renderer.GetPropertyBlock(block);
                block.SetColor("_EmissionColor", new Color(1, .78f, .46f) * (.015f + levels[pane.place] * .18f));
                pane.renderer.SetPropertyBlock(block);
            }
        }
    }
}
