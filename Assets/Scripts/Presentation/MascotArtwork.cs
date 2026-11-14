using System;
using System.Collections.Generic;
using UnityEngine;

namespace FlipRace.Presentation
{
    /// <summary>
    /// Original Canvas mascot paths, with cached animated textures.
    /// UnitBounds uses radius units and GUI coordinates (positive Y downward).
    /// Draw UnitBounds * radius around the runner center; the caller owns rotation
    /// and the upper lane's horizontal reflection. No per-frame rasterization.
    /// </summary>
    public static class MascotArtwork
    {
        public static readonly Rect UnitBounds = new Rect(-1.5f,-1.75f,3f,3f);
        const int TextureSize=128, Samples=2, RunningFrames=96, StrideFrames=16, AirFrames=12, DetailFrames=8;
        const float TwoPi=Mathf.PI*2, StrideRate=25, RunLoopSeconds=TwoPi*6/StrideRate;
        // Only the four selected variants own animation caches. A selection prepares
        // the requested pose, not all 192 poses synchronously before the next paint.
        static readonly FrameSet[] frames=new FrameSet[4];
        static readonly int[] activeCharacters={-1,-1,-1,-1};
        static readonly Texture2D[,] previews=new Texture2D[CharacterRoster.Count,4];
        // WebGL cannot collect temporary managed arrays until the synchronous
        // Awake/Bake returns. Reuse these two 256 KiB supersampled canvases:
        // allocating both for every pose used nearly 200 MiB before first paint.
        static readonly Painter frameCanvas=new Painter();
        static readonly Painter torsoCanvas=new Painter();

        sealed class FrameSet
        {
            public int Character, Player;
            public Painter Body, Open, Shut, Dizzy, Pleased, Happy;
            public readonly Texture2D[] Run=new Texture2D[RunningFrames];
            public readonly Texture2D[] RunBlink=new Texture2D[StrideFrames];
            public readonly Texture2D[] RunSuccess=new Texture2D[StrideFrames];
            public readonly Texture2D[] Idle=new Texture2D[DetailFrames];
            public readonly Texture2D[] IdleBlink=new Texture2D[DetailFrames];
            public readonly Texture2D[] Air=new Texture2D[AirFrames];
            public readonly Texture2D[] AirBlink=new Texture2D[AirFrames];
            public readonly Texture2D[] Recover=new Texture2D[DetailFrames];
            public readonly Texture2D[] Success=new Texture2D[DetailFrames];
            public readonly Texture2D[] Win=new Texture2D[DetailFrames];

            public void Dispose()
            {
                Destroy(Run);Destroy(RunBlink);Destroy(RunSuccess);Destroy(Idle);Destroy(IdleBlink);
                Destroy(Air);Destroy(AirBlink);Destroy(Recover);Destroy(Success);Destroy(Win);
            }
            static void Destroy(Texture2D[] textures)
            {
                for(int i=0;i<textures.Length;i++)if(textures[i]!=null)UnityEngine.Object.Destroy(textures[i]);
            }
        }

        public static Texture2D Create(int player) {return Create(player,player);}
        public static Texture2D Create(int characterIndex,int playerIndex)
        {
            int c=CharacterRoster.Normalize(characterIndex),p=Mathf.Clamp(playerIndex,0,3);
            if(previews[c,p]==null)
            {
                Painter body=new Painter();DrawBody(body,c,p);
                Painter eyes=new Painter();DrawEyes(eyes,c,false,false);
                previews[c,p]=MakeFrame(body,eyes,Scarf(p),0,0,false,"preview-"+c+"-"+p,0,c);
            }
            return previews[c,p];
        }
        public static void WarmUp(int characterIndex,int playerIndex) { Ensure(characterIndex,playerIndex); }
        static FrameSet Ensure(int characterIndex,int playerIndex)
        {
            int c=CharacterRoster.Normalize(characterIndex),p=Mathf.Clamp(playerIndex,0,3);
            if(frames[p]==null||activeCharacters[p]!=c)
            {
                if(frames[p]!=null)frames[p].Dispose();
                frames[p]=Bake(c,p);activeCharacters[p]=c;
            }
            return frames[p];
        }

        /// <summary>
        /// A pose is rasterized on first use, then retrieved without allocation.
        /// Create builds only one menu preview and does not prewarm an animation.
        /// Pass a visual clock that stops during pause. Feet move at ~4 Hz,
        /// scarf follows each stride, leaf sways at ~0.65 Hz, eyes blink separately.
        /// </summary>
        public static Texture2D GetFrame(int player,float time,bool running,bool airborne,bool recovering)
        {return GetFrame(player,player,time,running,airborne,recovering);}
        public static Texture2D GetFrame(int characterIndex,int playerIndex,float time,bool running,bool airborne,bool recovering)
        {return GetMotionFrame(characterIndex,playerIndex,time,time,Mathf.Repeat(time/.8f,1),running,airborne,recovering);}

        /// <summary>
        /// visualTime is pause-aware time for breathing/blinks. motionTime is
        /// distance/base speed, so the stride follows actual boost/slow travel.
        /// flipProgress is the real 0..1 flip phase. Reactions keep locomotion.
        /// Up to four selected characters lazily cache immutable RGBA frames.
        /// </summary>
        public static Texture2D GetMotionFrame(int characterIndex,int playerIndex,float visualTime,float motionTime,
            float flipProgress,bool running,bool airborne,bool recovering,MascotReaction reaction=MascotReaction.None,float reactionAge=0)
        {
            int character=CharacterRoster.Normalize(characterIndex);
            FrameSet set=Ensure(character,playerIndex);
            if(float.IsNaN(visualTime)||float.IsInfinity(visualTime))visualTime=0;
            if(float.IsNaN(motionTime)||float.IsInfinity(motionTime))motionTime=0;
            bool blink=Mathf.Sin(visualTime*1.71f+character*.71f+playerIndex*.29f)>.992f;
            int detail=LoopIndex(visualTime*4/TwoPi,DetailFrames);
            if(recovering)return Pose(set,set.Recover,detail);
            if(airborne)
            {
                int flight=Mathf.Clamp(Mathf.RoundToInt(Mathf.Clamp01(flipProgress)*(AirFrames-1)),0,AirFrames-1);
                return blink?Pose(set,set.AirBlink,flight):Pose(set,set.Air,flight);
            }
            if(reaction==MascotReaction.Win)return Pose(set,set.Win,LoopIndex(reactionAge*1.6f,DetailFrames));
            if(!running)return blink?Pose(set,set.IdleBlink,detail):Pose(set,set.Idle,detail);
            int stride=LoopIndex(motionTime*StrideRate/TwoPi,StrideFrames);
            if(reaction==MascotReaction.Success)return Pose(set,set.RunSuccess,stride);
            return blink?Pose(set,set.RunBlink,stride):Pose(set,set.Run,LoopIndex(motionTime/RunLoopSeconds,RunningFrames));
        }
        public static Texture2D GetReactionFrame(int characterIndex,int playerIndex,float time,MascotReaction reaction)
        {
            FrameSet set=Ensure(characterIndex,playerIndex);
            if(float.IsNaN(time)||float.IsInfinity(time))time=0;
            int detail=LoopIndex(time*1.6f,DetailFrames);
            if(reaction==MascotReaction.Success)return Pose(set,set.Success,detail);
            if(reaction==MascotReaction.Hit)return Pose(set,set.Recover,detail);
            if(reaction==MascotReaction.Win)return Pose(set,set.Win,detail);
            return Pose(set,set.Idle,detail);
        }

        static int LoopIndex(float cycles,int count)
        {return Mathf.Min(count-1,Mathf.FloorToInt(Mathf.Repeat(cycles,1)*count));}

        static FrameSet Bake(int character,int player)
        {
            var set=new FrameSet { Character=character, Player=player,
                Body=new Painter(), Open=new Painter(), Shut=new Painter(), Dizzy=new Painter(),
                Pleased=new Painter(), Happy=new Painter() };
            DrawBody(set.Body,character,player);
            DrawEyes(set.Open,character,false,false); DrawEyes(set.Shut,character,true,false);
            DrawEyes(set.Dizzy,character,false,true);
            DrawEyes(set.Pleased,character,false,false,MascotReaction.Success);
            DrawEyes(set.Happy,character,false,false,MascotReaction.Win);
            return set;
        }

