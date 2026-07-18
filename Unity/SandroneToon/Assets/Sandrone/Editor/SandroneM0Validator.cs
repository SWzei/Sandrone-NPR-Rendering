using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace SandroneToon.Editor
{
    public static class SandroneM0Validator
    {
        [Serializable]
        private sealed class CheckResult
        {
            public string name;
            public bool passed;
            public string details;
        }

        [Serializable]
        private sealed class ValidationReport
        {
            public string phase = "M0";
            public string generatedUtc;
            public string unityVersion;
            public string renderPipeline;
            public string urpPackageVersion;
            public int meshVertexCount;
            public long meshTriangleCount;
            public int subMeshCount;
            public int blendShapeCount;
            public int rendererBoneCount;
            public int hierarchyTransformCount;
            public int materialSlotCount;
            public float characterHeightMeters;
            public int shaderCompilerMessageCount;
            public int shaderKeywordPragmaCount;
            public string[] blendShapeNames;
            public string[] materialNames;
            public List<CheckResult> checks = new();
            public List<string> failures = new();
            public List<string> warnings = new();
            public string[] intentionallyDeferred =
            {
                "M1 main-light NdotL/NdotV/HeadAxis debugging",
                "M2 toon bands and Ramp",
                "M3 ShadowCaster and real-time shadow attenuation",
                "M4 ControlMap/MatCap/material response",
                "M5 Face SDF",
                "M6 hair and eye stencil specialization",
                "M7 outline normals and outline pass",
                "M8 emission and Bloom",
                "M9 final post-processing and performance stripping"
            };
        }

        [MenuItem("Sandrone/M0/Validate Baseline")]
        public static void ValidateAndWriteReport()
        {
            if (EditorSceneManager.GetActiveScene().path != SandroneM0Bootstrap.ScenePath)
            {
                EditorSceneManager.OpenScene(SandroneM0Bootstrap.ScenePath);
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

            var modelImporter = AssetImporter.GetAtPath(SandroneM0Bootstrap.ModelPath) as ModelImporter;
            Check("ModelImporterExists", modelImporter != null, SandroneM0Bootstrap.ModelPath);
            if (modelImporter != null)
            {
                Check("ModelNormals", modelImporter.importNormals == ModelImporterNormals.Import,
                    modelImporter.importNormals.ToString());
                Check("ModelTangents", modelImporter.importTangents == ModelImporterTangents.CalculateMikk,
                    modelImporter.importTangents.ToString());
                Check("ModelIndexFormat", modelImporter.indexFormat == ModelImporterIndexFormat.Auto,
                    modelImporter.indexFormat.ToString());
                Check("ModelBlendShapes", modelImporter.importBlendShapes, modelImporter.importBlendShapes.ToString());
                Check("ModelScale", Mathf.Approximately(modelImporter.globalScale, 1f) && modelImporter.useFileScale,
                    $"globalScale={modelImporter.globalScale}, useFileScale={modelImporter.useFileScale}");
            }

            var controller = UnityEngine.Object.FindFirstObjectByType<SandroneM0Controller>();
            Check("SceneController", controller != null, controller != null ? controller.name : "missing");
            var renderer = controller != null ? controller.GetComponentInChildren<SkinnedMeshRenderer>(true) : null;
            Check("SkinnedRenderer", renderer != null, renderer != null ? renderer.name : "missing");

            if (renderer != null)
            {
                var mesh = renderer.sharedMesh;
                Check("MeshAssigned", mesh != null, mesh != null ? mesh.name : "missing");
                if (mesh != null)
                {
                    report.meshVertexCount = mesh.vertexCount;
                    report.subMeshCount = mesh.subMeshCount;
                    report.blendShapeCount = mesh.blendShapeCount;
                    report.meshTriangleCount = Enumerable.Range(0, mesh.subMeshCount).Sum(i => (long)mesh.GetIndexCount(i) / 3L);
                    report.blendShapeNames = Enumerable.Range(0, mesh.blendShapeCount).Select(mesh.GetBlendShapeName).ToArray();
                    Check("TriangleCount", report.meshTriangleCount == 69864,
                        $"expected=69864, actual={report.meshTriangleCount}");
                    Check("SubMeshCount", mesh.subMeshCount == 31, $"expected=31, actual={mesh.subMeshCount}");
                    Check("BlendShapeCount", mesh.blendShapeCount == 61, $"expected=61, actual={mesh.blendShapeCount}");
                    Check("VertexBufferValid", mesh.vertexCount > 0,
                        $"PMX=61973; Unity imported={mesh.vertexCount}; triangle/submesh/blend-shape invariants are authoritative across FBX conversion");
                    if (mesh.vertexCount != 61973)
                    {
                        report.warnings.Add($"FBX vertex buffer count differs from PMX ({mesh.vertexCount} vs 61973). Blender/FBX/Unity may weld or split vertices; triangles, submeshes and all 61 blend shapes remain intact.");
                    }
                }

                report.rendererBoneCount = renderer.bones.Length;
                report.hierarchyTransformCount = controller.GetComponentsInChildren<Transform>(true).Length;
                report.materialSlotCount = renderer.sharedMaterials.Length;
                report.characterHeightMeters = renderer.bounds.size.y;
                report.materialNames = renderer.sharedMaterials.Select(m => m != null ? m.name : "<null>").ToArray();

                Check("MaterialSlotCount", renderer.sharedMaterials.Length == 31,
                    $"expected=31, actual={renderer.sharedMaterials.Length}");
                Check("NoNullMaterials", renderer.sharedMaterials.All(m => m != null),
                    string.Join(", ", report.materialNames));
                Check("CharacterHeight", renderer.bounds.size.y > 1.55f && renderer.bounds.size.y < 1.72f,
                    $"height={renderer.bounds.size.y:F4}m");
                Check("RootTransform", controller.transform.position.sqrMagnitude < 1e-8f &&
                                       Quaternion.Angle(controller.transform.rotation, Quaternion.identity) < 0.01f &&
                                       (controller.transform.localScale - Vector3.one).sqrMagnitude < 1e-8f,
                    $"position={controller.transform.position}, rotation={controller.transform.rotation.eulerAngles}, scale={controller.transform.localScale}");

                var shader = AssetDatabase.LoadAssetAtPath<Shader>(SandroneM0Bootstrap.ShaderPath);
                Check("MaterialShader", renderer.sharedMaterials.All(m => m != null && m.shader == shader),
                    shader != null ? shader.name : "missing shader");
                Check("BaseMapBindings", renderer.sharedMaterials.All(m => m != null && m.GetTexture("_BaseMap") != null),
                    "Every M0 material must have a BaseMap.");
            }

            if (controller != null)
            {
                Check("HeadBone", FindTransform(controller.transform, "頭") != null, "頭");
                Check("ClockworkBone", controller.ClockworkTarget != null && controller.ClockworkTarget.name == "KeyB02_M",
                    controller.ClockworkTarget != null ? controller.ClockworkTarget.name : "missing");
                Check("EyeALBinding", controller.EyeALBinding?.renderer != null && controller.EyeALBinding.materialIndex == 9,
                    controller.EyeALBinding != null ? controller.EyeALBinding.materialIndex.ToString() : "missing");
                Check("BlushBinding", controller.BlushBinding?.renderer != null && controller.BlushBinding.materialIndex == 30,
                    controller.BlushBinding != null ? controller.BlushBinding.materialIndex.ToString() : "missing");
            }

            var materialMap = AssetDatabase.LoadAssetAtPath<SandroneMaterialMap>(SandroneM0Bootstrap.MaterialMapPath);
            Check("MaterialMap", materialMap != null && materialMap.Entries.Count == 31,
                materialMap != null ? $"entries={materialMap.Entries.Count}, contract={materialMap.ContractVersion}" : "missing");
            if (materialMap != null)
            {
                Check("MaterialMapIndices", materialMap.Entries.Select(e => e.sourceIndex).SequenceEqual(Enumerable.Range(0, 31)),
                    string.Join(",", materialMap.Entries.Select(e => e.sourceIndex)));
                Check("SourceHash", materialMap.SourcePmxSha256 == "f73cd498580b0950856536223d57df04eb1164e01836c783cc75188a4c5c7514",
                    materialMap.SourcePmxSha256);
                foreach (var entry in materialMap.Entries)
                {
                    var textureImporter = AssetImporter.GetAtPath(entry.baseTextureAssetPath) as TextureImporter;
                    Check($"Texture_sRGB_{entry.sourceIndex:00}", textureImporter != null && textureImporter.sRGBTexture,
                        entry.baseTextureAssetPath);
                }
            }

            var m0Shader = AssetDatabase.LoadAssetAtPath<Shader>(SandroneM0Bootstrap.ShaderPath);
            Check("ShaderExists", m0Shader != null && m0Shader.isSupported, m0Shader != null ? m0Shader.name : "missing");
            if (m0Shader != null)
            {
                var messages = ShaderUtil.GetShaderMessages(m0Shader);
                report.shaderCompilerMessageCount = messages.Length;
                Check("ShaderCompileMessages", messages.Length == 0,
                    messages.Length == 0 ? "none" : string.Join(" | ", messages.Select(m => $"{m.severity}:{m.message}")));
                var source = File.ReadAllText(Path.GetFullPath(Path.Combine(Application.dataPath, "..", SandroneM0Bootstrap.ShaderPath)));
                report.shaderKeywordPragmaCount = source.Split('\n').Count(line =>
                    line.Contains("#pragma multi_compile", StringComparison.Ordinal) ||
                    line.Contains("#pragma shader_feature", StringComparison.Ordinal));
                Check("ShaderVariants", report.shaderKeywordPragmaCount == 0,
                    $"keyword pragmas={report.shaderKeywordPragmaCount}; M0 intentionally has one feature path");
                Check("NoFuturePasses", !source.Contains("ShadowCaster", StringComparison.Ordinal) &&
                                        !source.Contains("Outline", StringComparison.Ordinal) &&
                                        !source.Contains("Lighting.hlsl", StringComparison.Ordinal),
                    "M1+ lighting, M3 shadows, and M7 outline must remain deferred.");
            }

            var artifactDirectory = Path.GetFullPath(Path.Combine(Application.dataPath, "../TestArtifacts/M0"));
            var frontPath = Path.Combine(artifactDirectory, "ReferenceComparison/M0_Front.png");
            var sidePath = Path.Combine(artifactDirectory, "ReferenceComparison/M0_Side.png");
            Check("FrontCapture", CaptureHasContent(frontPath), frontPath);
            Check("SideCapture", CaptureHasContent(sidePath), sidePath);

            Directory.CreateDirectory(artifactDirectory);
            var reportPath = Path.Combine(artifactDirectory, "M0ValidationReport.json");
            File.WriteAllText(reportPath, JsonUtility.ToJson(report, true));
            Debug.Log($"[Sandrone M0] Validation report: {reportPath}");

            if (report.failures.Count > 0)
            {
                throw new BuildFailedException("Sandrone M0 validation failed:\n" + string.Join("\n", report.failures));
            }
        }

        private static bool CaptureHasContent(string path)
        {
            if (!File.Exists(path) || new FileInfo(path).Length <= 1024)
            {
                return false;
            }

            var image = new Texture2D(2, 2, TextureFormat.RGB24, false, false);
            try
            {
                if (!image.LoadImage(File.ReadAllBytes(path), false))
                {
                    return false;
                }
                var pixels = image.GetPixels32();
                if (pixels.Length == 0)
                {
                    return false;
                }
                var min = 255;
                var max = 0;
                var stride = Math.Max(1, pixels.Length / 4096);
                for (var i = 0; i < pixels.Length; i += stride)
                {
                    var luminance = (pixels[i].r * 54 + pixels[i].g * 183 + pixels[i].b * 19) >> 8;
                    min = Math.Min(min, luminance);
                    max = Math.Max(max, luminance);
                }
                return max - min >= 12;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(image);
            }
        }

        private static Transform FindTransform(Transform root, string name)
        {
            return root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == name);
        }
    }
}
