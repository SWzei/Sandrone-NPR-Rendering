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
    public static class SandroneM1Validator
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
            public string phase = "M1";
            public string generatedUtc;
            public string unityVersion;
            public string renderPipeline;
            public string urpPackageVersion;
            public int meshVertexCount;
            public long meshTriangleCount;
            public int subMeshCount;
            public int blendShapeCount;
            public int rendererBoneCount;
            public int materialSlotCount;
            public float characterHeightMeters;
            public int shaderCompilerMessageCount;
            public int shaderKeywordPragmaCount;
            public int shaderPassCount;
            public Vector3 headForwardWS;
            public Vector3 headRightWS;
            public Vector3 headUpWS;
            public Vector3 directionToMainLightWS;
            public float ndotlFrontMean;
            public float ndotlBackMean;
            public float ndotlFrontBackMae;
            public float ndotlFrontRightMae;
            public float headAxisChannelSpread;
            public List<CheckResult> checks = new();
            public List<string> failures = new();
            public List<string> warnings = new();
            public string[] intentionallyDeferred =
            {
                "M2 toon bands, Ramp assets, fwidth band anti-aliasing",
                "M3 ShadowCaster, shadow coordinates, cast-shadow attenuation",
                "M4 ControlMap, MatCap and material-family specular response",
                "M5 Face SDF",
                "M6 hair and eye stencil specialization",
                "M7 outline normals and outline pass",
                "M8 emission and Bloom",
                "M9 final post-processing and performance stripping"
            };
        }

        private readonly struct ImageStats
        {
            public readonly bool valid;
            public readonly int minLuminance;
            public readonly int maxLuminance;
            public readonly float meanLuminance;
            public readonly float channelSpread;
            public readonly Color32[] pixels;

            public ImageStats(bool valid, int minLuminance, int maxLuminance, float meanLuminance,
                float channelSpread, Color32[] pixels)
            {
                this.valid = valid;
                this.minLuminance = minLuminance;
                this.maxLuminance = maxLuminance;
                this.meanLuminance = meanLuminance;
                this.channelSpread = channelSpread;
                this.pixels = pixels;
            }
        }

        [MenuItem("Sandrone/M1/Validate Main Light Baseline")]
        public static void ValidateAndWriteReport()
        {
            if (EditorSceneManager.GetActiveScene().path != SandroneM1Bootstrap.ScenePath)
            {
                EditorSceneManager.OpenScene(SandroneM1Bootstrap.ScenePath);
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

            var m0ReportPath = Path.GetFullPath(Path.Combine(Application.dataPath, "../TestArtifacts/M0/M0ValidationReport.json"));
            PriorReport priorReport = null;
            if (File.Exists(m0ReportPath))
            {
                priorReport = JsonUtility.FromJson<PriorReport>(File.ReadAllText(m0ReportPath));
            }
            Check("M0RegressionGate", priorReport != null && priorReport.phase == "M0" &&
                                      (priorReport.failures == null || priorReport.failures.Length == 0), m0ReportPath);

            var modelImporter = AssetImporter.GetAtPath(SandroneM0Bootstrap.ModelPath) as ModelImporter;
            Check("ImportedNormals", modelImporter != null && modelImporter.importNormals == ModelImporterNormals.Import,
                modelImporter != null ? modelImporter.importNormals.ToString() : "missing importer");
            Check("MikkTangents", modelImporter != null && modelImporter.importTangents == ModelImporterTangents.CalculateMikk,
                modelImporter != null ? modelImporter.importTangents.ToString() : "missing importer");

            var controller = UnityEngine.Object.FindFirstObjectByType<SandroneM1Controller>();
            Check("M1Controller", controller != null, controller != null ? controller.name : "missing");
            var renderer = controller != null ? controller.TargetRenderer as SkinnedMeshRenderer : null;
            Check("SkinnedRenderer", renderer != null, renderer != null ? renderer.name : "missing");

            if (renderer != null)
            {
                var mesh = renderer.sharedMesh;
                Check("MeshAssigned", mesh != null, mesh != null ? mesh.name : "missing");
                if (mesh != null)
                {
                    report.meshVertexCount = mesh.vertexCount;
                    report.meshTriangleCount = Enumerable.Range(0, mesh.subMeshCount)
                        .Sum(index => (long)mesh.GetIndexCount(index) / 3L);
                    report.subMeshCount = mesh.subMeshCount;
                    report.blendShapeCount = mesh.blendShapeCount;
                    Check("TriangleCount", report.meshTriangleCount == 69864,
                        $"expected=69864, actual={report.meshTriangleCount}");
                    Check("SubMeshCount", mesh.subMeshCount == 31, $"expected=31, actual={mesh.subMeshCount}");
                    Check("BlendShapeCount", mesh.blendShapeCount == 61, $"expected=61, actual={mesh.blendShapeCount}");
                    if (mesh.vertexCount != 61973)
                    {
                        report.warnings.Add($"Inherited M0 warning: Unity vertex buffer is {mesh.vertexCount}, PMX is 61973; stable cross-format invariants remain triangles/submeshes/blend-shapes.");
                    }
                }

                report.rendererBoneCount = renderer.bones.Length;
                report.materialSlotCount = renderer.sharedMaterials.Length;
                report.characterHeightMeters = renderer.bounds.size.y;
                Check("RendererBones", renderer.bones.Length == 692, renderer.bones.Length.ToString());
                Check("MaterialSlotCount", renderer.sharedMaterials.Length == 31,
                    $"expected=31, actual={renderer.sharedMaterials.Length}");
                Check("NoNullMaterials", renderer.sharedMaterials.All(material => material != null),
                    string.Join(",", renderer.sharedMaterials.Select(material => material != null ? material.name : "<null>")));
                Check("CharacterHeight", renderer.bounds.size.y > 1.55f && renderer.bounds.size.y < 1.72f,
                    $"height={renderer.bounds.size.y:F4}m");
                Check("RootTransform", controller.CharacterRoot != null &&
                                       controller.CharacterRoot.position.sqrMagnitude < 1e-8f &&
                                       Quaternion.Angle(controller.CharacterRoot.rotation, Quaternion.identity) < 0.01f &&
                                       (controller.CharacterRoot.localScale - Vector3.one).sqrMagnitude < 1e-8f,
                    controller.CharacterRoot != null
                        ? $"position={controller.CharacterRoot.position}, rotation={controller.CharacterRoot.rotation.eulerAngles}, scale={controller.CharacterRoot.localScale}"
                        : "missing root");

                var shader = AssetDatabase.LoadAssetAtPath<Shader>(SandroneM1Bootstrap.ShaderPath);
                Check("MaterialShader", renderer.sharedMaterials.All(material => material != null && material.shader == shader),
                    shader != null ? shader.name : "missing shader");
                Check("BaseMapBindings", renderer.sharedMaterials.All(material => material != null && material.GetTexture("_BaseMap") != null),
                    "Every M1 material must retain its M0 BaseMap.");

                var map = AssetDatabase.LoadAssetAtPath<SandroneMaterialMap>(SandroneM0Bootstrap.MaterialMapPath);
                var texturePathsMatch = map != null && map.Entries.Count == 31 && map.Entries.All(entry =>
                {
                    var material = renderer.sharedMaterials[entry.sourceIndex];
                    return AssetDatabase.GetAssetPath(material.GetTexture("_BaseMap")) == entry.baseTextureAssetPath;
                });
                Check("M0TextureMappingPreserved", texturePathsMatch, "31 BaseMaps must match SandroneMaterialMap_v1_M0.");
            }

            if (controller != null)
            {
                Check("HeadBone", controller.Head != null && controller.Head.name == "頭",
                    controller.Head != null ? controller.Head.name : "missing");
                Check("CharacterRoot", controller.CharacterRoot != null, controller.CharacterRoot != null ? controller.CharacterRoot.name : "missing");
                Check("DirectionalLight", controller.MainLight != null && controller.MainLight.type == LightType.Directional,
                    controller.MainLight != null ? controller.MainLight.type.ToString() : "missing");
                Check("SingleDirectionalLight", UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None)
                        .Count(light => light.type == LightType.Directional) == 1,
                    "M1 scene requires exactly one directional light.");
                Check("MainLightWhiteUnitIntensity", controller.MainLight != null &&
                                                     Approximately(controller.MainLight.color, Color.white, 1e-4f) &&
                                                     Mathf.Abs(controller.MainLight.intensity - 1f) < 1e-4f,
                    controller.MainLight != null ? $"color={controller.MainLight.color}, intensity={controller.MainLight.intensity}" : "missing");
                Check("CastShadowsDeferred", controller.MainLight != null && controller.MainLight.shadows == LightShadows.None,
                    controller.MainLight != null ? controller.MainLight.shadows.ToString() : "missing");
                Check("RenderSettingsSun", RenderSettings.sun == controller.MainLight,
                    RenderSettings.sun != null ? RenderSettings.sun.name : "missing");

                report.headForwardWS = controller.HeadForwardWS;
                report.headRightWS = controller.HeadRightWS;
                report.headUpWS = controller.HeadUpWS;
                report.directionToMainLightWS = controller.MainLightDirectionWS;

                var normalized = Mathf.Abs(report.headForwardWS.magnitude - 1f) < 1e-4f &&
                                 Mathf.Abs(report.headRightWS.magnitude - 1f) < 1e-4f &&
                                 Mathf.Abs(report.headUpWS.magnitude - 1f) < 1e-4f;
                var orthogonal = Mathf.Abs(Vector3.Dot(report.headForwardWS, report.headRightWS)) < 1e-4f &&
                                 Mathf.Abs(Vector3.Dot(report.headForwardWS, report.headUpWS)) < 1e-4f &&
                                 Mathf.Abs(Vector3.Dot(report.headRightWS, report.headUpWS)) < 1e-4f;
                var handedness = Vector3.Dot(Vector3.Cross(report.headRightWS, report.headUpWS), report.headForwardWS);
                Check("HeadAxesNormalized", normalized,
                    $"F={report.headForwardWS.magnitude:F6}, R={report.headRightWS.magnitude:F6}, U={report.headUpWS.magnitude:F6}");
                Check("HeadAxesOrthogonal", orthogonal,
                    $"FR={Vector3.Dot(report.headForwardWS, report.headRightWS):F6}, FU={Vector3.Dot(report.headForwardWS, report.headUpWS):F6}, RU={Vector3.Dot(report.headRightWS, report.headUpWS):F6}");
                Check("HeadAxesRightHanded", handedness > 0.999f, handedness.ToString("F6"));

                ValidateAxisTracking(controller, Check);
                ValidateLightDirectionSign(controller, Check);
            }

            var camera = GameObject.Find("M1_CalibrationCamera")?.GetComponent<Camera>();
            Check("CalibrationCamera", camera != null && camera.orthographic, camera != null ? "Orthographic" : "missing");
            Check("CameraHDRDeferred", camera != null && !camera.allowHDR, camera != null ? camera.allowHDR.ToString() : "missing");

            var m1Shader = AssetDatabase.LoadAssetAtPath<Shader>(SandroneM1Bootstrap.ShaderPath);
            Check("ShaderExists", m1Shader != null && m1Shader.isSupported, m1Shader != null ? m1Shader.name : "missing");
            if (m1Shader != null)
            {
                var messages = ShaderUtil.GetShaderMessages(m1Shader);
                report.shaderCompilerMessageCount = messages.Length;
                Check("ShaderCompileMessages", messages.Length == 0,
                    messages.Length == 0 ? "none" : string.Join(" | ", messages.Select(message => $"{message.severity}:{message.message}")));

                var shaderSourcePath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", SandroneM1Bootstrap.ShaderPath));
                var source = File.ReadAllText(shaderSourcePath);
                report.shaderKeywordPragmaCount = source.Split('\n').Count(line =>
                    line.Contains("#pragma multi_compile", StringComparison.Ordinal) ||
                    line.Contains("#pragma shader_feature", StringComparison.Ordinal));
                report.shaderPassCount = Regex.Matches(source, @"\bPass\s*\{").Count;

                Check("ShaderVariants", report.shaderKeywordPragmaCount == 1 &&
                                        source.Contains("#pragma multi_compile _ _CLUSTER_LIGHT_LOOP", StringComparison.Ordinal),
                    $"keyword pragmas={report.shaderKeywordPragmaCount}; exactly one URP-required Forward/Forward+ switch yields two variants");
                Check("ForwardPlusCompatibility", source.Contains("_CLUSTER_LIGHT_LOOP", StringComparison.Ordinal),
                    "PC Renderer is Forward+; URP 17.5 requires the cluster-loop variant so GetMainLight does not depend on unpopulated unity_LightData.z.");
                Check("SingleForwardPass", report.shaderPassCount == 1 && source.Contains("UniversalForward", StringComparison.Ordinal),
                    $"passes={report.shaderPassCount}");
                Check("WorldNormalTransform", source.Contains("TransformObjectToWorldNormal", StringComparison.Ordinal), shaderSourcePath);
                Check("MainLightAPI", source.Contains("Lighting.hlsl", StringComparison.Ordinal) &&
                                      source.Contains("GetMainLight()", StringComparison.Ordinal), shaderSourcePath);
                Check("ViewDirectionAPI", source.Contains("GetWorldSpaceNormalizeViewDir", StringComparison.Ordinal), shaderSourcePath);
                Check("RequiredDebugViews", source.Contains("_M1DebugMode", StringComparison.Ordinal) &&
                                            source.Contains("_HeadForwardWS", StringComparison.Ordinal) &&
                                            source.Contains("ndotlSigned01", StringComparison.Ordinal) &&
                                            source.Contains("ndotv", StringComparison.Ordinal), shaderSourcePath);
                Check("NoM2PlusFeatures", !source.Contains("RampMap", StringComparison.Ordinal) &&
                                          !source.Contains("FaceMap", StringComparison.Ordinal) &&
                                          !source.Contains("ShadowCaster", StringComparison.Ordinal) &&
                                          !source.Contains("_MAIN_LIGHT_SHADOWS", StringComparison.Ordinal) &&
                                          !source.Contains("GetShadowCoord", StringComparison.Ordinal) &&
                                          !source.Contains("shadowAttenuation", StringComparison.Ordinal) &&
                                          !source.Contains("Outline", StringComparison.Ordinal) &&
                                          !source.Contains("Emission", StringComparison.Ordinal) &&
                                          !source.Contains("GetAdditional", StringComparison.Ordinal),
                    "M2 Ramp, M3 shadows, additional lights and M7/M8 features must remain deferred.");
            }

            ValidateCaptures(report, Check);

            var artifactDirectory = Path.GetFullPath(Path.Combine(Application.dataPath, "../TestArtifacts/M1"));
            Directory.CreateDirectory(artifactDirectory);
            var reportPath = Path.Combine(artifactDirectory, "M1ValidationReport.json");
            File.WriteAllText(reportPath, JsonUtility.ToJson(report, true));
            Debug.Log($"[Sandrone M1] Validation report: {reportPath}");

            if (report.failures.Count > 0)
            {
                throw new BuildFailedException("Sandrone M1 validation failed:\n" + string.Join("\n", report.failures));
            }
        }

        private static void ValidateAxisTracking(SandroneM1Controller controller,
            Action<string, bool, string> check)
        {
            if (controller.CharacterRoot == null || controller.Head == null)
            {
                check("RootRotationTracksHeadAxes", false, "missing root/head");
                check("HeadRotationTracksHeadAxes", false, "missing head");
                return;
            }

            var originalRootRotation = controller.CharacterRoot.rotation;
            var originalHeadLocalRotation = controller.Head.localRotation;
            try
            {
                var beforeRoot = controller.HeadForwardWS;
                var rootDelta = Quaternion.Euler(0f, 90f, 0f);
                controller.CharacterRoot.rotation = rootDelta * originalRootRotation;
                controller.Apply(true);
                var expectedRoot = rootDelta * beforeRoot;
                var rootAngleError = Vector3.Angle(expectedRoot, controller.HeadForwardWS);
                check("RootRotationTracksHeadAxes", rootAngleError < 0.05f, $"angleError={rootAngleError:F6}deg");

                controller.CharacterRoot.rotation = originalRootRotation;
                controller.Apply(true);
                var beforeHead = controller.HeadForwardWS;
                controller.Head.localRotation = originalHeadLocalRotation *
                                                Quaternion.AngleAxis(15f, controller.HeadUpLocal);
                controller.Apply(true);
                var headAngle = Vector3.Angle(beforeHead, controller.HeadForwardWS);
                check("HeadRotationTracksHeadAxes", headAngle > 10f && headAngle < 20f, $"observed={headAngle:F4}deg");
            }
            finally
            {
                controller.CharacterRoot.rotation = originalRootRotation;
                controller.Head.localRotation = originalHeadLocalRotation;
                controller.Apply(true);
            }
        }

        private static void ValidateLightDirectionSign(SandroneM1Controller controller,
            Action<string, bool, string> check)
        {
            if (controller.MainLight == null)
            {
                check("LightDirectionSign", false, "missing light");
                return;
            }

            var originalRotation = controller.MainLight.transform.rotation;
            try
            {
                var expected = new Vector3(0.2f, 0.3f, 0.93f).normalized;
                controller.SetLightDirectionToSource(expected);
                var dot = Vector3.Dot(expected, controller.MainLightDirectionWS);
                check("LightDirectionSign", dot > 0.9999f,
                    $"dot(expected,-light.forward)={dot:F6}; URP direction is surface-to-light");
            }
            finally
            {
                controller.MainLight.transform.rotation = originalRotation;
            }
        }

        private static void ValidateCaptures(ValidationReport report, Action<string, bool, string> check)
        {
            var root = Path.GetFullPath(Path.Combine(Application.dataPath, "../TestArtifacts/M1"));
            var baseFront = Path.Combine(root, "ReferenceComparison/M1_BaseLit_Front.png");
            var baseSide = Path.Combine(root, "ReferenceComparison/M1_BaseLit_Side.png");
            var ndotlFront = Path.Combine(root, "Debug/M1_NdotL_FrontLight.png");
            var ndotlRight = Path.Combine(root, "Debug/M1_NdotL_RightLight.png");
            var ndotlBack = Path.Combine(root, "Debug/M1_NdotL_BackLight.png");
            var ndotv = Path.Combine(root, "Debug/M1_NdotV.png");
            var headAxis = Path.Combine(root, "Debug/M1_HeadAxis.png");
            var lightColor = Path.Combine(root, "Debug/M1_MainLightColor.png");
            var lightDistance = Path.Combine(root, "Debug/M1_MainLightDistanceAttenuation.png");

            var paths = new[] { baseFront, baseSide, ndotlFront, ndotlRight, ndotlBack, ndotv, headAxis, lightColor, lightDistance };
            foreach (var path in paths)
            {
                var stats = ReadImageStats(path);
                check($"Capture_{Path.GetFileNameWithoutExtension(path)}", stats.valid && stats.maxLuminance - stats.minLuminance >= 12,
                    $"{path}; range={stats.maxLuminance - stats.minLuminance}");
            }

            var frontStats = ReadImageStats(ndotlFront);
            var rightStats = ReadImageStats(ndotlRight);
            var backStats = ReadImageStats(ndotlBack);
            var headStats = ReadImageStats(headAxis);
            var baseFrontStats = ReadImageStats(baseFront);
            var lightColorStats = ReadImageStats(lightColor);
            var lightDistanceStats = ReadImageStats(lightDistance);
            report.ndotlFrontMean = frontStats.meanLuminance;
            report.ndotlBackMean = backStats.meanLuminance;
            report.ndotlFrontBackMae = MeanAbsoluteLuminanceDifference(frontStats, backStats);
            report.ndotlFrontRightMae = MeanAbsoluteLuminanceDifference(frontStats, rightStats);
            report.headAxisChannelSpread = headStats.channelSpread;

            check("NdotLFrontBrighterThanBack", report.ndotlFrontMean > report.ndotlBackMean + 2f,
                $"front={report.ndotlFrontMean:F3}, back={report.ndotlBackMean:F3}");
            check("NdotLLightSweepChangesImage", report.ndotlFrontBackMae > 3f && report.ndotlFrontRightMae > 2f,
                $"front/back MAE={report.ndotlFrontBackMae:F3}, front/right MAE={report.ndotlFrontRightMae:F3}");
            check("HeadAxisIsColorEncoded", report.headAxisChannelSpread > 2f,
                $"mean channel spread={report.headAxisChannelSpread:F3}");
            check("BaseLitRetainsColor", baseFrontStats.channelSpread > 3f,
                $"mean channel spread={baseFrontStats.channelSpread:F3}");
            check("MainLightColorNonZero", lightColorStats.meanLuminance > 45f,
                $"image mean={lightColorStats.meanLuminance:F3}");
            check("MainLightDistanceAttenuationNonZero", lightDistanceStats.meanLuminance > 45f,
                $"image mean={lightDistanceStats.meanLuminance:F3}");
        }

        private static ImageStats ReadImageStats(string path)
        {
            if (!File.Exists(path) || new FileInfo(path).Length <= 1024)
            {
                return new ImageStats(false, 0, 0, 0f, 0f, Array.Empty<Color32>());
            }

            var image = new Texture2D(2, 2, TextureFormat.RGB24, false, false);
            try
            {
                if (!image.LoadImage(File.ReadAllBytes(path), false))
                {
                    return new ImageStats(false, 0, 0, 0f, 0f, Array.Empty<Color32>());
                }

                var sourcePixels = image.GetPixels32();
                var stride = Math.Max(1, sourcePixels.Length / 200000);
                var pixels = sourcePixels.Where((_, index) => index % stride == 0).ToArray();
                var min = 255;
                var max = 0;
                double luminanceSum = 0;
                double spreadSum = 0;
                foreach (var pixel in pixels)
                {
                    var luminance = Luminance(pixel);
                    min = Math.Min(min, luminance);
                    max = Math.Max(max, luminance);
                    luminanceSum += luminance;
                    spreadSum += Math.Max(pixel.r, Math.Max(pixel.g, pixel.b)) -
                                 Math.Min(pixel.r, Math.Min(pixel.g, pixel.b));
                }
                return new ImageStats(true, min, max, (float)(luminanceSum / pixels.Length),
                    (float)(spreadSum / pixels.Length), pixels);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(image);
            }
        }

        private static float MeanAbsoluteLuminanceDifference(ImageStats a, ImageStats b)
        {
            if (!a.valid || !b.valid || a.pixels.Length != b.pixels.Length || a.pixels.Length == 0)
            {
                return 0f;
            }
            double sum = 0;
            for (var index = 0; index < a.pixels.Length; index++)
            {
                sum += Math.Abs(Luminance(a.pixels[index]) - Luminance(b.pixels[index]));
            }
            return (float)(sum / a.pixels.Length);
        }

        private static int Luminance(Color32 pixel)
        {
            return (pixel.r * 54 + pixel.g * 183 + pixel.b * 19) >> 8;
        }

        private static bool Approximately(Color a, Color b, float tolerance)
        {
            return Mathf.Abs(a.r - b.r) < tolerance && Mathf.Abs(a.g - b.g) < tolerance &&
                   Mathf.Abs(a.b - b.b) < tolerance && Mathf.Abs(a.a - b.a) < tolerance;
        }
    }
}
