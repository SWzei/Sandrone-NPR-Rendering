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

namespace SandroneToon.Editor
{
    public static class SandroneM2Validator
    {
        [Serializable]
        private sealed class CheckResult
        {
            public string name;
            public bool passed;
            public string details;
        }

        [Serializable]
        private sealed class PriorReport
        {
            public string phase;
            public string[] failures;
        }

        [Serializable]
        private sealed class ValidationReport
        {
            public string phase = "M2";
            public string generatedUtc;
            public string unityVersion;
            public string renderPipeline;
            public string urpPackageVersion;
            public int checkCount;
            public int meshVertexCount;
            public long meshTriangleCount;
            public int subMeshCount;
            public int blendShapeCount;
            public int rendererBoneCount;
            public int materialSlotCount;
            public int rampWidth;
            public int rampHeight;
            public int rampRowCount;
            public int shaderCompilerMessageCount;
            public int shaderKeywordPragmaCount;
            public int shaderPassCount;
            public int shaderTextureSampleCount;
            public float m1FrontForegroundLuminance;
            public float m2FrontForegroundLuminance;
            public float m2ToM1LuminanceRatio;
            public float bandFrontBackMae;
            public float finalFrontBackMae;
            public float bandIntermediateRatio;
            public float bandIntermediateRatioNear;
            public float bandIntermediateRatioFar;
            public Vector3 headForwardWS;
            public Vector3 headRightWS;
            public Vector3 headUpWS;
            public List<CheckResult> checks = new();
            public List<string> failures = new();
            public List<string> warnings = new();
            public string[] intentionallyDeferred =
            {
                "M3 ShadowCaster, shadow coordinates and cast-shadow attenuation",
                "M4 ControlMap, MatCap and within-material response masks",
                "M5 Face SDF",
                "M6 hair/eye stencil specialization",
                "M7 outline normals and outline pass",
                "M8 emission and Bloom",
                "M9 post-processing and performance stripping"
            };
        }

        private sealed class ImageData
        {
            public int width;
            public int height;
            public Color32[] pixels = Array.Empty<Color32>();
            public bool Valid => width > 0 && height > 0 && pixels.Length == width * height;
        }

