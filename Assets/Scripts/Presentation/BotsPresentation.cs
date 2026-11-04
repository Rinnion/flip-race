using System;
using FlipRace.Core;
using UnityEngine;
namespace FlipRace.Presentation
{
    public enum BotFrameCohort { Active, Countdown, Paused, Suppressed }
    public readonly struct BotFrameSample
    {
        public readonly int Frame, PlanCalls, DiagnosticCalls;
        public readonly double Delta, Elapsed, AdvanceMilliseconds, DiagnosticsMilliseconds;
        public readonly BotFrameCohort Cohort;
        public readonly bool AdvanceInvoked, CaptureFault;
        public BotFrameSample(int frame,double delta,double elapsed,BotFrameCohort cohort,bool invoked,int plans,double advance,double diagnostics=0,int diagnosticCalls=0,bool captureFault=false)
        {Frame=frame;Delta=delta;Elapsed=elapsed;Cohort=cohort;AdvanceInvoked=invoked;PlanCalls=plans;AdvanceMilliseconds=advance;DiagnosticsMilliseconds=diagnostics;DiagnosticCalls=diagnosticCalls;CaptureFault=captureFault;}
        public BotFrameSample WithDiagnostics(double milliseconds,bool captureFault=false)
        {return new BotFrameSample(Frame,Delta,Elapsed,Cohort,AdvanceInvoked,PlanCalls,AdvanceMilliseconds,DiagnosticsMilliseconds+milliseconds,DiagnosticCalls+1,CaptureFault||captureFault);}
    }
    // Fixed-size upper-bound histogram; no frame-time sorting or outlier removal.
    public sealed class BotTimingDistribution
    {
        public const double BinMilliseconds=.05;
        readonly int[] bins=new int[1001];
        public int Count { get; private set; }
        public double Sum { get; private set; }
        public double Maximum { get; private set; }
        public int Over50 { get; private set; }
        int Bin(double value){return Math.Min(1000,(int)Math.Ceiling(Math.Min(value,50)/BinMilliseconds));}
        internal void Add(double value)
        {bins[Bin(value)]++;Count++;Sum+=value;if(value>Maximum)Maximum=value;if(value>50)Over50++;}
        internal void Replace(double before,double after)
        {bins[Bin(before)]--;bins[Bin(after)]++;Sum+=after-before;if(after>Maximum)Maximum=after;if(before>50)Over50--;if(after>50)Over50++;}
        public double P95UpperBound
        {
            get{if(Count==0)return 0;int rank=(int)Math.Ceiling(Count*.95),total=0;for(int i=0;i<bins.Length;i++){total+=bins[i];if(total>=rank)return i==1000?Maximum:i*BinMilliseconds;}return Maximum;}
        }
    }
    public sealed class BotFramePerformance
    {
        public const int Capacity=10000;
        readonly BotFrameSample[] samples=new BotFrameSample[Capacity];
        public int StoredCount { get; private set; }
        public int TotalFrames { get; private set; }
        public int ActiveFrames { get; private set; }
        public int CountdownFrames { get; private set; }
        public int PausedFrames { get; private set; }
        public int SuppressedFrames { get; private set; }
        public int ZeroPlanActiveFrames { get; private set; }
        public int OverflowFrames { get; private set; }
        public int CaptureFaultFrames { get; private set; }
        public int ActiveCaptureFaultFrames { get; private set; }
        public bool Terminal { get; private set; }
        public bool Sealed { get; private set; }
        public int TerminalFrame { get; private set; } = -1;
        public BotTimingDistribution Advance { get; } = new BotTimingDistribution();
        public BotTimingDistribution Diagnostics { get; } = new BotTimingDistribution();
        public BotTimingDistribution Combined { get; } = new BotTimingDistribution();
        public BotFrameSample[] CopySamples(){var copy=new BotFrameSample[StoredCount];Array.Copy(samples,copy,StoredCount);return copy;}
        internal void Seal(){if(Terminal)Sealed=true;}
        internal int CohortAt(int frame){return StoredCount>0&&samples[StoredCount-1].Frame==frame?(int)samples[StoredCount-1].Cohort:-1;}
        internal void Record(BotFrameSample sample,bool terminal)
        {
            if(Terminal)return;
            TotalFrames++;
            if(sample.CaptureFault){CaptureFaultFrames++;if(sample.Cohort==BotFrameCohort.Active)ActiveCaptureFaultFrames++;}
            if(sample.Cohort==BotFrameCohort.Active){ActiveFrames++;if(sample.PlanCalls==0)ZeroPlanActiveFrames++;Advance.Add(sample.AdvanceMilliseconds);Diagnostics.Add(0);Combined.Add(sample.AdvanceMilliseconds);}
            else if(sample.Cohort==BotFrameCohort.Countdown)CountdownFrames++;
            else if(sample.Cohort==BotFrameCohort.Paused)PausedFrames++;
            else SuppressedFrames++;
            if(StoredCount<Capacity)samples[StoredCount++]=sample;else OverflowFrames++;
            if(terminal){Terminal=true;TerminalFrame=sample.Frame;}
        }
        internal void RecordDiagnostics(int frame,double milliseconds,bool captureFault=false)
        {
            if(Sealed||StoredCount==0)return;
            int i=StoredCount-1;var old=samples[i];
            if(old.Frame!=frame)return;
            var next=old.WithDiagnostics(milliseconds,captureFault);samples[i]=next;
            if(!old.CaptureFault&&next.CaptureFault){CaptureFaultFrames++;if(old.Cohort==BotFrameCohort.Active)ActiveCaptureFaultFrames++;}
            if(old.Cohort==BotFrameCohort.Active)
            {Diagnostics.Replace(old.DiagnosticsMilliseconds,next.DiagnosticsMilliseconds);Combined.Replace(old.AdvanceMilliseconds+old.DiagnosticsMilliseconds,next.AdvanceMilliseconds+next.DiagnosticsMilliseconds);}
        }
    }
    public sealed partial class FlipRaceGame
    {
        bool pendingBot;
        BotDifficulty pendingBotDifficulty = BotDifficulty.Novice;
        StochasticBotController botController;
        BotFramePerformance botFramePerformance;
        bool botFramePending,botFrameAdvanced;
        int botFrameOrdinal,botFramePlans;
        double botFrameDelta,botFrameAdvanceMs;
        BotFrameCohort botFrameCohort;
        public BotFramePerformance BotPerformance { get { return BotEnabled?botFramePerformance:null; } }
        void BeginBotPerformanceFrame(double delta)
        {
            botFramePending=false;
            var perf=BotPerformance;if(perf==null)return;
            if(perf.Terminal){perf.Seal();return;}
            if(Game.Phase!="countdown"&&Game.Phase!="racing")return;
            if(Game.Players[1].Status!=PlayerStatus.Running)return;
            botFramePending=true;botFrameAdvanced=false;botFrameOrdinal=Time.frameCount;botFrameDelta=delta;botFrameAdvanceMs=0;botFramePlans=BotPlanningCalls;
            botFrameCohort=paused||showRules||showTracks||Portrait?BotFrameCohort.Paused:Game.Phase=="countdown"?BotFrameCohort.Countdown:suppressFrame?BotFrameCohort.Suppressed:BotFrameCohort.Active;
        }
        void AdvanceMeasuredBot(double delta)
        {
            long started=System.Diagnostics.Stopwatch.GetTimestamp();
            try{botController.Advance(delta);}
            finally{botFrameAdvanceMs=(System.Diagnostics.Stopwatch.GetTimestamp()-started)*1000d/System.Diagnostics.Stopwatch.Frequency;botFrameAdvanced=true;}
        }
        void EndBotPerformanceFrame(bool captureFault=false)
        {
            if(!botFramePending)return;
            if(botFrameCohort==BotFrameCohort.Countdown && Game.Elapsed>0)botFrameCohort=BotFrameCohort.Active;
            botFramePerformance.Record(new BotFrameSample(botFrameOrdinal,botFrameDelta,Game.Elapsed,botFrameCohort,botFrameAdvanced,BotPlanningCalls-botFramePlans,botFrameAdvanceMs,0,0,captureFault),Game.Players[1].Status!=PlayerStatus.Running);
            botFramePending=false;
        }
        internal int BotDiagnosticCohort(int frame){var p=BotPerformance;return p==null?-1:p.CohortAt(frame);}
        internal void RecordBotDiagnosticsCost(int frame,double milliseconds,bool captureFault=false)
        {var perf=BotPerformance;if(perf!=null)perf.RecordDiagnostics(frame,milliseconds,captureFault);}
        uint botSessionSeed;
        bool botSeedInitialized;
        long botAttemptSequenceIndex;
        BotOpportunity[] botOpportunities = Array.Empty<BotOpportunity>();
        public uint BotSessionSeed { get { return botSessionSeed; } }
        public long BotAttemptSequenceIndex { get { return botAttemptSequenceIndex; } }
        uint NextBotSeed()
        {
            if (!botSeedInitialized)
            {
                botSessionSeed = BitConverter.ToUInt32(Guid.NewGuid().ToByteArray(), 0);
                botSeedInitialized = true;
            }
            // This invertible uint permutation avoids seed repeats during the
            // first 2^32 attempts. Menu, frame and pause paths never call it.
            if (botAttemptSequenceIndex >= uint.MaxValue) throw new InvalidOperationException("Bot session attempt limit reached.");
            unchecked
            {
                uint value = botSessionSeed + (uint)++botAttemptSequenceIndex * 0x9e3779b9u;
                value ^= value >> 16; value *= 0x7feb352du;
                value ^= value >> 15; value *= 0x846ca68bu;
                return value ^ (value >> 16);
            }
        }
        void InitializeBotController()
        {
            botController = new StochasticBotController(Game);
            botFramePerformance = new BotFramePerformance();
            botOpportunities = StochasticTrackMap.Create(Game.Track, Game.Flight).Opportunities;
        }
        public uint BotAttemptSeed { get { return BotEnabled && Game.StochasticBot != null ? Game.StochasticBot.Seed : 0; } }
        public double BotQuality { get { return BotEnabled && Game.StochasticBot != null ? Game.StochasticBot.Quality : 0; } }
        public double BotCollectProbability { get { return BotEnabled && Game.StochasticBot != null ? Game.StochasticBot.CollectProbability : 0; } }
        public double BotHitProbability { get { return BotEnabled && Game.StochasticBot != null ? Game.StochasticBot.HitProbability : 0; } }
        public System.Collections.Generic.IReadOnlyList<StochasticBotDecision> BotDecisions { get { return BotEnabled && botController != null ? botController.Decisions : Array.Empty<StochasticBotDecision>(); } }
        public System.Collections.Generic.IReadOnlyList<BotPlanningSample> BotPlanningSamples { get { return BotEnabled && botController != null ? botController.PlanningSamples : Array.Empty<BotPlanningSample>(); } }
        public StochasticBotDecision BotLastDecision { get { var d=BotDecisions; return d.Count==0?null:d[d.Count-1]; } }
        public BotPlanningSample BotLastPlan { get { var p=BotPlanningSamples; return p.Count==0?null:p[p.Count-1]; } }
        public int BotPlanningCalls { get { return BotEnabled && botController != null ? botController.PlanningCalls : 0; } }
        public int BotCandidateSimulations { get { return BotEnabled && botController != null ? botController.CandidateSimulations : 0; } }
        public int BotForecastSteps { get { return BotEnabled && botController != null ? botController.ForecastSteps : 0; } }
        public int BotForecastObservations { get { return BotEnabled && botController != null ? botController.ForecastObservations : 0; } }
        public int BotForecastHorizonViolations { get { return BotEnabled && botController != null ? botController.ForecastHorizonViolations : 0; } }
        public int BotForecastRollbacks { get { return BotEnabled && botController != null ? botController.ForecastTrialRollbacks : 0; } }
        public int BotSuppressedInputs { get { return BotEnabled && botController != null ? botController.SuppressedInputs : 0; } }
        public double BotMaximumRequestedHorizon { get { return BotEnabled && botController != null ? botController.MaximumRequestedHorizon : 0; } }
        public double BotMaximumTrialHorizon { get { return BotEnabled && botController != null ? botController.MaximumTrialHorizon : 0; } }
        public double BotMaximumTrialOvershoot { get { return BotEnabled && botController != null ? botController.MaximumTrialOvershoot : 0; } }
        public string BotLastPlanningFailure { get { return BotEnabled && botController != null ? botController.LastPlanningFailure : ""; } }
        public int BotGoalsTotal { get { return BotEnabled ? botOpportunities.Length : 0; } }
        public int BotObstacleCount { get { int n=0;if(BotEnabled)foreach(var o in botOpportunities)if(o.Kind==BotOpportunityKind.Obstacle)n++;return n; } }
        public int BotPositiveGoalCount { get { return BotGoalsTotal-BotObstacleCount; } }
        public int BotGoalsCompleted
        {
            get
            {
                if(!BotEnabled)return 0;
                var p=Game.Players[1];int count=0;
                foreach(var o in botOpportunities)
                {
                    if(o.Kind==BotOpportunityKind.Obstacle)
                    {if(!p.HitObstacleIds.Contains(o.ObstacleId)&&p.Distance>=o.EndPosition-1e-7)count++;}
                    else if(o.Kind==BotOpportunityKind.Finish)
                    {if(p.FinishBoostsEarned>0&&p.FinishBoostElapsed>=RaceConfig.FinishBoostDuration-1e-7)count++;}
                    else if(p.SuccessfulRiskIds.Contains(o.Id.Substring(o.Id.IndexOf(':')+1)))count++;
                }
                return count;
            }
        }
        static readonly string[] BotLevelNames = { "Новичок", "Любитель", "Уверенный", "Сильный", "Мастер" };
        public bool BotEnabled { get { return Game != null && Game.IsBot; } }
        public int SelectedBotDifficulty { get { return (int)(BotEnabled ? Game.BotDifficulty : pendingBotDifficulty); } }
        public int SelectedBotSeed { get { return BotEnabled ? Game.BotSeed : 0; } }
        public int BotInputCursor { get { return BotActionCursor; } }
        public int BotActionCursor { get { return !BotEnabled || botController == null ? 0 : botController.Cursor; } }
        public Rect BotMenuButtonRect { get { MeasureFlightMenu(); return ModeChoiceRect(2); } }
        Rect ModeChoiceRect(int option)
        { float cw=(flightPanel.width-40)/3; return new Rect(flightPanel.x+16+option*(cw+4),flightPanel.y+210,cw,28); }
        public Rect BotDifficultyRect(int option)
        { MeasureFlightMenu(); float cw=(flightPanel.width-48)/5; return new Rect(flightPanel.x+16+option*(cw+4),flightPanel.y+63,cw,44); }
        Rect BotPersonalArea(int slot)
        { Rect a=SoloOverlayRect; return slot==1?new Rect(a.x,a.y,a.width,Mathf.Max(70,a.height-140)):new Rect(a.x,a.yMax-140,a.width,140); }
        public Rect BotCharacterRect(int hero)
        { Rect a=BotPersonalArea(0);float w=Mathf.Min(a.width-16,560),cw=(w-8)/3;float top=a.y+Mathf.Max(0,(a.height-140)/2);return new Rect(a.center.x-w/2+hero%3*(cw+4),top+hero/3*48,cw,44); }
        public Rect BotStartRect { get { Rect a=BotPersonalArea(0);return new Rect(a.center.x-110,BotCharacterRect(0).y+96,220,44); } }
        public Rect BotRetryRect { get { Rect a=BotPersonalArea(0);return new Rect(a.center.x-214,a.yMax-45,210,44); } }
        public Rect BotTrackChoiceRect { get { Rect r=BotRetryRect;return new Rect(r.xMax+8,r.y,r.width,r.height); } }
        void EnterBotMenu()
        {
            if(flightSelection.IsCustom)return;
            pendingBot=true;pendingSolo=pendingParty=false;
            if(!RaceTracks.IsFull(pendingFlightTrack))pendingFlightTrack=RaceTracks.GetAt(0).Id;
            flightPage=8;BlockGameplayInput();
        }
        Rect ModeLegacyRect { get { return new Rect(flightPanel.xMax-160,flightPanel.y+39,144,22); } }
        void DrawModeChoices()
        {
            if(flightPage!=5)FlightButton(ModeLegacyRect,"Прежние виды ›",false,true,9);
            FlightButton(ModeChoiceRect(0),"Один ›",pendingSolo,true,10);
            FlightButton(ModeChoiceRect(1),"Компания ›",pendingParty,true,10);
            FlightButton(BotMenuButtonRect,flightSelection.IsCustom?"С ботом · готовый профиль":"С ботом ›",pendingBot,!flightSelection.IsCustom,9);
        }
        bool ModeChoicePointer(Vector2 p)
        {
            if(flightPage!=5 && ModeLegacyRect.Contains(p)){pendingBot=pendingSolo=pendingParty=false;flightPage=5;BlockGameplayInput();return true;}
            if(ModeChoiceRect(0).Contains(p)){pendingBot=false;EnterSoloMenu();return true;}
            if(ModeChoiceRect(1).Contains(p)){pendingBot=pendingSolo=false;pendingParty=true;flightPage=6;BlockGameplayInput();return true;}
            if(BotMenuButtonRect.Contains(p)){EnterBotMenu();return true;}
            return false;
        }
        void BotMenuPointer(Vector2 p)
        {
            for(int i=0;i<5;i++)if(BotDifficultyRect(i).Contains(p)){pendingBotDifficulty=(BotDifficulty)i;return;}
            for(int i=0;i<2;i++)if(PartyCameraRect(i).Contains(p)){pendingPartyCamera=(PartyCamera)i;return;}
        }
        void DrawBotChoices()
        {
            Text(new Rect(flightPanel.x+16,flightPanel.y+43,flightPanel.width-32,15),"УРОВЕНЬ СОПЕРНИКА",8,muted);
            for(int i=0;i<5;i++)FlightButton(BotDifficultyRect(i),BotLevelNames[i],(int)pendingBotDifficulty==i,true,10);
            Text(new Rect(flightPanel.x+16,flightPanel.y+109,flightPanel.width-32,15),"КАМЕРА",8,muted);
            FlightButton(PartyCameraRect(0),"Постоянный скролл",pendingPartyCamera==PartyCamera.Constant,true,10);
            FlightButton(PartyCameraRect(1),"За последним · без выбывания",pendingPartyCamera==PartyCamera.Lagger,true,10);
            label.wordWrap=true;
            Text(new Rect(flightPanel.x+16,flightPanel.y+175,flightPanel.width-32,32),!TrackProfileSupported?"Эта трасса поддерживает только Основной. Выбери его в настройках управления.":flightSelection.IsCustom?"Боты доступны только с Основным или Быстрым переворотом. Выбери готовый профиль.":!BotTrackSupported?"Для этой трассы нет подготовленного бота. Выбери другую трассу или человеческий режим.":"Один человек и бот. Награды личные. Оба бегут до своего исхода.",10,muted);
            label.wordWrap=false;DrawModeChoices();DrawFlightApplyFooter();
        }
        void ResetBotRace()
        {
            int hero=Game.Players[0].CharacterIndex;
            double warning=Game.HiddenWarningDuration;
            uint seed=NextBotSeed();
            Game=RaceSimulation.CreateStochasticBotRace(Game.Track,Game.Flight,Game.BotDifficulty,seed,Game.PartyCamera,PartyStartLeft,PartyWindowWidth);
            HiddenObstacles.ConfigureWarning(Game,warning);
            Game.Players[0].CharacterIndex=hero;Game.Players[1].CharacterIndex=(hero+1)%CharacterRoster.Count;
            InitializeBotController();
        }
        void BotPointer(Vector2 p)
        {
            if(Game.Phase=="ready")
            {
                for(int h=0;h<CharacterRoster.Count;h++)if(BotCharacterRect(h).Contains(p)){SelectCharacter(0,h);Game.Players[1].CharacterIndex=(h+1)%CharacterRoster.Count;return;}
                if(BotStartRect.Contains(p))Act(0);
            }
            else if(Game.Phase=="finished")
            { if(BotRetryRect.Contains(p))Act(0);else if(BotTrackChoiceRect.Contains(p))OpenTracks(); }
            else if(arena.Contains(p))LocalRacePress(0,p);
        }
        void DrawBotField()
        {
            Rect field=SharedFieldRect;Matrix4x4 canvas=GUI.matrix;
            GUI.matrix=canvas*Matrix4x4.Translate(new Vector3(field.x,field.y,0));
            var world=artwork.DrawSharedWorld(field.width,field.height,Game,animationTime);
            var human=world.Player(0);var bot=world.Player(1);
            bool bothVisible=Game.Players[0].Status!=PlayerStatus.Eliminated && Game.Players[1].Status!=PlayerStatus.Eliminated
                && world.View.IsPlayerVisible(0) && world.View.IsPlayerVisible(1);
            bool overlap=bothVisible && Mathf.Abs(human.X-bot.X)<Mathf.Max(human.Radius,bot.Radius)*1.2f
                && Mathf.Abs(human.Y-bot.Y)<Mathf.Max(human.Radius,bot.Radius)*1.2f;
            bool samePose=overlap && Mathf.Abs(human.X-bot.X)<.5f && Mathf.Abs(human.Y-bot.Y)<.5f
                && Mathf.Abs(Mathf.DeltaAngle(human.Rotation,bot.Rotation))<1;
            // Keep exact physical origins. Draw the translucent bot first when
            // poses differ; matching poses expose both existing texture halves.
            for(int pass=0;pass<2;pass++)
            {
                int slot=1-pass;
                if(Game.Players[slot].Status==PlayerStatus.Eliminated||!world.View.IsPlayerVisible(slot))continue;
                var hero=world.Player(slot);DrawSharedMascot(slot,hero,samePose,slot==1 && overlap && !samePose);
                artwork.DrawSharedPersonalForeground(field.width,field.height,Game,slot,animationTime,hero);
            }
            // Identity labels follow the true projection on opposite sides of
            // the body. They are painted after both heroes so neither hides one.
            for(int slot=0;slot<2;slot++)
            {
                if(Game.Players[slot].Status==PlayerStatus.Eliminated||!world.View.IsPlayerVisible(slot))continue;
                var hero=world.Player(slot);float side=slot==0?1:-1;
                float bx=Mathf.Clamp(hero.X+side*20,18,field.width-18);
                float by=Mathf.Clamp(hero.Y+side*(hero.Radius*2.2f+8),9,field.height-9);
                Round(new Rect(bx-17,by-8,34,16),5,pales[slot]);Text(new Rect(bx-17,by-8,34,16),slot==0?"ТЫ":"БОТ",8,accents[slot],true,TextAnchor.MiddleCenter);
            }
            GUI.matrix=canvas;
            Fill(new Rect(0,field.y,field.x,field.height),paper);Fill(new Rect(field.xMax,field.y,width-field.xMax,field.height),paper);
            if(Game.Phase=="ready"||Game.Phase=="finished")
            {
                for(int slot=0;slot<2;slot++)Fill(BotPersonalArea(slot),new Color(.98f,.984f,.949f,.97f));
                if(Game.Phase=="ready")
                {
                    Rect a=BotPersonalArea(1);float y=a.center.y-32;
                    GUI.color=Color.white;GUI.DrawTexture(new Rect(a.center.x-100,y,64,64),MascotArtwork.Create(Game.Players[1].CharacterIndex,1));
                    Text(new Rect(a.center.x-10,y+2,210,25),"Бот · "+BotLevelNames[(int)Game.BotDifficulty],16,accents[1],true);
                    Text(new Rect(a.center.x-10,y+29,210,24),PlayerName(1)+" · Готов",11,muted);
                    for(int h=0;h<CharacterRoster.Count;h++)
                    {
                        Rect c=BotCharacterRect(h);bool chosen=Game.Players[0].CharacterIndex==h;
                        StrokeRound(c,7,chosen?accents[0]:Hex(0xd9dece),chosen?pales[0]:paper);
                        float s=Mathf.Min(40,c.width*.35f);GUI.color=Color.white;GUI.DrawTexture(new Rect(c.x+2,c.y+2,s,40),MascotArtwork.Create(h,0));
                        Text(new Rect(c.x+s+4,c.y,c.width-s-6,44),CharacterRoster.Name(h),10,ink,chosen,TextAnchor.MiddleCenter);
                    }
                    Round(BotStartRect,7,pales[0]);Text(BotStartRect,"Начать →",12,accents[0],true,TextAnchor.MiddleCenter);
                }
                else
                {
                    for(int slot=0;slot<2;slot++)
                    {
                        Rect a=BotPersonalArea(slot);var p=Game.Players[slot];float y=a.y+Mathf.Max(2,(a.height-(slot==0?140:88))/2);
                        string who=slot==0?"Ты · "+PlayerName(0):"Бот · "+BotLevelNames[(int)Game.BotDifficulty]+" · "+PlayerName(1);
                        Text(new Rect(a.x+8,y,a.width-16,24),who,15,accents[slot],true,TextAnchor.MiddleCenter);
                        Text(new Rect(a.x+8,y+27,a.width-16,22),p.Status==PlayerStatus.Finished?"Финиш · "+Number(p.FinishTime.Value,2)+" с":"Выбыл слева · "+Number(p.Distance,0)+" ед",12,ink,true,TextAnchor.MiddleCenter);
                        Text(new Rect(a.x+8,y+53,a.width-16,22),"+"+Rewards(p)+" · −"+p.SlowsEarned+CoinScore(p)+" · ударов "+p.Collisions,10,muted,false,TextAnchor.MiddleCenter);
                    }
                    Round(BotRetryRect,7,pales[0]);Text(BotRetryRect,"Ещё раз →",11,accents[0],true,TextAnchor.MiddleCenter);
                    StrokeRound(BotTrackChoiceRect,7,Hex(0xd9dece),paper);Text(BotTrackChoiceRect,"Выбрать трассу",11,ink,true,TextAnchor.MiddleCenter);
                }
            }
            else if(Game.Phase=="countdown")Text(SoloCountdownRect,Math.Ceiling(Game.CountdownRemaining).ToString(),48,accents[0],true,TextAnchor.MiddleCenter);
            else for(int slot=0;slot<2;slot++)
            {
                var p=Game.Players[slot];string state=p.Status==PlayerStatus.Eliminated?"Выбыл":p.Status==PlayerStatus.Finished?"Финиш · "+Number(p.FinishTime.Value,2)+" с":!world.View.IsPlayerVisible(slot)?"Впереди →":"+"+Rewards(p)+CoinScore(p);
                Text(new Rect(arena.x+8,slot==1?field.y-48:field.yMax+8,arena.width-120,22),(slot==0?"Ты":"Бот · "+BotLevelNames[(int)Game.BotDifficulty])+" · "+state,10,accents[slot],true);
            }
        }
    }
}
