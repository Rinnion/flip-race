using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace FlipRace.Core
{
    public static class TrackFingerprint
    {
        // Fixed ordered tokens, independent of JSON property order, whitespace,
        // locale, serializer and platform. References are deliberately excluded.
        public static string Compute(Track track,TrackProfilePolicy policy=TrackProfilePolicy.Any,
            TrackWarningMode warning=TrackWarningMode.Selected)
        {
            if(track==null)throw new ArgumentNullException(nameof(track));
            using(var bytes=new MemoryStream())using(var writer=new BinaryWriter(bytes,Encoding.UTF8,true))
            {
                writer.Write(TrackJson.PhysicsVersion);writer.Write((int)policy);writer.Write((int)warning);
                Text(writer,track.Id);Text(writer,track.Name);Text(writer,track.Focus);Number(writer,track.Length);writer.Write(track.NegativeEnabled);
                writer.Write(track.Obstacles==null?-1:track.Obstacles.Length);
                if(track.Obstacles!=null)foreach(var o in track.Obstacles){writer.Write(o!=null);if(o==null)continue;
                    Text(writer,o.Id);Text(writer,o.Kind);Number(writer,o.X);Number(writer,o.Width);Number(writer,o.Height);
                    Number(writer,o.GapTop);Number(writer,o.GapBottom);Number(writer,o.TopOffsetX);Number(writer,o.BottomOffsetX);writer.Write(o.Hidden);
                    Nullable(writer,o.HiddenRevealDistance);Nullable(writer,o.HiddenWarningDuration);}
                writer.Write(track.RiskZones==null?-1:track.RiskZones.Length);
                if(track.RiskZones!=null)foreach(var z in track.RiskZones){writer.Write(z!=null);if(z==null)continue;
                    Text(writer,z.Id);Text(writer,z.Kind);Text(writer,z.ObstacleId);Text(writer,z.Placement);writer.Write(z.FreeReward);
                    Number(writer,z.X);Number(writer,z.Y);Number(writer,z.Radius);Number(writer,z.ClearX);Number(writer,z.SafeFlipX);Number(writer,z.RiskFlipX);
                    Number(writer,z.LandingWidth);Number(writer,z.FinishWidth);Number(writer,z.FinishHeight);}
                writer.Write(track.Cues==null?-1:track.Cues.Length);
                if(track.Cues!=null)foreach(var cue in track.Cues){writer.Write(cue!=null);if(cue!=null){Number(writer,cue.X);Text(writer,cue.Text);}}
                Numbers(writer,track.SafeRoute);Numbers(writer,track.RiskRoute);writer.Flush();
                using(var sha=SHA256.Create()){var hash=sha.ComputeHash(bytes.ToArray());var text=new StringBuilder(64);foreach(byte b in hash)text.Append(b.ToString("x2"));return text.ToString();}
            }
        }
        static void Text(BinaryWriter writer,string value){writer.Write(value!=null);if(value!=null)writer.Write(value);}
        static void Number(BinaryWriter writer,double value){writer.Write(value==0?0d:value);}
        static void Nullable(BinaryWriter writer,double? value){writer.Write(value.HasValue);if(value.HasValue)Number(writer,value.Value);}
        static void Numbers(BinaryWriter writer,double[] values){writer.Write(values==null?-1:values.Length);if(values!=null)foreach(double value in values)Number(writer,value);}
    }
}
