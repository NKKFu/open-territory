// Distances along the player's approach, in the local axes of a wall or car.
// An expanded footprint clears the feet. The handhold uses the actual surface.
const FOOT_RADIUS = .42;
const clamp = (n, lo, hi) => Math.max(lo, Math.min(hi, n));
const smooth = t => t * t * (3 - 2 * t);
const mix = (a, b, t) => a + (b - a) * t;
function travel(from,to,entrySpeed,exitSpeed,duration,u) {
  // Cubic interpolation with matching velocities at the edge and the landing.
  return mix(from,to,smooth(u))+duration*u*(1-u)*(entrySpeed*(1-u)-exitSpeed*u);
}

function span(start, dir, box, margin) {
  const c = Math.cos(box.yaw), s = Math.sin(box.yaw);
  const x = start.x - box.x, z = start.z - box.z;
  const axes = [
    [c*x - s*z, c*dir.x - s*dir.z, box.w/2 + margin],
    [s*x + c*z, s*dir.x + c*dir.z, box.d/2 + margin]
  ];
  let near = -Infinity, far = Infinity;
  for (const [origin, speed, half] of axes) {
    if (half <= 0) return null;
    if (Math.abs(speed) < .0001) {
      if (Math.abs(origin) > half) return null;
      continue;
    }
    const a = (-half - origin) / speed, b = (half - origin) / speed;
    near = Math.max(near, Math.min(a, b));
    far = Math.min(far, Math.max(a, b));
  }
  return near < far && far > 0 ? {near, far} : null;
}

function handhold(box, x, z) {
  const c = Math.cos(box.yaw), s = Math.sin(box.yaw);
  const lx = clamp(c*(x-box.x) - s*(z-box.z), -box.w*.46, box.w*.46);
  const lz = clamp(s*(x-box.x) + c*(z-box.z), -box.d*.46, box.d*.46);
  return {x:box.x+c*lx+s*lz, y:box.top+.04, z:box.z-s*lx+c*lz};
}

export function planParkour(start, dir, surfaces, groundHeight) {
  let near = Infinity, far = 0, highest = null, crossesSurface = false;
  for (const box of surfaces) {
    const hit = span(start, dir, box, FOOT_RADIUS);
    if (!hit || box.top <= start.y + .2) continue;
    if (span(start,dir,box,0)) crossesSurface = true;
    near = Math.min(near, hit.near);
    far = Math.max(far, hit.far);
    if (!highest || box.top > highest.top) highest = box;
  }
  if (!highest || !crossesSurface || near < -.12 || near > 1.35) return null;
  const rise = highest.top - start.y;
  const rim = span(start,dir,highest,FOOT_RADIUS);
  const face = span(start, dir, highest, 0) || rim;
  if (rise < .5 || rise > 2.55) return null;
  const climb = rise > 1.65;
  let end;
  if (climb) {
    const footing = span(start, dir, highest, -.24);
    if (!footing || footing.far - footing.near < .20) return null;
    end = Math.max(.3, Math.min(footing.near + .48, (footing.near + footing.far)/2));
  } else {
    if (far - near > 3.65) return null;
    end = far + .62;
  }
  if (end > 5.6) return null;
  const endY = climb ? highest.top : groundHeight(start.x + end*dir.x, start.z + end*dir.z);
  if (!climb && Math.abs(endY - start.y) > .65) return null;
  const plant = climb ? face.near + .1 : (face.near + face.far)/2;
  const cx = start.x + dir.x*plant, cz = start.z + dir.z*plant;
  // From a bumper the hood is a usable step before the edge of the roof.
  // Approaching from the side has no such step.
  let step = null;
  if (climb && rim.near-near>.45) {
    for (const box of surfaces) {
      const approach=span(start,dir,box,FOOT_RADIUS);
      if (box.top<highest.top && approach && approach.near<rim.near) step=Math.max(step??start.y,box.top);
    }
  }
  return {
    start:{x:start.x, y:start.y, z:start.z}, dir, near:Math.max(0, near-.035),
    far:far+.035, end, endY, top:highest.top, climb, step, rim:Math.max(0,rim.near-.04), elapsed:0,
    duration:climb ? 1.08 : .58 + end*.085,
    hands:[-1,1].map(side => handhold(highest, cx+dir.z*side*.37, cz-dir.x*side*.37))
  };
}

export function parkourPosition(plan, progress, out) {
  const t = clamp(progress, 0, 1);
  const {start, dir, near, far, top, end, endY, climb} = plan;
  const lift = climb ? .46 : .31, release = climb ? .84 : .69;
  const staged = plan.step!==null;
  const crest = staged ? plan.rim : near;
  const carryEnd = climb ? end-.12 : far;
  const landingSpeed=(end-carryEnd)/(1-release);
  const exitSpeed=Math.min((carryEnd-crest)/(release-lift),landingSpeed*1.6);
  let distance, y;
  if (t < lift) {
    const u = t/lift;
    if (staged && u>.5) {
      distance=mix(near,crest,smooth(2*u-1));
      y=mix(plan.step+.08,top+.08,smooth(2*u-1));
    } else {
      const v=staged?2*u:u;
      distance = mix(0, near, smooth(v));
      y = mix(start.y, (staged?plan.step:top)+.08, smooth(v));
    }
  } else if (t < release) {
    const u = (t-lift)/(release-lift);
    distance = travel(crest,carryEnd,0,exitSpeed,release-lift,u);
    y = top + .08 + .12*Math.sin(Math.PI*u)**2;
  } else {
    const u = (t-release)/(1-release);
    distance = travel(carryEnd,end,exitSpeed,landingSpeed,1-release,u);
    y = mix(top+.08, endY, smooth(u));
  }
  out.x = start.x + dir.x*distance;
  out.y = y;
  out.z = start.z + dir.z*distance;
  return out;
}