        static Texture2D Pose(FrameSet set,Texture2D[] group,int i)
        {
            if(group[i]!=null)return group[i];
            int character=set.Character,player=set.Player;
            Color32 scarf=Scarf(player);
            Painter body=set.Body;
            if(group==set.Run)
            {
                float cycle=(float)i/RunningFrames;
                // Six strides per leaf cycle preserves the JS frequency ratio.
                float step=Mathf.Sin(cycle*TwoPi*6);
                float leaf=Mathf.Sin(cycle*TwoPi)*.24f;
                group[i]=MakeFrame(body,set.Open,scarf,step,leaf,false,"run-"+character+"-"+player+"-"+i,0,character,
                    MascotReaction.None,cycle*TwoPi*6);
            }
            else if(group==set.RunBlink||group==set.RunSuccess)
            {
                float phase=(float)i/StrideFrames*TwoPi,wave=Mathf.Sin(phase);
                bool success=group==set.RunSuccess;
                group[i]=MakeFrame(body,success?set.Pleased:set.Shut,scarf,wave,success?.18f:0,false,
                    (success?"run-success-":"run-blink-")+player+"-"+i,0,character,
                    success?MascotReaction.Success:MascotReaction.None,phase);
            }
            else if(group==set.Air||group==set.AirBlink)
            {
                float flight=(float)i/(AirFrames-1),wave=Mathf.Sin(flight*TwoPi);
                bool blink=group==set.AirBlink;
                group[i]=MakeFrame(body,blink?set.Shut:set.Open,scarf,0,wave*.24f,true,
                    (blink?"air-blink-":"air-")+player+"-"+i,wave*.10f,character,MascotReaction.None,float.NaN,flight);
            }
            else
            {
                float wave=Mathf.Sin((float)i/DetailFrames*TwoPi),leaf=wave*.18f;
                if(group==set.Idle||group==set.IdleBlink)
                    group[i]=MakeFrame(body,group==set.Idle?set.Open:set.Shut,scarf,0,leaf,false,"idle-"+player+"-"+i,wave*.026f,character);
                else if(group==set.Recover)
                    group[i]=MakeFrame(body,set.Dizzy,scarf,wave,leaf,true,"recover-"+player+"-"+i,wave*.09f,character,MascotReaction.Hit);
                else if(group==set.Success)
                    group[i]=MakeFrame(body,set.Pleased,scarf,wave,leaf,false,"success-"+player+"-"+i,wave*.08f,character,MascotReaction.Success);
                else
                    group[i]=MakeFrame(body,set.Happy,scarf,wave,leaf,false,"win-"+player+"-"+i,wave*.12f,character,MascotReaction.Win);
            }
            return group[i];
        }

        static Texture2D MakeFrame(Painter body,Painter eyes,Color32 scarf,float step,float leafAngle,
            bool airborne,string name,float flutter=0,int character=0,MascotReaction reaction=MascotReaction.None,
            float stridePhase=float.NaN,float flight=.5f)
        {
            Painter p=frameCanvas;p.Clear();
            Color32 outline=Outline(character);
            bool striding=!float.IsNaN(stridePhase);
            float bounce=striding?Mathf.Abs(Mathf.Sin(stridePhase))*.07f:0;
            float tuck=airborne?Mathf.Sin(Mathf.Clamp01(flight)*Mathf.PI):0;
            float tail=step*.24f+flutter+(airborne?-.08f:0);
            p.Shape(new Curve().M(-.4f,.27f)
                .Q(-.95f,.25f+tail*.7f,-1.27f,.03f+tail)
                .L(-1.04f,.39f+tail*.35f).L(-1.28f,.59f+tail*.65f)
                .Q(-.69f,.6f,-.3f,.45f).Close(),scarf,outline);
            p.Stroke(new Curve().M(-.54f,.34f).Q(-.88f,.38f+tail*.3f,-1.14f,.30f+tail*.6f),
                new Color32(255,255,255,100),.035f);
            for(int i=0;i<2;i++)
            {
                float side=i==0?-1:1,offset=i==0?-.32f:.37f;
                float lift=striding?Mathf.Max(0,-step*side):0;
                float footY=airborne?.72f-tuck*.16f:.85f-lift*.18f;
                float footX=offset+(airborne?side*(.10f+tuck*.09f):step*side*.24f);
                Color32 foot=FootColor(character,i);
                p.Stroke(new Curve().M(offset*.8f,.58f).Q(footX-side*.09f,.72f,footX,footY),outline,.16f);
                p.Stroke(new Curve().M(offset*.8f,.58f).Q(footX-side*.09f,.72f,footX,footY),foot,.09f);
                p.Ellipse(footX,footY,.24f-lift*.025f,.14f+lift*.025f,foot,outline,.075f);
            }
            Painter torso=torsoCanvas;torso.Clear();
            if(character==6)DrawNikoWings(torso,leafAngle+step*.24f,tuck,reaction);
            if(character==9)DrawPushinkaBraids(torso,leafAngle+step*.22f);
            torso.Composite(body);torso.Composite(eyes);
            DrawSprout(torso,leafAngle+step*.055f,character,reaction);
            DrawAccessories(torso,character,leafAngle+step*.22f,reaction);
            float lean=striding?.025f+step*.025f:airborne?tuck*.055f:0;
            float squash=striding?Mathf.Cos(stridePhase*2)*.018f:airborne?-tuck*.025f:0;
            p.CompositeTransformed(torso,0,-bounce+flutter*.12f,lean,1+squash,1-squash);
            return p.Finish(name);
        }

        static void DrawBody(Painter p,int character,int player)
        {
            switch(character)
            {
                case 6:DrawNikoBody(p,player);return;
                case 7:DrawIgriBody(p,player);return;
                case 8:DrawShadowBody(p,player);return;
                case 9:DrawPushinkaBody(p,player);return;
            }
            if(character==4){DrawJackBody(p,player);return;}
            if(character==5){DrawPumpkinBody(p,player);return;}
            Color32 outline=Outline(character);
            Color32 fur=Hex(character==3?0x98929f:character==2?0xf3f0dc:0xfff9e9);
            p.Ellipse(0,.39f,.56f,.47f,Hex(character==3?0x96909e:character==2?0xb98766:0xfff8e6),outline);
            p.Ellipse(.19f,.42f,.27f,.28f,Hex(character==3?0xaaa3b3:character==2?0xbe9272:0xf7eed9));
            if(character==2)
            {
                // Teal travelling cloak and gold clasp from the original Travnik.
                p.Shape(new Curve().M(-.52f,.24f).Q(-.74f,.30f,-.59f,.79f)
                    .Q(-.40f,.99f,-.17f,.75f).L(-.08f,.48f).Close(),Hex(0x2f9d8d),Hex(0x397f70));
                p.Shape(new Curve().M(.48f,.24f).Q(.65f,.36f,.52f,.81f)
                    .Q(.30f,.97f,.17f,.79f).L(.08f,.48f).Close(),Hex(0x258e80),Hex(0x397f70));
                p.Stroke(new Curve().M(-.48f,.39f).Q(-.57f,.69f,-.41f,.80f),Hex(0x68c3ab),.035f);
                p.Ellipse(0,.49f,.14f,.095f,Hex(0xe7c548),Hex(0xa29046));
                p.Ellipse(0,.49f,.075f,.045f,Hex(0xb98766));
            }
            // Exact silhouette from renderer.js: tall ears and rounded low cheeks.
            if(character==2)
                p.Shape(new Curve().M(-.83f,-.37f).L(-.78f,-1.34f)
                    .Q(-.78f,-1.50f,-.62f,-1.33f).L(-.18f,-.78f)
                    .Q(.04f,-.7f,.29f,-.78f).L(.73f,-1.33f)
                    .Q(.87f,-1.44f,.88f,-1.28f).L(.87f,-.34f)
                    .C(.91f,.44f,-.85f,.59f,-.83f,-.37f).Close(),fur,outline);
            else
                p.Shape(new Curve().M(-.79f,-.29f).L(-.68f,-1.5f).L(-.16f,-.78f)
                    .Q(.06f,-.85f,.26f,-.77f).L(.82f,-1.43f).L(.88f,-.32f)
                    .C(1.22f,-.12f,1.12f,.24f,.72f,.27f).Q(.06f,.39f,-.65f,.26f)
                    .C(-1.06f,.2f,-1.17f,-.07f,-.79f,-.29f).Close(),fur,outline);
            Color32 ear=Hex(character==3?0xb4a8bd:character==2?0xded7bc:0xeed8bb);
            p.Shape(new Curve().M(-.69f,-.52f).L(-.63f,-1.2f).L(-.34f,-.62f).Close(),ear);
            p.Shape(new Curve().M(.48f,-.6f).L(.75f,-1.16f).L(.77f,-.48f).Close(),ear);
            if(character==0)
                p.Shape(new Curve().M(.42f,-.59f).C(.1f,-.93f,-.05f,-.55f,.12f,-.27f)
                    .L(.49f,.18f).C(.74f,-.1f,.91f,-.34f,.8f,-.56f)
                    .C(.72f,-.73f,.57f,-.72f,.42f,-.59f).Close(),Hex(0xefce6f));
            else if(character==1)
                p.Shape(new Curve().M(.1f,-.42f).C(.08f,-.88f,.77f,-.83f,.81f,-.4f)
                    .L(.83f,.11f).Q(.66f,.3f,.59f,.04f).Q(.46f,.35f,.34f,.08f)
                    .Q(.08f,.28f,.1f,-.05f).Close(),Hex(0x7ebac8));
            else if(character==2)
                p.Shape(new Curve().M(-.43f,-.08f).C(-.72f,-.43f,-.93f,-.04f,-.64f,.16f)
                    .L(-.43f,.34f).L(-.15f,.08f).C(.04f,-.2f,-.18f,-.4f,-.43f,-.08f).Close(),Hex(0xec984e),Hex(0xc78343),.035f);
            Color32 blush=Hex(character==3?0xba9dbd:0xedc2ae);
            if(character!=2)p.Ellipse(-.69f,.07f,.12f,.04f,blush);
            p.Ellipse(character==2?.66f:.83f,.07f,.1f,.04f,blush);
            Color32 mouth=Hex(character==3?0x5f516e:0x6e6354);
            p.Shape(new Curve().M(.04f,-.04f).L(.21f,-.04f).L(.13f,.05f).Close(),mouth);
            p.Stroke(new Curve().M(.13f,.04f).L(.13f,.12f).L(.03f,.17f).L(-.07f,.13f),mouth,.045f);
            p.Stroke(new Curve().M(.13f,.12f).L(.23f,.17f).L(.34f,.11f),mouth,.045f);
            Color32 scarf=Scarf(player);
            p.Shape(RoundedRect(-.56f,.27f,1.18f,.18f,.07f),scarf,Hex(player==0?0x8d76aa:0xbf7e58));
            p.Stroke(new Curve().M(-.42f,.3f).L(.38f,.3f),new Color32(255,255,255,140),.04f);
            // A stripe vs two pale stitches also distinguish players without color.
            if(player==0)p.Stroke(new Curve().M(.38f,.31f).L(.38f,.41f),Hex(0xf8f2ff),.045f);
            else {p.Ellipse(.32f,.36f,.025f,.025f,Hex(0xfff4df));p.Ellipse(.43f,.36f,.025f,.025f,Hex(0xfff4df));}
            if(character==0)p.Stroke(new Curve().M(0,-.79f).L(.02f,-1.01f).L(.15f,-1.22f),Hex(0x819b62),.055f);
            else if(character==2)p.Stroke(new Curve().M(.10f,-.78f).Q(.15f,-1.0f,.05f,-1.13f),Hex(0xad7055),.09f);
            else if(character==3)p.Stroke(new Curve().M(.02f,-.78f).Q(.15f,-1.0f,.18f,-1.24f),Hex(0x819b62),.055f);
        }

