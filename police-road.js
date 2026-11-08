const clamp=(v,a,b)=>Math.max(a,Math.min(b,v));
const columns=[-130,-72,0,72,130],rows=[-131,-72,0,72],lane=3.3,corner=8.7;
const rounds=[[2,2],[2,3],[0,3],[0,1],[2,1],[2,0],[4,0],[4,2],[2,2]];
const node=(col,row)=>({col,row,x:columns[col],z:rows[row]});
const same=(a,b)=>a.col===b.col&&a.row===b.row;
const heading=(a,b)=>({x:Math.sign(b.x-a.x),z:Math.sign(b.z-a.z)});
const range=(a,b)=>Math.hypot(a.x-b.x,a.z-b.z);

// The road lattice uses right-hand lanes. Through a junction a quadratic
// joins the incoming and outgoing lane tangents instead of cutting a block.
export function createPoliceRoute(roadZ){
  let previous=node(3,2),next=node(2,2),round=0,chosen=node(2,3),path=null,cursor=0,targetRefresh=0;
  function choose(goal){
    if(!goal&&same(next,node(...rounds[round])))round=(round+1)%rounds.length;
    const aim=goal??node(...rounds[round]);
    const neighbours=[];
    for(const [dc,dr] of [[-1,0],[1,0],[0,-1],[0,1]]){
      const col=next.col+dc,row=next.row+dr;
      if(col>=0&&col<columns.length&&row>=0&&row<rows.length){
        const n=node(col,row);if(!same(n,previous))neighbours.push(n);
      }
    }
    const incoming=heading(previous,next);
    function score(n){
      const d=heading(next,n),length=range(next,n);
      const progress=clamp(((aim.x-next.x)*d.x+(aim.z-next.z)*d.z)/length,0,1);
      const near={x:next.x+d.x*length*progress,z:next.z+d.z*length*progress};
      return range(near,aim)*1.5+range(n,aim)*.35+(d.x===incoming.x&&d.z===incoming.z?0:1.2);
    }
    return neighbours.sort((a,b)=>score(a)-score(b))[0];
  }
  function pointOnLane(n,d,along){
    const x=n.x+d.x*along+d.z*lane;
    return {x,z:d.x?roadZ(x,n.z)-d.x*lane:n.z+d.z*along};
  }
  function build(){
    const dir=heading(previous,next),out=heading(next,chosen),span=range(previous,next);
    const points=[],lengths=[];
    function add(p){
      const last=points.at(-1),dist=last?range(last,p):0;
      if(last&&dist<.005)return;
      lengths.push((lengths.at(-1)||0)+dist);points.push(p);
    }
    for(let a=corner;a<span-corner;a+=2.7)add(pointOnLane(previous,dir,a));
    const entry=pointOnLane(next,dir,-corner),exit=pointOnLane(next,out,corner);add(entry);
    const bend=dir.z*out.x-dir.x*out.z;
    const through=bend?(dir.x?{x:exit.x,z:entry.z}:{x:entry.x,z:exit.z}):{x:(entry.x+exit.x)/2,z:(entry.z+exit.z)/2};
    const turnAt=lengths.at(-1);
    for(let i=1;i<=12;i++){
      const t=i/12,u=1-t;
      add({x:u*u*entry.x+2*u*t*through.x+t*t*exit.x,z:u*u*entry.z+2*u*t*through.z+t*t*exit.z});
    }
    return {points,lengths,length:lengths.at(-1),turnAt,bend};
  }
  function locate(pos){
    let at=0,best=Infinity;
    for(let i=1;i<path.points.length;i++){
      const a=path.points[i-1],b=path.points[i],x=b.x-a.x,z=b.z-a.z,sq=x*x+z*z;
      const t=clamp(((pos.x-a.x)*x+(pos.z-a.z)*z)/sq,0,1);
      const d=(pos.x-a.x-x*t)**2+(pos.z-a.z-z*t)**2;
      if(d<best){best=d;at=path.lengths[i-1]+Math.sqrt(sq)*t;}
    }
    return at;
  }
  function sample(at){
    const i=Math.max(1,path.lengths.findIndex(n=>n>=at)),a=path.points[i-1],b=path.points[i];
    const segment=path.lengths[i]-path.lengths[i-1],t=clamp((at-path.lengths[i-1])/segment,0,1);
    return {x:a.x+(b.x-a.x)*t,z:a.z+(b.z-a.z)*t,dx:(b.x-a.x)/segment,dz:(b.z-a.z)/segment};
  }
  path=build();
  return {
    update(pos,speed,goal,time){
      cursor=locate(pos);
      if(cursor>=path.length-1.65){
        previous=next;next=chosen;chosen=choose(goal);path=build();cursor=locate(pos);targetRefresh=time+.65;
      }else if(time>=targetRefresh&&cursor<path.turnAt-9){
        targetRefresh=time+.65;const planned=choose(goal);
        if(!same(planned,chosen)){chosen=planned;path=build();cursor=locate(pos);}
      }
      const turnGap=path.turnAt-cursor,cruise=goal?11.6:6.8;
      // Inner right turns are tighter than left turns in right-hand traffic.
      const turnSpeed=path.bend>0?3.4:4.6;
      const limit=path.bend?Math.min(cruise,Math.sqrt(turnSpeed**2+5*Math.max(0,turnGap-2-speed*.55))):cruise;
      const aim=sample(Math.min(path.length,cursor+clamp(4.1+speed*.55,5.5,9)));
      return {aim,limit,signal:path.bend&&turnGap<Math.max(9,speed*2)?(path.bend>0?'right':'left'):null,
        road:previous.row===next.row?previous.z:previous.x,axis:previous.row===next.row?'x':'z',
        direction:previous.row===next.row?Math.sign(next.x-previous.x):Math.sign(next.z-previous.z),
        next:{x:next.x,z:next.z},toward:{x:chosen.x,z:chosen.z}};
    }
  };
}
