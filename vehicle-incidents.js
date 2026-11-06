// Drivers leave and return along a checked route. The city owns collisions,
// rendering and walking; this controller owns the interruption's lifecycle.
export function createVehicleIncidents({findExit,makeActor,removeActor,moveActor,
  isTalking=()=>false,setDoor=()=>{},lookAt=()=>{},impact=()=>{}}){
  function begin(car,speed,time){
    if(!car.auto||car.kind==='bus'||car.incident||speed<3.5||time<(car.incidentCooldown||0))return false;
    car.incident={state:'waiting',elapsed:0,actor:null,route:null,index:0,door:0};
    car.velocity=0;car.hazards=true;car.incidentCount=(car.incidentCount||0)+1;
    impact(car,speed);return true;
  }
  function stage(i,state){i.state=state;i.elapsed=0;i.index=0;}
  function walk(car,i,points,dt,next){
    const goal=points[i.index];
    if(!goal){stage(i,next);return;}
    if(moveActor(i.actor,goal,dt,car))i.index++;
  }
  return {begin,
    update(car,dt,time){
      const i=car.incident;if(!i)return false;
      car.velocity=0;car.stoppedReason='averiguando';car.braking=true;
      if(car.brake)car.brake.emissiveIntensity=1.5;
      if(i.actor&&(i.actor.ragdoll||isTalking(i.actor)))return true;
      i.elapsed+=dt;
      if(i.state==='waiting'){
        if(i.elapsed<.8)return true;
        const route=findExit(car);if(!route)return true;
        i.route=route;stage(i,'opening');
      }else if(i.state==='opening'){
        i.door=Math.min(1,i.door+dt*2);setDoor(car,i.route.side,i.door);
        if(i.door>=1){i.actor=makeActor(car,i.route.seat);car.driver.visible=false;stage(i,'exiting');}
      }else if(i.state==='exiting'){
        walk(car,i,[i.route.exit],dt,'walking');
      }else if(i.state==='walking'){
        i.door=Math.max(0,i.door-dt*2);setDoor(car,i.route.side,i.door);
        walk(car,i,i.route.inspection,dt,'inspecting');
      }else if(i.state==='inspecting'){
        lookAt(i.actor,car);
        if(i.elapsed>4.5)stage(i,'returning');
      }else if(i.state==='returning'){
        walk(car,i,[...i.route.inspection].reverse().concat([i.route.exit]),dt,'reopening');
      }else if(i.state==='reopening'){
        i.door=Math.min(1,i.door+dt*2);setDoor(car,i.route.side,i.door);
        if(i.door>=1)stage(i,'entering');
      }else if(i.state==='entering'){
        if(moveActor(i.actor,i.route.seat,dt,car)){
          removeActor(i.actor);i.actor=null;car.driver.visible=true;stage(i,'closing');
        }
      }else if(i.state==='closing'){
        i.door=Math.max(0,i.door-dt*2);setDoor(car,i.route.side,i.door);
        if(i.door===0){car.hazards=false;car.incidentCooldown=time+8;car.incident=null;}
      }
      return true;
    }
  };
}
