using System;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace OpenTerritory
{
    // Native uGUI presentation. World labels are projected into a non-blocking overlay;
    // only the explicit action button consumes pointer input.
    public sealed partial class CityCanvasUI : MonoBehaviour
    {
        CityUI owner; CityContext ctx; CityUI.Options options;
        const float SettingsHeight = 622;
        Canvas canvas; RectTransform root, panel, prompt, speech;
        GameObject settings, touch;
        RectTransform aimReticle; readonly RectTransform[] aimTicks=new RectTransform[4];
        Font font; Text actionText, actionKeyText, speechText, fullscreenText;
        GameObject[] pages = new GameObject[3]; Button[] tabs = new Button[3], quality = new Button[3];
        Slider volume, effects, ambience, music, sensitivity; Text volumeText, effectsText, ambienceText, musicText, sensitivityText;
        Toggle invert, touchToggle, vignetteToggle, ambientOcclusionToggle, sunRaysToggle; RectTransform stickKnob;
        Text[] touchLabels = new Text[9]; int selectedTab;
        Button touchHackNext; Text touchHackerCaption;
        Button touchCrouch; Text touchCrouchCaption;
        readonly Color ink = new Color(.88f,.94f,.92f), muted = new Color(.61f,.75f,.75f), accent = new Color(.37f,.81f,.75f);
        readonly Color deep = new Color(.035f,.09f,.12f,.97f), surface = new Color(.09f,.19f,.22f,.98f);

        public void Initialize(CityUI ui, CityContext context, CityUI.Options values)
        {
            owner=ui;ctx=context;options=values;font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            root=Rect("Interface da Baía",null); root.SetParent(transform,false);
            canvas=root.gameObject.AddComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceOverlay;canvas.sortingOrder=30;
            var scaler=root.gameObject.AddComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution=new Vector2(1280,800);scaler.matchWidthOrHeight=1;
            root.gameObject.AddComponent<GraphicRaycaster>();
            if(!EventSystem.current){var e=new GameObject("Eventos da interface",typeof(EventSystem),typeof(StandaloneInputModule));e.transform.SetParent(transform,false);}
            BuildStatusHud();BuildSettings();BuildWorldLabels();BuildTouch();BuildReticle();BuildHackingUI();Bind(values);ShowSettings(false);
        }
        RectTransform Rect(string name,Transform parent)
        {
            var r=new GameObject(name,typeof(RectTransform)).GetComponent<RectTransform>();if(parent)r.SetParent(parent,false);return r;
        }
        void Box(RectTransform r,float x,float y,float w,float h)
        {r.anchorMin=r.anchorMax=new Vector2(0,1);r.pivot=new Vector2(0,1);r.anchoredPosition=new Vector2(x,-y);r.sizeDelta=new Vector2(w,h);if(w>=300){r.anchorMax=new Vector2(1,1);r.sizeDelta=new Vector2(-x,h);}}
        void Stretch(RectTransform r,float left=0,float right=0,float top=0,float bottom=0)
        {r.anchorMin=Vector2.zero;r.anchorMax=Vector2.one;r.offsetMin=new Vector2(left,bottom);r.offsetMax=new Vector2(-right,-top);}
        Image Paint(RectTransform r,Color c,bool raycast=false)
        {var i=r.gameObject.AddComponent<Image>();i.color=c;i.raycastTarget=raycast;return i;}
        Text Label(string name,Transform parent,string text,int size,Color color,TextAnchor alignment=TextAnchor.MiddleLeft)
        {
            var r=Rect(name,parent);var t=r.gameObject.AddComponent<CityEnglishText>();t.font=font;t.fontSize=size;t.text=text;t.color=color;
            t.alignment=alignment;t.raycastTarget=false;t.supportRichText=false;t.horizontalOverflow=HorizontalWrapMode.Wrap;t.verticalOverflow=VerticalWrapMode.Truncate;return t;
        }
        Button Button(string name,Transform parent,string text,Action action,out Text caption)
        {
            var r=Rect(name,parent);var image=Paint(r,surface,true);var b=r.gameObject.AddComponent<Button>();b.targetGraphic=image;
            var colors=b.colors;colors.normalColor=Color.white;colors.highlightedColor=new Color(1.3f,1.5f,1.4f);colors.selectedColor=colors.highlightedColor;colors.pressedColor=accent;colors.fadeDuration=.08f;b.colors=colors;
            caption=Label("Texto",r,text,17,ink,TextAnchor.MiddleCenter);Stretch(caption.rectTransform,8,8,2,2);
            b.onClick.AddListener(()=>action());return b;
        }
        Button Button(string name,Transform parent,string text,Action action){return Button(name,parent,text,action,out _);}
        void Row(RectTransform r,int index,int count,float y,float h,float gap=6)
        {r.anchorMin=new Vector2((float)index/count,1);r.anchorMax=new Vector2((float)(index+1)/count,1);r.pivot=new Vector2(.5f,1);r.offsetMin=new Vector2(index==0?0:gap/2,-y-h);r.offsetMax=new Vector2(index==count-1?0:-gap/2,-y);}
        void BuildSettings()
        {
            var shade=Rect("Configurações",root);Stretch(shade);Paint(shade,new Color(.015f,.04f,.055f,.72f),true);settings=shade.gameObject;
            panel=Rect("Painel de configurações",shade);panel.anchorMin=panel.anchorMax=new Vector2(.5f,.5f);panel.pivot=new Vector2(.5f,.5f);panel.sizeDelta=new Vector2(680,SettingsHeight);Paint(panel,deep);
            var stripe=Rect("Linha do mar",panel);stripe.anchorMin=new Vector2(0,1);stripe.anchorMax=Vector2.one;stripe.offsetMin=new Vector2(0,-4);stripe.offsetMax=Vector2.zero;Paint(stripe,accent);
            var content=Rect("Conteúdo",panel);Stretch(content,28,28,22,22);
            var title=Label("Título",content,"BAÍA DO SOL",30,ink);Box(title.rectTransform,0,0,400,43);
            var sub=Label("Subtítulo",content,"Configurações",16,muted);Box(sub.rectTransform,0,46,320,26);
            var close=Button("Fechar",content,"×",()=>owner.SetSettings(false));var cr=(RectTransform)close.transform;cr.anchorMin=cr.anchorMax=new Vector2(1,1);cr.pivot=new Vector2(1,1);cr.anchoredPosition=Vector2.zero;cr.sizeDelta=new Vector2(42,42);
            string[] names={"Imagem","Som","Controles"};
            for(int i=0;i<3;i++){int index=i;tabs[i]=Button(names[i],content,names[i],()=>SelectTab(index));Row((RectTransform)tabs[i].transform,i,3,92,42);var page=Rect(names[i]+" · opções",content);Stretch(page,0,0,157,66);pages[i]=page.gameObject;}
            BuildImage(pages[0].transform);BuildSound(pages[1].transform);BuildControls(pages[2].transform);
            var footer=Rect("Rodapé",content);footer.anchorMin=Vector2.zero;footer.anchorMax=new Vector2(1,0);footer.pivot=Vector2.zero;footer.sizeDelta=new Vector2(0,44);
            var reset=Button("Restaurar padrões",footer,"Restaurar padrões",()=>owner.RestoreDefaults());Row((RectTransform)reset.transform,0,2,0,44);
            var resume=Button("Voltar ao jogo",footer,"Voltar ao jogo · Esc",()=>owner.SetSettings(false));Row((RectTransform)resume.transform,1,2,0,44);
        }
        void BuildImage(Transform parent)
        {
            var l=Label("Qualidade",parent,"Qualidade da imagem",18,ink);Box(l.rectTransform,0,0,350,28);
            string[] names={"Leve","Equilibrada","Alta"};
            for(int i=0;i<3;i++){int index=i;quality[i]=Button("Qualidade "+names[i],parent,names[i],()=>{options.quality=index;owner.Save();RefreshQuality();});Row((RectTransform)quality[i].transform,i,3,38,44);}
            var fov=Label("Campo de visão fixo",parent,"Campo de visão    "+CityUI.FixedFieldOfView.ToString("0.#",System.Globalization.CultureInfo.GetCultureInfo("en-US"))+"°",20,ink);Box(fov.rectTransform,0,110,450,32);
            var hint=Label("Informação da câmera",parent,"Ângulo fixo durante a exploração.",15,muted);Box(hint.rectTransform,0,146,500,26);
            var full=Button("Tela cheia",parent,"",()=>owner.ToggleFullscreen(),out fullscreenText);Row((RectTransform)full.transform,0,1,178,46);
            vignetteToggle=Check("Vinheta suave",parent,228,v=>{options.vignette=v;owner.Save();});
            ambientOcclusionToggle=Check("Oclusão ambiente",parent,272,v=>{options.ambientOcclusion=v;owner.Save();});
            sunRaysToggle=Check("Raios solares",parent,316,v=>{options.sunRays=v;owner.Save();});
        }
        Slider Slider(string name,Transform parent,float y,float min,float max,Action<float> changed,out Text caption,bool compact=false)
        {
            caption=Label(name+" · valor",parent,name,compact?17:18,ink);Box(caption.rectTransform,0,y,550,compact?26:30);
            var r=Rect(name,parent);Row(r,0,1,y+(compact?30:40),compact?28:34);Paint(r,new Color(0,0,0,0),true);
            var track=Rect("Trilho",r);Stretch(track,10,10,compact?11:14,compact?11:14);Paint(track,new Color(.15f,.26f,.29f));
            var area=Rect("Preenchimento",r);Stretch(area,10,10,compact?11:14,compact?11:14);var fill=Rect("Cor",area);Stretch(fill);Paint(fill,accent);
            var handleArea=Rect("Área do controle",r);Stretch(handleArea,10,10);var handle=Rect("Controle",handleArea);handle.sizeDelta=new Vector2(20,compact?26:30);Paint(handle,ink);
            var slider=r.gameObject.AddComponent<Slider>();slider.minValue=min;slider.maxValue=max;slider.fillRect=fill;slider.handleRect=handle;slider.targetGraphic=handle.GetComponent<Image>();slider.direction=UnityEngine.UI.Slider.Direction.LeftToRight;
            slider.onValueChanged.AddListener(v=>changed(v));return slider;
        }
        void BuildSound(Transform parent)
        {
            volume=Slider("Volume",parent,0,0,1,v=>{options.volume=v;volumeText.text="Volume    "+Mathf.RoundToInt(v*100)+"%";owner.Save();},out volumeText,true);
            effects=Slider("Efeitos",parent,62,0,1,v=>{options.effects=v;effectsText.text="Efeitos    "+Mathf.RoundToInt(v*100)+"%";owner.Save();},out effectsText,true);
            ambience=Slider("Ambiente",parent,124,0,1,v=>{options.ambience=v;ambienceText.text="Ambiente    "+Mathf.RoundToInt(v*100)+"%";owner.Save();},out ambienceText,true);
            music=Slider("Música e rádio",parent,186,0,1,v=>{options.music=v;musicText.text="Música e rádio    "+Mathf.RoundToInt(v*100)+"%";owner.Save();},out musicText,true);
            var theme=Label("Tema do menu",parent,"MÚSICA DO MENU",13,muted);Box(theme.rectTransform,0,251,350,20);
            var credit=Button("Crédito da música",parent,CityMenuMusic.Title+" · "+CityMenuMusic.Artist,()=>Application.OpenURL(CityMenuMusic.SourceUrl),out var creditText);
            var cr=(RectTransform)credit.transform;cr.anchorMin=new Vector2(0,1);cr.anchorMax=new Vector2(.72f,1);cr.pivot=new Vector2(0,1);cr.offsetMin=new Vector2(0,-309);cr.offsetMax=new Vector2(-3,-275);
            creditText.resizeTextForBestFit=true;creditText.resizeTextMinSize=12;creditText.resizeTextMaxSize=16;
            var license=Button("Licença da música",parent,"CC BY 4.0 ↗",()=>Application.OpenURL(CityMenuMusic.LicenseUrl),out var licenseText);
            var lr=(RectTransform)license.transform;lr.anchorMin=new Vector2(.72f,1);lr.anchorMax=Vector2.one;lr.pivot=new Vector2(0,1);lr.offsetMin=new Vector2(3,-309);lr.offsetMax=new Vector2(0,-275);
            licenseText.resizeTextForBestFit=true;licenseText.resizeTextMinSize=12;licenseText.resizeTextMaxSize=15;
        }
        Toggle Check(string name,Transform parent,float y,Action<bool> changed)
        {
            var r=Rect(name,parent);Row(r,0,1,y,36);var bg=Rect("Caixa",r);Box(bg,0,5,26,26);Paint(bg,surface,true);var tick=Rect("Marcado",bg);Stretch(tick,6,6,6,6);Paint(tick,accent);
            var l=Label("Texto",r,name,17,ink);Stretch(l.rectTransform,40);
            var toggle=r.gameObject.AddComponent<Toggle>();toggle.targetGraphic=bg.GetComponent<Image>();toggle.graphic=tick.GetComponent<Image>();toggle.onValueChanged.AddListener(v=>changed(v));return toggle;
        }
        void BuildControls(Transform parent)
        {
            sensitivity=Slider("Sensibilidade da câmera",parent,0,.4f,1.8f,v=>{options.sensitivity=v;sensitivityText.text="Sensibilidade da câmera    "+Mathf.RoundToInt(v*100)+"%";owner.Save();},out sensitivityText);
            invert=Check("Inverter eixo vertical",parent,92,v=>{options.invertY=v;owner.Save();});
            touchToggle=Check("Mostrar controles de toque",parent,136,v=>{options.touch=v;owner.Save();});
            var help=Label("Atalhos",parent,"WASD · move   /   Space · jump or brake   /   Shift · run\nC · crouch or stand   /   Q · talk   /   Hold Q · emotes   /   E · interact\nF · connect   /   V · drone   /   1–3 · equipment   /   X · holster\nUnarmed: click or B · punch   /   Right click or G · block\nR · reload   /   H · horn   /   M · map\nMove mouse · camera   /   Hold Alt · cursor",14,muted);Box(help.rectTransform,0,194,600,128);
            help.rectTransform.anchorMax=new Vector2(1,1);help.rectTransform.sizeDelta=new Vector2(0,112);
        }
        public void Bind(CityUI.Options values)
        {
            options=values;volume.SetValueWithoutNotify(options.volume);sensitivity.SetValueWithoutNotify(options.sensitivity);
            effects.SetValueWithoutNotify(options.effects);ambience.SetValueWithoutNotify(options.ambience);music.SetValueWithoutNotify(options.music);
            effectsText.text="Efeitos    "+Mathf.RoundToInt(options.effects*100)+"%";ambienceText.text="Ambiente    "+Mathf.RoundToInt(options.ambience*100)+"%";musicText.text="Música e rádio    "+Mathf.RoundToInt(options.music*100)+"%";
            volumeText.text="Volume    "+Mathf.RoundToInt(options.volume*100)+"%";sensitivityText.text="Sensibilidade da câmera    "+Mathf.RoundToInt(options.sensitivity*100)+"%";
            invert.SetIsOnWithoutNotify(options.invertY);touchToggle.SetIsOnWithoutNotify(options.touch);vignetteToggle.SetIsOnWithoutNotify(options.vignette);ambientOcclusionToggle.SetIsOnWithoutNotify(options.ambientOcclusion);sunRaysToggle.SetIsOnWithoutNotify(options.sunRays);RefreshQuality();RefreshFullscreen();SelectTab(selectedTab);
        }
        void RefreshQuality()
        {
            for(int i=0;i<3;i++)quality[i].GetComponent<Image>().color=i==options.quality?new Color(.18f,.43f,.43f):surface;
            ambientOcclusionToggle.interactable=options.quality>0;
            var label=ambientOcclusionToggle.GetComponentInChildren<Text>();
            label.text=options.quality>0?"Oclusão ambiente":"Oclusão ambiente · Equilibrada/Alta";
            label.color=options.quality>0?ink:muted;
            sunRaysToggle.interactable=options.quality>0;
            var raysLabel=sunRaysToggle.GetComponentInChildren<Text>();
            raysLabel.text=options.quality>0?"Raios solares":"Raios solares · Equilibrada/Alta";
            raysLabel.color=options.quality>0?ink:muted;
        }
        public void RefreshFullscreen(){fullscreenText.text=(Screen.fullScreen?"Sair da tela cheia":"Tela cheia")+" · Alt + Enter";}
        void SelectTab(int index)
        {
            selectedTab=index;for(int i=0;i<3;i++){pages[i].SetActive(i==index);tabs[i].GetComponent<Image>().color=i==index?new Color(.18f,.43f,.43f):surface;}
            if(settings.activeSelf)EventSystem.current?.SetSelectedGameObject(tabs[index].gameObject);
        }
        public void ShowSettings(bool value)
        {
            settings.SetActive(value);prompt.gameObject.SetActive(false);speech.gameObject.SetActive(false);SetTouchVisible(owner.TouchEnabled&&!value);
            if(value){Bind(options);EventSystem.current?.SetSelectedGameObject(tabs[selectedTab].gameObject);}else EventSystem.current?.SetSelectedGameObject(null);
        }
        void BuildWorldLabels()
        {
            var b=Button("Interação no mundo",root,"",()=>ctx.QueueCurrentAction(),out actionText);prompt=(RectTransform)b.transform;prompt.pivot=new Vector2(.5f,0);prompt.sizeDelta=new Vector2(112,34);actionText.fontSize=16;
            prompt.GetComponent<Image>().color=new Color(.025f,.095f,.12f,.93f);var outline=prompt.gameObject.AddComponent<Outline>();outline.effectColor=new Color(.35f,.65f,.63f,.6f);outline.effectDistance=new Vector2(1,-1);
            var key=Rect("Tecla de interação",prompt);key.anchorMin=new Vector2(0,0);key.anchorMax=new Vector2(0,1);key.offsetMin=new Vector2(6,6);key.offsetMax=new Vector2(30,-6);Paint(key,accent);
            actionKeyText=Label("Tecla",key,"E",14,deep,TextAnchor.MiddleCenter);Stretch(actionKeyText.rectTransform);
            Stretch(actionText.rectTransform,40,10,3,3);actionText.alignment=TextAnchor.MiddleLeft;
            speech=Rect("Fala no mundo",root);speech.pivot=new Vector2(.5f,0);speech.sizeDelta=new Vector2(240,34);Paint(speech,new Color(.025f,.095f,.12f,.93f));
            speechText=Label("Fala",speech,"",16,new Color(1,.94f,.79f),TextAnchor.MiddleCenter);Stretch(speechText.rectTransform,10,10,8,8);
        }
        bool Place(RectTransform label,Vector3 point)
        {
            var p=ctx.Camera.WorldToViewportPoint(point);if(p.z<.1f||p.x<0||p.x>1||p.y<0||p.y>1)return false;
            Vector2 at;RectTransformUtility.ScreenPointToLocalPointInRectangle(root,ctx.Camera.WorldToScreenPoint(point),canvas.renderMode==RenderMode.ScreenSpaceOverlay?null:canvas.worldCamera,out at);
            label.anchorMin=label.anchorMax=new Vector2(.5f,.5f);
            float half=label.sizeDelta.x*.5f;at.x=Mathf.Clamp(at.x,root.rect.xMin+half+12,root.rect.xMax-half-12);
            at.y=Mathf.Clamp(at.y+22,root.rect.yMin+12,root.rect.yMax-label.sizeDelta.y-12);label.anchoredPosition=at;return true;
        }
        void BuildReticle()
        {
            aimReticle=Rect("Mira sobre o ombro",root);aimReticle.anchorMin=aimReticle.anchorMax=new Vector2(.5f,.5f);aimReticle.sizeDelta=Vector2.zero;
            for(int i=0;i<4;i++)
            {
                var tick=Rect("Traço da mira "+i,aimReticle);tick.anchorMin=tick.anchorMax=new Vector2(.5f,.5f);tick.sizeDelta=i<2?new Vector2(5,1.5f):new Vector2(1.5f,5);
                var image=tick.gameObject.AddComponent<Image>();image.color=new Color(1,1,1,.85f);image.raycastTarget=false;aimTicks[i]=tick;
            }
            aimReticle.gameObject.SetActive(false);
        }
        void LateUpdate()
        {
            if(!ctx||!ctx.Ready)return;
            RefreshStatusHud();
            RefreshHackingUI();
            bool aiming=!ctx.Paused&&ctx.Combat&&ctx.Combat.Aiming&&!ctx.Combat.Reloading;
            aimReticle.gameObject.SetActive(aiming);
            if(aiming)
            {
                float gap=7+ctx.Motor.Speed*1.3f+ctx.Combat.Recoil*9;
                aimTicks[0].anchoredPosition=Vector2.left*gap;aimTicks[1].anchoredPosition=Vector2.right*gap;
                aimTicks[2].anchoredPosition=Vector2.up*gap;aimTicks[3].anchoredPosition=Vector2.down*gap;
            }
            panel.sizeDelta=new Vector2(Mathf.Min(680,root.rect.width-24),SettingsHeight);
            if(ctx.Paused){if(!settings.activeSelf)ShowSettings(true);return;}
            if(settings.activeSelf)ShowSettings(false);
            var action=ctx.CurrentAction;bool show=action!=null&&!ctx.Remote.Scanning&&!ctx.Remote.VehicleActive&&!(ctx.Emotes&&(ctx.Emotes.WheelOpen||ctx.Emotes.Performing));
            if(show){actionText.text=action.Text;actionKeyText.text=action.KeyLabel;prompt.sizeDelta=new Vector2(Mathf.Min(Mathf.Max(104,actionText.preferredWidth+50),root.rect.width-20),34);show=Place(prompt,action.Position+Vector3.up*.45f);}
            prompt.gameObject.SetActive(show);
            bool talking=ctx.Clock<ctx.SpeechUntil&&!string.IsNullOrEmpty(ctx.Speech);
            if(talking){speechText.text=ctx.Speech;float width=Mathf.Min(Mathf.Clamp(speechText.preferredWidth+24,104,320),root.rect.width-20);speech.sizeDelta=new Vector2(width,72);speech.sizeDelta=new Vector2(width,Mathf.Clamp(speechText.preferredHeight+16,34,88));talking=ctx.Visible(ctx.Camera.transform.position,ctx.SpeechPosition)&&Place(speech,ctx.SpeechPosition+Vector3.up*.45f);}
            speech.gameObject.SetActive(talking);
            if(talking&&show&&Mathf.Abs(speech.anchoredPosition.x-prompt.anchoredPosition.x)<(speech.sizeDelta.x+prompt.sizeDelta.x)*.5f&&Mathf.Abs(speech.anchoredPosition.y-prompt.anchoredPosition.y)<speech.sizeDelta.y+prompt.sizeDelta.y+6)
                prompt.anchoredPosition=new Vector2(prompt.anchoredPosition.x,Mathf.Max(root.rect.yMin+8,speech.anchoredPosition.y-prompt.sizeDelta.y-6));
            bool driving=ctx.Motor.Riding||ctx.Remote.VehicleActive;
            bool unarmed=ctx.Combat.Equipped==null;
            string[] labels={action!=null?action.KeyLabel:"E",ctx.Remote.DroneActive?"Subir":driving?"Frear":"Pular",ctx.Remote.DroneActive?"Descer":driving?"F. mão":"Correr","F","Drone","R",unarmed?"Guarda":"Mirar",unarmed?"Soco":"Disparar","Arma"};
            if(touch.activeSelf)for(int i=0;i<labels.Length;i++)touchLabels[i].text=labels[i];
            if(touchHackerCaption){touchHackerCaption.text="Hacker";touchHackerCaption.color=ctx.Remote.HackerMode?accent:ink;}
            if(touchHackNext)touchHackNext.gameObject.SetActive(ctx.Remote.HackerMode&&ctx.Remote.ActionCount>1);
            if(touchCrouch)
            {
                touchCrouch.gameObject.SetActive(!driving&&!ctx.Motor.Swimming&&!ctx.Remote.Active&&!ctx.Motor.SeatHeight.HasValue);
                touchCrouch.interactable=ctx.Motor.Grounded&&!ctx.Motor.Busy;
                touchCrouchCaption.text=ctx.Motor.Crouched?"Stand":"Crouch";
                touchCrouchCaption.color=ctx.Motor.Crouched?accent:ink;
            }
        }
        void BuildTouch()
        {
            var area=Rect("Controles de toque",root);Stretch(area);touch=area.gameObject;
            var stick=Rect("Mover",area);stick.anchorMin=stick.anchorMax=Vector2.zero;stick.pivot=Vector2.zero;stick.anchoredPosition=new Vector2(22,28);stick.sizeDelta=new Vector2(150,150);Paint(stick,new Color(.04f,.14f,.17f,.55f),true);
            var label=Label("Mover",stick,"Mover",16,ink,TextAnchor.UpperCenter);Stretch(label.rectTransform,0,0,8,0);
            stickKnob=Rect("Direção",stick);stickKnob.anchorMin=stickKnob.anchorMax=new Vector2(.5f,.5f);stickKnob.sizeDelta=new Vector2(38,38);Paint(stickKnob,accent);
            var drag=stick.gameObject.AddComponent<CityPointerControl>();drag.Drag=e=>{Vector2 p;RectTransformUtility.ScreenPointToLocalPointInRectangle(stick,e.position,e.pressEventCamera,out p);ctx.Input.TouchMove=Vector2.ClampMagnitude((p-stick.rect.center)/55,1);stickKnob.anchoredPosition=ctx.Input.TouchMove*45;};drag.Released=()=>{ctx.Input.TouchMove=Vector2.zero;stickKnob.anchoredPosition=Vector2.zero;};
            touchCrouch=Button("Crouch",area,"Crouch",()=>{if(!ctx.Paused)ctx.Input.TouchCrouch=true;},out touchCrouchCaption);
            var cr=(RectTransform)touchCrouch.transform;cr.anchorMin=cr.anchorMax=Vector2.zero;cr.pivot=Vector2.zero;cr.anchoredPosition=new Vector2(22,244);cr.sizeDelta=new Vector2(112,42);
            for(int i=0;i<9;i++)
            {
                int key=i;var b=Button("Toque "+i,area,"",()=>{},out touchLabels[i]);var r=(RectTransform)b.transform;r.anchorMin=r.anchorMax=new Vector2(1,0);r.pivot=new Vector2(1,0);r.anchoredPosition=new Vector2(-22-(2-i%3)*79,28+(2-i/3)*52);r.sizeDelta=new Vector2(73,46);
                var press=b.gameObject.AddComponent<CityPointerControl>();press.Pressed=()=>Press(key,true);press.Released=()=>Press(key,false);
            }
            var hacker=Button("Modo hacker",area,"Hacker",()=>ctx.Input.TouchHackerToggle=true,out touchHackerCaption);
            var hr=(RectTransform)hacker.transform;hr.anchorMin=hr.anchorMax=new Vector2(1,0);hr.pivot=new Vector2(1,0);hr.anchoredPosition=new Vector2(-22,192);hr.sizeDelta=new Vector2(115,42);
            touchHackNext=Button("Alternar hack",area,"Trocar ação",()=>ctx.Input.TouchHackNext=true);var nr=(RectTransform)touchHackNext.transform;
            nr.anchorMin=nr.anchorMax=new Vector2(1,0);nr.pivot=new Vector2(1,0);nr.anchoredPosition=new Vector2(-145,192);nr.sizeDelta=new Vector2(114,42);
            var esc=Button("Abrir configurações",area,"Esc",()=>owner.SetSettings(true));var er=(RectTransform)esc.transform;er.anchorMin=er.anchorMax=Vector2.one;er.pivot=Vector2.one;er.anchoredPosition=new Vector2(-18,-18);er.sizeDelta=new Vector2(60,38);
            var map=Button("Abrir mapa",area,"Mapa · M",()=>ctx.Input.TouchMap=true);var mr=(RectTransform)map.transform;mr.anchorMin=mr.anchorMax=Vector2.one;mr.pivot=Vector2.one;mr.anchoredPosition=new Vector2(-86,-18);mr.sizeDelta=new Vector2(108,38);
        }
        void Press(int key,bool down)
        {
            if(ctx.Paused)return;var i=ctx.Input;
            if(key==0&&down)ctx.QueueCurrentAction();if(key==1){i.TouchBrake=down;if(down)i.TouchJump=true;}
            if(key==2)i.TouchRun=down;if(key==3)i.TouchHack=down;if(key==4&&down)i.TouchDrone=true;
            if(key==5&&down)i.TouchReload=true;if(key==6)i.TouchAim=down;if(key==7&&down)i.TouchFire=true;if(key==8&&down)owner.CycleTouchEquipment();
        }
        public void SetTouchVisible(bool visible){if(touch&&touch.activeSelf!=visible){if(!visible)ReleaseControls();touch.SetActive(visible);}}
        public void ReleaseControls(){if(!touch)return;foreach(var p in touch.GetComponentsInChildren<CityPointerControl>(true))p.Release();}
        void OnDestroy(){if(root)Destroy(root.gameObject);}
    }
}
