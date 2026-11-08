// A single billboard follows the current target. Its size uses CSS pixels so zoom
// and mobile display density do not change the legibility of the key or action.
export function createInteractionPrompt(T,scene,touchButton,canSee){
  const canvas=document.createElement('canvas');canvas.width=512;canvas.height=64;
  const ctx=canvas.getContext('2d'),texture=new T.CanvasTexture(canvas);
  texture.colorSpace=T.SRGBColorSpace;texture.generateMipmaps=false;texture.minFilter=T.LinearFilter;
  const material=new T.SpriteMaterial({map:texture,transparent:true,depthTest:false,depthWrite:false,toneMapped:false,fog:false});
  const sprite=new T.Sprite(material);sprite.visible=false;sprite.renderOrder=7;scene.add(sprite);
  const projected=new T.Vector3(),speechPoint=new T.Vector3(),local=new T.Vector3();
  const head=new T.Vector3(),feet=new T.Vector3(),right=new T.Vector3();
  let text='',width=0,accessible='';
  function paint(label){
    text=label;ctx.font='600 30px system-ui, sans-serif';
    width=Math.min(canvas.width,Math.ceil(ctx.measureText(label).width)+90);
    ctx.clearRect(0,0,canvas.width,canvas.height);
    ctx.fillStyle='rgba(14,36,40,.93)';ctx.beginPath();ctx.roundRect(1,1,width-2,62,13);ctx.fill();
    ctx.fillStyle='#e1f1db';ctx.beginPath();ctx.roundRect(8,8,47,48,8);ctx.fill();
    ctx.textBaseline='middle';ctx.textAlign='center';ctx.fillStyle='#123538';ctx.font='750 30px system-ui, sans-serif';ctx.fillText('E',31.5,32);
    ctx.fillStyle='#f7f7ec';ctx.textAlign='left';ctx.font='600 30px system-ui, sans-serif';ctx.fillText(label,69,32,width-80);
    texture.repeat.x=width/canvas.width;texture.needsUpdate=true;
  }
  function fitSpeech(camera,speech){
    if(!speech.visible||innerWidth>=650)return;
    speechPoint.copy(speech.position).project(camera);
    const depth=-local.copy(speech.position).applyMatrix4(camera.matrixWorldInverse).z;
    if(depth<=camera.near||Math.abs(speechPoint.x)>=1)return;
    const unit=2*Math.tan(camera.fov*Math.PI/360)*depth/innerHeight,limit=innerWidth-16;
    if(speech.scale.x>limit*unit){
      const ratio=limit*unit/speech.scale.x,height=speech.scale.y;
      speech.scale.set(limit*unit,height*ratio,1);
      speech.position.addScaledVector(local.setFromMatrixColumn(camera.matrixWorld,1),-(height-speech.scale.y)/2);
    }
    const half=speech.scale.x/unit/2,x=(speechPoint.x+1)*innerWidth/2;
    const shift=T.MathUtils.clamp(x,half+8,innerWidth-half-8)-x;
    speech.position.addScaledVector(right.setFromMatrixColumn(camera.matrixWorld,0),shift*unit);
  }
  function update(label,position,camera,speech,actor,subject,target,hideWorld=false){
    sprite.visible=false;
    const buttonLabel=label||'Interagir';
    if(buttonLabel!==accessible){touchButton.setAttribute('aria-label',buttonLabel);accessible=buttonLabel;}
    if(!label||hideWorld||!canSee(position,target))return;
    if(label!==text)paint(label);
    const depth=-local.copy(position).applyMatrix4(camera.matrixWorldInverse).z;
    if(depth<=camera.near)return;
    const worldPixel=2*Math.tan(camera.fov*Math.PI/360)*depth/innerHeight;
    const height=Math.min(32,Math.max(26,innerHeight*.05)),w=height*width/64;
    sprite.position.copy(position);sprite.scale.set(w*worldPixel,height*worldPixel,1);
    projected.copy(position).project(camera);
    if(speech.visible){
      speechPoint.copy(speech.position).project(camera);
      const speechDepth=-local.copy(speech.position).applyMatrix4(camera.matrixWorldInverse).z;
      if(speechDepth>camera.near){
        const speechPixel=worldPixel*speechDepth/depth,sw=speech.scale.x/speechPixel,sh=speech.scale.y/speechPixel;
        const overlapX=Math.abs(projected.x-speechPoint.x)*innerWidth<(w+sw+10);
        const gap=(speechPoint.y-projected.y)*innerHeight/2;
        if(overlapX&&Math.abs(gap)<(height+sh)/2+4){
          const shift=(height+sh)/2+6-gap;
          if(shift*worldPixel>1.15)return;
          sprite.position.addScaledVector(local.setFromMatrixColumn(camera.matrixWorld,1),-shift*worldPixel);
          projected.copy(sprite.position).project(camera);
        }
      }
    }
    // Keep the complete key and verb beside the actors, even when the speech
    // bubble moves the prompt down to face height.
    const startX=projected.x*innerWidth/2,leftEdge=(w-innerWidth)/2+3,rightEdge=-leftEdge;
    let left=startX,rightX=startX;
    for(const body of [actor,subject]){
      if(!body?.visible)continue;
      const distance=-local.copy(body.position).applyMatrix4(camera.matrixWorldInverse).z;
      if(distance<=camera.near||distance>depth+.4)continue;
      head.copy(body.position);head.y+=2.55*body.scale.y;head.project(camera);
      feet.copy(body.position).project(camera);
      if(projected.y>=head.y+height/innerHeight||projected.y<=feet.y-height/innerHeight)continue;
      const center=(head.x+feet.x)*innerWidth/4,margin=.68*body.scale.x/(worldPixel*distance/depth)+w/2+4;
      if(startX>center-margin&&startX<center+margin){left=Math.min(left,center-margin);rightX=Math.max(rightX,center+margin);}
    }
    if(left!==rightX){
      const leftFits=left>=leftEdge,rightFits=rightX<=rightEdge;
      const useLeft=leftFits&&(!rightFits||startX-left<rightX-startX);
      const x=T.MathUtils.clamp(useLeft?left:rightX,leftEdge,rightEdge);
      sprite.position.addScaledVector(right.setFromMatrixColumn(camera.matrixWorld,0),(x-startX)*worldPixel);
      projected.copy(sprite.position).project(camera);
    }
    sprite.visible=projected.z>-1&&projected.z<1&&Math.abs(projected.x)<1&&Math.abs(projected.y)<1;
    if(sprite.visible&&sprite.position.distanceToSquared(position)>.0001)sprite.visible=canSee(sprite.position,target);
  }
  return {fitSpeech,update,get visibleLabel(){return sprite.visible?text:null;}};
}
