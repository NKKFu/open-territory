using UnityEngine;

namespace OpenTerritory
{
    public static class CitySteeringWheel
    {
        public const float GripRadius = .215f;
        public const string ColumnName = "Steering column";
        static Mesh columnMesh;

        // Leave room between the driver's chest and the rim, below the face
        // and behind the dashboard. Fit from the authored anchors so repeated
        // fits never accumulate offsets.
        public static void Fit(VehicleData vehicle, Transform wheel)
        {
            if (!wheel || vehicle.kind != "car") return;
            wheel.localPosition = new Vector3(vehicle.wheelMount.x, vehicle.wheelMount.y - .12f, vehicle.seatMount.z + .70f);
            wheel.localRotation = Quaternion.Euler(18, 0, 0);
            FitColumn(vehicle, wheel);
        }

        static void FitColumn(VehicleData vehicle, Transform wheel)
        {
            if (!columnMesh) columnMesh = Resources.Load<Mesh>("Vehicles/SteeringColumn");
            if (!columnMesh) return; // The editor fitter creates the shared asset before baking.
            var dashboard = Dashboard(vehicle, wheel);
            if (dashboard.size == Vector3.zero) return;
            var column = wheel.parent.Find(ColumnName);
            if (!column) { column = new GameObject(ColumnName).transform; column.SetParent(wheel.parent, false); }
            column.gameObject.layer = wheel.parent.gameObject.layer;
            column.localPosition = wheel.localPosition;
            column.localRotation = wheel.localRotation;
            column.localScale = Vector3.one;
            var rim = wheel.GetComponentsInChildren<MeshFilter>(true);
            MeshFilter largest = null;
            foreach (var mesh in rim) if (mesh.sharedMesh && (!largest || mesh.sharedMesh.vertexCount > largest.sharedMesh.vertexCount)) largest = mesh;
            var material = largest ? largest.GetComponent<Renderer>().sharedMaterial : null;
            // The hub overlaps the spokes. The stationary shroud runs into the
            // actual dashboard, including the taller van's lower mounting point.
            var mount = new Vector3(vehicle.wheelMount.x, dashboard.max.y - .07f, dashboard.min.z + .09f);
            var end = column.parent.TransformPoint(mount);
            var endLocal = column.InverseTransformPoint(end);
            Part(column, "Hub", new Vector3(0, 0, -.035f), new Vector3(0, 0, .105f), .085f, material);
            Part(column, "Shroud", new Vector3(0, 0, .06f), endLocal, .065f, material);
        }

        static void Part(Transform parent, string name, Vector3 from, Vector3 to, float radius, Material material)
        {
            var part = parent.Find(name);
            if (!part)
            {
                part = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer)).transform;
                part.SetParent(parent, false);
            }
            part.gameObject.layer = parent.parent.gameObject.layer;
            part.localPosition = from;
            part.localRotation = Quaternion.LookRotation(to - from);
            part.localScale = new Vector3(radius, radius, Vector3.Distance(from, to));
            part.GetComponent<MeshFilter>().sharedMesh = columnMesh;
            part.GetComponent<MeshRenderer>().sharedMaterial = material;
        }

        static Bounds Dashboard(VehicleData vehicle, Transform wheel)
        {
            // Imported box batches retain 24 vertices per authored box. Match
            // the full-width dash body, rather than a windshield/collider bound.
            foreach (var mesh in wheel.parent.GetComponentsInChildren<MeshFilter>(true))
            {
                if (!mesh.sharedMesh || mesh.transform.IsChildOf(wheel)) continue;
                var vertices = mesh.sharedMesh.vertices;
                if (vertices.Length % 24 != 0) continue;
                var matrix = wheel.parent.worldToLocalMatrix * mesh.transform.localToWorldMatrix;
                for (int i = 0; i < vertices.Length; i += 24)
                {
                    var bounds = new Bounds(matrix.MultiplyPoint3x4(vertices[i]), Vector3.zero);
                    for (int j = 1; j < 24; j++) bounds.Encapsulate(matrix.MultiplyPoint3x4(vertices[i + j]));
                    if (bounds.size.x > vehicle.width * .7f && Mathf.Abs(bounds.size.y - .22f) < .005f &&
                        Mathf.Abs(bounds.size.z - .39f) < .005f && bounds.center.y > 1 && bounds.center.z > .5f)
                        return bounds;
                }
            }
            return new Bounds();
        }

        public static Vector3 GripPoint(Transform wheel, Quaternion neutralRotation, float turn, int hand)
        {
            // Limit the driver's reach as before, but keep both hands in the
            // wheel's tilted plane, including when the vehicle pitches or rolls.
            float grip = Mathf.Clamp(turn, -1.1f, 1.1f), side = hand == 0 ? -1 : 1;
            var point = new Vector3(side * GripRadius * Mathf.Cos(grip), side * GripRadius * Mathf.Sin(grip), -.025f);
            return wheel.parent.TransformPoint(wheel.localPosition + neutralRotation * point);
        }
    }
}
