// Small procedural sounds keep the static game self-contained. The browser's
// first user gesture starts audio; there is no autoplay prompt or extra UI.
export function createCityAudio(){
  let context=null,master,meter,noiseBuffer,seaGain,windGain,rainBed,fountain,tires,started=false,muted=false,volume=1,paused=false;
  const outputGain=()=>muted||paused?0:.42*volume;
  function updateOutput(){if(master)master.gain.setTargetAtTime(outputGain(),context.currentTime,.04);}
  let listener={x:0,z:0,yaw:0},footDistance=0,lastPosition=null,birdAt=3,lastStepAt=0;
  const voices=[],doorStates=new Map(),eventCounts={},cooldowns=new Map();
  let insectAt=0,fountainLevel=0,insectLevel=0,tireLevel=0,siren=null,sirenLevel=0,sirenPan=0,rainLevel=0,rainExposure=1;
  const clamp=(v,a,b)=>Math.max(a,Math.min(b,v));
  function value(param,target,speed=.12){param.setTargetAtTime(target,context.currentTime,speed);}
  function spatial(x,z){
    const dx=x-listener.x,dz=z-listener.z,distance=Math.hypot(dx,dz);
    return {gain:1/(1+(distance/12)**2),pan:clamp((Math.cos(listener.yaw)*dx-Math.sin(listener.yaw)*dz)/Math.max(5,distance),-.9,.9)};
  }
  function updateSiren(unit,inside){
    const placement=unit?spatial(unit.g.position.x,unit.g.position.z):null;
    sirenLevel=placement?placement.gain*(inside?.035:.105):0;sirenPan=placement?.pan??0;
    if(sirenLevel<.0015){
      if(siren){
        const old=siren;siren=null;value(old.gain.gain,0,.04);
        old.osc.stop(context.currentTime+.18);old.mod.stop(context.currentTime+.18);
        old.osc.onended=()=>{for(const node of Object.values(old))node.disconnect();};
      }
      return;
    }
    if(!siren){
      const osc=context.createOscillator(),mod=context.createOscillator(),depth=context.createGain();
      const gain=context.createGain(),pan=context.createStereoPanner();
      osc.type='triangle';osc.frequency.value=800;mod.frequency.value=.92;depth.gain.value=220;gain.gain.value=0;
      mod.connect(depth).connect(osc.frequency);osc.connect(gain).connect(pan).connect(master);
      osc.start();mod.start();siren={osc,mod,depth,gain,pan};
    }
    value(siren.gain.gain,sirenLevel,.07);value(siren.pan.pan,sirenPan,.08);
  }
  function noiseLoop(frequency,gain,spatialized=false,type='lowpass'){
    const source=context.createBufferSource(),filter=context.createBiquadFilter(),volume=context.createGain();
    source.buffer=noiseBuffer;source.loop=true;filter.type=type;filter.frequency.value=frequency;volume.gain.value=gain;
    if(type==='bandpass')filter.Q.value=3.4;
    const pan=context.createStereoPanner();
    source.connect(filter).connect(volume).connect(pan).connect(master);source.start();return spatialized?{gain:volume.gain,pan:pan.pan,frequency:filter.frequency}:volume;
  }
  function unlock(){
    if(started){if(context?.state!=='running')context.resume().catch(()=>{});return;}
    const AudioContext=window.AudioContext||window.webkitAudioContext;if(!AudioContext)return;
    context=new AudioContext();master=context.createGain();master.gain.value=outputGain();
    const limiter=context.createDynamicsCompressor();limiter.threshold.value=-12;limiter.knee.value=18;limiter.ratio.value=4;
    meter=context.createAnalyser();meter.fftSize=256;master.connect(limiter).connect(meter).connect(context.destination);
    noiseBuffer=context.createBuffer(1,context.sampleRate*2,context.sampleRate);
    const data=noiseBuffer.getChannelData(0);for(let i=0;i<data.length;i++)data[i]=(Math.random()*2-1)*.7;
    windGain=noiseLoop(240,.005);seaGain=noiseLoop(850,0);rainBed=noiseLoop(2800,0,true);
    fountain=noiseLoop(2400,0,true);tires=noiseLoop(1100,0,true,'bandpass');
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
    if(!context||context.state!=='running'||muted||paused||volume===0)return;
    if(kind==='applause'){
      const placement=spatial(x,z),now=context.currentTime;
      if(placement.gain<.03)return;
      for(let i=0;i<4;i++){
        const source=context.createBufferSource(),filter=context.createBiquadFilter(),gain=context.createGain(),pan=context.createStereoPanner();
        source.buffer=noiseBuffer;filter.type='bandpass';filter.frequency.value=1500+i*110;filter.Q.value=.6;pan.pan.value=placement.pan;
        const start=now+i*.31;
        gain.gain.setValueAtTime(.0001,start);gain.gain.exponentialRampToValueAtTime(.115*placement.gain,start+.008);
        gain.gain.exponentialRampToValueAtTime(.0001,start+.095);
        source.connect(filter).connect(gain).connect(pan).connect(master);source.start(start);source.stop(start+.12);
        source.onended=()=>{source.disconnect();filter.disconnect();gain.disconnect();pan.disconnect();};
      }
      eventCounts.applause=(eventCounts.applause||0)+1;return;
    }
    if(kind.startsWith('busker-')){
      const placement=spatial(x,z),distance=Math.hypot(x-listener.x,z-listener.z);
      if(distance>26)return;
      placement.gain*=Math.max(0,1-distance/26)**1.4;
      const [,song,index]=kind.split('-'),beat=Number(index),phrase=Number(song),now=context.currentTime;
      // Original four-chord phrases, with a short plucked fundamental and overtone.
      const progression=[[146.83,196,174.61,130.81],[164.81,130.81,196,146.83],[130.81,174.61,146.83,196]][phrase%3];
      const bass=progression[Math.floor(beat/16)%4],offsets=beat%2?[1.5,2,2.5]:[1,1.25,1.5];
      offsets.forEach((ratio,i)=>{
        tone(bass*ratio,.056,now+i*.009,.22,placement,'triangle');
        tone(bass*ratio*2,.010,now+i*.009,.10,placement);
      });
      if(beat%4===0)tone(bass/2,.030,now,.18,placement,'triangle');
      eventCounts.busker=(eventCounts.busker||0)+1;return;
    }
    if(kind.startsWith('radio-')||kind==='harbor-winch'||kind==='drone-start'||kind==='film-reel'){
      const placement=spatial(x,z),now=context.currentTime;
      if(placement.gain<.013)return;
      if(kind.startsWith('radio-')){
        const program=Number(kind.slice(-1));
        const melodies=[[329.63,392,493.88,440,392,329.63],[293.66,370,440,554.37,440,370],[261.63,329.63,392,523.25,493.88,392]];
        const melody=melodies[program]||melodies[0],beat=[.24,.19,.30][program]||.24;
        melody.forEach((hz,i)=>{
          tone(hz,.063,now+i*beat,.29,placement,'triangle');
          if(i%2===0)tone(hz*.5,.048,now+i*beat,.40,placement);
        });
      }else if(kind==='harbor-winch'){
        tone(348,.06,now,.14,placement);tone(348,.06,now+.25,.14,placement);
        tone(78,.036,now+.34,.88,placement,'sawtooth');
      }else if(kind==='film-reel'){
        for(let i=0;i<6;i++)tone(105+i*4,.019,now+i*.068,.029,placement,'triangle');
      }else{tone(495,.041,now,.09,placement);tone(747,.030,now+.10,.12,placement);}
      eventCounts[kind]=(eventCounts[kind]||0)+1;return;
    }
    if(['phone','horn','bell','register','plate'].includes(kind))return signal(kind,x,z);
    const location=spatial(x,z);if(location.gain<.015)return;
    const now=context.currentTime,gain=context.createGain(),pan=context.createStereoPanner();
    if(kind==='pulse'){
      tone(920,.09,now,.11,location,'sine');tone(570,.065,now+.055,.12,location,'triangle');
    }
    const frequency={step:210,grass:850,door:360,automatic:1500,impact:170,break:1100,flutter:1600,pulse:3300,sidearm:2100,carbine:3200,reload:1700,splash:1150,stroke:1550,mooring:290,applause:1800}[kind]||300;
    const duration={step:.11,grass:.12,door:.22,automatic:.45,impact:.32,break:.28,flutter:.23,pulse:.11,sidearm:.17,carbine:.12,reload:.10,splash:.38,stroke:.21,mooring:.18}[kind]||.12;
    const level={step:.19,grass:.11,door:.14,automatic:.055,impact:.3,break:.18,flutter:.07,pulse:.055,sidearm:.27,carbine:.29,reload:.075,splash:.19,stroke:.09,mooring:.08}[kind]||.1;
    const source=context.createBufferSource(),filter=context.createBiquadFilter();source.buffer=noiseBuffer;
    filter.type=kind==='automatic'?'bandpass':'lowpass';filter.frequency.value=frequency;filter.Q.value=.6;
    gain.gain.setValueAtTime(.0001,now);gain.gain.exponentialRampToValueAtTime(level*location.gain,now+.008);
    gain.gain.exponentialRampToValueAtTime(.0001,now+duration);pan.pan.value=location.pan;
    source.connect(filter).connect(gain).connect(pan).connect(master);source.start();source.stop(now+duration+.02);
    source.onended=()=>{source.disconnect();filter.disconnect();gain.disconnect();pan.disconnect();};
    eventCounts[kind]=(eventCounts[kind]||0)+1;
  }
  // Separate envelopes make the public phone's two rings and the bell's
  // decaying partials recognizable without loud samples or sustained honking.
  function tone(frequency,level,start,duration,placement,type='sine'){
    const osc=context.createOscillator(),gain=context.createGain(),pan=context.createStereoPanner();
    osc.type=type;osc.frequency.value=frequency;pan.pan.value=placement.pan;
    gain.gain.setValueAtTime(.0001,start);gain.gain.exponentialRampToValueAtTime(Math.max(.00011,level*placement.gain),start+.008);
    gain.gain.exponentialRampToValueAtTime(.0001,start+duration);
    osc.connect(gain).connect(pan).connect(master);osc.start(start);osc.stop(start+duration+.02);
    osc.onended=()=>{osc.disconnect();gain.disconnect();pan.disconnect();};
  }
  function signal(kind,x,z){
    const now=context.currentTime,placement=spatial(x,z),key=kind==='phone'?`${kind}:${Math.round(x)}:${Math.round(z)}`:kind;
    if(now<(cooldowns.get(key)||0)||placement.gain<.015)return false;
    // Bound the source cache even when a vehicle travels across the whole map.
    for(const [id,until] of cooldowns)if(until<=now)cooldowns.delete(id);
    cooldowns.set(key,now+({phone:2.4,horn:.9,bell:.8,register:.18,plate:.09}[kind]));
    if(kind==='plate'){tone(1240,.10,now,.27,placement,'triangle');tone(2075,.047,now,.16,placement);}
    if(kind==='register'){tone(880,.045,now,.08,placement);tone(1174,.041,now+.085,.13,placement);}
    if(kind==='phone')for(const delay of [0,.42])for(const frequency of [650,870])tone(frequency,.045,now+delay,.28,placement,'triangle');
    if(kind==='horn')for(const frequency of [350,440])tone(frequency,.065,now,.4,placement,'triangle');
    if(kind==='bell')for(const [frequency,level,duration] of [[1800,.075,.7],[2820,.025,.38],[4100,.012,.18]])tone(frequency,level,now,duration,placement);
    eventCounts[kind]=(eventCounts[kind]||0)+1;return true;
  }
  function insects(time,night,inside){
    const placement=spatial(0,182),level=clamp((night-.4)/.6,0,1)*(inside?.15:1);
    insectLevel=level*placement.gain;
    if(time<insectAt)return;insectAt=time+.7+Math.random()*1.1;
    if(level<.01||placement.gain<.04||muted||volume===0)return;
    const now=context.currentTime;
    for(let i=0;i<3;i++)tone(3900+Math.random()*400,.018*level,now+i*.09,.045,placement);
    eventCounts.insects=(eventCounts.insects||0)+1;
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
  let rotorVoice=null,outboard=null;
  function watercraft(boat){
    if(!context||context.state!=='running')return;
    if(!boat){if(outboard){const old=outboard;outboard=null;value(old.gain.gain,0,.04);old.osc.stop(context.currentTime+.2);
      old.osc.onended=()=>{old.osc.disconnect();old.filter.disconnect();old.gain.disconnect();};}return;}
    if(!outboard){const osc=context.createOscillator(),filter=context.createBiquadFilter(),gain=context.createGain();
      osc.type='sawtooth';filter.type='lowpass';gain.gain.value=0;osc.connect(filter).connect(gain).connect(master);osc.start();outboard={osc,filter,gain};}
    const load=Math.abs(boat.speed)/boat.top;value(outboard.osc.frequency,34+load*68,.13);value(outboard.filter.frequency,115+load*285,.2);
    value(outboard.gain.gain,(.027+load*.045)*(boat.model==='sail'?.62:1),.12);
  }
  function aircraft(position,time){
    if(!context||context.state!=='running')return;
    if(!position){
      if(rotorVoice){
        const old=rotorVoice;rotorVoice=null;value(old.gain.gain,0,.04);
        old.low.stop(context.currentTime+.18);old.high.stop(context.currentTime+.18);
        old.low.onended=()=>Object.values(old).forEach(node=>node.disconnect());
      }
      return;
    }
    if(!rotorVoice){
      const low=context.createOscillator(),high=context.createOscillator(),filter=context.createBiquadFilter();
      const gain=context.createGain(),pan=context.createStereoPanner();low.type='sawtooth';high.type='triangle';
      filter.frequency.value=680;gain.gain.value=0;low.connect(filter);high.connect(filter);filter.connect(gain).connect(pan).connect(master);
      low.start();high.start();rotorVoice={low,high,filter,gain,pan};
    }
    const p=spatial(position.x,position.z);
    value(rotorVoice.low.frequency,112+Math.sin(time*14)*3,.07);value(rotorVoice.high.frequency,176+Math.sin(time*10)*4,.07);
    value(rotorVoice.gain.gain,p.gain*.023,.10);value(rotorVoice.pan.pan,p.pan);
  }
  return {
    aircraft,
    watercraft,
    play:transient,
    update({dt,time,position,yaw,vehicle,vehicles,walking,paved,inside,doors,night,weather,exposure=inside?.14:1}){
      listener={x:position.x,z:position.z,yaw};
      const distance=lastPosition?Math.hypot(position.x-lastPosition.x,position.z-lastPosition.z):0;
      lastPosition={x:position.x,z:position.z};
      rainExposure=exposure;
      rainLevel=(weather?.rain||0)*(.014+.092*exposure);
      if(!context||context.state!=='running'||paused)return;
      updateSiren(vehicles.find(c=>c.auto&&c.pursuitSiren&&!c.incident&&!c.impact),inside);
      const waterPlacement=spatial(-36,36);
      fountainLevel=.035*waterPlacement.gain*(inside?.15:1);
      value(fountain.gain,fountainLevel*(.92+.08*Math.sin(time*1.7)),.4);value(fountain.pan,waterPlacement.pan);
      insects(time,night,inside);
      value(rainBed.gain,rainLevel,.42);value(rainBed.frequency,440+exposure*2900,.38);
      value(rainBed.pan,Math.sin(yaw)*.16,.65);
      value(windGain.gain,(.0035+(weather?.windSpeed||1.8)*.0011)*(.16+.84*exposure),.5);
      const sea=clamp(1-Math.abs(position.z+155)/100,0,1)*(inside ? .2 : 1);
      value(seaGain.gain,sea*(.04+.028*(weather?.seaState||0))*(.8+.2*Math.sin(time*.43)),.6);
      if(walking&&distance<2){
        footDistance+=distance;
        if(footDistance>1.55&&time-lastStepAt>.24){footDistance%=1.55;lastStepAt=time;transient(paved?'step':'grass',position.x,position.z);}
      }else footDistance=0;
      const audible=vehicles.filter(c=>c.kind!=='bike'&&(c.auto||c===vehicle||c.incident)&&!c.incident?.actor)
        .map(c=>({c,d:Math.hypot(c.g.position.x-position.x,c.g.position.z-position.z)})).filter(p=>p.d<48)
        .sort((a,b)=>(a.c===vehicle?-100:a.d)-(b.c===vehicle?-100:b.d)).slice(0,voices.length);
      tireLevel=0;let tirePan=0,tireFrequency=1150;
      voices.forEach((voice,i)=>{
        const item=audible[i];if(!item){value(voice.gain.gain,0);voice.id=null;return;}
        const {c,d}=item,placement=spatial(c.g.position.x,c.g.position.z),speed=Math.abs(c.velocity);
        const scale=(c.kind==='bus'||c.role==='fire') ? .75 : 1;
        const slip=clamp(((c.tireSlip||0)-.14)*2.5,0,1),surface=c.tireSurface?.grip??1;
        const scrub=slip*clamp((speed-3)/7,0,1)*placement.gain*.13;
        if(scrub>tireLevel){tireLevel=scrub;tirePan=placement.pan;tireFrequency=surface<.8?520+speed*14:1150+speed*22+slip*350;}
        const enginePitch=c.engineRpm?22+c.engineRpm/50:32+speed*3.4;
        value(voice.oscillator.frequency,enginePitch*scale,.1);value(voice.filter.frequency,130+speed*22,.15);
        value(voice.gain.gain,(c===vehicle ? .06 : .025)*placement.gain*(inside ? .3 : 1)*(1+speed/30),.2);
        value(voice.pan.pan,c===vehicle?0:placement.pan);voice.id=c.debugId||`${c.kind||'car'}:${Math.round(d)}`;
      });
      value(tires.gain,tireLevel,.10);value(tires.pan,tirePan);value(tires.frequency,tireFrequency,.12);
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
      return {started,paused,volume,fountainLevel,insectLevel,tireLevel,sirenLevel,sirenPan,rainLevel,rainExposure,state:context?.state||'waiting-for-gesture',engines:voices.filter(v=>v.id).length,peak,events:{...eventCounts}};
    },
    setMuted(value){muted=!!value;updateOutput();},
    setVolume(value){volume=clamp(value,0,1);updateOutput();},
    setPaused(value){paused=value;updateOutput();},
    dispose(){removeEventListener('pointerdown',onGesture,{capture:true});removeEventListener('keydown',onGesture,{capture:true});context?.close();}
  };
}
