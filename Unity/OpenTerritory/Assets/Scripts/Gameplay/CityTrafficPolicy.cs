using System;
using System.Collections.Generic;
using UnityEngine;

namespace OpenTerritory
{
    // Decisions only. Steering, tires, suspension and contacts remain owned by
    // CityVehicles/PhysX, including while changing lanes or backing up.
    public sealed class CityTrafficPolicy
    {
        public sealed class State
        {
            public float[] Stops=Array.Empty<float>();public readonly HashSet<int> Done=new HashSet<int>();
            public int Pending=-1,StopsPassed,Avoids;public float Previous=-1,Wait,StopGap=float.PositiveInfinity;
            public string Phase="";public float NextScan,IndicateUntil,HoldUntil,Cooldown;
            public Collider Blocker;public float BlockerHalf,Offset;public Vector3 BlockerPosition;
            public bool Reverse,Blocked;public float MaximumWait;public string PassDenied="";
            public float PersonHoldUntil,QueueHoldUntil,BlockedFor,ClearFor;
            public Collider WaitingFor;
            public string ObstacleReason="";
        }
        readonly CityContext ctx;readonly CityVehicles owner;readonly Collider[] overlaps=new Collider[80];
        readonly RaycastHit[] hits=new RaycastHit[80];
        public CityTrafficPolicy(CityContext context,CityVehicles vehicles){ctx=context;owner=vehicles;}
        public void Prepare(CityVehicles.Vehicle car)
        {
            var state=car.Traffic;state.Stops=new float[car.Data.routeStops.Length];
            for(int i=0;i<state.Stops.Length;i++)state.Stops[i]=owner.RouteDistance(car,car.Data.routeStops[i].position);
        }
        public float Patience(CityVehicles.Vehicle car)=>Mathf.Clamp(car.StopPatience,1.15f,2.15f)*2.8f;
        public float ClearanceDelay(CityVehicles.Vehicle car)=>Mathf.Lerp(.75f,1.25f,Mathf.InverseLerp(1.15f,2.15f,car.StopPatience));
        // Scan the complete body along the steering arc, not just a narrow ray
        // from the nose. The initial overlap also catches a cut-in at the side.
        // Safety speed is applied immediately; only acceleration is smoothed.
        public float VehicleLimit(CityVehicles.Vehicle car,float curvature,out float gap)
        {
            float speed=Mathf.Max(0,car.Speed),deceleration=Mathf.Lerp(2.8f,1.9f,ctx.Wetness);
            float scan=Mathf.Clamp(6+speed*1.5f+speed*speed/(2*deceleration),10,48);
            curvature=Mathf.Clamp(curvature,-.16f,.16f);
            if(Mathf.Abs(curvature)>.001f)scan=Mathf.Min(scan,Mathf.PI*.45f/Mathf.Abs(curvature));
            float limit=float.PositiveInfinity,nearest=float.PositiveInfinity;
            car.Traffic.ObstacleReason="";
            int mask=(1<<CityContext.VehicleLayer)|(1<<CityContext.WorldLayer);
            Vector3 size=new Vector3(car.Data.width*.5f+.16f,.65f,car.Data.length*.5f+.16f);
            float homeHeight=owner.RoutePoint(car,car.Progress).y;
            Vector3 Point(float d)
            {
                float x=Mathf.Abs(curvature)<.001f?0:(1-Mathf.Cos(curvature*d))/curvature;
                float z=Mathf.Abs(curvature)<.001f?d:Mathf.Sin(curvature*d)/curvature;
                Vector3 point=car.Position+CityPhysics.Flat(car.Root.right)*x+CityPhysics.Flat(car.Root.forward)*z;
                point.y+=owner.RoutePoint(car,car.Progress+d).y-homeHeight;
                return point+Vector3.up*1.12f+car.Root.forward*car.Data.centerOffset;
            }
            void Consider(Collider collider,float distance,Vector3 direction)
            {
                if(!collider||CityPhysics.Belongs(collider,car.Root))return;
                float lead=collider.attachedRigidbody?Mathf.Max(0,Vector3.Dot(collider.attachedRigidbody.linearVelocity,direction)):0;
                float reaction=deceleration*Mathf.Max(1.05f,car.Headway);
                // A stopped queue leaves a real gap even when waiting uphill.
                float allowed=Mathf.Max(0,Mathf.Sqrt(reaction*reaction+lead*lead+2*deceleration*Mathf.Max(0,distance-2.3f))-reaction);
                if(distance<=2.3f&&lead<.3f)allowed=0;
                if(allowed<limit){limit=allowed;nearest=distance;car.Traffic.ObstacleReason="obstaculo-"+collider.name;}
            }
            var origin=Point(0);Quaternion rotation=Quaternion.LookRotation(CityPhysics.Flat(car.Root.forward));
            int count=Physics.OverlapBoxNonAlloc(origin,size,overlaps,rotation,mask,QueryTriggerInteraction.Ignore);
            if(count==overlaps.Length){gap=0;return 0;}
            for(int i=0;i<count;i++)Consider(overlaps[i],0,car.Root.forward);
            for(float d=0;d<scan;d+=3)
            {
                var from=Point(d);var delta=Point(Mathf.Min(scan,d+3))-from;
                var direction=delta.normalized;
                rotation=Quaternion.LookRotation(CityPhysics.Flat(direction));
                count=Physics.BoxCastNonAlloc(from,size,direction,hits,rotation,delta.magnitude,mask,QueryTriggerInteraction.Ignore);
                if(count==hits.Length){gap=0;return 0;}
                for(int i=0;i<count;i++)Consider(hits[i].collider,d+hits[i].distance,direction);
            }
            gap=nearest;
            if(limit<.1f)car.Traffic.QueueHoldUntil=ctx.Clock+ClearanceDelay(car)*.7f;
            if(ctx.Clock<car.Traffic.QueueHoldUntil){car.Traffic.ObstacleReason="aguardando-transito";return 0;}
            return limit;
        }
        // Test the swept body, including its near overlap and the curved path.
        // A single forward sphere misses people beside the bumper and cannot
        // anticipate someone entering the lane from a pavement.
        public float PeopleGap(CityVehicles.Vehicle car, float curvature, bool reverse, out string who)
        {
            float speed = Mathf.Abs(car.Speed), scan = Mathf.Min(48, Mathf.Max(8, speed * 1.1f + speed * speed / 5.2f));
            float horizon = Mathf.Clamp(.4f + speed / 3.4f, .8f, 2.8f), gap = float.PositiveInfinity;
            who = "";
            foreach (var p in ctx.Residents.People)
            {
                if (!p.Root || !p.Root.gameObject.activeInHierarchy || p.Bus != null) continue;
                float scale = p.Root.lossyScale.y;
                float radius = p.Down ? .65f : p.Controller ? p.Controller.radius * scale : .4f * scale;
                float height = p.Down ? .7f : p.Controller ? p.Controller.height * scale : 2.32f * scale;
                float next = PersonGap(car, p.Root.position, p.Down ? Vector3.zero : p.Velocity, radius, height, curvature, reverse, scan, horizon);
                if (next < gap) { gap = next; who = "pedestre-" + p.Data.id; }
            }
            if (!ctx.Motor.Riding && !ctx.Motor.Swimming)
            {
                float next = PersonGap(car, ctx.Player.position, ctx.Motor.Velocity, .4f, ctx.Combat.Down ? .7f : 2.25f, curvature, reverse, scan, horizon);
                if (next < gap) { gap = next; who = "jogador"; }
            }
            return gap;
        }
        float PersonGap(CityVehicles.Vehicle car, Vector3 feet, Vector3 velocity, float radius, float height,
            float curvature, bool reverse, float scan, float horizon)
        {
            var p = car.Root.InverseTransformPoint(feet);
            float rise = owner.RoutePoint(car, car.Progress + p.z).y - owner.RoutePoint(car, car.Progress).y;
            float top = car.Data.kind == "bus" ? 3.3f : 2.5f;
            if (feet.y > car.Position.y + rise + top || feet.y + height < car.Position.y + rise + .12f) return float.PositiveInfinity;
            var v = car.Root.InverseTransformDirection(CityPhysics.Flat(velocity));
            if (v.sqrMagnitude > 12 * 12) v = Vector3.zero; // A spawn/warp is not walking velocity.
            if (reverse) { p.x = -p.x; p.z = -p.z; v.x = -v.x; v.z = -v.z; curvature = 0; }
            float front = car.Data.length * .5f + (reverse ? -car.Data.centerOffset : car.Data.centerOffset);
            float rear = car.Data.length - front, width = car.Data.width * .5f + radius + .28f;
            float nearest = float.PositiveInfinity;
            curvature = Mathf.Clamp(curvature, -.16f, .16f);
            float reach = Mathf.Min(front + scan + 1, Mathf.Abs(curvature) > .001f ? Mathf.PI * .5f / Mathf.Abs(curvature) : float.PositiveInfinity);
            if (new Vector2(p.x, p.z).magnitude > front + scan + width + new Vector2(v.x, v.z).magnitude * horizon) return nearest;
            for (float time = 0; time <= horizon + .001f; time += horizon / 8)
            {
                var person = new Vector2(p.x + v.x * time, p.z + v.z * time);
                if (person.y < -rear - radius - .3f) continue;
                float along = Mathf.Clamp(person.y, -rear, reach);
                Vector2 closest = new Vector2(0, along);
                if (Mathf.Abs(curvature) >= .001f)
                {
                    along = Mathf.Clamp(Mathf.Atan2(curvature * person.y, 1 - curvature * person.x) / curvature, 0, reach);
                    closest = new Vector2((1 - Mathf.Cos(curvature * along)) / curvature, Mathf.Sin(curvature * along) / curvature);
                    var behind = new Vector2(0, Mathf.Clamp(person.y, -rear, 0));
                    if ((person - behind).sqrMagnitude < (person - closest).sqrMagnitude) { closest = behind; along = behind.y; }
                }
                if ((person - closest).sqrMagnitude < width * width)
                    nearest = Mathf.Min(nearest, Mathf.Max(0, along - front - radius - .75f));
            }
            return nearest;
        }
        float Signed(CityVehicles.Vehicle car,float distance)=>Mathf.Repeat(distance+car.RouteLength*.5f,car.RouteLength)-car.RouteLength*.5f;
        public void Stop(CityVehicles.Vehicle car,float dt,ref float speed)
        {
            var s=car.Traffic;s.StopGap=float.PositiveInfinity;
            if(s.Previous>car.RouteLength*.75f&&car.Progress<car.RouteLength*.25f){s.Done.Clear();s.Pending=-1;s.Wait=0;}
            s.Previous=car.Progress;if(s.Stops.Length==0)return;
            if(s.Pending<0)
            {
                float nearest=30;for(int i=0;i<s.Stops.Length;i++){float ahead=Mathf.Repeat(s.Stops[i]-car.Progress,car.RouteLength);if(!s.Done.Contains(i)&&ahead<nearest){nearest=ahead;s.Pending=i;s.Wait=0;}}
            }
            if(s.Pending<0)return;float gap=Signed(car,s.Stops[s.Pending]-car.Progress);s.StopGap=gap;
            if(gap< -4){s.Done.Add(s.Pending);s.Pending=-1;return;}
            float cruise=speed,v=Mathf.Abs(car.Speed);
            float reaction=1.6f*.65f,approach=Mathf.Max(0,Mathf.Sqrt(reaction*reaction+3.2f*Mathf.Max(0,gap-.45f))-reaction);
            if(gap>.95f)approach=Mathf.Max(.45f,approach);else approach=0;
            speed=Mathf.Min(speed,approach);car.StoppedReason="pare";
            if(gap<=1.05f&&v<.085f)
            {
                speed=0;s.Wait+=dt;s.MaximumWait=Mathf.Max(s.MaximumWait,s.Wait);
                bool clear=PriorityClear(car,car.Data.routeStops[s.Pending].junction);
                float patience=car.StopPatience>0?car.StopPatience:1.25f;
                if(s.Wait>=patience&&!clear)car.StoppedReason="preferencia";
                if(s.Wait>=patience&&clear){s.Done.Add(s.Pending);s.Pending=-1;s.StopsPassed++;s.Wait=0;speed=cruise;car.StoppedReason="";}
            }
        }
        bool PriorityClear(CityVehicles.Vehicle car,Vector3 junction)
        {
            foreach(var other in owner.Fleet)if(other!=car&&CityPhysics.Flat(other.Position-junction).magnitude<5&&CityPhysics.Flat(other.Body.linearVelocity).magnitude>.3f)return false;
            foreach(var person in ctx.Residents.People)if(!person.Down&&person.Bus==null&&CityPhysics.Flat(person.Root.position-junction).magnitude<4)return false;
            return !ctx.Motor.Controller.enabled||CityPhysics.Flat(ctx.Player.position-junction).magnitude>=4;
        }
        bool Queue(CityVehicles.Vehicle vehicle)
        {
            if(!vehicle.Data.auto)return false;
            if(vehicle.TrafficHold||ctx.Clock<vehicle.JamUntil)return true;
            string reason=vehicle.StoppedReason??"";
            // An autonomous car waiting for another road user is a queue, even
            // when its immediate reason is a person rather than a red signal.
            return vehicle.TargetSpeed<.5f||reason=="pare"||reason=="preferencia"||reason=="semaforo"||reason.StartsWith("embarque")||reason.StartsWith("parada");
        }
        Vector3 Direction(CityVehicles.Vehicle car,float at)=>CityPhysics.Flat(owner.RoutePoint(car,at+1)-owner.RoutePoint(car,at-1)).normalized;
        Vector3 Alternate(CityVehicles.Vehicle car,float at)
        {
            Vector3 home=owner.RoutePoint(car,at);
            // The reference map uses 1.45 m offsets on local streets. The
            // donor's 6.6 m lane separation would put a passing car on a pavement.
            float separation=ReferenceMap.Active?2.9f:6.6f;
            return car.Data.oneWay?owner.RoutePoint(car,at,true):home-Vector3.Cross(Vector3.up,Direction(car,at))*separation;
        }
        bool RoadAllows(CityVehicles.Vehicle car,float ahead)
        {
            var s=car.Traffic;if(car.Data.kind!="car"||car.Commanded||s.StopGap<ahead+17)return false;
            if(car.Data.oneWay)
            {
                for(float d=0;d<ahead+14;d+=4)if(CityPhysics.Flat(Alternate(car,car.Progress+d)-owner.RoutePoint(car,car.Progress+d)).magnitude<5.05f)return false;
                return true;
            }
            if(!ReferenceMap.Active&&(car.Data.axis=="curve"||car.Data.axis=="district"))return false;
            if(!ReferenceMap.Active&&car.Data.axis=="x"&&Mathf.Abs(car.Data.road-72)<1&&car.Position.x> -95&&car.Position.x<15)return false;
            Vector3 forward=Direction(car,car.Progress);
            for(float d=-10;d<ahead+16;d+=3)
            {
                if(Vector3.Angle(forward,Direction(car,car.Progress+d))>8)return false;
                if(ReferenceMap.Active)
                {
                    var p=Alternate(car,car.Progress+d);var heading=Direction(car,car.Progress+d);
                    // Both ends of the car must fit on the actual asphalt.
                    float half=car.Data.length*.5f;
                    if(ctx.LifeNavigation.RoadClearance(p+heading*half)<car.Data.width*.5f+.12f||
                       ctx.LifeNavigation.RoadClearance(p-heading*half)<car.Data.width*.5f+.12f)return false;
                }
            }
            foreach(var signal in ctx.Data.signals)
            {
                if(CitySignalRules.Approach(signal,car.Position,forward,0,out var arm,out float lineGap)&&lineGap<ahead+20)return false;
                Vector3 delta=new Vector3(signal.x,car.Position.y,signal.z)-car.Position;float along=Vector3.Dot(delta,forward),across=Mathf.Abs(Vector3.Dot(delta,Vector3.Cross(Vector3.up,forward)));
                if(across<8&&along> -15&&along<ahead+20)return false;
            }
            return true;
        }
        bool ClearLane(CityVehicles.Vehicle car,bool alternate,float required,bool transition)
        {
            Vector3 origin=owner.RoutePoint(car,car.Progress),forward=Direction(car,car.Progress),right=Vector3.Cross(Vector3.up,forward);
            float offset=Vector3.Dot(Alternate(car,car.Progress)-origin,right),lane=alternate?offset:0;
            foreach(var other in owner.Fleet)
            {
                if(other==car||!other.Root.gameObject.activeInHierarchy)continue;Vector3 d=other.Position-origin;float ahead=Vector3.Dot(d,forward),across=Vector3.Dot(d,right);
                if(Mathf.Abs(d.y)>3.5f)continue;
                if(Mathf.Abs(across-lane)>other.Data.width*.5f+car.Data.width*.5f+.25f)continue;
                float speed=Vector3.Dot(other.Body.linearVelocity,forward),front=required+Mathf.Max(0,-speed)*9+other.Data.length*.5f,rear=12+Mathf.Max(0,speed)*4+other.Data.length*.5f;
                if(ahead> -rear&&ahead<front){car.Traffic.PassDenied="veiculo-"+other.Data.id;return false;}
            }
            bool ActorBlocks(Vector3 p)
            {
                Vector3 d=p-origin;float along=Vector3.Dot(d,forward),across=Vector3.Dot(d,right);
                if(Mathf.Abs(d.y)>4)return false;
                return along> -6&&along<required+8&&across>Mathf.Min(0,offset)-2.3f&&across<Mathf.Max(0,offset)+2.3f;
            }
            foreach(var p in ctx.Residents.People)if(p.Root.gameObject.activeInHierarchy&&p.Bus==null&&
                (ActorBlocks(p.Root.position)||ActorBlocks(p.Root.position+p.Velocity*2))){car.Traffic.PassDenied="pedestre-"+p.Data.id;return false;}
            if(!ctx.Motor.Riding&&!ctx.Motor.Swimming&&(ActorBlocks(ctx.Player.position)||ActorBlocks(ctx.Player.position+ctx.Motor.Velocity*2))){car.Traffic.PassDenied="jogador";return false;}
            for(float d=0;d<=required;d+=1.5f)
            {
                float at=car.Progress+d;Vector3 home=owner.RoutePoint(car,at),target=alternate?Alternate(car,at):home;
                if(transition)target=Vector3.Lerp(car.Position+forward*d,target,Mathf.SmoothStep(0,1,d/9));
                float height=ctx.Ground(target.x,target.z);
                if(car.Data.oneWay)
                {
                    // These two surveyed lanes can have different elevations
                    // on a diagonal hill. Compare with their actual surfaces,
                    // not with the height of the neighbouring lane.
                    Vector3 otherLane=Alternate(car,at),span=CityPhysics.Flat(otherLane-home);
                    float blend=Mathf.Clamp01(Vector3.Dot(CityPhysics.Flat(target-home),span)/Mathf.Max(.001f,span.sqrMagnitude));
                    float expected=Mathf.Lerp(home.y,otherLane.y,blend);
                    if(Mathf.Abs(height-expected)>.12f){car.Traffic.PassDenied="fora-do-asfalto "+height+"/"+expected;return false;}
                }
                else if(Mathf.Abs(height-ctx.Ground(home.x,home.z))>.28f){car.Traffic.PassDenied="desnivel";return false;}
                if(ReferenceMap.Active&&ctx.LifeNavigation.RoadClearance(target)<car.Data.width*.5f+.12f){car.Traffic.PassDenied="fora-do-asfalto";return false;}
                target.y=height+1.2f;Quaternion rotation=Quaternion.LookRotation(Direction(car,at));
                int count=Physics.OverlapBoxNonAlloc(target,new Vector3(car.Data.width*.5f+.18f,.9f,car.Data.length*.5f+.25f),overlaps,rotation,ctx.AllMask&~(1<<CityContext.GroundLayer),QueryTriggerInteraction.Ignore);
                if(count==overlaps.Length){car.Traffic.PassDenied="corredor-ocupado";return false;}
                for(int i=0;i<count;i++)if(overlaps[i]&&!CityPhysics.Belongs(overlaps[i],car.Root)){car.Traffic.PassDenied="volume-"+overlaps[i].name;return false;}
            }
            car.Traffic.PassDenied="";
            return true;
        }
        bool RearClear(CityVehicles.Vehicle car)
        {
            Vector3 direction=-Direction(car,car.Progress),origin=car.Position+Vector3.up*1.1f;
            int mask=(ctx.AllMask|(1<<CityContext.PlayerLayer))&~(1<<CityContext.GroundLayer);
            return !CityPhysics.Cast(origin,car.Data.width*.5f+.15f,direction,car.Data.length*.5f+3.3f,out _,car.Root,null,mask);
        }
        bool FindBlocker(CityVehicles.Vehicle car,out Collider blocker,out float ahead,out float half)
        {
            blocker=null;ahead=float.PositiveInfinity;half=0;Vector3 forward=Direction(car,car.Progress),right=Vector3.Cross(Vector3.up,forward);
            foreach(var other in owner.Fleet)
            {
                if(other==car||Mathf.Abs(other.Speed)>.45f||Queue(other))continue;
                Vector3 d=other.Position-car.Position;float along=Vector3.Dot(d,forward),across=Mathf.Abs(Vector3.Dot(d,right));
                if(along>3&&along<22&&along<ahead&&across<(car.Data.width+other.Data.width)*.5f+.25f){blocker=other.Root.GetComponent<Collider>();ahead=along;half=other.Data.length*.5f;}
            }
            int mask=1<<CityContext.WorldLayer;
            if(CityPhysics.Cast(car.Position+Vector3.up*1.15f,.65f,forward,22,out var hit,car.Root,null,mask))
            {
                Vector3 size=hit.collider.bounds.size;float along=Vector3.Dot(hit.collider.bounds.center-car.Position,forward);
                if(size.x*size.z<32&&Mathf.Max(size.x,size.z)<8&&along>3&&along<ahead){blocker=hit.collider;ahead=along;half=(Mathf.Abs(forward.x)*size.x+Mathf.Abs(forward.z)*size.z)*.5f;}
            }
            return blocker;
        }
        public void Pass(CityVehicles.Vehicle car,float dt,ref Vector3 target,ref float speed,ref int signal)
        {
            var s=car.Traffic;s.Reverse=false;
            if(car.Data.kind!="car"||car.Commanded)return;
            Vector3 forward=Direction(car,car.Progress),right=Vector3.Cross(Vector3.up,forward),home=owner.RoutePoint(car,car.Progress);
            if(s.Phase.Length==0)
            {
                if(ctx.Clock<s.Cooldown||ctx.Clock<s.NextScan)return;s.NextScan=ctx.Clock+.35f;
                if(!FindBlocker(car,out var blocker,out float ahead,out float half))
                {s.WaitingFor=null;s.BlockedFor=s.ClearFor=0;return;}
                if(s.WaitingFor!=blocker){s.WaitingFor=blocker;s.BlockedFor=s.ClearFor=0;}
                // Count time spent actually waiting, not the approach to a car.
                if(Mathf.Abs(car.Speed)<.7f)s.BlockedFor+=.35f;else s.BlockedFor=0;
                if(s.BlockedFor<Patience(car)||ctx.Clock<s.PersonHoldUntil)return;
                if(!RoadAllows(car,ahead)||!ClearLane(car,true,ahead+half+12,false)){s.ClearFor=0;return;}
                s.ClearFor+=.35f;if(s.ClearFor<.7f)return;
                s.Blocker=blocker;s.BlockerPosition=blocker.bounds.center;s.BlockerHalf=half;s.Offset=Vector3.Dot(Alternate(car,car.Progress)-home,right);
                s.Phase="indicate";s.Blocked=false;s.IndicateUntil=ctx.Clock+.65f;s.Avoids++;
            }
            if(s.Blocker)s.BlockerPosition=s.Blocker.bounds.center;
            float distance=Vector3.Dot(s.BlockerPosition-car.Position,forward),needed=Mathf.Max(12,distance+s.BlockerHalf+12);
            float laneError=CityPhysics.Flat(car.Position-Alternate(car,car.Progress)).magnitude;
            int direction=s.Offset<0?-1:1;signal=s.Phase=="return"?-direction:direction;
            if(s.Phase=="indicate")
            {
                speed=0;car.StoppedReason="sinalizando";
                if(ctx.Clock>=s.IndicateUntil)
                {
                    if(!ClearLane(car,true,needed,false)){s.Phase="";s.Cooldown=ctx.Clock+1;return;}
                    // Leave enough longitudinal run for the whole body to
                    // clear the blocker before closing the following gap.
                    s.Phase=distance<(ReferenceMap.Active?17.3f:13.8f)?"reverse":"out";
                }
                return;
            }
            if(s.Phase=="reverse")
            {
                car.StoppedReason="manobra";speed=0;
                float run=ReferenceMap.Active?17.5f:14;
                if(distance>=run&&Mathf.Abs(car.Speed)<.1f){s.Phase="out";return;}
                if(distance<run&&RearClear(car)&&ClearLane(car,true,needed,false)){s.Reverse=true;car.StoppedReason="recuo";}
                return;
            }
            if(s.Phase=="out"&&laneError<.55f){s.Phase="pass";s.HoldUntil=ctx.Clock+(car.Data.oneWay?5:0);}
            if(s.Phase=="pass"&&distance< -car.Data.length*.5f-s.BlockerHalf-3&&ctx.Clock>=s.HoldUntil&&ClearLane(car,false,12,true))s.Phase="return";
            bool returning=s.Phase=="return";
            if(returning&&CityPhysics.Flat(car.Position-home).magnitude<.38f&&Vector3.Angle(car.Root.forward,forward)<5){s.Phase="";s.Cooldown=ctx.Clock+4;signal=0;return;}
            if(ctx.Clock>=s.NextScan)
            {
                s.NextScan=ctx.Clock+.25f;
                // Once beside the blocker, it is behind the changing lane.
                s.Blocked=!ClearLane(car,!returning,returning?12:needed,false);
                if(s.Blocked&&!returning&&CityPhysics.Flat(car.Position-home).magnitude<1.2f&&ClearLane(car,false,8,false)){s.Phase="return";returning=true;s.Blocked=false;}
            }
            float look=Mathf.Clamp(6+Mathf.Abs(car.Speed)*.5f,7,10),at=car.Progress+look;
            target=returning?owner.RoutePoint(car,at):Alternate(car,at);
            speed=Mathf.Min(speed,s.Blocked?0:3.2f);car.StoppedReason=s.Blocked?"preferencia":"contorno";
        }
    }
}
