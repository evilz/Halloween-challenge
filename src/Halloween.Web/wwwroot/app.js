"use strict";
const $ = id => document.getElementById(id);
const canvas = $("board"), ctx = canvas.getContext("2d");
const descriptions = {hunter:"Repère ses cibles et tire dès qu'une ligne se libère.",wanderer:"Explore les couloirs et capture les spectres neutres.",survivor:"Garde ses distances et privilégie les chemins sûrs."};
let catalog = [], selected = new Set(["hunter","wanderer","survivor"]), state = null;
let frames = [], initial = null, frameIndex = 0, replay = false, running = false, busy = false, ownMatchId = null;
let loopGeneration = 0;
function element(tag, className, text) { const node = document.createElement(tag); if(className)node.className=className; if(text!=null)node.textContent=text; return node; }
function notice(message) { $("notice").hidden = !message; $("notice").textContent = message || ""; }
function showTab(name) {
  for(const tab of document.querySelectorAll(".tab"))tab.hidden=tab.id!==`tab-${name}`;
  for(const button of document.querySelectorAll(".nav"))button.classList.toggle("active",button.dataset.tab===name);
  if(name==="arena")draw();
}
document.querySelectorAll("[data-tab]").forEach(button=>button.addEventListener("click",()=>showTab(button.dataset.tab)));
async function api(path, method="GET", body) {
  const response = await fetch(`/api${path}`, {method,headers:body?{"Content-Type":"application/json"}:{},body:body?JSON.stringify(body):undefined});
  if(!response.ok) { let error; try{error=await response.json();}catch{} throw new Error(error?.error || (response.status===429?"Trop de requêtes. Réduisez la vitesse et réessayez dans un instant.":`Le serveur répond ${response.status}.`)); }
  return response.status===204?null:response.json();
}
async function loadBots() {
  catalog=await api("/bots"); $("bot-count").textContent=catalog.length;
  $("bot-selection").replaceChildren(); $("bot-cards").replaceChildren();
  for(const bot of catalog) {
    const row=element("label","bot-checkbox"), checkbox=document.createElement("input");
    checkbox.type="checkbox"; checkbox.value=bot.id; checkbox.checked=selected.has(bot.id);
    checkbox.addEventListener("change",()=>{
      if(checkbox.checked && selected.size>=9) {checkbox.checked=false; notice("L'arène accueille jusqu'à 9 bots.");return;}
      checkbox.checked?selected.add(bot.id):selected.delete(bot.id); updateSelected();
    });
    row.append(checkbox,element("span","bot-dot",bot.name.slice(0,1).toUpperCase()),element("span",null,bot.name),element("small",null,bot.kind==="builtin"?"INTÉGRÉ":"API"));
    $("bot-selection").append(row);
    const card=element("article","panel bot-card");
    card.append(element("span","bot-dot",bot.name.slice(0,1).toUpperCase()),element("h3",null,bot.name),element("p",null,descriptions[bot.id]||bot.url),element("span","badge",bot.kind==="builtin"?"BOT INTÉGRÉ":"API CONNECTÉE"));
    $("bot-cards").append(card);
  }
  updateSelected();
}
function updateSelected() { $("selected-count").textContent=`${selected.size} / 9`; }
function stop() {running=false;loopGeneration++;updateControls();}
function updateControls() {
  $("new-match").disabled=busy; $("first-match").disabled=busy;
  $("play").disabled=(busy&&!running)||!state||(!replay&&state.finished&&frameIndex===frames.length-1)||(replay&&frameIndex===frames.length-1);
  $("step").disabled=busy||!state||(!replay&&state.finished&&frameIndex===frames.length-1)||(replay&&frameIndex===frames.length-1);
  $("play").textContent=running?"Ⅱ Pause":"▶ Lancer";
  $("export").disabled=!state||busy; $("import").disabled=busy;
  $("timeline").disabled=busy||frames.length<2;
  $("match-status").textContent=busy?"DÉCISION DES BOTS…":replay?"LECTURE DU REPLAY":state?.finished?"PARTIE TERMINÉE":running?"PARTIE EN COURS":state?"PARTIE EN PAUSE":"PRÊT À JOUER";
  $("status-dot").classList.toggle("paused",!running);
}
function pack(snapshot) {const {walls,options,...frame}=snapshot;return frame;}
function displayFrame(index) {
  frameIndex=Math.max(0,Math.min(index,frames.length-1));
  state={...initial,...frames[frameIndex]}; render();
}
function numeric(id) {
  const input=$(id); if(!input.checkValidity()||input.value===""||!Number.isInteger(Number(input.value)))throw new Error(`Valeur invalide : ${input.parentElement.textContent.trim()}.`);
  return Number(input.value);
}
async function newMatch() {
  if(busy)return; stop(); notice(""); busy=true;updateControls();
  try {
    const options={width:numeric("width"),height:numeric("height"),maxTurns:numeric("turns"),seed:numeric("seed"),visionRadius:numeric("radius"),reloadTurns:numeric("reload"),respawnTurns:numeric("respawn"),initialEnemies:numeric("enemies"),enemySpawnEvery:numeric("spawn"),neutralTurns:numeric("neutral"),maxEnemies:numeric("max-enemies")};
    if(!selected.size)throw new Error("Sélectionnez au moins un bot pour créer la partie.");
    // Release this browser's previous match before replacing it.
    if(ownMatchId) {await api(`/matches/${ownMatchId}`,"DELETE");ownMatchId=null;}
    state=await api("/matches","POST",{options,botIds:[...selected]});ownMatchId=state.id;
    replay=false;initial=state;frames=[pack(state)];frameIndex=0;render();
  } catch(error){notice(error.message);} finally{busy=false;updateControls();}
}
async function advance() {
  if(busy||!state)return false;
  if(replay||frameIndex<frames.length-1) {if(frameIndex>=frames.length-1)return false; displayFrame(frameIndex+1);return true;}
  if(state.finished)return false;
  busy=true;updateControls();
  try {const next=await api(`/matches/${ownMatchId}/step`,"POST");frames.push(pack(next));displayFrame(frames.length-1);return true;}
  catch(error){notice(error.message);stop();return false;}
  finally{busy=false;updateControls();}
}
async function play() {
  if(running){stop();return;}
  running=true; const generation=++loopGeneration;updateControls();
  while(running&&generation===loopGeneration) {
    const started=performance.now();if(!await advance())break;
    // A pause during an HTTP request must prevent the next turn.
    if(!running||generation!==loopGeneration)break;
    const delay=Math.max(0,Number($("speed").value)-(performance.now()-started));
    await new Promise(resolve=>setTimeout(resolve,delay));
  }
  if(generation===loopGeneration)stop();
}
$("new-match").addEventListener("click",newMatch);$("first-match").addEventListener("click",newMatch);
$("step").addEventListener("click",()=>{stop();void advance();});$("play").addEventListener("click",play);
$("timeline").addEventListener("input",()=>{stop();displayFrame(Number($("timeline").value));});
$("vision").addEventListener("change",draw);
$("bot-form").addEventListener("submit",async event=>{
  event.preventDefault(); const result=$("bot-result");$("register").disabled=true;result.className="connection-result";result.textContent="Vérification de POST /name…";
  try{const bot=await api("/bots","POST",{url:$("bot-url").value});if(selected.size<9)selected.add(bot.id);await loadBots();result.textContent=`${bot.name} est connecté. Sélectionnez-le dans l'arène pour votre prochaine partie.`;result.classList.add("success");}
  catch(error){result.textContent=error.message;result.classList.add("error");}finally{$("register").disabled=false;}
});
$("export").addEventListener("click",()=>{
  if(!state)return; const blob=new Blob([JSON.stringify({version:2,initial,frames})],{type:"application/json"});
  const url=URL.createObjectURL(blob),link=document.createElement("a");link.href=url;link.download=`hollow-arena-${initial.options.seed}.json`;link.click();setTimeout(()=>URL.revokeObjectURL(url),1000);
});
$("import").addEventListener("click",()=>$("replay-file").click());
function validateReplay(value) {
  if(!value||![1,2].includes(value.version)||!Array.isArray(value.frames)||value.frames.length<1||value.frames.length>5001)throw new Error("Format de replay non reconnu.");
  const origin=value.version===1?value.frames[0]:value.initial;
  const o=origin?.options;
  if(!o||!Number.isInteger(o.width)||o.width<10||o.width>50||!Number.isInteger(o.height)||o.height<10||o.height>25||!Number.isInteger(o.maxTurns)||o.maxTurns<50||o.maxTurns>5000||!Number.isInteger(o.visionRadius)||o.visionRadius<1||o.visionRadius>10)throw new Error("Dimensions du replay invalides.");
  const position=p=>p&&Number.isInteger(p.x)&&Number.isInteger(p.y)&&p.x>=0&&p.y>=0&&p.x<o.width&&p.y<o.height;
  if(!Array.isArray(origin.walls)||origin.walls.length>1250||!origin.walls.every(position))throw new Error("Carte du replay invalide.");
  for(const frame of value.frames) {
    if(!Number.isInteger(frame.turn)||frame.turn<0||frame.turn>5000||!Array.isArray(frame.players)||frame.players.length>9||!Array.isArray(frame.enemies)||frame.enemies.length>100||!Array.isArray(frame.shots)||frame.shots.length>9||!Array.isArray(frame.events)||frame.events.length>60)throw new Error("Un tour du replay est invalide.");
    if(!frame.players.every(p=>position(p.position)&&typeof p.id==="string"&&typeof p.name==="string"&&p.name.length<=40&&Number.isFinite(p.score)&&typeof p.color==="string"&&/^#[0-9a-f]{6}$/i.test(p.color))||!frame.enemies.every(e=>position(e.position))||!frame.shots.every(s=>position(s.from)&&position(s.to))||!frame.events.every(e=>typeof e.message==="string"&&e.message.length<500&&typeof e.kind==="string"&&Number.isInteger(e.turn)))throw new Error("Entités du replay invalides.");
  }
  return {origin,frames:value.frames.map(pack)};
}
$("replay-file").addEventListener("change",async()=>{
  const file=$("replay-file").files[0];if(!file)return;stop();
  try{if(file.size>32*1024*1024)throw new Error("Le replay dépasse 32 Mo.");const validated=validateReplay(JSON.parse(await file.text()));initial=validated.origin;frames=validated.frames;replay=true;notice("");displayFrame(0);}
  catch(error){notice(`Import impossible : ${error.message}`);}finally{$("replay-file").value="";}
});
function render() {
  $("empty-board").hidden=Boolean(state);if(!state){draw();return;}
  $("turn").textContent=state.turn;$("max-turns").textContent=` / ${state.options.maxTurns}`;$("seed-label").textContent=`Graine ${state.options.seed}`;
  $("timeline").max=frames.length-1;$("timeline").value=frameIndex;$("timeline-label").textContent=`${state.turn} / ${frames.at(-1).turn}`;
  const vision=$("vision"),previous=vision.value;vision.replaceChildren(new Option("Spectateur · carte entière","all"));
  state.players.forEach(p=>vision.add(new Option(p.name,p.id)));vision.value=state.players.some(p=>p.id===previous)?previous:"all";
  $("ranking").replaceChildren();
  [...state.players].sort((a,b)=>b.score-a.score||b.kills-a.kills||a.deaths-b.deaths).forEach((p,index)=>{
    const row=element("div","ranking-row"),name=element("div",`rank-name${p.alive?"":" dead-name"}`),avatar=document.createElement("canvas");
    avatar.width=58;avatar.height=58;avatar.className="rank-avatar";pumpkin(avatar.getContext("2d"),29,29,22,p.color,index);
    name.append(element("strong",null,p.name),element("small",null,p.alive?`${p.kills} élim. · ${p.reloadRemaining?`recharge ${p.reloadRemaining}`:"tir prêt"}`:`${p.deaths} morts · retour ${p.respawnRemaining}`));
    row.append(element("span","rank-number",String(index+1).padStart(2,"0")),avatar,name,element("span","rank-score",p.score));$("ranking").append(row);
  });
  $("events").replaceChildren();for(const e of [...state.events].reverse()){
    const row=element("div",`event-row event-${e.kind}`);row.append(element("span","event-turn",`T ${String(e.turn).padStart(3,"0")}`),element("span",null,e.message));$("events").append(row);
  }
  $("event-count").textContent=`${state.events.length} ÉVÉNEMENTS RÉCENTS`;updateControls();draw();
}
function pumpkin(context,x,y,r,color,index) {
  context.save();context.translate(x,y);context.shadowColor=color;context.shadowBlur=r*.45;
  context.fillStyle=color;context.beginPath();context.ellipse(-r*.24,0,r*.57,r*.65,0,0,Math.PI*2);context.ellipse(r*.24,0,r*.57,r*.65,0,0,Math.PI*2);context.fill();context.shadowBlur=0;
  context.strokeStyle="#35263255";context.lineWidth=r*.07;context.beginPath();context.ellipse(0,0,r*.32,r*.65,0,0,Math.PI*2);context.stroke();
  context.fillStyle="#6f9575";context.fillRect(-r*.08,-r*.85,r*.17,r*.23);
  context.fillStyle="#1b202a";context.beginPath();context.moveTo(-r*.6,-r*.09);context.lineTo(-r*.2,-r*.32);context.lineTo(-r*.14,r*.01);context.closePath();context.moveTo(r*.6,-r*.09);context.lineTo(r*.2,-r*.32);context.lineTo(r*.14,r*.01);context.closePath();context.fill();
  context.beginPath();context.moveTo(-r*.4,r*.2);context.lineTo(-r*.13,r*.29);context.lineTo(0,r*.19);context.lineTo(r*.13,r*.29);context.lineTo(r*.4,r*.2);context.lineTo(r*.22,r*.43);context.lineTo(-r*.22,r*.43);context.closePath();context.fill();
  if(index%3===1){context.fillStyle="#343242";context.beginPath();context.moveTo(-r*.65,-r*.55);context.lineTo(r*.03,-r*1.25);context.lineTo(r*.45,-r*.55);context.fill();context.fillRect(-r*.8,-r*.57,r*1.6,r*.12);}
  if(index%3===2){context.strokeStyle="#eee1be";context.lineWidth=r*.09;context.beginPath();context.arc(0,-r*.78,r*.26,Math.PI,0);context.stroke();}
  context.restore();
}
function ghost(x,y,r,neutral) {
  ctx.save();ctx.translate(x,y);const color=neutral?"#70dcca":"#b5a2f1";ctx.shadowColor=color;ctx.shadowBlur=r*.7;
  ctx.fillStyle=color;ctx.beginPath();ctx.moveTo(-r*.65,r*.6);ctx.lineTo(-r*.65,-r*.1);ctx.bezierCurveTo(-r*.65,-r*1.05,r*.65,-r*1.05,r*.65,-r*.1);ctx.lineTo(r*.65,r*.6);ctx.lineTo(r*.3,r*.42);ctx.lineTo(0,r*.7);ctx.lineTo(-r*.3,r*.42);ctx.closePath();ctx.fill();ctx.shadowBlur=0;
  ctx.fillStyle="#232739";ctx.beginPath();ctx.ellipse(-r*.23,-r*.1,r*.1,r*.17,0,0,Math.PI*2);ctx.ellipse(r*.23,-r*.1,r*.1,r*.17,0,0,Math.PI*2);ctx.fill();
  ctx.beginPath();ctx.arc(0,r*.22,r*.1,0,Math.PI*2);ctx.fill();ctx.restore();
}
function draw() {
  const box=canvas.getBoundingClientRect();if(!box.width)return;const dpr=Math.min(window.devicePixelRatio||1,2);
  const width=state?.options.width||25,height=state?.options.height||17;
  canvas.parentElement.style.aspectRatio=`${width}/${height}`;const displayWidth=box.width,displayHeight=displayWidth*height/width;
  canvas.width=Math.round(displayWidth*dpr);canvas.height=Math.round(displayHeight*dpr);ctx.setTransform(dpr,0,0,dpr,0,0);
  const tile=displayWidth/width;ctx.fillStyle="#10141b";ctx.fillRect(0,0,displayWidth,displayHeight);
  const walls=new Set((state?.walls||[]).map(p=>`${p.x},${p.y}`));
  for(let y=0;y<height;y++)for(let x=0;x<width;x++){
    const px=x*tile,py=y*tile;const wall=walls.has(`${x},${y}`);
    ctx.fillStyle=wall?((x+y)%3===0?"#363d50":"#2d3445"):((x+y)%2===0?"#161c26":"#141a23");
    ctx.fillRect(px+1,py+1,tile-2,tile-2);
    if(wall){ctx.fillStyle="#414a60";ctx.fillRect(px+2,py+2,tile-4,Math.max(1,tile*.12));ctx.fillStyle="#222837";ctx.fillRect(px+2,py+tile*.82,tile-4,tile*.12);ctx.strokeStyle="#242a38";ctx.lineWidth=1;ctx.beginPath();ctx.moveTo(px+tile*.45,py+tile*.17);ctx.lineTo(px+tile*.45,py+tile*.48);ctx.lineTo(px+tile*.78,py+tile*.48);ctx.stroke();}
    else if((x*19+y*11)%23===0){ctx.fillStyle="#38433877";ctx.fillRect(px+tile*.3,py+tile*.7,tile*.08,tile*.1);ctx.fillRect(px+tile*.45,py+tile*.6,tile*.08,tile*.2);}
  }
  if(!state)return;
  const focused=state.players.find(p=>p.id===$("vision").value),radius=state.options.visionRadius;
  const visible=p=>!focused||(Math.abs(p.x-focused.position.x)<=radius&&Math.abs(p.y-focused.position.y)<=radius);
  for(const shot of state.shots){if(!visible(shot.from)||!visible(shot.to))continue;ctx.save();ctx.strokeStyle="#ffce80";ctx.shadowColor="#ffb454";ctx.shadowBlur=12;ctx.lineWidth=Math.max(2,tile*.09);ctx.beginPath();ctx.moveTo((shot.from.x+.5)*tile,(shot.from.y+.5)*tile);ctx.lineTo((shot.to.x+.5)*tile,(shot.to.y+.5)*tile);ctx.stroke();ctx.restore();}
  for(const e of state.enemies)if(visible(e.position))ghost((e.position.x+.5)*tile,(e.position.y+.5)*tile,tile*.43,e.neutral);
  state.players.forEach((p,index)=>{
    if(!visible(p.position))return;const x=(p.position.x+.5)*tile,y=(p.position.y+.5)*tile;
    if(p.alive){pumpkin(ctx,x,y,tile*.44,p.color,index);ctx.fillStyle="#dbe0eccc";ctx.font=`${Math.max(8,tile*.22)}px system-ui`;ctx.textAlign="center";ctx.fillText(String(index+1),x,y+tile*.42);}
    else{ctx.strokeStyle=p.color+"80";ctx.lineWidth=Math.max(1,tile*.06);ctx.beginPath();ctx.moveTo(x-tile*.15,y-tile*.15);ctx.lineTo(x+tile*.15,y+tile*.15);ctx.moveTo(x+tile*.15,y-tile*.15);ctx.lineTo(x-tile*.15,y+tile*.15);ctx.stroke();}
  });
  if(focused){for(let y=0;y<height;y++)for(let x=0;x<width;x++)if(!visible({x,y})){ctx.fillStyle="#090c13ed";ctx.fillRect(x*tile,y*tile,tile,tile);}ctx.strokeStyle=focused.color+"bb";ctx.lineWidth=1;const x1=Math.max(0,focused.position.x-radius),y1=Math.max(0,focused.position.y-radius),x2=Math.min(width-1,focused.position.x+radius),y2=Math.min(height-1,focused.position.y+radius);ctx.strokeRect(x1*tile+.5,y1*tile+.5,(x2-x1+1)*tile-1,(y2-y1+1)*tile-1);}
}
new ResizeObserver(()=>draw()).observe(canvas.parentElement);
void loadBots().catch(error=>notice(`Connexion au serveur impossible : ${error.message}`));render();
