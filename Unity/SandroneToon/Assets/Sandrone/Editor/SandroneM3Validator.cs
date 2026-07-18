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
    public static class SandroneM3Validator
    {
        [Serializable]
        private sealed class CheckResult
        {
            public string name = string.Empty;
            public bool passed;
            public string details = string.Empty;
        }

        [Serializable]
        private sealed class PriorReport
        {
            public string phase = string.Empty;
            public int checkCount;
            public List<string> failures = new();
        }

        [Serializable]
        private sealed class ValidationReport
        {
            public string phase = "M3";
            public string generatedUtc = string.Empty;
            public string unityVersion = string.Empty;
            public string renderPipeline = string.Empty;
            public string urpPackageVersion = string.Empty;
            public int checkCount;
            public int meshVertexCount;
            public int meshTriangleCount;
            public int subMeshCount;
            public int blendShapeCount;
            public int rendererBoneCount;
            public int materialSlotCount;
            public int characterShaderPassCount;
            public int characterShaderKeywordPragmaCount;
            public int characterShaderTextureSampleCount;
            public int shaderCompilerMessageCount;
            public float receiverBlockerMae;
            public float groundCasterMae;
            public float rawShadowOccludedRatio;
            public float rawShadowLitRatio;
            public float rawShadowIntermediateRatio;
            public float rawStyledShadowMae;
            public float formFinalLitMae;
            public float alphaClipShadowIoU;
            public float groundShadowCoverage;
            public float m2FrontForegroundLuminance;
            public float m3FrontForegroundLuminance;
            public float m3ToM2LuminanceRatio;
            public List<CheckResult> checks = new();
            public List<string> failures = new();
            public List<string> warnings = new();
            public List<string> intentionallyDeferred = new();
        }

        private sealed class ImageData
        {
            public int width;
            public int height;
            public Color32[] pixels = Array.Empty<Color32>();
            public bool Valid => width > 0 && height > 0 && pixels.Length == width * height;
        }

        [MenuItem("Sandrone/M3/Validate Real Shadows")]
        public static void ValidateAndWriteReport()
        {
            EditorSceneManager.OpenScene(SandroneM3Bootstrap.ScenePath);
            var report = new ValidationReport
            {
                generatedUtc = DateTime.UtcNow.ToString("O"),
                unityVersion = Application.unityVersion,
                renderPipeline = GraphicsSettings.currentRenderPipeline?.GetType().FullName ?? "None",
                urpPackageVersion = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(UniversalRenderPipelineAsset).Assembly)?.version ?? "unknown"
            };

            void Check(string name, bool passed, string details)
            {
                report.checks.Add(new CheckResult { name = name, passed = passed, details = details });
                if (!passed)
                {
                    report.failures.Add($"{name}: {details}");
                }
            }

            Check("EditorVersion", Application.unityVersion == "6000.5.3f1", Application.unityVersion);
            Check("ColorSpace", PlayerSettings.colorSpace == ColorSpace.Linear, PlayerSettings.colorSpace.ToString());
            Check("URPAssigned", GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset, report.renderPipeline);
            Check("URPPackageVersion", report.urpPackageVersion == "17.5.0", report.urpPackageVersion);
            ValidatePriorReport(Check);

            var controller = UnityEngine.Object.FindFirstObjectByType<SandroneM3Controller>();
            var renderer = controller != null ? controller.TargetRenderer as SkinnedMeshRenderer : null;
            Check("M3Controller", controller != null, controller?.name ?? "missing");
            Check("SkinnedRenderer", renderer != null, renderer?.name ?? "missing");
            if (controller != null && renderer != null)
            {
                ValidateRenderer(controller, renderer, report, Check);
                ValidateShadowScene(controller, renderer, Check);
            }
            ValidateProfilesAndPipeline(controller, Check);
            ValidateShaders(report, Check);
            ValidateAlphaProbeAsset(Check);
            ValidateCaptures(report, Check);

            report.warnings.Add($"Inherited M0 warning: Unity vertex buffer is {report.meshVertexCount}, PMX is 61973; stable invariants remain triangles/submeshes/blend-shapes.");
            report.warnings.Add("Performance baseline: 31 material slots can submit 31 UniversalForward plus 31 ShadowCaster draws before ground/debug objects. Confirm actual events and GPU time in Frame Debugger/Profiler during manual acceptance.");
            report.intentionallyDeferred.AddRange(new[]
            {
                "M4 ControlMap, MatCap and material-family specular",
                "M5 Face SDF",
                "M6 hair/eye stencil specialization",
                "M7 outline normals and outline pass",
                "M8 emission and Bloom",
                "M9 post-processing and performance stripping"
            });
            report.checkCount = report.checks.Count;

            var output = Path.GetFullPath(Path.Combine(Application.dataPath, "../TestArtifacts/M3/M3ValidationReport.json"));
            Directory.CreateDirectory(Path.GetDirectoryName(output) ?? Application.dataPath);
            File.WriteAllText(output, JsonUtility.ToJson(report, true));
            Debug.Log($"[Sandrone M3] Validation report: {output}");
            if (report.failures.Count > 0)
            {
                throw new BuildFailedException("Sandrone M3 validation failed:\n" + string.Join("\n", report.failures));
            }
        }

        private static void ValidatePriorReport(Action<string, bool, string> check)
        {
            var path = Path.GetFullPath(Path.Combine(Application.dataPath, "../TestArtifacts/M2/M2ValidationReport.json"));
            PriorReport prior = null;
            if (File.Exists(path))
            {
                prior = JsonUtility.FromJson<PriorReport>(File.ReadAllText(path));
            }
            check("M2RegressionGate", prior != null && prior.phase == "M2" && prior.checkCount >= 88 && prior.failures.Count == 0,
                prior != null ? $"phase={prior.phase}, checks={prior.checkCount}, failures={prior.failures.Count}" : path);
        }

        private static void ValidateRenderer(SandroneM3Controller controller, SkinnedMeshRenderer renderer,
            ValidationReport report, Action<string, bool, string> check)
        {
            var mesh = renderer.sharedMesh;
            check("MeshAssigned", mesh != null, mesh?.name ?? "missing");
            if (mesh != null)
            {
                report.meshVertexCount = mesh.vertexCount;
                report.meshTriangleCount = (int)(mesh.GetIndexCount(0) / 3);
                for (var i = 1; i < mesh.subMeshCount; i++)
                {
                    report.meshTriangleCount += (int)(mesh.GetIndexCount(i) / 3);
                }
                report.subMeshCount = mesh.subMeshCount;
                report.blendShapeCount = mesh.blendShapeCount;
                check("TriangleCount", report.meshTriangleCount == 69864, report.meshTriangleCount.ToString());
                check("SubMeshCount", report.subMeshCount == 31, report.subMeshCount.ToString());
                check("BlendShapeCount", report.blendShapeCount == 61, report.blendShapeCount.ToString());
            }
            report.rendererBoneCount = renderer.bones.Length;
            report.materialSlotCount = renderer.sharedMaterials.Length;
            check("RendererBones", report.rendererBoneCount == 692, report.rendererBoneCount.ToString());
            check("MaterialSlotCount", report.materialSlotCount == 31, report.materialSlotCount.ToString());
            check("NoNullMaterials", renderer.sharedMaterials.All(material => material != null),
                string.Join(",", renderer.sharedMaterials.Select(material => material?.name ?? "null")));
            check("CharacterHeight", renderer.bounds.size.y > 1.55f && renderer.bounds.size.y < 1.72f, renderer.bounds.size.y.ToString("F4"));
            check("RootTransform", controller.CharacterRoot != null && controller.CharacterRoot.position.sqrMagnitude < 1e-8f &&
                                   Quaternion.Angle(controller.CharacterRoot.rotation, Quaternion.identity) < 0.01f &&
                                   Vector3.Distance(controller.CharacterRoot.localScale, Vector3.one) < 1e-5f,
                controller.CharacterRoot != null ? $"pos={controller.CharacterRoot.position}, rot={controller.CharacterRoot.eulerAngles}" : "missing");

            var expectedShader = AssetDatabase.LoadAssetAtPath<Shader>(SandroneM3Bootstrap.ShaderPath);
            check("MaterialShader", renderer.sharedMaterials.All(material => material != null && material.shader == expectedShader),
                expectedShader?.name ?? "missing");
            var map = AssetDatabase.LoadAssetAtPath<SandroneMaterialMap>(SandroneM0Bootstrap.MaterialMapPath);
            var bindings = map != null && map.Entries.Count == 31 && map.Entries.All(entry =>
            {
                var m2 = AssetDatabase.LoadAssetAtPath<Material>(SandroneM2Bootstrap.MaterialPath(entry.sourceIndex, entry.materialAssetPath));
                var m3 = renderer.sharedMaterials[entry.sourceIndex];
                return m2 != null && m3 != null && m3.GetTexture("_BaseMap") == m2.GetTexture("_BaseMap") &&
                       m3.GetTexture("_RampMap") == m2.GetTexture("_RampMap") &&
                       Approximately(m3.GetFloat("_RampRow"), m2.GetFloat("_RampRow")) &&
                       Approximately(m3.GetFloat("_Threshold"), m2.GetFloat("_Threshold"));
            });
            check("M2BindingsPreserved", bindings, "BaseMap, RampMap, row and threshold must match all 31 M2 materials.");
            var surface = map != null && map.Entries.All(entry =>
            {
                var m2 = AssetDatabase.LoadAssetAtPath<Material>(SandroneM2Bootstrap.MaterialPath(entry.sourceIndex, entry.materialAssetPath));
                var m3 = renderer.sharedMaterials[entry.sourceIndex];
                return m2 != null && m3 != null && m2.renderQueue == m3.renderQueue &&
                       m2.GetTag("RenderType", false, "") == m3.GetTag("RenderType", false, "") &&
                       Approximately(m2.GetFloat("_SrcBlend"), m3.GetFloat("_SrcBlend")) &&
                       Approximately(m2.GetFloat("_DstBlend"), m3.GetFloat("_DstBlend")) &&
                       Approximately(m2.GetFloat("_ZWrite"), m3.GetFloat("_ZWrite")) &&
                       Approximately(m2.GetFloat("_Cull"), m3.GetFloat("_Cull"));
            });
            check("M2SurfaceStatePreserved", surface, "Queue, RenderType, Blend, ZWrite and Cull must match M2.");
            check("CharacterCastsShadows", renderer.shadowCastingMode == ShadowCastingMode.On, renderer.shadowCastingMode.ToString());
            check("CharacterReceivesShadows", renderer.receiveShadows, renderer.receiveShadows.ToString());
            check("OpaqueShadowCull", renderer.sharedMaterials.Where(material => material.GetFloat("_ZWrite") > 0.5f)
                    .All(material => Approximately(material.GetFloat("_ShadowCull"), (float)CullMode.Back)),
                "opaque/cutout ShadowCaster uses back-face culling; forward Cull remains inherited");
        }

        private static void ValidateShadowScene(SandroneM3Controller controller, SkinnedMeshRenderer renderer,
            Action<string, bool, string> check)
        {
            check("DirectionalLight", controller.MainLight != null && controller.MainLight.type == LightType.Directional,
                controller.MainLight?.type.ToString() ?? "missing");
            check("SingleDirectionalLight", UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None).Count(light => light.type == LightType.Directional) == 1,
                "exactly one");
            check("MainLightSoftShadows", controller.MainLight != null && controller.MainLight.shadows == LightShadows.Soft,
                controller.MainLight?.shadows.ToString() ?? "missing");
            check("MainLightStrength", controller.MainLight != null && Approximately(controller.MainLight.shadowStrength, 0.85f),
                controller.MainLight?.shadowStrength.ToString("F3") ?? "missing");
            check("RenderSettingsSun", RenderSettings.sun == controller.MainLight, RenderSettings.sun?.name ?? "missing");
            check("ShadowProfileBound", controller.ShadowProfile != null && controller.ShadowProfile.ContractVersion == "SandroneShadowProfile_v1_M3",
                controller.ShadowProfile?.ContractVersion ?? "missing");

            var ground = FindSceneObject<MeshRenderer>("M3_ShadowGround");
            check("GroundReceiver", ground != null && ground.receiveShadows && ground.shadowCastingMode == ShadowCastingMode.Off,
                ground != null ? $"receive={ground.receiveShadows}, cast={ground.shadowCastingMode}" : "missing");
            check("GroundReceiverShader", ground != null && ground.sharedMaterial != null &&
                                         ground.sharedMaterial.shader.name == "Sandrone/M3/ShadowReceiver",
                ground?.sharedMaterial?.shader?.name ?? "missing");
            var blocker = FindSceneObject<MeshRenderer>("M3_ValidationBlocker");
            check("ValidationBlocker", blocker != null && blocker.shadowCastingMode == ShadowCastingMode.ShadowsOnly && !blocker.gameObject.activeSelf,
                blocker != null ? $"cast={blocker.shadowCastingMode}, active={blocker.gameObject.activeSelf}" : "missing");
            var probe = FindSceneObject<MeshRenderer>("M3_AlphaClipProbe");
            check("AlphaClipProbe", probe != null && !probe.gameObject.activeSelf && probe.sharedMaterial != null &&
                                    probe.sharedMaterial.GetFloat("_AlphaClip") > 0.5f &&
                                    Approximately(probe.sharedMaterial.GetFloat("_Cutoff"), SandroneM3Bootstrap.AlphaCutoff),
                probe?.sharedMaterial?.name ?? "missing");
            var camera = FindSceneObject<Camera>("M3_CalibrationCamera");
            check("CalibrationCamera", camera != null && camera.orthographic && !camera.allowHDR &&
                                       camera.nearClipPlane >= 0.05f && camera.farClipPlane >= 10f,
                camera != null ? $"ortho={camera.orthographic}, HDR={camera.allowHDR}, clips={camera.nearClipPlane}/{camera.farClipPlane}" : "missing");
        }

        private static void ValidateProfilesAndPipeline(SandroneM3Controller controller, Action<string, bool, string> check)
        {
            var profile = AssetDatabase.LoadAssetAtPath<SandroneM3ShadowProfile>(SandroneM3Bootstrap.ShadowProfilePath);
            check("ShadowProfile", profile != null && profile.ContractVersion == "SandroneShadowProfile_v1_M3", profile?.ContractVersion ?? "missing");
            if (profile != null)
            {
                var runtimeRefresh = false;
                var originalStrength = profile.CastShadowStrength;
                var originalLow = profile.CastShadowLow;
                var originalHigh = profile.CastShadowHigh;
                try
                {
                    if (controller?.TargetRenderer != null)
                    {
                        controller.Apply(true);
                        var probeStrength = originalStrength > 0.5f ? originalStrength - 0.125f : originalStrength + 0.125f;
                        profile.EditorSet(probeStrength, originalLow, originalHigh);
                        controller.Apply();
                        var block = new MaterialPropertyBlock();
                        controller.TargetRenderer.GetPropertyBlock(block, 0);
                        runtimeRefresh = Approximately(block.GetFloat(Shader.PropertyToID("_CastShadowStrength")), probeStrength);
                    }
                }
                finally
                {
                    profile.EditorSet(originalStrength, originalLow, originalHigh);
                    controller?.Apply(true);
                }
                check("ShadowProfileRange", Approximately(profile.CastShadowStrength, SandroneM3Bootstrap.CastShadowStrength) &&
                                            Approximately(profile.CastShadowLow, SandroneM3Bootstrap.CastShadowLow) &&
                                            Approximately(profile.CastShadowHigh, SandroneM3Bootstrap.CastShadowHigh) &&
                                            profile.CastShadowLow < profile.CastShadowHigh && runtimeRefresh,
                    $"strength={profile.CastShadowStrength:F2}, low/high={profile.CastShadowLow:F2}/{profile.CastShadowHigh:F2}, runtimeRefresh={runtimeRefresh}");
            }
            var pc = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset");
            var mobile = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/Settings/Mobile_RPAsset.asset");
            check("PCShadowConfig", pc != null && pc.supportsMainLightShadows && pc.mainLightShadowmapResolution == 2048 &&
                                    pc.shadowCascadeCount == 4 && pc.supportsSoftShadows && pc.shadowDistance >= 20f,
                pc != null ? $"res={pc.mainLightShadowmapResolution}, cascades={pc.shadowCascadeCount}, soft={pc.supportsSoftShadows}, distance={pc.shadowDistance}" : "missing");
            check("PCShadowBias", pc != null && pc.shadowDepthBias > 0f && pc.shadowDepthBias <= 1f &&
                                  pc.shadowNormalBias > 0f && pc.shadowNormalBias <= 1.5f,
                pc != null ? $"depth={pc.shadowDepthBias}, normal={pc.shadowNormalBias}" : "missing");
            check("MobileShadowConfig", mobile != null && mobile.supportsMainLightShadows &&
                                        mobile.mainLightShadowmapResolution == 1024 && mobile.shadowCascadeCount == 1 &&
                                        !mobile.supportsSoftShadows,
                mobile != null ? $"res={mobile.mainLightShadowmapResolution}, cascades={mobile.shadowCascadeCount}, soft={mobile.supportsSoftShadows}" : "missing");
        }

        private static void ValidateShaders(ValidationReport report, Action<string, bool, string> check)
        {
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(SandroneM3Bootstrap.ShaderPath);
            check("CharacterShaderExists", shader != null && shader.isSupported, shader?.name ?? "missing");
            if (shader != null)
            {
                var messages = ShaderUtil.GetShaderMessages(shader);
                report.shaderCompilerMessageCount += messages.Length;
                check("CharacterShaderCompileMessages", messages.Length == 0,
                    messages.Length == 0 ? "none" : string.Join(" | ", messages.Select(message => message.message)));
                var source = File.ReadAllText(Path.GetFullPath(Path.Combine(Application.dataPath, "..", SandroneM3Bootstrap.ShaderPath)));
                report.characterShaderPassCount = Regex.Matches(source, @"\bPass\s*\{").Count;
                report.characterShaderKeywordPragmaCount = source.Split('\n').Count(line => line.Contains("#pragma multi_compile", StringComparison.Ordinal));
                report.characterShaderTextureSampleCount = Regex.Matches(source, @"SAMPLE_TEXTURE2D\(").Count;
                check("CharacterShaderPasses", report.characterShaderPassCount == 2 && source.Contains("UniversalForward", StringComparison.Ordinal) &&
                                               source.Contains("\"ShadowCaster\"", StringComparison.Ordinal),
                    $"passes={report.characterShaderPassCount}");
                check("CharacterShaderVariants", report.characterShaderKeywordPragmaCount == 4 &&
                                                source.Contains("_CLUSTER_LIGHT_LOOP", StringComparison.Ordinal) &&
                                                source.Contains("_MAIN_LIGHT_SHADOWS_CASCADE", StringComparison.Ordinal) &&
                                                source.Contains("_SHADOWS_SOFT_HIGH", StringComparison.Ordinal),
                    $"keyword pragmas={report.characterShaderKeywordPragmaCount}");
                check("CharacterTextureSampleBudget", report.characterShaderTextureSampleCount == 3,
                    $"samples={report.characterShaderTextureSampleCount}; forward Base+Ramp, caster Base alpha");
                check("ShadowReceiveAPI", source.Contains("defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)", StringComparison.Ordinal) &&
                                          source.Contains("shadowCoord = TransformWorldToShadowCoord(input.positionWS)", StringComparison.Ordinal) &&
                                          source.Contains("GetMainLight(shadowCoord)", StringComparison.Ordinal) &&
                                          !source.Contains("GetMainLight(input.shadowCoord)", StringComparison.Ordinal) &&
                                          source.Contains("mainLight.shadowAttenuation", StringComparison.Ordinal),
                    "URP 17.5 contract: vertex interpolation for non-cascade/screen variants, per-pixel world-to-shadow transform for cascades");
                ValidateCascadeContracts(check);
                check("NoDoubleShadowEdge", source.Contains("min(formBand, castShadowStyled)", StringComparison.Ordinal) &&
                                            !source.Contains("formBand * castShadowStyled", StringComparison.Ordinal), SandroneM3Bootstrap.ShaderPath);
                check("ShadowCasterBias", source.Contains("ApplyShadowBias", StringComparison.Ordinal) &&
                                          source.Contains("ApplyShadowClamping", StringComparison.Ordinal), SandroneM3Bootstrap.ShaderPath);
                check("ShadowCasterCullSeparated", source.Contains("Cull [_ShadowCull]", StringComparison.Ordinal) &&
                                                    source.Contains("Cull [_Cull]", StringComparison.Ordinal),
                    "double-sided forward rendering does not force double-sided shadow casting");
                check("AlphaClipSharedInputs", Regex.Matches(source, @"clip\(.*_Cutoff").Count >= 2 &&
                                               Regex.Matches(source, @"SAMPLE_TEXTURE2D\(_BaseMap").Count >= 2 &&
                                               source.Contains("_BaseColor.a * _LayerWeight", StringComparison.Ordinal), SandroneM3Bootstrap.ShaderPath);
                check("TransparentCasterPolicy", source.Contains("if (_ZWrite < 0.5h)", StringComparison.Ordinal) &&
                                                 source.Contains("clip(-1.0h)", StringComparison.Ordinal),
                    "Alpha-blended overlays are intentionally excluded from binary shadow maps.");
                check("RequiredDebugViews", source.Contains("castShadowRaw", StringComparison.Ordinal) &&
                                            source.Contains("castShadowStyled", StringComparison.Ordinal) &&
                                            source.Contains("ComputeCascadeIndex", StringComparison.Ordinal), SandroneM3Bootstrap.ShaderPath);
                check("NoM4PlusFeatures", !source.Contains("ControlMap", StringComparison.Ordinal) &&
                                          !source.Contains("MatCap", StringComparison.Ordinal) &&
                                          !source.Contains("FaceMap", StringComparison.Ordinal) &&
                                          !source.Contains("Outline", StringComparison.Ordinal) &&
                                          !source.Contains("Emission", StringComparison.Ordinal), "M4+ features remain deferred.");
            }

            var receiver = AssetDatabase.LoadAssetAtPath<Shader>(SandroneM3Bootstrap.ReceiverShaderPath);
            check("ReceiverShaderExists", receiver != null && receiver.isSupported, receiver?.name ?? "missing");
            if (receiver != null)
            {
                var messages = ShaderUtil.GetShaderMessages(receiver);
                report.shaderCompilerMessageCount += messages.Length;
                check("ReceiverShaderCompileMessages", messages.Length == 0,
                    messages.Length == 0 ? "none" : string.Join(" | ", messages.Select(message => message.message)));
                var source = File.ReadAllText(Path.GetFullPath(Path.Combine(Application.dataPath, "..", SandroneM3Bootstrap.ReceiverShaderPath)));
                check("ReceiverOnlyPass", Regex.Matches(source, @"\bPass\s*\{").Count == 1 &&
                                          !source.Contains("ShadowCaster", StringComparison.Ordinal), SandroneM3Bootstrap.ReceiverShaderPath);
                check("ReceiverShadowAPI",
                    source.Contains("TransformWorldToShadowCoord(input.positionWS)", StringComparison.Ordinal) &&
                    source.Contains("defined(_MAIN_LIGHT_SHADOWS_SCREEN)", StringComparison.Ordinal) &&
                    source.Contains("float4 shadowCoord = input.screenPos", StringComparison.Ordinal) &&
                    source.Contains("shadowAttenuation", StringComparison.Ordinal) &&
                    !source.Contains("output.shadowCoord = GetShadowCoord(positionInputs)", StringComparison.Ordinal),
                    "per-pixel cascade selection with explicit screen-space compatibility; vertex atlas-tile interpolation forbidden");
            }
            check("AllShaderCompileMessages", report.shaderCompilerMessageCount == 0, report.shaderCompilerMessageCount.ToString());
        }

        private static void ValidateCascadeContracts(Action<string, bool, string> check)
        {
            var paths = new[]
            {
                SandroneM3Bootstrap.ShaderPath,
                "Assets/Sandrone/Shaders/SandroneMaterialResponseM4.shader",
                "Assets/Sandrone/Shaders/SandroneFaceSDFM5.shader",
                "Assets/Sandrone/Shaders/SandroneHairEyeM6.shader",
                "Assets/Sandrone/Shaders/SandroneHairEyeEmissionM8.shader"
            };
            foreach (var path in paths)
            {
                var absolute = Path.GetFullPath(Path.Combine(Application.dataPath, "..", path));
                var source = File.Exists(absolute) ? File.ReadAllText(absolute) : string.Empty;
                var correct = source.Contains("defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)", StringComparison.Ordinal) &&
                              source.Contains("defined(MAIN_LIGHT_CALCULATE_SHADOWS)", StringComparison.Ordinal) &&
                              Regex.IsMatch(source, @"shadowCoord\s*=\s*TransformWorldToShadowCoord\(input\.positionWS\)") &&
                              Regex.IsMatch(source, @"GetMainLight\(shadowCoord\)") &&
                              !Regex.IsMatch(source, @"GetMainLight\(input\.shadowCoord\)");
                check("CascadeShadowCoord_" + Path.GetFileNameWithoutExtension(path), correct,
                    correct ? "URP conditional vertex/per-pixel contract" : path);
            }
        }

        private static void ValidateAlphaProbeAsset(Action<string, bool, string> check)
        {
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(SandroneM3Bootstrap.AlphaProbeTexturePath);
            var importer = AssetImporter.GetAtPath(SandroneM3Bootstrap.AlphaProbeTexturePath) as TextureImporter;
            check("AlphaProbeTexture", texture != null && texture.width == 128 && texture.height == 128,
                texture != null ? $"{texture.width}x{texture.height}" : "missing");
            check("AlphaProbeImport", importer != null && importer.sRGBTexture && !importer.mipmapEnabled &&
                                      importer.wrapMode == TextureWrapMode.Clamp && importer.filterMode == FilterMode.Point &&
                                      importer.textureCompression == TextureImporterCompression.Uncompressed,
                importer != null ? $"sRGB={importer.sRGBTexture}, mip={importer.mipmapEnabled}, wrap={importer.wrapMode}, filter={importer.filterMode}" : "missing");
        }

        private static void ValidateCaptures(ValidationReport report, Action<string, bool, string> check)
        {
            var root = Path.GetFullPath(Path.Combine(Application.dataPath, "../TestArtifacts/M3"));
            var paths = new[]
            {
                "ReferenceComparison/M3_FinalToon_Front.png", "ReferenceComparison/M3_FinalToon_Side.png",
                "Debug/M3_CastShadowRaw_Self.png", "Debug/M3_CastShadowStyled_Self.png",
                "Debug/M3_FormBand.png", "Debug/M3_FinalLitMask.png", "Debug/M3_RampSample.png",
                "Debug/M3_CascadeIndex.png", "Debug/M3_Silhouette.png",
                "Debug/M3_CastShadowRaw_Near.png", "Debug/M3_CastShadowRaw_Mid.png", "Debug/M3_CastShadowRaw_Far.png",
                "Debug/M3_CascadeIndex_Near.png", "Debug/M3_CascadeIndex_Mid.png", "Debug/M3_CascadeIndex_Far.png",
                "Debug/M3_Receiver_NoBlocker.png", "Debug/M3_Receiver_WithBlocker.png",
                "Debug/M3_Ground_NoCaster.png", "Debug/M3_Ground_WithCaster.png",
                "Debug/M3_AlphaClip_Visual.png", "Debug/M3_AlphaClip_NoCaster.png", "Debug/M3_AlphaClip_Shadow.png"
            };
            foreach (var relative in paths)
            {
                var image = ReadImage(Path.Combine(root, relative));
                check($"Capture_{Path.GetFileNameWithoutExtension(relative)}", image.Valid && image.pixels.Length > 1000,
                    image.Valid ? $"{image.width}x{image.height}" : relative);
            }

            var silhouette = ReadImage(Path.Combine(root, "Debug/M3_Silhouette.png"));
            var raw = ReadImage(Path.Combine(root, "Debug/M3_CastShadowRaw_Self.png"));
            var styled = ReadImage(Path.Combine(root, "Debug/M3_CastShadowStyled_Self.png"));
            var form = ReadImage(Path.Combine(root, "Debug/M3_FormBand.png"));
            var finalLit = ReadImage(Path.Combine(root, "Debug/M3_FinalLitMask.png"));
            var receiverNo = ReadImage(Path.Combine(root, "Debug/M3_Receiver_NoBlocker.png"));
            var receiverWith = ReadImage(Path.Combine(root, "Debug/M3_Receiver_WithBlocker.png"));
            var groundNo = ReadImage(Path.Combine(root, "Debug/M3_Ground_NoCaster.png"));
            var groundWith = ReadImage(Path.Combine(root, "Debug/M3_Ground_WithCaster.png"));
            report.receiverBlockerMae = MaskedMae(receiverNo, receiverWith, silhouette);
            report.groundCasterMae = MeanAbsoluteError(groundNo, groundWith);
            ShadowToneRatios(raw, silhouette, out report.rawShadowOccludedRatio,
                out report.rawShadowLitRatio, out report.rawShadowIntermediateRatio);
            report.rawStyledShadowMae = MaskedMae(raw, styled, silhouette);
            report.formFinalLitMae = MaskedMae(form, finalLit, silhouette);
            report.groundShadowCoverage = DifferenceCoverage(groundNo, groundWith, 8);
            check("CharacterReceivesExternalShadow", report.receiverBlockerMae > 3f, $"masked MAE={report.receiverBlockerMae:F3}");
            check("CharacterCastsGroundShadow", report.groundCasterMae > 0.4f && report.groundShadowCoverage > 0.001f,
                $"MAE={report.groundCasterMae:F3}, coverage={report.groundShadowCoverage:F6}");
            check("ShadowAttenuationDynamicRange", report.rawShadowOccludedRatio > 0.01f && report.rawShadowLitRatio > 0.10f,
                $"occluded={report.rawShadowOccludedRatio:F6}, lit={report.rawShadowLitRatio:F6}");
            check("SoftShadowIntermediatePixels", report.rawShadowIntermediateRatio > 0.00005f && report.rawShadowIntermediateRatio < 0.35f,
                $"transition-only ratio={report.rawShadowIntermediateRatio:F6}; excludes stable shadow plateau");
            check("StyledShadowObservable", report.rawStyledShadowMae > 0.05f, $"masked MAE={report.rawStyledShadowMae:F3}");
            check("FormAndCastCombined", report.formFinalLitMae > 0.05f, $"masked MAE={report.formFinalLitMae:F3}");

            var alphaVisual = ReadImage(Path.Combine(root, "Debug/M3_AlphaClip_Visual.png"));
            var alphaNo = ReadImage(Path.Combine(root, "Debug/M3_AlphaClip_NoCaster.png"));
            var alphaShadow = ReadImage(Path.Combine(root, "Debug/M3_AlphaClip_Shadow.png"));
            report.alphaClipShadowIoU = AlphaClipIoU(alphaVisual, alphaNo, alphaShadow);
            check("AlphaClipForwardShadowMatch", report.alphaClipShadowIoU > 0.80f,
                $"IoU={report.alphaClipShadowIoU:F4}; same BaseMap/Color/LayerWeight/Cutoff");

            var m2 = ReadImage(Path.GetFullPath(Path.Combine(Application.dataPath,
                "../TestArtifacts/M2/ReferenceComparison/M2_FinalToon_Front.png")));
            var m3 = ReadImage(Path.Combine(root, "ReferenceComparison/M3_FinalToon_Front.png"));
            report.m2FrontForegroundLuminance = ForegroundMeanLuminance(m2);
            report.m3FrontForegroundLuminance = ForegroundMeanLuminance(m3);
            report.m3ToM2LuminanceRatio = report.m2FrontForegroundLuminance > 1e-5f
                ? report.m3FrontForegroundLuminance / report.m2FrontForegroundLuminance : 0f;
            check("M2ColorContinuity", report.m3ToM2LuminanceRatio >= 0.70f && report.m3ToM2LuminanceRatio <= 1.05f,
                $"M2={report.m2FrontForegroundLuminance:F3}, M3={report.m3FrontForegroundLuminance:F3}, ratio={report.m3ToM2LuminanceRatio:F3}");
        }

        private static ImageData ReadImage(string path)
        {
            if (!File.Exists(path) || new FileInfo(path).Length <= 1024)
            {
                return new ImageData();
            }
            var texture = new Texture2D(2, 2, TextureFormat.RGB24, false, false);
            try
            {
                if (!texture.LoadImage(File.ReadAllBytes(path), false))
                {
                    return new ImageData();
                }
                return new ImageData { width = texture.width, height = texture.height, pixels = texture.GetPixels32() };
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }

        private static float MaskedMae(ImageData a, ImageData b, ImageData mask)
        {
            if (!SameSize(a, b) || !SameSize(a, mask)) return 0f;
            double sum = 0;
            var count = 0;
            for (var i = 0; i < a.pixels.Length; i++)
            {
                if (Luminance(mask.pixels[i]) < 128) continue;
                sum += Math.Abs(Luminance(a.pixels[i]) - Luminance(b.pixels[i]));
                count++;
            }
            return count > 0 ? (float)(sum / count) : 0f;
        }

        private static float MeanAbsoluteError(ImageData a, ImageData b)
        {
            if (!SameSize(a, b)) return 0f;
            double sum = 0;
            for (var i = 0; i < a.pixels.Length; i++)
            {
                sum += Math.Abs(Luminance(a.pixels[i]) - Luminance(b.pixels[i]));
            }
            return (float)(sum / a.pixels.Length);
        }

        private static void ShadowToneRatios(ImageData image, ImageData mask,
            out float occludedRatio, out float litRatio, out float transitionRatio)
        {
            occludedRatio = 0f;
            litRatio = 0f;
            transitionRatio = 0f;
            if (!SameSize(image, mask)) return;
            var occluded = 0;
            var lit = 0;
            var transition = 0;
            var count = 0;
            for (var i = 0; i < image.pixels.Length; i++)
            {
                if (Luminance(mask.pixels[i]) < 128) continue;
                var value = Luminance(image.pixels[i]);
                // The configured 0.85 shadow strength has a linear full-shadow
                // plateau of 0.15, encoded near 108 in an sRGB PNG. Values <=192
                // are therefore stable occlusion, not evidence of a soft edge.
                if (value <= 192) occluded++;
                else if (value >= 247) lit++;
                else transition++;
                count++;
            }
            if (count <= 0) return;
            occludedRatio = (float)occluded / count;
            litRatio = (float)lit / count;
            transitionRatio = (float)transition / count;
        }

        private static float DifferenceCoverage(ImageData a, ImageData b, int threshold)
        {
            if (!SameSize(a, b)) return 0f;
            var count = 0;
            for (var i = 0; i < a.pixels.Length; i++)
            {
                if (Math.Abs(Luminance(a.pixels[i]) - Luminance(b.pixels[i])) > threshold) count++;
            }
            return (float)count / a.pixels.Length;
        }

        private static float AlphaClipIoU(ImageData visual, ImageData noCaster, ImageData shadow)
        {
            if (!SameSize(visual, noCaster) || !SameSize(visual, shadow)) return 0f;
            var intersection = 0;
            var union = 0;
            for (var i = 0; i < visual.pixels.Length; i++)
            {
                var visible = Luminance(visual.pixels[i]) > 128;
                var cast = Luminance(noCaster.pixels[i]) - Luminance(shadow.pixels[i]) > 64;
                if (visible && cast) intersection++;
                if (visible || cast) union++;
            }
            return union > 0 ? (float)intersection / union : 0f;
        }

        private static float ForegroundMeanLuminance(ImageData image)
        {
            if (!image.Valid) return 0f;
            var cornerCount = Math.Min(32, image.width) * Math.Min(32, image.height);
            double corner = 0;
            for (var y = 0; y < Math.Min(32, image.height); y++)
            for (var x = 0; x < Math.Min(32, image.width); x++)
                corner += Luminance(image.pixels[y * image.width + x]);
            corner /= Math.Max(1, cornerCount);
            double sum = 0;
            var count = 0;
            foreach (var pixel in image.pixels)
            {
                var value = Luminance(pixel);
                if (Math.Abs(value - corner) <= 16) continue;
                sum += value / 255.0;
                count++;
            }
            return count > 0 ? (float)(sum / count) : 0f;
        }

        private static bool SameSize(ImageData a, ImageData b) => a.Valid && b.Valid && a.width == b.width && a.height == b.height;
        private static int Luminance(Color32 pixel) => Mathf.RoundToInt(pixel.r * 0.2126f + pixel.g * 0.7152f + pixel.b * 0.0722f);
        private static bool Approximately(float a, float b) => Mathf.Abs(a - b) < 1e-4f;

        private static T FindSceneObject<T>(string name) where T : Component
        {
            return Resources.FindObjectsOfTypeAll<T>().FirstOrDefault(component =>
                component.gameObject.scene.IsValid() && component.gameObject.name == name);
        }
    }
}
