using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace OpenTerritory
{
    // Only installed by an explicit native test launch. The normal scene has
    // no teleport, synthetic input, report writer or test objects running.
    [DefaultExecutionOrder(-1200)]
    public sealed class UnityCityGameplayProbe : MonoBehaviour
    {
        [Serializable] public class Result {public string name;public bool passed;public float value;public string detail;}
        [Serializable] public class BusSample {public string stop,reason;public Vector3 position;public float elapsed,gap,speed,target,obstacle;}
        [Serializable] public class Report {public string unity,firstNextStop,playerExitStop,npcExitStop;public float playerExitTime,npcExitTime;public bool passed=true;public int vehicles,boats;public List<Result> checks=new List<Result>();public List<string> errors=new List<string>();public List<BusSample> bus=new List<BusSample>();}
        CityContext ctx;Vector2 move;bool run,brake,jump,interact;Report report=new Report();string output;bool finished;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            var args=Environment.GetCommandLineArgs();if(Array.IndexOf(args,"--city-qa")<0&&Array.IndexOf(args,"--city-gameplay-qa")<0)return;
            new GameObject("Native gameplay verification").AddComponent<UnityCityGameplayProbe>();
        }
        void Awake(){Application.logMessageReceived+=Log;}
        void OnDestroy(){Application.logMessageReceived-=Log;}
        void Log(string message,string trace,LogType type){if(type==LogType.Exception||type==LogType.Error||type==LogType.Assert)report.errors.Add(message+"\n"+trace);}
        void Update()
        {
            if(!ctx||finished)return;var input=ctx.Input;input.TouchMove=move;input.TouchRun=run;input.TouchBrake=brake;
            if(jump){input.TouchJump=true;jump=false;}if(interact){input.TouchInteract=true;interact=false;}
        }
        void Check(string name,bool passed,float value=0,string detail="")
        {report.checks.Add(new Result{name=name,passed=passed,value=value,detail=detail});report.passed&=passed;Debug.Log("GAMEPLAY QA "+name+": "+(passed?"PASS":"FAIL")+" "+value+" "+detail);}
        IEnumerator Start()
        {
            var args=Environment.GetCommandLineArgs();int at=Array.IndexOf(args,"-city-qa-report");output=at>=0&&at+1<args.Length?args[at+1]:Path.Combine(Application.persistentDataPath,"gameplay-qa.json");
            report.unity=Application.unityVersion;float readyUntil=Time.realtimeSinceStartup+90;
            while((!CityContext.Instance||!CityContext.Instance.Ready)&&Time.realtimeSinceStartup<readyUntil)yield return null;
            ctx=CityContext.Instance;
            if(!ctx||!ctx.Ready){Check("native-scene-ready",false);Finish();yield break;}
            ctx.Pause(false);report.vehicles=ctx.Vehicles.Fleet.Count;report.boats=ctx.Marine.Fleet.Count;
            Check("native-fleet",report.vehicles>=30&&report.boats==5,report.vehicles);
            yield return new WaitForSeconds(1.2f);
            ImportedGroundHeights();
            if(Array.IndexOf(args,"--city-qa-traffic")>=0||Array.IndexOf(args,"--city-qa-traffic-one-way")>=0)
            {yield return TrafficRules(Array.IndexOf(args,"--city-qa-traffic-one-way")>=0);Check("no-runtime-errors",report.errors.Count==0,report.errors.Count);Finish();yield break;}
            if(Array.IndexOf(args,"--city-qa-passengers")>=0)
            {yield return BusJourney();Check("no-runtime-errors",report.errors.Count==0,report.errors.Count);Finish();yield break;}
            if(Array.IndexOf(args,"--city-qa-transit")>=0)
            {yield return Seat();yield return BusStops();Check("no-runtime-errors",report.errors.Count==0,report.errors.Count);Finish();yield break;}
            // A real open bay in the imported Acácias car park gives the
            // capsule room to hop and run without a synthetic ground plane.
            Place(new Vector3(-174,0,-96),90);yield return new WaitForSeconds(.65f);
            float floor=ctx.Player.position.y,peak=floor;jump=true;
            for(float t=0;t<1.1f;t+=Time.deltaTime){peak=Mathf.Max(peak,ctx.Player.position.y);yield return null;}
            Check("standing-hop-height",peak-floor>.23f&&peak-floor<.39f,peak-floor,"m above settled feet");
            Place(new Vector3(-175,0,-96),90);yield return new WaitForSeconds(.4f);run=true;move=Vector2.up;yield return new WaitForSeconds(.8f);
            floor=ctx.Player.position.y;peak=floor;jump=true;
            for(float t=0;t<1.0f;t+=Time.deltaTime){peak=Mathf.Max(peak,ctx.Player.position.y);yield return null;}
            move=Vector2.zero;run=false;Check("running-hop-height",peak-floor>.32f&&peak-floor<.53f,peak-floor,"m above takeoff");
            yield return new WaitForSeconds(.45f);
            yield return WalkCamera();yield return Curb();yield return Wall();
            yield return Car();
            if(ctx.Motor.Riding){Check("recover-exit-before-next-case",false);Finish();yield break;}
            yield return Parkour();yield return Bicycle();yield return Seat();yield return BusStops();yield return Marine();
            Check("no-runtime-errors",report.errors.Count==0,report.errors.Count);
            Finish();
        }
        void Place(Vector3 position,float heading)
        {move=Vector2.zero;run=brake=false;position.y=ctx.Ground(position.x,position.z)+.06f;ctx.Motor.Warp(position);ctx.Player.rotation=Quaternion.Euler(0,heading,0);ctx.Follow.Face(heading);ctx.Follow.ResetTracking();}
        void ImportedGroundHeights()
        {
            foreach(var stop in ctx.Data.stops)
            {
                GroundHeight("native-road-height-"+stop.id,stop.roadX,stop.roadZ);
                GroundHeight("native-sidewalk-height-"+stop.id,stop.x,stop.z);
            }
            foreach(float z in new[]{-80f,-70f,60f,80f,90f,120f})GroundHeight("native-road-grade-"+z,-72,z);
        }
        void GroundHeight(string name,float x,float z)
        {
            var g=ctx.Data.ground;int ix=Mathf.Clamp(Mathf.RoundToInt((x-g.x)/g.step),0,g.width-1),iz=Mathf.Clamp(Mathf.RoundToInt((z-g.z)/g.step),0,g.height-1);
            float authored=g.heights[iz*g.width+ix];bool hit=Physics.Raycast(new Vector3(x,35,z),Vector3.down,out var floor,90,1<<CityContext.GroundLayer,QueryTriggerInteraction.Ignore);
            float actual=hit?floor.point.y:-999;
            Check(name,hit&&Mathf.Abs(actual-authored)<.035f,actual-authored,"m; native "+actual+"; authored "+authored);
        }
        IEnumerator WalkCamera()
        {
            Place(new Vector3(-176,0,-93),90);yield return new WaitForSeconds(.5f);Vector3 start=ctx.Player.position;
            move=Vector2.right;yield return new WaitForSeconds(1.8f);Vector3 direction=ctx.Motor.MoveDirection;float sideways=Mathf.Abs(ctx.Player.position.x-start.x);
            Check("held-sidestep-stays-straight",sideways<.45f&&CityPhysics.Flat(ctx.Player.position-start).magnitude>3,sideways,"sideways drift in m");
            Check("walking-camera-follows-new-heading",Vector3.Dot(ctx.Follow.FlatForward,direction)>.92f,Vector3.Dot(ctx.Follow.FlatForward,direction));
            move=Vector2.zero;yield return new WaitForSeconds(.25f);move=new Vector2(1,1);yield return new WaitForSeconds(.25f);direction=ctx.Motor.MoveDirection;
            yield return new WaitForSeconds(1.2f);Check("held-diagonal-does-not-circle",Vector3.Dot(ctx.Motor.MoveDirection,direction)>.995f,Vector3.Angle(direction,ctx.Motor.MoveDirection),"degrees");move=Vector2.zero;
        }
        IEnumerator Curb()
        {
            Vector3 start=Vector3.zero,direction=Vector3.right;float rise=0;bool found=false;
            for(float z=18;z<68&&!found;z+=2)for(float x=-62;x< -10&&!found;x+=2)
            {
                float low=ctx.Ground(x,z),high=ctx.Ground(x+3,z);if(high-low<.12f||high-low>.30f)continue;
                var a=new Vector3(x,low+.04f,z);var b=new Vector3(x+3,high+.04f,z);
                if(!CityPhysics.StandClear(a,ctx.Player)||!CityPhysics.StandClear(b,ctx.Player)||CityPhysics.Cast(a+Vector3.up*1.3f,.15f,Vector3.right,4,out _,ctx.Player))continue;
                start=a;rise=high-low;found=true;
            }
            if(!found){Check("real-curb-found",false);yield break;}
            Place(start,90);yield return new WaitForSeconds(.45f);Vector3 before=ctx.Player.position;move=Vector2.up;yield return new WaitForSeconds(1.25f);move=Vector2.zero;
            float traveled=Vector3.Dot(ctx.Player.position-before,direction);Check("walk-up-existing-curb",traveled>2.8f&&ctx.Player.position.y>before.y+.08f,traveled,"rise "+rise+" m");
        }
        IEnumerator Wall()
        {
            PlaceData home=null;foreach(var p in ctx.Data.places)if(p.kind=="home"){home=p;break;}
            if(home==null){Check("home-facade-present",false);yield break;}
            float front=home.z+home.d*.5f;Place(new Vector3(home.x+4,0,front+3),180);yield return new WaitForSeconds(.4f);
            Vector3 before=ctx.Player.position;move=Vector2.up;yield return new WaitForSeconds(1.6f);move=Vector2.zero;
            Check("capsule-stops-at-physical-facade",ctx.Player.position.z>front+.15f&&ctx.Player.position.z<before.z-1,ctx.Player.position.z-front,"m from facade");
            Check("camera-remains-outside-facade",ctx.Camera.transform.position.z>front-.1f,ctx.Camera.transform.position.z-front);
        }
        IEnumerator Car()
        {
            CityVehicles.Vehicle car=null;foreach(var v in ctx.Vehicles.Fleet)if(!v.Data.auto&&v.Data.kind=="car"&&v.Position.x< -145&&Mathf.Abs(v.Position.z+103)<5){car=v;break;}
            if(car==null){Check("exhibition-car-present",false);yield break;}
            Vector3 entrance=car.Root.TransformPoint(new Vector3(-car.Data.width*.5f-.65f,0,car.Data.seatMount.z));Place(entrance,car.Root.eulerAngles.y);yield return new WaitForSeconds(.5f);
            Check("native-E-selects-car",ctx.CurrentAction!=null&&ctx.CurrentAction.id=="vehicle-"+car.Data.id,0,ctx.CurrentAction?.id??"none");
            interact=true;yield return new WaitForSeconds(1.3f);
            bool boarded=ctx.Motor.Riding==car.Root;Check("native-car-entry",boarded&&car.Driver&&car.Driver.gameObject.activeInHierarchy);
            if(!boarded)yield break;
            var before=car.Position;move=Vector2.up;yield return new WaitForSeconds(1.75f);float speed=car.Speed;
            Check("rigidbody-accelerates",speed>2.5f&&speed<10,speed,"m/s");
            float heading=car.Root.eulerAngles.y;move=new Vector2(1,1);yield return new WaitForSeconds(.55f);move=Vector2.zero;
            Check("steering-turns-body",Mathf.Abs(Mathf.DeltaAngle(heading,car.Root.eulerAngles.y))>1,Mathf.DeltaAngle(heading,car.Root.eulerAngles.y),"degrees");
            Check("driving-camera-follows-heading",Vector3.Dot(ctx.Follow.FlatForward,CityPhysics.Flat(car.Root.forward).normalized)>.90f,Vector3.Dot(ctx.Follow.FlatForward,CityPhysics.Flat(car.Root.forward).normalized));
            brake=true;yield return new WaitForSeconds(1.6f);brake=false;
            Check("native-service-brake",Mathf.Abs(car.Speed)<.25f,car.Speed,"m/s");
            Check("suspension-remains-above-ground",car.Position.y>ctx.Ground(car.Position.x,car.Position.z)-.24f,car.Position.y-ctx.Ground(car.Position.x,car.Position.z));
            interact=true;yield return new WaitForSeconds(1.2f);
            Check("native-car-exit",!ctx.Motor.Riding&&ctx.Motor.Controller.enabled);
            Check("exit-capsule-has-space",CityPhysics.StandClear(ctx.Player.position,ctx.Player),Vector3.Distance(ctx.Player.position,car.Position));
        }
        IEnumerator Parkour()
        {
            CityVehicles.Vehicle car=null;foreach(var v in ctx.Vehicles.Fleet)if(!v.Data.auto&&v.Data.kind=="car"&&v.Position.x< -145&&Mathf.Abs(v.Position.z+103)<5&&v!=ctx.Vehicles.Active){car=v;break;}
            if(car==null){Check("parkour-car-present",false);yield break;}
            Vector3 side=car.Root.right,start=car.Position-side*(car.Data.width*.5f+.69f);float heading=Quaternion.LookRotation(side).eulerAngles.y;
            Place(start,heading);yield return new WaitForSeconds(.5f);float y=ctx.Player.position.y,peak=y;Vector3 before=ctx.Player.position;
            bool entered=false;move=Vector2.up;jump=true;
            for(float time=0;time<1.7f;time+=Time.deltaTime){entered|=ctx.Motor.Mode=="vault"||ctx.Motor.Mode=="climb";peak=Mathf.Max(peak,ctx.Player.position.y);if(time>.12f)move=Vector2.zero;yield return null;}
            Check("native-car-parkour-starts",entered,peak-y,"m above takeoff");
            Check("native-car-parkour-finishes-clear",entered&&!ctx.Motor.Busy&&ctx.Motor.Controller.enabled&&CityPhysics.StandClear(ctx.Player.position,ctx.Player),CityPhysics.Flat(ctx.Player.position-before).magnitude,"m along actual car body");
        }
        IEnumerator Bicycle()
        {
            CityVehicles.Vehicle bike=null;foreach(var v in ctx.Vehicles.Fleet)if(v.Data.kind=="bike"){bike=v;break;}
            if(bike==null){Check("bicycle-present",false);yield break;}
            Place(bike.Root.TransformPoint(new Vector3(-bike.Data.width*.5f-.65f,0,0)),bike.Root.eulerAngles.y);yield return new WaitForSeconds(.4f);interact=true;yield return new WaitForSeconds(.3f);
            bool mounted=ctx.Motor.Riding==bike.Root;Check("bicycle-mount",mounted&&bike.Rider&&bike.Rider.gameObject.activeInHierarchy);if(!mounted)yield break;
            float phase=bike.Pedal;move=Vector2.up;yield return new WaitForSeconds(1.1f);move=Vector2.zero;
            Check("bicycle-pedal-and-propel",Mathf.Abs(bike.Pedal-phase)>.2f&&bike.Speed>.7f,bike.Speed);
            brake=true;yield return new WaitForSeconds(1);brake=false;interact=true;yield return new WaitForSeconds(.4f);Check("bicycle-dismount",!ctx.Motor.Riding);
        }
        IEnumerator Seat()
        {
            SeatData seat=null;foreach(var s in ctx.Data.seats)if(!s.occupied){seat=s;break;}
            if(seat==null){Check("free-bench-present",false);yield break;}
            Vector3 point=new Vector3(seat.x,0,seat.z)+Quaternion.Euler(0,seat.yaw*Mathf.Rad2Deg,0)*new Vector3(0,0,seat.depth*.5f+.9f);
            Place(point,seat.yaw*Mathf.Rad2Deg+180);yield return new WaitForSeconds(.4f);
            var action=ctx.Actions.Find(a=>a.id=="seat-"+seat.id);
            Check("native-E-selects-bench",ctx.CurrentAction==action,0,"selected "+ctx.CurrentAction?.id+"; grounded "+ctx.Motor.Grounded+"; player "+ctx.Player.position+"; prompt "+action?.Position);
            interact=true;yield return new WaitForSeconds(.9f);
            Check("native-sit-on-bench",ctx.Motor.SeatHeight.HasValue&&seat.occupied,ctx.Motor.SeatHeight??-1);
            interact=true;yield return new WaitForSeconds(.9f);Check("native-stand-from-bench",!ctx.Motor.SeatHeight.HasValue&&!seat.occupied&&ctx.Motor.Controller.enabled);
        }
        IEnumerator BusStops()
        {
            CityVehicles.Vehicle bus=null;foreach(var v in ctx.Vehicles.Fleet)if(v.Data.kind=="bus"){bus=v;break;}
            if(bus==null){Check("circular-present",false);yield break;}
            Vector3 roofFrom=bus.Root.TransformPoint(new Vector3(0,4.2f,0));
            bool hasRoof=Physics.Raycast(roofFrom,-bus.Root.up,out var roof,4,1<<CityContext.VehicleLayer,QueryTriggerInteraction.Ignore)&&roof.rigidbody==bus.Body;
            float roofHeight=hasRoof?bus.Root.InverseTransformPoint(roof.point).y:0;
            Check("circular-roof-has-native-collision",hasRoof&&roofHeight>3.16f&&roofHeight<3.25f,roofHeight,"local roof height in m");
            int reached=0;
            foreach(var stop in ctx.Data.stops)
            {
                Vector3 at=new Vector3(stop.roadX,ctx.Ground(stop.roadX,stop.roadZ)+.05f,stop.roadZ);int closest=0;float best=float.MaxValue;
                for(int i=0;i<bus.Route.Length;i++){float d=CityPhysics.Flat(bus.Route[i]-at).sqrMagnitude;if(d<best){best=d;closest=i;}}
                Vector3 forward=CityPhysics.Flat(bus.Route[(closest+2)%bus.Route.Length]-bus.Route[(closest+bus.Route.Length-2)%bus.Route.Length]).normalized;
                Vector3 approach=at-forward*9;approach.y=ctx.Ground(approach.x,approach.z)+.08f;
                bus.Body.position=approach;bus.Body.rotation=Quaternion.LookRotation(forward);bus.Body.linearVelocity=forward*2;bus.Body.angularVelocity=Vector3.zero;bus.AtStop=null;bus.LastStop=bus.NextStop=-1;bus.ExitRequested=false;bus.TargetSpeed=2;
                float began=Time.time,end=began+15,next=began;
                while(bus.AtStop==null&&Time.time<end)
                {
                    if(Time.time>=next){next=Time.time+.25f;report.bus.Add(new BusSample{stop=stop.id,reason=bus.StoppedReason,position=bus.Position,elapsed=Time.time-began,gap=float.IsInfinity(bus.StopGap)?-999:bus.StopGap,speed=bus.Speed,target=bus.TargetSpeed,obstacle=float.IsInfinity(bus.ObstacleGap)?-1:bus.ObstacleGap});}
                    yield return null;
                }
                bool correct=bus.AtStop!=null&&bus.AtStop.id==stop.id;Check("circular-stop-"+stop.id,correct,bus.Speed,"gap "+bus.StopGap+"; target "+bus.TargetSpeed+"; reason "+bus.StoppedReason+"; position "+bus.Position);if(correct)reached++;
            }
            Check("all-four-circular-stops",reached==4,reached);
        }
        IEnumerator BusJourney()
        {
            CityVehicles.Vehicle bus=null;foreach(var v in ctx.Vehicles.Fleet)if(v.Data.kind=="bus"){bus=v;break;}
            CityResidents.Resident passenger=null;foreach(var p in ctx.Residents.People)if(p.Data.initialDestination=="espera-acacias"){passenger=p;break;}
            Check("native-commuter-has-waiting-routine",passenger!=null&&!passenger.Data.stationary,passenger?.Data.id??0,passenger?.Data.name??"No Acacias passenger in exported city");
            if(bus==null||passenger==null)yield break;
            // The actual bus and commuter start at their authored positions.
            // Neither is teleported, directly boarded, or assigned a test stop.
            float until=Time.time+32,next=Time.time;
            while((bus.AtStop==null||bus.AtStop.id!="acacias"||Mathf.Abs(bus.Speed)>.3f)&&Time.time<until)
            {if(Time.time>=next){next=Time.time+.5f;RecordJourney(bus,passenger,"approach");}yield return null;}
            bool stopped=bus.AtStop!=null&&bus.AtStop.id=="acacias";
            Check("circular-arrives-for-real-boarding",stopped,bus.Speed,"stop "+bus.AtStop?.id+"; reason "+bus.StoppedReason);
            if(!stopped)yield break;
            Place(ctx.Vehicles.PassengerEntryPoint(bus)+bus.Root.forward*.8f,bus.Root.eulerAngles.y-90);yield return new WaitForSeconds(.20f);
            Check("native-E-selects-circular",ctx.CurrentAction?.id=="vehicle-"+bus.Data.id,0,ctx.CurrentAction?.id??"none");
            interact=true;yield return new WaitForSeconds(.25f);
            bool aboard=ctx.Motor.Riding==bus.Root&&ctx.Vehicles.Active==bus&&bus.Passenger;
            Check("native-player-boards-circular",aboard&&bus.Rider&&bus.Rider.gameObject.activeInHierarchy);
            if(!aboard)yield break;
            until=Time.time+10;next=Time.time;
            while(passenger.Bus!=bus&&Time.time<until)
            {if(Time.time>=next){next=Time.time+.5f;RecordJourney(bus,passenger,"waiting-to-board");}yield return null;}
            bool npcAboard=passenger.Bus==bus&&ctx.Vehicles.PassengerVehicle(passenger.Root)==bus;
            Check("native-commuter-walks-and-boards",npcAboard,Vector3.Distance(passenger.Root.position,ctx.Vehicles.PassengerEntryPoint(bus)),"state "+passenger.State+"; activity "+passenger.Activity+"; dwell "+bus.Dwell);
            int boardedStop=bus.StopCount;Vector3 initial=bus.Position;
            if(npcAboard)
            {
                until=Time.time+2;while(ctx.Vehicles.PassengerTransition(passenger.Root)<1&&Time.time<until)yield return null;
                float seatError=Vector3.Distance(passenger.Root.position,bus.Root.TransformPoint(ctx.Vehicles.PassengerLocalSeat(passenger.Root)));
                Check("native-commuter-rides-seated",!passenger.Controller.enabled&&ctx.Vehicles.PassengerTransition(passenger.Root)>=1&&seatError<.08f,seatError,"m from physical bus seat");
            }
            until=Time.time+16;while((bus.AtStop!=null||bus.Speed<1)&&Time.time<until)yield return null;
            bool departed=bus.AtStop==null&&bus.Speed>=1;
            Check("circular-departs-with-player",departed&&ctx.Motor.Riding==bus.Root,bus.Speed);
            if(!departed)yield break;
            yield return null;Check("native-E-offers-next-stop",ctx.CurrentAction?.id=="vehicle-exit"&&ctx.CurrentAction.Text.Contains("próxima"),0,ctx.CurrentAction?.Text??"none");
            interact=true;yield return new WaitForSeconds(.15f);Check("native-E-requests-next-stop",bus.ExitRequested&&ctx.Motor.Riding==bus.Root);
            Vector3 previous=bus.Position;float distance=0,maxStep=0;until=Time.time+100;next=Time.time;
            while((bus.StopCount<=boardedStop||ctx.Motor.Riding||npcAboard&&passenger.Bus!=null)&&Time.time<until)
            {
                float step=CityPhysics.Flat(bus.Position-previous).magnitude;previous=bus.Position;distance+=step;maxStep=Mathf.Max(maxStep,step);
                RecordExits(bus,passenger,boardedStop);
                if(Time.time>=next){next=Time.time+.5f;RecordJourney(bus,passenger,"journey");}
                yield return null;
            }
            RecordExits(bus,passenger,boardedStop);
            bool atNext=report.firstNextStop=="praca";
            Check("native-circular-travels-between-real-stops",atNext&&distance>45&&maxStep<1.2f,distance,"m; maximum frame step "+maxStep+"; first next stop "+report.firstNextStop+"; reason "+bus.StoppedReason);
            Check("native-player-leaves-at-requested-stop",report.playerExitStop=="praca"&&!ctx.Motor.Riding&&ctx.Motor.Controller.enabled,CityPhysics.Flat(ctx.Player.position-bus.Position).magnitude,"exit at "+report.playerExitStop+" time "+report.playerExitTime);
            Check("native-player-bus-exit-is-clear",!ctx.Motor.Riding&&CityPhysics.StandClear(ctx.Player.position,ctx.Player));
            Check("native-commuter-finishes-trip",npcAboard&&report.npcExitStop=="praca"&&passenger.Bus==null&&ctx.Vehicles.PassengerVehicle(passenger.Root)==null&&passenger.Controller.enabled,CityPhysics.Flat(passenger.Root.position-initial).magnitude,"m traveled; exit at "+report.npcExitStop+" time "+report.npcExitTime+"; state "+passenger.State);
            Check("native-commuter-bus-exit-is-clear",npcAboard&&passenger.Bus==null&&CityPhysics.StandClear(passenger.Root.position,passenger.Root),CityPhysics.Flat(passenger.Root.position-bus.Position).magnitude);
        }
        void RecordExits(CityVehicles.Vehicle bus,CityResidents.Resident passenger,int boardedStop)
        {
            if(bus.StopCount<=boardedStop)return;
            if(string.IsNullOrEmpty(report.firstNextStop)&&bus.AtStop!=null)report.firstNextStop=bus.AtStop.id;
            if(string.IsNullOrEmpty(report.playerExitStop)&&!ctx.Motor.Riding){report.playerExitStop=bus.AtStop?.id??"moving";report.playerExitTime=ctx.Clock;}
            if(string.IsNullOrEmpty(report.npcExitStop)&&passenger.Bus==null){report.npcExitStop=bus.AtStop?.id??"moving";report.npcExitTime=ctx.Clock;}
        }
        void PutVehicle(CityVehicles.Vehicle car,Vector3 position,Vector3 forward,bool automatic,float speed=0)
        {
            position.y=ctx.Ground(position.x,position.z)+.08f;car.Body.position=position;car.Body.rotation=Quaternion.LookRotation(CityPhysics.Flat(forward));
            car.Body.linearVelocity=forward.normalized*speed;car.Body.angularVelocity=Vector3.zero;car.Body.WakeUp();car.Data.auto=automatic;car.TargetSpeed=speed;car.TrafficHold=false;
            car.Motion=new CityVehicleMath.State{Direction=1,Gear=1,Speed=speed};car.Traffic.Pending=-1;car.Traffic.Previous=-1;car.Traffic.Wait=car.Traffic.MaximumWait=0;car.Traffic.Done.Clear();car.Traffic.Phase="";car.Traffic.Avoids=0;car.Traffic.NextScan=car.Traffic.Cooldown=0;
        }
        void PlaceResident(CityResidents.Resident person,Vector3 point)
        {ctx.Residents.Control(person,true);person.Controller.enabled=false;point.y=ctx.Ground(point.x,point.z)+.04f;person.Root.position=point;person.Controller.enabled=true;}
        IEnumerator TrafficRules(bool onlyOneWay=false)
        {
            CityVehicles.Vehicle district=null,main=null,blocker=null,oncoming=null;
            foreach(var car in ctx.Vehicles.Fleet)
            {
                if(district==null&&car.Data.oneWay&&car.Data.routeStops.Length>0)district=car;
                if(main==null&&car.Data.auto&&car.Data.axis=="z"&&Mathf.Abs(car.Data.road)<1&&car.Data.direction>0)main=car;
                if(!car.Data.auto&&car.Data.kind=="car"&&car.Data.role==""){if(blocker==null)blocker=car;else if(oncoming==null)oncoming=car;}
            }
            CityResidents.Resident walker=null;foreach(var p in ctx.Residents.People)if(!p.Data.stationary&&!p.Data.performance&&p.Bus==null&&!p.Down){walker=p;break;}
            Check("traffic-native-fixtures",district!=null&&main!=null&&blocker!=null&&oncoming!=null&&walker!=null);
            if(district==null||main==null||blocker==null||oncoming==null||walker==null)yield break;
            var walkerHome=walker.Root.position;var blockerHome=blocker.Position;var blockerFacing=blocker.Root.forward;var oncomingHome=oncoming.Position;var oncomingFacing=oncoming.Root.forward;
            Vector3 forward=Vector3.forward;
            if(!onlyOneWay)
            {
            var stop=district.Data.routeStops[0];float stopAt=ctx.Vehicles.RouteDistance(district,stop.position);
            forward=CityPhysics.Flat(ctx.Vehicles.RoutePoint(district,stopAt-6)-ctx.Vehicles.RoutePoint(district,stopAt-10)).normalized;
            PlaceResident(walker,stop.junction+Vector3.Cross(Vector3.up,forward)*1.8f);
            PutVehicle(district,ctx.Vehicles.RoutePoint(district,stopAt-8),forward,true,2);int passed=district.Traffic.StopsPassed;
            float until=Time.time+16,next=Time.time;
            while(district.Traffic.Wait<1.35f&&Time.time<until){if(Time.time>=next){next=Time.time+.4f;RecordTraffic(district,"pare");}yield return null;}
            Check("native-PARE-complete-stop",Mathf.Abs(district.Speed)<.085f&&district.Traffic.Wait>=1.25f,district.Traffic.Wait,"s; gap "+district.Traffic.StopGap+"; speed "+district.Speed);
            yield return new WaitForSeconds(.7f);Check("native-PARE-yields-to-person",district.Traffic.StopsPassed==passed&&district.StoppedReason=="preferencia",district.Speed);
            PlaceResident(walker,walkerHome);ctx.Residents.Control(walker,false);
            until=Time.time+8;while(district.Traffic.StopsPassed==passed&&Time.time<until)yield return null;
            yield return new WaitForSeconds(1);Check("native-PARE-resumes-after-clearance",district.Traffic.StopsPassed>passed&&district.Speed>.5f,district.Speed);
            // A real parked model blocks the centre avenue; another real body
            // approaches in the opposite lane before that fixture is cleared.
            PutVehicle(main,new Vector3(3.3f,0,-56),Vector3.forward,true,2);
            PutVehicle(blocker,new Vector3(3.3f,0,-35),Vector3.forward,false);
            PutVehicle(oncoming,new Vector3(-3.3f,0,-10),Vector3.back,false,5);
            until=Time.time+2.0f;while(Time.time<until){oncoming.Body.linearVelocity=Vector3.back*5;yield return null;}
            Check("native-contour-rejects-oncoming",main.Traffic.Avoids==0,main.Position.x,"x while opposing vehicle approaches");
            PutVehicle(oncoming,oncomingHome,oncomingFacing,false);
            PlaceResident(walker,new Vector3(-3.3f,0,-30));yield return new WaitForSeconds(1.2f);
            Check("native-contour-rejects-person",main.Traffic.Avoids==0,main.Traffic.Avoids);
            PlaceResident(walker,walkerHome);ctx.Residents.Control(walker,false);
            yield return ObservePass(main,blocker,"two-way",45);
            PutVehicle(blocker,blockerHome,blockerFacing,false);
            }
            float start=-1;
            for(float d=14;d<district.RouteLength-60&&start<0;d+=3)
            {
                bool clear=true;for(float f=0;f<=55;f+=4)if(CityPhysics.Flat(ctx.Vehicles.RoutePoint(district,d+f,true)-ctx.Vehicles.RoutePoint(district,d+f)).magnitude<5.05f)clear=false;
                foreach(float s in district.Traffic.Stops)if(Mathf.Repeat(s-d,district.RouteLength)<55)clear=false;
                if(clear)start=d;
            }
            Check("native-two-lane-branch-present",start>=0,start);
            if(start>=0)
            {
                float heightDifference=0,surfaceError=0;
                for(float d=0;d<48;d+=3)
                {
                    Vector3 a=ctx.Vehicles.RoutePoint(district,start+d),b=ctx.Vehicles.RoutePoint(district,start+d,true);
                    heightDifference=Mathf.Max(heightDifference,Mathf.Abs(a.y-b.y));surfaceError=Mathf.Max(surfaceError,Mathf.Abs(ctx.Ground(a.x,a.z)-a.y),Mathf.Abs(ctx.Ground(b.x,b.z)-b.y));
                }
                Check("native-two-lane-grade-matches-asphalt",heightDifference>.28f&&surfaceError<.035f,surfaceError,"m ground error; legitimate lane height difference "+heightDifference);
                forward=CityPhysics.Flat(ctx.Vehicles.RoutePoint(district,start+2)-ctx.Vehicles.RoutePoint(district,start)).normalized;
                PutVehicle(district,ctx.Vehicles.RoutePoint(district,start),forward,true,2);
                Vector3 facing=CityPhysics.Flat(ctx.Vehicles.RoutePoint(district,start+21)-ctx.Vehicles.RoutePoint(district,start+19)).normalized;
                PutVehicle(blocker,ctx.Vehicles.RoutePoint(district,start+20),facing,false);
                yield return ObservePass(district,blocker,"one-way",45);
            }
            PutVehicle(blocker,blockerHome,blockerFacing,false);
        }
        IEnumerator ObservePass(CityVehicles.Vehicle car,CityVehicles.Vehicle blocker,string label,float duration)
        {
            float until=Time.time+duration,next=Time.time,maxSide=0,signalAt=-1,departedAt=-1;int collisions=0;bool reversed=false;
            void Contact(CityVehicles.Vehicle v,float strength,Collider other){if(v==car&&strength>.8f)collisions++;}
            ctx.Vehicles.Impact+=Contact;
            while(Time.time<until)
            {
                Vector3 home=ctx.Vehicles.RoutePoint(car,ctx.Vehicles.RouteDistance(car,car.Position));float side=CityPhysics.Flat(home-car.Position).magnitude;maxSide=Mathf.Max(maxSide,side);
                if(signalAt<0&&car.Traffic.Phase=="indicate"&&car.Signal!=0)signalAt=ctx.Clock;
                if(departedAt<0&&side>.5f)departedAt=ctx.Clock;
                reversed|=car.Traffic.Reverse;
                if(Time.time>=next){next=Time.time+.4f;RecordTraffic(car,label);}
                if(car.Traffic.Avoids>0&&car.Traffic.Phase.Length==0&&side<.45f&&Vector3.Dot(blocker.Position-car.Position,car.Root.forward)<-5)break;
                yield return null;
            }
            ctx.Vehicles.Impact-=Contact;
            Check("native-contour-"+label,car.Traffic.Avoids>0&&maxSide>(label=="one-way"?4.5f:5.8f)&&car.Traffic.Phase.Length==0,maxSide,"m lateral; phase "+car.Traffic.Phase+"; reversed "+reversed+"; reason "+car.StoppedReason);
            Check("native-contour-signals-"+label,signalAt>=0&&departedAt-signalAt>=.60f,departedAt-signalAt,"s before lateral movement");
            Check("native-contour-no-impact-"+label,collisions==0,collisions);
        }
        void RecordTraffic(CityVehicles.Vehicle car,string phase)
        {report.bus.Add(new BusSample{stop=phase,position=car.Position,elapsed=ctx.Clock,speed=car.Speed,target=car.TargetSpeed,gap=float.IsInfinity(car.Traffic.StopGap)?-999:car.Traffic.StopGap,obstacle=float.IsInfinity(car.ObstacleGap)?-1:car.ObstacleGap,reason=car.StoppedReason+"; "+car.Traffic.Phase+"; wait "+car.Traffic.Wait+"; signal "+car.Signal+"; denied "+car.Traffic.PassDenied});}
        void RecordJourney(CityVehicles.Vehicle bus,CityResidents.Resident passenger,string phase)
        {
            report.bus.Add(new BusSample{stop=phase+"/"+bus.AtStop?.id,reason=bus.StoppedReason+"; NPC "+passenger.State+"/"+passenger.Activity+" "+passenger.Root.position+"; dwell "+bus.Dwell,
                position=bus.Position,elapsed=Time.time,gap=float.IsInfinity(bus.StopGap)?-999:bus.StopGap,speed=bus.Speed,target=bus.TargetSpeed,obstacle=float.IsInfinity(bus.ObstacleGap)?-1:bus.ObstacleGap});
        }
        IEnumerator Marine()
        {
            if(ctx.Marine.Fleet.Count==0)yield break;var boat=ctx.Marine.Fleet[0];
            Place(new Vector3(2.12f,0,-173.3f),180);yield return new WaitForSeconds(.4f);interact=true;yield return new WaitForSeconds(.35f);
            bool aboard=ctx.Marine.ActiveBoat==boat;Check("native-boat-board",aboard);
            if(aboard)
            {
                Vector3 before=boat.Root.position;move=Vector2.up;yield return new WaitForSeconds(1.4f);move=Vector2.zero;
                Check("buoyant-boat-propels",CityPhysics.Flat(boat.Root.position-before).magnitude>.6f,boat.Speed);
                interact=true;float end=Time.time+5;while(ctx.Marine.ActiveBoat!=null&&Time.time<end)yield return null;Check("boat-safe-disembark",ctx.Marine.ActiveBoat==null);
            }
            if(ctx.Motor.Riding)yield break;
            ctx.Motor.Warp(new Vector3(-9,ctx.Marine.Surface(-9,-175)-.2f,-175));yield return new WaitForSeconds(.8f);Check("shore-enters-swimming",ctx.Motor.Swimming);
            Vector3 swim=ctx.Player.position;move=Vector2.up;yield return new WaitForSeconds(.8f);move=Vector2.zero;Check("native-swimming-movement",CityPhysics.Flat(ctx.Player.position-swim).magnitude>.5f,CityPhysics.Flat(ctx.Player.position-swim).magnitude);
            if(ctx.Data.ladders.Length>0)
            {
                var ladder=ctx.Data.ladders[0];ctx.Motor.Warp(new Vector3(ladder.x,ctx.Marine.Surface(ladder.x,ladder.z)-1.5f,ladder.z));yield return new WaitForSeconds(.4f);interact=true;yield return new WaitForSeconds(1.2f);
                Check("native-ladder-returns-to-land",!ctx.Motor.Swimming&&ctx.Motor.Controller.enabled&&Vector2.Distance(new Vector2(ctx.Player.position.x,ctx.Player.position.z),new Vector2(ladder.landX,ladder.landZ))<.5f);
            }
        }
        void Finish()
        {
            finished=true;if(ctx){ctx.Input.TouchMove=Vector2.zero;ctx.Input.TouchRun=ctx.Input.TouchBrake=false;}
            report.passed&=report.errors.Count==0;var folder=Path.GetDirectoryName(output);if(!string.IsNullOrEmpty(folder))Directory.CreateDirectory(folder);File.WriteAllText(output,JsonUtility.ToJson(report,true));
            Debug.Log("Native gameplay report: "+output);Application.Quit(report.passed?0:2);
        }
    }
}
