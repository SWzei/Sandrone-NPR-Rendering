using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace SandroneToon.Editor
{
    public static class SandroneM5Validator
    {
        [Serializable] public sealed class Check { public string name; public bool passed; public string details; }
        [Serializable] public sealed class DebugEvidence { public int mode; public string name, path, sha256; public float meanLuminance, maeFromPrevious; }
        [Serializable] public sealed class Report
        {
            public string phase = "M5", generatedUtc, unityVersion, renderPipeline, urpPackageVersion, graphicsDevice, graphicsApi;
            public int checkCount, failureCount, warningCount, shaderCompilerMessageCount, shaderKeywordPragmaCount, shaderTextureSampleCount;
            public float faceTargetMae, faceNonTargetMae, leftRightMae, headRotationMae, syntheticMapMae, pcMobileMae, m4M5Mae;
            public List<Check> checks = new(); public List<DebugEvidence> debugEvidence = new(); public List<string> failures = new(); public List<string> warnings = new();
            public string[] intentionallyDeferred = { "M6 hair anisotropy and eye/hair stencil", "M7 outline normals/pass", "M8 emission/Bloom", "M9 post-processing and variant stripping" };
        }

        [MenuItem("Sandrone/M5/Validate")]
        public static void ValidateAndWriteReport()
        {
            var report = new Report { generatedUtc = DateTime.UtcNow.ToString("O"), unityVersion = Application.unityVersion,
                renderPipeline = GraphicsSettings.currentRenderPipeline?.GetType().FullName ?? "null", urpPackageVersion = PackageVersion(),
                graphicsDevice = SystemInfo.graphicsDeviceName, graphicsApi = SystemInfo.graphicsDeviceType.ToString() };
            void Add(string name, bool passed, string details) { report.checks.Add(new Check { name = name, passed = passed, details = details }); if (!passed) report.failures.Add($"{name}: {details}"); }

            Add("RealGraphicsDevice", SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null, $"{report.graphicsApi}; {report.graphicsDevice}");
            Add("EditorVersion", Application.unityVersion == "6000.5.3f1", Application.unityVersion);
            Add("ColorSpace", PlayerSettings.colorSpace == ColorSpace.Linear, PlayerSettings.colorSpace.ToString());
            Add("URPAssigned", GraphicsSettings.currentRenderPipeline != null, report.renderPipeline);
            Add("URPPackageVersion", report.urpPackageVersion == "17.5.0", report.urpPackageVersion);
            var m4Report = Path.Combine(ProjectRoot(), "TestArtifacts/M4/M4ValidationReport.json"); var m4Text = File.Exists(m4Report) ? File.ReadAllText(m4Report) : "";
            Add("M4RegressionGate", m4Text.Contains("\"phase\": \"M4\"") && m4Text.Contains("\"failures\": []"), m4Report);

            var profile = AssetDatabase.LoadAssetAtPath<SandroneM5FaceProfile>(SandroneM5Bootstrap.ProfilePath);
            Add("FaceProfile", profile != null && profile.ContractVersion == "SandroneFaceProfile_v1_M5", profile?.ContractVersion ?? "missing");
            Add("FaceSlots", profile != null && profile.FaceMaterialIndices.SequenceEqual(new[] { 0, 1 }), profile == null ? "missing" : string.Join(",", profile.FaceMaterialIndices));
            Add("FaceParameterRange", profile != null && profile.Softness >= 0.01f && profile.Softness <= 0.03f && profile.DerivativeAA > 0f && profile.MirrorBlendWidth >= 0.08f && profile.MirrorBlendWidth <= 0.12f, profile == null ? "missing" : $"softness={profile.Softness}, AA={profile.DerivativeAA}, mirrorBlend={profile.MirrorBlendWidth}");
            ValidateFaceMap(Add, profile);

            var shader = AssetDatabase.LoadAssetAtPath<Shader>(SandroneM5Bootstrap.ShaderPath);
            Add("ShaderExists", shader != null, shader?.name ?? "missing"); Add("ShaderSupported", shader != null && shader.isSupported, "Sandrone/M5/FaceSDF");
            var messages = shader == null ? Array.Empty<ShaderMessage>() : ShaderUtil.GetShaderMessages(shader); report.shaderCompilerMessageCount = messages.Length;
            Add("ShaderCompileMessages", messages.Length == 0, string.Join(" | ", messages.Select(m => m.message)));
            var source = File.Exists(Absolute(SandroneM5Bootstrap.ShaderPath)) ? File.ReadAllText(Absolute(SandroneM5Bootstrap.ShaderPath)) : "";
            var m4Source = File.Exists(Absolute(SandroneM4Bootstrap.ShaderPath)) ? File.ReadAllText(Absolute(SandroneM4Bootstrap.ShaderPath)) : "";
            var m4Blocks = UnityPerMaterialBlocks(m4Source); var m4Fields = m4Blocks.FirstOrDefault() ?? new List<string>();
            var m5Fields = UnityPerMaterialFields(source);
            Add("M4InternalCBufferConsistency", m4Blocks.Count >= 2 && m4Blocks.All(x => x.SequenceEqual(m4Fields)),
                $"M4 UnityPerMaterial blocks={m4Blocks.Count}; Forward and ShadowCaster must remain identical");
            Add("M4CBufferPrefixCompatibility", m4Fields.Count > 0 && m5Fields.Take(m4Fields.Count).SequenceEqual(m4Fields), $"M4 fields={m4Fields.Count}, M5 fields={m5Fields.Count}; exact typed prefix required by UsePass");
            report.shaderKeywordPragmaCount = Regex.Matches(source, @"#pragma\s+multi_compile").Count;
            report.shaderTextureSampleCount = Regex.Matches(source, @"SAMPLE_TEXTURE2D\(").Count;
            Add("ForwardAndShadowCasterContract", source.Contains("Name \"M5FaceSDF\"") && source.Contains("UsePass \"Sandrone/M4/MaterialResponse/ShadowCaster\""), "M5 Forward + audited M4 ShadowCaster reuse");
            Add("FaceHorizontalProjection", source.Contains("L-headU*dot(L,headU)") && source.Contains("horizontalLength"), "light projected onto head horizontal plane with vertical fallback");
            Add("FaceThresholdContract", source.Contains("(1.0h-faceForward)*0.5h") && source.Contains("sdfRight") && source.Contains("sdfLeft") && source.Contains("smoothstep(-_FaceMirrorBlendWidth"), "q=(1-f)/2 and continuous dual-orientation mirror blend");
            Add("FaceDerivativeAA", source.Contains("fwidth(faceSdf)*_FaceAA") && source.Contains("_FaceSoftness"), "minimum softness + derivative AA");
            Add("CastShadowPreserved", source.Contains("min(formMask,castStyled)"), "Face SDF replaces form mask only; real cast shadow remains limiting mask");
            Add("M4MaterialResponsePreserved", source.Contains("_ResponseType") && source.Contains("_MatCapIntensity") && source.Contains("_M4FeatureWeight"), "M4 response contract retained");
            Add("NoGlobalDebugKeywords", !source.Contains("M5_DEBUG_") && !source.Contains("FACE_SDF_OFF"), "debug/toggle are MPB uniforms");
            Add("ShaderVariantBudget", report.shaderKeywordPragmaCount == 3, $"Forward pragmas={report.shaderKeywordPragmaCount}; ShadowCaster variant inherited via UsePass");
            Add("TextureSampleBudget", report.shaderTextureSampleCount == 6, $"Forward source samples={report.shaderTextureSampleCount}; dual FaceMap samples are face/debug only");
            var auditStencil = Regex.Match(source, @"Stencil\s*\{\s*Ref\s*\[_M5AuditSlotId\]\s*ReadMask\s*0\s*WriteMask\s*0\s*Comp\s*Always\s*Pass\s*Keep\s*\}", RegexOptions.IgnoreCase);
            Add("AuditStencilHasNoEffect", auditStencil.Success, "diagnostic Ref only; ReadMask=0, WriteMask=0, Always/Keep");
            var featureSource = auditStencil.Success ? source.Remove(auditStencil.Index, auditStencil.Length) : source;
            featureSource = Regex.Replace(featureSource, @"//.*?$|/\*.*?\*/", "", RegexOptions.Multiline | RegexOptions.Singleline);
            Add("NoLaterPhaseFeatures", !Regex.IsMatch(featureSource, "Stencil|Outline|Emission|Anisotrop", RegexOptions.IgnoreCase), "M6+ features absent after excluding the exact no-op audit identity block");

            if (File.Exists(Absolute(SandroneM5Bootstrap.ScenePath)))
            {
                EditorSceneManager.OpenScene(SandroneM5Bootstrap.ScenePath, OpenSceneMode.Single);
                var controller = UnityEngine.Object.FindFirstObjectByType<SandroneM5Controller>(); var renderer = controller?.TargetRenderer as SkinnedMeshRenderer;
                var camera = UnityEngine.Object.FindFirstObjectByType<Camera>();
                Add("M5Controller", controller != null, controller?.name ?? "missing"); Add("HeadBone", controller?.Head != null && controller.Head.name == "頭", controller?.Head?.name ?? "missing");
                Add("SkinnedRenderer", renderer != null, renderer?.name ?? "missing"); Add("MaterialSlotCount", renderer != null && renderer.sharedMaterials.Length == 31, renderer == null ? "missing" : renderer.sharedMaterials.Length.ToString());
                Add("CalibrationCamera", camera != null && camera.orthographic && !camera.allowHDR, "orthographic, HDR off");
                Add("SingleDirectionalLight", UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None).Count(l => l.type == LightType.Directional) == 1, "exactly one");
                Add("FormalReceiverOnly", GameObject.Find("M5_ShadowGround") != null && UnityEngine.Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None).Count(x => x.gameObject.name.Contains("Probe") || x.gameObject.name.Contains("Blocker")) == 0, "one formal ground, no validation planes/blockers");
                // Opening a scene can invalidate previously loaded editor asset wrappers.
                // Reload the two asset dependencies and make this branch an explicit gate so
                // material checks can never disappear silently from a passing report.
                var sceneProfile = AssetDatabase.LoadAssetAtPath<SandroneM5FaceProfile>(SandroneM5Bootstrap.ProfilePath);
                var sceneShader = AssetDatabase.LoadAssetAtPath<Shader>(SandroneM5Bootstrap.ShaderPath);
                Add("SceneValidationDependencies", sceneProfile != null && sceneShader != null, "FaceProfile and M5 Shader reloaded after scene open");
                if (controller != null && renderer != null && sceneProfile != null && sceneShader != null)
                {
                    ValidateMaterials(Add, renderer, sceneProfile, sceneShader); ValidateSurfaceState(Add, renderer);
                    controller.DebugMode = SandroneM5DebugMode.FaceSDF; controller.FaceSdfEnabled = false; controller.Apply(true);
                    var block = new MaterialPropertyBlock(); renderer.GetPropertyBlock(block, 0);
                    Add("ControllerImmediateUpdate", Mathf.Approximately(block.GetFloat(Shader.PropertyToID("_M5DebugMode")), 12f) && Mathf.Approximately(block.GetFloat(Shader.PropertyToID("_FaceSDFWeight")), 0f), "slot 0 MPB reflects debug/toggle immediately");
                    controller.DebugMode = SandroneM5DebugMode.FinalToon; controller.FaceSdfEnabled = true; controller.Apply(true); renderer.GetPropertyBlock(block, 0);
                    Add("DebugStateRestored", Mathf.Approximately(block.GetFloat(Shader.PropertyToID("_M5DebugMode")), 0f) && Mathf.Approximately(block.GetFloat(Shader.PropertyToID("_FaceSDFWeight")), 1f), "Final mode restored without shared-material mutation");
                    Add("NoGlobalM5Keywords", Shader.enabledGlobalKeywords.All(k => !k.name.StartsWith("M5_", StringComparison.Ordinal)), "global keyword set clean");
                    Add("RuntimePasses", renderer.sharedMaterials.Select((m, i) => i <= 1
                        ? m.FindPass("M5FaceSDF") >= 0 && m.FindPass("ShadowCaster") >= 0
                        : m.FindPass("M4MaterialResponse") >= 0 && m.FindPass("ShadowCaster") >= 0).All(x => x),
                        "face slots expose M5 Forward; 29 non-face slots retain M4 Forward; all expected opaque slots retain ShadowCaster");
                }
                else Add("SceneMaterialValidationExecuted", false, "controller, renderer, profile or shader missing after scene open");
            }
            else Add("M5Scene", false, SandroneM5Bootstrap.ScenePath);

            ValidateCaptures(Add, report);
            var specialPath = Path.Combine(ProjectRoot(), "TestArtifacts/M5SpecialAudit/M5SpecialAuditReport.json");
            if (File.Exists(specialPath))
            {
                var special = JsonUtility.FromJson<SandroneM5SpecialAudit.Report>(File.ReadAllText(specialPath));
                Add("SpecialAuditReport", special != null && special.failures.Count == 0, specialPath);
                Add("NonFaceRegressionGate", special != null && special.nonFaceMae < 0.5f && special.maxNonFaceSlotMae < 0.5f && special.noNewBlackRegions,
                    special == null ? "unreadable" : $"non-face={special.nonFaceMae:F3}, maxSlot={special.maxNonFaceSlotMae:F3}, blackPixels={special.newNearBlackPixels}, component={special.largestNewNearBlackComponent}");
                Add("LightSweepGate", special != null && special.lightTransformSynchronized && special.sweepContinuous && special.mirrorCrossingContinuous, "Transform-driven 20-degree sweep and sign crossing");
                Add("DebugSemanticGate", special != null && special.failedDebugCount == 0, special == null ? "unreadable" : $"failed={special.failedDebugCount}");
            }
            else Add("SpecialAuditReport", false, specialPath);
            report.warnings.Add("Sandrone_Face_SDF.png is a deterministic project authoring seed aligned to T_Face UV, not extracted game data and not a true 3D signed distance field; replace after Blender light-angle authoring.");
            report.warnings.Add("M5 uses dual FaceMap samples only in the local face variant on slots 0/1; material count remains 31 and per-slot MPBs retain the existing SRP Batcher caveat.");
            report.warnings.Add("Visible-Editor Game View and Frame Debugger evidence is produced by SandroneM5GameViewAudit; SRP Batcher status, GPU time and physical mobile-device profiling remain manual acceptance items.");
            report.checkCount = report.checks.Count; report.failureCount = report.failures.Count; report.warningCount = report.warnings.Count;
            Directory.CreateDirectory(Artifact("")); File.WriteAllText(Artifact("M5ValidationReport.json"), JsonUtility.ToJson(report, true));
            if (report.failures.Count > 0) throw new InvalidOperationException($"M5 validation failed ({report.failures.Count}/{report.checkCount}): {string.Join("; ", report.failures)}");
            Debug.Log($"[Sandrone M5] Validation passed: {report.checkCount}/{report.checkCount}, warnings={report.warningCount}.");
        }

        private static void ValidateFaceMap(Action<string, bool, string> add, SandroneM5FaceProfile profile)
        {
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(SandroneM5Bootstrap.FaceMapPath); var importer = AssetImporter.GetAtPath(SandroneM5Bootstrap.FaceMapPath) as TextureImporter;
            add("FaceMapExists", texture != null && profile?.FaceMap == texture, SandroneM5Bootstrap.FaceMapPath);
            add("FaceMapDimensions", texture != null && texture.width == 2048 && texture.height == 2048, texture == null ? "missing" : $"{texture.width}x{texture.height}");
            add("FaceMapImport", importer != null && !importer.sRGBTexture && !importer.mipmapEnabled && importer.wrapMode == TextureWrapMode.Clamp && importer.textureCompression == TextureImporterCompression.Uncompressed,
                $"sRGB={importer?.sRGBTexture}, mip={importer?.mipmapEnabled}, wrap={importer?.wrapMode}, compression={importer?.textureCompression}");
            if (File.Exists(Absolute(SandroneM5Bootstrap.FaceMapPath)))
            {
                var image = Decode(Absolute(SandroneM5Bootstrap.FaceMapPath)); try
                {
                    var pixels = image.GetPixels32(); var min = pixels.Min(p => p.r); var max = pixels.Max(p => p.r);
                    add("FaceMapRange", min < 32 && max > 224, $"R range={min}..{max}");
                    var monotonic = true; var y = image.height / 2; for (var x = 1; x < image.width; x++) monotonic &= pixels[y * image.width + x].r + 2 >= pixels[y * image.width + x - 1].r;
                    add("FaceMapHorizontalMonotonicity", monotonic, "center scanline is non-decreasing within 2/255 quantization tolerance");
                }
                finally { UnityEngine.Object.DestroyImmediate(image); }
            }
        }

        private static void ValidateMaterials(Action<string, bool, string> add, SkinnedMeshRenderer renderer, SandroneM5FaceProfile profile, Shader shader)
        {
            var map = AssetDatabase.LoadAssetAtPath<SandroneMaterialMap>(SandroneM0Bootstrap.MaterialMapPath); var bindings = true; var faceOnly = true; var m4 = true; var auditIds = true; var exactNonFaceReuse = true; var skirtCull = true;
            for (var i = 0; i < 31; i++)
            {
                var material = renderer.sharedMaterials[i]; var entry = map.Entries.First(x => x.sourceIndex == i);
                var previous = AssetDatabase.LoadAssetAtPath<Material>(SandroneM4Bootstrap.MaterialPath(i, entry.materialAssetPath));
                var isFace = profile.FaceMaterialIndices.Contains(i);
                bindings &= material != null && material.GetTexture("_BaseMap") != null && material.GetTexture("_RampMap") != null && material.GetTexture("_ControlMap") != null && material.GetTexture("_MatCapMap") != null;
                bindings &= isFace ? material.shader == shader && material.GetTexture("_FaceMap") == profile.FaceMap : material == previous;
                faceOnly &= isFace ? material.HasProperty("_UseFaceSDF") && Mathf.RoundToInt(material.GetFloat("_UseFaceSDF")) == 1 : !material.HasProperty("_UseFaceSDF");
                auditIds &= !isFace || (material.HasProperty("_M5AuditSlotId") && Mathf.RoundToInt(material.GetFloat("_M5AuditSlotId")) == i);
                exactNonFaceReuse &= isFace || material == previous;
                m4 &= previous != null && material.GetTexture("_ControlMap") == previous.GetTexture("_ControlMap") && Mathf.Approximately(material.GetFloat("_ResponseType"), previous.GetFloat("_ResponseType")) && Mathf.Approximately(material.GetFloat("_FeatureGroup"), previous.GetFloat("_FeatureGroup"));
                skirtCull &= i != 21 || Mathf.RoundToInt(material.GetFloat("_Cull")) == (int)CullMode.Back;
            }
            add("MaterialTextureBindings", bindings, "face has exact Base/Ramp/Control/MatCap/Face bindings; non-face uses exact M4 assets"); add("FaceEnableSlotsOnly", faceOnly, "only slots 0/1 expose and use Face SDF"); add("AuditSlotIds", auditIds, "face diagnostic identities are exactly 0/1"); add("ExactNonFaceM4Reuse", exactNonFaceReuse, "slots 2..30 reference the original M4 material assets"); add("M4ResponseBindingsPreserved", m4, "ControlMap/ResponseType/FeatureGroup unchanged from M4"); add("SkirtBackfaceCull", skirtCull, "slot 21 dark back-facing layer is Cull Back in the exact reused M4 asset");
        }

        private static void ValidateSurfaceState(Action<string, bool, string> add, SkinnedMeshRenderer renderer)
        {
            var ok = true; var shadow = true;
            var map = AssetDatabase.LoadAssetAtPath<SandroneMaterialMap>(SandroneM0Bootstrap.MaterialMapPath);
            for (var i = 0; i < 31; i++)
            {
                var entry = map.Entries.First(x => x.sourceIndex == i); var previous = AssetDatabase.LoadAssetAtPath<Material>(SandroneM4Bootstrap.MaterialPath(i, entry.materialAssetPath)); var current = renderer.sharedMaterials[i];
                ok &= previous != null && current.renderQueue == previous.renderQueue && Mathf.Approximately(current.GetFloat("_SrcBlend"), previous.GetFloat("_SrcBlend")) && Mathf.Approximately(current.GetFloat("_DstBlend"), previous.GetFloat("_DstBlend")) && Mathf.Approximately(current.GetFloat("_ZWrite"), previous.GetFloat("_ZWrite")) && Mathf.Approximately(current.GetFloat("_AlphaClip"), previous.GetFloat("_AlphaClip")) && Mathf.Approximately(current.GetFloat("_Cull"), previous.GetFloat("_Cull"));
                shadow &= current.GetShaderPassEnabled("ShadowCaster") == previous.GetShaderPassEnabled("ShadowCaster") && Mathf.Approximately(current.GetFloat("_ShadowCull"), previous.GetFloat("_ShadowCull"));
            }
            add("SurfaceStatePreserved", ok, "queue/blend/ZWrite/AlphaClip/Cull match M4"); add("ShadowStatePreserved", shadow, "ShadowCaster enable and ShadowCull match M4");
        }

        private static void ValidateCaptures(Action<string, bool, string> add, Report report)
        {
            var files = new List<string> { "ReferenceComparison/M5_FinalToon_Front.png", "ReferenceComparison/M5_Face_ThreeQuarter.png", "AB/M5_FaceSDF_On.png", "AB/M5_FaceSDF_Off.png", "Masks/M5_FaceSlots01_Mask.png", "Masks/M5_FaceSlots01_FullMask.png", "Debug/M5_FaceLitMask_RightLight.png", "Debug/M5_FaceLitMask_LeftLight.png", "Debug/M5_HeadAxes_BeforeHeadRotate.png", "Debug/M5_HeadAxes_AfterHeadRotate.png", "Debug/M5_FaceMapSyntheticLow.png", "Debug/M5_FaceMapSyntheticHigh.png", "Pipeline/M5_PC_ForwardPlus.png", "Pipeline/M5_Mobile_Forward.png" };
            files.AddRange(Enum.GetNames(typeof(SandroneM5DebugMode)).Select(x => $"Debug/M5_{x}.png"));
            foreach (var file in files) add("Capture_" + Path.GetFileNameWithoutExtension(file), File.Exists(Artifact(file)), file);
            if (!files.All(x => File.Exists(Artifact(x)))) return;
            Image previous = null; var hashes = new HashSet<string>();
            foreach (SandroneM5DebugMode mode in Enum.GetValues(typeof(SandroneM5DebugMode)))
            {
                var path = Artifact($"Debug/M5_{mode}.png"); var image = Read(path); var evidence = new DebugEvidence { mode = (int)mode, name = mode.ToString(), path = path, sha256 = Hash(path), meanLuminance = MeanLuminance(image) };
                if (previous != null && previous.width == image.width && previous.height == image.height) evidence.maeFromPrevious = Mae(previous.pixels, image.pixels);
                report.debugEvidence.Add(evidence); hashes.Add(evidence.sha256); previous = image;
            }
            add("DebugHashesUnique", hashes.Count == Enum.GetValues(typeof(SandroneM5DebugMode)).Length, $"unique={hashes.Count}/{Enum.GetValues(typeof(SandroneM5DebugMode)).Length}");
            MaskedMae(Artifact("AB/M5_FaceSDF_On.png"), Artifact("AB/M5_FaceSDF_Off.png"), Artifact("Masks/M5_FaceSlots01_Mask.png"), out report.faceTargetMae, out report.faceNonTargetMae);
            add("FaceFinalContribution", report.faceTargetMae > 1f && report.faceNonTargetMae < 0.5f, $"target MAE={report.faceTargetMae:F3}, non-target MAE={report.faceNonTargetMae:F3}");
            report.leftRightMae = PairMae("Debug/M5_FaceLitMask_RightLight.png", "Debug/M5_FaceLitMask_LeftLight.png"); add("LightSideResponse", report.leftRightMae > 1f, $"MAE={report.leftRightMae:F3}");
            MaskedMae(Artifact("Debug/M5_HeadAxes_BeforeHeadRotate.png"), Artifact("Debug/M5_HeadAxes_AfterHeadRotate.png"), Artifact("Masks/M5_FaceSlots01_Mask.png"), out report.headRotationMae, out _);
            add("HeadRotationResponse", report.headRotationMae > 1f, $"face-slot masked MAE={report.headRotationMae:F3}");
            report.syntheticMapMae = PairMae("Debug/M5_FaceMapSyntheticLow.png", "Debug/M5_FaceMapSyntheticHigh.png"); add("FaceMapChannelResponse", report.syntheticMapMae > 1f, $"R=.2/.8 MAE={report.syntheticMapMae:F3}");
            report.pcMobileMae = PairMae("Pipeline/M5_PC_ForwardPlus.png", "Pipeline/M5_Mobile_Forward.png");
            var pc = Read(Artifact("Pipeline/M5_PC_ForwardPlus.png")); var mobile = Read(Artifact("Pipeline/M5_Mobile_Forward.png"));
            add("PCForwardPlusCapture", ValidCharacterImage(pc) && NoFailureColors(pc), $"{pc.width}x{pc.height}"); add("MobileForwardCapture", ValidCharacterImage(mobile) && NoFailureColors(mobile), $"{mobile.width}x{mobile.height}");
            add("PCMobileReasonableDifference", report.pcMobileMae < 25f, $"8-bit MAE={report.pcMobileMae:F3}");
            var m4 = Path.Combine(ProjectRoot(), "TestArtifacts/M4/ReferenceComparison/M4_FinalToon_Front.png");
            if (File.Exists(m4))
            {
                MaskedMae(m4, Artifact("ReferenceComparison/M5_FinalToon_Front.png"), Artifact("Masks/M5_FaceSlots01_FullMask.png"), out report.m4M5Mae, out var outsideMae);
                add("M5ChangesImage", report.m4M5Mae > 0.05f && outsideMae < 0.5f, $"face-slot MAE={report.m4M5Mae:F3}, outside MAE={outsideMae:F3}");
            }
            WriteDiff(Artifact("AB/M5_FaceSDF_On.png"), Artifact("AB/M5_FaceSDF_Off.png"), Artifact("AB/M5_FaceSDF_Diff.png"));
        }

        private sealed class Image { public int width, height; public Color32[] pixels = Array.Empty<Color32>(); }
        private static List<string> UnityPerMaterialFields(string source)
        {
            return UnityPerMaterialBlocks(source).FirstOrDefault() ?? new List<string>();
        }
        private static List<List<string>> UnityPerMaterialBlocks(string source)
        {
            var result = new List<List<string>>();
            foreach (Match block in Regex.Matches(source, @"CBUFFER_START\(UnityPerMaterial\)(.*?)CBUFFER_END", RegexOptions.Singleline))
            {
                var fields = new List<string>();
                foreach (Match field in Regex.Matches(block.Groups[1].Value, @"(?m)^\s*(float|float2|float3|float4|half|half2|half3|half4|int|uint)\s+(_[A-Za-z0-9_]+)\s*;"))
                    fields.Add(field.Groups[1].Value + " " + field.Groups[2].Value);
                result.Add(fields);
            }
            return result;
        }
        private static Texture2D Decode(string path) { var t = new Texture2D(2, 2); if (!t.LoadImage(File.ReadAllBytes(path))) throw new InvalidOperationException(path); return t; }
        private static Image Read(string path) { var t = Decode(path); try { return new Image { width = t.width, height = t.height, pixels = t.GetPixels32() }; } finally { UnityEngine.Object.DestroyImmediate(t); } }
        private static float PairMae(string a, string b) { var x = Read(Artifact(a)); var y = Read(Artifact(b)); return x.width == y.width && x.height == y.height ? Mae(x.pixels, y.pixels) : 0f; }
        private static float Mae(Color32[] a, Color32[] b) { if (a.Length == 0 || a.Length != b.Length) return float.PositiveInfinity; double sum = 0; for (var i = 0; i < a.Length; i++) sum += (Math.Abs(a[i].r - b[i].r) + Math.Abs(a[i].g - b[i].g) + Math.Abs(a[i].b - b[i].b)) / 3.0; return (float)(sum / a.Length); }
        private static void MaskedMae(string a, string b, string mask, out float target, out float nonTarget)
        {
            var x = Read(a); var y = Read(b); var m = Read(mask); double ts = 0, ns = 0; var tc = 0; var nc = 0;
            for (var i = 0; i < x.pixels.Length; i++) { var d = (Math.Abs(x.pixels[i].r - y.pixels[i].r) + Math.Abs(x.pixels[i].g - y.pixels[i].g) + Math.Abs(x.pixels[i].b - y.pixels[i].b)) / 3.0; if (m.pixels[i].r + m.pixels[i].g + m.pixels[i].b > 30) { ts += d; tc++; } else { ns += d; nc++; } }
            target = tc == 0 ? 0 : (float)(ts / tc); nonTarget = nc == 0 ? 0 : (float)(ns / nc);
        }
        private static void WriteDiff(string a, string b, string output)
        {
            var x = Read(a); var y = Read(b); var t = new Texture2D(x.width, x.height, TextureFormat.RGB24, false, false); var p = new Color32[x.pixels.Length];
            try { for (var i = 0; i < p.Length; i++) p[i] = new Color32((byte)Math.Min(255, Math.Abs(x.pixels[i].r - y.pixels[i].r) * 4), (byte)Math.Min(255, Math.Abs(x.pixels[i].g - y.pixels[i].g) * 4), (byte)Math.Min(255, Math.Abs(x.pixels[i].b - y.pixels[i].b) * 4), 255); t.SetPixels32(p); t.Apply(); File.WriteAllBytes(output, t.EncodeToPNG()); }
            finally { UnityEngine.Object.DestroyImmediate(t); }
        }
        private static float MeanLuminance(Image image) { if (image.pixels.Length == 0) return 0; double s = 0; foreach (var p in image.pixels) s += .2126 * p.r + .7152 * p.g + .0722 * p.b; return (float)(s / image.pixels.Length); }
        private static bool ValidCharacterImage(Image image) => image.pixels.Length > 1000 && image.pixels.Count(p => Math.Abs(p.r - 39) + Math.Abs(p.g - 38) + Math.Abs(p.b - 38) > 15) > image.pixels.Length * .05;
        private static bool NoFailureColors(Image image) => image.pixels.Length > 0 && image.pixels.Count(p => p.r > 200 && p.b > 200 && p.g < 80) < image.pixels.Length * .0001 && MeanLuminance(image) > 15;
        private static string Hash(string path) { using var sha = SHA256.Create(); return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", ""); }
        private static string PackageVersion() { var p = Path.Combine(ProjectRoot(), "Packages/packages-lock.json"); var m = Regex.Match(File.ReadAllText(p), "com.unity.render-pipelines.universal[^}]*version\\\"\\s*:\\s*\\\"([^\\\"]+)"); return m.Success ? m.Groups[1].Value : "unknown"; }
        private static string Artifact(string relative) => Path.Combine(ProjectRoot(), "TestArtifacts/M5", relative);
        private static string ProjectRoot() => Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        private static string Absolute(string assetPath) => Path.GetFullPath(Path.Combine(Application.dataPath, "..", assetPath));
    }
}
