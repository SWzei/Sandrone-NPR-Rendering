using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Debug = UnityEngine.Debug;

namespace SandroneToon.Editor
{
    public static class SandroneM9Bootstrap
    {
        public const string ProfilePath="Assets/Sandrone/Configs/SandroneFinalProfile_M9.asset";
        public const string VolumeProfilePath="Assets/Sandrone/Configs/SandroneFinalVolume_M9.asset";
        public const string ScenePath="Assets/Sandrone/Tests/Scenes/ToonCalibration_M9.unity";
        public const string OverdrawShaderPath="Assets/Sandrone/Shaders/SandroneOverdrawAuditM9.shader";
        private static readonly Color Background=new(.153f,.149f,.149f,1f);

        [Serializable] public sealed class OverdrawReport
        {
            public string generatedUtc,evidenceSessionId, method="AdditiveFragmentCount_CharacterOutlineSword_NoPost_ZTestAlways_OriginalCullAndAlphaClip";
            public int width,height,foregroundPixels;public float coverage,meanLayers,p95Layers,maxLayers;
        }
        [Serializable] public sealed class PlayerBuildReport
        {
            public string generatedUtc,evidenceSessionId,result,platform,outputPath;public int errors,warnings;public double totalSeconds;public long totalBytes;
        }

        [MenuItem("Sandrone/M9/Build Final Composition and Player")]
        public static void Build()
        {
            SandroneEvidenceSession.EnsureActive("M0-M9 Windows PC verification");
            Debug.Log("[Sandrone M9] Build started; full M8 build/regression is a hard gate.");
            SandroneM8Bootstrap.Build();EnsureFolder("Assets/Sandrone/Configs");EnsureFolder("Assets/Sandrone/Tests/Scenes");
            AssetDatabase.ImportAsset(OverdrawShaderPath,ImportAssetOptions.ForceSynchronousImport|ImportAssetOptions.ForceUpdate);
            var profile=CreateProfile();var volume=CreateVolumeProfile(profile);CreateScene(profile,volume);
            AssetDatabase.SaveAssets();AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            EditorBuildSettings.scenes=new[]{new EditorBuildSettingsScene(ScenePath,true)};
            BuildPlayerAndRunProbe();SandroneM9Validator.ValidateAndWriteReport();
            Debug.Log("[Sandrone M9] Build, player probe and validation completed.");
        }

        private static SandroneM9FinalProfile CreateProfile()
        {
            var profile=AssetDatabase.LoadAssetAtPath<SandroneM9FinalProfile>(ProfilePath);
            if(profile==null){profile=ScriptableObject.CreateInstance<SandroneM9FinalProfile>();AssetDatabase.CreateAsset(profile,ProfilePath);}
            profile.EditorSet(-.08f,0f,0f,-18f,Color.white);EditorUtility.SetDirty(profile);return profile;
        }

        private static VolumeProfile CreateVolumeProfile(SandroneM9FinalProfile profile)
        {
            var volume=AssetDatabase.LoadAssetAtPath<VolumeProfile>(VolumeProfilePath);
            if(volume==null){volume=ScriptableObject.CreateInstance<VolumeProfile>();volume.name="SandroneFinalVolume_M9";AssetDatabase.CreateAsset(volume,VolumeProfilePath);}
            volume.components.RemoveAll(x=>x==null);
            if(!volume.TryGet(out Tonemapping tone)){tone=volume.Add<Tonemapping>(true);AssetDatabase.AddObjectToAsset(tone,volume);}
            if(!volume.TryGet(out ColorAdjustments color)){color=volume.Add<ColorAdjustments>(true);AssetDatabase.AddObjectToAsset(color,volume);}
            tone.active=true;tone.mode.Override(TonemappingMode.Neutral);color.active=true;color.postExposure.Override(profile.PostExposure);color.contrast.Override(profile.Contrast);
            color.colorFilter.Override(profile.ColorFilter);color.hueShift.Override(profile.HueShift);color.saturation.Override(profile.Saturation);
            EditorUtility.SetDirty(tone);EditorUtility.SetDirty(color);EditorUtility.SetDirty(volume);return volume;
        }

