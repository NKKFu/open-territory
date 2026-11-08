window.__unityExport=()=>{
 const keys=o=>o?Object.keys(o):null;
 return {scene:scene.children.length,people:people.length,cars:cars.map(c=>({axis:c.axis,road:c.laneRoad,x:c.g.position.x,z:c.g.position.z,doors:c.carDoors&&Object.entries(c.carDoors).map(([k,d])=>[k,keys(d)])})),benches:benches.slice(0,2).map(keys),service:servicePoints.map(s=>({...s,place:s.place?.kind})),devices:devices.map(d=>({label:d.label,keys:keys(d.unit),type:d.unit.type})),boats:boats.map(b=>({keys:keys(b),user:keys(b.g.userData)})),fish:keys(fishMarket.exportData?.()),harbor:keys(harbor),cinema:keys(streetCinema),ground:renderedGround._surfaces?.().length,resident:keys(people[0]),rig:keys(player.userData),solids:solids.slice(0,4),obstacles:cameraObstacles.slice(0,4)};
};
