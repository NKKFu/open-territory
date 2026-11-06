// Small procedural sounds keep the static game self-contained. The browser's
// first user gesture starts audio; there is no autoplay prompt or extra UI.
export function createCityAudio(){
  let context=null,master,meter,noiseBuffer,seaGain,windGain,started=false,muted=false;
  let listener={x:0,z:0,yaw:0},footDistance=0,lastPosition=null,birdAt=3,lastStepAt=0;
  const voices=[],doorStates=new Map(),eventCounts={};
  const clamp=(v,a,b)=>Math.max(a,Math.min(b,v));
  function value(param,target,speed=.12){param.setTargetAtTime(target,context.currentTime,speed);}
  function spatial(x,z){
    const dx=x-listener.x,dz=z-listener.z,distance=Math.hypot(dx,dz);
    return {gain:1/(1+(distance/12)**2),pan:clamp((Math.cos(listener.yaw)*dx-Math.sin(listener.yaw)*dz)/Math.max(5,distance),-.9,.9)};
  }
  function noiseLoop(frequency,gain){
    const source=context.createBufferSource(),filter=context.createBiquadFilter(),volume=context.createGain();
    source.buffer=noiseBuffer;source.loop=true;filter.type='lowpass';filter.frequency.value=frequency;volume.gain.value=gain;
    source.connect(filter).connect(volume).connect(master);source.start();return volume;
  }
  function unlock(){
    if(started){if(context?.state!=='running')context.resume().catch(()=>{});return;}
    const AudioContext=window.AudioContext||window.webkitAudioContext;if(!AudioContext)return;
    context=new AudioContext();master=context.createGain();master.gain.value=muted?0:.42;
    const limiter=context.createDynamicsCompressor();limiter.threshold.value=-12;limiter.knee.value=18;limiter.ratio.value=4;
    meter=context.createAnalyser();meter.fftSize=256;master.connect(limiter).connect(meter).connect(context.destination);
    noiseBuffer=context.createBuffer(1,context.sampleRate*2,context.sampleRate);
    const data=noiseBuffer.getChannelData(0);for(let i=0;i<data.length;i++)data[i]=(Math.random()*2-1)*.7;
    windGain=noiseLoop(240,.005);seaGain=noiseLoop(850,0);
    for(let i=0;i<4;i++){
      const oscillator=context.createOscillator(),filter=context.createBiquadFilter(),gain=context.createGain(),pan=context.createStereoPanner();
      oscillator.type='sawtooth';oscillator.frequency.value=35;filter.type='lowpass';filter.frequency.value=240;gain.gain.value=0;
      oscillator.connect(filter).connect(gain).connect(pan).connect(master);oscillator.start();voices.push({oscillator,filter,gain,pan,id:null});
    }
    started=true;context.resume().catch(()=>{});
  }
  const onGesture=()=>unlock();
  addEventListener('pointerdown',onGesture,{capture:true});addEventListener('keydown',onGesture,{capture:true});
  function transient(kind,x,z){
    if(!context||context.state!=='running'||muted)return;
    const location=spatial(x,z);if(location.gain<.015)return;
    const now=context.currentTime,gain=context.createGain(),pan=context.createStereoPanner();
    const frequency={step:210,grass:850,door:360,automatic:1500,impact:170,break:1100,flutter:1600}[kind]||300;
    const duration={step:.11,grass:.12,door:.22,automatic:.45,impact:.32,break:.28,flutter:.23}[kind]||.12;
    const level={step:.19,grass:.11,door:.14,automatic:.055,impact:.3,break:.18,flutter:.07}[kind]||.1;
    const source=context.createBufferSource(),filter=context.createBiquadFilter();source.buffer=noiseBuffer;
    filter.type=kind==='automatic'?'bandpass':'lowpass';filter.frequency.value=frequency;filter.Q.value=.6;
    gain.gain.setValueAtTime(.0001,now);gain.gain.exponentialRampToValueAtTime(level*location.gain,now+.008);
    gain.gain.exponentialRampToValueAtTime(.0001,now+duration);pan.pan.value=location.pan;
    source.connect(filter).connect(gain).connect(pan).connect(master);source.start();source.stop(now+duration+.02);
    source.onended=()=>{source.disconnect();filter.disconnect();gain.disconnect();pan.disconnect();};
    eventCounts[kind]=(eventCounts[kind]||0)+1;
  }
  function bird(x,z,coastal){
    const now=context.currentTime,position=spatial(x,z),osc=context.createOscillator(),gain=context.createGain(),pan=context.createStereoPanner();
    osc.type='sine';osc.frequency.setValueAtTime(coastal?650:1700,now);
    osc.frequency.exponentialRampToValueAtTime(coastal?950:2700,now+.12);
    osc.frequency.exponentialRampToValueAtTime(coastal?530:1500,now+.35);
    gain.gain.setValueAtTime(.0001,now);gain.gain.exponentialRampToValueAtTime(.045*position.gain,now+.03);gain.gain.exponentialRampToValueAtTime(.0001,now+.5);
    pan.pan.value=position.pan;osc.connect(gain).connect(pan).connect(master);osc.start();osc.stop(now+.52);
    osc.onended=()=>{osc.disconnect();gain.disconnect();pan.disconnect();};eventCounts.bird=(eventCounts.bird||0)+1;
  }
  return {
    play:transient,
    update({dt,time,position,yaw,vehicle,vehicles,walking,paved,inside,doors,night}){
      listener={x:position.x,z:position.z,yaw};
      const distance=lastPosition?Math.hypot(position.x-lastPosition.x,position.z-lastPosition.z):0;
      lastPosition={x:position.x,z:position.z};
      if(!context||context.state!=='running')return;
      value(windGain.gain,inside ? .001 : .005,.5);
      const sea=clamp(1-Math.abs(position.z+155)/100,0,1)*(inside ? .2 : 1);
      value(seaGain.gain,sea*.045*(.8+.2*Math.sin(time*.43)),.6);
      if(walking&&distance<2){
        footDistance+=distance;
        if(footDistance>1.55&&time-lastStepAt>.24){footDistance%=1.55;lastStepAt=time;transient(paved?'step':'grass',position.x,position.z);}
      }else footDistance=0;
      const audible=vehicles.filter(c=>c.kind!=='bike'&&(c.auto||c===vehicle||c.incident)&&!c.incident?.actor)
        .map(c=>({c,d:Math.hypot(c.g.position.x-position.x,c.g.position.z-position.z)})).filter(p=>p.d<48)
        .sort((a,b)=>(a.c===vehicle?-100:a.d)-(b.c===vehicle?-100:b.d)).slice(0,voices.length);
      voices.forEach((voice,i)=>{
        const item=audible[i];if(!item){value(voice.gain.gain,0);voice.id=null;return;}
        const {c,d}=item,placement=spatial(c.g.position.x,c.g.position.z),speed=Math.abs(c.velocity);
        const scale=(c.kind==='bus'||c.role==='fire') ? .75 : 1;
        const enginePitch=c.engineRpm?22+c.engineRpm/50:32+speed*3.4;
        value(voice.oscillator.frequency,enginePitch*scale,.1);value(voice.filter.frequency,130+speed*22,.15);
        value(voice.gain.gain,(c===vehicle ? .06 : .025)*placement.gain*(inside ? .3 : 1)*(1+speed/30),.2);
        value(voice.pan.pan,c===vehicle?0:placement.pan);voice.id=c.debugId||`${c.kind||'car'}:${Math.round(d)}`;
      });
      for(const place of doors){
        const door=place.door,openness=Math.abs(door.angle||0)+Math.abs(door.offset||0),previous=doorStates.get(place)||0;
        if(openness>.2&&previous<=.2&&Math.hypot(door.x-position.x,door.z-position.z)<20)transient(door.automatic?'automatic':'door',door.x,door.z);
        if(openness<.035&&previous>=.035&&Math.hypot(door.x-position.x,door.z-position.z)<20)transient('door',door.x,door.z);
        doorStates.set(place,openness);
      }
      if(time>birdAt){birdAt=time+5+Math.random()*8;if(!inside&&night<.6)bird(position.x+(Math.random()-.5)*30,position.z-12,sea>.5);}
    },
    snapshot:()=>{
      let peak=0;if(meter){const samples=new Float32Array(meter.fftSize);meter.getFloatTimeDomainData(samples);peak=Math.max(...samples.map(Math.abs));}
      return {started,state:context?.state||'waiting-for-gesture',engines:voices.filter(v=>v.id).length,peak,events:{...eventCounts}};
    },
    setMuted(value){muted=!!value;if(master)master.gain.setTargetAtTime(muted?0:.42,context.currentTime,.05);},
    dispose(){removeEventListener('pointerdown',onGesture,{capture:true});removeEventListener('keydown',onGesture,{capture:true});context?.close();}
  };
}