        static void DrawEyes(Painter p,int character,bool blink,bool recovering,MascotReaction reaction=MascotReaction.None)
        {
            if(character>=6){DrawCatalogFace(p,character,blink,recovering,reaction);return;}
            if(character>=4){DrawAutumnEyes(p,character,blink,recovering,reaction);return;}
            Color32 outline=Outline(character),eye=Hex(character==0?0x86a250:character==1?0x82a9ac:character==3?0xa184be:0x737e49);
            for(int i=0;i<2;i++)
            {
                float x=i==0?-.4f:.49f;
                if(recovering)
                {
                    p.Stroke(new Curve().M(x-.13f,-.37f).L(x+.13f,-.12f),Hex(0x655b4e),.09f);
                    p.Stroke(new Curve().M(x+.13f,-.37f).L(x-.13f,-.12f),Hex(0x655b4e),.09f);
                }
                else if(character==2 || reaction==MascotReaction.Win || (reaction==MascotReaction.Success&&character==1))
                    p.Stroke(new Curve().M(x-.15f,-.21f).Q(x,-.57f,x+.15f,-.21f),Hex(character==3?0x5f516e:0x655b4e),.075f);
                else if(blink || (reaction==MascotReaction.Success && (character==0?i==0:i==1)))
                    p.Stroke(new Curve().M(x-.18f,-.17f).L(x+.18f,-.17f),Hex(character==3?0x5f516e:0x655b4e),.09f);
                else
                {
                    p.Ellipse(x,-.27f,.23f,.27f,Hex(0xfffdf1),outline);
                    p.Ellipse(x+.045f,-.25f,.15f,.2f,eye);
                    p.Ellipse(x+.075f,-.24f,.06f,.15f,Hex(0x5c604b));
                    p.Ellipse(x+.01f,-.33f,.055f,.065f,Hex(0xffffff));
                }
            }
        }

        static void DrawSprout(Painter p,float angle,int character,MascotReaction reaction)
        {
            if(character>=6)
            {
                if(character==6)DrawNikoCurl(p,angle,reaction);
                return;
            }
            if(character==4){DrawJackHat(p,angle,reaction);return;}
            if(character==5){DrawPumpkinHat(p,angle,reaction);return;}
            if(character==3)
            {
                float flowerAngle=angle+(reaction==MascotReaction.Win?.35f:0);
                float cy=reaction==MascotReaction.Hit?-1.15f:-1.27f;
                for(int i=0;i<7;i++)
                {
                    float a=i*TwoPi/7+flowerAngle,x=.18f+Mathf.Cos(a)*.19f,y=cy+Mathf.Sin(a)*.19f;
                    p.Ellipse(x,y,.13f,.13f,Hex(i%2==0?0xd5b7e2:0xc8a7d8),Hex(0x9680a7),.035f);
                }
                p.Ellipse(.18f,cy,.105f,.115f,Hex(0xf1d775),Hex(0xb4a154),.03f);
                p.Ellipse(.15f,cy-.045f,.03f,.035f,Hex(0xfff1b1));
                return;
            }
            float size=character==2?.48f:character==1?.34f:.39f;
            Curve leaf=new Curve().M(-size*.45f,size*.45f)
                .Q(-size*.52f,-size*.4f,size*.65f,-size*.65f)
                .Q(size*.5f,size*.3f,-size*.45f,size*.45f).Close();
            float lx=character==2?.28f:.22f,ly=character==2?-.75f:character==1?-.88f:-1.27f;
            float leafAngle=angle+(character==2?Mathf.PI*.5f:0)+(reaction==MascotReaction.Hit?.4f:0);
            leaf.Transform(lx,ly,leafAngle);p.Shape(leaf,Hex(character==2?0x5ca562:0xa6be80));
            Curve vein=new Curve().M(-size*.4f,size*.4f).L(size*.4f,-size*.4f);
            vein.Transform(lx,ly,leafAngle);p.Stroke(vein,Hex(character==2?0x3d7b4b:0x819b62),.035f);
        }

        static void DrawAccessories(Painter p,int character,float wave,MascotReaction reaction)
        {
            if(character>=6)
            {
                if(character==7)DrawIgriPendant(p,wave);
                if(character==8)DrawShadowSaw(p,wave,reaction);
                return;
            }
            if(character>=4){DrawAutumnPendant(p,character,wave,reaction);return;}
            bool cheerful=reaction==MascotReaction.Success||reaction==MascotReaction.Win;
            if(character==2)
            {
                float lift=cheerful?-.15f:0;
                Curve staff=new Curve().M(1.03f,.94f).Q(.95f,.38f,1.12f,-.22f)
                    .C(1.20f,-.53f,1.39f,-.21f,1.19f,-.03f);
                staff.Transform(0,lift+wave*.06f,0);
                p.Stroke(staff,Hex(0x8b5b43),.14f);
                p.Stroke(staff,Hex(0xc4875f),.085f);
            }
            if(cheerful&&character==1)
            {
                for(int i=0;i<3;i++)
                {
                    float x=i==0?-.98f:i==1?1.02f:.77f,y=i==0?-.64f:i==1?-.80f:-1.1f;
                    p.Shape(new Curve().M(x,y-.1f).Q(x-.1f,y+.04f,x,y+.07f).Q(x+.1f,y+.04f,x,y-.1f).Close(),Hex(0x7ebac8));
                }
            }
            if(reaction==MascotReaction.Win&&(character==0||character==3))
                p.Ellipse(-.69f,.23f-wave*.12f,.14f,.17f,Hex(character==3?0xaaa3b4:0xfff9e9),Outline(character));
        }

        // Autumn heroes use the same mutable pose construction and lazy caches
        // as the original four. Only their drawing branches differ; no PNG is
        // substituted for motion, blinking, reactions or an airborne frame.
        static void DrawJackBody(Painter p,int player)
        {
            Color32 ink=Outline(4),ivory=Hex(0xfff8e9),honey=Hex(0xe3bd7d);
            p.Shape(new Curve().M(-.45f,.30f).Q(-.60f,.61f,-.47f,.86f)
                .Q(0,.96f,.49f,.86f).Q(.60f,.58f,.46f,.30f).Close(),ivory,ink,.065f);
            p.Shape(new Curve().M(.31f,.37f).Q(.53f,.55f,.40f,.82f)
                .Q(.16f,.88f,.05f,.83f).Q(.36f,.67f,.31f,.37f).Close(),Hex(0xf0e5d5));
            // Honey ear/hair silhouette, including the small unruly side locks.
            p.Shape(new Curve().M(-.85f,-.34f).L(-.98f,-1.08f).Q(-.62f,-1.04f,-.33f,-.77f)
                .Q(.18f,-.94f,.67f,-.77f).L(.84f,-1.00f).L(.91f,-.38f)
                .L(1.01f,.15f).L(.86f,.08f).L(.87f,.27f).L(.70f,.16f)
                .L(.61f,.34f).L(.43f,.17f).L(-.48f,.22f).L(-.64f,.38f)
                .L(-.72f,.19f).L(-.89f,.32f).L(-.90f,.13f).L(-1.02f,.20f).Close(),honey,ink,.065f);
            p.Shape(new Curve().M(-.78f,-.38f).Q(-.73f,-.69f,-.38f,-.71f)
                .Q(.03f,-.89f,.57f,-.62f).Q(.92f,-.49f,.91f,-.10f)
                .C(.93f,.42f,-.88f,.45f,-.88f,-.08f).Close(),ivory,ink,.055f);
            p.Shape(new Curve().M(-.87f,-.63f).L(-.91f,-.94f).L(-.56f,-.72f).Close(),Hex(0xf2d59f));
            p.Shape(new Curve().M(-.64f,-.72f).Q(-.30f,-1.01f,.13f,-.76f)
                .Q(.12f,-.55f,-.13f,-.52f).Q(-.45f,-.50f,-.64f,-.72f).Close(),honey);
            p.Stroke(new Curve().M(-.36f,-.77f).Q(-.17f,-.61f,-.11f,-.65f),Hex(0xf4d69f),.055f);
            // The wine strand is separate from the eye, not a face marking.
            p.Shape(new Curve().M(-.64f,-.86f).Q(-.37f,-.65f,-.62f,-.40f)
                .Q(-.85f,-.09f,-.72f,.30f).Q(-1.01f,.08f,-.85f,-.28f)
                .Q(-.56f,-.63f,-.64f,-.86f).Close(),Hex(0xa77277),Hex(0x93696c),.045f);
            p.Stroke(new Curve().M(-.65f,-.65f).Q(-.90f,-.12f,-.79f,.14f),Hex(0xc08c8c),.04f);
            for(int side=0;side<2;side++)for(int n=0;n<5;n++){
                float x=(side==0?-.48f:.51f)+(n%3-1)*.075f;
                p.Ellipse(x,.06f+(n/3)*.065f,.018f,.018f,Hex(0xe0b49b));
            }
            DrawAutumnMouth(p,Hex(0x887160));DrawAutumnCollar(p,player,ink);
        }

