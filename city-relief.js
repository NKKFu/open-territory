// Survey elevations, in metres from sea level. Streets and sidewalks share
// each change in grade. Homes, shops and the upper garden have level lots.
const stations=[
  [-95,0],[-90,.12],[-80,1.19],[-75,1.58],[-70,1.58],[-65,1.16],[-55,0],
  [50,0],[55,.14],[60,.68],[70,2.13],[80,3.58],[85,4.13],[90,4.30],
  [120,4.30],[125,4.54],[135,5.88],[145,7.53],[150,8.36],[155,8.80]
];
const first=stations[0][0],last=stations.at(-1)[0],step=5;
const grid=new Float64Array((last-first)/step+1);
for(let i=1;i<stations.length;i++){
  const [start,low]=stations[i-1],[end,high]=stations[i];
  for(let z=start;z<=end;z+=step)grid[(z-first)/step]=low+(high-low)*(z-start)/(end-start);
}
export function cityElevation(z){
  if(z<=first)return grid[0];
  if(z>=last)return grid[grid.length-1];
  const pos=(z-first)/step,index=Math.floor(pos);
  return grid[index]+(grid[index+1]-grid[index])*(pos-index);
}
export function gradedBetween(z0,z1){
  if(z1<z0)[z0,z1]=[z1,z0];
  const start=cityElevation(z0);
  if(Math.abs(cityElevation(z1)-start)>1e-7)return true;
  return stations.some(([z,height])=>z>z0&&z<z1&&Math.abs(height-start)>1e-7);
}
// Cut all adjoining faces on the same contours. Sampling just the corners of a
// long block would leave road paint and curbs suspended over changes in grade.
export function addGradedFace(output,points){
  let polygon=points;
  const min=Math.min(...points.map(p=>p.z)),max=Math.max(...points.map(p=>p.z));
  function append(poly){
    for(let i=1;i<poly.length-1;i++){
      for(const p of [poly[0],poly[i],poly[i+1]])output.push(p.x,p.y+cityElevation(p.z),p.z);
    }
  }
  for(const [line] of stations){
    if(line<=min+1e-7||line>=max-1e-7)continue;
    const lower=[],upper=[];
    for(let i=0;i<polygon.length;i++){
      const a=polygon[i],b=polygon[(i+1)%polygon.length];
      if(a.z<=line)lower.push(a);if(a.z>=line)upper.push(a);
      if((a.z<line&&b.z>line)||(a.z>line&&b.z<line)){
        const t=(line-a.z)/(b.z-a.z),cut={x:a.x+(b.x-a.x)*t,y:a.y+(b.y-a.y)*t,z:line};
        lower.push(cut);upper.push(cut);
      }
    }
    append(lower);polygon=upper;
  }
  append(polygon);
}
