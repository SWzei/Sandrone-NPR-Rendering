using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace SandroneToon.Editor
{
    public static class SandroneM9Validator
    {
        [Serializable] public sealed class Check { public string name,details;public bool passed; }
        [Serializable] public sealed class Report
        {
            public string generatedUtc,evidenceSessionId,unityVersion,graphicsApi,graphicsDevice;public int checkCount,failureCount,warningCount,shaderCompilerMessageCount;
            public float gradingMae,neutralAcesMae,aaMae,pcMobileMae,m8Saturation,m9Saturation,m8Luminance,m9Luminance;
            public int redPixels,magentaPixels;public SandroneM9Bootstrap.OverdrawReport overdraw;public SandroneM9Bootstrap.PlayerBuildReport playerBuild;
            public SandroneM9PerformanceProbe.Report playerPerformance;public VariantReport variants;
            public List<Check> checks=new();public List<string> failures=new();public List<string>warnings=new();
        }
        [Serializable] public sealed class VariantReport
        {
            public string generatedUtc,policy;public int callbackCount,inputVariants,removedVariants,retainedVariants;public VariantEntry[] entries;
        }
        [Serializable] public sealed class VariantEntry { public string shader,pass,passType;public int input,removed,retained; }

        [MenuItem("Sandrone/M9/Validate Final Composition")]
        public static void ValidateAndWriteReport()
        {
            var report=new Report{generatedUtc=DateTime.UtcNow.ToString("O"),evidenceSessionId=SandroneEvidenceSession.CurrentSessionId,unityVersion=Application.unityVersion,graphicsApi=SystemInfo.graphicsDeviceType.ToString(),graphicsDevice=SystemInfo.graphicsDeviceName};
            void Add(string name,bool passed,string details){report.checks.Add(new Check{name=name,passed=passed,details=details});if(!passed)report.failures.Add(name+": "+details);}
            Add("UnityVersion",Application.unityVersion=="6000.5.3f1",Application.unityVersion);Add("ColorSpaceLinear",PlayerSettings.colorSpace==ColorSpace.Linear,PlayerSettings.colorSpace.ToString());Add("FrameTimingStatsEnabled",PlayerSettings.enableFrameTimingStats,PlayerSettings.enableFrameTimingStats.ToString());
            Add("EvidenceSessionActive",!string.IsNullOrEmpty(report.evidenceSessionId),report.evidenceSessionId);
            AddCurrentReport("M8Gate","TestArtifacts/M8/M8ValidationReport.json",Add);
            AddCurrentReport("M7Gate","TestArtifacts/M7/M7ValidationReport.json",Add);AddCurrentReport("M6Gate","TestArtifacts/M6/M6ValidationReport.json",Add);
            AddCurrentReport("M5Gate","TestArtifacts/M5/M5ValidationReport.json",Add);AddCurrentReport("M0M4Gate","TestArtifacts/Audit/M0M4RegressionAudit.json",Add);

            var shaders=AssetDatabase.FindAssets("t:Shader",new[]{"Assets/Sandrone/Shaders"}).Select(AssetDatabase.GUIDToAssetPath).Select(AssetDatabase.LoadAssetAtPath<Shader>).Where(x=>x!=null).ToArray();
            var messages=shaders.SelectMany(shader=>ShaderUtil.GetShaderMessages(shader).Select(message=>$"{shader.name}:{message.message}")).ToArray();report.shaderCompilerMessageCount=messages.Length;
            Add("AllSandroneShadersSupported",shaders.All(x=>x.isSupported),string.Join(",",shaders.Where(x=>!x.isSupported).Select(x=>x.name)));Add("ShaderCompilerMessages",messages.Length==0,string.Join(" | ",messages));
            Add("M9NoCharacterShader",shaders.Count(x=>x.name.StartsWith("Sandrone/M9/"))==1,"only OverdrawAudit editor evidence shader");

            var profile=AssetDatabase.LoadAssetAtPath<SandroneM9FinalProfile>(SandroneM9Bootstrap.ProfilePath);Add("ProfileContract",profile!=null&&profile.ContractVersion=="SandroneFinalProfile_v1_M9",profile?.ContractVersion??"missing");
            Add("NeutralDecision",profile!=null&&profile.ToneMapping=="Neutral"&&profile.TaaDecision.StartsWith("Deferred_"),profile==null?"missing":$"tone={profile.ToneMapping}, taa={profile.TaaDecision}");
            Add("BoundedAdjustments",profile!=null&&profile.PostExposure>=-.25f&&profile.PostExposure<=0&&profile.Saturation>=-30&&profile.Saturation<=0&&profile.Contrast==0&&profile.HueShift==0,
                profile==null?"missing":$"exposure={profile.PostExposure}, saturation={profile.Saturation}, contrast={profile.Contrast}, hue={profile.HueShift}");
            var volumeProfile=AssetDatabase.LoadAssetAtPath<VolumeProfile>(SandroneM9Bootstrap.VolumeProfilePath);Tonemapping tone=null;ColorAdjustments color=null;volumeProfile?.TryGet(out tone);volumeProfile?.TryGet(out color);
            Add("FinalVolumeOnly",volumeProfile!=null&&volumeProfile.components.Count==2&&tone!=null&&color!=null,volumeProfile==null?"missing":string.Join(",",volumeProfile.components.Select(x=>x?.GetType().Name??"null")));
            Add("NeutralSerialized",tone!=null&&tone.active&&tone.mode.overrideState&&tone.mode.value==TonemappingMode.Neutral,tone==null?"missing":tone.mode.value.ToString());
            Add("ColorSerialized",color!=null&&color.active&&color.postExposure.overrideState&&color.saturation.overrideState&&Mathf.Approximately(color.postExposure.value,profile.PostExposure)&&Mathf.Approximately(color.saturation.value,profile.Saturation),
                color==null?"missing":$"exposure={color.postExposure.value}, saturation={color.saturation.value}");
            Add("BloomStillSeparate",volumeProfile!=null&&!volumeProfile.components.Any(x=>x is Bloom),"M8 Bloom remains in its dedicated profile");

            var scene=EditorSceneManager.OpenScene(SandroneM9Bootstrap.ScenePath,OpenSceneMode.Single);Add("SceneOpened",scene.IsValid(),scene.path);
            var m9=UnityEngine.Object.FindFirstObjectByType<SandroneM9FinalController>();var m8=UnityEngine.Object.FindFirstObjectByType<SandroneM8VfxBloomController>();var m7=UnityEngine.Object.FindFirstObjectByType<SandroneM7OutlineController>();
            var m6=UnityEngine.Object.FindFirstObjectByType<SandroneM6Controller>();var m5=UnityEngine.Object.FindFirstObjectByType<SandroneM5Controller>();var m0=UnityEngine.Object.FindFirstObjectByType<SandroneM0Controller>();var probe=UnityEngine.Object.FindFirstObjectByType<SandroneM9PerformanceProbe>();
            Add("ControllerChain",m0!=null&&m5!=null&&m6!=null&&m7!=null&&m8!=null&&m9!=null&&probe!=null,"M0+M5+M6+M7+M8+M9+player probe");
            var camera=UnityEngine.Object.FindFirstObjectByType<Camera>();var cameraData=camera?.GetUniversalAdditionalCameraData();Add("CameraHDRPost",camera!=null&&camera.allowHDR&&cameraData!=null&&cameraData.renderPostProcessing,$"hdr={camera?.allowHDR}, post={cameraData?.renderPostProcessing}");
            Add("DesktopSMAAHigh",cameraData!=null&&cameraData.antialiasing==AntialiasingMode.SubpixelMorphologicalAntiAliasing&&cameraData.antialiasingQuality==AntialiasingQuality.High,cameraData==null?"missing":$"{cameraData.antialiasing}/{cameraData.antialiasingQuality}");
            Add("M9VolumeBinding",m9!=null&&m9.GradingVolume!=null&&m9.GradingVolume.isGlobal&&m9.GradingVolume.sharedProfile==volumeProfile&&m9.GradingVolume.priority==20,m9?.GradingVolume?.name??"missing");
            Add("M8VolumePreserved",m8!=null&&m8.BloomVolume!=null&&m8.BloomVolume.sharedProfile==AssetDatabase.LoadAssetAtPath<VolumeProfile>(SandroneM8Bootstrap.VolumeProfilePath),m8?.BloomVolume?.sharedProfile?.name??"missing");
            Add("DefaultState",m9!=null&&m9.GradingEnabled&&m9.AntiAliasingEnabled&&!m9.MobileQuality,"Neutral + desktop SMAA");
            ValidateBindings(m8,m7,Add);Add("BuildSettingsM9Only",EditorBuildSettings.scenes.Length==1&&EditorBuildSettings.scenes[0].enabled&&EditorBuildSettings.scenes[0].path==SandroneM9Bootstrap.ScenePath,string.Join(",",EditorBuildSettings.scenes.Select(x=>x.path)));

            var required=new[]{"AB/M9_M8Baseline_PostOff_AAOff.png","AB/M9_Neutral_PostOn_AAOff.png","AB/M9_ACES_PostOn_AAOff.png","ReferenceComparison/M9_Final_Neutral_SMAA.png",
                "Pipeline/M9_PC_SMAA.png","Pipeline/M9_Mobile_FXAA.png","Debug/M9_OverdrawHeatmap.png","M9OverdrawAudit.json","M9ShaderVariantReport.json","M9PlayerBuildReport.json","M9PlayerPerformance.json","Player/M9Player_Final.ppm"};
            foreach(var item in required)Add("Artifact_"+Path.GetFileNameWithoutExtension(item),File.Exists(SandroneM9Bootstrap.Artifact(item)),item);
            if(required.All(x=>File.Exists(SandroneM9Bootstrap.Artifact(x))))ValidateEvidence(report,Add);

            report.warnings.Add("The additive overdraw audit is a locked-view fragment-layer estimate, not a hardware early-Z/ROP counter.");
            report.warnings.Add("Unity records GPU upload/allocation counters but not external-memory bandwidth; PIX/RenderDoc target-device bandwidth remains explicitly unmeasured.");
            report.warnings.Add("Material merging and 692-bone stripping were rejected for M9 because the current asset has incompatible stencil/transparent/pass states and no validated animation corpus.");
            report.warnings.Add("Original model imagery is descriptive art-direction context only and is not a validator input or repository dependency.");
            report.warningCount=report.warnings.Count;report.checkCount=report.checks.Count;report.failureCount=report.failures.Count;var json=JsonUtility.ToJson(report,true);File.WriteAllText(ReportPath(),json);
            var apiDirectory=SandroneM9Bootstrap.Artifact("Api/"+(SystemInfo.graphicsDeviceType==GraphicsDeviceType.Direct3D12?"D3D12":"D3D11"));Directory.CreateDirectory(apiDirectory);File.WriteAllText(Path.Combine(apiDirectory,"M9ValidationReport.json"),json);AssetDatabase.Refresh();
            if(report.failureCount>0)throw new BuildFailedException("M9 validation failed: "+string.Join("; ",report.failures));Debug.Log($"[Sandrone M9] Validation passed: {report.checkCount}/{report.checkCount}, warnings={report.warningCount}.");
        }

        private static void ValidateBindings(SandroneM8VfxBloomController m8,SandroneM7OutlineController m7,Action<string,bool,string> add)
        {
            if(m8==null||m7==null)return;var source=m8.CharacterRenderer;add("CharacterSlotsPreserved",source.sharedMaterials.Length==31,"slots="+source.sharedMaterials.Length);
            var map=AssetDatabase.LoadAssetAtPath<SandroneMaterialMap>(SandroneM0Bootstrap.MaterialMapPath);
            for(var slot=0;slot<31;slot++)
            {
                var entry=map.Entries.First(x=>x.sourceIndex==slot);Material expected;
                if(slot==SandroneM8Bootstrap.EyeSlot)expected=AssetDatabase.LoadAssetAtPath<Material>(SandroneM8Bootstrap.EyeMaterialPath);
                else expected=SandroneM6Bootstrap.TargetSlots.Contains(slot)?AssetDatabase.LoadAssetAtPath<Material>(SandroneM6Bootstrap.MaterialPath(slot,entry.materialAssetPath)):SandroneM6Bootstrap.BaselineMaterial(slot,entry.materialAssetPath);
                add($"ExactM8Material{slot:00}",source.sharedMaterials[slot]==expected,AssetDatabase.GetAssetPath(source.sharedMaterials[slot]));
            }
            add("OutlinePreserved",m7.OutlineRenderer.sharedMesh==AssetDatabase.LoadAssetAtPath<Mesh>(SandroneM7Bootstrap.MeshPath)&&m7.OutlineRenderer.sharedMaterials.Length==14,$"mesh={AssetDatabase.GetAssetPath(m7.OutlineRenderer.sharedMesh)}, mats={m7.OutlineRenderer.sharedMaterials.Length}");
            add("CrystalPreserved",m8.CrystalRenderer.sharedMaterials.Length==2&&m8.CrystalRenderer.sharedMaterials[0]==AssetDatabase.LoadAssetAtPath<Material>(SandroneM8Bootstrap.SwordBaseMaterialPath)&&m8.CrystalRenderer.sharedMaterials[1]==AssetDatabase.LoadAssetAtPath<Material>(SandroneM8Bootstrap.CrystalMaterialPath),"2 exact M8 materials");
            if(source is SkinnedMeshRenderer skinned)add("NoBoneOrMeshRewrite",skinned.bones.Length==692&&AssetDatabase.GetAssetPath(skinned.sharedMesh)=="Assets/Sandrone/Models/Sandrone_M0.fbx",$"bones={skinned.bones.Length}, mesh={AssetDatabase.GetAssetPath(skinned.sharedMesh)}");
        }

        private static void ValidateEvidence(Report report,Action<string,bool,string> add)
        {
            var baseline=Load("AB/M9_M8Baseline_PostOff_AAOff.png");var neutral=Load("AB/M9_Neutral_PostOn_AAOff.png");var final=Load("ReferenceComparison/M9_Final_Neutral_SMAA.png");var aces=Load("AB/M9_ACES_PostOn_AAOff.png");
            report.gradingMae=Mae(baseline,neutral);report.neutralAcesMae=Mae(neutral,aces);report.aaMae=Mae(neutral,final);report.pcMobileMae=Mae(Load("Pipeline/M9_PC_SMAA.png"),Load("Pipeline/M9_Mobile_FXAA.png"));
            add("GradingResponse",report.gradingMae>.05f,$"MAE={report.gradingMae:F4}");add("NeutralAcesDistinct",report.neutralAcesMae>.02f,$"MAE={report.neutralAcesMae:F4}");add("SMAAResponse",report.aaMae>.001f,$"MAE={report.aaMae:F4}");add("PCMobileCompatible",report.pcMobileMae<10f,$"MAE={report.pcMobileMae:F4}");
            (report.m8Luminance,report.m8Saturation)=Distribution(baseline);(report.m9Luminance,report.m9Saturation)=Distribution(final);
            var saturationReduction=report.m8Saturation-report.m9Saturation;var luminanceDelta=Mathf.Abs(report.m9Luminance-report.m8Luminance);
            add("SaturationReductionBounded",saturationReduction>.005f&&saturationReduction<.10f,$"M8={report.m8Saturation:F4}, M9={report.m9Saturation:F4}, reduction={saturationReduction:F4}");
            add("LuminanceChangeBounded",luminanceDelta<=.02f,$"M8={report.m8Luminance:F4}, M9={report.m9Luminance:F4}, absDelta={luminanceDelta:F4}");
            report.redPixels=Red(final);report.magentaPixels=Magenta(final);add("RedSkirtPreserved",report.redPixels>50000,"pixels="+report.redPixels);add("NoPinkMaterial",report.magentaPixels<10,"magenta="+report.magentaPixels);

            report.overdraw=JsonUtility.FromJson<SandroneM9Bootstrap.OverdrawReport>(File.ReadAllText(SandroneM9Bootstrap.Artifact("M9OverdrawAudit.json")));
            add("OverdrawMeasured",report.overdraw!=null&&report.overdraw.evidenceSessionId==report.evidenceSessionId&&report.overdraw.foregroundPixels>10000&&report.overdraw.meanLayers>=.75f&&report.overdraw.maxLayers<100,report.overdraw==null?"missing":$"session={report.overdraw.evidenceSessionId}, coverage={report.overdraw.coverage:F3}, mean={report.overdraw.meanLayers:F2}, p95={report.overdraw.p95Layers:F2}, max={report.overdraw.maxLayers:F2}");
            report.variants=JsonUtility.FromJson<VariantReport>(File.ReadAllText(SandroneM9Bootstrap.Artifact("M9ShaderVariantReport.json")));
            add("VariantAudit",report.variants!=null&&report.variants.policy=="SandroneM9ConservativeStrip_v1"&&report.variants.inputVariants>0&&report.variants.retainedVariants>0,report.variants==null?"missing":$"input={report.variants.inputVariants}, removed={report.variants.removedVariants}, retained={report.variants.retainedVariants}");
            add("VariantStripApplied",report.variants!=null&&report.variants.removedVariants>0,report.variants==null?"missing":"removed="+report.variants.removedVariants);
            report.playerBuild=JsonUtility.FromJson<SandroneM9Bootstrap.PlayerBuildReport>(File.ReadAllText(SandroneM9Bootstrap.Artifact("M9PlayerBuildReport.json")));
            add("WindowsPlayerBuild",report.playerBuild!=null&&report.playerBuild.evidenceSessionId==report.evidenceSessionId&&report.playerBuild.result=="Succeeded"&&report.playerBuild.errors==0&&report.playerBuild.warnings==0&&report.playerBuild.totalBytes>1000000,report.playerBuild==null?"missing":$"session={report.playerBuild.evidenceSessionId}, {report.playerBuild.result}, errors={report.playerBuild.errors}, warnings={report.playerBuild.warnings}, bytes={report.playerBuild.totalBytes}, seconds={report.playerBuild.totalSeconds:F1}");
            report.playerPerformance=JsonUtility.FromJson<SandroneM9PerformanceProbe.Report>(File.ReadAllText(SandroneM9Bootstrap.Artifact("M9PlayerPerformance.json")));
            add("PlayerRealGPU",report.playerPerformance!=null&&report.playerPerformance.evidenceSessionId==report.evidenceSessionId&&!report.playerPerformance.graphicsApi.Contains("Null")&&report.playerPerformance.sampledFrames==240&&
                report.playerPerformance.productName=="Sandrone Toon M9"&&report.playerPerformance.quality=="PC"&&report.playerPerformance.renderPipeline=="PC_RPAsset"&&
                Mathf.Abs(report.playerPerformance.renderScale-1f)<.001f&&report.playerPerformance.shadowCascadeCount==4&&report.playerPerformance.softShadows,
                report.playerPerformance==null?"missing":$"{report.playerPerformance.graphicsApi}/{report.playerPerformance.graphicsDevice}, product={report.playerPerformance.productName}, quality={report.playerPerformance.quality}, rp={report.playerPerformance.renderPipeline}, scale={report.playerPerformance.renderScale:F2}, cascades={report.playerPerformance.shadowCascadeCount}, soft={report.playerPerformance.softShadows}, samples={report.playerPerformance.sampledFrames}");
            add("PlayerFrameTiming",report.playerPerformance!=null&&report.playerPerformance.frameTimingSamples>0&&report.playerPerformance.cpuFrameSamples>0&&report.playerPerformance.gpuFrameSamples>0&&
                report.playerPerformance.frameDurationMedianMs>0&&report.playerPerformance.frameDurationP95Ms>=report.playerPerformance.frameDurationMedianMs&&
                report.playerPerformance.cpuFrameMeanMs>0&&report.playerPerformance.cpuFrameMedianMs>0&&report.playerPerformance.cpuFrameP95Ms>=report.playerPerformance.cpuFrameMedianMs&&report.playerPerformance.cpuFrameMaxMs>=report.playerPerformance.cpuFrameP95Ms&&
                report.playerPerformance.gpuFrameMeanMs>0&&report.playerPerformance.gpuFrameMedianMs>0&&report.playerPerformance.gpuFrameP95Ms>=report.playerPerformance.gpuFrameMedianMs&&report.playerPerformance.gpuFrameMaxMs>=report.playerPerformance.gpuFrameP95Ms,
                report.playerPerformance==null?"missing":$"frame median/p95={report.playerPerformance.frameDurationMedianMs:F3}/{report.playerPerformance.frameDurationP95Ms:F3}ms, CPU mean/median/p95={report.playerPerformance.cpuFrameMeanMs:F3}/{report.playerPerformance.cpuFrameMedianMs:F3}/{report.playerPerformance.cpuFrameP95Ms:F3}ms ({report.playerPerformance.cpuFrameSamples}), GPU mean/median/p95={report.playerPerformance.gpuFrameMeanMs:F3}/{report.playerPerformance.gpuFrameMedianMs:F3}/{report.playerPerformance.gpuFrameP95Ms:F3}ms ({report.playerPerformance.gpuFrameSamples}), packets={report.playerPerformance.frameTimingSamples}");
            var batches=report.playerPerformance?.counters?.FirstOrDefault(x=>x.name.IndexOf("Batch",StringComparison.OrdinalIgnoreCase)>=0);var draws=report.playerPerformance?.counters?.FirstOrDefault(x=>x.name.IndexOf("Draw Call",StringComparison.OrdinalIgnoreCase)>=0);var setpass=report.playerPerformance?.counters?.FirstOrDefault(x=>x.name=="SetPass Calls Count");var triangles=report.playerPerformance?.counters?.FirstOrDefault(x=>x.name=="Triangles Count");
            add("PlayerDrawCounters",setpass!=null&&setpass.meanRaw>0&&triangles!=null&&triangles.meanRaw>0,$"batches={batches?.meanRaw}, draws={draws?.meanRaw}, setpass={setpass?.meanRaw}, triangles={triangles?.meanRaw}");
            var player=LoadPpm(SandroneM9Bootstrap.Artifact("Player/M9Player_Final.ppm"));add("PlayerNoPink",Magenta(player)<10,"magenta="+Magenta(player));add("PlayerRedSkirt",Red(player)>20000,"red="+Red(player));
        }

        private static void AddCurrentReport(string name,string projectRelativePath,Action<string,bool,string> add)
        {
            var passed=SandroneEvidenceSession.IsCurrentSuccessfulReport(projectRelativePath,out var details);add(name,passed,details);
        }
        private static Texture2D Load(string relative)=>LoadAbsolute(SandroneM9Bootstrap.Artifact(relative));
        private static Texture2D LoadAbsolute(string path){var image=new Texture2D(2,2,TextureFormat.RGBA32,false);image.LoadImage(File.ReadAllBytes(path),false);return image;}
        private static Texture2D LoadPpm(string path)
        {
            using var stream=new FileStream(path,FileMode.Open,FileAccess.Read);using var reader=new BinaryReader(stream);
            string Token(){var chars=new List<char>();byte value;do{value=reader.ReadByte();}while(char.IsWhiteSpace((char)value));while(!char.IsWhiteSpace((char)value)){chars.Add((char)value);value=reader.ReadByte();}return new string(chars.ToArray());}
            if(Token()!="P6")throw new InvalidDataException("Player capture is not P6 PPM.");var width=int.Parse(Token());var height=int.Parse(Token());if(Token()!="255")throw new InvalidDataException("Unsupported PPM range.");
            var pixels=new Color32[width*height];for(var y=0;y<height;y++)for(var x=0;x<width;x++)pixels[(height-1-y)*width+x]=new Color32(reader.ReadByte(),reader.ReadByte(),reader.ReadByte(),255);
            var image=new Texture2D(width,height,TextureFormat.RGBA32,false);image.SetPixels32(pixels);image.Apply();return image;
        }
        private static float Mae(Texture2D a,Texture2D b){if(a.width!=b.width||a.height!=b.height)return float.PositiveInfinity;var x=a.GetPixels32();var y=b.GetPixels32();double sum=0;for(var i=0;i<x.Length;i++)sum+=Math.Abs(x[i].r-y[i].r)+Math.Abs(x[i].g-y[i].g)+Math.Abs(x[i].b-y[i].b);return(float)(sum/(x.Length*3.0));}
        private static (float luminance,float saturation) Distribution(Texture2D image)
        {
            var pixels=image.GetPixels32();var corner=Color.black;var cornerCount=0;for(var y=0;y<Math.Min(32,image.height);y++)for(var x=0;x<Math.Min(32,image.width);x++){corner+=(Color)pixels[y*image.width+x];cornerCount++;}corner/=Math.Max(1,cornerCount);double luma=0,sat=0;var count=0;
            foreach(var p in pixels){var c=(Color)p;if(Vector3.Distance(new Vector3(c.r,c.g,c.b),new Vector3(corner.r,corner.g,corner.b))<=.08f)continue;Color.RGBToHSV(c,out _,out var s,out _);sat+=s;luma+=c.r*.2126+c.g*.7152+c.b*.0722;count++;}
            return count==0?(0,0):((float)(luma/count),(float)(sat/count));
        }
        private static int Red(Texture2D image)=>image.GetPixels32().Count(p=>p.r>48&&p.r>p.g*1.35f&&p.r>p.b*1.2f);
        private static int Magenta(Texture2D image)=>image.GetPixels32().Count(p=>p.r>220&&p.b>220&&p.g<64);
        private static string ReportPath()=>SandroneM9Bootstrap.Artifact("M9ValidationReport.json");
    }
}