        static Curve PumpkinHead()
        {
            return new Curve().M(0,-.76f).L(-.34f,-.76f).L(-.96f,-1.13f)
                .Q(-.98f,-.68f,-.88f,-.36f).C(-1.06f,.12f,-.76f,.35f,0,.35f)
                .C(.76f,.35f,1.06f,.12f,.88f,-.36f).Q(.98f,-.68f,.96f,-1.13f)
                .L(.34f,-.76f).Close();
        }
        static void DrawPumpkinBody(Painter p,int player)
        {
            Color32 ink=Outline(5),apricot=Hex(0xefbf88),slate=Hex(0x898591);
            p.Shape(new Curve().M(0,.29f).L(-.43f,.29f).Q(-.60f,.59f,-.47f,.86f)
                .Q(-.19f,.93f,0,.91f).Close(),apricot);
            p.Shape(new Curve().M(0,.29f).L(.43f,.29f).Q(.60f,.59f,.47f,.86f)
                .Q(.19f,.93f,0,.91f).Close(),slate);
            p.Stroke(new Curve().M(-.43f,.29f).Q(-.60f,.59f,-.47f,.86f)
                .Q(0,.96f,.47f,.86f).Q(.60f,.59f,.43f,.29f),ink,.065f);
            p.Shape(new Curve().M(0,-.76f).L(-.34f,-.76f).L(-.96f,-1.13f)
                .Q(-.98f,-.68f,-.88f,-.36f).C(-1.06f,.12f,-.76f,.35f,0,.35f).Close(),apricot);
            p.Shape(new Curve().M(0,-.76f).L(.34f,-.76f).L(.96f,-1.13f)
                .Q(.98f,-.68f,.88f,-.36f).C(1.06f,.12f,.76f,.35f,0,.35f).Close(),slate);
            p.Stroke(PumpkinHead(),ink,.065f);
            p.Shape(new Curve().M(-.85f,-.94f).L(-.78f,-.53f).L(-.49f,-.74f).Close(),Hex(0xf8d59e));
            p.Shape(new Curve().M(.85f,-.94f).L(.78f,-.53f).L(.49f,-.74f).Close(),Hex(0xaaa0ac));
            p.Stroke(new Curve().M(-.38f,.48f).Q(-.44f,.63f,-.36f,.78f),Hex(0xf9d9a9),.055f);
            p.Ellipse(.66f,.13f,.10f,.10f,slate,Hex(0xefe8d9),.025f);
            p.Stroke(Star(.66f,.13f,.067f,.029f),Hex(0xefe8d9),.022f);
            DrawAutumnMouth(p,Hex(0x807368));DrawAutumnCollar(p,player,ink);
        }
        static void DrawAutumnMouth(Painter p,Color32 ink)
        {
            p.Shape(new Curve().M(-.07f,.025f).L(.09f,.025f).L(.015f,.11f).Close(),ink);
            p.Stroke(new Curve().M(.015f,.13f).Q(-.08f,.24f,.20f,.19f),ink,.04f);
        }
        static void DrawAutumnCollar(Painter p,int player,Color32 ink)
        {
            p.Shape(new Curve().M(-.48f,.28f).Q(0,.38f,.49f,.28f).L(.54f,.43f)
                .Q(0,.54f,-.53f,.43f).Close(),Hex(0xf9f0df),ink,.045f);
            p.Stroke(new Curve().M(-.38f,.35f).Q(0,.42f,.40f,.35f),Hex(0xfffbef),.045f);
            Color32 stitch=Scarf(player);
            if(player==0)p.Stroke(new Curve().M(.36f,.36f).L(.37f,.44f),stitch,.05f);
            else {p.Ellipse(.31f,.41f,.025f,.025f,stitch);p.Ellipse(.41f,.40f,.025f,.025f,stitch);}
        }
        static void DrawAutumnEyes(Painter p,int character,bool blink,bool recovering,MascotReaction reaction)
        {
            Color32 ink=Outline(character);
            for(int i=0;i<2;i++){
                float x=i==0?-.40f:.43f;
                if(recovering){
                    p.Stroke(new Curve().M(x-.12f,-.39f).L(x+.12f,-.15f),ink,.065f);
                    p.Stroke(new Curve().M(x+.12f,-.39f).L(x-.12f,-.15f),ink,.065f);
                }
                else if(reaction==MascotReaction.Win){
                    p.Stroke(new Curve().M(x-.16f,-.20f).Q(x,-.49f,x+.16f,-.20f),ink,.055f);
                }
                else if(blink||(reaction==MascotReaction.Success&&i==0)){
                    p.Stroke(new Curve().M(x-.19f,-.23f).Q(x,-.19f,x+.19f,-.23f),ink,.055f);
                }
                else {
                    p.Ellipse(x,-.28f,.235f,character==4?.175f:.205f,Hex(0xfffbef),ink,.055f);
                    Color32 iris=Hex(character==4?0xd5ac6b:i==0?0x998b7c:0xa2c6d4);
                    p.Ellipse(x+.025f,-.27f,character==4?.10f:.105f,.15f,iris);
                    if(character==4){
                        p.Shape(new Curve().M(x+.01f,-.39f).Q(x-.07f,-.24f,x+.07f,-.15f)
                            .Q(x+.11f,-.29f,x+.01f,-.39f).Close(),Hex(0x989854));
                        p.Stroke(new Curve().M(x+.01f,-.34f).L(x+.055f,-.21f),Hex(0xc4bb73),.018f);
                        p.Ellipse(x-.01f,-.35f,.026f,.035f,Hex(0xfff6d5));
                    }else{
                        p.Ellipse(x+.04f,-.23f,.047f,.095f,Hex(i==0?0x7c7168:0x81a9bc));
                        p.Shape(Star(x+.003f,-.355f,.056f,.022f),Hex(0xfffdf4));
                        p.Ellipse(x+.075f,-.19f,.021f,.022f,Hex(0xfffdf4));
                    }
                }
            }
        }
        static Curve Star(float x,float y,float radius,float inner)
        {
            var star=new Curve();
            for(int i=0;i<10;i++){
                float a=-Mathf.PI*.5f+i*Mathf.PI/5,r=i%2==0?radius:inner;
                if(i==0)star.M(x+Mathf.Cos(a)*r,y+Mathf.Sin(a)*r);else star.L(x+Mathf.Cos(a)*r,y+Mathf.Sin(a)*r);
            }
            return star.Close();
        }
        static void DrawJackHat(Painter p,float wave,MascotReaction reaction)
        {
            Color32 ink=Outline(4);float dy=(reaction==MascotReaction.Hit?.06f:0)+wave*.018f;
            var crown=new Curve().M(-.20f,-.79f).Q(.30f,-.99f,.33f,-1.57f)
                .Q(.75f,-1.39f,.88f,-.84f).L(.81f,-.65f).Close();
            crown.Transform(0,dy,wave*.045f);p.Shape(crown,Hex(0xbd947c),ink,.055f);
            var light=new Curve().M(.41f,-1.42f).Q(.68f,-1.17f,.74f,-.91f);
            light.Transform(0,dy,wave*.045f);p.Stroke(light,Hex(0xd7b293),.045f);
            var brim=new Curve().M(-.40f,-.79f).C(-.19f,-1.02f,.61f,-.82f,1.03f,-.49f)
                .Q(1.12f,-.34f,.82f,-.41f).Q(.08f,-.49f,-.40f,-.79f).Close();
            brim.Transform(0,dy,wave*.045f);p.Shape(brim,Hex(0xb98e78),ink,.055f);
            var band=new Curve().M(.02f,-.85f).Q(.45f,-.72f,.82f,-.63f);
            band.Transform(0,dy,wave*.045f);p.Stroke(band,Hex(0xa16f75),.075f);
            var feather=new Curve().M(.03f,-.79f).Q(-.25f,-.93f,-.19f,-1.17f)
                .Q(.01f,-1.11f,.08f,-.91f).Q(.02f,-1.16f,.16f,-1.24f)
                .Q(.26f,-.94f,.03f,-.79f).Close();
            feather.Transform(0,dy,wave*.075f);p.Shape(feather,Hex(0xa9777c),Hex(0x916b70),.035f);
            var vein=new Curve().M(.02f,-.78f).L(.11f,-1.14f);vein.Transform(0,dy,wave*.075f);
            p.Stroke(vein,Hex(0xc2908e),.025f);
        }
        static void DrawPumpkinHat(Painter p,float wave,MascotReaction reaction)
        {
            float dy=wave*.035f+(reaction==MascotReaction.Hit?.06f:0);
            DrawSmallPumpkin(p,.035f,-1.12f+dy,.37f,.255f,true);
        }
        static void DrawSmallPumpkin(Painter p,float x,float y,float rx,float ry,bool stem)
        {
            Color32 ink=Hex(0xa38a72);
            p.Ellipse(x,y,rx,ry,Hex(0xefbf88),ink,.045f);
            p.Stroke(new Curve().M(x-rx*.42f,y-ry*.88f).Q(x-rx*.74f,y,x-rx*.39f,y+ry*.88f),Hex(0xd5a575),.027f);
            p.Stroke(new Curve().M(x,y-ry*.92f).Q(x-rx*.17f,y,x,y+ry*.94f),Hex(0xd5a575),.027f);
            p.Stroke(new Curve().M(x+rx*.43f,y-ry*.87f).Q(x+rx*.73f,y,x+rx*.4f,y+ry*.87f),Hex(0xd5a575),.027f);
            p.Stroke(new Curve().M(x-rx*.46f,y-ry*.58f).Q(x-rx*.13f,y-ry*.82f,x+rx*.15f,y-ry*.63f),Hex(0xffdcab),.032f);
            for(int i=0;i<2;i++){
                float ex=x+(i==0?-.40f:.40f)*rx;
                p.Shape(new Curve().M(ex,y-ry*.33f).L(ex-rx*.17f,y+ry*.02f).L(ex+rx*.17f,y+ry*.02f).Close(),Hex(0x927e6a));
            }
            if(stem){
                p.Stroke(new Curve().M(x+.02f,y-ry).Q(x+.035f,y-ry-.19f,x+.16f,y-ry-.15f),Hex(0x84946c),.075f);
                p.Stroke(new Curve().M(x+.02f,y-ry-.015f).Q(x+.06f,y-ry-.16f,x+.15f,y-ry-.15f),Hex(0xb3c094),.033f);
            }else{
                p.Stroke(new Curve().M(x-rx*.42f,y+ry*.30f).Q(x,y+ry*.72f,x+rx*.42f,y+ry*.30f),Hex(0x927e6a),.025f);
            }
        }
        static void DrawAutumnPendant(Painter p,int character,float wave,MascotReaction reaction)
        {
            float x=wave*.055f,y=.60f+(reaction==MascotReaction.Win?-.025f:0);
            p.Stroke(new Curve().M(0,.47f).Q(x-.025f,.54f,x,y-.025f),Hex(0x9d9578),.025f);
            if(character==4){
                p.Shape(new Curve().M(x,y-.01f).Q(x+.16f,y+.12f,x-.025f,y+.25f)
                    .Q(x-.10f,y+.13f,x,y-.01f).Close(),Hex(0xc2b779),Hex(0x9c926a),.025f);
                p.Stroke(new Curve().M(x,y+.03f).L(x-.014f,y+.19f),Hex(0xe2d6a0),.02f);
            }else DrawSmallPumpkin(p,x,y+.06f,.13f,.115f,false);
        }
        // Game-size adaptations of Tanya's catalog portraits 69, 68, 48 and 38.
        static Curve TallEarHead()
        {
            return new Curve().M(0,-.76f).L(-.72f,-1.49f)
                .Q(-.78f,-1.56f,-.81f,-1.42f).Q(-.95f,-.91f,-.97f,-.17f)
                .C(-1.04f,.22f,-.78f,.32f,0,.32f)
                .C(.78f,.32f,1.04f,.22f,.97f,-.17f)
                .Q(.95f,-.91f,.81f,-1.42f).Q(.78f,-1.56f,.72f,-1.49f).Close();
        }

