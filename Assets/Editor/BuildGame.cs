using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace FlipRace.Editor
{
    public static class BuildGame
    {
        [MenuItem("Flip Race/Build WebGL")]
        public static void WebGL()
        {
            BuildWebGL(false);
        }

        [MenuItem("Flip Race/Build V4 Control Trial")]
        public static void WebGLTrial()
        {
            BuildWebGL(true);
        }

        static void BuildWebGL(bool trial)
        {
            Directory.CreateDirectory("Assets/Scenes");
            const string scenePath = "Assets/Scenes/FlipRace.unity";
            if (!File.Exists(scenePath))
            {
                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                var camera = new GameObject("Camera").AddComponent<Camera>();
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.97f, 0.96f, 0.91f);
                camera.orthographic = true;
                EditorSceneManager.SaveScene(scene, scenePath);
            }
            EditorBuildSettings.scenes = new[] {new EditorBuildSettingsScene(scenePath, true)};
            PlayerSettings.companyName = "Katikaavry";
            PlayerSettings.productName = "Флип! — гонка катикаавров";
            PlayerSettings.bundleVersion = trial ? "4.0.1-trial" : "3.0.1";
            PlayerSettings.runInBackground = true;
            PlayerSettings.defaultWebScreenWidth = 960;
            PlayerSettings.defaultWebScreenHeight = 540;
            PlayerSettings.colorSpace = ColorSpace.Gamma;
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.WebGL, ScriptingImplementation.IL2CPP);
            PlayerSettings.SetManagedStrippingLevel(NamedBuildTarget.WebGL, ManagedStrippingLevel.Low);
            // IMGUI's built-in skin references native classes not found through
            // the runtime bootstrap; stripping them causes class-ID 115 errors.
            PlayerSettings.stripEngineCode = false;
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.WebGL, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.WebGL, new[] {GraphicsDeviceType.OpenGLES3});
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip;
            PlayerSettings.WebGL.decompressionFallback = true;
            PlayerSettings.WebGL.dataCaching = false;
            PlayerSettings.WebGL.initialMemorySize = 64;
            PlayerSettings.WebGL.maximumMemorySize = 256;
            PlayerSettings.WebGL.exceptionSupport = WebGLExceptionSupport.ExplicitlyThrownExceptionsOnly;
            PlayerSettings.WebGL.template = Directory.Exists("Assets/WebGLTemplates/Flip") ? "PROJECT:Flip" : "APPLICATION:Minimal";
            QualitySettings.vSyncCount = 0;
            QualitySettings.antiAliasing = 0;
            AssetDatabase.SaveAssets();
            var output = Environment.GetEnvironmentVariable("FLIP_BUILD_PATH") ?? (trial ? "Builds/WebGL-v4" : "Builds/WebGL");
            Directory.CreateDirectory(output);
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
                scenes = new[] {scenePath}, locationPathName = output,
                target = BuildTarget.WebGL, options = BuildOptions.None,
                extraScriptingDefines = trial ? new[] { "FLIP_V4_TRIAL" } : Array.Empty<string>()
            });
            if (report.summary.result != BuildResult.Succeeded)
                throw new Exception("WebGL build failed: " + report.summary.result);
            Debug.Log("FLIP_BUILD_OK " + report.summary.totalSize + " bytes at " + Path.GetFullPath(output));
        }
    }
}
