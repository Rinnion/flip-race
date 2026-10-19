using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using FlipRace.Core;

internal static partial class Program
{
    static string N(double value) { return value.ToString("R",CultureInfo.InvariantCulture); }
    static string Q(string value) { return value==null?"null":"\""+value.Replace("\\","\\\\").Replace("\"","\\\"")+"\""; }
    static string ArrayText(double[] values) { return "new double[] { "+string.Join(", ",values.Select(N))+" }"; }
    static string Suffix(Course c) { return c.Id=="rhythm-cascade"?"Cascade":c.Id=="rhythm-dense"?"Dense":"Waves"; }
    static void Emit(Course[] courses)
    {
        var s=new StringBuilder();
        s.AppendLine("using System;");s.AppendLine();s.AppendLine("namespace FlipRace.Core");s.AppendLine("{");
        s.AppendLine("    // Authored from verified module geometry. Route arrays are proof fixtures;");
        s.AppendLine("    // gameplay never reads them as controls. Catalogue activation is separate.");
        s.AppendLine("    public static partial class TracksRhythm");s.AppendLine("    {");
        foreach(var c in courses)s.AppendLine("        public const string "+Suffix(c)+"Id = "+Q(c.Id)+";");
        s.AppendLine("        public struct RhythmGroup { public int Number; public double StartX, EndX; public string Cue; }");
        s.AppendLine("        static bool IsFastWitness(FlightSettings flight)");
        s.AppendLine("        { return flight != null && flight.FirstAction == FirstAction.Flip && flight.Law == MotionLaw.Gravity");
        s.AppendLine("            && flight.AirRepeats == AirRepeatLimit.One && flight.GravityAcceleration == 2700 && flight.FlipImpulse == 35; }");
        s.AppendLine("        public static Track Draft(string id)");s.AppendLine("        {");
        foreach(var c in courses)s.AppendLine("            if (id == "+Suffix(c)+"Id) return WithNegatives(Build"+Suffix(c)+"());");
        s.AppendLine("            throw new ArgumentException(\"Unknown rhythm draft: \" + id);");s.AppendLine("        }");
        foreach(bool full in new[]{false,true}) {
            s.AppendLine("        public static double[] "+(full?"FullRoute":"SafeRoute")+"(string id, FlightSettings flight)");s.AppendLine("        {");
            foreach(var c in courses) {
                s.AppendLine("            if (id == "+Suffix(c)+"Id) return IsFastWitness(flight)");
                s.AppendLine("                ? "+ArrayText(c.Routes[RouteKey(FlightPresets.FastFlip,full)].Select(i=>i.X).ToArray()));
                s.AppendLine("                : "+ArrayText(c.Routes[RouteKey(FlightPresets.Chosen,full)].Select(i=>i.X).ToArray())+";");
            }
            s.AppendLine("            throw new ArgumentException(\"Unknown rhythm route: \" + id);");s.AppendLine("        }");
        }
        s.AppendLine("        public static RhythmGroup[] Groups(string id)");s.AppendLine("        {");
        foreach(var c in courses) {
            s.AppendLine("            if (id == "+Suffix(c)+"Id) return new[] {");
            foreach(var group in c.Modules.GroupBy(m=>m.Group))s.AppendLine("                new RhythmGroup { Number = "+(group.Key+1)+", StartX = "+N(group.First().X)+", EndX = "+N(group.Last().EndX)+", Cue = "+Q(c.Track.Cues[group.Key].Text)+" },");
            s.AppendLine("            };");
        }
        s.AppendLine("            throw new ArgumentException(\"Unknown rhythm groups: \" + id);");s.AppendLine("        }");
        foreach(var c in courses) {
            s.AppendLine("        static Track Build"+Suffix(c)+"()");s.AppendLine("        {");
            s.AppendLine("            return new Track(7920, new[] {");
            foreach(var o in c.Track.Obstacles)s.AppendLine("                new Obstacle { Id = "+Q(o.Id)+", Kind = "+Q(o.Kind)+", X = "+N(o.X)+", Width = "+N(o.Width)
                +", Height = "+N(o.Height)+", GapTop = "+N(o.GapTop)+", GapBottom = "+N(o.GapBottom)+", TopOffsetX = "+N(o.TopOffsetX)+", BottomOffsetX = "+N(o.BottomOffsetX)+" },");
            s.AppendLine("            }, new[] {");
            foreach(var cue in c.Track.Cues)s.AppendLine("                new Cue("+N(cue.X)+", "+Q(cue.Text)+"),");
            s.AppendLine("            }) {");
            string focus=c.Track.Focus;
            s.AppendLine("                Id = "+Suffix(c)+"Id, Name = "+Q(c.Name)+", Focus = "+Q(focus)+", NegativeEnabled = false,");
            s.AppendLine("                SafeRoute = SafeRoute("+Suffix(c)+"Id, FlightPresets.Chosen),");
            s.AppendLine("                RiskRoute = FullRoute("+Suffix(c)+"Id, FlightPresets.Chosen),");
            s.AppendLine("                RiskZones = new[] {");
            foreach(var z in c.Track.RiskZones)s.AppendLine("                    new RiskZone { Id = "+Q(z.Id)+", ObstacleId = "+Q(z.ObstacleId)+", Kind = "+Q(z.Kind)
                +", Placement = "+Q(z.Placement)+", FreeReward = "+z.FreeReward.ToString().ToLowerInvariant()+", X = "+N(z.X)+", Y = "+N(z.Y)+", Radius = "+N(z.Radius)
                +", ClearX = "+N(z.ClearX)+", SafeFlipX = "+N(z.SafeFlipX)+", RiskFlipX = "+N(z.RiskFlipX)+", LandingWidth = "+N(z.LandingWidth)+", FinishHeight = "+N(z.FinishHeight)+" },");
            s.AppendLine("                }");s.AppendLine("            };");s.AppendLine("        }");
        }
        s.AppendLine("    }");s.AppendLine("}");
        File.WriteAllText("Tools/RhythmCourseCompose/TracksRhythmFull-"+Revision+".cs.txt",s.ToString());
    }
}