        static Curve CatalogTorso()
        {
            return new Curve().M(-.43f,.31f).Q(-.61f,.56f,-.53f,.83f)
                .Q(-.50f,.91f,0,.91f).Q(.50f,.91f,.53f,.83f)
                .Q(.61f,.56f,.43f,.31f).Close();
        }

        static void DrawNikoBody(Painter p,int player)
        {
            Color32 ink=Outline(6),blue=Hex(0xc9e7e9),lavender=Hex(0xcdd5f2);
            Curve torso=CatalogTorso();
            p.Shape(torso,blue);
            p.Shape(new Curve().M(0,.31f).L(.43f,.31f).Q(.61f,.56f,.53f,.83f)
                .Q(.50f,.91f,0,.91f).Close(),lavender);
            p.Stroke(torso,ink,.061f);
            Curve head=TallEarHead();
            p.Shape(head,blue);
            p.Shape(new Curve().M(0,-.76f).L(.72f,-1.49f)
                .Q(.78f,-1.56f,.81f,-1.42f).Q(.95f,-.91f,.97f,-.17f)
                .C(1.04f,.22f,.78f,.32f,0,.32f).Close(),lavender);
            p.Stroke(head,ink,.066f);
            p.Stroke(new Curve().M(-.73f,-1.27f).Q(-.78f,-1.12f,-.79f,-.98f),Hex(0xe7f4f3),.047f);
            p.Stroke(new Curve().M(.72f,-1.25f).L(.58f,-1.03f),Hex(0xe7eaf9),.047f);
            DrawCatalogBlush(p,Hex(0xe49bac));
            DrawCatalogCollar(p,player,Hex(0xaabce0),ink);
        }

        static void DrawNikoWings(Painter p,float wave,float tuck,MascotReaction reaction)
        {
            float lift=wave*.36f+tuck*.15f+(reaction==MascotReaction.Win?.12f:0);
            float tip=-.46f-lift;
            Color32 ink=Outline(6);
            for(int side=-1;side<=1;side+=2)
            {
                p.Shape(new Curve().M(side*.42f,.43f).Q(side*1.08f,.07f,side*1.31f,tip)
                    .Q(side*1.42f,tip+.17f,side*1.23f,tip+.50f)
                    .Q(side*1.32f,tip+.48f,side*1.42f,tip+.36f)
                    .Q(side*1.43f,tip+.62f,side*1.09f,tip+.82f)
                    .L(side*1.32f,tip+.80f).Q(side*1.04f,.71f,side*.42f,.67f).Close(),
                    Hex(0xf2d487),ink,.052f);
                p.Shape(new Curve().M(side*.44f,.43f).Q(side*1.10f,.04f,side*1.28f,tip+.09f)
                    .Q(side*1.30f,tip+.31f,side*1.14f,tip+.56f).L(side*1.34f,tip+.49f)
                    .Q(side*1.24f,tip+.70f,side*1.00f,tip+.82f).L(side*1.18f,tip+.86f)
                    .Q(side*.94f,.64f,side*.44f,.60f).Close(),Hex(0xefbcaa));
            }
        }

        static void DrawNikoCurl(Painter p,float wave,MascotReaction reaction)
        {
            Curve curl=new Curve().M(.13f,.12f).C(-.04f,.24f,-.25f,.10f,-.18f,-.07f)
                .C(-.10f,-.30f,.15f,-.27f,.17f,-.12f).C(.19f,-.02f,.06f,.04f,.03f,-.05f);
            curl.Transform(0,-1.12f+(reaction==MascotReaction.Hit?.05f:0),wave*.30f);
            p.Stroke(curl,Hex(0x809163),.095f);
            p.Stroke(curl,Hex(0xb8cd94),.055f);
        }

        static void DrawIgriBody(Painter p,int player)
        {
            Color32 ink=Outline(7),cream=Hex(0xf9efdc);
            Curve torso=CatalogTorso();
            p.Shape(torso,cream);
            p.Shape(new Curve().M(-.43f,.31f).Q(-.61f,.56f,-.53f,.83f)
                .Q(-.50f,.87f,-.46f,.88f).C(-.53f,.77f,-.33f,.73f,-.40f,.63f)
                .C(-.50f,.55f,-.27f,.52f,-.43f,.31f).Close(),Hex(0xb188a2));
            p.Stroke(torso,ink,.061f);
            p.Shape(TallEarHead(),cream,ink,.066f);
            p.Shape(new Curve().M(-.78f,-1.27f).L(-.58f,-.84f)
                .Q(-.75f,-.71f,-.87f,-.51f).Q(-.86f,-.92f,-.78f,-1.27f).Close(),Hex(0xe9d9c0));
            p.Shape(new Curve().M(.78f,-1.27f).Q(.86f,-.96f,.87f,-.50f)
                .Q(.75f,-.71f,.57f,-.78f).Close(),Hex(0xe9d9c0));
            p.Shape(new Curve().M(.12f,-.36f).L(.24f,-.27f).L(.37f,-.39f)
                .L(.51f,-.31f).L(.61f,-.47f).L(.75f,-.42f).Q(.82f,-.51f,.76f,-.60f)
                .Q(.96f,-.42f,.91f,-.07f).L(.72f,-.18f).L(.62f,-.05f)
                .L(.49f,-.15f).L(.30f,.01f).Close(),Hex(0x8c7c70),ink,.065f);
            p.Stroke(new Curve().M(.28f,-.22f).L(.34f,-.28f),Hex(0xbaaa9a),.043f);
            DrawCatalogBlush(p,Hex(0xe9a4ac));
            DrawCatalogCollar(p,player,Hex(0xbc9ab6),ink);
        }

