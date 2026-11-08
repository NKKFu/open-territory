using UnityEngine;

namespace OpenTerritory
{
    public static class CityPhysics
    {
        static readonly RaycastHit[] Hits=new RaycastHit[96];
        static readonly Collider[] Overlaps=new Collider[96];
        public static bool Belongs(Collider collider,Transform root) => root&&collider&&(collider.transform==root||collider.transform.IsChildOf(root));
        public static bool Cast(Vector3 origin,float radius,Vector3 direction,float distance,out RaycastHit nearest,Transform ignore=null,Transform other=null,int mask=~0)
        {
            nearest=default;float best=distance+1;
            int count=radius>0?Physics.SphereCastNonAlloc(origin,radius,direction,Hits,distance,mask,QueryTriggerInteraction.Ignore):
                Physics.RaycastNonAlloc(origin,direction,Hits,distance,mask,QueryTriggerInteraction.Ignore);
            RaycastHit[] results=Hits;
            if(count==Hits.Length){results=radius>0?Physics.SphereCastAll(origin,radius,direction,distance,mask,QueryTriggerInteraction.Ignore):Physics.RaycastAll(origin,direction,distance,mask,QueryTriggerInteraction.Ignore);count=results.Length;}
            for(int i=0;i<count;i++)
            {
                var hit=results[i];if(!hit.collider||Belongs(hit.collider,ignore)||Belongs(hit.collider,other)||hit.distance>=best)continue;
                nearest=hit;best=hit.distance;
            }
            return nearest.collider;
        }
        public static bool Ground(Vector3 point,out RaycastHit hit,Transform ignore=null,Transform other=null,float reach=3)
        {
            if(Cast(point+Vector3.up*.75f,0,Vector3.down,reach+.75f,out hit,ignore,other))return hit.normal.y>.4f;
            return false;
        }
        public static bool StandClear(Vector3 feet,Transform ignore=null,Transform other=null,float height=2.35f,float radius=.37f)
        {
            Vector3 a=feet+Vector3.up*(radius+.04f),b=feet+Vector3.up*(height-radius);
            int count=Physics.OverlapCapsuleNonAlloc(a,b,radius,Overlaps,~0,QueryTriggerInteraction.Ignore);
            if(count==Overlaps.Length)return false;
            for(int i=0;i<count;i++)if(!Belongs(Overlaps[i],ignore)&&!Belongs(Overlaps[i],other))return false;
            return true;
        }
        public static bool SweepClear(Vector3 feet,Vector3 end,Transform ignore=null,Transform other=null,float height=2.35f)
        {
            Vector3 delta=end-feet;float distance=delta.magnitude;if(distance<.005f)return true;
            Vector3 a=feet+Vector3.up*.41f,b=feet+Vector3.up*(height-.37f);
            int count=Physics.CapsuleCastNonAlloc(a,b,.36f,delta/distance,Hits,distance,~0,QueryTriggerInteraction.Ignore);
            if(count==Hits.Length)return false;
            for(int i=0;i<count;i++)if(Hits[i].collider&&!Belongs(Hits[i].collider,ignore)&&!Belongs(Hits[i].collider,other))return false;
            return true;
        }
        public static Vector3 Flat(Vector3 vector){vector.y=0;return vector;}
        public static float Ease(float value) => value*value*(3-2*value);
    }
}
