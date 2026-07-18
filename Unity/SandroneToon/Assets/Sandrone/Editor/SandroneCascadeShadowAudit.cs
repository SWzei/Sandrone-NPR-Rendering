using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace SandroneToon.Editor
{
    public static class SandroneCascadeShadowAudit
    {
        [Serializable] public sealed class CaptureRecord
        {
            public string name;
            public float viewDistance,meanLuminance,darkRatio,midRatio,brightRatio;
            public int magentaPixels;
        }

        [Serializable] public sealed class Report
        {
            public string generatedUtc,evidenceSessionId,unityVersion,graphicsApi,graphicsDevice,renderPipeline;
            public int shadowCascadeCount,shaderCount,shaderCompilerMessageCount,captureCount,failureCount;
            public float shadowDistance;
            public Vector3 cascadeSplits;
            public List<CaptureRecord> captures=new();
            public List<string> failures=new();
        }

        private static readonly string[] ShaderPaths=
        {
            SandroneM3Bootstrap.ShaderPath,
            "Assets/Sandrone/Shaders/SandroneMaterialResponseM4.shader",
            "Assets/Sandrone/Shaders/SandroneFaceSDFM5.shader",
            "Assets/Sandrone/Shaders/SandroneHairEyeM6.shader",
            "Assets/Sandrone/Shaders/SandroneHairEyeEmissionM8.shader"
        };

        [MenuItem("Sandrone/M3/Audit PC Cascade Shadow Contract")]
        public static void Run()
        {
            var session=SandroneEvidenceSession.EnsureActive("M0-M9 Windows PC verification");
            var report=new Report{generatedUtc=DateTime.UtcNow.ToString("O"),evidenceSessionId=session.sessionId,unityVersion=Application.unityVersion,
                graphicsApi=SystemInfo.graphicsDeviceType.ToString(),graphicsDevice=SystemInfo.graphicsDeviceName,shaderCount=ShaderPaths.Length};
            void Fail(bool condition,string message){if(!condition)report.failures.Add(message);}

            Fail(SystemInfo.graphicsDeviceType==GraphicsDeviceType.Direct3D11||SystemInfo.graphicsDeviceType==GraphicsDeviceType.Direct3D12,
                "Audit requires a real D3D11 or D3D12 device, got "+SystemInfo.graphicsDeviceType);
            var pipeline=AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset");
            Fail(pipeline!=null,"PC_RPAsset missing.");
            if(pipeline!=null)
            {
                report.renderPipeline=pipeline.name;report.shadowCascadeCount=pipeline.shadowCascadeCount;report.shadowDistance=pipeline.shadowDistance;
                report.cascadeSplits=pipeline.cascade4Split;
                Fail(pipeline.shadowCascadeCount==4,"PC pipeline is not configured for four cascades.");
            }

            foreach(var path in ShaderPaths)
            {
                var shader=AssetDatabase.LoadAssetAtPath<Shader>(path);Fail(shader!=null&&shader.isSupported,"Shader missing/unsupported: "+path);
                if(shader!=null)
                {
                    var messages=ShaderUtil.GetShaderMessages(shader);report.shaderCompilerMessageCount+=messages.Length;
                    foreach(var message in messages)report.failures.Add(path+": "+message.message);
                }
                var absolute=Path.GetFullPath(Path.Combine(Application.dataPath,"..",path));var source=File.Exists(absolute)?File.ReadAllText(absolute):string.Empty;
                var correct=source.Contains("defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)",StringComparison.Ordinal)&&
                            source.Contains("defined(MAIN_LIGHT_CALCULATE_SHADOWS)",StringComparison.Ordinal)&&
                            Regex.IsMatch(source,@"shadowCoord\s*=\s*TransformWorldToShadowCoord\(input\.positionWS\)")&&
                            Regex.IsMatch(source,@"GetMainLight\(shadowCoord\)")&&!Regex.IsMatch(source,@"GetMainLight\(input\.shadowCoord\)");
                Fail(correct,"Cascade coordinate contract mismatch: "+path);
            }

            if(pipeline!=null) CaptureRuntime(report,pipeline,Fail);
            report.captureCount=report.captures.Count;report.failureCount=report.failures.Count;
            Directory.CreateDirectory(Root());File.WriteAllText(Path.Combine(Root(),"CascadeShadowAudit.json"),JsonUtility.ToJson(report,true));
            if(report.failureCount>0)throw new BuildFailedException("Cascade shadow audit failed:\n"+string.Join("\n",report.failures));
            Debug.Log($"[Sandrone Cascade] {report.graphicsApi} passed: shaders={report.shaderCount}, captures={report.captureCount}, compilerMessages={report.shaderCompilerMessageCount}.");
        }

        private static void CaptureRuntime(Report report,UniversalRenderPipelineAsset pipeline,Action<bool,string> fail)
        {
            var previousDefault=GraphicsSettings.defaultRenderPipeline;var previousQuality=QualitySettings.renderPipeline;
            var scene=EditorSceneManager.OpenScene(SandroneM3Bootstrap.ScenePath,OpenSceneMode.Single);
            var controller=UnityEngine.Object.FindFirstObjectByType<SandroneM3Controller>();var camera=UnityEngine.Object.FindFirstObjectByType<Camera>();
            fail(scene.IsValid()&&controller!=null&&camera!=null,"M3 scene/controller/camera missing for runtime audit.");if(controller==null||camera==null)return;
            var root=controller.CharacterRoot;var rootPosition=root.position;var rootRotation=root.rotation;var cameraPosition=camera.transform.position;var cameraRotation=camera.transform.rotation;
            var cameraSize=camera.orthographicSize;var cameraFar=camera.farClipPlane;var debug=controller.DebugMode;var lightRotation=controller.MainLight.transform.rotation;
            try
            {
                GraphicsSettings.defaultRenderPipeline=pipeline;QualitySettings.renderPipeline=pipeline;root.SetPositionAndRotation(Vector3.zero,Quaternion.identity);
                controller.DebugMode=SandroneM3DebugMode.CastShadowRaw;controller.SetLightDirectionToSource(SandroneM3Bootstrap.DefaultDirectionToLight);controller.Apply(true);
                camera.orthographic=true;camera.orthographicSize=.92f;camera.farClipPlane=Mathf.Max(60,pipeline.shadowDistance+5);camera.backgroundColor=Color.black;
                var splits=pipeline.cascade4Split;var distances=new[]{pipeline.shadowDistance*splits.x,pipeline.shadowDistance*splits.y,pipeline.shadowDistance*splits.z};
                for(var i=0;i<distances.Length;i++)
                {
                    CaptureAt(report,camera,Mathf.Max(1,distances[i]-.35f),$"Split{i+1}_Before");
                    CaptureAt(report,camera,distances[i]+.35f,$"Split{i+1}_After");
                }
                camera.transform.position=new Vector3(3.2f,1.2f,5.5f);camera.transform.rotation=Quaternion.LookRotation(new Vector3(0,.82f,0)-camera.transform.position,Vector3.up);Capture(report,camera,"View_ThreeQuarter",camera.transform.position.magnitude);
                camera.transform.position=new Vector3(-3.2f,.9f,5.5f);camera.transform.rotation=Quaternion.LookRotation(new Vector3(0,.82f,0)-camera.transform.position,Vector3.up);Capture(report,camera,"View_OppositeThreeQuarter",camera.transform.position.magnitude);
                controller.SetLightDirectionToSource(Vector3.right);controller.Apply(true);CaptureAt(report,camera,distances[1],"Light_Side");
                controller.SetLightDirectionToSource(-Vector3.forward);controller.Apply(true);CaptureAt(report,camera,distances[1],"Light_Back");
                foreach(var capture in report.captures)
                {
                    fail(capture.meanLuminance>.005f,$"Empty/black capture: {capture.name}, mean={capture.meanLuminance:F6}");
                    fail(capture.magentaPixels==0,$"Magenta shader-error pixels in {capture.name}: {capture.magentaPixels}");
                }
            }
            finally
            {
                root.SetPositionAndRotation(rootPosition,rootRotation);camera.transform.SetPositionAndRotation(cameraPosition,cameraRotation);camera.orthographicSize=cameraSize;camera.farClipPlane=cameraFar;
                controller.MainLight.transform.rotation=lightRotation;controller.DebugMode=debug;controller.Apply(true);GraphicsSettings.defaultRenderPipeline=previousDefault;QualitySettings.renderPipeline=previousQuality;
            }
        }

        private static void CaptureAt(Report report,Camera camera,float distance,string name)
        {
            camera.transform.position=new Vector3(0,.82f,distance);camera.transform.rotation=Quaternion.LookRotation(Vector3.back,Vector3.up);Capture(report,camera,name,distance);
        }

        private static void Capture(Report report,Camera camera,string name,float distance)
        {
            const int width=512,height=768;var previous=RenderTexture.active;var oldTarget=camera.targetTexture;
            var target=new RenderTexture(width,height,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB);var image=new Texture2D(width,height,TextureFormat.RGB24,false);
            try
            {
                camera.targetTexture=target;target.Create();camera.Render();RenderTexture.active=target;image.ReadPixels(new Rect(0,0,width,height),0,0);image.Apply();
                var pixels=image.GetPixels32();double luminance=0;var dark=0;var mid=0;var bright=0;var magenta=0;
                foreach(var pixel in pixels)
                {
                    var value=(pixel.r*.2126+pixel.g*.7152+pixel.b*.0722)/255.0;luminance+=value;
                    if(value<.1)dark++;else if(value<.9)mid++;else bright++;if(pixel.r>220&&pixel.b>220&&pixel.g<64)magenta++;
                }
                var count=(float)pixels.Length;report.captures.Add(new CaptureRecord{name=name,viewDistance=distance,meanLuminance=(float)(luminance/pixels.Length),darkRatio=dark/count,midRatio=mid/count,brightRatio=bright/count,magentaPixels=magenta});
                File.WriteAllBytes(Path.Combine(Root(),name+".png"),image.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture=oldTarget;RenderTexture.active=previous;UnityEngine.Object.DestroyImmediate(image);target.Release();UnityEngine.Object.DestroyImmediate(target);
            }
        }

        private static string Root()
        {
            var api=SystemInfo.graphicsDeviceType==GraphicsDeviceType.Direct3D12?"D3D12":"D3D11";
            var path=Path.GetFullPath(Path.Combine(Application.dataPath,"../TestArtifacts/Audit/CascadeShadowContract",api));Directory.CreateDirectory(path);return path;
        }
    }
}