        static void DrawIgriPendant(Painter p,float wave)
        {
            float x=wave*.10f;
            Color32 ink=Outline(7);
            p.Stroke(new Curve().M(0,.45f).L(x,.56f),ink,.028f);
            p.Shape(new Curve().M(x,.53f).L(x+.082f,.62f).L(x+.049f,.62f)
                .L(x+.106f,.70f).L(x+.063f,.70f).L(x+.129f,.80f)
                .Q(x,.82f,x-.129f,.80f).L(x-.063f,.70f).L(x-.106f,.70f)
                .L(x-.049f,.62f).L(x-.082f,.62f).Close(),Hex(0xa2b87b),ink,.035f);
            p.Stroke(new Curve().M(x-.039f,.63f).L(x+.043f,.63f),Hex(0xc2cf98),.022f);
        }

        static Curve WideEarHead(float leftEarY,float rightEarY)
        {
            return new Curve().M(-.83f,-.48f).Q(-1.00f,-.82f,-1.00f,leftEarY+.08f)
                .Q(-1.00f,leftEarY,-.91f,leftEarY).Q(-.57f,leftEarY+.06f,-.32f,-.87f)
                .Q(0,-.92f,.34f,-.87f).Q(.67f,rightEarY+.035f,.97f,rightEarY)
                .Q(1.05f,rightEarY,1.05f,rightEarY+.09f).Q(1.03f,-.78f,.90f,-.37f)
                .C(.85f,.15f,.55f,.32f,0,.33f).C(-.60f,.33f,-.94f,.12f,-.92f,-.20f)
                .Q(-.91f,-.37f,-.83f,-.48f).Close();
        }

        static void DrawShadowBody(Painter p,int player)
        {
            Color32 ink=Outline(8);
            Curve torso=CatalogTorso();
            p.Shape(torso,Hex(0xb2ce8a));
            p.Shape(new Curve().M(-.43f,.31f).L(.43f,.31f).Q(.54f,.47f,.55f,.68f)
                .Q(.04f,.63f,-.55f,.69f).Q(-.54f,.47f,-.43f,.31f).Close(),Hex(0x527964));
            p.Stroke(torso,ink,.069f);
            DrawCatalogCollar(p,player,Hex(0x68655a),ink);
            Curve head=WideEarHead(-1.17f,-1.16f);
            p.Shape(head,Hex(0xb9d687));
            p.Shape(new Curve().M(-.92f,-1.08f).Q(-.78f,-.79f,-.69f,-.66f).L(-.83f,-.48f)
                .Q(-.92f,-.71f,-.92f,-1.08f).Close(),Hex(0xa4bd78));
            p.Shape(new Curve().M(.96f,-1.08f).Q(.89f,-.76f,.77f,-.58f).L(.89f,-.45f)
                .Q(.95f,-.72f,.96f,-1.08f).Close(),Hex(0xa4bd78));
            p.Shape(new Curve().M(-.89f,-.36f).C(-.44f,-.44f,.44f,-.44f,.90f,-.37f)
                .C(.85f,.15f,.55f,.32f,0,.33f).C(-.60f,.33f,-.94f,.12f,-.92f,-.20f)
                .Q(-.91f,-.29f,-.89f,-.36f).Close(),Hex(0x535149));
            p.Stroke(head,ink,.075f);
            p.Stroke(new Curve().M(-.63f,-.52f).Q(-.49f,-.55f,-.39f,-.52f),Hex(0x778b54),.034f);
        }

        static void DrawShadowSaw(Painter p,float wave,MascotReaction reaction)
        {
            float angle=wave*.16f+(reaction==MascotReaction.Win?-.11f:0);
            Color32 ink=Outline(8);
            // The handle sways around the right paw in artwork coordinates.
            Curve blade=new Curve().M(.03f,-.11f).L(.39f,-.78f).L(.51f,-.71f)
                .L(.46f,-.65f).L(.49f,-.60f).L(.41f,-.55f).L(.44f,-.50f)
                .L(.36f,-.44f).L(.39f,-.39f).L(.31f,-.33f).L(.34f,-.28f)
                .L(.26f,-.22f).L(.29f,-.17f).L(.21f,-.11f).L(.23f,-.06f)
                .L(.15f,-.01f).Close();
            Curve grip=RoundedRect(-.12f,-.15f,.26f,.28f,.065f);
            Curve hole=RoundedRect(-.052f,-.076f,.12f,.12f,.027f);
            Curve shine=new Curve().M(.12f,-.24f).L(.37f,-.68f);
            const float x=.74f,y=.62f;
            blade.Transform(x,y,angle);grip.Transform(x,y,angle-.42f);
            hole.Transform(x,y,angle-.42f);shine.Transform(x,y,angle);
            p.Shape(blade,Hex(0xbdb9b7),ink,.05f);
            p.Stroke(shine,Hex(0xd8d2cd),.025f);
            p.Shape(grip,Hex(0xb7c77a),ink,.049f);
            p.Shape(hole,Hex(0x686258),ink,.025f);
        }

        static void DrawPushinkaBody(Painter p,int player)
        {
            Color32 ink=Outline(9),mint=Hex(0xc3e6e2),lilac=Hex(0xc39ac6);
            Curve torso=CatalogTorso();
            p.Shape(torso,mint);
            p.Shape(new Curve().M(-.55f,.68f).Q(0,.62f,.55f,.68f).Q(.56f,.75f,.53f,.83f)
                .Q(.50f,.91f,0,.91f).Q(-.50f,.91f,-.53f,.83f).Q(-.56f,.75f,-.55f,.68f).Close(),lilac);
            p.Stroke(torso,ink,.064f);
            DrawCatalogCollar(p,player,Hex(0xb7d8d6),ink);
            DrawPushinkaCrossing(p,-1,Hex(0x9fd8cc),ink);
            DrawPushinkaCrossing(p,1,Hex(0xcca2cd),ink);
            Curve head=WideEarHead(-1.28f,-1.24f);
            p.Shape(head,lilac);
            p.Shape(new Curve().M(-.90f,-.33f).Q(.06f,-.39f,.90f,-.37f)
                .C(.85f,.15f,.55f,.32f,0,.33f).C(-.60f,.33f,-.94f,.12f,-.92f,-.20f)
                .Q(-.91f,-.29f,-.90f,-.33f).Close(),mint);
            p.Stroke(head,ink,.065f);
            p.Stroke(new Curve().M(-.86f,-1.13f).Q(-.78f,-1.09f,-.72f,-1.02f),Hex(0xf6e1dc),.039f);
            p.Stroke(new Curve().M(.85f,-1.10f).Q(.78f,-1.07f,.71f,-1.01f),Hex(0xf6e1dc),.039f);
        }

        static void DrawPushinkaBraids(Painter p,float wave)
        {
            Color32 ink=Outline(9);
            for(int side=-1;side<=1;side+=2)
            {
                float x=side*.62f+wave*.10f;
                p.Ellipse(x,.59f,.134f,.137f,Hex(0xb488ba),ink,.045f);
                p.Ellipse(x+side*.057f,.44f,.167f,.16f,Hex(0xc095c3),ink,.045f);
                p.Ellipse(x+side*.072f,.29f,.208f,.163f,Hex(0xb58ab9),ink,.045f);
                p.Stroke(new Curve().M(x+side*.183f,.28f).Q(x+side*.207f,.32f,x+side*.21f,.36f),
                    Hex(0xe4c4db),.028f);
            }
        }

        static void DrawPushinkaCrossing(Painter p,int direction,Color32 color,Color32 ink)
        {
            // Each diagonal keeps the small animal face at its outer tip.
            Curve band=new Curve().M(-direction*.34f,.36f).Q(-direction*.29f,.32f,-direction*.21f,.39f)
                .L(direction*.28f,.69f).Q(direction*.36f,.65f,direction*.47f,.75f)
                .C(direction*.59f,.88f,direction*.41f,.92f,direction*.28f,.84f)
                .L(-direction*.39f,.48f).Q(-direction*.43f,.42f,-direction*.34f,.36f).Close();
            p.Shape(band,color,ink,.041f);
            p.Ellipse(direction*.44f,.78f,.023f,.024f,ink);
            p.Stroke(new Curve().M(direction*.29f,.73f).Q(direction*.32f,.79f,direction*.37f,.80f)
                .Q(direction*.43f,.84f,direction*.48f,.82f),ink,.025f);
            p.Stroke(new Curve().M(-direction*.33f,.41f).L(-direction*.27f,.45f),Hex(0xf5e7df),.026f);
        }

        static void DrawCatalogCollar(Painter p,int player,Color32 fill,Color32 ink)
        {
            p.Shape(new Curve().M(-.46f,.31f).Q(0,.39f,.46f,.31f).Q(.53f,.39f,.46f,.46f)
                .Q(0,.53f,-.46f,.46f).Q(-.53f,.39f,-.46f,.31f).Close(),fill,ink,.047f);
            Color32 stitch=Scarf(player);
            if(player==0)p.Stroke(new Curve().M(.33f,.36f).L(.33f,.44f),stitch,.048f);
            else
            {
                p.Ellipse(.30f,.41f,.026f,.026f,stitch);
                p.Ellipse(.39f,.40f,.026f,.026f,stitch);
            }
        }

