using System;
using System.Collections;
using System.Linq;
using UnityEngine;

namespace OpenTerritory
{
    // Executed only by the explicitly enabled reference-map QA suite.
    public static class CityNatureAudit
    {
        public static IEnumerator Run(CityContext ctx, Action<string,bool,string> check)
        {
            var trees=ctx.Data.natureTrees;var rocks=ctx.Data.rocks;
            check("nature-mixed-tree-ages",trees.Length>1000&&trees.Max(t=>t.scale.y)>1.6f&&trees.Min(t=>t.scale.y)<.55f,
                trees.Length+" trees, scale "+trees.Min(t=>t.scale.y)+" to "+trees.Max(t=>t.scale.y));
            if(ctx.Data.natureGroves.Length>0){check("nature-groves-and-clearings",trees.Where(t=>t.grove>=0).Select(t=>t.grove).Distinct().Count()>=20&&trees.Count(t=>t.grove>=0)>trees.Length*.6f&&trees.Count(t=>t.grove<0)>200,
                "groves="+trees.Where(t=>t.grove>=0).Select(t=>t.grove).Distinct().Count());
            check("nature-groves-are-denser",ctx.Data.natureGroves.Length>=20&&ctx.Data.natureGroves.Sum(g=>g.count)>ctx.Data.natureGroves.Sum(g=>g.originalCount)*3,
                "woodland trees="+trees.Count(t=>t.grove>=0));
            var cover=UnityEngine.Object.FindFirstObjectByType<CityGroundCoverInfo>();
            check("nature-woodland-understorey",cover&&cover.groveShrubs>5000&&cover.groveShrubSamples.Length>100,
                cover?"grove shrubs="+cover.groveShrubs:"missing");
            bool glades=cover&&cover.groveShrubSamples.All(p=>ctx.Data.natureGroves.All(g=>new Vector2(p.x-g.clearingX,p.z-g.clearingZ).sqrMagnitude>g.clearingRadius*g.clearingRadius));
            check("nature-understorey-keeps-glades-open",glades,"all sampled understorey positions");}
            else
            {
                var cover=UnityEngine.Object.FindFirstObjectByType<CityGroundCoverInfo>();
                check("nature-no-groves",trees.Length>=6000&&trees.All(t=>t.grove<0),"all trees use dispersed placement");
                check("nature-spread-shrubs",cover&&cover.shrubs>25000&&cover.scatteredShrubs>1000&&cover.groveShrubs==0,
                    cover?"shrubs="+cover.shrubs+" scattered supplements="+cover.scatteredShrubs:"missing");
                int regions=trees.Select(t=>new Vector2Int(Mathf.FloorToInt(t.position.x/120),Mathf.FloorToInt(t.position.z/120))).Distinct().Count();
                check("nature-map-wide-distribution",regions>80,"occupied 120m areas="+regions);
            }
            int trailSamples=0,trailBlocked=0,existingObstacles=0;
            var natureSolids=new System.Collections.Generic.HashSet<int>(trees.Select(t=>t.solidIndex));
            foreach(var trail in ReferenceMap.Layout.roads.Where(r=>r.kind=="trail"))
                for(int i=2;i<trail.points.Length-2;i+=4)
                {
                    var p=trail.points[i];var direction=trail.points[i+1]-trail.points[i-1];direction.y=0;direction.Normalize();
                    var side=Vector3.Cross(Vector3.up,direction);
                    foreach(float offset in new[]{-.3f,0,.3f})
                    {
                        var sample=p+side*(trail.width*offset);trailSamples++;
                        if(!Physics.Raycast(sample+Vector3.up*15,Vector3.down,out var hit,40,1<<CityContext.GroundLayer)){trailBlocked++;continue;}
                        foreach(var collider in Physics.OverlapCapsule(hit.point+Vector3.up*.5f,hit.point+Vector3.up*1.9f,.34f,1<<CityContext.WorldLayer))
                        {
                            if(collider.name.StartsWith("Colisão-")&&int.TryParse(collider.name.Substring(8),out int id)&&natureSolids.Contains(id))trailBlocked++;
                            else if(collider.name.StartsWith("Rocha •"))trailBlocked++;
                            else existingObstacles++;
                        }
                    }
                }
            check("nature-vegetation-keeps-trails-clear",trailSamples>0&&trailBlocked==0,"samples="+trailSamples+" nature obstacles="+trailBlocked+" existing structural obstacles="+existingObstacles);
            var root=GameObject.Find("Pedras e rochas • afloramentos naturais");
            var groups=root?root.GetComponentsInChildren<LODGroup>():new LODGroup[0];
            check("nature-rocks-baked-with-lod",rocks.Length>=250&&groups.Length==rocks.Length&&groups.All(g=>g.GetLODs().Length==3),"rocks="+groups.Length);
            var meshes=groups.SelectMany(g=>g.GetLODs()).SelectMany(l=>l.renderers).Select(r=>r.GetComponent<MeshFilter>().sharedMesh).Distinct().ToArray();
            check("nature-rocks-share-36-meshes",meshes.Length==36,"meshes="+meshes.Length);
            bool grounded=true,solid=true,reduced=true;
            foreach(var data in rocks)
            {
                grounded&=Physics.Raycast(data.position+Vector3.up*12,Vector3.down,out var ground,40,1<<CityContext.GroundLayer)&&Mathf.Abs(ground.point.y-data.position.y)<.35f;
                var group=groups.FirstOrDefault(g=>g.name=="Rocha • "+data.solidIndex);
                if(!group){solid=false;continue;}
                int previous=int.MaxValue;
                foreach(var level in group.GetLODs())
                {int count=level.renderers[0].GetComponent<MeshFilter>().sharedMesh.triangles.Length;reduced&=count<previous;previous=count;}
                if(data.scale.y*.75f>.35f||Mathf.Max(data.scale.x,data.scale.z)>1.1f)
                {
                    var collider=group.GetComponent<MeshCollider>();
                    solid&=collider&&collider.enabled&&collider.convex&&collider.gameObject.layer==CityContext.WorldLayer;
                    solid&=Physics.Raycast(data.position+Vector3.up*(data.scale.y+3),Vector3.down,out var hit,data.scale.y+5,1<<CityContext.WorldLayer)&&hit.collider&&hit.collider.name.StartsWith("Rocha •");
                }
            }
            check("nature-rocks-follow-terrain",grounded,"all rock bases checked against physical ground");
            check("nature-boulders-have-real-collision",solid,"independent of visible LOD");
            check("nature-rock-lods-reduce-triangles",reduced,"54 -> 36 -> 24");
            var largest=rocks.OrderByDescending(r=>r.scale.y).First();
            Vector3 saved=ctx.Player.position;var start=largest.position+Vector3.left*(largest.scale.x+3);
            start.y=ctx.Ground(start.x,start.z)+.06f;ctx.Motor.Warp(start);yield return new WaitForSeconds(.25f);
            float travel=largest.scale.x+7;
            var target=GameObject.Find("Rocha • "+largest.solidIndex).GetComponent<MeshCollider>();
            bool contact=false,penetrated=false;
            for(int i=0;i<Mathf.CeilToInt(travel/.12f);i++)
            {
                var flags=ctx.Motor.Controller.Move(new Vector3(.12f,-.08f,0));
                Vector3 body=ctx.Player.position+Vector3.up*1.12f;
                float separation=Vector3.Distance(body,target.ClosestPoint(body));
                contact|=(flags&CollisionFlags.Sides)!=0&&separation<.7f;
                penetrated|=separation<.25f;
            }
            // The controller may slide around a convex rock; passing its center's X is legal.
            // Verify actual contact and lack of penetration rather than an arbitrary end plane.
            check("nature-character-respects-boulder-surface",contact&&!penetrated,"contact="+contact+" penetrated="+penetrated);
            ctx.Motor.Warp(saved);yield return null;
        }
    }
}
