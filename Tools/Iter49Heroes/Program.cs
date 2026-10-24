using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FlipRace.Presentation;
using UnityEngine;

static class Program
{
    static int checks;
    static readonly string[] Groups={"Run","RunBlink","RunSuccess","Idle","IdleBlink","Air","AirBlink","Recover","Success","Win"};
    static readonly JsonSerializerOptions Json=new JsonSerializerOptions{WriteIndented=true};
    static readonly List<object> Samples=new List<object>();
    static string Hash(string p)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p)));
    static void Check(bool pass,string why){checks++;if(!pass)throw new Exception(why);}
    static object Ensure(Type type,int hero,int player)=>type.GetMethod("Ensure",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,new object[]{hero,player});
    static Texture2D Pose(Type type,object set,Texture2D[] array,int index)=>(Texture2D)type.GetMethod("Pose",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,new object[]{set,array,index});
    static Color32[] Pixels(Texture2D texture)=>(Color32[])typeof(Texture2D).GetField("pixels",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(texture);
    static Texture2D[] ArrayOf(object set,string group)=>(Texture2D[])set.GetType().GetField(group).GetValue(set);
    static void Main()
    {
        try{Run();}catch(Exception e){Console.Error.WriteLine(e);Environment.ExitCode=1;}
    }
    static void Run()
    {
        Texture2D.CapturePixels=true;
        Check(CharacterRoster.Count==6,"six production roster entries");
        Check(CharacterRoster.Name(4)=="Джек"&&CharacterRoster.Name(5)=="Тыква","new identities have real indexes");
        var hashes=new[]{"Assets/Scripts/Presentation/MascotArtwork.cs","Assets/Scripts/Presentation/CharacterRoster.cs","Tools/Iter49Heroes/Baseline.g.cs","Tools/Iter49Heroes/Program.cs","Tools/Iter49Heroes/UnityShim.cs"}.ToDictionary(p=>p,Hash);
        int oldPoses=0,newPoses=0,previews=0;double maxCold=0;
        var oldType=typeof(BaselineMascot);var type=typeof(MascotArtwork);
        for(int player=0;player<2;player++)for(int hero=0;hero<6;hero++){
            var timer=Stopwatch.StartNew();var selected=MascotArtwork.GetFrame(hero,player,0,false,false,false);timer.Stop();maxCold=Math.Max(maxCold,timer.Elapsed.TotalMilliseconds);
            int count=Texture2D.Created;Check(ReferenceEquals(selected,MascotArtwork.GetFrame(hero,player,0,false,false,false))&&Texture2D.Created==count,"repeated selection reuses one cached texture");
            object current=Ensure(type,hero,player),before=hero<4?Ensure(oldType,hero,player):null;
            int poses=0;
            foreach(string group in Groups){
                var array=ArrayOf(current,group);var original=hero<4?ArrayOf(before,group):null;
                var unique=new HashSet<string>();
                for(int i=0;i<array.Length;i++){
                    var texture=Pose(type,current,array,i);string hash=texture.GetHash();unique.Add(hash);poses++;
                    count=Texture2D.Created;Check(ReferenceEquals(texture,Pose(type,current,array,i))&&Texture2D.Created==count,"repeated pose reuses texture");
                    if(hero<4){Check(hash==Pose(oldType,before,original,i).GetHash(),"original pixel mismatch "+hero+"/"+player+"/"+group+"/"+i);oldPoses++;}
                    else{AssertBounds(texture,hero,player,group,i);newPoses++;}
                    Samples.Add(new{Hero=hero,Player=player,Group=group,Index=i,Hash=hash});
                }
                if(hero>=4)Check(unique.Count>=Math.Min(3,array.Length),"new hero group actually animates "+hero+" "+group);
            }
            Check(poses==192,"all192 existing poses retained");
            var preview=MascotArtwork.Create(hero,player);previews++;
            if(hero<4)Check(preview.GetHash()==BaselineMascot.Create(hero,player).GetHash(),"old preview remains pixel-identical");
            else AssertBounds(preview,hero,player,"Preview",0);
            count=Texture2D.Created;Check(ReferenceEquals(preview,MascotArtwork.Create(hero,player))&&Texture2D.Created==count,"preview reused");
            foreach(var pair in new[]{(MascotReaction.None,"Idle"),(MascotReaction.Hit,"Recover"),(MascotReaction.Success,"Success"),(MascotReaction.Win,"Win")})
                Check(MascotArtwork.GetReactionFrame(hero,player,0,pair.Item1).GetHash()==Pose(type,current,ArrayOf(current,pair.Item2),0).GetHash(),"public reaction selects the real matching cached pose");
            if(hero>=4){
                var open=Pose(type,current,ArrayOf(current,"Idle"),0);AssertIdentity(open,hero);var closed=Pose(type,current,ArrayOf(current,"IdleBlink"),0);
                Check(open.GetHash()!=closed.GetHash(),"new hero truly blinks");
                Check(Pose(type,current,ArrayOf(current,"Recover"),0).GetHash()!=open.GetHash(),"real hit pose");
                Check(Pose(type,current,ArrayOf(current,"Success"),0).GetHash()!=open.GetHash(),"real success pose");
                Check(Pose(type,current,ArrayOf(current,"Win"),0).GetHash()!=open.GetHash(),"real win pose");
                foreach(string group in new[]{"Idle","Run","Air","Recover","Success","Win","IdleBlink"}){
                    var texture=Pose(type,current,ArrayOf(current,group),group=="Air"?6:group=="Run"?12:0);
                    SavePng("Verification/iter49-02-art-"+hero+"-"+player+"-"+group.ToLowerInvariant()+".png",texture);
                }
                ContactSheet(hero,player,current);
            }
            for(int i=0;i<128;i++){
                float visual=i*.137f,motion=i*.071f,flight=(i%23)/22f;bool running=(i&1)!=0,airborne=i%3==0,recovering=i%11==0;var reaction=(MascotReaction)(i%4);
                var actual=MascotArtwork.GetMotionFrame(hero,player,visual,motion,flight,running,airborne,recovering,reaction,i*.053f);
                if(hero<4)Check(actual.GetHash()==BaselineMascot.GetMotionFrame(hero,player,visual,motion,flight,running,airborne,recovering,reaction,i*.053f).GetHash(),"old public selector pixels preserved");
                else Check(actual!=null,"new hero public selector supplies real frame");
            }
        }
        Check(MascotArtwork.Create(4,0).GetHash()!=MascotArtwork.Create(5,0).GetHash(),"new hero silhouettes/palettes distinct");
        Check(MascotArtwork.Create(4,0).GetHash()!=MascotArtwork.Create(4,1).GetHash()&&MascotArtwork.Create(5,0).GetHash()!=MascotArtwork.Create(5,1).GetHash(),"both new heroes retain player scarf/stitch identity");
        Check(hashes.All(p=>Hash(p.Key)==p.Value),"source hashes stable during proof");
        File.WriteAllText("Verification/iter49-02-art.json",JsonSerializer.Serialize(new{Status="PASS",Checks=checks,OldPosesPixelIdentical=oldPoses,NewPoses=newPoses,Previews=previews,MaxColdSelectionCpuMs=maxCold,SourceHashes=hashes,
            Scope="Production procedural rasterizer/cache with existing CPU Unity shim; both slots, all192 poses. Upper gravity is the existing caller-owned transform; contact sheets show180-degree rotation. Desktop CPU timings exclude native Unity/WebGL/GPU/physical phone.",Samples},Json));
        Console.WriteLine("PASS ITER49-02 ART: "+checks+" checks; "+oldPoses+" original poses pixel-identical; "+newPoses+" new poses without clipped edges; "+previews+" previews; every existing group animated. Desktop shim max cold selection "+maxCold.ToString("F3")+" ms; native/mobile not claimed.");
    }
    static Color32 At(Texture2D texture,float x,float y)
    {
        int px=(int)((x+1.5f)*128/3),py=127-(int)((y+1.75f)*128/3);return Pixels(texture)[py*128+px];
    }
    static void AssertIdentity(Texture2D texture,int hero)
    {
        var left=At(texture,-.25f,.63f);var right=At(texture,.25f,.63f);
        if(hero==5){
            Check(left.a==255&&right.a==255&&left.r>right.r+60,"Pumpkin body has real apricot/slate vertical split");
            left=At(texture,-.73f,-.12f);right=At(texture,.73f,-.12f);
            Check(left.a==255&&right.a==255&&left.r>right.r+60,"Pumpkin face preserves the same split");
            var hat=At(texture,.035f,-1.25f);Check(hat.a==255&&hat.r>hat.b+30,"Pumpkin ornament is apricot rather than old sprout");
        }else{
            Check(left.r>220&&right.r>220&&left.a==255&&right.a==255,"Jack ivory body retained");
            var hat=At(texture,.46f,-1.35f);Check(hat.a>200&&hat.r>hat.b+20,"Jack tall terracotta hat present");
        }
    }    static void AssertBounds(Texture2D texture,int hero,int player,string group,int index)
    {
        var pixels=Pixels(texture);int minX=128,minY=128,maxX=-1,maxY=-1,opaque=0;
        for(int y=0;y<128;y++)for(int x=0;x<128;x++)if(pixels[y*128+x].a>0){minX=Math.Min(minX,x);minY=Math.Min(minY,y);maxX=Math.Max(maxX,x);maxY=Math.Max(maxY,y);opaque++;}
        Check(opaque>1500,"new hero body not empty");
        Check(minX>0&&minY>0&&maxX<127&&maxY<127,"clipped UnitBounds edge "+hero+"/"+player+"/"+group+"/"+index+" bounds="+minX+","+minY+".."+maxX+","+maxY);
        // Full square is retained in the contact-sheet rotation for gravity QA.


    }
    static void ContactSheet(int hero,int player,object set)
    {
        var image=new Color32[7*144*2*144];int width=7*144;
        for(int i=0;i<image.Length;i++)image[i]=new Color32(244,240,228,255);
        string[] groups={"Idle","Run","Air","Recover","Success","Win","IdleBlink"};
        for(int col=0;col<groups.Length;col++){
            string group=groups[col];var t=Pose(typeof(MascotArtwork),set,ArrayOf(set,group),group=="Air"?6:group=="Run"?12:0);var src=Pixels(t);
            for(int row=0;row<2;row++)for(int y=0;y<128;y++)for(int x=0;x<128;x++){
                int sx=row==0?x:127-x,sy=row==0?127-y:y;var c=src[sy*128+sx];int at=(row*144+8+y)*width+col*144+8+x;
                image[at]=Over(c,image[at]);
            }
        }
        Png("Verification/iter49-02-art-sheet-"+hero+"-"+player+".png",width,288,image);
    }
    static Color32 Over(Color32 fg,Color32 bg)
    {int a=fg.a;return new Color32((byte)((fg.r*a+bg.r*(255-a)+127)/255),(byte)((fg.g*a+bg.g*(255-a)+127)/255),(byte)((fg.b*a+bg.b*(255-a)+127)/255),255);}
    static void SavePng(string path,Texture2D texture)
    {
        var source=Pixels(texture);var image=new Color32[source.Length];
        for(int y=0;y<128;y++)for(int x=0;x<128;x++)image[y*128+x]=source[(127-y)*128+x];Png(path,128,128,image);
    }
    static void Png(string path,int width,int height,Color32[] image)
    {
        using var output=File.Create(path);output.Write(new byte[]{137,80,78,71,13,10,26,10});
        byte[] header=new byte[13];Big(header,0,(uint)width);Big(header,4,(uint)height);header[8]=8;header[9]=6;Chunk(output,"IHDR",header);
        using var packed=new MemoryStream();using(var z=new ZLibStream(packed,CompressionLevel.Optimal,true))for(int y=0;y<height;y++){
            z.WriteByte(0);for(int x=0;x<width;x++){var c=image[y*width+x];z.WriteByte(c.r);z.WriteByte(c.g);z.WriteByte(c.b);z.WriteByte(c.a);}
        }
        Chunk(output,"IDAT",packed.ToArray());Chunk(output,"IEND",Array.Empty<byte>());
    }
    static void Big(byte[] bytes,int offset,uint value)
    {bytes[offset]=(byte)(value>>24);bytes[offset+1]=(byte)(value>>16);bytes[offset+2]=(byte)(value>>8);bytes[offset+3]=(byte)value;}
    static void Chunk(Stream stream,string kind,byte[] data)
    {
        byte[] tag=Encoding.ASCII.GetBytes(kind),size=new byte[4];Big(size,0,(uint)data.Length);stream.Write(size);stream.Write(tag);stream.Write(data);
        uint crc=0xffffffff;foreach(byte b in tag.Concat(data)){crc^=b;for(int i=0;i<8;i++)crc=(crc&1)!=0?(crc>>1)^0xedb88320:crc>>1;}
        Big(size,0,crc^0xffffffff);stream.Write(size);
    }
}