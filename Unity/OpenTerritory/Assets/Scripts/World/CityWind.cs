using UnityEngine;
using UnityEngine.Rendering;

namespace OpenTerritory
{
    // A fixed pool gives the street-level breeze a visible direction even when
    // the hillside trees are far away. All motion is in world space.
    [DefaultExecutionOrder(100)]
    public sealed class CityWind : MonoBehaviour
    {
        sealed class Mote
        {
            public Transform root; public Renderer renderer; public LineRenderer line;
            public Vector3[] points; public Vector3 position, velocity; public Color color;
            public float age, lifetime, phase, length, spin, ground, height, groundTimer; public bool alive;
        }
        readonly Mote[] leaves = new Mote[32], gusts = new Mote[10];
        readonly System.Random random = new System.Random(88121);
        MaterialPropertyBlock block;
        CityContext ctx; Material material; Mesh leafMesh;
        float leafBudget, gustBudget, coverTimer;
        bool sheltered;
        const int Obstacles = (1 << CityContext.GroundLayer) | (1 << CityContext.WorldLayer);
        public int ActiveLeaves { get; private set; }
        public int ActiveGusts { get; private set; }
        public int EmittedLeaves { get; private set; }
        public int EmittedGusts { get; private set; }

        public void Initialize(CityContext context)
        {
            ctx = context;
            block = new MaterialPropertyBlock();
            leafBudget = 2; gustBudget = 2;
            var shader = Resources.Load<Shader>("Shaders/CityWind");
            if (!shader) { Debug.LogError("Wind shader missing"); return; }
            material = new Material(shader) { name = "Folhas e rastros de vento" };
            leafMesh = new Mesh {
                name = "Folha dobrada low poly",
                vertices = new[] { new Vector3(0,.035f,0), new Vector3(-.18f,0,0), new Vector3(-.035f,0,.09f), new Vector3(.18f,0,0), new Vector3(.035f,0,-.09f) },
                colors = new[] { Color.white, new Color(.8f,.8f,.8f), Color.white, new Color(.8f,.8f,.8f), new Color(.9f,.9f,.9f) },
                triangles = new[] { 0,1,2, 0,2,3, 0,3,4, 0,4,1 }
            };
            leafMesh.RecalculateBounds();
            var width = new AnimationCurve(new Keyframe(0,0), new Keyframe(.2f,.85f), new Keyframe(.55f,1), new Keyframe(1,0));
            var fade = new Gradient();
            fade.SetKeys(new[] { new GradientColorKey(Color.white,0), new GradientColorKey(Color.white,1) },
                new[] { new GradientAlphaKey(0,0), new GradientAlphaKey(1,.25f), new GradientAlphaKey(.8f,.7f), new GradientAlphaKey(0,1) });
            for (int i = 0; i < leaves.Length; i++) leaves[i] = Create(false, i, width, fade);
            for (int i = 0; i < gusts.Length; i++) gusts[i] = Create(true, i, width, fade);
        }

        Mote Create(bool gust, int index, AnimationCurve width, Gradient fade)
        {
            var root = new GameObject((gust ? "Rastro de rajada • " : "Folha ao vento • ") + index).transform;
            root.SetParent(transform, false);
            var mote = new Mote { root = root };
            if (gust)
            {
                var line = root.gameObject.AddComponent<LineRenderer>(); mote.line = line; mote.renderer = line;
                mote.points = new Vector3[9]; line.positionCount = 9; line.useWorldSpace = true;
                line.widthCurve = width; line.colorGradient = fade; line.numCornerVertices = 1;
            }
            else
            {
                root.gameObject.AddComponent<MeshFilter>().sharedMesh = leafMesh;
                mote.renderer = root.gameObject.AddComponent<MeshRenderer>();
            }
            mote.renderer.sharedMaterial = material; mote.renderer.enabled = false;
            mote.renderer.shadowCastingMode = ShadowCastingMode.Off; mote.renderer.receiveShadows = false;
            mote.renderer.lightProbeUsage = LightProbeUsage.Off; mote.renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return mote;
        }