        static void DrawCatalogBlush(Painter p,Color32 color)
        {
            for(int side=-1;side<=1;side+=2)
            for(int i=-1;i<=1;i++)
            {
                float x=side*.68f+i*.066f;
                p.Stroke(new Curve().M(x+.03f,.055f).L(x-.015f,.14f),color,.034f);
            }
        }

        static void DrawCatalogFace(Painter p,int character,bool blink,bool recovering,MascotReaction reaction)
        {
            Color32 ink=Outline(character);
            for(int side=-1;side<=1;side+=2)
            {
                // Igri's zigzag mask is part of the face in every expression.
                if(character==7&&side==1)break;
                float x=side*.47f;
                bool closed=blink||(reaction==MascotReaction.Success&&side==-1);
                if(character==9)
                    DrawPushinkaEye(p,x,side,closed,recovering,reaction);
                else if(character==8)
                    DrawShadowEye(p,x,closed,recovering,reaction);
                else
                    DrawTriangleEye(p,x,character,closed,recovering,reaction);
            }
            if(character==9)DrawPushinkaGlasses(p,ink);
            DrawCatalogMouth(p,character,recovering,reaction);
        }

        static void DrawTriangleEye(Painter p,float x,int character,bool closed,bool recovering,MascotReaction reaction)
        {
            Color32 ink=Outline(character),iris=Hex(character==6?0x9bc9e2:0xaa7e98);
            const float y=-.35f;
            float depth=closed?.043f:recovering?.17f:reaction==MascotReaction.Win?.12f:.29f;
            p.Shape(new Curve().M(x-.125f,y).L(x+.125f,y).L(x,y+depth).Close(),iris,ink,.05f);
            p.Stroke(new Curve().M(x-.225f,y).Q(x,y+(reaction==MascotReaction.Win?-.09f:0),x+.225f,y),
                ink,.054f);
            if(!closed&&!recovering)
                p.Stroke(new Curve().M(x-.061f,y+.047f).L(x-.019f,y+.09f),
                    Hex(character==6?0xd7ecf5:0xd5aec6),.028f);
            if(recovering)DrawEyeCross(p,x,y+.085f,ink,.063f);
        }

        static void DrawShadowEye(Painter p,float x,bool closed,bool recovering,MascotReaction reaction)
        {
            Color32 lemon=Hex(0xbdca79),olive=Hex(0x819259);
            float depth=closed?.008f:recovering?.12f:reaction==MascotReaction.Win?.105f:.255f;
            const float y=-.30f;
            var lid=new Curve().M(x-.20f,y).Q(x,y+(reaction==MascotReaction.Win?-.12f:0),x+.20f,y);
            p.Stroke(lid,lemon,.054f);
            if(!closed)
            {
                p.Shape(new Curve().M(x-.19f,y).L(x+.19f,y).C(x+.14f,y+depth*1.25f,
                    x-.14f,y+depth*1.25f,x-.19f,y).Close(),lemon);
                if(recovering)DrawEyeCross(p,x,y+.043f,olive,.052f);
            }
        }

        static void DrawPushinkaEye(Painter p,float x,int side,bool closed,bool recovering,MascotReaction reaction)
        {
            Color32 ink=Outline(9);
            if(closed||reaction==MascotReaction.Win)
            {
                float lift=closed?.048f:-.24f;
                p.Stroke(new Curve().M(x-.25f,-.26f).Q(x,-.26f+lift,x+.25f,-.26f),ink,.066f);
                return;
            }
            p.Ellipse(x,-.30f,.257f,.183f,Hex(0xfff6eb),ink,.030f);
            float irisX=x-side*.055f;
            p.Ellipse(irisX,-.281f,.133f,.157f,Hex(0xae83b5),ink,.029f);
            if(recovering)DrawEyeCross(p,irisX,-.28f,ink,.071f);
            else
            {
                p.Ellipse(irisX,-.276f,.066f,.106f,Hex(0x655368));
                p.Ellipse(irisX-.039f,-.345f,.027f,.030f,Hex(0xfff3e6));
            }
            p.Stroke(new Curve().M(x-.25f,-.35f).Q(x,-.60f,x+.25f,-.35f),ink,.070f);
        }

        static void DrawPushinkaGlasses(Painter p,Color32 ink)
        {
            Color32 frame=Hex(0xb284b4);
            var bridge=new Curve().M(-.21f,-.145f).L(.21f,-.145f);
            p.Stroke(bridge,ink,.052f);
            p.Stroke(bridge,frame,.027f);
            for(int side=-1;side<=1;side+=2)
            {
                var rim=new Curve().M(side*.20f,-.21f).L(side*.22f,-.09f)
                    .Q(side*.23f,-.061f,side*.27f,-.061f).L(side*.67f,-.061f)
                    .Q(side*.71f,-.061f,side*.72f,-.12f).L(side*.75f,-.22f);
                p.Stroke(rim,ink,.073f);
                p.Stroke(rim,frame,.041f);
            }
        }

        static void DrawEyeCross(Painter p,float x,float y,Color32 ink,float radius)
        {
            p.Stroke(new Curve().M(x-radius,y-radius).L(x+radius,y+radius),ink,.036f);
            p.Stroke(new Curve().M(x+radius,y-radius).L(x-radius,y+radius),ink,.036f);
        }

        static void DrawCatalogMouth(Painter p,int character,bool recovering,MascotReaction reaction)
        {
            Color32 ink=character==8?Hex(0xbdca79):Outline(character);
            if(character!=6)
                p.Shape(new Curve().M(-.06f,.014f).Q(0,-.015f,.06f,.014f).L(0,.064f).Close(),ink);
            if(recovering||reaction==MascotReaction.Win)
            {
                p.Ellipse(0,.163f,recovering?.057f:.095f,recovering?.05f:.071f,ink);
                if(reaction==MascotReaction.Win)p.Ellipse(0,.186f,.045f,.024f,Hex(0xe5b4a7));
                return;
            }
            if(character==8&&reaction!=MascotReaction.Success)
                p.Stroke(new Curve().M(-.054f,.20f).Q(0,.087f,.054f,.20f),ink,.040f);
            else
            {
                float depth=reaction==MascotReaction.Success?.30f:.227f;
                p.Stroke(new Curve().M(-.119f,.137f).Q(-.10f,depth,0,.151f)
                    .Q(.10f,depth,.119f,.137f),ink,.040f);
            }
        }

        static Color32 Outline(int character)
        {
            switch(character)
            {
                case 2:return Hex(0x7d7967);
                case 3:return Hex(0x65596f);
                case 4:return Hex(0x8b7767);
                case 5:return Hex(0x7d7378);
                case 6:return Hex(0x817264);
                case 7:return Hex(0x786859);
                case 8:return Hex(0x635950);
                case 9:return Hex(0x786576);
                default:return Hex(0x746d58);
            }
        }

        static Color32 FootColor(int character,int side)
        {
            switch(character)
            {
                case 3:return Hex(0xaaa3b4);
                case 4:return Hex(0xf5ecdc);
                case 5:return Hex(side==0?0xefbf88:0x898591);
                case 6:return Hex(side==0?0xbadce1:0xc2caec);
                case 7:return Hex(0xf5e8d4);
                case 8:return Hex(0xa6bc7a);
                case 9:return Hex(side==0?0xa7dcd1:0xc49bc6);
                default:return Hex(0xeee5d0);
            }
        }
        static Color32 Scarf(int player) {return Hex(player==0?0xb7a1de:player==1?0xf1ab81:player==2?0x8fcab7:0xdba0bc);}

        static Curve RoundedRect(float x,float y,float w,float h,float r)
        {
            return new Curve().M(x+r,y).L(x+w-r,y).Q(x+w,y,x+w,y+r)
                .L(x+w,y+h-r).Q(x+w,y+h,x+w-r,y+h)
                .L(x+r,y+h).Q(x,y+h,x,y+h-r).L(x,y+r).Q(x,y,x+r,y).Close();
        }
        static Color32 Hex(int value){return new Color32((byte)(value>>16),(byte)(value>>8),(byte)value,255);}

        // Flatten curves before scan conversion; original control points stay readable.
        sealed class Curve
        {
            public readonly List<Vector2> Points=new List<Vector2>(64);
            public bool Closed;
            public Curve M(float x,float y){Points.Add(new Vector2(x,y));return this;}
            public Curve L(float x,float y){Points.Add(new Vector2(x,y));return this;}
            public Curve Q(float cx,float cy,float x,float y)
            {
                Vector2 p=Points[Points.Count-1],c=new Vector2(cx,cy),end=new Vector2(x,y);
                for(int i=1;i<=16;i++){float t=i/16f,u=1-t;Points.Add(u*u*p+2*u*t*c+t*t*end);}
                return this;
            }
            public Curve C(float ax,float ay,float bx,float by,float x,float y)
            {
                Vector2 p=Points[Points.Count-1],a=new Vector2(ax,ay),b=new Vector2(bx,by),end=new Vector2(x,y);
                for(int i=1;i<=24;i++){float t=i/24f,u=1-t;Points.Add(u*u*u*p+3*u*u*t*a+3*u*t*t*b+t*t*t*end);}
                return this;
            }
            public Curve Close(){Closed=true;return this;}
            public void Transform(float x,float y,float angle)
            {
                float c=Mathf.Cos(angle),s=Mathf.Sin(angle);
                for(int i=0;i<Points.Count;i++){Vector2 p=Points[i];Points[i]=new Vector2(x+c*p.x-s*p.y,y+s*p.x+c*p.y);}
            }
        }

