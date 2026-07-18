using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace SandroneToon.Editor
{
    public static class SandroneM8Validator
    {
        [Serializable] public sealed class Check { public string name, details; public bool passed; }
        [Serializable] public sealed class Report
        {
            public string generatedUtc, unityVersion, graphicsApi, graphicsDevice;
            public int checkCount, failureCount, warningCount, shaderCompilerMessageCount;
            public float baselineM7Mae, legacyM7LdrMae, eyeEmissionMae, eyeEmissionTargetMae, eyeBloomMae, crystalEmissionMae, crystalEmissionTargetMae, crystalBloomMae, combinedBloomMae, pcMobileMae;
            public int eyeMaskPixels, crystalMaskPixels, eyeEmissionPixels, crystalEmissionPixels, bloomExtractionEyePixels, bloomExtractionCrystalPixels, redSkirtPixels;
            public SandroneM8Bootstrap.HdrAudit hdrAudit;
            public List<Check> checks=new(); public List<string> failures=new(); public List<string>warnings=new();
            public string[] intentionallyDeferred={"Display-screen emission (third candidate)","Dissolve/particles/trails","M9 tonemapping, grading, AA, variant stripping and target-device profiling"};
        }
        [Serializable] private sealed class SwordReport
        {
            public string phase, source_sha256, blender_version, fbx_sha256; public int vertex_count, triangle_count, material_count, source_bone_count;
            public SwordMaterial[] materials;
        }
        [Serializable] private sealed class SwordMaterial { public int index, triangle_count; public string name_j; public bool double_sided; }

        [MenuItem("Sandrone/M8/Validate VFX and Bloom")]
        public static void ValidateAndWriteReport()
        {
            var report=new Report{generatedUtc=DateTime.UtcNow.ToString("O"),unityVersion=Application.unityVersion,
                graphicsApi=SystemInfo.graphicsDeviceType.ToString(),graphicsDevice=SystemInfo.graphicsDeviceName};
            void Add(string name,bool passed,string details){report.checks.Add(new Check{name=name,passed=passed,details=details});if(!passed)report.failures.Add(name+": "+details);}

            Add("UnityVersion",Application.unityVersion=="6000.5.3f1",Application.unityVersion);
            Add("ColorSpaceLinear",PlayerSettings.colorSpace==ColorSpace.Linear,PlayerSettings.colorSpace.ToString());
            AddCurrentReport("M7GateReport","TestArtifacts/M7/M7ValidationReport.json",Add);
            AddCurrentReport("M6GateReport","TestArtifacts/M6/M6ValidationReport.json",Add);
            AddCurrentReport("M5GateReport","TestArtifacts/M5/M5ValidationReport.json",Add);
            AddCurrentReport("M0M4GateReport","TestArtifacts/Audit/M0M4RegressionAudit.json",Add);
            var eyeShader=AssetDatabase.LoadAssetAtPath<Shader>(SandroneM8Bootstrap.EyeShaderPath);var vfxShader=AssetDatabase.LoadAssetAtPath<Shader>(SandroneM8Bootstrap.VfxShaderPath);
            Add("EyeShaderSupported",eyeShader!=null&&eyeShader.isSupported,eyeShader?.name??"missing");
            Add("VfxShaderSupported",vfxShader!=null&&vfxShader.isSupported,vfxShader?.name??"missing");
            var shaderMessages=new List<string>();
            foreach(var shader in new[]{eyeShader,vfxShader}.Where(x=>x!=null))shaderMessages.AddRange(ShaderUtil.GetShaderMessages(shader).Select(x=>$"{shader.name}: {x.message}"));
            report.shaderCompilerMessageCount=shaderMessages.Count;Add("ShaderCompilerMessages",shaderMessages.Count==0,string.Join(" | ",shaderMessages));
            var eyeSource=ReadAsset(SandroneM8Bootstrap.EyeShaderPath);var vfxSource=ReadAsset(SandroneM8Bootstrap.VfxShaderPath);var m6Source=ReadAsset(SandroneM6Bootstrap.ShaderPath);
            Add("EyePass",eyeSource.Contains("Name \"M8HairEyeEmission\"")&&eyeSource.Contains("\"LightMode\"=\"UniversalForward\""),"M8HairEyeEmission/UniversalForward");
            Add("VfxPass",vfxSource.Contains("Name \"M8VFXEmission\"")&&vfxSource.Contains("\"LightMode\"=\"UniversalForward\""),"M8VFXEmission/UniversalForward");
            var m6Fields=CBufferFields(m6Source);var m8Fields=CBufferFields(eyeSource);
            Add("M6CBufferPrefix",m8Fields.Take(m6Fields.Count).SequenceEqual(m6Fields),$"M6={m6Fields.Count}, M8={m8Fields.Count}");
            Add("HdrNotSaturated",eyeSource.Contains("baseOutput+emission")&&vfxSource.Contains("saturate(baseSample.rgb)+emission"),"emission appended after bounded LDR base");
            Add("MaskDrivenEmission",eyeSource.Contains("SAMPLE_TEXTURE2D(_EmissionMask")&&vfxSource.Contains("SAMPLE_TEXTURE2D(_EmissionMask"),"explicit masks");
            Add("NoM9Features",!eyeSource.Contains("Tonemap")&&!vfxSource.Contains("Tonemap")&&!eyeSource.Contains("RendererFeature")&&!vfxSource.Contains("RendererFeature"),"no tone mapping/grading/custom post pass");

            var profile=AssetDatabase.LoadAssetAtPath<SandroneM8VfxBloomProfile>(SandroneM8Bootstrap.ProfilePath);
            Add("ProfileContract",profile!=null&&profile.ContractVersion=="SandroneVfxBloomProfile_v1_M8",profile?.ContractVersion??"missing");
            Add("SelectedModules",profile!=null&&profile.SelectedModules=="EyeLight+CrystallineSword",profile?.SelectedModules??"missing");
            Add("HdrRanges",profile!=null&&profile.EyeEmissionIntensity>1f&&profile.CrystalEmissionIntensity>1f&&profile.BloomThreshold>1f,
                profile==null?"missing":$"eye={profile.EyeEmissionIntensity}, crystal={profile.CrystalEmissionIntensity}, threshold={profile.BloomThreshold}");
            var volumeProfile=AssetDatabase.LoadAssetAtPath<VolumeProfile>(SandroneM8Bootstrap.VolumeProfilePath);Bloom bloom=null;volumeProfile?.TryGet(out bloom);
            Add("DedicatedBloomOnly",volumeProfile!=null&&volumeProfile.components.Count==1&&bloom!=null,volumeProfile==null?"missing":string.Join(",",volumeProfile.components.Select(x=>x.GetType().Name)));
            Add("BloomThreshold",bloom!=null&&bloom.threshold.overrideState&&bloom.threshold.value>1f,bloom==null?"missing":bloom.threshold.value.ToString("F3"));
            Add("BloomBounded",bloom!=null&&Mathf.Approximately(bloom.intensity.value,.35f)&&bloom.clamp.value<=8f&&bloom.dirtIntensity.value==0f,
                bloom==null?"missing":$"intensity={bloom.intensity.value}, scatter={bloom.scatter.value}, clamp={bloom.clamp.value}");
            Add("NoGradingComponents",volumeProfile!=null&&!volumeProfile.components.Any(x=>x is Tonemapping||x is ColorAdjustments||x is ColorLookup),"M9 deferred");

            ValidateTextures(report,Add);
            ValidateBlenderAndModel(Add);
            var scene=EditorSceneManager.OpenScene(SandroneM8Bootstrap.ScenePath,OpenSceneMode.Single);Add("SceneOpened",scene.IsValid(),scene.path);
            var m8=UnityEngine.Object.FindFirstObjectByType<SandroneM8VfxBloomController>();var m7=UnityEngine.Object.FindFirstObjectByType<SandroneM7OutlineController>();
            var m6=UnityEngine.Object.FindFirstObjectByType<SandroneM6Controller>();var m5=UnityEngine.Object.FindFirstObjectByType<SandroneM5Controller>();var m0=UnityEngine.Object.FindFirstObjectByType<SandroneM0Controller>();
            Add("ControllerChain",m0!=null&&m5!=null&&m6!=null&&m7!=null&&m8!=null,"M0+M5+M6+M7+M8");
            var camera=UnityEngine.Object.FindFirstObjectByType<Camera>();var data=camera?.GetUniversalAdditionalCameraData();
            Add("CameraHDR",camera!=null&&camera.allowHDR,camera==null?"missing":camera.allowHDR.ToString());
            Add("PostProcessingEnabled",data!=null&&data.renderPostProcessing,data==null?"missing":data.renderPostProcessing.ToString());
            Add("VolumeBinding",m8!=null&&m8.BloomVolume!=null&&m8.BloomVolume.isGlobal&&m8.BloomVolume.sharedProfile==volumeProfile,m8?.BloomVolume?.name??"missing");
            Add("DefaultState",m8!=null&&m8.EyeEmissionEnabled&&m8.CrystalEmissionEnabled&&m8.CrystalVisible&&m8.BloomEnabled&&m8.DebugMode==SandroneM8DebugMode.FinalColor,"eye+crystal+bloom final");
            ValidateBindings(m8,m7,Add);
            Add("BuildSettingsM8Only",EditorBuildSettings.scenes.Length==1&&EditorBuildSettings.scenes[0].enabled&&EditorBuildSettings.scenes[0].path==SandroneM8Bootstrap.ScenePath,
                string.Join(",",EditorBuildSettings.scenes.Select(x=>$"{x.enabled}:{x.path}")));

            var required=new[]{"AB/M8_M7Baseline_AllOff.png","AB/M8_M7Control_SameHDR.png","AB/M8_EyeEmissionOff.png","AB/M8_EyeEmission_NoBloom.png","AB/M8_EyeEmission_Bloom.png",
                "AB/M8_CrystalEmissionOff.png","AB/M8_CrystalEmission_NoBloom.png","AB/M8_CrystalEmission_Bloom.png",
                "ReferenceComparison/M8_FinalCombined_Front.png","AB/M8_Combined_BloomOff.png","Debug/M8_Eye_EmissionMask.png",
                "Debug/M8_Eye_BloomExtraction.png","Debug/M8_Crystal_EmissionMask.png","Debug/M8_Crystal_BloomExtraction.png",
                "Pipeline/M8_PC_ForwardPlus.png","Pipeline/M8_Mobile_Forward.png","M8HdrExtractionAudit.json"};
            foreach(var relative in required)Add("Capture_"+Path.GetFileNameWithoutExtension(relative),File.Exists(SandroneM8Bootstrap.Artifact(relative)),relative);
            if(required.All(x=>File.Exists(SandroneM8Bootstrap.Artifact(x))))ValidateImages(report,Add);

            report.warnings.Add("EyeLight mask is an exact alpha extraction from source 目光.png; crystal mask is a documented cyan-channel project seed inside the independently isolated Mat_Cyrstal submesh. Both require art review before production.");
            report.warnings.Add("The crystalline sword is an M8 calibration prop, not a hand-authored attachment/animation setup; its renderer intentionally does not cast shadows in the VFX audit.");
            report.warnings.Add("Bloom adds full-screen post-processing cost. GPU time, bandwidth, SRP Batcher, physical mobile device and built-player variants remain unmeasured for M9.");
            report.warningCount=report.warnings.Count;report.checkCount=report.checks.Count;report.failureCount=report.failures.Count;
            Directory.CreateDirectory(Path.GetDirectoryName(ReportPath())!);File.WriteAllText(ReportPath(),JsonUtility.ToJson(report,true));AssetDatabase.Refresh();
            if(report.failureCount>0)throw new BuildFailedException("M8 validation failed: "+string.Join("; ",report.failures));
            Debug.Log($"[Sandrone M8] Validation passed: {report.checkCount}/{report.checkCount}, warnings={report.warningCount}.");
        }

        private static void ValidateTextures(Report report,Action<string,bool,string> add)
        {
            var eyeSource=LoadTexture(Path.Combine(Path.GetFullPath(Path.Combine(Application.dataPath,"../../..")),"【桑多涅】","tex","目光.png"));
            var eyeMask=LoadTexture(SandroneM8Bootstrap.Absolute(SandroneM8Bootstrap.EyeMaskPath));
            var sourcePixels=eyeSource.GetPixels32();var maskPixels=eyeMask.GetPixels32();var exact=sourcePixels.Length==maskPixels.Length;
            report.eyeMaskPixels=0;if(exact)for(var i=0;i<sourcePixels.Length;i++){if(maskPixels[i].r>0)report.eyeMaskPixels++;if(maskPixels[i].r!=sourcePixels[i].a||maskPixels[i].g!=sourcePixels[i].a||maskPixels[i].b!=sourcePixels[i].a){exact=false;break;}}
            add("EyeMaskExactAlpha",exact,$"nonzero={report.eyeMaskPixels}, size={eyeMask.width}x{eyeMask.height}");
            UnityEngine.Object.DestroyImmediate(eyeSource);UnityEngine.Object.DestroyImmediate(eyeMask);
            var sourceWeapon=Path.Combine(Path.GetFullPath(Path.Combine(Application.dataPath,"../../..")),"【桑多涅】","tex","武器1.png");
            add("WeaponBaseByteIdentical",Hash(sourceWeapon)==Hash(SandroneM8Bootstrap.Absolute(SandroneM8Bootstrap.WeaponBasePath)),Hash(sourceWeapon));
            var crystalMask=LoadTexture(SandroneM8Bootstrap.Absolute(SandroneM8Bootstrap.CrystalMaskPath));report.crystalMaskPixels=crystalMask.GetPixels32().Count(x=>x.r>0);
            add("CrystalMaskNonEmpty",report.crystalMaskPixels>100000&&report.crystalMaskPixels<500000,$"nonzero={report.crystalMaskPixels}");UnityEngine.Object.DestroyImmediate(crystalMask);
            foreach(var path in new[]{SandroneM8Bootstrap.EyeMaskPath,SandroneM8Bootstrap.CrystalMaskPath})
            {
                var importer=AssetImporter.GetAtPath(path)as TextureImporter;
                add("LinearMask_"+Path.GetFileNameWithoutExtension(path),importer!=null&&!importer.sRGBTexture&&importer.mipmapEnabled&&!importer.isReadable&&importer.textureCompression==TextureImporterCompression.Uncompressed,
                    importer==null?"missing":$"sRGB={importer.sRGBTexture}, mip={importer.mipmapEnabled}, readable={importer.isReadable}, compression={importer.textureCompression}");
            }
        }

        private static void ValidateBlenderAndModel(Action<string,bool,string> add)
        {
            var path=SandroneM8Bootstrap.Absolute(SandroneM8Bootstrap.BlenderReportRelative);var parsed=File.Exists(path)?JsonUtility.FromJson<SwordReport>(File.ReadAllText(path)):null;
            add("BlenderReport",parsed!=null&&parsed.phase=="M8"&&parsed.blender_version=="5.1.2",parsed==null?"missing":parsed.blender_version);
            add("SwordSourceStructure",parsed!=null&&parsed.vertex_count==7473&&parsed.triangle_count==6492&&parsed.material_count==2&&parsed.source_bone_count==1,
                parsed==null?"missing":$"v={parsed.vertex_count}, tri={parsed.triangle_count}, mat={parsed.material_count}, bone={parsed.source_bone_count}");
            add("CrystalMaterialSource",parsed?.materials!=null&&parsed.materials.Length==2&&parsed.materials[1].name_j=="Mat_Cyrstal"&&parsed.materials[1].triangle_count==1576,
                parsed?.materials==null?"missing":string.Join(",",parsed.materials.Select(x=>$"{x.index}:{x.name_j}:{x.triangle_count}")));
            add("SwordFbxHash",parsed!=null&&parsed.fbx_sha256==Hash(SandroneM8Bootstrap.Absolute(SandroneM8Bootstrap.SwordModelPath)),parsed?.fbx_sha256??"missing");
            var model=AssetDatabase.LoadAssetAtPath<GameObject>(SandroneM8Bootstrap.SwordModelPath);var renderer=model?.GetComponentsInChildren<Renderer>(true).SingleOrDefault();
            var modelImporter=AssetImporter.GetAtPath(SandroneM8Bootstrap.SwordModelPath)as ModelImporter;
            add("SwordUnityModel",model!=null&&renderer!=null&&modelImporter!=null&&!modelImporter.isReadable,$"renderer={renderer?.name??"missing"}, readable={modelImporter?.isReadable}");
            if(renderer is SkinnedMeshRenderer skinned)add("SwordSubmeshes",skinned.sharedMesh!=null&&skinned.sharedMesh.subMeshCount==2,$"submeshes={skinned.sharedMesh?.subMeshCount}");
            else if(renderer is MeshRenderer meshRenderer){var mesh=meshRenderer.GetComponent<MeshFilter>()?.sharedMesh;add("SwordSubmeshes",mesh!=null&&mesh.subMeshCount==2,$"submeshes={mesh?.subMeshCount}");}
        }

        private static void ValidateBindings(SandroneM8VfxBloomController m8,SandroneM7OutlineController m7,Action<string,bool,string> add)
        {
            if(m8==null||m7==null)return;var source=m8.CharacterRenderer;add("SourceRendererPreserved",source==m7.SourceRenderer,source?.name??"missing");
            add("EyeSlotM8Only",source.sharedMaterials[SandroneM8Bootstrap.EyeSlot].shader.name=="Sandrone/M8/HairEyeEmission",AssetDatabase.GetAssetPath(source.sharedMaterials[SandroneM8Bootstrap.EyeSlot]));
            var map=AssetDatabase.LoadAssetAtPath<SandroneMaterialMap>(SandroneM0Bootstrap.MaterialMapPath);
            for(var slot=0;slot<31;slot++)
            {
                if(slot==SandroneM8Bootstrap.EyeSlot)continue;var entry=map.Entries.First(x=>x.sourceIndex==slot);
                var expected=SandroneM6Bootstrap.TargetSlots.Contains(slot)?AssetDatabase.LoadAssetAtPath<Material>(SandroneM6Bootstrap.MaterialPath(slot,entry.materialAssetPath)):SandroneM6Bootstrap.BaselineMaterial(slot,entry.materialAssetPath);
                add($"ExactM7CharacterMaterial{slot:00}",source.sharedMaterials[slot]==expected,AssetDatabase.GetAssetPath(source.sharedMaterials[slot]));
            }
            var eye=source.sharedMaterials[SandroneM8Bootstrap.EyeSlot];var entry10=map.Entries.First(x=>x.sourceIndex==SandroneM8Bootstrap.EyeSlot);
            var m6Eye=AssetDatabase.LoadAssetAtPath<Material>(SandroneM6Bootstrap.MaterialPath(SandroneM8Bootstrap.EyeSlot,entry10.materialAssetPath));
            add("EyeBaseBindingPreserved",eye.GetTexture("_BaseMap")==m6Eye.GetTexture("_BaseMap")&&eye.renderQueue==m6Eye.renderQueue&&
                Mathf.Approximately(eye.GetFloat("_SrcBlend"),m6Eye.GetFloat("_SrcBlend"))&&Mathf.Approximately(eye.GetFloat("_DstBlend"),m6Eye.GetFloat("_DstBlend")),
                $"queue={eye.renderQueue}, blend={eye.GetFloat("_SrcBlend")}/{eye.GetFloat("_DstBlend")}");
            add("EyeStencilPreserved",eye.GetFloat("_M6StencilRef")==1&&eye.GetFloat("_M6StencilReadMask")==1&&eye.GetFloat("_M6StencilWriteMask")==0&&eye.GetFloat("_M6StencilComp")==3,
                $"ref/read/write/comp={eye.GetFloat("_M6StencilRef")}/{eye.GetFloat("_M6StencilReadMask")}/{eye.GetFloat("_M6StencilWriteMask")}/{eye.GetFloat("_M6StencilComp")}");
            var eyeBlock=new MaterialPropertyBlock();source.GetPropertyBlock(eyeBlock,SandroneM8Bootstrap.EyeSlot);
            add("EyeMPBScoped",eyeBlock.HasFloat("_M8EmissionWeight")&&eyeBlock.HasFloat("_M8DebugMode")&&eyeBlock.HasFloat("_M8BloomThreshold")&&
                !eyeBlock.HasTexture("_BaseMap")&&!eyeBlock.HasTexture("_EmissionMask")&&!eyeBlock.HasVector("_BaseMap_ST")&&!eyeBlock.HasColor("_BaseColor"),
                "only M8 weight/debug/threshold; no BaseMap/EmissionMask/ST/BaseColor override");
            add("OutlineRendererPreserved",m7.OutlineRenderer.sharedMesh==AssetDatabase.LoadAssetAtPath<Mesh>(SandroneM7Bootstrap.MeshPath)&&m7.OutlineRenderer.sharedMaterials.Length==14,
                $"mesh={AssetDatabase.GetAssetPath(m7.OutlineRenderer.sharedMesh)}, mats={m7.OutlineRenderer.sharedMaterials.Length}");
            add("CrystalRenderer",m8.CrystalRenderer!=null&&m8.CrystalRenderer.sharedMaterials.Length==2,m8.CrystalRenderer?.name??"missing");
            if(m8.CrystalRenderer!=null)
            {
                add("SwordBaseMaterial",m8.CrystalRenderer.sharedMaterials[0]==AssetDatabase.LoadAssetAtPath<Material>(SandroneM8Bootstrap.SwordBaseMaterialPath),AssetDatabase.GetAssetPath(m8.CrystalRenderer.sharedMaterials[0]));
                add("CrystalEmissionMaterial",m8.CrystalRenderer.sharedMaterials[1]==AssetDatabase.LoadAssetAtPath<Material>(SandroneM8Bootstrap.CrystalMaterialPath),AssetDatabase.GetAssetPath(m8.CrystalRenderer.sharedMaterials[1]));
                add("CrystalNoShadows",m8.CrystalRenderer.shadowCastingMode==ShadowCastingMode.Off&&!m8.CrystalRenderer.receiveShadows,$"cast={m8.CrystalRenderer.shadowCastingMode}, receive={m8.CrystalRenderer.receiveShadows}");
                var crystalBlock=new MaterialPropertyBlock();m8.CrystalRenderer.GetPropertyBlock(crystalBlock,SandroneM8Bootstrap.CrystalSlot);
                add("CrystalMPBScoped",crystalBlock.HasFloat("_M8EmissionWeight")&&crystalBlock.HasFloat("_M8DebugMode")&&crystalBlock.HasFloat("_M8BloomThreshold")&&
                    !crystalBlock.HasTexture("_BaseMap")&&!crystalBlock.HasTexture("_EmissionMask")&&!crystalBlock.HasVector("_BaseMap_ST")&&!crystalBlock.HasColor("_BaseColor"),
                    "only M8 weight/debug/threshold; no BaseMap/EmissionMask/ST/BaseColor override");
            }
        }

        private static void ValidateImages(Report report,Action<string,bool,string> add)
        {
            var baseline=Load("AB/M8_M7Baseline_AllOff.png");var sameHdrControl=Load("AB/M8_M7Control_SameHDR.png");
            report.baselineM7Mae=Mae(baseline,sameHdrControl);add("AllOffPreservesM7SameConfig",report.baselineM7Mae<.5f,$"same HDR/camera/lighting MAE={report.baselineM7Mae:F4}");
            var m7Path=SandroneM8Bootstrap.Absolute("../TestArtifacts/M7/ReferenceComparison/M7_FinalToon_Front.png");
            if(File.Exists(m7Path)){var m7=LoadAbsolute(m7Path);report.legacyM7LdrMae=Mae(baseline,m7);}
            var eyeOff=Load("AB/M8_EyeEmissionOff.png");
            var eyeNo=Load("AB/M8_EyeEmission_NoBloom.png");var eyeBloom=Load("AB/M8_EyeEmission_Bloom.png");
            var eyeStats=Difference(eyeOff,eyeNo);report.eyeEmissionMae=Mae(eyeOff,eyeNo);report.eyeEmissionPixels=eyeStats.changedPixels;report.eyeEmissionTargetMae=eyeStats.targetMae;
            add("EyeEmissionVisible",eyeStats.changedPixels>20&&eyeStats.targetMae>3f,$"pixels={eyeStats.changedPixels}, targetMAE={eyeStats.targetMae:F3}, frameMAE={report.eyeEmissionMae:F4}");
            report.eyeBloomMae=Mae(eyeNo,eyeBloom);add("EyeBloomVisible",report.eyeBloomMae>.01f,$"MAE={report.eyeBloomMae:F4}");
            var crystalOff=Load("AB/M8_CrystalEmissionOff.png");var crystalNo=Load("AB/M8_CrystalEmission_NoBloom.png");var crystalBloom=Load("AB/M8_CrystalEmission_Bloom.png");
            var crystalStats=Difference(crystalOff,crystalNo);report.crystalEmissionMae=Mae(crystalOff,crystalNo);report.crystalEmissionPixels=crystalStats.changedPixels;report.crystalEmissionTargetMae=crystalStats.targetMae;report.crystalBloomMae=Mae(crystalNo,crystalBloom);
            add("CrystalEmissionVisible",crystalStats.changedPixels>100&&crystalStats.targetMae>3f,$"pixels={crystalStats.changedPixels}, targetMAE={crystalStats.targetMae:F3}, frameMAE={report.crystalEmissionMae:F4}");add("CrystalBloomVisible",report.crystalBloomMae>.01f,$"MAE={report.crystalBloomMae:F4}");
            var combined=Load("ReferenceComparison/M8_FinalCombined_Front.png");var combinedOff=Load("AB/M8_Combined_BloomOff.png");report.combinedBloomMae=Mae(combined,combinedOff);
            add("CombinedBloomToggle",report.combinedBloomMae>.01f,$"MAE={report.combinedBloomMae:F4}");report.redSkirtPixels=RedPixels(combined);add("RedSkirtPreserved",report.redSkirtPixels>50000,$"pixels={report.redSkirtPixels}");
            report.bloomExtractionEyePixels=NonBlack(Load("Debug/M8_Eye_BloomExtraction.png"));report.bloomExtractionCrystalPixels=NonBlack(Load("Debug/M8_Crystal_BloomExtraction.png"));
            add("EyeExtractionNonEmpty",report.bloomExtractionEyePixels>20&&report.bloomExtractionEyePixels<200000,$"pixels={report.bloomExtractionEyePixels}");
            add("CrystalExtractionNonEmpty",report.bloomExtractionCrystalPixels>100&&report.bloomExtractionCrystalPixels<300000,$"pixels={report.bloomExtractionCrystalPixels}");
            report.pcMobileMae=Mae(Load("Pipeline/M8_PC_ForwardPlus.png"),Load("Pipeline/M8_Mobile_Forward.png"));add("PCMobileCompatible",report.pcMobileMae<10f,$"MAE={report.pcMobileMae:F4}");
            report.hdrAudit=JsonUtility.FromJson<SandroneM8Bootstrap.HdrAudit>(File.ReadAllText(SandroneM8Bootstrap.Artifact("M8HdrExtractionAudit.json")));
            add("ActualHDRAboveThreshold",report.hdrAudit!=null&&report.hdrAudit.peakHdr>report.hdrAudit.bloomThresholdLinear&&report.hdrAudit.hdrPixelCount>20,
                report.hdrAudit==null?"missing":$"peak={report.hdrAudit.peakHdr:F3}, threshold={report.hdrAudit.bloomThresholdLinear:F3}, pixels={report.hdrAudit.hdrPixelCount}");
            add("ExtractionContractIsolated",report.hdrAudit!=null&&report.hdrAudit.outsideExtractionPixels==0&&report.hdrAudit.outsideExtractionRatio==0f,
                report.hdrAudit==null?"missing":$"outside={report.hdrAudit.outsideExtractionPixels}, ratio={report.hdrAudit.outsideExtractionRatio:F4}; non-M8 Final shaders are bounded <=1");
        }

        private static void AddCurrentReport(string name,string projectRelativePath,Action<string,bool,string> add)
        {
            var passed=SandroneEvidenceSession.IsCurrentSuccessfulReport(projectRelativePath,out var details);add(name,passed,details);
        }
        private static string ReadAsset(string path)=>File.Exists(SandroneM8Bootstrap.Absolute(path))?File.ReadAllText(SandroneM8Bootstrap.Absolute(path)):"";
        private static List<string> CBufferFields(string source){var match=Regex.Match(source,@"CBUFFER_START\(UnityPerMaterial\)(.*?)CBUFFER_END",RegexOptions.Singleline);return match.Success?Regex.Matches(match.Groups[1].Value,@"\b(?:float|half)(?:[234](?:x[234])?)?\s+(_[A-Za-z0-9_]+)").Cast<Match>().Select(x=>x.Groups[1].Value).ToList():new List<string>();}
        private static Texture2D Load(string relative)=>LoadAbsolute(SandroneM8Bootstrap.Artifact(relative));
        private static Texture2D LoadAbsolute(string path){var t=new Texture2D(2,2,TextureFormat.RGBA32,false);t.LoadImage(File.ReadAllBytes(path),false);return t;}
        private static Texture2D LoadTexture(string path)=>LoadAbsolute(path);
        private static float Mae(Texture2D a,Texture2D b){if(a.width!=b.width||a.height!=b.height)return float.PositiveInfinity;var x=a.GetPixels32();var y=b.GetPixels32();double sum=0;for(var i=0;i<x.Length;i++)sum+=Math.Abs(x[i].r-y[i].r)+Math.Abs(x[i].g-y[i].g)+Math.Abs(x[i].b-y[i].b);return(float)(sum/(x.Length*3.0));}
        private static (int changedPixels,float targetMae) Difference(Texture2D a,Texture2D b)
        {
            if(a.width!=b.width||a.height!=b.height)return(0,float.PositiveInfinity);var x=a.GetPixels32();var y=b.GetPixels32();var changed=0;double sum=0;
            for(var i=0;i<x.Length;i++){var dr=Math.Abs(x[i].r-y[i].r);var dg=Math.Abs(x[i].g-y[i].g);var db=Math.Abs(x[i].b-y[i].b);if(Math.Max(dr,Math.Max(dg,db))<=2)continue;changed++;sum+=dr+dg+db;}
            return(changed,changed==0?0f:(float)(sum/(changed*3.0)));
        }
        private static int NonBlack(Texture2D image)=>image.GetPixels32().Count(x=>Mathf.Max(x.r,Mathf.Max(x.g,x.b))>12);
        private static int RedPixels(Texture2D image)=>image.GetPixels32().Count(p=>p.r>48&&p.r>p.g*1.35f&&p.r>p.b*1.2f);
        private static string Hash(string path)
        {
            using var algorithm=SHA256.Create();using var stream=File.OpenRead(path);
            return string.Concat(algorithm.ComputeHash(stream).Select(value=>value.ToString("x2")));
        }
        private static string ReportPath()=>SandroneM8Bootstrap.Artifact("M8ValidationReport.json");
    }
}
