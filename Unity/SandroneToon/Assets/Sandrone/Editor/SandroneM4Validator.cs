using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace SandroneToon.Editor
{
    public static class SandroneM4Validator
    {
        [Serializable] public sealed class Check { public string name; public bool passed; public string details; }
        [Serializable] public sealed class Report
        {
            public string phase="M4",generatedUtc,UnityVersion,renderPipeline,urpPackageVersion;
            public int checkCount,failureCount,warningCount,shaderPassCount,shaderKeywordPragmaCount,shaderTextureSampleCount,shaderCompilerMessageCount;
            public float metalToggleMae,stockingToggleMae,hairToggleMae,matCapViewMae,m3ForegroundLuminance,m4ForegroundLuminance,m4ToM3LuminanceRatio;
            public float event24TargetMae,event24NonTargetMae,event28TargetMae,event28NonTargetMae,event29TargetMae,event29NonTargetMae;
            public List<Check> checks=new(); public List<string> failures=new(); public List<string> warnings=new();
            public string[] intentionallyDeferred={"M5 Face SDF","M6 hair anisotropy and eye/hair stencil","M7 outline normals/pass","M8 emission/Bloom","M9 post-processing and variant stripping"};
        }

        [MenuItem("Sandrone/M4/Validate")]
        public static void ValidateAndWriteReport()
        {
            var r=new Report { generatedUtc=DateTime.UtcNow.ToString("O"),UnityVersion=Application.unityVersion,
                renderPipeline=GraphicsSettings.currentRenderPipeline?.GetType().FullName??"null",urpPackageVersion=PackageVersion() };
            void Add(string name,bool passed,string details){ r.checks.Add(new Check{name=name,passed=passed,details=details}); if(!passed) r.failures.Add($"{name}: {details}"); }
            Add("EditorVersion",Application.unityVersion=="6000.5.3f1",Application.unityVersion);
            Add("ColorSpace",PlayerSettings.colorSpace==ColorSpace.Linear,PlayerSettings.colorSpace.ToString());
            Add("URPAssigned",GraphicsSettings.currentRenderPipeline!=null,r.renderPipeline);
            Add("URPPackageVersion",r.urpPackageVersion=="17.5.0",r.urpPackageVersion);
            var m3Report=Path.Combine(ProjectRoot(),"TestArtifacts/M3/M3ValidationReport.json");
            var m3Text=File.Exists(m3Report)?File.ReadAllText(m3Report):"";
            Add("M3RegressionGate",m3Text.Contains("\"phase\": \"M3\"") && m3Text.Contains("\"failures\": []"),m3Report);

            var profile=AssetDatabase.LoadAssetAtPath<SandroneM4MaterialResponseProfile>(SandroneM4Bootstrap.ProfilePath);
            Add("ResponseProfile",profile!=null && profile.ContractVersion=="SandroneMaterialResponseProfile_v1_M4",profile?.ContractVersion??"missing");
            Add("ProfileEntryCount",profile!=null && profile.Materials.Length==31,profile==null?"missing":profile.Materials.Length.ToString());
            Add("ProfileUniqueIndices",profile!=null && profile.Materials.Select(x=>x.materialIndex).Distinct().Count()==31,"expected 0..30 unique");
            if(profile!=null)
            {
                Add("ProfileIndexRange",profile.Materials.All(x=>x.materialIndex>=0&&x.materialIndex<31),"0..30");
                Add("SkinFamily",Types(profile,17,SandroneM4ResponseType.Skin)&&Types(profile,18,SandroneM4ResponseType.Skin),"slots 17,18");
                Add("MetalFamily",Types(profile,24,SandroneM4ResponseType.Metal),"slot 24");
                Add("StockingFamily",Feature(profile,28,SandroneM4FeatureGroup.StockingOverlay)&&Types(profile,28,SandroneM4ResponseType.Silk),"slot 28");
                Add("HairOverlayFamily",Feature(profile,29,SandroneM4FeatureGroup.HairOverlay),"slot 29");
                Add("A_B_IndependentGroups",Feature(profile,24,SandroneM4FeatureGroup.Metal)&&Feature(profile,28,SandroneM4FeatureGroup.StockingOverlay)&&Feature(profile,29,SandroneM4FeatureGroup.HairOverlay),"饰=24, 袜+=28, 髮+=29");
            }

            ValidateTexture(Add,SandroneM4Bootstrap.BodyControlPath,false,2048,2048,"BodyControl");
            ValidateTexture(Add,SandroneM4Bootstrap.SkirtControlPath,false,2048,2048,"SkirtControl");
            ValidateTexture(Add,SandroneM4Bootstrap.HairControlPath,false,2048,2048,"HairControl");
            ValidateTexture(Add,SandroneM4Bootstrap.NeutralControlPath,false,16,16,"NeutralControl");
            ValidateTexture(Add,SandroneM4Bootstrap.MatCapPath,true,512,512,"MetalMatCap");
            if(profile!=null)
            {
                Add("AllDataTexturesBound",profile.BodyControlMap&&profile.SkirtControlMap&&profile.HairControlMap&&profile.NeutralControlMap&&profile.MetalMatCap,"five profile textures");
                var bodyPixels=DecodeAsset(SandroneM4Bootstrap.BodyControlPath).GetPixels32();
                var rMin=bodyPixels.Min(p=>p.r); var rMax=bodyPixels.Max(p=>p.r); var bMax=bodyPixels.Max(p=>p.b); var aMax=bodyPixels.Max(p=>p.a);
                Add("ControlRVariation",rMax-rMin>32,$"range={rMin}..{rMax}");
                Add("ControlGNeutralAO",bodyPixels.All(p=>p.g>=254),"G remains neutral until authored AO exists");
                Add("ControlBMetalCandidates",bMax>32,$"max={bMax}");
                Add("ControlAReserved",aMax==0,$"max={aMax}");
            }

            var shader=AssetDatabase.LoadAssetAtPath<Shader>(SandroneM4Bootstrap.ShaderPath);
            Add("ShaderExists",shader!=null,shader?.name??"missing"); Add("ShaderSupported",shader!=null&&shader.isSupported,"Sandrone/M4/MaterialResponse");
            var messages=shader==null?Array.Empty<ShaderMessage>():ShaderUtil.GetShaderMessages(shader); r.shaderCompilerMessageCount=messages.Length;
            Add("ShaderCompileMessages",messages.Length==0,string.Join(" | ",messages.Select(m=>m.message)));
            var source=File.Exists(Absolute(SandroneM4Bootstrap.ShaderPath))?File.ReadAllText(Absolute(SandroneM4Bootstrap.ShaderPath)):"";
            r.shaderPassCount=Regex.Matches(source,@"(?m)^\s*Pass\s*$").Count;
            r.shaderKeywordPragmaCount=Regex.Matches(source,@"#pragma\s+multi_compile").Count;
            r.shaderTextureSampleCount=Regex.Matches(source,@"SAMPLE_TEXTURE2D\(").Count;
            Add("ShaderPasses",r.shaderPassCount==2,$"passes={r.shaderPassCount}");
            Add("ShaderVariants",r.shaderKeywordPragmaCount==4,$"keyword pragmas={r.shaderKeywordPragmaCount}; Forward+/main-shadow/soft-shadow plus punctual ShadowCaster only");
            Add("NoGlobalDebugState",!source.Contains("M4_DEBUG_")&&!source.Contains("M4_METAL_OFF")&&!source.Contains("M4_STOCKING_OFF")&&!source.Contains("M4_HAIR_OVERLAY_OFF"),"M4 debug/toggles are per-material-slot uniforms, not global keywords");
            Add("TextureSampleBudget",r.shaderTextureSampleCount==5,$"samples={r.shaderTextureSampleCount}; forward Base+Control+Ramp+MatCap, caster Base");
            Add("ViewSpaceMatCap",source.Contains("GetWorldToViewMatrix")&&source.Contains("normalVS.xy"),SandroneM4Bootstrap.ShaderPath);
            Add("CastShadowMasksSpecular",source.Contains("_SpecIntensity * castStyled"),"specular visibility follows styled cast shadow");
            Add("SharedAlphaContract",source.Contains("_M4FeatureWeight")&&source.Contains("overlayWeight"),"forward/caster share per-slot feature weight");
            Add("NoLaterPhaseFeatures",!Regex.IsMatch(source,"FaceMap|FaceSDF|Stencil|Outline|Emission|Anisotrop",RegexOptions.IgnoreCase),"M5+ contracts absent");

            if(File.Exists(Absolute(SandroneM4Bootstrap.ScenePath)))
            {
                var scene=EditorSceneManager.OpenScene(SandroneM4Bootstrap.ScenePath,OpenSceneMode.Single);
                var controller=UnityEngine.Object.FindFirstObjectByType<SandroneM4Controller>();
                var renderer=UnityEngine.Object.FindFirstObjectByType<SkinnedMeshRenderer>();
                var camera=UnityEngine.Object.FindFirstObjectByType<Camera>();
                Add("M4Controller",controller!=null,controller?.name??"missing"); Add("SkinnedRenderer",renderer!=null,renderer?.name??"missing");
                Add("MaterialSlotCount",renderer!=null&&renderer.sharedMaterials.Length==31,renderer==null?"missing":renderer.sharedMaterials.Length.ToString());
                Add("NoNullMaterials",renderer!=null&&renderer.sharedMaterials.All(m=>m!=null),"31 slots");
                Add("MaterialShader",renderer!=null&&renderer.sharedMaterials.All(m=>m.shader==shader),shader?.name??"missing");
                Add("CharacterShadows",renderer!=null&&renderer.shadowCastingMode==ShadowCastingMode.On&&renderer.receiveShadows,"cast On, receive true");
                Add("CalibrationCamera",camera!=null&&camera.orthographic&&!camera.allowHDR,"orthographic, HDR off");
                Add("SingleDirectionalLight",UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None).Count(l=>l.type==LightType.Directional)==1,"exactly one");
                if(renderer!=null&&profile!=null) ValidateMaterials(Add,renderer,profile,shader);
            }
            else Add("M4Scene",false,SandroneM4Bootstrap.ScenePath);

            string[] captures={"ReferenceComparison/M4_FinalToon_Front.png","ReferenceComparison/M4_FinalToon_Side.png",
                "Debug/M4_ControlR.png","Debug/M4_ControlG.png","Debug/M4_ControlB.png","Debug/M4_ControlA.png","Debug/M4_NDotH.png","Debug/M4_Specular.png",
                "Debug/M4_MatCapUV.png","Debug/M4_MatCapSample.png","Debug/M4_MaterialResponse.png","Debug/M4_FinalLitMask.png","Debug/M4_Silhouette.png",
                "Debug/M4_MatCapSample_ThreeQuarter.png","AB/M4_AllOn.png","AB/M4_MetalOff.png","AB/M4_StockingOff.png","AB/M4_HairOverlayOff.png"};
            captures=captures.Concat(new[]{"AB/M4_MetalSlot24_IsolationOn.png","AB/M4_MetalSlot24_IsolationOff.png",
                "AB/M4_StockingSlot28_IsolationOn.png","AB/M4_StockingSlot28_IsolationOff.png",
                "AB/M4_HairSlot29_IsolationOn.png","AB/M4_HairSlot29_IsolationOff.png",
                "AB/M4_Event24_MetalSlotOn.png","AB/M4_Event24_MetalSlotOff.png",
                "AB/M4_Event28_StockingSlotOn.png","AB/M4_Event28_StockingSlotOff.png",
                "AB/M4_Event29_HairSlotOn.png","AB/M4_Event29_HairSlotOff.png"}).ToArray();
            captures=captures.Concat(new[]{"Masks/M4_Slot24_AlphaAwareMask.png","Masks/M4_Slot28_AlphaAwareMask.png","Masks/M4_Slot29_AlphaAwareMask.png"}).ToArray();
            foreach(var capture in captures) Add("Capture_"+Path.GetFileNameWithoutExtension(capture),File.Exists(Artifact(capture)),capture);
            if(captures.All(c=>File.Exists(Artifact(c))))
            {
                r.metalToggleMae=Mae(Artifact("AB/M4_MetalSlot24_IsolationOn.png"),Artifact("AB/M4_MetalSlot24_IsolationOff.png"));
                r.stockingToggleMae=Mae(Artifact("AB/M4_StockingSlot28_IsolationOn.png"),Artifact("AB/M4_StockingSlot28_IsolationOff.png"));
                r.hairToggleMae=Mae(Artifact("AB/M4_HairSlot29_IsolationOn.png"),Artifact("AB/M4_HairSlot29_IsolationOff.png"));
                r.matCapViewMae=Mae(Artifact("Debug/M4_MatCapSample.png"),Artifact("Debug/M4_MatCapSample_ThreeQuarter.png"));
                Add("MetalSlot24Isolation",r.metalToggleMae>0.02f,$"On/Off MAE={r.metalToggleMae:F4}");
                Add("StockingSlot28Isolation",r.stockingToggleMae>0.01f,$"On/Off MAE={r.stockingToggleMae:F4}");
                Add("HairOverlaySlot29Isolation",r.hairToggleMae>0.01f,$"On/Off MAE={r.hairToggleMae:F4}");
                MaskedMae(Artifact("AB/M4_Event24_MetalSlotOn.png"),Artifact("AB/M4_Event24_MetalSlotOff.png"),Artifact("Masks/M4_Slot24_AlphaAwareMask.png"),out r.event24TargetMae,out r.event24NonTargetMae);
                MaskedMae(Artifact("AB/M4_Event28_StockingSlotOn.png"),Artifact("AB/M4_Event28_StockingSlotOff.png"),Artifact("Masks/M4_Slot28_AlphaAwareMask.png"),out r.event28TargetMae,out r.event28NonTargetMae);
                MaskedMae(Artifact("AB/M4_Event29_HairSlotOn.png"),Artifact("AB/M4_Event29_HairSlotOff.png"),Artifact("Masks/M4_Slot29_AlphaAwareMask.png"),out r.event29TargetMae,out r.event29NonTargetMae);
                Add("Event24FinalContribution",r.event24TargetMae>1f&&r.event24NonTargetMae<0.5f,$"target MAE={r.event24TargetMae:F3}, non-target MAE={r.event24NonTargetMae:F3}");
                Add("Event28FinalContribution",r.event28TargetMae>1f&&r.event28NonTargetMae<0.5f,$"target MAE={r.event28TargetMae:F3}, non-target MAE={r.event28NonTargetMae:F3}");
                Add("Event29FinalContribution",r.event29TargetMae>1f&&r.event29NonTargetMae<0.5f,$"target MAE={r.event29TargetMae:F3}, non-target MAE={r.event29NonTargetMae:F3}");
                WriteDiff(Artifact("AB/M4_Event24_MetalSlotOn.png"),Artifact("AB/M4_Event24_MetalSlotOff.png"),Artifact("AB/M4_Event24_Diff.png"));
                WriteDiff(Artifact("AB/M4_Event28_StockingSlotOn.png"),Artifact("AB/M4_Event28_StockingSlotOff.png"),Artifact("AB/M4_Event28_Diff.png"));
                WriteDiff(Artifact("AB/M4_Event29_HairSlotOn.png"),Artifact("AB/M4_Event29_HairSlotOff.png"),Artifact("AB/M4_Event29_Diff.png"));
                Add("ThreeQuarterCaptureChanged",r.matCapViewMae>0.05f,$"MAE={r.matCapViewMae:F4}; proves the view changed, not by itself that the MatCap debug keyword rendered");
                var m3=Path.Combine(ProjectRoot(),"TestArtifacts/M3/ReferenceComparison/M3_FinalToon_Front.png");
                if(File.Exists(m3))
                {
                    r.m3ForegroundLuminance=ForegroundLuminance(m3,new Color32(39,38,38,255));
                    r.m4ForegroundLuminance=ForegroundLuminance(Artifact("ReferenceComparison/M4_FinalToon_Front.png"),new Color32(39,38,38,255));
                    r.m4ToM3LuminanceRatio=r.m4ForegroundLuminance/Mathf.Max(r.m3ForegroundLuminance,1e-5f);
                    Add("M3ColorContinuity",r.m4ToM3LuminanceRatio>0.75f&&r.m4ToM3LuminanceRatio<1.20f,$"M3={r.m3ForegroundLuminance:F3}, M4={r.m4ForegroundLuminance:F3}, ratio={r.m4ToM3LuminanceRatio:F3}");
                }
            }

            r.warnings.Add("Generated ControlMaps are deterministic authoring seeds inferred from BaseMap luminance/gold hue, not recovered game ILM; replace them with artist-authored maps after UV review.");
            r.warnings.Add("Control G is intentionally neutral (1.0): no trustworthy authored AO source exists, avoiding double-darkening the hand-painted BaseMap.");
            r.warnings.Add("Performance baseline remains 31 material slots; M4 adds ControlMap and branch-dependent MatCap sampling. Confirm draw calls, variants and GPU time in Frame Debugger/Profiler.");
            r.warnings.Add("Frame Debugger event ordinals are environment-dependent; object/material slot/pass identity and render state still require the documented GUI inspection.");
            r.checkCount=r.checks.Count; r.failureCount=r.failures.Count; r.warningCount=r.warnings.Count;
            Directory.CreateDirectory(Path.Combine(ProjectRoot(),"TestArtifacts/M4"));
            File.WriteAllText(Path.Combine(ProjectRoot(),"TestArtifacts/M4/M4ValidationReport.json"),JsonUtility.ToJson(r,true));
            if(r.failures.Count>0) throw new InvalidOperationException($"M4 validation failed ({r.failures.Count}/{r.checkCount}): {string.Join("; ",r.failures)}");
            Debug.Log($"[Sandrone M4] Validation passed: {r.checkCount}/{r.checkCount}, warnings={r.warningCount}.");
        }

        private static void ValidateMaterials(Action<string,bool,string> add,SkinnedMeshRenderer renderer,SandroneM4MaterialResponseProfile profile,Shader shader)
        {
            var responseOk=true; var bindings=true; var surface=true; var skirtCull=true;
            var matCap=profile.MetalMatCap;
            for(var i=0;i<31;i++)
            {
                var m=renderer.sharedMaterials[i]; profile.TryGet(i,out var e);
                responseOk &= Mathf.RoundToInt(m.GetFloat("_ResponseType"))==(int)e.responseType && Mathf.RoundToInt(m.GetFloat("_FeatureGroup"))==(int)e.featureGroup && Mathf.Approximately(m.GetFloat("_OverlayColorBoost"),e.overlayColorBoost);
                var baseMap=m.GetTexture("_BaseMap") as Texture2D;
                var expectedControl=baseMap==null?profile.NeutralControlMap:baseMap.name=="T_Body"?profile.BodyControlMap:baseMap.name=="T_Skirt"?profile.SkirtControlMap:baseMap.name=="T_Hair"?profile.HairControlMap:profile.NeutralControlMap;
                bindings &= baseMap!=null&&m.GetTexture("_RampMap")!=null&&m.GetTexture("_ControlMap")==expectedControl&&m.GetTexture("_MatCapMap")==matCap;
                var map=AssetDatabase.LoadAssetAtPath<SandroneMaterialMap>(SandroneM0Bootstrap.MaterialMapPath);
                var sourceEntry=map.Entries.First(x=>x.sourceIndex==i); var previous=AssetDatabase.LoadAssetAtPath<Material>(SandroneM3Bootstrap.MaterialPath(i,sourceEntry.materialAssetPath));
                surface &= previous!=null&&m.renderQueue==previous.renderQueue&&Mathf.Approximately(m.GetFloat("_SrcBlend"),previous.GetFloat("_SrcBlend"))&&Mathf.Approximately(m.GetFloat("_ZWrite"),previous.GetFloat("_ZWrite"));
                skirtCull &= i!=21 || Mathf.RoundToInt(m.GetFloat("_Cull"))==(int)CullMode.Back;
            }
            add("MaterialResponseBindings",responseOk,"31 profile entries match serialized material constants"); add("MaterialTextureBindings",bindings,"31 exact Base/Ramp/expected Control/MatCap asset references"); add("M3SurfaceStatePreserved",surface,"queue/blend/ZWrite; slot 21 Cull correction is checked separately");
            add("SkirtBackfaceCull",skirtCull,"slot 21 dark back-facing layer uses Cull Back; slot 26 red front-facing layer remains unchanged");
        }

        private static bool Types(SandroneM4MaterialResponseProfile p,int i,SandroneM4ResponseType t)=>p.TryGet(i,out var e)&&e.responseType==t;
        private static bool Feature(SandroneM4MaterialResponseProfile p,int i,SandroneM4FeatureGroup f)=>p.TryGet(i,out var e)&&e.featureGroup==f;
        private static void ValidateTexture(Action<string,bool,string> add,string path,bool srgb,int w,int h,string label)
        {
            var texture=AssetDatabase.LoadAssetAtPath<Texture2D>(path); var importer=AssetImporter.GetAtPath(path) as TextureImporter;
            add(label+"Exists",texture!=null,path); add(label+"Dimensions",texture!=null&&texture.width==w&&texture.height==h,texture==null?"missing":$"{texture.width}x{texture.height}");
            add(label+"Import",importer!=null&&importer.sRGBTexture==srgb&&!importer.mipmapEnabled&&importer.wrapMode==TextureWrapMode.Clamp&&importer.textureCompression==TextureImporterCompression.Uncompressed,$"sRGB={importer?.sRGBTexture}, mip={importer?.mipmapEnabled}, wrap={importer?.wrapMode}");
        }
        private static Texture2D DecodeAsset(string path){ var t=new Texture2D(2,2); t.LoadImage(File.ReadAllBytes(Absolute(path))); return t; }
        private static float Mae(string a,string b){ var x=Load(a); var y=Load(b); var n=Math.Min(x.Length,y.Length); double sum=0; for(var i=0;i<n;i++) sum+=(Math.Abs(x[i].r-y[i].r)+Math.Abs(x[i].g-y[i].g)+Math.Abs(x[i].b-y[i].b))/3.0; return (float)(sum/n); }
        private static void MaskedMae(string onPath,string offPath,string maskPath,out float target,out float nonTarget)
        {
            var onTexture=Decode(onPath); var offTexture=Decode(offPath); var maskTexture=Decode(maskPath);
            try
            {
                if(onTexture.width!=offTexture.width||onTexture.height!=offTexture.height||onTexture.width!=maskTexture.width||onTexture.height!=maskTexture.height) throw new InvalidOperationException("A/B/mask dimensions differ.");
                var on=onTexture.GetPixels32(); var off=offTexture.GetPixels32(); var maskPixels=maskTexture.GetPixels32();
                var mask=new bool[maskPixels.Length];
                for(var i=0;i<mask.Length;i++) mask[i]=maskPixels[i].r+maskPixels[i].g+maskPixels[i].b>30;
                var dilated=(bool[])mask.Clone(); const int radius=2; var width=onTexture.width; var height=onTexture.height;
                for(var y=0;y<height;y++) for(var x=0;x<width;x++) if(mask[y*width+x])
                    for(var dy=-radius;dy<=radius;dy++) for(var dx=-radius;dx<=radius;dx++)
                    { var xx=x+dx; var yy=y+dy; if(xx>=0&&xx<width&&yy>=0&&yy<height) dilated[yy*width+xx]=true; }
                double targetSum=0,nonSum=0; var targetCount=0; var nonCount=0;
                for(var i=0;i<on.Length;i++)
                {
                    var error=(Math.Abs(on[i].r-off[i].r)+Math.Abs(on[i].g-off[i].g)+Math.Abs(on[i].b-off[i].b))/3.0;
                    if(dilated[i]){targetSum+=error;targetCount++;} else {nonSum+=error;nonCount++;}
                }
                target=targetCount==0?0:(float)(targetSum/targetCount); nonTarget=nonCount==0?0:(float)(nonSum/nonCount);
            }
            finally{UnityEngine.Object.DestroyImmediate(onTexture);UnityEngine.Object.DestroyImmediate(offTexture);UnityEngine.Object.DestroyImmediate(maskTexture);}
        }
        private static void WriteDiff(string a,string b,string output)
        {
            var x=Decode(a); var y=Decode(b);
            try
            {
                var xp=x.GetPixels32(); var yp=y.GetPixels32(); var pixels=new Color32[xp.Length];
                for(var i=0;i<pixels.Length;i++) pixels[i]=new Color32((byte)Math.Min(255,Math.Abs(xp[i].r-yp[i].r)*4),(byte)Math.Min(255,Math.Abs(xp[i].g-yp[i].g)*4),(byte)Math.Min(255,Math.Abs(xp[i].b-yp[i].b)*4),255);
                var diff=new Texture2D(x.width,x.height,TextureFormat.RGB24,false,false);
                try{diff.SetPixels32(pixels);diff.Apply();File.WriteAllBytes(output,diff.EncodeToPNG());}finally{UnityEngine.Object.DestroyImmediate(diff);}
            }
            finally{UnityEngine.Object.DestroyImmediate(x);UnityEngine.Object.DestroyImmediate(y);}
        }
        private static Texture2D Decode(string path){var t=new Texture2D(2,2,TextureFormat.RGB24,false,false);if(!t.LoadImage(File.ReadAllBytes(path),false)){UnityEngine.Object.DestroyImmediate(t);throw new InvalidOperationException($"Cannot decode {path}");}return t;}
        private static Color32[] Load(string p){ var t=new Texture2D(2,2); try{ t.LoadImage(File.ReadAllBytes(p)); return t.GetPixels32(); } finally{ UnityEngine.Object.DestroyImmediate(t); } }
        private static float ForegroundLuminance(string p,Color32 bg){ var px=Load(p); double sum=0; var n=0; foreach(var c in px){ if(Math.Abs(c.r-bg.r)+Math.Abs(c.g-bg.g)+Math.Abs(c.b-bg.b)<12) continue; sum+=(0.2126*c.r+0.7152*c.g+0.0722*c.b)/255.0; n++; } return n==0?0:(float)(sum/n); }
        private static string PackageVersion(){ var p=Path.Combine(ProjectRoot(),"Packages/packages-lock.json"); var m=Regex.Match(File.ReadAllText(p),"com.unity.render-pipelines.universal[^}]*version\\\"\\s*:\\s*\\\"([^\\\"]+)"); return m.Success?m.Groups[1].Value:"unknown"; }
        private static string Artifact(string relative)=>Path.Combine(ProjectRoot(),"TestArtifacts/M4",relative);
        private static string ProjectRoot()=>Path.GetFullPath(Path.Combine(Application.dataPath,".."));
        private static string Absolute(string assetPath)=>Path.GetFullPath(Path.Combine(Application.dataPath,"..",assetPath));
    }
}
