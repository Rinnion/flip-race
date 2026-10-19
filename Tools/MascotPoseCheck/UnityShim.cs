// CPU-only substitutes. No Unity/WebGL/GPU/upload/paint timings are claimed.
using System;
namespace UnityEngine
{
    public class Object { public static void Destroy(Object value) { } }
    public enum TextureFormat { RGBA32 }
    public enum TextureWrapMode { Clamp }
    public enum FilterMode { Bilinear }
    public sealed class Texture2D : Object
    {
        public static int Created;
        public static bool CapturePixels;
        public string name;
        public TextureWrapMode wrapMode;
        public FilterMode filterMode;
        Color32[] pixels;
        public Texture2D(int width, int height, TextureFormat format, bool mipmap) { Created++; }
        public void SetPixels32(Color32[] values) { pixels = (Color32[])values.Clone(); }
        public void Apply(bool mipmap, bool unreadable) { if (unreadable && !CapturePixels) pixels = null; }
        public string GetHash()
        {
            if(pixels==null)throw new InvalidOperationException("Pixel capture is disabled");
            var bytes=new byte[pixels.Length*4];
            for(int i=0;i<pixels.Length;i++){bytes[i*4]=pixels[i].r;bytes[i*4+1]=pixels[i].g;bytes[i*4+2]=pixels[i].b;bytes[i*4+3]=pixels[i].a;}
            return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes));
        }
    }
    public struct Color32
    {
        public byte r,g,b,a;
        public Color32(byte r,byte g,byte b,byte a) { this.r=r;this.g=g;this.b=b;this.a=a; }
    }
    public struct Color
    {
        public float r,g,b,a;
        public Color(float r,float g,float b,float a=1) { this.r=r;this.g=g;this.b=b;this.a=a; }
    }
    public struct Vector2
    {
        public float x,y;
        public Vector2(float x,float y) { this.x=x;this.y=y; }
        public static Vector2 operator +(Vector2 a,Vector2 b) { return new Vector2(a.x+b.x,a.y+b.y); }
        public static Vector2 operator *(float v,Vector2 a) { return new Vector2(v*a.x,v*a.y); }
    }
    public struct Rect
    {
        public float x,y,width,height;
        public Rect(float x,float y,float width,float height) { this.x=x;this.y=y;this.width=width;this.height=height; }
        public float xMin { get { return x; } }
        public float yMin { get { return y; } }
    }
    public static class Time { public static double realtimeSinceStartupAsDouble { get { return System.Diagnostics.Stopwatch.GetTimestamp()/(double)System.Diagnostics.Stopwatch.Frequency; } } }
    public static class Mathf
    {
        public const float PI = (float)Math.PI;
        public static float Sin(float x) { return MathF.Sin(x); }
        public static float Cos(float x) { return MathF.Cos(x); }
        public static float Sqrt(float x) { return MathF.Sqrt(x); }
        public static float Abs(float x) { return MathF.Abs(x); }
        public static float Min(float a,float b) { return MathF.Min(a,b); }
        public static int Min(int a,int b) { return Math.Min(a,b); }
        public static float Max(float a,float b) { return MathF.Max(a,b); }
        public static int Max(int a,int b) { return Math.Max(a,b); }
        public static float Clamp(float x,float low,float high) { return Math.Clamp(x,low,high); }
        public static int Clamp(int x,int low,int high) { return Math.Clamp(x,low,high); }
        public static float Clamp01(float x) { return Clamp(x,0,1); }
        public static int FloorToInt(float x) { return (int)MathF.Floor(x); }
        public static int CeilToInt(float x) { return (int)MathF.Ceiling(x); }
        public static int RoundToInt(float x) { return (int)MathF.Round(x); }
        public static float Repeat(float x,float length) { return Clamp(x-MathF.Floor(x/length)*length,0,length); }
    }
}
