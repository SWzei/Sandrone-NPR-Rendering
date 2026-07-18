using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace SandroneToon.Editor
{
    public static class SandroneM0M4RegressionAudit
    {
        [Serializable] private sealed class Check { public string name=""; public bool passed; public string details=""; }
        [Serializable] private sealed class DebugEvidence
        {
            public string phase="",mode="",path="",sha256="";
            public int width,height; public float meanLuminance,maeFromPrevious;
        }
        [Serializable] private sealed class Report
        {
            public string generatedUtc="",unityVersion="",graphicsDevice="",graphicsApi="";
            public int checkCount,failureCount; public float pcMobileMae;
            public List<Check> checks=new(); public List<DebugEvidence> debugEvidence=new(); public List<string> failures=new();
        }

        private readonly struct DebugPath
        {
            public readonly string phase,mode,path;
            public DebugPath(string p,string m,string x){phase=p;mode=m;path=x;}
        }

        [MenuItem("Sandrone/Audit/Run Full M0-M4 Regression")]
        public static void RunFullRegression()
        {
            SandroneM0Bootstrap.Build();
            SandroneM1Bootstrap.Build();
            SandroneM2Bootstrap.Build();
            SandroneM3Bootstrap.Build();
            SandroneM4Bootstrap.Build();
            RunAudit();
        }

        [MenuItem("Sandrone/Audit/Validate Current M0-M4")]
        public static void RunAudit()
        {
            var report=new Report{generatedUtc=DateTime.UtcNow.ToString("O"),unityVersion=Application.unityVersion,
                graphicsDevice=SystemInfo.graphicsDeviceName,graphicsApi=SystemInfo.graphicsDeviceType.ToString()};
            void Add(string name,bool passed,string details){report.checks.Add(new Check{name=name,passed=passed,details=details});if(!passed)report.failures.Add($"{name}: {details}");}

            Add("RealGraphicsDevice",SystemInfo.graphicsDeviceType!=GraphicsDeviceType.Null,$"{report.graphicsApi}; {report.graphicsDevice}");
            Add("UnityVersion",Application.unityVersion=="6000.5.3f1",Application.unityVersion);
            for(var phase=0;phase<=4;phase++)
            {
                var path=Path.Combine(ProjectRoot(),$"TestArtifacts/M{phase}/M{phase}ValidationReport.json");
                var text=File.Exists(path)?File.ReadAllText(path):"";
                Add($"M{phase}FreshReport",File.Exists(path)&&text.Contains($"\"phase\": \"M{phase}\"")&&!text.Contains("\"failures\": [\n        \""),path);
            }

            ValidateHeadRotation(Add);

            EditorSceneManager.OpenScene(SandroneM4Bootstrap.ScenePath,OpenSceneMode.Single);
            var controller=UnityEngine.Object.FindFirstObjectByType<SandroneM4Controller>();
            var renderer=controller?.TargetRenderer as SkinnedMeshRenderer;
            var camera=UnityEngine.Object.FindFirstObjectByType<Camera>();
            Add("M4StableScene",controller!=null&&renderer!=null&&camera!=null,"controller/renderer/camera after serialized reload");
            if(controller==null||renderer==null||camera==null) Finish(report);

            controller.ClearMaterialSlotFeatureWeights(); controller.DebugMode=SandroneM4DebugMode.FinalToon;
            controller.MetalEnabled=true;controller.StockingEnabled=true;controller.HairOverlayEnabled=true;controller.Apply(true);
            var block=new MaterialPropertyBlock();renderer.GetPropertyBlock(block,24);
            Add("ControllerImmediateMPB",Mathf.Approximately(block.GetFloat(Shader.PropertyToID("_M4FeatureWeight")),1f),"slot 24 readback after Apply");
            Add("NoGlobalM4Keywords",Shader.enabledGlobalKeywords.All(k=>!k.name.StartsWith("M4_",StringComparison.Ordinal)),"no debug/toggle state in global keyword set");

            var shader=AssetDatabase.LoadAssetAtPath<Shader>(SandroneM4Bootstrap.ShaderPath);
            var passMaterial=renderer.sharedMaterials.FirstOrDefault();
            Add("M4PassContract",passMaterial!=null&&passMaterial.FindPass("M4MaterialResponse")>=0&&passMaterial.FindPass("ShadowCaster")>=0,"forward + ShadowCaster");
            Add("M4ShaderMessages",shader!=null&&ShaderUtil.GetShaderMessages(shader).Length==0,shader?.name??"missing");
            Add("TransparentShadowCasterDisabled",renderer.sharedMaterials.Where(m=>m.GetFloat("_ZWrite")<0.5f).All(m=>!m.GetShaderPassEnabled("ShadowCaster")),"transparent slots do not submit binary ShadowCaster draws");
            ValidateSurfaces(renderer,Add);
            ValidateSyntheticControlChannels(renderer,camera,controller,Add);

            var pc=AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset");
            var mobile=AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/Settings/Mobile_RPAsset.asset");
            var oldDefault=GraphicsSettings.defaultRenderPipeline;var oldQuality=QualitySettings.renderPipeline;
            var pcPath=Artifact("Pipeline/M4_PC_ForwardPlus.png");var mobilePath=Artifact("Pipeline/M4_Mobile_Forward.png");
            try
            {
                CaptureWithPipeline(pc,camera,controller,pcPath);
                CaptureWithPipeline(mobile,camera,controller,mobilePath);
            }
            finally{QualitySettings.renderPipeline=oldQuality;GraphicsSettings.defaultRenderPipeline=oldDefault;}
            var pcImage=Read(pcPath);var mobileImage=Read(mobilePath);
            report.pcMobileMae=Mae(pcImage.pixels,mobileImage.pixels);
            Add("PCForwardPlusCapture",ValidCharacterImage(pcImage),$"{pcImage.width}x{pcImage.height}");
            Add("MobileForwardCapture",ValidCharacterImage(mobileImage),$"{mobileImage.width}x{mobileImage.height}");
            Add("NoPinkOrBlackSilhouette",NoFailureColors(pcImage)&&NoFailureColors(mobileImage),"pink pixels <0.01%, character luminance >0.05");
            Add("PCMobileReasonableDifference",report.pcMobileMae<25f,$"8-bit MAE={report.pcMobileMae:F3}; shadow quality is intentionally different");

            CollectDebugEvidence(report,Add);
            Finish(report);
        }

        private static void ValidateHeadRotation(Action<string,bool,string> add)
        {
            EditorSceneManager.OpenScene(SandroneM1Bootstrap.ScenePath,OpenSceneMode.Single);
            var controller=UnityEngine.Object.FindFirstObjectByType<SandroneM1Controller>();var camera=UnityEngine.Object.FindFirstObjectByType<Camera>();
            if(controller==null||controller.Head==null||camera==null){add("M1HeadRotationResponse",false,"M1 controller/head/camera missing");return;}
            var original=controller.Head.localRotation;var before=Artifact("DebugResponse/M1_HeadAxis_Before.png");var after=Artifact("DebugResponse/M1_HeadAxis_AfterHeadRotate.png");
            try
            {
                controller.DebugMode=SandroneM1DebugMode.HeadAxis;controller.Apply(true);camera.backgroundColor=Color.black;CaptureCamera(camera,before,512,768);
                controller.Head.localRotation=original*Quaternion.Euler(0f,25f,0f);controller.Apply(true);CaptureCamera(camera,after,512,768);
            }
            finally{controller.Head.localRotation=original;controller.DebugMode=SandroneM1DebugMode.BaseLit;controller.Apply(true);}
            var mae=PairMaeAbsolute(before,after);add("M1HeadRotationResponse",mae>1f,$"HeadAxis 25-degree head rotation MAE={mae:F3}");
        }

        private static void ValidateSyntheticControlChannels(SkinnedMeshRenderer renderer,Camera camera,SandroneM4Controller controller,Action<string,bool,string> add)
        {
            var isolation=AssetDatabase.LoadAssetAtPath<Shader>(SandroneM4Bootstrap.IsolationShaderPath);var original=renderer.sharedMaterials;
            var transparent=new Material(isolation){name="Audit_ControlProbe_Transparent"};transparent.SetColor("_Color",new Color(0,0,0,0));
            var target=new Material(original[24]){name="Audit_ControlProbe_Slot24"};var control=new Texture2D(2,2,TextureFormat.RGBA32,false,true){name="Audit_RGBA_0.2_0.4_0.6_0.8"};
            control.SetPixels(Enumerable.Repeat(new Color(0.2f,0.4f,0.6f,0.8f),4).ToArray());control.Apply();target.SetTexture("_ControlMap",control);
            var materials=Enumerable.Repeat(transparent,original.Length).ToArray();materials[24]=target;renderer.sharedMaterials=materials;
            var paths=new List<string>();
            try
            {
                controller.ClearMaterialSlotFeatureWeights();controller.MetalEnabled=true;controller.StockingEnabled=true;controller.HairOverlayEnabled=true;
                camera.transform.position=new Vector3(0f,0.82f,4f);camera.transform.rotation=Quaternion.LookRotation(new Vector3(0f,0.82f,0f)-camera.transform.position,Vector3.up);camera.orthographicSize=0.92f;camera.backgroundColor=Color.black;
                foreach(var mode in new[]{SandroneM4DebugMode.ControlR,SandroneM4DebugMode.ControlG,SandroneM4DebugMode.ControlB,SandroneM4DebugMode.ControlA})
                {controller.DebugMode=mode;controller.Apply(true);var path=Artifact($"DebugResponse/M4_Synthetic_{mode}.png");CaptureCamera(camera,path,512,768);paths.Add(path);}
            }
            finally
            {
                renderer.sharedMaterials=original;controller.DebugMode=SandroneM4DebugMode.FinalToon;controller.ClearMaterialSlotFeatureWeights();controller.Apply(true);
                UnityEngine.Object.DestroyImmediate(transparent);UnityEngine.Object.DestroyImmediate(target);UnityEngine.Object.DestroyImmediate(control);
            }
            var r=MeanRgb(Read(paths[0]));var g=MeanRgb(Read(paths[1]));var b=MeanRgb(Read(paths[2]));var a=MeanRgb(Read(paths[3]));
            var unique=paths.Select(Hash).Distinct().Count()==4;
            var semantic=r.x>r.y*4&&r.x>r.z*4&&g.y>g.x*4&&g.y>g.z*4&&b.z>b.x*4&&b.z>b.y*4&&a.x>a.z*4&&a.y>a.z*4&&a.x>r.x&&a.y>g.y;
            add("M4SyntheticControlChannelProbe",unique&&semantic,$"unique={unique}; mean R={r}, G={g}, B={b}, A={a}; transient RGBA=(.2,.4,.6,.8)");
        }

        private static void ValidateSurfaces(SkinnedMeshRenderer renderer,Action<string,bool,string> add)
        {
            var opaque=true;var cutout=true;var transparent=true;
            foreach(var material in renderer.sharedMaterials)
            {
                var queue=material.renderQueue;var z=material.GetFloat("_ZWrite");var clip=material.GetFloat("_AlphaClip");
                var src=material.GetFloat("_SrcBlend");var dst=material.GetFloat("_DstBlend");
                if(queue<(int)RenderQueue.AlphaTest) opaque&=z>0.5f&&src==(float)BlendMode.One&&dst==(float)BlendMode.Zero;
                else if(queue<(int)RenderQueue.Transparent) cutout&=z>0.5f&&clip>0.5f&&src==(float)BlendMode.One&&dst==(float)BlendMode.Zero;
                else transparent&=z<0.5f&&src==(float)BlendMode.SrcAlpha&&dst==(float)BlendMode.OneMinusSrcAlpha;
            }
            add("OpaqueState",opaque,"Blend One/Zero, ZWrite On");add("CutoutState",cutout,"AlphaClip, opaque blend, ZWrite On");
            add("TransparentState",transparent,"SrcAlpha/OneMinusSrcAlpha, ZWrite Off");
        }

        private static void CaptureWithPipeline(UniversalRenderPipelineAsset asset,Camera camera,SandroneM4Controller controller,string path)
        {
            if(asset==null)throw new InvalidOperationException("URP asset missing.");
            QualitySettings.renderPipeline=asset;GraphicsSettings.defaultRenderPipeline=asset;
            controller.CharacterRoot.SetPositionAndRotation(Vector3.zero,Quaternion.identity);controller.DebugMode=SandroneM4DebugMode.FinalToon;controller.Apply(true);
            camera.transform.position=new Vector3(0f,0.82f,4f);camera.transform.rotation=Quaternion.LookRotation(new Vector3(0f,0.82f,0f)-camera.transform.position,Vector3.up);
            camera.orthographic=true;camera.orthographicSize=0.92f;camera.backgroundColor=new Color(0.153f,0.149f,0.149f,1f);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);var previous=RenderTexture.active;
            var rt=new RenderTexture(768,1280,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB){antiAliasing=1};var image=new Texture2D(768,1280,TextureFormat.RGB24,false,false);
            try{camera.targetTexture=rt;rt.Create();camera.Render();RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,768,1280),0,0);image.Apply();File.WriteAllBytes(path,image.EncodeToPNG());}
            finally{camera.targetTexture=null;RenderTexture.active=previous;UnityEngine.Object.DestroyImmediate(image);rt.Release();UnityEngine.Object.DestroyImmediate(rt);}
        }

        private static void CaptureCamera(Camera camera,string path,int width,int height)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);var previous=RenderTexture.active;var rt=new RenderTexture(width,height,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB){antiAliasing=1};var image=new Texture2D(width,height,TextureFormat.RGB24,false,false);
            try{camera.targetTexture=rt;rt.Create();camera.Render();RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,width,height),0,0);image.Apply();File.WriteAllBytes(path,image.EncodeToPNG());}
            finally{camera.targetTexture=null;RenderTexture.active=previous;UnityEngine.Object.DestroyImmediate(image);rt.Release();UnityEngine.Object.DestroyImmediate(rt);}
        }

        private static void CollectDebugEvidence(Report report,Action<string,bool,string> add)
        {
            var paths=new List<DebugPath>
            {
                new("M1","0 BaseLit","M1/ReferenceComparison/M1_BaseLit_Front.png"),new("M1","1 NdotL","M1/Debug/M1_NdotL_FrontLight.png"),new("M1","2 NdotV","M1/Debug/M1_NdotV.png"),new("M1","3 HeadAxis","M1/Debug/M1_HeadAxis.png"),new("M1","4 MainLightColor","M1/Debug/M1_MainLightColor.png"),new("M1","5 DistanceAttenuation","M1/Debug/M1_MainLightDistanceAttenuation.png"),
                new("M2","0 FinalToon","M2/ReferenceComparison/M2_FinalToon_Front.png"),new("M2","1 HalfLambert","M2/Debug/M2_HalfLambert.png"),new("M2","2 BandMask","M2/Debug/M2_BandMask_FrontLight.png"),new("M2","3 RampUV","M2/Debug/M2_RampUV.png"),new("M2","4 RampSample","M2/Debug/M2_RampSample.png"),new("M2","5 NdotV","M2/Debug/M2_NdotV.png"),new("M2","6 HeadAxis","M2/Debug/M2_HeadAxis.png"),new("M2","7 Silhouette","M2/Debug/M2_Silhouette.png"),
                new("M3","0 FinalToon","M3/ReferenceComparison/M3_FinalToon_Front.png"),new("M3","1 CastShadowRaw","M3/Debug/M3_CastShadowRaw_Self.png"),new("M3","2 CastShadowStyled","M3/Debug/M3_CastShadowStyled_Self.png"),new("M3","3 FormBand","M3/Debug/M3_FormBand.png"),new("M3","4 FinalLitMask","M3/Debug/M3_FinalLitMask.png"),new("M3","5 RampSample","M3/Debug/M3_RampSample.png"),new("M3","6 CascadeIndex","M3/Debug/M3_CascadeIndex.png"),new("M3","7 Silhouette","M3/Debug/M3_Silhouette.png"),
                new("M4","0 FinalToon","M4/AB/M4_AllOn.png"),new("M4","1 ControlR","M4/Debug/M4_ControlR.png"),new("M4","2 ControlG","M4/Debug/M4_ControlG.png"),new("M4","3 ControlB","M4/Debug/M4_ControlB.png"),new("M4","4 ControlA","M4/Debug/M4_ControlA.png"),new("M4","5 NDotH","M4/Debug/M4_NDotH.png"),new("M4","6 Specular","M4/Debug/M4_Specular.png"),new("M4","7 MatCapUV","M4/Debug/M4_MatCapUV.png"),new("M4","8 MatCapSample","M4/Debug/M4_MatCapSample.png"),new("M4","9 MaterialResponse","M4/Debug/M4_MaterialResponse.png"),new("M4","10 FinalLitMask","M4/Debug/M4_FinalLitMask.png"),new("M4","11 Silhouette","M4/Debug/M4_Silhouette.png")
            };
            var hashes=new Dictionary<string,HashSet<string>>();Image previous=null;string previousPhase="";
            foreach(var item in paths)
            {
                var full=Path.Combine(ProjectRoot(),"TestArtifacts",item.path);var image=Read(full);var hash=File.Exists(full)?Hash(full):"missing";
                var evidence=new DebugEvidence{phase=item.phase,mode=item.mode,path=full,sha256=hash,width=image.width,height=image.height,meanLuminance=MeanLuminance(image)};
                if(previous!=null&&previousPhase==item.phase&&previous.width==image.width&&previous.height==image.height)evidence.maeFromPrevious=Mae(previous.pixels,image.pixels);
                report.debugEvidence.Add(evidence);if(!hashes.TryGetValue(item.phase,out var set)){set=new HashSet<string>();hashes[item.phase]=set;}set.Add(hash);
                previous=image;previousPhase=item.phase;
            }
            foreach(var phase in hashes.Keys)add($"{phase}DebugHashesUnique",hashes[phase].Count==paths.Count(x=>x.phase==phase),$"unique={hashes[phase].Count}/{paths.Count(x=>x.phase==phase)}");
            add("M1LightRotationResponse",PairMae("M1/Debug/M1_NdotL_FrontLight.png","M1/Debug/M1_NdotL_RightLight.png")>1f,"front/right NdotL MAE > 1");
            add("M2LightRotationResponse",PairMae("M2/Debug/M2_BandMask_FrontLight.png","M2/Debug/M2_BandMask_BackLight.png")>1f,"front/back band MAE > 1");
            add("M3BlockerResponse",PairMae("M3/Debug/M3_Receiver_NoBlocker.png","M3/Debug/M3_Receiver_WithBlocker.png")>1f,"external blocker MAE > 1");
            add("M4CameraMatCapResponse",PairMae("M4/Debug/M4_MatCapSample.png","M4/Debug/M4_MatCapSample_ThreeQuarter.png")>1f,"front/three-quarter MatCap MAE > 1");
        }

        private sealed class Image{public int width,height;public Color32[] pixels=Array.Empty<Color32>();}
        private static Image Read(string path){if(!File.Exists(path))return new Image();var t=new Texture2D(2,2);try{t.LoadImage(File.ReadAllBytes(path));return new Image{width=t.width,height=t.height,pixels=t.GetPixels32()};}finally{UnityEngine.Object.DestroyImmediate(t);}}
        private static float PairMae(string a,string b){var x=Read(Path.Combine(ProjectRoot(),"TestArtifacts",a));var y=Read(Path.Combine(ProjectRoot(),"TestArtifacts",b));return x.width==y.width&&x.height==y.height?Mae(x.pixels,y.pixels):0f;}
        private static float PairMaeAbsolute(string a,string b){var x=Read(a);var y=Read(b);return x.width==y.width&&x.height==y.height?Mae(x.pixels,y.pixels):0f;}
        private static float Mae(Color32[] a,Color32[] b){if(a.Length==0||a.Length!=b.Length)return float.PositiveInfinity;double sum=0;for(var i=0;i<a.Length;i++)sum+=(Math.Abs(a[i].r-b[i].r)+Math.Abs(a[i].g-b[i].g)+Math.Abs(a[i].b-b[i].b))/3.0;return(float)(sum/a.Length);}
        private static float MeanLuminance(Image image){if(image.pixels.Length==0)return 0;double sum=0;foreach(var p in image.pixels)sum+=0.2126*p.r+0.7152*p.g+0.0722*p.b;return(float)(sum/image.pixels.Length);}
        private static Vector3 MeanRgb(Image image){double r=0,g=0,b=0;var n=0;foreach(var p in image.pixels){if(p.r+p.g+p.b<12)continue;r+=p.r;g+=p.g;b+=p.b;n++;}return n==0?Vector3.zero:new Vector3((float)(r/n),(float)(g/n),(float)(b/n));}
        private static bool ValidCharacterImage(Image image)=>image.pixels.Length>1000&&image.pixels.Count(p=>Math.Abs(p.r-39)+Math.Abs(p.g-38)+Math.Abs(p.b-38)>15)>image.pixels.Length*0.05;
        private static bool NoFailureColors(Image image){if(image.pixels.Length==0)return false;var pink=image.pixels.Count(p=>p.r>200&&p.b>200&&p.g<80);return pink<image.pixels.Length*0.0001&&MeanLuminance(image)>15f;}
        private static string Hash(string path){using var sha=SHA256.Create();return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-","");}
        private static string ProjectRoot()=>Path.GetFullPath(Path.Combine(Application.dataPath,".."));
        private static string Artifact(string relative)=>Path.Combine(ProjectRoot(),"TestArtifacts/Audit",relative);
        private static void Finish(Report report)
        {
            report.checkCount=report.checks.Count;report.failureCount=report.failures.Count;Directory.CreateDirectory(Path.Combine(ProjectRoot(),"TestArtifacts/Audit"));
            File.WriteAllText(Path.Combine(ProjectRoot(),"TestArtifacts/Audit/M0M4RegressionAudit.json"),JsonUtility.ToJson(report,true));
            if(report.failures.Count>0)throw new InvalidOperationException($"M0-M4 audit failed ({report.failureCount}/{report.checkCount}): {string.Join("; ",report.failures)}");
            Debug.Log($"[Sandrone Audit] Passed {report.checkCount}/{report.checkCount}; device={report.graphicsApi} {report.graphicsDevice}.");
        }
    }
}
