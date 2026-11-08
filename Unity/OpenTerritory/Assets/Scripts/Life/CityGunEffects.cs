using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace OpenTerritory
{
    // All emitters, traces, marks, lights and cases are bounded pools. Nothing
    // is instantiated by a shot; case motion and lifetimes use the city clock.
    [DefaultExecutionOrder(120)]
    public sealed class CityGunEffects : MonoBehaviour
    {
        sealed class Trace { public LineRenderer line; public Vector3 from,to; public float start,until; public bool pulse; }
        sealed class Case { public Transform root; public Vector3 velocity,spin; public float until; public int bounces; }
        sealed class Mark { public Transform root,support; public Vector3 localPoint,localNormal; public Renderer renderer; public float until,roll; }
        CityContext ctx;
        Transform pool;
        ParticleSystem flashes, smoke, sparks, dust, energy, rings, blood;
        readonly List<ParticleSystem> systems=new List<ParticleSystem>();
        readonly List<Material> materials=new List<Material>();
        readonly List<Trace> traces=new List<Trace>();
        readonly List<Case> cases=new List<Case>();
        readonly List<Mark> marks=new List<Mark>();
        readonly List<Mark> bloodMarks=new List<Mark>();
        Mesh caseMesh,markMesh,bloodMesh;
        MaterialPropertyBlock tint;
        Light muzzleLight;
        float lightUntil; int traceIndex,caseIndex,markIndex,bloodIndex;
        public IReadOnlyList<ParticleSystem> Emitters => systems;
        public int MuzzleBursts { get; private set; }
        public int ImpactBursts { get; private set; }
        public int EjectedCases { get; private set; }
        public int ImpactMarks { get; private set; }
        public int BloodBursts { get; private set; }
        public int BloodMarks { get; private set; }
        public int LiveBloodParticles => blood ? blood.particleCount : 0;
        public int ActiveBloodMarks { get { int n=0; foreach(var mark in bloodMarks)if(mark.root.gameObject.activeSelf)n++;return n; } }
        public ParticleSystem BloodEmitter => blood;
        public Vector3 LastMuzzle { get; private set; }
        public Vector3 LastContact { get; private set; }
        public string LastSurface { get; private set; }
        public int PoolSize => traces.Count+cases.Count+marks.Count+bloodMarks.Count+systems.Count+1;
        public int LiveParticles { get { int n=0;foreach(var ps in systems)n+=ps.particleCount;return n; } }
        public bool TracesVisible { get { foreach(var trace in traces)if(trace.line.enabled)return true;return false; } }
        public Vector3 LatestTraceEnd => traces.Count==0?Vector3.zero:traces[(traceIndex-1+traces.Count)%traces.Count].to;

        Material ParticleMaterial(string label, bool additive=false, bool star=false, bool ring=false, bool solid=false)
        {
            var material=new Material(Resources.Load<Shader>("Shaders/CityGunParticle")){name=label};
            material.SetFloat("_DstBlend",additive?(int)BlendMode.One:(int)BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_Star",star?1:0);material.SetFloat("_Ring",ring?1:0);material.SetFloat("_Solid",solid?1:0);materials.Add(material);return material;
        }
        ParticleSystem Emitter(string label,Material material,int capacity,float gravity, float grow=1)
        {
            var go=new GameObject(label);go.transform.SetParent(pool,false);
            var ps=go.AddComponent<ParticleSystem>();ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
            var main=ps.main;main.loop=true;main.playOnAwake=false;main.duration=5;main.maxParticles=capacity;
            main.simulationSpace=ParticleSystemSimulationSpace.World;main.cullingMode=ParticleSystemCullingMode.AlwaysSimulate;
            main.startSpeed=0;main.startLifetime=.4f;main.startSize=.05f;main.gravityModifier=gravity;
            var emission=ps.emission;emission.enabled=false;var shape=ps.shape;shape.enabled=false;
            var color=ps.colorOverLifetime;color.enabled=true;
            var gradient=new Gradient();gradient.SetKeys(new[]{new GradientColorKey(Color.white,0),new GradientColorKey(Color.white,1)},new[]{new GradientAlphaKey(.8f,0),new GradientAlphaKey(.55f,.3f),new GradientAlphaKey(0,1)});color.color=gradient;
            var size=ps.sizeOverLifetime;size.enabled=true;size.size=new ParticleSystem.MinMaxCurve(1,new AnimationCurve(new Keyframe(0,.65f),new Keyframe(1,grow)));
            var renderer=go.GetComponent<ParticleSystemRenderer>();renderer.enabled=true;renderer.renderMode=ParticleSystemRenderMode.Billboard;renderer.sharedMaterial=material;
            renderer.SetActiveVertexStreams(new List<ParticleSystemVertexStream>{ParticleSystemVertexStream.Position,ParticleSystemVertexStream.Color,ParticleSystemVertexStream.UV});
            renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;renderer.maxParticleSize=.6f;
            // Step these burst-only systems with the city clock. This also
            // updates bounds when the renderer has not seen an emitter yet.
            systems.Add(ps);ps.Pause(false);return ps;
        }
        public void Initialize(CityContext context)
        {
            ctx=context;tint=new MaterialPropertyBlock();pool=new GameObject("Efeitos de armas · pool").transform;pool.SetParent(transform,false);
            var flashMat=ParticleMaterial("Clarão quente",true,true);var smokeMat=ParticleMaterial("Fumaça e poeira");
            var sparkMat=ParticleMaterial("Faíscas",true);var pulseMat=ParticleMaterial("Descarga Maré",true);var ringMat=ParticleMaterial("Onda Maré",true,false,true);
            flashes=Emitter("Clarões",flashMat,32,0,1.3f);smoke=Emitter("Fumaça",smokeMat,160,-.035f,2.4f);
            sparks=Emitter("Faíscas de metal",sparkMat,200,.5f,.25f);dust=Emitter("Fragmentos e poeira",smokeMat,200,.22f,2.5f);
            energy=Emitter("Íons Maré",pulseMat,120,-.03f,1.2f);rings=Emitter("Ondas Maré",ringMat,20,0,3.2f);
            blood=Emitter("Blood droplets",ParticleMaterial("Deep red blood",solid:true),256,.68f,.48f);
            var bloodRenderer=blood.GetComponent<ParticleSystemRenderer>();bloodRenderer.renderMode=ParticleSystemRenderMode.Stretch;bloodRenderer.lengthScale=1.25f;bloodRenderer.velocityScale=.025f;
            var sparkRenderer=sparks.GetComponent<ParticleSystemRenderer>();sparkRenderer.renderMode=ParticleSystemRenderMode.Stretch;sparkRenderer.lengthScale=1.7f;sparkRenderer.velocityScale=.04f;
            var lineMat=new Material(Shader.Find("Sprites/Default")){name="Traços breves"};materials.Add(lineMat);
            for(int i=0;i<16;i++)
            {
                var go=new GameObject("Traço "+i);go.transform.SetParent(pool,false);var line=go.AddComponent<LineRenderer>();line.sharedMaterial=lineMat;
                line.positionCount=2;line.useWorldSpace=true;line.shadowCastingMode=ShadowCastingMode.Off;line.receiveShadows=false;line.enabled=false;line.numCapVertices=2;
                traces.Add(new Trace{line=line});
            }
            var brass=new Material(Shader.Find("Standard")){name="Cápsulas de latão",color=new Color(.64f,.40f,.15f)};brass.SetFloat("_Metallic",.8f);brass.SetFloat("_Glossiness",.45f);materials.Add(brass);
            caseMesh=CaseMesh();
            for(int i=0;i<28;i++)
            {
                var go=new GameObject("Cápsula "+i);go.transform.SetParent(pool,false);go.AddComponent<MeshFilter>().sharedMesh=caseMesh;go.AddComponent<MeshRenderer>().sharedMaterial=brass;go.SetActive(false);cases.Add(new Case{root=go.transform});
            }
            var markMaterial=new Material(Resources.Load<Shader>("Shaders/CityGunParticle")){name="Impacto no sólido"};markMaterial.SetFloat("_DstBlend",(int)BlendMode.OneMinusSrcAlpha);materials.Add(markMaterial);
            markMesh=new Mesh{name="Marca facetada"};
            markMesh.vertices=new[]{new Vector3(-.5f,-.5f,0),new Vector3(.5f,-.5f,0),new Vector3(.5f,.5f,0),new Vector3(-.5f,.5f,0)};markMesh.uv=new[]{Vector2.zero,Vector2.right,Vector2.one,Vector2.up};markMesh.colors=new[]{new Color(0,0,0,.85f),new Color(0,0,0,.85f),new Color(0,0,0,.85f),new Color(0,0,0,.85f)};markMesh.triangles=new[]{0,1,2,0,2,3};markMesh.RecalculateBounds();
            for(int i=0;i<48;i++)
            {
                var go=new GameObject("Marca de impacto "+i);go.transform.SetParent(pool,false);go.AddComponent<MeshFilter>().sharedMesh=markMesh;var renderer=go.AddComponent<MeshRenderer>();renderer.sharedMaterial=markMaterial;renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;go.SetActive(false);marks.Add(new Mark{root=go.transform,renderer=renderer});
            }
            // One shared splatter mesh and 32 reusable marks. On saturation the
            // oldest mark is replaced; every reuse receives a fresh city expiry.
            bloodMesh=BloodMesh();
            var bloodMaterial=ParticleMaterial("Blood on surfaces",solid:true);
            for(int i=0;i<32;i++)
            {
                var go=new GameObject("Blood splatter "+i);go.transform.SetParent(pool,false);
                go.AddComponent<MeshFilter>().sharedMesh=bloodMesh;var renderer=go.AddComponent<MeshRenderer>();renderer.sharedMaterial=bloodMaterial;
                renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;go.SetActive(false);
                bloodMarks.Add(new Mark{root=go.transform,renderer=renderer});
            }
            var lamp=new GameObject("Luz breve do cano");lamp.transform.SetParent(pool,false);muzzleLight=lamp.AddComponent<Light>();muzzleLight.type=LightType.Point;muzzleLight.range=3.2f;muzzleLight.shadows=LightShadows.None;muzzleLight.enabled=false;
        }
        Mesh CaseMesh()
        {
            var vertices=new List<Vector3>();var normals=new List<Vector3>();var triangles=new List<int>();
            for(int i=0;i<8;i++)
            {
                float a=i*Mathf.PI/4,b=(i+1)*Mathf.PI/4;int k=vertices.Count;
                var p=new Vector3(Mathf.Cos(a)*.009f,Mathf.Sin(a)*.009f,-.018f);var q=new Vector3(Mathf.Cos(b)*.009f,Mathf.Sin(b)*.009f,-.018f);
                vertices.AddRange(new[]{p,q,new Vector3(q.x,q.y,.018f),new Vector3(p.x,p.y,.018f)});
                var n=(p+q).normalized;n.z=0;n.Normalize();for(int j=0;j<4;j++)normals.Add(n);
                triangles.AddRange(new[]{k,k+1,k+2,k,k+2,k+3});
                k=vertices.Count;vertices.AddRange(new[]{new Vector3(0,0,-.018f),q,p,new Vector3(0,0,.018f),new Vector3(p.x,p.y,.018f),new Vector3(q.x,q.y,.018f)});
                normals.AddRange(new[]{Vector3.back,Vector3.back,Vector3.back,Vector3.forward,Vector3.forward,Vector3.forward});triangles.AddRange(new[]{k,k+1,k+2,k+3,k+4,k+5});
            }
            var mesh=new Mesh{name="Cápsula octogonal"};mesh.SetVertices(vertices);mesh.SetNormals(normals);mesh.SetTriangles(triangles,0);mesh.RecalculateBounds();return mesh;
        }
        static void Emit(ParticleSystem system,Vector3 at,Vector3 velocity,Color color,float size,float life)
        {
            var particle=new ParticleSystem.EmitParams{position=at,velocity=velocity,startColor=color,startSize=size,startLifetime=life,rotation=Random.Range(0,360)};system.Emit(particle,1);
        }
        Mesh BloodMesh()
        {
            var vertices=new List<Vector3>();var uv=new List<Vector2>();var colors=new List<Color>();var indices=new List<int>();
            for(int i=0;i<7;i++)
            {
                float angle=i*2.399963f, radius=i==0?0:.25f+i*.044f, size=i==0?.75f:.09f+i*.009f;
                Vector3 p=new Vector3(Mathf.Sin(angle)*radius,Mathf.Cos(angle)*radius,0);int k=vertices.Count;
                vertices.AddRange(new[]{p+new Vector3(-size,-size,0)*.5f,p+new Vector3(size,-size,0)*.5f,p+new Vector3(size,size,0)*.5f,p+new Vector3(-size,size,0)*.5f});
                uv.AddRange(new[]{Vector2.zero,Vector2.right,Vector2.one,Vector2.up});
                for(int j=0;j<4;j++)colors.Add(new Color(.29f,.006f,.014f,.94f));
                indices.AddRange(new[]{k,k+1,k+2,k,k+2,k+3});
            }
            var mesh=new Mesh{name="Shared blood splatter"};mesh.SetVertices(vertices);mesh.SetUVs(0,uv);mesh.SetColors(colors);mesh.SetTriangles(indices,0);mesh.RecalculateBounds();return mesh;
        }
        public void Bleed(Transform victim,Vector3 point,Vector3 normal,Vector3 direction,float damage)
        {
            if(!ctx||ctx.Paused||!victim||damage<=0)return;
            if(normal.sqrMagnitude<.01f)normal=-direction;
            Vector3 at=point+normal*.035f;
            int count=Mathf.Clamp(Mathf.RoundToInt(damage*.35f),8,18);
            for(int i=0;i<count;i++)
            {
                Vector3 velocity=normal*Random.Range(.45f,1.45f)+direction*.25f+Vector3.up*.25f+Random.insideUnitSphere*.9f;
                Emit(blood,at,velocity,new Color(Random.Range(.40f,.61f),.012f,.025f,.95f),Random.Range(.027f,.060f),Random.Range(.38f,.72f));
            }
            Emit(blood,at,normal*.22f,new Color(.52f,.008f,.016f,.64f),.20f,.16f);BloodBursts++;ctx.Sound("bullet-flesh",point,.72f);
            // Cast only against real surfaces; no decals on a capsule floating
            // above clothing, and no stains projected through floors or walls.
            Vector3 start=point+Vector3.up*.06f;
            if(!Physics.Raycast(start,Vector3.down,out var ground,3,ctx.SolidMask,QueryTriggerInteraction.Ignore)||ground.normal.y<.45f)return;
            var mark=bloodMarks[bloodIndex++%bloodMarks.Count];mark.support=ground.collider.transform;
            Vector3 position=ground.point+ground.normal*.009f;
            mark.localPoint=mark.support.InverseTransformPoint(position);mark.localNormal=mark.support.InverseTransformDirection(ground.normal);
            mark.roll=Random.Range(0,360);mark.until=ctx.Clock+18;
            mark.root.SetPositionAndRotation(position,Quaternion.LookRotation(ground.normal)*Quaternion.Euler(0,0,mark.roll));
            mark.root.localScale=Vector3.one*Random.Range(.25f,.42f);tint.SetFloat("_Opacity",1);mark.renderer.SetPropertyBlock(tint);
            mark.root.gameObject.SetActive(true);BloodMarks++;
        }
        public void Shoot(string weapon,CityWeaponVisual visual,Vector3 from,Vector3 to,RaycastHit contact,bool actor)
        {
            bool pulse=weapon=="pulse";Vector3 direction=(to-from).normalized;Vector3 right=visual.Root.right;
            LastMuzzle=from;MuzzleBursts++;
            Color hot=pulse?new Color(.18f,1,.86f,1):new Color(1,.66f,.19f,1);
            Emit(flashes,from+direction*.025f,Vector3.zero,hot,pulse?.24f:weapon=="carbine"?.31f:.23f,.065f);
            Emit(flashes,from,Vector3.zero,pulse?new Color(.65f,1,1,1):new Color(1,1,.76f,1),.09f,.04f);
            for(int i=0;i<(pulse?5:3);i++)
                Emit(pulse?energy:smoke,from,direction*Random.Range(.25f,.7f)+Vector3.up*.18f+Random.insideUnitSphere*.12f,pulse?new Color(.12f,.9f,.76f,.7f):new Color(.47f,.48f,.45f,.26f),Random.Range(.055f,.10f),pulse?.26f:Random.Range(.25f,.5f));
            if(pulse)Emit(rings,from+direction*.04f,direction*.8f,new Color(.15f,1,.83f,.65f),.2f,.16f);
            else Eject(visual.Ejection.position,right,weapon=="carbine");
            muzzleLight.transform.position=from;muzzleLight.color=hot;muzzleLight.intensity=pulse?1.3f:2.1f;muzzleLight.enabled=true;lightUntil=ctx.Clock+.042f;
            var trace=traces[traceIndex++%traces.Count];trace.from=from;trace.to=to;trace.start=ctx.Clock;trace.until=ctx.Clock+(pulse?.12f:.055f);trace.pulse=pulse;
            trace.line.startWidth=pulse?.024f:.012f;trace.line.endWidth=pulse?.012f:.004f;
            trace.line.startColor=new Color(hot.r,hot.g,hot.b,pulse?.8f:.65f);trace.line.endColor=new Color(hot.r,hot.g,hot.b,.08f);
            // Conventional shots show a short travelling streak, never a long laser.
            trace.line.SetPosition(0,from);trace.line.SetPosition(1,pulse?to:Vector3.Lerp(from,to,.28f));trace.line.enabled=true;
            if(contact.collider)Impact(weapon,contact,direction,actor);
        }
        public static string Surface(Collider collider)
        {
            if(!collider)return "stone";
            if(collider.gameObject.layer==CityContext.VehicleLayer||collider.name.StartsWith("Anteparo"))return "metal";
            string label=(collider.name+" "+(collider.transform.parent?collider.transform.parent.name:"")).ToLowerInvariant();
            var renderer=collider.GetComponentInParent<Renderer>();if(renderer&&renderer.sharedMaterial)label+=" "+renderer.sharedMaterial.name.ToLowerInvariant();
            if(label.Contains("metal")||label.Contains("steel")||label.Contains("ferro")||label.Contains("plate")||label.Contains("carro")||label.Contains("latão"))return "metal";
            if(label.Contains("wood")||label.Contains("madeira")||label.Contains("árvore")||label.Contains("tronco"))return "wood";
            if(label.Contains("glass")||label.Contains("vidro"))return "glass";
            return "stone";
        }
        void Impact(string weapon,RaycastHit hit,Vector3 direction,bool actor)
        {
            ImpactBursts++;LastContact=hit.point;LastSurface=actor?"actor":Surface(hit.collider);
            Vector3 normal=hit.normal.sqrMagnitude>.1f?hit.normal:-direction;Vector3 at=hit.point+normal*.016f;
            if(weapon=="pulse")
            {
                Emit(rings,at,normal*.1f,new Color(.15f,.95f,.85f,.8f),actor?.22f:.32f,.24f);
                for(int i=0;i<9;i++)Emit(energy,at,normal*Random.Range(.2f,.8f)+Random.insideUnitSphere*.85f,new Color(.13f,.84f,1,.8f),.027f,Random.Range(.12f,.32f));
                ctx.Sound("pulse-impact",hit.point,.45f);return;
            }
            if(actor)
            {
                // Damage resolution owns the blood burst so invulnerability,
                // stun-only hits and occluded shots cannot make false wounds.
                return;
            }
            bool metallic=LastSurface=="metal",glass=LastSurface=="glass",wood=LastSurface=="wood";
            for(int i=0;i<(metallic?12:glass?8:3);i++)
            {
                Vector3 velocity=Vector3.Reflect(direction,normal)*Random.Range(.8f,2.5f)+normal*Random.Range(.5f,1.7f)+Random.insideUnitSphere*1.8f;
                Emit(metallic?sparks:dust,at,velocity,metallic?new Color(1,.76f,.3f,1):glass?new Color(.65f,.88f,.9f,.65f):wood?new Color(.57f,.38f,.18f,.8f):new Color(.66f,.62f,.53f,.65f),metallic?.024f:.035f,Random.Range(.12f,.35f));
            }
            for(int i=0;i<(metallic?2:6);i++)Emit(dust,at,normal*.4f+Random.insideUnitSphere*.45f,wood?new Color(.55f,.39f,.24f,.35f):new Color(.58f,.56f,.51f,.32f),Random.Range(.09f,.17f),Random.Range(.25f,.52f));
            ctx.Sound("bullet-"+LastSurface,hit.point,metallic?.55f:.42f);
            var mark=marks[markIndex++%marks.Count];mark.support=hit.collider.transform;
            mark.localPoint=mark.support.InverseTransformPoint(at);mark.localNormal=mark.support.InverseTransformDirection(normal);mark.until=ctx.Clock+10;
            mark.roll=Random.Range(0,360);mark.root.SetPositionAndRotation(at,Quaternion.LookRotation(normal)*Quaternion.Euler(0,0,mark.roll));mark.root.localScale=Vector3.one*Random.Range(.065f,.11f);tint.SetFloat("_Opacity",1);mark.renderer.SetPropertyBlock(tint);mark.root.gameObject.SetActive(true);ImpactMarks++;
        }
        void Eject(Vector3 at,Vector3 right,bool rifle)
        {
            var item=cases[caseIndex++%cases.Count];item.root.gameObject.SetActive(true);item.root.SetPositionAndRotation(at,Random.rotation);
            item.root.localScale=rifle?new Vector3(.8f,.8f,1.45f):Vector3.one;
            item.velocity=right*Random.Range(1.1f,1.9f)+Vector3.up*Random.Range(.9f,1.6f)+ctx.Motor.Velocity*.45f;
            item.spin=Random.insideUnitSphere*700;item.until=ctx.Clock+3.2f;item.bounces=0;EjectedCases++;
        }
        void Update()
        {
            if(!ctx||!ctx.Ready)return;
            if(ctx.Paused)return;
            float clock=ctx.Clock,dt=Time.deltaTime;
            foreach(var ps in systems)if(ps.particleCount>0)ps.Simulate(dt,false,false,false);
            if(clock>=lightUntil)muzzleLight.enabled=false;
            foreach(var trace in traces)
            {
                if(!trace.line.enabled)continue;if(clock>=trace.until){trace.line.enabled=false;continue;}
                float p=Mathf.InverseLerp(trace.start,trace.until,clock);
                if(!trace.pulse){trace.line.SetPosition(0,Vector3.Lerp(trace.from,trace.to,Mathf.Max(0,p-.22f)));trace.line.SetPosition(1,Vector3.Lerp(trace.from,trace.to,Mathf.Min(1,p+.28f)));}
                var c=trace.line.startColor;c.a=(trace.pulse?.8f:.65f)*(1-p);trace.line.startColor=c;c.a*=.2f;trace.line.endColor=c;
            }
            foreach(var item in cases)
            {
                if(!item.root.gameObject.activeSelf)continue;
                if(clock>=item.until){item.root.gameObject.SetActive(false);continue;}
                if(item.bounces>=2)continue;
                Vector3 before=item.root.position;item.velocity+=Physics.gravity*dt;Vector3 move=item.velocity*dt;
                if(Physics.Raycast(before,move.normalized,out var contact,move.magnitude+.009f,ctx.SolidMask,QueryTriggerInteraction.Ignore))
                {
                    item.root.position=contact.point+contact.normal*.011f;item.velocity=Vector3.Reflect(item.velocity,contact.normal)*.3f;item.spin*=.4f;item.bounces++;
                    if(item.bounces==1)ctx.Sound("case-drop",contact.point,.12f);
                }
                else item.root.position=before+move;
                item.root.Rotate(item.spin*dt,Space.World);
            }
            foreach(var mark in marks)
            {
                if(!mark.root.gameObject.activeSelf)continue;
                if(!mark.support||clock>=mark.until){mark.root.gameObject.SetActive(false);continue;}
                mark.root.position=mark.support.TransformPoint(mark.localPoint);mark.root.rotation=Quaternion.LookRotation(mark.support.TransformDirection(mark.localNormal))*Quaternion.Euler(0,0,mark.roll);
                tint.SetFloat("_Opacity",Mathf.Clamp01((mark.until-clock)/2));mark.renderer.SetPropertyBlock(tint);
            }
            foreach(var mark in bloodMarks)
            {
                if(!mark.root.gameObject.activeSelf)continue;
                if(!mark.support||!mark.support.gameObject.activeInHierarchy||clock>=mark.until){mark.root.gameObject.SetActive(false);continue;}
                mark.root.position=mark.support.TransformPoint(mark.localPoint);
                mark.root.rotation=Quaternion.LookRotation(mark.support.TransformDirection(mark.localNormal))*Quaternion.Euler(0,0,mark.roll);
                tint.SetFloat("_Opacity",Mathf.Clamp01((mark.until-clock)/3));mark.renderer.SetPropertyBlock(tint);
            }
        }
        void OnDestroy()
        { if(pool)Destroy(pool.gameObject);foreach(var material in materials)Destroy(material);if(caseMesh)Destroy(caseMesh);if(markMesh)Destroy(markMesh);if(bloodMesh)Destroy(bloodMesh); }
    }
}
