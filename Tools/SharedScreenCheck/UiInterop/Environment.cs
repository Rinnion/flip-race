using System.Collections.Generic;
using UnityEngine;
using FlipRace.Presentation;

namespace FlipRace.SharedScreenCheck.Interop
{
    public static class Prefs
    {
        static readonly Dictionary<string,string> Strings=new Dictionary<string,string>();
        static readonly Dictionary<string,int> Ints=new Dictionary<string,int>();
        public static string GetString(string key,string fallback="")=>Strings.TryGetValue(key,out var value)?value:fallback;
        public static int GetInt(string key,int fallback=0)=>Ints.TryGetValue(key,out var value)?value:fallback;
        public static void SetString(string key,string value)=>Strings[key]=value;
        public static void SetInt(string key,int value)=>Ints[key]=value;
        public static void Save(){}
        public static void Clear(){Strings.Clear();Ints.Clear();}
    }
    public static class Clock { public static double realtimeSinceStartupAsDouble;public static int frameCount; }
    public static class Display { public static int width,height,sleepTimeout; public static float PixelRatio=1; public static Rect? SafeAreaOverride; public static Rect safeArea { get { return SafeAreaOverride ?? new Rect(0,0,width,height); } } }
    public static class Devices
    {
        public static bool multiTouchEnabled,simulateMouseWithTouches;
        public static Touch[] Touches=System.Array.Empty<Touch>();
        public static HashSet<KeyCode> Keys=new HashSet<KeyCode>();
        public static bool MouseHeld,MouseDown;
        public static Vector3 mousePosition;
        public static int touchCount=>Touches.Length;
        public static Touch GetTouch(int index)=>Touches[index];
        public static bool GetKeyDown(KeyCode key)=>Keys.Contains(key);
        public static bool GetMouseButton(int button)=>button==0&&MouseHeld;
        public static bool GetMouseButtonDown(int button)=>button==0&&MouseDown;
        public static void Clear(){Touches=System.Array.Empty<Touch>();Keys.Clear();MouseHeld=MouseDown=false;}
    }
    public static class MascotTextures
    {
        public static readonly Rect UnitBounds=new Rect(-1.5f,-1.75f,3,3);
        public static Texture2D Create(int hero,int player)=>null;
        public static Texture2D GetFrame(int hero,int player,float time,bool running,bool airborne,bool recovering)=>null;
        public static Texture2D GetMotionFrame(int hero,int player,float visualTime,float motionTime,float flipProgress,bool running,bool airborne,bool recovering,MascotReaction reaction,float reactionAge)=>null;
    }
}
