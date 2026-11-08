using System;
using UnityEngine;

namespace OpenTerritory
{
    [DefaultExecutionOrder(-120)]
    public partial class CityPlayer : MonoBehaviour
    {
        public const float WalkSpeed = 2.65f, SprintSpeed = 9.2f;
        CityContext ctx;CharacterController controller;Renderer[] renderers;bool[] originalVisibility;
        Vector3 planarVelocity,lastSafe,platformPoint,seatExit;Transform platform;float vertical,lastGround=-10,jumpUntil=-10,moveReferenceYaw;Vector2 heldInput;
        SeatData occupiedSeat;Vector3? seat;float seatMove;Vector3 seatFrom,seatTo;bool leavingSeat,locked;
        public Transform Riding {get;private set;}
        public float Speed {get;private set;}
        public bool Swimming {get;private set;}
        public bool Remote {get;private set;}
        public bool Grounded {get;private set;}
        public bool Busy => locked||vault!=null||seatMove>0||Remote||(ctx&&ctx.Combat&&ctx.Combat.Down)||(ctx&&ctx.Police&&ctx.Police.Custody)||(ctx&&ctx.Actors&&ctx.Actors.Petting);
        public float VerticalSpeed=>vertical;
        public float? SeatHeight=>seat.HasValue?Mathf.Max(.1f,seat.Value.y-ctx.Player.position.y):(float?)null;
        public string Mode {get;private set;}="idle";
        public float SwimEffort {get;private set;}
        public float SwimStroke {get;private set;}
        public Vector3[] HandTargets {get;}=new Vector3[2];
        public CharacterController Controller=>controller;
        public Vector3 MoveDirection {get;private set;}
        public Vector3 Velocity=>planarVelocity;
        public float Acceleration {get;private set;}
        float facingVelocity;
        public void Initialize(CityContext context)
        {
            ctx=context;controller=ctx.Player.GetComponent<CharacterController>();if(!controller)controller=ctx.Player.gameObject.AddComponent<CharacterController>();
            controller.height=2.25f;controller.radius=.38f;controller.center=Vector3.up*1.125f;controller.stepOffset=.32f;controller.slopeLimit=46;controller.skinWidth=.025f;controller.minMoveDistance=0;
            ctx.Player.gameObject.layer=CityContext.PlayerLayer;
            renderers=ctx.Player.GetComponentsInChildren<Renderer>(true);originalVisibility=new bool[renderers.Length];for(int i=0;i<renderers.Length;i++)originalVisibility[i]=renderers[i].enabled;
            lastSafe=ctx.Player.position;
            foreach(var entry in ctx.Data.seats)
            {
                var bench=entry;
                ctx.AddAction("seat-"+bench.id,()=>SeatApproach(bench),()=>"Sentar",()=>Sit(bench),
                    ()=>ctx.Node(bench.id)&&ctx.Node(bench.id).gameObject.activeInHierarchy&&!Busy&&!Riding&&!Swimming&&!seat.HasValue&&!bench.occupied&&Grounded&&ctx.Visible(ctx.Player.position+Vector3.up*1.5f,SeatApproach(bench)),2.6f,2,
                    target:()=>ctx.Node(bench.id),focus:()=>new Vector3(bench.x,bench.position.y+bench.seatTop+.15f,bench.z));
            }
            ctx.AddAction("stand",()=>ctx.Player.position+Vector3.up,()=>"Levantar",Stand,()=>seat.HasValue&&seatMove<=0,3,60,requiresLook:false);
        }
        public void Warp(Vector3 point)
        {
            if(!ctx||!controller)return;bool active=controller.enabled||vault!=null;ResetTraversal();controller.enabled=false;ctx.Player.position=point;ResetCrouch(true);controller.enabled=active;
            planarVelocity=Vector3.zero;vertical=0;Speed=0;platform=null;lastSafe=point;heldInput=Vector2.zero;facingVelocity=0;Acceleration=0;
            if(ctx.Follow)ctx.Follow.ResetTracking();
        }
        public void SetRemote(bool active){Remote=active;if(active)jumpWindup=-1;planarVelocity=Vector3.zero;Speed=0;vertical=Mathf.Min(vertical,0);}
        public void SetInputLock(bool active){locked=active;if(active){jumpWindup=-1;planarVelocity=Vector3.zero;Speed=0;}}
        public void SetVisible(bool visible){for(int i=0;i<renderers.Length;i++)if(renderers[i])renderers[i].enabled=visible&&originalVisibility[i];}
        public void SetSeat(Vector3? point)
        {
            jumpWindup=-1;jumpUntil=-10;
            seat=point;planarVelocity=Vector3.zero;vertical=0;Speed=0;ResetCrouch(!point.HasValue);controller.enabled=!point.HasValue&&!Riding&&!Swimming;Mode=point.HasValue?"sit":"idle";
        }
        public void SetRide(Transform ride,bool visible=false)
        {
            jumpWindup=-1;jumpUntil=-10;
            Riding=ride;platform=null;seat=null;vault=null;seatMove=0;planarVelocity=Vector3.zero;vertical=0;Speed=0;
            ResetCrouch(!ride);controller.enabled=!ride&&!Swimming;SetVisible(!ride||visible);Mode=ride?"drive":"idle";
        }
        public void SetCarriedPose(Vector3 point,Quaternion rotation,float speed,string mode)
        {ctx.Player.SetPositionAndRotation(point,rotation);Speed=Mathf.Abs(speed);Mode=mode;Grounded=false;}
        public void SetMarineState(bool swimming,string mode,float speed,float effort,float stroke)
        {if(swimming){jumpWindup=-1;jumpUntil=-10;ResetCrouch();}Swimming=swimming;Mode=mode;Speed=Mathf.Abs(speed);SwimEffort=effort;SwimStroke=stroke;controller.enabled=!swimming&&!Riding&&!seat.HasValue&&vault==null;}
        public void ClearMotion(){ResetTraversal();planarVelocity=Vector3.zero;Speed=0;vertical=0;heldInput=Vector2.zero;}
        void Update()
        {
            if(!ctx||!ctx.Ready||ctx.Paused)return;float dt=Mathf.Min(Time.deltaTime,.05f);
            if(vault!=null){UpdateVault(dt);return;}if(seatMove>0){UpdateSeat(dt);return;}
            if(Riding||Swimming)return;
            if(seat.HasValue){Mode="sit";return;}
            Grounded=controller.enabled&&controller.isGrounded;
            if(Busy){jumpWindup=-1;planarVelocity=Vector3.zero;Speed=0;if(controller.enabled)controller.Move(Vector3.down*2*dt);return;}
            UpdateCrouch(dt);
            if(platform)
            {
                Vector3 point=platform.TransformPoint(platformPoint),delta=point-ctx.Player.position;
                if(delta.magnitude<1.5f)controller.Move(delta);else platform=null;
            }
            var input=ctx.Input;if(input.Jump&&(!Crouched||StandForJump()))jumpUntil=ctx.Clock+.20f;
            // Use the visible camera every frame: holding a key while orbiting
            // remains predictable, and diagonal input never increases top speed.
            moveReferenceYaw=ctx.Camera.transform.eulerAngles.y;
            Vector3 forward=Quaternion.Euler(0,moveReferenceYaw,0)*Vector3.forward;
            Vector3 right=Vector3.Cross(Vector3.up,forward);MoveDirection=Vector3.ClampMagnitude(forward*input.Move.y+right*input.Move.x,1);
            bool aimed=ctx.Combat&&ctx.Combat.Aiming;
            bool fighting=ctx.Combat&&ctx.Combat.Fighting;
            float maximum=Crouched?CrouchSpeed:aimed?(input.Move.y<-.1f?1.35f:1.75f):input.Run?SprintSpeed:WalkSpeed;
            if(fighting)maximum=Mathf.Min(maximum,ctx.Combat.Punching?1.1f:1.65f);
            float oldSpeed=planarVelocity.magnitude;
            float alignment=oldSpeed>.1f&&MoveDirection.sqrMagnitude>.01f?Vector3.Dot(planarVelocity.normalized,MoveDirection.normalized):1;
            // Tight turns shed momentum before the new stride accelerates.
            float corner=aimed?1:Mathf.Lerp(.38f,1,Mathf.InverseLerp(-1,.7f,alignment));
            Vector3 target=MoveDirection*maximum*corner;
            float acceleration=MoveDirection.sqrMagnitude<.001f?29:alignment<0?34:input.Run?19:23;
            planarVelocity=Vector3.MoveTowards(planarVelocity,target,(Grounded?acceleration:5.5f)*dt);
            Acceleration=Mathf.Clamp((planarVelocity.magnitude-oldSpeed)/Mathf.Max(dt,.001f),-30,30);
            if(Grounded){lastGround=ctx.Clock;if(vertical<0)vertical=-2;lastSafe=ctx.Player.position;}
            if(jumpUntil>=ctx.Clock&&ctx.Clock-lastGround<.11f&&jumpWindup<0)
            {
                Vector3 direction=MoveDirection.sqrMagnitude>.1f?MoveDirection.normalized:ctx.Player.forward;
                if(MoveDirection.sqrMagnitude>.1f&&TryParkour(direction))return;
                jumpUntil=-10;jumpWindup=0;
            }
            if(jumpWindup>=0)
            {
                jumpWindup+=dt;
                if(jumpWindup>=JumpWindupTime)
                {
                    Vector3 direction=MoveDirection.sqrMagnitude>.1f?MoveDirection.normalized:ctx.Player.forward;
                    if(Grounded&&MoveDirection.sqrMagnitude>.1f&&TryParkour(direction))return;
                    LaunchJump();
                }
            }
            vertical=Mathf.Max(-25,vertical-22*dt);
            bool wasGrounded=Grounded;float impactVelocity=vertical;
            Vector3 before=ctx.Player.position;var flags=controller.Move((planarVelocity+Vector3.up*vertical)*dt);
            if((flags&CollisionFlags.Above)!=0&&vertical>0)vertical=0;
            if((flags&CollisionFlags.Below)!=0)
            {
                Grounded=true;
                if(!wasGrounded&&impactVelocity< -2.8f) RegisterLanding(-impactVelocity);
                vertical=-2;
            }
            else Grounded=false;
            airTime=Grounded?0:airTime+dt;
            Speed=CityPhysics.Flat(ctx.Player.position-before).magnitude/Mathf.Max(dt,.001f);
            if(aimed||fighting||MoveDirection.sqrMagnitude>.02f)
            {
                Vector3 face=fighting?ctx.Combat.MeleeForward:aimed?forward:(planarVelocity.sqrMagnitude>.08f?planarVelocity.normalized:MoveDirection);
                float heading=Mathf.Atan2(face.x,face.z)*Mathf.Rad2Deg-(aimed?18:0);
                float smooth=aimed?.055f:Mathf.Lerp(.075f,.14f,Mathf.Clamp01(Speed/SprintSpeed));
                float angle=Mathf.SmoothDampAngle(ctx.Player.eulerAngles.y,heading,ref facingVelocity,smooth,aimed?900:620,dt);
                ctx.Player.rotation=Quaternion.Euler(0,angle,0);
            }
            Mode=Grounded?(Crouched?Speed>.12f?"crouch-walk":"crouch":Speed>5?"run":Speed>.12f?"walk":"idle"):vertical>0?"jump":"fall";
            if(Grounded&&CityPhysics.Ground(ctx.Player.position,out var ground,ctx.Player,null,.5f))
            {
                var body=ground.rigidbody;platform=body&&ground.normal.y>.6f?body.transform:null;
                if(platform)platformPoint=platform.InverseTransformPoint(ctx.Player.position);
            }
            else platform=null;
            if(ctx.Player.position.y< -45){Warp(lastSafe+Vector3.up*.15f);ctx.Say("Local seguro.",ctx.Player.position+Vector3.up*2);}
        }
        void Sit(SeatData bench)
        {
            if(!CanLeaveSeat(bench,out seatExit)){ctx.Say("Saída bloqueada.",ctx.Player.position+Vector3.up*2);return;}
            jumpWindup=-1;jumpUntil=-10;
            occupiedSeat=bench;bench.occupied=true;float yaw=bench.yaw*Mathf.Rad2Deg;
            Vector3 front=Quaternion.Euler(0,yaw,0)*Vector3.forward;
            seatFrom=ctx.Player.position;seatTo=new Vector3(bench.x,bench.position.y,bench.z)+front*(bench.depth*.5f-.30f);
            seat=new Vector3(seatTo.x,seatTo.y+bench.seatTop,seatTo.z);ctx.Player.rotation=Quaternion.Euler(0,yaw,0);
            leavingSeat=false;seatMove=.001f;controller.enabled=false;ResetCrouch();planarVelocity=Vector3.zero;Mode="sit";
        }
        public void OnSeatDestroyed(int id,Transform root)
        {
            if(occupiedSeat==null||occupiedSeat.id!=id)return;
            occupiedSeat.occupied=false;occupiedSeat=null;seatMove=0;SetSeat(null);
            // The original furniture collision has already been disabled.
            // Let the normal controller settle onto the floor, without a warp.
            vertical=-2;planarVelocity=Vector3.zero;
        }
        Vector3 SeatApproach(SeatData bench)
        {
            // The prompt stays in front of the furniture: an opaque bench
            // must not reject its own action, or let it be used through a wall.
            var point=new Vector3(bench.x,0,bench.z)+Quaternion.Euler(0,bench.yaw*Mathf.Rad2Deg,0)*new Vector3(0,0,bench.depth*.5f+.45f);
            point.y=ctx.Ground(point.x,point.z)+1.1f;return point;
        }
        bool CanLeaveSeat(SeatData bench,out Vector3 point)
        {
            var rotation=Quaternion.Euler(0,bench.yaw*Mathf.Rad2Deg,0);
            foreach(var offset in new[]{new Vector3(0,0,bench.depth*.5f+.85f),new Vector3(bench.width*.5f+.6f,0,.35f),new Vector3(-bench.width*.5f-.6f,0,.35f)})
            {
                point=new Vector3(bench.x,0,bench.z)+rotation*offset;point.y=ctx.Ground(point.x,point.z)+.03f;
                if(CityPhysics.StandClear(point,ctx.Player))return true;
            }
            point=seatExit;return false;
        }
        void Stand()
        {
            if(occupiedSeat!=null&&!CanLeaveSeat(occupiedSeat,out seatExit)){ctx.Say("Saída bloqueada.",ctx.Player.position+Vector3.up*2);return;}
            seatFrom=ctx.Player.position;seatTo=seatExit;leavingSeat=true;seatMove=.001f;
        }
        void UpdateSeat(float dt)
        {
            seatMove=Mathf.Min(1,seatMove+dt/.65f);ctx.Player.position=Vector3.Lerp(seatFrom,seatTo,CityPhysics.Ease(seatMove));
            if(seatMove<1)return;seatMove=0;
            if(leavingSeat){if(occupiedSeat!=null)occupiedSeat.occupied=false;occupiedSeat=null;SetSeat(null);vertical=-2;}
        }
    }
}