        void LateUpdate()
        {
            if (!ctx || !ctx.Ready || ctx.Paused || !material) return;
            float dt = Time.deltaTime;
            var wind = ctx.Systems.Weather.Wind;
            float gust = CityWindField.Gust(ctx.Player.position, ctx.Clock, wind);
            float speed = CityWindField.Sample(ctx.Player.position, ctx.Clock, wind).magnitude;
            coverTimer -= dt;
            if (coverTimer <= 0) { sheltered = Physics.Raycast(ctx.Player.position + Vector3.up * 1.4f, Vector3.up, 45, Obstacles, QueryTriggerInteraction.Ignore); coverTimer = .15f; }
            if (!sheltered && speed > .25f)
            {
                leafBudget = Mathf.Min(2, leafBudget + dt * (2.5f + speed * .45f) * (.65f + gust * .55f));
                gustBudget = Mathf.Min(2, gustBudget + dt * (1.1f + speed * .12f) * (.55f + gust * .5f));
                int quality = ctx.UI.Quality;
                Emit(leaves, quality == 0 ? 14 : quality == 2 ? 32 : 24, ref leafBudget, false);
                Emit(gusts, quality == 0 ? 5 : quality == 2 ? 10 : 8, ref gustBudget, true);
            }
            else { leafBudget = 0; gustBudget = 0; }
            ActiveLeaves = Move(leaves, wind, speed, dt);
            ActiveGusts = Move(gusts, wind, speed, dt);
        }

        void Emit(Mote[] pool, int limit, ref float budget, bool gust)
        {
            if (budget < 1) return;
            for (int i = 0; i < limit && budget >= 1; i++)
            {
                var mote = pool[i]; if (mote.alive) continue;
                budget -= 1;
                if (!SpawnPoint(gust, out var point, out float ground)) continue;
                mote.position = point; mote.root.position = point;
                mote.velocity = CityWindField.Sample(point, ctx.Clock, ctx.Systems.Weather.Wind);
                mote.ground = ground; mote.height = point.y - ground; mote.groundTimer = 0;
                mote.age = 0; mote.lifetime = Range(2.8f, 5.2f); mote.phase = Range(0, 6.28f); mote.length = Range(2.5f, 4.2f); mote.spin = Range(55, 170);
                mote.color = gust ? new Color(.86f,.91f,.88f,.42f) : Color.Lerp(new Color(.40f,.52f,.19f,.95f), new Color(.68f,.44f,.18f,.95f), Range(0,1));
                mote.root.localScale = Vector3.one * Range(.55f, 1.1f); mote.alive = true; mote.renderer.enabled = true;
                if (gust) EmittedGusts++; else EmittedLeaves++;
            }
        }

        bool SpawnPoint(bool gust, out Vector3 point, out float ground)
        {
            Vector3 forward = CityPhysics.Flat(ctx.Camera.transform.forward).normalized;
            Vector3 side = Vector3.Cross(Vector3.up, forward);
            for (int attempt = 0; attempt < 5; attempt++)
            {
                point = ctx.Player.position + forward * Range(2, 12) + side * Range(-8, 8) - ctx.Systems.Weather.Wind.normalized * Range(1, 3);
                if (!Physics.Raycast(point + Vector3.up * 8, Vector3.down, out var floor, 24, 1 << CityContext.GroundLayer, QueryTriggerInteraction.Ignore)) continue;
                if (floor.point.y <= CityMarine.SeaLevel + .05f) continue;
                point.y = floor.point.y + (gust ? Range(.55f, 1.7f) : Range(.18f, 1.15f));
                if (ctx.Systems.Sheltered(point) || Physics.CheckSphere(point, .18f, Obstacles, QueryTriggerInteraction.Ignore)) continue;
                ground = floor.point.y; return true;
            }
            point = Vector3.zero; ground = 0; return false;
        }