        // Supersampling and premultiplied averaging soften outlines without dark edges.
        sealed class Painter
        {
            const int Size=TextureSize*Samples;
            static readonly Color32[] textureUpload=new Color32[TextureSize*TextureSize];
            readonly Color32[] pixels=new Color32[Size*Size];
            public void Clear(){Array.Clear(pixels,0,pixels.Length);}
            static Vector2 Pixel(Vector2 v)
            {return new Vector2((v.x-UnitBounds.xMin)*Size/UnitBounds.width,(v.y-UnitBounds.yMin)*Size/UnitBounds.height);}
            void Put(int x,int y,Color32 c)
            {
                if(x<0||y<0||x>=Size||y>=Size||c.a==0)return;
                int index=y*Size+x;
                if(c.a==255||pixels[index].a==0){pixels[index]=c;return;}
                Color32 d=pixels[index];
                int da=(d.a*(255-c.a)+127)/255,alpha=c.a+da;
                pixels[index]=new Color32((byte)((c.r*c.a+d.r*da)/alpha),(byte)((c.g*c.a+d.g*da)/alpha),(byte)((c.b*c.a+d.b*da)/alpha),(byte)alpha);
            }
            public void Composite(Painter layer)
            {
                for(int i=0;i<pixels.Length;i++)
                {
                    Color32 c=layer.pixels[i];
                    if(c.a==255)pixels[i]=c;
                    else if(c.a!=0)Put(i%Size,i/Size,c);
                }
            }
            public void CompositeTransformed(Painter layer,float offsetX,float offsetY,float angle,float scaleX,float scaleY)
            {
                float c=Mathf.Cos(angle),s=Mathf.Sin(angle);
                float units=UnitBounds.width/Size;
                for(int y=0;y<Size;y++)
                {
                    float wy=UnitBounds.yMin+(y+.5f)*units-offsetY;
                    for(int x=0;x<Size;x++)
                    {
                        float wx=UnitBounds.xMin+(x+.5f)*units-offsetX;
                        float px=((c*wx+s*wy)/scaleX-UnitBounds.xMin)/units-.5f;
                        float py=((-s*wx+c*wy)/scaleY-UnitBounds.yMin)/units-.5f;
                        int ix=Mathf.FloorToInt(px),iy=Mathf.FloorToInt(py);
                        if(ix<0||iy<0||ix>=Size-1||iy>=Size-1)continue;
                        float tx=px-ix,ty=py-iy;
                        Color32 a=layer.pixels[iy*Size+ix],b=layer.pixels[iy*Size+ix+1];
                        Color32 d=layer.pixels[(iy+1)*Size+ix],e=layer.pixels[(iy+1)*Size+ix+1];
                        float wa=(1-tx)*(1-ty)*a.a,wb=tx*(1-ty)*b.a,wd=(1-tx)*ty*d.a,we=tx*ty*e.a;
                        float alpha=wa+wb+wd+we;
                        if(alpha<.5f)continue;
                        Put(x,y,new Color32((byte)((a.r*wa+b.r*wb+d.r*wd+e.r*we)/alpha),
                            (byte)((a.g*wa+b.g*wb+d.g*wd+e.g*we)/alpha),
                            (byte)((a.b*wa+b.b*wb+d.b*wd+e.b*we)/alpha),(byte)alpha));
                    }
                }
            }
            public void Shape(Curve curve,Color32 fill,Color32? outline=null,float lineWidth=.09f)
            {
                int count=curve.Points.Count;
                Vector2[] path=new Vector2[count];
                float minY=Size,maxY=0;
                for(int i=0;i<count;i++){path[i]=Pixel(curve.Points[i]);minY=Mathf.Min(minY,path[i].y);maxY=Mathf.Max(maxY,path[i].y);}
                float[] intersections=new float[count];
                for(int y=Mathf.Max(0,Mathf.FloorToInt(minY));y<Mathf.Min(Size,Mathf.CeilToInt(maxY));y++)
                {
                    float scan=y+.5f;int n=0;
                    for(int i=0,j=count-1;i<count;j=i++)
                    {
                        Vector2 a=path[j],b=path[i];
                        if((a.y>scan)!=(b.y>scan))intersections[n++]=a.x+(scan-a.y)*(b.x-a.x)/(b.y-a.y);
                    }
                    Array.Sort(intersections,0,n);
                    for(int i=0;i+1<n;i+=2)
                    {
                        int left=Mathf.Max(0,Mathf.CeilToInt(intersections[i]-.5f));
                        int right=Mathf.Min(Size,Mathf.CeilToInt(intersections[i+1]-.5f));
                        for(int x=left;x<right;x++)Put(x,y,fill);
                    }
                }
                if(outline.HasValue)Stroke(curve,outline.Value,lineWidth);
            }
            public void Ellipse(float cx,float cy,float rx,float ry,Color32 fill,Color32? outline=null,float lineWidth=.09f)
            {
                if(outline.HasValue)
                {
                    FillEllipse(cx,cy,rx+lineWidth*.5f,ry+lineWidth*.5f,outline.Value);
                    FillEllipse(cx,cy,rx-lineWidth*.5f,ry-lineWidth*.5f,fill);
                }
                else FillEllipse(cx,cy,rx,ry,fill);
            }
            void FillEllipse(float cx,float cy,float rx,float ry,Color32 fill)
            {
                if(rx<=0||ry<=0)return;
                Vector2 center=Pixel(new Vector2(cx,cy));rx*=Size/UnitBounds.width;ry*=Size/UnitBounds.height;
                for(int y=Mathf.Max(0,Mathf.FloorToInt(center.y-ry));y<Mathf.Min(Size,Mathf.CeilToInt(center.y+ry));y++)
                {
                    float dy=(y+.5f-center.y)/ry,half=rx*Mathf.Sqrt(Mathf.Max(0,1-dy*dy));
                    int left=Mathf.Max(0,Mathf.CeilToInt(center.x-half-.5f));
                    int right=Mathf.Min(Size,Mathf.CeilToInt(center.x+half-.5f));
                    for(int x=left;x<right;x++)Put(x,y,fill);
                }
            }
            public void Stroke(Curve path,Color32 color,float width)
            {
                float radius=width*.5f*Size/UnitBounds.width;
                int count=path.Points.Count;
                for(int i=0;i<(path.Closed?count:count-1);i++)
                {
                    Vector2 a=Pixel(path.Points[i]),b=Pixel(path.Points[(i+1)%count]);
                    float dx=b.x-a.x,dy=b.y-a.y,length=dx*dx+dy*dy;
                    int minX=Mathf.Max(0,Mathf.FloorToInt(Mathf.Min(a.x,b.x)-radius));
                    int maxX=Mathf.Min(Size-1,Mathf.CeilToInt(Mathf.Max(a.x,b.x)+radius));
                    int minY=Mathf.Max(0,Mathf.FloorToInt(Mathf.Min(a.y,b.y)-radius));
                    int maxY=Mathf.Min(Size-1,Mathf.CeilToInt(Mathf.Max(a.y,b.y)+radius));
                    for(int y=minY;y<=maxY;y++)for(int x=minX;x<=maxX;x++)
                    {
                        float t=length>0?Mathf.Clamp01(((x+.5f-a.x)*dx+(y+.5f-a.y)*dy)/length):0;
                        float ex=x+.5f-a.x-t*dx,ey=y+.5f-a.y-t*dy;
                        if(ex*ex+ey*ey<=radius*radius)Put(x,y,color);
                    }
                }
            }
            public Texture2D Finish(string name)
            {
                // SetPixels32 copies into the Texture2D before Apply; this upload
                // buffer is reusable as soon as the synchronous call returns.
                Color32[] output=textureUpload;Array.Clear(output,0,output.Length);
                for(int y=0;y<TextureSize;y++)for(int x=0;x<TextureSize;x++)
                {
                    int red=0,green=0,blue=0,alpha=0;
                    for(int sy=0;sy<Samples;sy++)for(int sx=0;sx<Samples;sx++)
                    {
                        Color32 c=pixels[(y*Samples+sy)*Size+x*Samples+sx];
                        alpha+=c.a;red+=c.r*c.a;green+=c.g*c.a;blue+=c.b*c.a;
                    }
                    if(alpha>0)output[(TextureSize-1-y)*TextureSize+x]=new Color32((byte)(red/alpha),(byte)(green/alpha),(byte)(blue/alpha),(byte)(alpha/(Samples*Samples)));
                }
                Texture2D texture=new Texture2D(TextureSize,TextureSize,TextureFormat.RGBA32,false);
                texture.name="Flip mascot "+name;texture.wrapMode=TextureWrapMode.Clamp;texture.filterMode=FilterMode.Bilinear;
                texture.SetPixels32(output);texture.Apply(false,true);return texture;
            }
        }
    }
}