        private static void CreateScene(SandroneM9FinalProfile profile,VolumeProfile volumeProfile)
        {
            var scene=EditorSceneManager.OpenScene(SandroneM8Bootstrap.ScenePath,OpenSceneMode.Single);
            if(!EditorSceneManager.SaveScene(scene,ScenePath))throw new IOException("Could not clone M8 scene.");
            scene=EditorSceneManager.OpenScene(ScenePath,OpenSceneMode.Single);
            var camera=UnityEngine.Object.FindFirstObjectByType<Camera>();var m8=UnityEngine.Object.FindFirstObjectByType<SandroneM8VfxBloomController>();
            if(camera==null||m8==null)throw new InvalidOperationException("M9 clone is missing M8 camera/controller.");
            var volumeObject=new GameObject("M9_GlobalFinalComposition");var volume=volumeObject.AddComponent<Volume>();volume.isGlobal=true;volume.priority=20;volume.sharedProfile=volumeProfile;volume.weight=1;
            var controller=m8.CharacterRenderer.transform.root.gameObject.AddComponent<SandroneM9FinalController>();controller.Configure(camera,volume,profile);
            m8.CharacterRenderer.transform.root.gameObject.AddComponent<SandroneM9PerformanceProbe>();
            m8.EyeEmissionEnabled=true;m8.CrystalEmissionEnabled=true;m8.CrystalVisible=true;m8.BloomEnabled=true;m8.DebugMode=SandroneM8DebugMode.FinalColor;m8.Apply(true);
            ConfigureCamera(camera,new Vector3(0,.82f,4),new Vector3(0,.82f,0),.92f);controller.GradingEnabled=true;controller.AntiAliasingEnabled=true;controller.MobileQuality=false;controller.Apply();
            EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene,ScenePath);
            scene=EditorSceneManager.OpenScene(ScenePath,OpenSceneMode.Single);camera=UnityEngine.Object.FindFirstObjectByType<Camera>();controller=UnityEngine.Object.FindFirstObjectByType<SandroneM9FinalController>();m8=UnityEngine.Object.FindFirstObjectByType<SandroneM8VfxBloomController>();
            if(camera==null||controller==null||m8==null)throw new InvalidOperationException("Reloaded M9 scene incomplete.");
            CaptureEvidence(camera,controller,m8);WriteOverdrawAudit(camera,controller,m8);
            controller.GradingEnabled=true;controller.AntiAliasingEnabled=true;controller.MobileQuality=false;controller.Apply();
            ConfigureCamera(camera,new Vector3(0,.82f,4),new Vector3(0,.82f,0),.92f);EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene,ScenePath);
        }

        private static void CaptureEvidence(Camera camera,SandroneM9FinalController controller,SandroneM8VfxBloomController m8)
        {
            m8.EyeEmissionEnabled=true;m8.CrystalEmissionEnabled=true;m8.CrystalVisible=true;m8.BloomEnabled=true;m8.DebugMode=SandroneM8DebugMode.FinalColor;m8.Apply(true);
            Capture(camera,controller,false,false,false,994,1654,"AB/M9_M8Baseline_PostOff_AAOff.png");
            Capture(camera,controller,true,false,false,994,1654,"AB/M9_Neutral_PostOn_AAOff.png");
            Capture(camera,controller,true,true,false,994,1654,"ReferenceComparison/M9_Final_Neutral_SMAA.png");
            var profile=controller.GradingVolume.sharedProfile;profile.TryGet(out Tonemapping tone);var original=tone.mode.value;
            try{tone.mode.value=TonemappingMode.ACES;Capture(camera,controller,true,false,false,994,1654,"AB/M9_ACES_PostOn_AAOff.png");}
            finally{tone.mode.value=original;}
            Capture(camera,controller,true,true,false,768,1280,"Pipeline/M9_PC_SMAA.png");
            Capture(camera,controller,true,true,true,768,1280,"Pipeline/M9_Mobile_FXAA.png");
        }

        private static void Capture(Camera camera,SandroneM9FinalController controller,bool grading,bool aa,bool mobile,int width,int height,string relative)
        {
            var pipeline=AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>(mobile?"Assets/Settings/Mobile_RPAsset.asset":"Assets/Settings/PC_RPAsset.asset");
            GraphicsSettings.defaultRenderPipeline=pipeline;QualitySettings.renderPipeline=pipeline;controller.GradingEnabled=grading;controller.AntiAliasingEnabled=aa;controller.MobileQuality=mobile;controller.Apply();
            ConfigureCamera(camera,new Vector3(0,.82f,4),new Vector3(0,.82f,0),.92f);camera.backgroundColor=Background;CaptureCamera(camera,relative,width,height);
        }

        private static void WriteOverdrawAudit(Camera camera,SandroneM9FinalController controller,SandroneM8VfxBloomController m8)
        {
            var shader=AssetDatabase.LoadAssetAtPath<Shader>(OverdrawShaderPath);if(shader==null||!shader.isSupported)throw new InvalidOperationException("M9 overdraw shader missing.");
            var m7=UnityEngine.Object.FindFirstObjectByType<SandroneM7OutlineController>();var targets=new[]{m8.CharacterRenderer,m7.OutlineRenderer,m8.CrystalRenderer};
            var all=UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include,FindObjectsSortMode.None);var enabled=all.ToDictionary(x=>x,x=>x.enabled);
            var originals=targets.ToDictionary(x=>x,x=>x.sharedMaterials);var created=new List<Material>();var oldBackground=camera.backgroundColor;var data=camera.GetUniversalAdditionalCameraData();var oldPost=data.renderPostProcessing;
            try
            {
                foreach(var item in all)item.enabled=targets.Contains(item);
                foreach(var renderer in targets)
                {
                    var replacement=renderer.sharedMaterials.Select(source=>CreateOverdrawMaterial(shader,source,renderer==m7.OutlineRenderer,created)).ToArray();renderer.sharedMaterials=replacement;
                }
                controller.GradingEnabled=false;controller.AntiAliasingEnabled=false;controller.Apply();camera.backgroundColor=Color.black;ConfigureCamera(camera,new Vector3(0,.82f,4),new Vector3(0,.82f,0),.92f);data.renderPostProcessing=false;
                var pixels=RenderFloat(camera,768,1280);var layers=pixels.Select(x=>Mathf.Max(x.r,Mathf.Max(x.g,x.b))).Where(x=>x>.5f).OrderBy(x=>x).ToArray();
                var report=new OverdrawReport{generatedUtc=DateTime.UtcNow.ToString("O"),evidenceSessionId=SandroneEvidenceSession.CurrentSessionId,width=768,height=1280,foregroundPixels=layers.Length,coverage=(float)layers.Length/(768*1280),
                    meanLayers=layers.Length==0?0:layers.Average(),p95Layers=layers.Length==0?0:layers[Mathf.Clamp(Mathf.CeilToInt(layers.Length*.95f)-1,0,layers.Length-1)],maxLayers=layers.Length==0?0:layers[^1]};
                File.WriteAllText(Artifact("M9OverdrawAudit.json"),JsonUtility.ToJson(report,true));WriteOverdrawVisualization(pixels,768,1280,report.maxLayers,Artifact("Debug/M9_OverdrawHeatmap.png"));
            }
            finally
            {
                foreach(var pair in originals)pair.Key.sharedMaterials=pair.Value;foreach(var pair in enabled)pair.Key.enabled=pair.Value;foreach(var material in created)UnityEngine.Object.DestroyImmediate(material);
                camera.backgroundColor=oldBackground;data.renderPostProcessing=oldPost;controller.GradingEnabled=true;controller.AntiAliasingEnabled=true;controller.MobileQuality=false;controller.Apply();
            }
        }

        private static Material CreateOverdrawMaterial(Shader shader,Material source,bool outline,List<Material> created)
        {
            var material=new Material(shader);created.Add(material);if(source!=null)
            {
                if(source.HasProperty("_BaseMap"))material.SetTexture("_BaseMap",source.GetTexture("_BaseMap"));if(source.HasProperty("_BaseMap_ST"))material.SetVector("_BaseMap_ST",source.GetVector("_BaseMap_ST"));
                if(source.HasProperty("_BaseColor"))material.SetColor("_BaseColor",source.GetColor("_BaseColor"));var clip=source.HasProperty("_AlphaClip")&&source.GetFloat("_AlphaClip")>.5f;
                material.SetFloat("_AuditAlphaClip",clip?1:0);material.SetFloat("_Cutoff",source.HasProperty("_Cutoff")?source.GetFloat("_Cutoff"):.5f);
                material.SetFloat("_Cull",outline?1:source.HasProperty("_Cull")?source.GetFloat("_Cull"):2);
            }
            return material;
        }

        private static Color[] RenderFloat(Camera camera,int width,int height)
        {
            var active=RenderTexture.active;var rt=new RenderTexture(width,height,24,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);var image=new Texture2D(width,height,TextureFormat.RGBAFloat,false,true);
            try{camera.targetTexture=rt;rt.Create();camera.Render();RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,width,height),0,0);image.Apply();return image.GetPixels();}
            finally{camera.targetTexture=null;RenderTexture.active=active;UnityEngine.Object.DestroyImmediate(image);rt.Release();UnityEngine.Object.DestroyImmediate(rt);}
        }

        private static void WriteOverdrawVisualization(Color[] pixels,int width,int height,float max,string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));var image=new Texture2D(width,height,TextureFormat.RGB24,false);var output=new Color[pixels.Length];
            for(var i=0;i<pixels.Length;i++){var value=Mathf.Max(pixels[i].r,Mathf.Max(pixels[i].g,pixels[i].b));var t=max<=0?0:Mathf.Clamp01(value/max);output[i]=Color.Lerp(Color.black,Color.Lerp(Color.blue,Color.red,t),value>0?1:0);}
            image.SetPixels(output);image.Apply();File.WriteAllBytes(path,image.EncodeToPNG());UnityEngine.Object.DestroyImmediate(image);
        }

        private static void BuildPlayerAndRunProbe()
        {
            var pcQuality=Array.IndexOf(QualitySettings.names,"PC");if(pcQuality>=0)QualitySettings.SetQualityLevel(pcQuality,true);
            var pcPipeline=AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset");
            if(pcPipeline==null)throw new InvalidOperationException("PC render pipeline asset missing before M9 player build.");
            GraphicsSettings.defaultRenderPipeline=pcPipeline;QualitySettings.renderPipeline=pcPipeline;
            PlayerSettings.productName="Sandrone Toon M9";AssetDatabase.SaveAssets();
            var playerRoot=Artifact("Player");Directory.CreateDirectory(playerRoot);var exe=Path.Combine(playerRoot,"SandroneM9.exe");
            PlayerSettings.enableFrameTimingStats=true;SandroneM9ShaderVariantAudit.Begin();var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=new[]{ScenePath},locationPathName=exe,target=BuildTarget.StandaloneWindows64,options=BuildOptions.None});
            SandroneM9ShaderVariantAudit.Finish(Artifact("M9ShaderVariantReport.json"));var summary=report.summary;
            var bytes=Directory.Exists(playerRoot)?Directory.GetFiles(playerRoot,"*",SearchOption.AllDirectories).Sum(x=>new FileInfo(x).Length):0;
            var build=new PlayerBuildReport{generatedUtc=DateTime.UtcNow.ToString("O"),evidenceSessionId=SandroneEvidenceSession.CurrentSessionId,result=summary.result.ToString(),platform=summary.platform.ToString(),outputPath=exe,errors=summary.totalErrors,warnings=summary.totalWarnings,totalSeconds=summary.totalTime.TotalSeconds,totalBytes=bytes};
            File.WriteAllText(Artifact("M9PlayerBuildReport.json"),JsonUtility.ToJson(build,true));if(summary.result!=BuildResult.Succeeded)throw new InvalidOperationException("M9 player build failed: "+summary.result);
            var playerLog=Artifact("Player/M9Player.log");var arguments=$"-screen-width 768 -screen-height 1280 -screen-fullscreen 0 -popupwindow -logFile \"{playerLog}\"";
            var info=new ProcessStartInfo(exe,arguments)
            {UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden};
            info.EnvironmentVariables["SANDRONE_M9_REPORT"]=Artifact("M9PlayerPerformance.json");info.EnvironmentVariables["SANDRONE_M9_SCREENSHOT"]=Artifact("Player/M9Player_Final.ppm");info.EnvironmentVariables["SANDRONE_EVIDENCE_SESSION"]=SandroneEvidenceSession.CurrentSessionId;
            using var process=Process.Start(info);if(process==null)throw new InvalidOperationException("Could not start M9 player.");if(!process.WaitForExit(120000)){process.Kill();throw new TimeoutException("M9 player probe timed out.");}
            if(process.ExitCode!=0)throw new InvalidOperationException("M9 player probe exit code "+process.ExitCode);
        }

        private static void ConfigureCamera(Camera camera,Vector3 position,Vector3 target,float size)
        {
            camera.transform.position=position;camera.transform.rotation=Quaternion.LookRotation(target-position,Vector3.up);camera.orthographic=true;camera.orthographicSize=size;camera.allowHDR=true;camera.allowMSAA=true;
            camera.GetUniversalAdditionalCameraData().renderPostProcessing=true;
        }

        private static void CaptureCamera(Camera camera,string relative,int width,int height)
        {
            var path=Artifact(relative);Directory.CreateDirectory(Path.GetDirectoryName(path));var active=RenderTexture.active;var rt=new RenderTexture(width,height,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB);var image=new Texture2D(width,height,TextureFormat.RGB24,false);
            try{camera.targetTexture=rt;rt.Create();camera.Render();RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,width,height),0,0);image.Apply();File.WriteAllBytes(path,image.EncodeToPNG());}
            finally{camera.targetTexture=null;RenderTexture.active=active;UnityEngine.Object.DestroyImmediate(image);rt.Release();UnityEngine.Object.DestroyImmediate(rt);}
        }

        private static void EnsureFolder(string path){var parts=path.Split('/');var current=parts[0];for(var i=1;i<parts.Length;i++){var next=current+"/"+parts[i];if(!AssetDatabase.IsValidFolder(next))AssetDatabase.CreateFolder(current,parts[i]);current=next;}}
        public static string Artifact(string relative){var path=Path.GetFullPath(Path.Combine(Application.dataPath,"../TestArtifacts/M9",relative));Directory.CreateDirectory(Path.GetDirectoryName(path));return path;}
    }
}