        int Move(Mote[] pool, Vector3 wind, float speed, float dt)
        {
            int active = 0;
            foreach (var mote in pool)
            {
                if (!mote.alive) continue;
                mote.age += dt;
                Vector3 flow = CityWindField.Sample(mote.position, ctx.Clock, wind), side = Vector3.Cross(Vector3.up, flow.normalized);
                float gust = CityWindField.Gust(mote.position, ctx.Clock, wind);
                Vector3 desired = flow * 1.35f + side * (Mathf.Sin(mote.age * 1.6f + mote.phase) * .25f);
                mote.velocity = Vector3.Lerp(mote.velocity, desired, 1 - Mathf.Exp(-dt * 3));
                Vector3 next = mote.position + mote.velocity * dt;
                mote.groundTimer -= dt;
                if (mote.groundTimer <= 0)
                {
                    if (Physics.Raycast(next + Vector3.up * 4, Vector3.down, out var floor, 12, 1 << CityContext.GroundLayer, QueryTriggerInteraction.Ignore)) mote.ground = floor.point.y;
                    mote.groundTimer = .25f;
                }
                float lift = mote.line ? .06f * Mathf.Sin(ctx.Clock * 1.5f + mote.phase) : .17f * Mathf.Sin(mote.age * 2.8f + mote.phase) + Mathf.Max(0, gust - 1) * .26f;
                next.y = Mathf.Lerp(next.y, mote.ground + Mathf.Max(.10f, mote.height + lift), 1 - Mathf.Exp(-dt * 6));
                if (mote.age >= mote.lifetime || (next - ctx.Player.position).sqrMagnitude > 32 * 32 || Physics.Linecast(mote.position, next, Obstacles, QueryTriggerInteraction.Ignore))
                { mote.alive = false; mote.renderer.enabled = false; continue; }
                mote.position = next; mote.root.position = next;
                if (mote.line)
                {
                    bool blocked = false; float localSpeed = Mathf.Max(.8f, mote.velocity.magnitude);
                    for (int i = 0; i < mote.points.Length; i++)
                    {
                        float t = i / (float)(mote.points.Length - 1);
                        float past = (1 - t) * mote.length / localSpeed;
                        mote.points[i] = next - CityWindField.Sample(next, ctx.Clock - past, wind) * (past * 1.35f)
                            + Vector3.up * (Mathf.Sin(t * Mathf.PI) * .12f);
                        if (i > 0 && Physics.Linecast(mote.points[i - 1], mote.points[i], Obstacles, QueryTriggerInteraction.Ignore)) blocked = true;
                    }
                    if (blocked) { mote.alive = false; mote.renderer.enabled = false; continue; }
                    mote.line.widthMultiplier = .028f + Mathf.Min(10, localSpeed) * .0025f;
                    mote.line.SetPositions(mote.points);
                }
                else mote.root.rotation = Quaternion.Euler(Mathf.Sin(mote.age * 3 + mote.phase) * 38 + mote.age * mote.spin * .4f,
                    mote.age * mote.spin + mote.phase * 57, Mathf.Sin(mote.age * 2.2f + mote.phase) * 48);
                float fade = Mathf.SmoothStep(0, 1, mote.age / .3f) * Mathf.SmoothStep(0, 1, (mote.lifetime - mote.age) / .7f);
                Color color = mote.color; color.a *= fade;
                block.SetColor("_Color", color); block.SetFloat("_Ribbon", mote.line ? 1 : 0); mote.renderer.SetPropertyBlock(block); active++;
            }
            return active;
        }

        float Range(float min, float max) => Mathf.Lerp(min, max, (float)random.NextDouble());
        void OnDestroy() { if (material) Destroy(material); if (leafMesh) Destroy(leafMesh); }
    }
}
