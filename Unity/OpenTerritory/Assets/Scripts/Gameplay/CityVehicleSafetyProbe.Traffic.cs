using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace OpenTerritory
{
    // Part of the existing opt-in native acceptance probe only.
    public sealed partial class CityVehicleSafetyProbe
    {
        CityVehicles.Vehicle lead;
        float BodyGap(CityVehicles.Vehicle a,CityVehicles.Vehicle b)
        {
            var delta=a.Root.InverseTransformPoint(b.Position);
            return delta.z-a.Data.length*.5f-a.Data.centerOffset-b.Data.length*.5f+b.Data.centerOffset;
        }
        void LeadAt(Vector3 position,Quaternion rotation,bool auto=false,float speed=0)
        {
            lead.Root.SetPositionAndRotation(position,rotation);lead.Body.position=position;lead.Body.rotation=rotation;
            lead.Data.auto=auto;lead.Occupied=auto;lead.TrafficHold=false;lead.JamUntil=0;lead.LastImpact=-1000;
            lead.Route=car.Route;lead.Cumulative=car.Cumulative;lead.RouteLength=car.RouteLength;
            lead.Body.linearVelocity=rotation*Vector3.forward*speed;lead.Body.angularVelocity=Vector3.zero;
            lead.Motion=new CityVehicleMath.State{Direction=1,Gear=1,Speed=speed};lead.TargetSpeed=speed;
            lead.CruiseSpeed=3;lead.Acceleration=1.4f;lead.Headway=1.4f;lead.StopPatience=1.5f;
            lead.LaneOffset=lead.LaneAmplitude=lead.LaneBias=0;lead.Traffic=new CityTrafficPolicy.State();
            lead.Progress=ctx.Vehicles.RouteDistance(lead,position);lead.Body.WakeUp();Physics.SyncTransforms();
        }
        IEnumerator TrafficChecks()
        {
            var data=ctx.Data.vehicles.First(v=>v.kind=="car"&&!v.auto&&v.role!="police"&&v.id!=car.Data.id);
            lead=ctx.Vehicles.Fleet.FirstOrDefault(v=>v.Data.id==data.id)??ctx.Vehicles.Spawn(data,new Vector3(20,200.1f,40),Quaternion.identity);
            var savedPosition=lead.Position;var savedRotation=lead.Root.rotation;
            bool wasAuto=lead.Data.auto;
            int priorOwner=0;
            foreach(var p in ctx.Residents.People.Where(p=>p.Data.id==-car.Data.id))
            {
                bool on=p.Controller.enabled;p.Controller.enabled=false;
                p.Root.position=new Vector3(18,200.04f,5+priorOwner++*2);p.LastPosition=p.Root.position;p.Velocity=Vector3.zero;p.Controller.enabled=on;
            }
            yield return PatientPedestrian();
            yield return FollowQueue();
            yield return WideObstacle();
            yield return StreetPass();
            lead.Data.auto=wasAuto;lead.TrafficHold=false;LeadAt(savedPosition,savedRotation,wasAuto);
        }
        IEnumerator PatientPedestrian()
        {
            LeadAt(new Vector3(20,200.1f,70),Quaternion.identity);
            ActorAt(new Vector3(0,200.04f,8));ctx.Motor.Warp(new Vector3(-12,200.04f,8));
            PlaceCar(-15,8,true);car.StopPatience=2.15f;car.Headway=1.7f;
            float end=ctx.Clock+11,minGap=float.PositiveInfinity,stoppedAt=0,creep=0;
            bool stopped=false;
            while(ctx.Clock<end)
            {
                float gap=person.Root.position.z-car.Position.z-car.Data.length*.5f-.4f;
                minGap=Mathf.Min(minGap,gap);
                if(!stopped&&car.Speed<.12f){stopped=true;stoppedAt=car.Position.z;}
                if(stopped)creep=Mathf.Max(creep,Mathf.Abs(car.Position.z-stoppedAt));
                yield return null;
            }
            CheckResult("patient-driver-waits-for-person-beyond-patience",stopped&&minGap>.5f&&creep<.25f&&car.Traffic.Avoids==0,
                "minimum="+minGap+" creep="+creep+" speed="+car.Speed);
            Capture("patient-pedestrian");
            ActorAt(new Vector3(12,200.04f,8));yield return new WaitForSeconds(.25f);
            CheckResult("pedestrian-clearance-delay-prevents-early-restart",Mathf.Abs(car.Speed)<.12f,car.Speed.ToString());
            ActorAt(new Vector3(0,200.04f,8));yield return new WaitForSeconds(.25f);
            ActorAt(new Vector3(12,200.04f,8));yield return new WaitForSeconds(.55f);
            CheckResult("person-reentering-lane-restarts-clearance-delay",Mathf.Abs(car.Speed)<.12f,car.Speed.ToString());
            yield return new WaitForSeconds(1.5f);
            CheckResult("patient-driver-resumes-when-person-stays-clear",car.Speed>.35f,car.Speed.ToString());
        }
        IEnumerator FollowQueue()
        {
            ActorAt(new Vector3(15,200.04f,50));ctx.Motor.Warp(new Vector3(-12,200.04f,50));
            PlaceCar(-25,8,true);car.Headway=1.6f;car.StopPatience=1.15f;
            LeadAt(new Vector3(0,200.1f,0),Quaternion.identity,true,3);
            float end=ctx.Clock+5,minimum=float.PositiveInfinity;
            while(ctx.Clock<end){minimum=Mathf.Min(minimum,BodyGap(car,lead));yield return null;}
            CheckResult("follows-slower-vehicle-with-space",minimum>2&&car.Speed<4.8f&&car.Traffic.Avoids==0,"gap="+minimum+" speed="+car.Speed);
            lead.TrafficHold=true;lead.TrafficHoldReason="semaforo";end=ctx.Clock+10;
            while(ctx.Clock<end){minimum=Mathf.Min(minimum,BodyGap(car,lead));yield return null;}
            CheckResult("queue-brakes-without-rear-ending",minimum>1.5f&&Mathf.Abs(car.Speed)<.15f,"gap="+minimum+" speed="+car.Speed);
            CheckResult("patience-never-bypasses-stopped-traffic-queue",car.Traffic.Avoids==0&&car.Traffic.Phase.Length==0,car.Traffic.Phase);
            Capture("patient-queue");lead.TrafficHold=false;yield return new WaitForSeconds(3);
            CheckResult("queue-restarts-in-order",lead.Speed>.6f&&car.Speed>.35f&&BodyGap(car,lead)>2,"leader="+lead.Speed+" follower="+car.Speed+" gap="+BodyGap(car,lead));
        }
        IEnumerator WideObstacle()
        {
            PlaceCar(-15,8,true);car.Headway=1.4f;
            // Only the edge of the bumper intersects the parked car. The old
            // centre sphere could miss the visible overlap entirely.
            float across=(car.Data.width+lead.Data.width)*.5f-.25f;
            LeadAt(new Vector3(across,200.1f,8),Quaternion.identity);
            float end=ctx.Clock+7,minimum=float.PositiveInfinity;
            while(ctx.Clock<end){minimum=Mathf.Min(minimum,BodyGap(car,lead));yield return null;}
            CheckResult("full-width-sensor-stops-for-offset-car",minimum>1&&Mathf.Abs(car.Speed)<.15f,"gap="+minimum+" speed="+car.Speed);
            LeadAt(new Vector3(20,200.1f,70),Quaternion.identity);yield return new WaitForSeconds(2);
            CheckResult("offset-obstacle-clearance-resumes-traffic",car.Speed>.35f,car.Speed.ToString());
        }
        void StreetPlace(CityVehicles.Vehicle vehicle,float at,bool automatic,float speed=0)
        {
            var point=ctx.Vehicles.RoutePoint(car,at);point.y=ctx.Ground(point.x,point.z)+.10f;
            var forward=CityPhysics.Flat(ctx.Vehicles.RoutePoint(car,at+1)-ctx.Vehicles.RoutePoint(car,at-1)).normalized;
            if(vehicle==lead)LeadAt(point,Quaternion.LookRotation(forward),automatic,speed);
            else
            {
                PlaceCar(0,speed,automatic);car.Root.SetPositionAndRotation(point,Quaternion.LookRotation(forward));
                car.Body.position=point;car.Body.rotation=car.Root.rotation;car.Body.linearVelocity=forward*speed;
                car.Progress=at;car.LastImpact=-1000;car.Headway=1.5f;car.StopPatience=1.15f;
            }
            Physics.SyncTransforms();
        }
        IEnumerator StreetPass()
        {
            var oldRoute=car.Route;var oldCumulative=car.Cumulative;float oldLength=car.RouteLength;
            var oldCamera=ctx.Camera.transform.position;var oldFacing=ctx.Camera.transform.rotation;
            // Pick a genuine local street with room for both bodies, away from
            // junctions and furniture. Keep the authored scene untouched.
            var policy=(CityTrafficPolicy)typeof(CityVehicles).GetField("traffic",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(ctx.Vehicles);
            var roadAllows=typeof(CityTrafficPolicy).GetMethod("RoadAllows",BindingFlags.Instance|BindingFlags.NonPublic);
            float start=-1;
            foreach(var circuit in ctx.LifeNavigation.Circuits)
            {
                car.Route=circuit.points;
                typeof(CityVehicles).GetMethod("PrepareRoute",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(ctx.Vehicles,new object[]{car});
                for(float at=35;at<car.RouteLength-75;at+=15)
                {
                    car.Progress=at;var p=ctx.Vehicles.RoutePoint(car,at);car.Root.position=p;car.Body.position=p;car.Traffic=new CityTrafficPolicy.State();
                    if(!(bool)roadAllows.Invoke(policy,new object[]{car,20f}))continue;
                    bool clear=true;var heading=CityPhysics.Flat(ctx.Vehicles.RoutePoint(car,at+1)-ctx.Vehicles.RoutePoint(car,at-1)).normalized;
                    for(float d=-12;d<58;d+=3)
                    {
                        var home=ctx.Vehicles.RoutePoint(car,at+d);var other=home-Vector3.Cross(Vector3.up,heading)*2.9f;
                        if(Physics.CheckBox(home+Vector3.up*1.15f,new Vector3(1.4f,.7f,2.8f),Quaternion.LookRotation(heading),1<<CityContext.WorldLayer,QueryTriggerInteraction.Ignore)||
                           Physics.CheckBox(other+Vector3.up*1.15f,new Vector3(1.4f,.7f,2.8f),Quaternion.LookRotation(heading),1<<CityContext.WorldLayer,QueryTriggerInteraction.Ignore)){clear=false;break;}
                    }
                    if(clear){start=at;break;}
                }
                if(start>=0)break;
            }
            CheckResult("real-street-has-a-safe-passing-corridor",start>=0,start.ToString());
            if(start>=0)
            {
                var origin=ctx.Vehicles.RoutePoint(car,start);var forward=CityPhysics.Flat(ctx.Vehicles.RoutePoint(car,start+1)-origin).normalized;
                var right=Vector3.Cross(Vector3.up,forward);
                foreach(var v in ctx.Vehicles.Fleet.ToArray())if(v!=car&&v!=lead&&CityPhysics.Flat(v.Position-origin).sqrMagnitude<140*140)ctx.Vehicles.Despawn(v);
                foreach(var p in ctx.Residents.People.ToArray())if(p!=person&&CityPhysics.Flat(p.Root.position-origin).sqrMagnitude<140*140)
                {bool on=p.Controller&&p.Controller.enabled;if(p.Controller)p.Controller.enabled=false;p.Root.position+=Vector3.up*200;if(p.Controller)p.Controller.enabled=on;}
                ctx.Motor.Warp(origin+right*15);ActorAt(origin+right*12);
                ctx.Camera.transform.position=origin+right*18-forward*12+Vector3.up*14;
                ctx.Camera.transform.LookAt(origin+forward*10+Vector3.up);
                StreetPlace(car,start,true,5);StreetPlace(lead,start+20,true);
                lead.TrafficHold=true;lead.TrafficHoldReason="semaforo";
                yield return new WaitForSeconds(10);
                CheckResult("real-street-queue-is-never-overtaken",car.Traffic.Avoids==0&&Mathf.Abs(car.Speed)<.15f,
                    "speed="+car.Speed+" attempts="+car.Traffic.Avoids);
                StreetPlace(car,start,true,5);StreetPlace(lead,start+20,false);
                float begin=ctx.Clock,end=begin+3;
                while(ctx.Clock<end)yield return null;
                CheckResult("waits-before-attempting-to-pass-parked-car",car.Traffic.Avoids==0,"wait="+car.Traffic.BlockedFor);
                var oppositeData=ctx.Data.vehicles.First(v=>v.kind=="car"&&!v.auto&&v.role!="police"&&v.id!=car.Data.id&&v.id!=lead.Data.id);
                var opposite=ctx.Vehicles.Fleet.FirstOrDefault(v=>v.Data.id==oppositeData.id)??ctx.Vehicles.Spawn(oppositeData,oppositeData.position,Quaternion.Euler(0,oppositeData.yaw*Mathf.Rad2Deg,0));
                var oppositeHome=opposite.Position;var oppositeFacing=opposite.Root.rotation;
                var opposingPosition=ctx.Vehicles.RoutePoint(car,start+32)-right*2.9f;opposingPosition.y=ctx.Ground(opposingPosition.x,opposingPosition.z)+.1f;
                opposite.Root.SetPositionAndRotation(opposingPosition,Quaternion.LookRotation(-forward));opposite.Body.position=opposingPosition;opposite.Body.rotation=opposite.Root.rotation;
                opposite.Body.linearVelocity=opposite.Body.angularVelocity=Vector3.zero;opposite.Body.WakeUp();Physics.SyncTransforms();
                yield return new WaitForSeconds(5);
                CheckResult("opposing-vehicle-vetoes-pass-after-patience",car.Traffic.Avoids==0,car.Traffic.PassDenied);
                opposite.Root.SetPositionAndRotation(oppositeHome,oppositeFacing);opposite.Body.position=oppositeHome;opposite.Body.rotation=oppositeFacing;
                opposite.Body.linearVelocity=opposite.Body.angularVelocity=Vector3.zero;Physics.SyncTransforms();
                // Opposing traffic/pedestrians keep vetoing a manoeuvre even
                // after this driver's patience has elapsed.
                ActorAt(ctx.Vehicles.RoutePoint(car,start+22)-right*2.9f);
                yield return new WaitForSeconds(7);
                CheckResult("patient-driver-does-not-pass-into-person",car.Traffic.Avoids==0,car.Traffic.PassDenied);
                person.Down=true;yield return new WaitForSeconds(1);
                CheckResult("fallen-person-also-blocks-passing-corridor",car.Traffic.Avoids==0,car.Traffic.PassDenied);person.Down=false;
                ActorAt(origin+right*12);
                end=ctx.Clock+45;float maxSide=0,minRoad=100,signalAt=-1,departAt=-1;int hits=0;string photographed="";
                void Contact(CityVehicles.Vehicle v,float strength,Collider other){if(v==car&&strength>.3f)hits++;}
                ctx.Vehicles.Impact+=Contact;
                while(ctx.Clock<end)
                {
                    float at=ctx.Vehicles.RouteDistance(car,car.Position),side=CityPhysics.Flat(car.Position-ctx.Vehicles.RoutePoint(car,at)).magnitude;
                    maxSide=Mathf.Max(maxSide,side);minRoad=Mathf.Min(minRoad,ctx.LifeNavigation.RoadClearance(car.Position));
                    if(signalAt<0&&car.Traffic.Phase=="indicate")signalAt=ctx.Clock;
                    if(departAt<0&&side>.5f)departAt=ctx.Clock;
                    if((car.Traffic.Phase=="pass"||car.Traffic.Phase=="return")&&photographed!=car.Traffic.Phase)
                    {
                        photographed=car.Traffic.Phase;ctx.Camera.transform.position=car.Position+right*14-forward*8+Vector3.up*10;
                        ctx.Camera.transform.LookAt(car.Position+forward*5);Capture("street-"+photographed);
                    }
                    if(car.Traffic.Avoids>0&&car.Traffic.Phase.Length==0&&Vector3.Dot(car.Position-lead.Position,forward)>8)break;
                    if(report.samples.Count==0||ctx.Clock-report.samples[report.samples.Count-1].time>.2f)
                        report.samples.Add(new Sample{scenario="real-street-pass",time=ctx.Clock,speed=car.Speed,target=car.TargetSpeed,gap=BodyGap(car,lead),reason=car.StoppedReason+"/"+car.Traffic.Phase+"/"+car.Traffic.PassDenied});
                    yield return null;
                }
                ctx.Vehicles.Impact-=Contact;
                CheckResult("parked-car-is-passed-and-own-lane-recovered",maxSide>2.1f&&car.Traffic.Phase.Length==0&&Vector3.Dot(car.Position-lead.Position,forward)>8,
                    "side="+maxSide+" phase="+car.Traffic.Phase+" denied="+car.Traffic.PassDenied+" progress="+Vector3.Dot(car.Position-lead.Position,forward));
                CheckResult("passing-signals-before-steering",signalAt>=0&&departAt-signalAt>=.6f,"signal lead="+(departAt-signalAt));
                CheckResult("passing-stays-on-real-asphalt",minRoad>=car.Data.width*.5f-.1f,"minimum road clearance="+minRoad);
                CheckResult("passing-has-no-vehicle-contact",hits==0,"contacts="+hits);
                ctx.Camera.transform.position=car.Position+right*12-forward*10+Vector3.up*8;ctx.Camera.transform.LookAt(car.Position+forward*5);Capture("real-street-passing");
            }
            car.Route=oldRoute;car.Cumulative=oldCumulative;car.RouteLength=oldLength;
            ActorAt(new Vector3(15,200.04f,15));ctx.Motor.Warp(new Vector3(-12,200.04f,15));PlaceCar();
            ctx.Camera.transform.SetPositionAndRotation(oldCamera,oldFacing);
        }
    }
}
