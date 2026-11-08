using System;
using System.Collections.Generic;
using UnityEngine;

namespace OpenTerritory
{
    [DefaultExecutionOrder(-140)]
    public class CityMarine : MonoBehaviour
    {
        public sealed class Boat
        {
            public BoatData Data;public Transform Root,Wheel;public Rigidbody Body;public Quaternion WheelRotation;public float Speed,Turn;public bool ExitRequested,Moored=true;
        }
        CityContext ctx;readonly List<Boat> fleet=new List<Boat>();Boat active;string mode="land";
        Vector3 swimVelocity,climbStart;LadderData climbing;float climbTime,effort,stroke,cooldown,lastSplash;
        public const float SeaLevel=-.28f,Shore=-165.2f;
        public IReadOnlyList<Boat> Fleet=>fleet;
        public Boat ActiveBoat=>active;
        public string Mode=>mode;
        public float Speed=>active!=null?active.Speed:swimVelocity.magnitude;
        public bool Swimming=>mode=="swim";
        public void Initialize(CityContext context)
        {
            ctx=context;
            foreach(var data in ctx.Data.boats)
            {
                var root=ctx.Node(data.id);if(!root)continue;var boat=new Boat{Data=data,Root=root,Wheel=ctx.Node(data.wheel)};
                boat.WheelRotation=boat.Wheel?boat.Wheel.localRotation:Quaternion.identity;
                if(data.width<.1f)data.width=2.5f;if(data.length<.1f)data.length=6;if(data.top<1)data.top=12;if(data.draft<.1f)data.draft=.7f;if(data.turn<.1f)data.turn=1;
                boat.Body=root.GetComponent<Rigidbody>();if(!boat.Body)boat.Body=root.gameObject.AddComponent<Rigidbody>();
                boat.Body.mass=data.model=="sail"?2400:1500;boat.Body.centerOfMass=new Vector3(0,-.3f,0);
                boat.Body.linearDamping=.15f;boat.Body.angularDamping=1.1f;boat.Body.interpolation=RigidbodyInterpolation.Interpolate;boat.Body.collisionDetectionMode=CollisionDetectionMode.ContinuousDynamic;
                SetLayer(root,CityContext.VehicleLayer);
                if(root.GetComponentsInChildren<Collider>().Length==0){var hull=root.gameObject.AddComponent<BoxCollider>();hull.center=new Vector3(0,.12f,0);hull.size=new Vector3(data.width,.85f,data.length*.88f);}
                fleet.Add(boat);
                ctx.AddAction("boat-"+data.id,()=>Approach(boat),()=>"Pilotar",()=>Board(boat),()=>CanBoard(boat),2.6f,10,target:()=>root);
            }
            foreach(var ladder in ctx.Data.ladders)
            {
                var item=ladder;ctx.AddAction("ladder-"+item.x+"-"+item.z,()=>new Vector3(item.x,Surface(item.x,item.z),item.z),()=>"Subir",()=>Climb(item),
                    ()=>mode=="swim"&&climbing==null&&!ctx.Motor.Busy,2.3f,20);
            }
            ctx.AddAction("boat-exit",()=>ctx.Player.position+Vector3.up,()=>Mathf.Abs(Speed)>.7f?"Parar e desembarcar":"Desembarcar",()=>{if(active!=null){active.ExitRequested=true;if(Mathf.Abs(active.Speed)<.7f)LeaveBoat();}},()=>active!=null&&!ctx.Motor.Remote,3,80,requiresLook:false);
        }
        static void SetLayer(Transform t,int layer){t.gameObject.layer=layer;foreach(Transform c in t)SetLayer(c,layer);}
        public bool InSea(Vector3 p)=>ReferenceMap.Active?ReferenceMap.Water(p):p.z<Shore&&p.z> -248&&p.x> -209&&p.x<209;
        public float Seabed(float x,float z){if(ReferenceMap.Active)return ReferenceMap.Height(x,z);float offshore=Mathf.Max(0,Shore-z);return -.19f-Mathf.Min(10,offshore*(.30f+.028f*Mathf.Sin(x*.06f))+Mathf.Max(0,offshore-8)*.12f);}
        public float Depth(float x,float z)=>Mathf.Max(0,SeaLevel-Seabed(x,z));
        public float Surface(float x,float z)
        {
            float sea=ctx&&ctx.Systems?ctx.Systems.Weather.SeaState:0,t=ctx?ctx.Clock:0;
            return SeaLevel+CityWater.Wave(x,z,t,sea,Depth(x,z));
        }
        public DockData DockAt(Vector3 point,float margin=0)
        {
            foreach(var dock in ctx.Data.docks)
            {Vector3 p=Quaternion.Euler(0,-dock.yaw*Mathf.Rad2Deg,0)*(point-new Vector3(dock.x,0,dock.z));if(Mathf.Abs(p.x)<=dock.w*.5f+margin&&Mathf.Abs(p.z)<=dock.d*.5f+margin)return dock;}
            return null;
        }
        Vector3 Approach(Boat boat)
        {
            Vector3 p=boat.Root.InverseTransformPoint(ctx.Player.position);p.x=Mathf.Clamp(p.x,-boat.Data.width*.5f,boat.Data.width*.5f);p.z=Mathf.Clamp(p.z,-boat.Data.length*.5f,boat.Data.length*.5f);p.y=.65f;return boat.Root.TransformPoint(p);
        }
        bool CanBoard(Boat boat)
        {
            if(active!=null||ctx.Motor.Riding||ctx.Motor.Busy||ctx.Motor.SeatHeight.HasValue||Mathf.Abs(boat.Speed)>1.2f||ctx.Clock<cooldown)return false;
            if(mode!="swim"&&DockAt(ctx.Player.position,.10f)==null)return false;
            Vector3 from=ctx.Player.position+Vector3.up*1.4f,target=Approach(boat),d=target-from;
            return !CityPhysics.Cast(from,.08f,d.normalized,d.magnitude,out _,ctx.Player,boat.Root);
        }
        void Board(Boat boat)
        {
            if(!CanBoard(boat))return;active=boat;boat.Moored=false;boat.ExitRequested=false;mode="boat";swimVelocity=Vector3.zero;climbing=null;
            ctx.Motor.SetMarineState(false,"pilot",0,0,stroke);ctx.Motor.SetRide(boat.Root,true);ctx.Sound("mooring",boat.Root.position);
        }
        public void Fall()
        {
            if(active!=null){active.Moored=true;active.ExitRequested=false;active=null;ctx.Motor.SetRide(null);}
            climbing=null;mode=InSea(ctx.Player.position)?"swim":"land";ctx.Motor.SetMarineState(mode=="swim",mode,0,0,stroke);
        }
        bool LeaveBoat()
        {
            if(active==null)return false;var boat=active;Vector3 landing=Vector3.zero;bool found=false,water=false;
            foreach(var dock in ctx.Data.docks)
            {
                Quaternion rotation=Quaternion.Euler(0,dock.yaw*Mathf.Rad2Deg,0);Vector3 center=new Vector3(dock.x,dock.top,dock.z),local=Quaternion.Inverse(rotation)*(boat.Root.position-center);
                foreach(float shift in new[]{0f,1.2f,-1.2f})
                {
                    var p=center+rotation*new Vector3(Mathf.Clamp(local.x,-dock.w*.5f+.62f,dock.w*.5f-.62f),.045f,Mathf.Clamp(local.z+shift,-dock.d*.5f+.62f,dock.d*.5f-.62f));
                    if(HullDistance(boat,p)>1.8f||!CityPhysics.StandClear(p,ctx.Player,boat.Root))continue;
                    landing=p;found=true;break;
                }
                if(found)break;
            }
            if(!found)foreach(int side in new[]{1,-1})foreach(float along in new[]{-.65f,.8f,-boat.Data.length*.4f})
            {
                Vector3 p=boat.Root.TransformPoint(new Vector3(side*(boat.Data.width*.5f+.8f),0,along));
                if(!InSea(p)||Depth(p.x,p.z)<.75f||DockAt(p,.35f)!=null||!WaterClear(p,boat.Root))continue;
                p.y=Surface(p.x,p.z)-1.55f;landing=p;found=water=true;break;
            }
            if(!found){boat.ExitRequested=false;ctx.Say("Saída bloqueada.",ctx.Player.position+Vector3.up*2);return false;}
            boat.Moored=true;boat.ExitRequested=false;boat.Body.linearVelocity=Vector3.zero;boat.Body.angularVelocity=Vector3.zero;active=null;
            mode=water?"swim":"land";ctx.Motor.SetRide(null);ctx.Motor.SetMarineState(water,water?"swim":"idle",0,0,stroke);ctx.Motor.Warp(landing);
            swimVelocity=Vector3.zero;cooldown=ctx.Clock+.35f;ctx.Sound(water?"splash":"mooring",landing);return true;
        }
        float HullDistance(Boat boat,Vector3 point)
        {var p=boat.Root.InverseTransformPoint(point);return new Vector2(Mathf.Max(0,Mathf.Abs(p.x)-boat.Data.width*.5f),Mathf.Max(0,Mathf.Abs(p.z)-boat.Data.length*.5f)).magnitude;}
        bool WaterClear(Vector3 point,Transform ignore=null)
        {
            point.y=Surface(point.x,point.z)-.2f;
            foreach(var collider in Physics.OverlapSphere(point,.34f,ctx.SolidMask,QueryTriggerInteraction.Ignore))
                if(!CityPhysics.Belongs(collider,ignore)&&!CityPhysics.Belongs(collider,ctx.Player))return false;
            return true;
        }
        void Climb(LadderData ladder)
        {
            Vector3 landing=new Vector3(ladder.landX,ladder.top+.04f,ladder.landZ);
            if(!CityPhysics.StandClear(landing,ctx.Player)){ctx.Say("Saída ocupada.",landing+Vector3.up);return;}
            climbing=ladder;climbStart=ctx.Player.position;climbTime=0;swimVelocity=Vector3.zero;ctx.Motor.SetInputLock(true);
        }
        void Update()
        {
            if(!ctx||!ctx.Ready||ctx.Paused)return;float dt=Mathf.Min(.05f,Time.deltaTime);
            if(ctx.Combat&&ctx.Combat.Down)return;
            if(active!=null)
            {
                if(active.ExitRequested&&Mathf.Abs(active.Speed)<.65f)LeaveBoat();
                if(active!=null)PilotPose();return;
            }
            if(ctx.Motor.Riding||ctx.Motor.Remote||ctx.Motor.SeatHeight.HasValue)return;
            Vector3 p=ctx.Player.position;
            if(mode=="land")
            {
                if(ctx.Clock<cooldown||!InSea(p)||DockAt(p)!=null||Depth(p.x,p.z)<1||p.y>Surface(p.x,p.z)+.28f)return;
                mode="swim";swimVelocity=Vector3.zero;effort=0;ctx.Motor.SetMarineState(true,"swim",0,0,stroke);ctx.Sound("splash",p);
            }
            if(climbing!=null)
            {
                climbTime=Mathf.Min(1,climbTime+dt/.95f);var ladder=climbing;
                Vector3 lifted=new Vector3(climbStart.x,ladder.top+.10f,climbStart.z),landing=new Vector3(ladder.landX,ladder.top+.045f,ladder.landZ);
                Vector3 next=climbTime<.56f?Vector3.Lerp(climbStart,lifted,CityPhysics.Ease(climbTime/.56f)):Vector3.Lerp(lifted,landing,CityPhysics.Ease((climbTime-.56f)/.44f));
                Quaternion facing=Quaternion.LookRotation(CityPhysics.Flat(landing-climbStart).normalized);ctx.Motor.SetCarriedPose(next,facing,0,"ladder");
                for(int i=0;i<2;i++)ctx.Motor.HandTargets[i]=new Vector3(ladder.x+(i==0?-.23f:.23f),Mathf.Lerp(climbStart.y+2,ladder.top+1,climbTime),ladder.z);
                if(climbTime>=1){climbing=null;mode="land";ctx.Motor.SetInputLock(false);ctx.Motor.SetMarineState(false,"idle",0,0,stroke);ctx.Motor.Warp(landing);cooldown=ctx.Clock+.35f;}
                return;
            }
            Vector3 forward=ctx.Follow.FlatForward,right=Vector3.Cross(Vector3.up,forward),move=Vector3.ClampMagnitude(forward*ctx.Input.Move.y+right*ctx.Input.Move.x,1);
            if(ctx.Motor.Busy)move=Vector3.zero;
            effort=CityVehicleMath.Damp(effort,move.magnitude,3.6f,dt);stroke+=dt*(1.5f+effort*(ctx.Input.Run?5.2f:3.65f));
            Vector3 wind=ctx.Systems?ctx.Systems.Weather.Wind:Vector3.zero;
            swimVelocity=Vector3.Lerp(swimVelocity,move*(ctx.Input.Run?3.45f:2.32f)+wind*.009f,1-Mathf.Exp(-4*dt));
            Vector3 wanted=swimVelocity*dt;
            if(CityPhysics.Cast(p+Vector3.up*1.25f,.34f,wanted.normalized,wanted.magnitude,out var hit,ctx.Player))wanted=Vector3.ProjectOnPlane(wanted,hit.normal);
            Vector3 nextP=p+wanted;
            if(ReferenceMap.Active?!ReferenceMap.Contains(nextP):(nextP.x< -208.4f||nextP.x>208.4f||nextP.z< -247.4f)){swimVelocity=Vector3.zero;nextP=p;}
            if(CityPhysics.Cast(p+Vector3.up*1.25f,.34f,(nextP-p).normalized,(nextP-p).magnitude,out _,ctx.Player))nextP=p;
            Quaternion rotation=ctx.Player.rotation;if(move.sqrMagnitude>.01f)rotation=Quaternion.Slerp(rotation,Quaternion.LookRotation(move),1-Mathf.Exp(-6*dt));
            if(Depth(nextP.x,nextP.z)<.62f||!InSea(nextP))
            {
                nextP.y=ctx.Ground(nextP.x,nextP.z)+.04f;mode="land";ctx.Motor.SetMarineState(false,"idle",0,0,stroke);ctx.Motor.Warp(nextP);cooldown=ctx.Clock+.3f;return;
            }
            nextP.y=CityVehicleMath.Damp(p.y,Surface(p.x,p.z)-1.63f+effort*.39f+.035f*Mathf.Sin(stroke),7,dt);
            ctx.Motor.SetMarineState(true,"swim",swimVelocity.magnitude,effort,stroke);ctx.Motor.SetCarriedPose(nextP,rotation,swimVelocity.magnitude,"swim");
            if(effort>.4f&&ctx.Clock-lastSplash>.55f){lastSplash=ctx.Clock;ctx.Sound("stroke",nextP,.35f);}
        }
        void FixedUpdate()
        {
            if(!ctx||!ctx.Ready||ctx.Paused)return;float dt=Time.fixedDeltaTime;
            foreach(var boat in fleet)
            {
                Vector3 p=boat.Root.position;var local=boat.Root.InverseTransformDirection(boat.Body.linearVelocity);boat.Speed=local.z;
                float throttle=0,steer=0;
                bool controlled=boat==active&&!ctx.Motor.Remote&&!ctx.Motor.Busy&&!(ctx.Combat&&ctx.Combat.Down);
                if(controlled&&!boat.ExitRequested){throttle=ctx.Input.Brake?0:ctx.Input.Move.y;steer=ctx.Input.Move.x;}
                float sea=ctx.Systems?ctx.Systems.Weather.SeaState:0,wanted=throttle*(throttle<0?boat.Data.top*.33f:boat.Data.top)*(1-sea*.15f);
                bool shallow=false;foreach(float sx in new[]{-1f,1f})foreach(float sz in new[]{-1f,1f})
                {
                    Vector3 c=boat.Root.TransformPoint(new Vector3(sx*boat.Data.width*.48f,0,sz*boat.Data.length*.44f))+boat.Body.linearVelocity*.5f;
                    if(!InSea(c)||Depth(c.x,c.z)<boat.Data.draft)shallow=true;
                }
                if(shallow)wanted=0;
                float rate=boat.ExitRequested||boat.Moored||shallow||controlled&&ctx.Input.Brake?2.1f:throttle!=0?.65f:.30f;
                float acceleration=(wanted-local.z)*rate;boat.Body.AddForce(boat.Root.forward*acceleration-boat.Root.right*local.x*1.4f,ForceMode.Acceleration);
                float turn=steer*boat.Data.turn*Mathf.Clamp01(Mathf.Abs(local.z)/3.4f)*Mathf.Sign(local.z);boat.Turn=CityVehicleMath.Damp(boat.Turn,turn,2.9f,dt);
                var angular=boat.Body.angularVelocity;angular.y=boat.Turn;boat.Body.angularVelocity=angular;
                for(int i=0;i<4;i++)
                {
                    Vector3 c=boat.Root.TransformPoint(new Vector3((i%2==0?-1:1)*boat.Data.width*.35f,0,(i<2?-1:1)*boat.Data.length*.30f));
                    float difference=Surface(c.x,c.z)-c.y,vertical=boat.Body.GetPointVelocity(c).y;
                    float force=Mathf.Max(0,9.81f+Mathf.Clamp(difference,-.7f,.7f)*24-vertical*7);
                    boat.Body.AddForceAtPosition(Vector3.up*(force*boat.Body.mass*.25f),c,ForceMode.Force);
                }
                if(boat.Wheel)boat.Wheel.localRotation=boat.WheelRotation*Quaternion.Euler(0,0,-boat.Turn*40);
            }
        }
        void LateUpdate(){if(ctx&&ctx.Ready&&!ctx.Paused&&active!=null&&!(ctx.Combat&&ctx.Combat.Down))PilotPose();}
        void PilotPose()
        {
            var boat=active;Vector3 position=boat.Root.TransformPoint(boat.Data.seat);ctx.Motor.SetCarriedPose(position,Quaternion.Euler(0,boat.Root.eulerAngles.y,0),boat.Speed,"pilot");
            if(boat.Wheel)for(int i=0;i<2;i++)ctx.Motor.HandTargets[i]=boat.Wheel.TransformPoint(new Vector3(i==0?-.16f:.16f,0,0));
        }
    }
}
