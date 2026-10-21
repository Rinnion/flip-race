using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.IO;
using FlipRace.Core;

internal static partial class Program
{
    static bool quiet;
    static readonly JsonSerializerOptions Json = new JsonSerializerOptions{IncludeFields=true,WriteIndented=true};
    static double FT(double d)=>(Math.Sqrt(1225+5400*d)-35)/2700;
    static double JT(double d)=>(350-Math.Sqrt(122500-1325*d))/662.5;
    static double MT(double d)=>.2+(Math.Sqrt(217.5*217.5+1325*(d-56.75))-217.5)/662.5;
    sealed class Planned { public double X;public string Decision; }
    sealed class Planner
    {
        public GameState Game; public List<Planned> Inputs=new List<Planned>();public List<object> Marks=new List<object>();
        public bool Fast,Full;public string Decision;
        public Planner(FlightSettings f,bool full){Fast=f==FlightPresets.FastFlip;Full=full;Game=RaceSimulation.CreateTrial(TracksMixed.Get(),f);Tap(Game);RaceSimulation.Advance(Game,3);}
        public void At(double x){MoveTo(Game,x,.05);}
        public void Press(){Inputs.Add(new Planned{X=Game.Players[0].Distance,Decision=Decision});Tap(Game);}
        public void Do(double x){At(x);Press();}
        public void Delay(double t){AdvanceTime(Game,t,.02);Press();}
        public void Flip(double x){Do(x);if(!Fast)Delay(.2);}
        public void Mark(string name,double x){At(x);var p=Game.Players[0];if(!quiet)Console.WriteLine($"{(Fast?"fast":"main")}/{(Full?"full":"safe")} {name} X{x} Y{RaceSimulation.GetPlayerCenterY(p):F2} side{p.SupportSide} support{p.Support} speed{p.CurrentSpeed} +{p.BoostsEarned} hits{p.Collisions} {string.Join(',',p.HitObstacleIds)}");Marks.Add(new{name,x,time=Game.Elapsed,p.Support,p.SupportSide,p.AirFlipAvailable,p.CurrentSpeed,p.BoostsEarned,p.Collisions});}
    }
    static Planner Plan(FlightSettings f,bool full,int negatives=0)
    {
        var b=new Planner(f,full);bool fast=b.Fast;
        b.Decision="1: малый — своя сторона / точная стандартная посадка";
        if(fast){b.Do(full?430:530);b.Do(full?632-220*FT(164):596);}
        else b.Do(full?632-220*(350+Math.Sqrt(122500-1325*48))/662.5:632-220*350/662.5);
        b.Mark("first-small",900);
        if(fast){b.Do(980);b.Do(1046);}else b.Do(1082-220*350/662.5);
        b.Mark("second-small",1250);
        if(full){b.Decision="1: необязательная низкая награда";b.Do(1320);if(fast)b.Do(1480);}
        b.Mark("episode1",1700);
        b.Decision="2: уйти от большого / before";
        b.Flip(full?1950-220*(fast?FT(106):MT(106)):2050-(fast?110:140));
        b.Mark("large-opposite",2300);
        b.Decision="3: подготовка к прямому окну / высокая награда";
        if(full)b.Flip(2700-220*(fast?FT(46):JT(46)));else b.Flip(2560);
        b.Mark("straight-entry",2850);
        b.Decision="3: прямое окно";
        b.Do(fast?2952.3:3004-220*350/662.5);if(fast)b.Do(2988.6);
        b.Mark("straight-exit",3260);
        if(fast){b.Decision="3: выбрать сторону перед низким входом";b.Do(3300);}
        b.Decision="3: низкий проход";
        b.Do(3550-(fast?23.0829351839127:108.2264150943396));
        if((negatives&1)!=0){b.Decision="4: намеренная первая ловушка";if(fast){b.Do(3702.5);b.Do(3739.9);}else b.Do(3762.5);}
        b.Mark("low-exit",3800);
        b.Decision="4: центральный смещённый вход";
        b.Do(4050+(fast?5.1503213562917:-19.1551226858691));if(!fast)b.Delay(.2);
        b.Mark("offset-exit",4230);
        if(full){
            b.Decision="4: цепочка, центр";b.Flip(4400-220*(fast?FT(106):MT(106)));
            b.Decision="4: цепочка, другая высота под ускорением";b.Do(4570-330*(fast?FT(46):JT(46)));
        }
        b.Mark("chain-exit",4770);
        b.Mark("pad-entry",5000);
        b.Decision="5: крупная посадка / обход";
        double[] pad=fast?(full?new[]{237.5}:Array.Empty<double>()):(full?new[]{26d,64,92}:Array.Empty<double>());
        foreach(double x in pad)b.Do(5000+x);
        b.Mark("pad-exit",5580);
        b.Decision="5: отдельный возврат за after";
        double[] after=fast?(full?new[]{45.5,290.5}:new[]{340d}):(full?new[]{25d,69,264,315}:Array.Empty<double>());
        foreach(double x in after)b.Do(5740+x);
        b.Mark("after-exit",6330);
        b.Decision="6: перейти на зеркальную сторону";
        if((negatives&2)!=0){b.Decision="6: намеренная вторая ловушка";if(fast)b.Do(6445);else{b.Do(6442.5);b.Do(6475.5);}}
        else if(full)b.Flip(6400);
        b.Decision="6: зеркальный малый / низкий бонус";
        if(fast){if(full){b.Do(6730);b.Do(6796);}else b.Do(7030);}else b.Do(full?6693:6730.773584905661);
        b.Mark("final-small",7150);
        b.Decision="6: зеркальная смена стороны";b.Do(7260+(fast?5.1503213562917:-19.1551226858691));if(!fast)b.Delay(.2);
        if(!fast){b.Decision="6: подойти к верхнему низкому окну";b.Flip(7420);}
        b.Decision="6: верхний низкий проход";b.Do(7700-(fast?23.0829351839127:108.2264150943396));
        b.Mark("final-low",7825);
        if(full){b.Decision="6: точный финиш";b.Do(7876-110*(fast?FT(10)+FT(26):JT(10)+JT(26)));}
        Finish(b.Game,.05);
        return b;
    }
    static void Main(string[] args)
    {
        CultureInfo.CurrentCulture=CultureInfo.InvariantCulture;captureCollision=false;
        if(args.Contains("--windows")){Windows();return;}
        if(args.Contains("--recovery")){Recovery();return;}
        if(args.Contains("--semantics")){Semantics();return;}
        if(args.Contains("--verify")){VerifyFinal();return;}
        if(args.Contains("--bypass")){Bypass();return;}
        var exports=new List<object>();
        foreach(var f in new[]{FlightPresets.Chosen,FlightPresets.FastFlip})foreach(bool full in new[]{false,true}){
            try{var b=Plan(f,full);var p=b.Game.Players[0];Console.WriteLine($"END {f.FirstAction}/{full}: {p.FinishTime} +{p.BoostsEarned} finish{p.FinishBoostsEarned} hits{p.Collisions}");
                exports.Add(new{profile=f==FlightPresets.Chosen?"chosen":"fast-flip",variant=full?"full":"safe",track=b.Game.Track,inputs=b.Inputs,marks=b.Marks,finish=p.FinishTime});}
            catch(Exception ex){Console.WriteLine("PLAN FAILED "+f.FirstAction+"/"+full+" "+ex.Message);}
        }
        File.WriteAllText("Verification/mixed-course-draft-r2.json",JsonSerializer.Serialize(exports,Json));
    }
}