        [MenuItem("Sandrone/M2/Validate Toon Ramp")]
        public static void ValidateAndWriteReport()
        {
            if (EditorSceneManager.GetActiveScene().path != SandroneM2Bootstrap.ScenePath)
            {
                EditorSceneManager.OpenScene(SandroneM2Bootstrap.ScenePath);
            }

            var report = new ValidationReport
            {
                generatedUtc = DateTime.UtcNow.ToString("O"),
                unityVersion = Application.unityVersion,
                renderPipeline = GraphicsSettings.defaultRenderPipeline != null
                    ? GraphicsSettings.defaultRenderPipeline.GetType().FullName
                    : "None",
                urpPackageVersion = UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages()
                    .FirstOrDefault(package => package.name == "com.unity.render-pipelines.universal")?.version ?? "missing"
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
            Check("URPAssigned", GraphicsSettings.defaultRenderPipeline != null &&
                                 GraphicsSettings.defaultRenderPipeline.GetType().Name.Contains("UniversalRenderPipelineAsset"),
                report.renderPipeline);
            Check("URPPackageVersion", report.urpPackageVersion == "17.5.0", report.urpPackageVersion);
            ValidatePriorReport("M1", "../TestArtifacts/M1/M1ValidationReport.json", Check);

            var modelImporter = AssetImporter.GetAtPath(SandroneM0Bootstrap.ModelPath) as ModelImporter;
            Check("ImportedNormals", modelImporter != null && modelImporter.importNormals == ModelImporterNormals.Import,
                modelImporter != null ? modelImporter.importNormals.ToString() : "missing importer");
            Check("MikkTangents", modelImporter != null && modelImporter.importTangents == ModelImporterTangents.CalculateMikk,
                modelImporter != null ? modelImporter.importTangents.ToString() : "missing importer");

            var controller = UnityEngine.Object.FindFirstObjectByType<SandroneM2Controller>();
            Check("M2Controller", controller != null, controller != null ? controller.name : "missing");
            var renderer = controller != null ? controller.TargetRenderer as SkinnedMeshRenderer : null;
            Check("SkinnedRenderer", renderer != null, renderer != null ? renderer.name : "missing");
            ValidateRenderer(renderer, controller, report, Check);
            ValidateRamp(renderer, report, Check);
            ValidateLightingAndAxes(controller, report, Check);
            ValidateCamera(Check);
            ValidateShader(report, Check);
            ValidateCaptures(report, Check);

            report.checkCount = report.checks.Count;
            report.warnings.Add("Performance baseline: 31 material slots remain 31 submesh Forward draws; M2 adds one Ramp sample but no extra Pass. Measure actual events in Frame Debugger during manual acceptance.");
            var artifactDirectory = Path.GetFullPath(Path.Combine(Application.dataPath, "../TestArtifacts/M2"));
            Directory.CreateDirectory(artifactDirectory);
            var reportPath = Path.Combine(artifactDirectory, "M2ValidationReport.json");
            File.WriteAllText(reportPath, JsonUtility.ToJson(report, true));
            Debug.Log($"[Sandrone M2] Validation report: {reportPath}");

            if (report.failures.Count > 0)
            {
                throw new BuildFailedException("Sandrone M2 validation failed:\n" + string.Join("\n", report.failures));
            }
        }

        private static void ValidatePriorReport(string expectedPhase, string relativePath,
            Action<string, bool, string> check)
        {
            var path = Path.GetFullPath(Path.Combine(Application.dataPath, relativePath));
            PriorReport prior = null;
            if (File.Exists(path))
            {
                prior = JsonUtility.FromJson<PriorReport>(File.ReadAllText(path));
            }
            check($"{expectedPhase}RegressionGate", prior != null && prior.phase == expectedPhase &&
                                                    (prior.failures == null || prior.failures.Length == 0), path);
        }

        private static void ValidateRenderer(SkinnedMeshRenderer renderer, SandroneM2Controller controller,
            ValidationReport report, Action<string, bool, string> check)
        {
            if (renderer == null)
            {
                return;
            }
            var mesh = renderer.sharedMesh;
            check("MeshAssigned", mesh != null, mesh != null ? mesh.name : "missing");
            if (mesh != null)
            {
                report.meshVertexCount = mesh.vertexCount;
                report.meshTriangleCount = Enumerable.Range(0, mesh.subMeshCount)
                    .Sum(index => (long)mesh.GetIndexCount(index) / 3L);
                report.subMeshCount = mesh.subMeshCount;
                report.blendShapeCount = mesh.blendShapeCount;
                check("TriangleCount", report.meshTriangleCount == 69864, report.meshTriangleCount.ToString());
                check("SubMeshCount", mesh.subMeshCount == 31, mesh.subMeshCount.ToString());
                check("BlendShapeCount", mesh.blendShapeCount == 61, mesh.blendShapeCount.ToString());
                if (mesh.vertexCount != 61973)
                {
                    report.warnings.Add($"Inherited M0 warning: Unity vertex buffer is {mesh.vertexCount}, PMX is 61973; stable invariants remain triangles/submeshes/blend-shapes.");
                }
            }

            report.rendererBoneCount = renderer.bones.Length;
            report.materialSlotCount = renderer.sharedMaterials.Length;
            check("RendererBones", renderer.bones.Length == 692, renderer.bones.Length.ToString());
            check("MaterialSlotCount", renderer.sharedMaterials.Length == 31, renderer.sharedMaterials.Length.ToString());
            check("NoNullMaterials", renderer.sharedMaterials.All(material => material != null),
                string.Join(",", renderer.sharedMaterials.Select(material => material != null ? material.name : "<null>")));
            check("CharacterHeight", renderer.bounds.size.y > 1.55f && renderer.bounds.size.y < 1.72f,
                renderer.bounds.size.y.ToString("F4"));
            check("RootTransform", controller != null && controller.CharacterRoot != null &&
                                   controller.CharacterRoot.position.sqrMagnitude < 1e-8f &&
                                   Quaternion.Angle(controller.CharacterRoot.rotation, Quaternion.identity) < 0.01f &&
                                   (controller.CharacterRoot.localScale - Vector3.one).sqrMagnitude < 1e-8f,
                controller?.CharacterRoot != null
                    ? $"pos={controller.CharacterRoot.position}, rot={controller.CharacterRoot.rotation.eulerAngles}, scale={controller.CharacterRoot.localScale}"
                    : "missing root");

            var shader = AssetDatabase.LoadAssetAtPath<Shader>(SandroneM2Bootstrap.ShaderPath);
            check("MaterialShader", renderer.sharedMaterials.All(material => material != null && material.shader == shader),
                shader != null ? shader.name : "missing shader");
            var map = AssetDatabase.LoadAssetAtPath<SandroneMaterialMap>(SandroneM0Bootstrap.MaterialMapPath);
            check("BaseMapBindings", map != null && map.Entries.Count == 31 && map.Entries.All(entry =>
                    AssetDatabase.GetAssetPath(renderer.sharedMaterials[entry.sourceIndex].GetTexture("_BaseMap")) == entry.baseTextureAssetPath),
                "31 M2 BaseMaps must preserve SandroneMaterialMap_v1_M0.");

            var surfacePreserved = map != null && map.Entries.All(entry =>
            {
                var m1 = AssetDatabase.LoadAssetAtPath<Material>(SandroneM1Bootstrap.MaterialPath(entry.sourceIndex, entry.materialAssetPath));
                var m2 = renderer.sharedMaterials[entry.sourceIndex];
                return m1 != null && m2 != null && m1.renderQueue == m2.renderQueue &&
                       m1.GetTag("RenderType", false, "") == m2.GetTag("RenderType", false, "") &&
                       Approximately(m1.GetFloat("_SrcBlend"), m2.GetFloat("_SrcBlend")) &&
                       Approximately(m1.GetFloat("_DstBlend"), m2.GetFloat("_DstBlend")) &&
                       Approximately(m1.GetFloat("_ZWrite"), m2.GetFloat("_ZWrite")) &&
                       Approximately(m1.GetFloat("_Cull"), m2.GetFloat("_Cull"));
            });
            check("M1SurfaceStatePreserved", surfacePreserved, "Queue, RenderType, Blend, ZWrite and Cull must match M1.");
        }

        private static void ValidateRamp(SkinnedMeshRenderer renderer, ValidationReport report,
            Action<string, bool, string> check)
        {
            var profile = AssetDatabase.LoadAssetAtPath<SandroneM2RampProfile>(SandroneM2Bootstrap.RampProfilePath);
            var ramp = AssetDatabase.LoadAssetAtPath<Texture2D>(SandroneM2Bootstrap.RampTexturePath);
            var importer = AssetImporter.GetAtPath(SandroneM2Bootstrap.RampTexturePath) as TextureImporter;
            check("RampProfile", profile != null && profile.ContractVersion == "SandroneRampProfile_v1_M2",
                profile != null ? profile.ContractVersion : "missing");
            check("RampTexture", ramp != null, SandroneM2Bootstrap.RampTexturePath);
            check("RampRows", profile != null && profile.Rows.Count == SandroneM2Bootstrap.RampRowCount,
                profile != null ? profile.Rows.Count.ToString() : "missing profile");
            if (ramp != null)
            {
                report.rampWidth = ramp.width;
                report.rampHeight = ramp.height;
                check("RampDimensions", ramp.width == SandroneM2Bootstrap.RampWidth && ramp.height == SandroneM2Bootstrap.RampHeight,
                    $"{ramp.width}x{ramp.height}");
            }
            report.rampRowCount = profile?.Rows.Count ?? 0;
            check("RampLinear", importer != null && !importer.sRGBTexture, importer != null ? $"sRGB={importer.sRGBTexture}" : "missing importer");
            check("RampClamp", importer != null && importer.wrapMode == TextureWrapMode.Clamp, importer?.wrapMode.ToString() ?? "missing");
            check("RampBilinear", importer != null && importer.filterMode == FilterMode.Bilinear, importer?.filterMode.ToString() ?? "missing");
            check("RampNoMip", importer != null && !importer.mipmapEnabled, importer != null ? $"mip={importer.mipmapEnabled}" : "missing");
            check("RampUncompressed", importer != null && importer.textureCompression == TextureImporterCompression.Uncompressed,
                importer?.textureCompression.ToString() ?? "missing");

            if (profile != null)
            {
                var allIndices = profile.Rows.SelectMany(row => row.materialIndices).OrderBy(index => index).ToArray();
                check("RampMaterialCoverage", allIndices.SequenceEqual(Enumerable.Range(0, 31)), string.Join(",", allIndices));
                check("RampFamiliesOrdered", profile.Rows.Select(row => (int)row.family).SequenceEqual(Enumerable.Range(0, 5)),
                    string.Join(",", profile.Rows.Select(row => row.family)));
                check("RampThresholdRange", profile.Rows.All(row => row.threshold >= 0.45f && row.threshold <= 0.60f),
                    string.Join(",", profile.Rows.Select(row => row.threshold.ToString("F3"))));
                check("RampOpaqueAlpha", profile.Rows.All(row => Approximately(row.shadowMultiplier.a, 1f) && Approximately(row.litMultiplier.a, 1f)),
                    "Ramp color alpha must remain 1; BaseMap owns transparency.");
            }

            ValidateRampSourcePixels(profile, check);
            if (renderer == null || profile == null || ramp == null)
            {
                return;
            }

            var bindingsCorrect = true;
            var parametersCorrect = true;
            for (var index = 0; index < renderer.sharedMaterials.Length; index++)
            {
                var material = renderer.sharedMaterials[index];
                var row = SandroneM2Bootstrap.RowForMaterial(index);
                bindingsCorrect &= material != null && material.GetTexture("_RampMap") == ramp &&
                                   Mathf.RoundToInt(material.GetFloat("_RampRow")) == row &&
                                   Approximately(material.GetFloat("_RampRowCount"), 5f);
                parametersCorrect &= material != null && row >= 0 &&
                                     Approximately(material.GetFloat("_Threshold"), SandroneM2Bootstrap.ThresholdForRow(row)) &&
                                     Approximately(material.GetFloat("_BandSoftness"), SandroneM2Bootstrap.BandSoftness) &&
                                     Approximately(material.GetFloat("_BandAA"), SandroneM2Bootstrap.BandAA);
            }
            check("RampMaterialBindings", bindingsCorrect, "Every material must bind the same Ramp and an integer row 0..4.");
            check("BandParameterBindings", parametersCorrect, "Threshold/softness/AA must match the locked M2 profile.");
        }

        private static void ValidateRampSourcePixels(SandroneM2RampProfile profile, Action<string, bool, string> check)
        {
            var path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", SandroneM2Bootstrap.RampTexturePath));
            if (profile == null || !File.Exists(path))
            {
                check("RampSourcePixels", false, path);
                return;
            }
            var image = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            try
            {
                var loaded = image.LoadImage(File.ReadAllBytes(path), false);
                var correct = loaded && image.width == 256 && image.height == 64;
                var separated = true;
                for (var row = 0; correct && row < profile.Rows.Count; row++)
                {
                    var y = Mathf.Clamp(Mathf.FloorToInt((row + 0.5f) / profile.Rows.Count * image.height), 0, image.height - 1);
                    correct &= Approximately(image.GetPixel(0, y), profile.Rows[row].shadowMultiplier, 2f / 255f) &&
                               Approximately(image.GetPixel(image.width - 1, y), profile.Rows[row].litMultiplier, 2f / 255f);
                    if (row > 0)
                    {
                        var previousY = Mathf.FloorToInt((row - 0.5f) / profile.Rows.Count * image.height);
                        separated &= ColorDistance(image.GetPixel(image.width / 2, y), image.GetPixel(image.width / 2, previousY)) > 0.025f;
                    }
                }
                check("RampSourcePixels", correct, "Row centers must reproduce authored linear shadow/lit endpoints within 2/255.");
                check("RampRowsSeparated", separated, "Adjacent row centers must remain observably distinct.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(image);
            }
        }

        private static void ValidateLightingAndAxes(SandroneM2Controller controller, ValidationReport report,
            Action<string, bool, string> check)
        {
            if (controller == null)
            {
                return;
            }
            check("HeadBone", controller.Head != null && controller.Head.name == "頭", controller.Head?.name ?? "missing");
            check("DirectionalLight", controller.MainLight != null && controller.MainLight.type == LightType.Directional,
                controller.MainLight?.type.ToString() ?? "missing");
            check("SingleDirectionalLight", UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None)
                    .Count(light => light.type == LightType.Directional) == 1, "exactly one");
            check("MainLightWhiteUnitIntensity", controller.MainLight != null &&
                                                  Approximately(controller.MainLight.color, Color.white, 1e-4f) &&
                                                  Approximately(controller.MainLight.intensity, 1f),
                controller.MainLight != null ? $"{controller.MainLight.color}, {controller.MainLight.intensity}" : "missing");
            check("CastShadowsDeferred", controller.MainLight != null && controller.MainLight.shadows == LightShadows.None,
                controller.MainLight?.shadows.ToString() ?? "missing");
            check("RenderSettingsSun", RenderSettings.sun == controller.MainLight, RenderSettings.sun?.name ?? "missing");

            report.headForwardWS = controller.HeadForwardWS;
            report.headRightWS = controller.HeadRightWS;
            report.headUpWS = controller.HeadUpWS;
            var normalized = IsUnit(report.headForwardWS) && IsUnit(report.headRightWS) && IsUnit(report.headUpWS);
            var orthogonal = Mathf.Abs(Vector3.Dot(report.headForwardWS, report.headRightWS)) < 1e-4f &&
                             Mathf.Abs(Vector3.Dot(report.headForwardWS, report.headUpWS)) < 1e-4f &&
                             Mathf.Abs(Vector3.Dot(report.headRightWS, report.headUpWS)) < 1e-4f;
            var handedness = Vector3.Dot(Vector3.Cross(report.headRightWS, report.headUpWS), report.headForwardWS);
            check("HeadAxesNormalized", normalized, $"F={report.headForwardWS.magnitude},R={report.headRightWS.magnitude},U={report.headUpWS.magnitude}");
            check("HeadAxesOrthogonal", orthogonal, "pairwise dot < 1e-4");
            check("HeadAxesRightHanded", handedness > 0.999f, handedness.ToString("F6"));
            ValidateAxisTracking(controller, check);
            ValidateLightSign(controller, check);
        }

        private static void ValidateAxisTracking(SandroneM2Controller controller, Action<string, bool, string> check)
        {
            if (controller.CharacterRoot == null || controller.Head == null)
            {
                check("RootRotationTracksHeadAxes", false, "missing root/head");
                check("HeadRotationTracksHeadAxes", false, "missing root/head");
                return;
            }
            var rootRotation = controller.CharacterRoot.rotation;
            var headRotation = controller.Head.localRotation;
            try
            {
                var before = controller.HeadForwardWS;
                var delta = Quaternion.Euler(0f, 90f, 0f);
                controller.CharacterRoot.rotation = delta * rootRotation;
                controller.Apply(true);
                check("RootRotationTracksHeadAxes", Vector3.Angle(delta * before, controller.HeadForwardWS) < 0.05f,
                    $"error={Vector3.Angle(delta * before, controller.HeadForwardWS):F6}deg");

                controller.CharacterRoot.rotation = rootRotation;
                controller.Head.localRotation = headRotation;
                controller.Apply(true);
                var beforeHead = controller.HeadForwardWS;
                controller.Head.localRotation = headRotation * Quaternion.Euler(0f, 15f, 0f);
                controller.Apply(true);
                var observed = Vector3.Angle(beforeHead, controller.HeadForwardWS);
                check("HeadRotationTracksHeadAxes", observed > 14.5f && observed < 15.5f, $"observed={observed:F4}deg");
            }
            finally
            {
                controller.CharacterRoot.rotation = rootRotation;
                controller.Head.localRotation = headRotation;
                controller.Apply(true);
            }
        }

        private static void ValidateLightSign(SandroneM2Controller controller, Action<string, bool, string> check)
        {
            if (controller.MainLight == null)
            {
                check("LightDirectionSign", false, "missing light");
                return;
            }
            var rotation = controller.MainLight.transform.rotation;
            try
            {
                var expected = new Vector3(0.2f, 0.3f, 0.93f).normalized;
                controller.SetLightDirectionToSource(expected);
                var dot = Vector3.Dot(expected, controller.MainLightDirectionWS);
                check("LightDirectionSign", dot > 0.9999f, $"dot(expected,-light.forward)={dot:F6}");
            }
            finally
            {
                controller.MainLight.transform.rotation = rotation;
            }
        }

        private static void ValidateCamera(Action<string, bool, string> check)
        {
            var camera = GameObject.Find("M2_CalibrationCamera")?.GetComponent<Camera>();
            check("CalibrationCamera", camera != null && camera.orthographic, camera != null ? "Orthographic" : "missing");
            check("CameraHDRDeferred", camera != null && !camera.allowHDR, camera != null ? camera.allowHDR.ToString() : "missing");
            check("CameraClips", camera != null && camera.nearClipPlane >= 0.05f && camera.farClipPlane >= 10f,
                camera != null ? $"near={camera.nearClipPlane}, far={camera.farClipPlane}" : "missing");
        }

        private static void ValidateShader(ValidationReport report, Action<string, bool, string> check)
        {
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(SandroneM2Bootstrap.ShaderPath);
            check("ShaderExists", shader != null && shader.isSupported, shader != null ? shader.name : "missing");
            if (shader == null)
            {
                return;
            }
            var messages = ShaderUtil.GetShaderMessages(shader);
            report.shaderCompilerMessageCount = messages.Length;
            check("ShaderCompileMessages", messages.Length == 0,
                messages.Length == 0 ? "none" : string.Join(" | ", messages.Select(message => $"{message.severity}:{message.message}")));

            var path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", SandroneM2Bootstrap.ShaderPath));
            var source = File.ReadAllText(path);
            report.shaderKeywordPragmaCount = source.Split('\n').Count(line =>
                line.Contains("#pragma multi_compile", StringComparison.Ordinal) ||
                line.Contains("#pragma shader_feature", StringComparison.Ordinal));
            report.shaderPassCount = Regex.Matches(source, @"\bPass\s*\{").Count;
            report.shaderTextureSampleCount = Regex.Matches(source, @"SAMPLE_TEXTURE2D\(").Count;
            check("ShaderVariants", report.shaderKeywordPragmaCount == 1 &&
                                    source.Contains("#pragma multi_compile _ _CLUSTER_LIGHT_LOOP", StringComparison.Ordinal),
                $"keyword pragmas={report.shaderKeywordPragmaCount}; expected only Forward/Forward+");
            check("SingleForwardPass", report.shaderPassCount == 1 && source.Contains("UniversalForward", StringComparison.Ordinal),
                $"passes={report.shaderPassCount}");
            check("TextureSampleBudget", report.shaderTextureSampleCount == 2,
                $"samples={report.shaderTextureSampleCount}; BaseMap + RampMap only");
            check("WorldNormalTransform", source.Contains("TransformObjectToWorldNormal", StringComparison.Ordinal), path);
            check("MainLightAPI", source.Contains("Lighting.hlsl", StringComparison.Ordinal) && source.Contains("GetMainLight()", StringComparison.Ordinal), path);
            check("DerivativeBandAA", source.Contains("fwidth(halfLambert)", StringComparison.Ordinal) &&
                                      source.Contains("smoothstep(_Threshold", StringComparison.Ordinal) &&
                                      source.Contains("_BandSoftness + derivativeWidth", StringComparison.Ordinal), path);
            check("RampRowCenter", source.Contains("(row + 0.5h) / max(_RampRowCount", StringComparison.Ordinal), path);
            check("RequiredDebugViews", source.Contains("_M2DebugMode", StringComparison.Ordinal) &&
                                        source.Contains("rampU", StringComparison.Ordinal) &&
                                        source.Contains("rampV", StringComparison.Ordinal) &&
                                        source.Contains("rampSample", StringComparison.Ordinal) &&
                                        source.Contains("bandMask", StringComparison.Ordinal), path);
            check("NoM3PlusFeatures", !source.Contains("ShadowCaster", StringComparison.Ordinal) &&
                                      !source.Contains("_MAIN_LIGHT_SHADOWS", StringComparison.Ordinal) &&
                                      !source.Contains("shadowAttenuation", StringComparison.Ordinal) &&
                                      !source.Contains("ControlMap", StringComparison.Ordinal) &&
                                      !source.Contains("FaceMap", StringComparison.Ordinal) &&
                                      !source.Contains("MatCap", StringComparison.Ordinal) &&
                                      !source.Contains("Outline", StringComparison.Ordinal) &&
                                      !source.Contains("Emission", StringComparison.Ordinal) &&
                                      !source.Contains("GetAdditional", StringComparison.Ordinal),
                "M3 shadows and M4+ material/face/outline/emission features remain deferred.");
        }

        private static void ValidateCaptures(ValidationReport report, Action<string, bool, string> check)
        {
            var root = Path.GetFullPath(Path.Combine(Application.dataPath, "../TestArtifacts/M2"));
            var relativePaths = new[]
            {
                "ReferenceComparison/M2_FinalToon_Front.png", "ReferenceComparison/M2_FinalToon_Side.png",
                "Debug/M2_HalfLambert.png", "Debug/M2_BandMask_FrontLight.png",
                "Debug/M2_BandMask_RightLight.png", "Debug/M2_BandMask_BackLight.png",
                "Debug/M2_RampUV.png", "Debug/M2_RampSample.png", "Debug/M2_NdotV.png",
                "Debug/M2_HeadAxis.png", "Debug/M2_Silhouette.png",
                "Debug/M2_BandMask_Near.png", "Debug/M2_Silhouette_Near.png",
                "Debug/M2_BandMask_Far.png", "Debug/M2_Silhouette_Far.png",
                "Debug/M2_FinalToon_FrontLight.png", "Debug/M2_FinalToon_BackLight.png"
            };
            foreach (var relative in relativePaths)
            {
                var path = Path.Combine(root, relative);
                var image = ReadImage(path);
                check($"Capture_{Path.GetFileNameWithoutExtension(path)}", image.Valid && image.pixels.Length > 1000,
                    $"{path}; {image.width}x{image.height}");
            }

            var silhouette = ReadImage(Path.Combine(root, "Debug/M2_Silhouette.png"));
            var bandFront = ReadImage(Path.Combine(root, "Debug/M2_BandMask_FrontLight.png"));
            var bandBack = ReadImage(Path.Combine(root, "Debug/M2_BandMask_BackLight.png"));
            var rampUv = ReadImage(Path.Combine(root, "Debug/M2_RampUV.png"));
            var rampSample = ReadImage(Path.Combine(root, "Debug/M2_RampSample.png"));
            var finalFront = ReadImage(Path.Combine(root, "Debug/M2_FinalToon_FrontLight.png"));
            var finalBack = ReadImage(Path.Combine(root, "Debug/M2_FinalToon_BackLight.png"));
            report.bandFrontBackMae = MaskedMae(bandFront, bandBack, silhouette);
            report.finalFrontBackMae = MaskedMae(finalFront, finalBack, silhouette);
            report.bandIntermediateRatio = IntermediateRatio(bandFront, silhouette);
            report.bandIntermediateRatioNear = IntermediateRatio(
                ReadImage(Path.Combine(root, "Debug/M2_BandMask_Near.png")),
                ReadImage(Path.Combine(root, "Debug/M2_Silhouette_Near.png")));
            report.bandIntermediateRatioFar = IntermediateRatio(
                ReadImage(Path.Combine(root, "Debug/M2_BandMask_Far.png")),
                ReadImage(Path.Combine(root, "Debug/M2_Silhouette_Far.png")));
            check("BandLightSweep", report.bandFrontBackMae > 35f, $"front/back masked MAE={report.bandFrontBackMae:F3}");
            check("FinalToonLightSweep", report.finalFrontBackMae > 8f, $"front/back masked MAE={report.finalFrontBackMae:F3}");
            check("BandAAIntermediatePixels", report.bandIntermediateRatio > 0.0002f && report.bandIntermediateRatio < 0.20f,
                $"ratio={report.bandIntermediateRatio:F6}");
            check("BandAAScaleBoundary", report.bandIntermediateRatioNear > 0.0001f &&
                                         report.bandIntermediateRatioFar > 0.0001f &&
                                         report.bandIntermediateRatioNear < 0.20f &&
                                         report.bandIntermediateRatioFar < 0.20f,
                $"near={report.bandIntermediateRatioNear:F6}, far={report.bandIntermediateRatioFar:F6}");
            check("RampRowsVisible", CountExpectedRampRows(rampUv, silhouette) == 5,
                $"visibleRows={CountExpectedRampRows(rampUv, silhouette)}");
            check("RampSampleIsColored", MaskedChannelSpread(rampSample, silhouette) > 5f,
                $"spread={MaskedChannelSpread(rampSample, silhouette):F3}");

            var m1Front = ReadImage(Path.GetFullPath(Path.Combine(Application.dataPath,
                "../TestArtifacts/M1/ReferenceComparison/M1_BaseLit_Front.png")));
            var m2Front = ReadImage(Path.Combine(root, "ReferenceComparison/M2_FinalToon_Front.png"));
            report.m1FrontForegroundLuminance = ForegroundMeanLuminance(m1Front);
            report.m2FrontForegroundLuminance = ForegroundMeanLuminance(m2Front);
            report.m2ToM1LuminanceRatio = report.m1FrontForegroundLuminance > 1e-5f
                ? report.m2FrontForegroundLuminance / report.m1FrontForegroundLuminance
                : 0f;
            check("BaseMapNotDoubleDarkened", report.m2ToM1LuminanceRatio >= 0.60f && report.m2ToM1LuminanceRatio <= 1.05f,
                $"M1={report.m1FrontForegroundLuminance:F3}, M2={report.m2FrontForegroundLuminance:F3}, ratio={report.m2ToM1LuminanceRatio:F3}");
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
            if (!SameSize(a, b) || !SameSize(a, mask))
            {
                return 0f;
            }
            double sum = 0;
            var count = 0;
            for (var i = 0; i < a.pixels.Length; i++)
            {
                if (Luminance(mask.pixels[i]) < 200)
                {
                    continue;
                }
                sum += Math.Abs(Luminance(a.pixels[i]) - Luminance(b.pixels[i]));
                count++;
            }
            return count > 0 ? (float)(sum / count) : 0f;
        }

        private static float IntermediateRatio(ImageData band, ImageData mask)
        {
            if (!SameSize(band, mask))
            {
                return 0f;
            }
            var foreground = 0;
            var intermediate = 0;
            for (var i = 0; i < band.pixels.Length; i++)
            {
                if (Luminance(mask.pixels[i]) < 200)
                {
                    continue;
                }
                foreground++;
                var value = Luminance(band.pixels[i]);
                if (value > 8 && value < 247)
                {
                    intermediate++;
                }
            }
            return foreground > 0 ? (float)intermediate / foreground : 0f;
        }

        private static int CountExpectedRampRows(ImageData rampUv, ImageData mask)
        {
            if (!SameSize(rampUv, mask))
            {
                return 0;
            }
            var visible = 0;
            for (var row = 0; row < 5; row++)
            {
                var expected = Mathf.RoundToInt(Mathf.LinearToGammaSpace((row + 0.5f) / 5f) * 255f);
                var count = 0;
                for (var i = 0; i < rampUv.pixels.Length; i++)
                {
                    if (Luminance(mask.pixels[i]) >= 200 && Math.Abs(rampUv.pixels[i].g - expected) <= 5)
                    {
                        count++;
                    }
                }
                if (count >= 20)
                {
                    visible++;
                }
            }
            return visible;
        }

        private static float MaskedChannelSpread(ImageData image, ImageData mask)
        {
            if (!SameSize(image, mask))
            {
                return 0f;
            }
            double sum = 0;
            var count = 0;
            for (var i = 0; i < image.pixels.Length; i++)
            {
                if (Luminance(mask.pixels[i]) < 200)
                {
                    continue;
                }
                var pixel = image.pixels[i];
                sum += Math.Max(pixel.r, Math.Max(pixel.g, pixel.b)) - Math.Min(pixel.r, Math.Min(pixel.g, pixel.b));
                count++;
            }
            return count > 0 ? (float)(sum / count) : 0f;
        }

        private static float ForegroundMeanLuminance(ImageData image)
        {
            if (!image.Valid)
            {
                return 0f;
            }
            var background = image.pixels[0];
            double sum = 0;
            var count = 0;
            foreach (var pixel in image.pixels)
            {
                var dr = pixel.r - background.r;
                var dg = pixel.g - background.g;
                var db = pixel.b - background.b;
                if (dr * dr + dg * dg + db * db <= 20 * 20)
                {
                    continue;
                }
                sum += Luminance(pixel) / 255.0;
                count++;
            }
            return count > 0 ? (float)(sum / count) : 0f;
        }

        private static bool SameSize(ImageData a, ImageData b)
        {
            return a.Valid && b.Valid && a.width == b.width && a.height == b.height;
        }

        private static int Luminance(Color32 pixel)
        {
            return (pixel.r * 54 + pixel.g * 183 + pixel.b * 19) >> 8;
        }

        private static bool IsUnit(Vector3 value) => Mathf.Abs(value.magnitude - 1f) < 1e-4f;
        private static bool Approximately(float a, float b) => Mathf.Abs(a - b) < 1e-4f;

        private static bool Approximately(Color a, Color b, float tolerance)
        {
            return Mathf.Abs(a.r - b.r) <= tolerance && Mathf.Abs(a.g - b.g) <= tolerance &&
                   Mathf.Abs(a.b - b.b) <= tolerance && Mathf.Abs(a.a - b.a) <= tolerance;
        }

        private static float ColorDistance(Color a, Color b)
        {
            var delta = new Vector3(a.r - b.r, a.g - b.g, a.b - b.b);
            return delta.magnitude;
        }
    }
}
