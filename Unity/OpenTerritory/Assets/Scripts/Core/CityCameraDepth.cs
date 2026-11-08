using System.Collections.Generic;
using UnityEngine;

namespace OpenTerritory
{
    // Camera effects share one request: disabling AO must not remove the depth
    // texture while sun rays are still using it, or the other way around.
    internal static class CityCameraDepth
    {
        sealed class Request { public int users; public bool inherited; }
        static readonly Dictionary<Camera, Request> requests = new Dictionary<Camera, Request>();
        public static void Acquire(Camera camera)
        {
            if (!requests.TryGetValue(camera, out var request))
            {
                request = new Request { inherited = (camera.depthTextureMode & DepthTextureMode.Depth) != 0 };
                requests.Add(camera, request);
            }
            request.users++; camera.depthTextureMode |= DepthTextureMode.Depth;
        }
        public static void Release(Camera camera)
        {
            if (ReferenceEquals(camera, null) || !requests.TryGetValue(camera, out var request)) return;
            if (--request.users > 0) return;
            if (camera && !request.inherited) camera.depthTextureMode &= ~DepthTextureMode.Depth;
            requests.Remove(camera);
        }
    }
}
