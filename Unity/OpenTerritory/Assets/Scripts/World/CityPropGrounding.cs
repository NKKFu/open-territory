using System;
using System.Collections.Generic;
using UnityEngine;

namespace OpenTerritory
{
    // Editor-baked placement and footings. No runtime raycasts or per-prop Update.
    public sealed class CityPropGrounding : MonoBehaviour
    {
        [Serializable] public sealed class Part
        {
            public MeshFilter filter;
            public Mesh original, medium, far, baked;
            public bool externalMesh;
        }
        [Serializable] public sealed class Shape
        {
            public BoxCollider collider;
            public Vector3 center, size;
            public bool footing;
            public int solidIndex = -1;
        }
        [Serializable] public sealed class Prop
        {
            public int nodeId;
            public string category;
            public Transform root;
            public Vector3 sourcePosition, originalPosition, position;
            public Part[] parts = Array.Empty<Part>();
            public Shape[] shapes = Array.Empty<Shape>();
            public Vector3[] groundSamples = Array.Empty<Vector3>();
            public float originalGap, minimumGround, maximumGround;
        }
        public string sourceHash;
        public Prop[] props = Array.Empty<Prop>();

        public static void Bind(CityContext ctx)
        {
            var baked = ctx.GetComponent<CityPropGrounding>();
            if (!baked) return;
            Physics.SyncTransforms();
            var owners = new Dictionary<Transform, Prop>();
            var shapeUpdates = new Dictionary<int, BoxCollider>();
            var sourcePositions = new Dictionary<int, Vector3>();
            foreach (var prop in ctx.Data.destructibles) sourcePositions[prop.id] = prop.position;
            foreach (var prop in baked.props)
            {
                // A new traffic route or dialogue also changes the whole-map hash.
                // Bind the actual scene objects and current records by identity.
                if (!prop.root || ctx.Node(prop.nodeId) != prop.root || !sourcePositions.ContainsKey(prop.nodeId)) continue;
                owners[prop.root] = prop;
                foreach (var shape in prop.shapes)
                    if (shape.solidIndex >= 0 && shape.collider) shapeUpdates[shape.solidIndex] = shape.collider;
            }
            var offsets = new Dictionary<int, Vector3>();
            foreach (var node in ctx.Nodes)
            {
                for (var parent = node.Value; parent; parent = parent.parent)
                    if (owners.TryGetValue(parent, out var prop))
                    {
                        offsets[node.Key] = prop.root.position - sourcePositions[prop.nodeId];
                        break;
                    }
            }
            foreach (var prop in ctx.Data.destructibles)
                if (offsets.TryGetValue(prop.id, out var delta)) prop.position += delta;
            foreach (var lamp in ctx.Data.streetLamps)
                if (offsets.TryGetValue(lamp.node, out var delta)) lamp.position += delta;
            foreach (var device in ctx.Data.devices)
                if (offsets.TryGetValue(device.node, out var delta))
                {
                    device.position += delta;
                    if (device.lookAt != Vector3.zero) device.lookAt += delta;
                }
            foreach (var solid in ctx.Data.solids)
                if (shapeUpdates.TryGetValue(solid.index, out var box) && ctx.Node(solid.owner) && box.transform.IsChildOf(ctx.Node(solid.owner)))
                {
                    var center = box.transform.TransformPoint(box.center);
                    solid.x = center.x; solid.z = center.z;
                    solid.bottom = box.bounds.min.y; solid.top = box.bounds.max.y;
                }
        }
    }
}
