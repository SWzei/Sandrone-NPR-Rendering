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
using UnityEngine.SceneManagement;

namespace SandroneToon.Editor
{
    public static class SandroneM6Validator
    {
        [Serializable] public sealed class Check { public string name, details; public bool passed; }
        [Serializable] public sealed class Report
        {
            public string generatedUtc, unityVersion, graphicsApi, graphicsDevice;
            public int checkCount, failureCount, warningCount, shaderCompilerMessageCount, m6MaterialCount;
            public float hairSpecMae, eyeLayersMae, eyeALMae, eyeALFullFrameMae, bangShadowMae, pcMobileMae;
            public int eyeALChangedPixels;
            public List<Check> checks = new();
            public List<string> failures = new();
            public List<string> warnings = new();
            public string[] intentionallyDeferred =
            {
                "Optional movable-eye-gear physical mobile-device AA profiling (desktop D3D11 0.5/2/5/10m audit is complete)",
                "M7 outline normal/width and outline passes",
                "M8 HDR emission/Bloom for EyeLight",
                "M9 build-time variant stripping and target-device GPU profiling"
            };
        }

        [MenuItem("Sandrone/M6/Validate Hair and Eyes")]
        public static void ValidateAndWriteReport()
        {
            var report = new Report
            {
                generatedUtc = DateTime.UtcNow.ToString("O"),
                unityVersion = Application.unityVersion,
                graphicsApi = SystemInfo.graphicsDeviceType.ToString(),
                graphicsDevice = SystemInfo.graphicsDeviceName
            };
            void Add(string name, bool passed, string details)
            {
                report.checks.Add(new Check { name = name, passed = passed, details = details });
                if (!passed) report.failures.Add(name + ": " + details);
            }

            Add("UnityVersion", Application.unityVersion == "6000.5.3f1", Application.unityVersion);
            Add("ColorSpaceLinear", PlayerSettings.colorSpace == ColorSpace.Linear, PlayerSettings.colorSpace.ToString());
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(SandroneM6Bootstrap.ShaderPath);
            Add("ShaderPresentSupported", shader != null && shader.isSupported, shader == null ? "missing" : shader.name);
            if (shader != null)
            {
                var messages = ShaderUtil.GetShaderMessages(shader);
                report.shaderCompilerMessageCount = messages.Length;
                Add("ShaderCompilerMessages", messages.Length == 0, string.Join(" | ", messages.Select(x => x.message)));
            }

            var source = File.Exists(Absolute(SandroneM6Bootstrap.ShaderPath)) ? File.ReadAllText(Absolute(SandroneM6Bootstrap.ShaderPath)) : "";
            Add("ForwardPass", source.Contains("Name \"M6HairEye\""), "M6HairEye UniversalForward");
            Add("M4ShadowCasterReuse", source.Contains("UsePass \"Sandrone/M4/MaterialResponse/ShadowCaster\""), "M4 audited ShadowCaster");
            Add("NoEmissionBloomOutline", !Regex.IsMatch(source, "_Emission|Bloom\\s*\\{|Name\\s+\\\"Outline", RegexOptions.IgnoreCase), "M7/M8 shader features absent");
            Add("NoGlobalFeatureKeyword", !source.Contains("shader_feature") && !source.Contains("multi_compile __"), "M6 toggles use MPB numeric weights");
            Add("M4CBufferPrefix", HasExactM4Prefix(source), "M6 fields append after exact M4 prefix");
            Add("TangentInput", source.Contains("tangentOS:TANGENT") && source.Contains("tangentLobe"), "low-frequency tangent highlight");
            Add("StencilState", source.Contains("ReadMask [_M6StencilReadMask]") && source.Contains("Comp [_M6StencilComp]"), "eye template/readers");
            Add("HairMaskBound", source.Contains("hairBand * control.r"), "Control R gates procedural hair lobe");
            Add("HairOverlayNoDoubleSpec", source.Contains("IsHairBase() * castStyled"), "slot 29 role cannot receive procedural lobe");
            Add("EyeLightingBoundedLDR", source.Contains("bounded LDR eye lighting") && source.Contains("saturate(baseLit + hairSpec"), "M8 HDR emission not implemented");

            var profile = AssetDatabase.LoadAssetAtPath<SandroneM6HairEyeProfile>(SandroneM6Bootstrap.ProfilePath);
            Add("ProfileContract", profile != null && profile.ContractVersion == "SandroneHairEyeProfile_v1_M6", profile?.ContractVersion ?? "missing");
            Add("ProfileSlots", profile != null && profile.Slots.Length == 11 &&
                profile.Slots.Select(x => x.materialIndex).OrderBy(x => x).SequenceEqual(SandroneM6Bootstrap.TargetSlots.OrderBy(x => x)),
                profile == null ? "missing" : string.Join(",", profile.Slots.Select(x => x.materialIndex)));
            Add("HairSpecConservative", profile != null && profile.HairSpecularIntensity <= 0.2f, profile?.HairSpecularIntensity.ToString("F3") ?? "missing");

            var scene = EditorSceneManager.OpenScene(SandroneM6Bootstrap.ScenePath, OpenSceneMode.Single);
            Add("SceneOpened", scene.IsValid(), scene.path);
            var controller = UnityEngine.Object.FindFirstObjectByType<SandroneM6Controller>();
            var face = UnityEngine.Object.FindFirstObjectByType<SandroneM5Controller>();
            var m0 = UnityEngine.Object.FindFirstObjectByType<SandroneM0Controller>();
            var renderer = controller?.TargetRenderer;
            Add("Controllers", controller != null && face != null && m0 != null, "M0 layer + M5 face + M6 hair/eye");
            Add("Renderer", renderer != null && renderer.sharedMaterials.Length == 31, renderer == null ? "missing" : $"slots={renderer.sharedMaterials.Length}");
            Add("HeadAxes", controller?.Head != null && controller.Head.name == "頭", controller?.Head?.name ?? "missing");
            Add("DirectionalLight", controller?.MainLight != null && controller.MainLight.type == LightType.Directional && controller.MainLight.shadows != LightShadows.None,
                controller?.MainLight == null ? "missing" : controller.MainLight.shadows.ToString());
            Add("FaceSDFStillEnabled", face != null && face.FaceSdfEnabled, "M5 controller retained");

            if (renderer != null)
            {
                ValidateMaterials(renderer, profile, report, Add);
                var block = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(block, 9);
                Add("EyeALInitialHidden", Mathf.Abs(block.GetFloat("_LayerWeight")) < 1e-5f, $"slot9 MPB LayerWeight={block.GetFloat("_LayerWeight"):F3}");
            }

            var files = new[]
            {
                "ReferenceComparison/M6_FinalToon_Front.png", "ReferenceComparison/M6_Head_ThreeQuarter.png",
                "ReferenceComparison/M6_Head_Side.png", "AB/M6_AllOn.png", "AB/M6_HairSpecOff.png",
                "AB/M6_EyeLayersOff.png", "AB/M6_EyeAL_0.png", "AB/M6_EyeAL_1.png",
                "AB/M6_BangShadow_On.png", "AB/M6_BangShadow_Off.png",
                "Pipeline/M6_PC_ForwardPlus.png", "Pipeline/M6_Mobile_Forward.png"
            };
            foreach (var file in files) Add("Capture_" + Path.GetFileNameWithoutExtension(file), File.Exists(Artifact(file)), file);
            if (files.All(x => File.Exists(Artifact(x))))
            {
                report.hairSpecMae = PairMae("AB/M6_AllOn.png", "AB/M6_HairSpecOff.png");
                report.eyeLayersMae = PairMae("AB/M6_AllOn.png", "AB/M6_EyeLayersOff.png");
                report.eyeALFullFrameMae = PairMae("AB/M6_EyeAL_0.png", "AB/M6_EyeAL_1.png");
                report.eyeALMae = PairChangedMae("AB/M6_EyeAL_0.png", "AB/M6_EyeAL_1.png", out report.eyeALChangedPixels);
                report.bangShadowMae = PairMae("AB/M6_BangShadow_On.png", "AB/M6_BangShadow_Off.png");
                report.pcMobileMae = PairMae("Pipeline/M6_PC_ForwardPlus.png", "Pipeline/M6_Mobile_Forward.png");
                Add("HairSpecResponds", report.hairSpecMae > 0.01f && report.hairSpecMae < 12f, $"MAE={report.hairSpecMae:F4}");
                Add("EyeLayersRespond", report.eyeLayersMae > 0.01f, $"MAE={report.eyeLayersMae:F4}");
                Add("EyeALAnimates", report.eyeALChangedPixels > 16 && report.eyeALMae > 1f,
                    $"changed={report.eyeALChangedPixels}, targetMAE={report.eyeALMae:F3}, fullMAE={report.eyeALFullFrameMae:F4}");
                Add("BangShadowContributes", report.bangShadowMae > 0.002f, $"MAE={report.bangShadowMae:F4}");
                Add("PCMobileCompatible", report.pcMobileMae < 8f, $"MAE={report.pcMobileMae:F4}");
                foreach (var file in new[] { "ReferenceComparison/M6_FinalToon_Front.png", "ReferenceComparison/M6_Head_ThreeQuarter.png", "ReferenceComparison/M6_Head_Side.png" })
                {
                    var image = Read(Artifact(file));
                    var span = image.pixels.Max(x => x.r + x.g + x.b) - image.pixels.Min(x => x.r + x.g + x.b);
                    Add("NonBlank_" + Path.GetFileNameWithoutExtension(file), span > 120 && !IsCyanFailure(image), $"span={span}");
                }
            }

            foreach (SandroneM6DebugMode mode in Enum.GetValues(typeof(SandroneM6DebugMode)))
            {
                var path = Artifact($"Debug/M6_{mode}.png");
                Add("Debug_" + mode, File.Exists(path) && Read(path).pixels.Any(x => x.r + x.g + x.b > 12), path);
            }

            report.m6MaterialCount = AssetDatabase.FindAssets("t:Material", new[] { SandroneM6Bootstrap.MaterialDirectory }).Length;
            Add("M6MaterialCount", report.m6MaterialCount == 11, report.m6MaterialCount.ToString());
            report.warnings.Add("可动眼齿轮模型未替换标准 M0–M6 基线；桌面 D3D11 的 0.5/2/5/10m Alpha/Mip/AA 已单独通过，物理移动设备仍未测。");
            report.warnings.Add("Sandrone_Hair_Control 是 M4 项目种子，不是原资产 ILM；最终美术遮罩替换后需重测高光覆盖与远距闪烁。");
            report.warnings.Add("GPU 时间、SRP Batcher 实际状态与构建后变体数需在 M9/目标设备测量。");
            report.warningCount = report.warnings.Count;
            report.checkCount = report.checks.Count;
            report.failureCount = report.failures.Count;
            Directory.CreateDirectory(Artifact(""));
            File.WriteAllText(Artifact("M6ValidationReport.json"), JsonUtility.ToJson(report, true));
            if (report.failureCount > 0) throw new BuildFailedException("M6 validation failed: " + string.Join("; ", report.failures));
            Debug.Log($"[Sandrone M6] Validation passed: {report.checkCount}/{report.checkCount}, warnings={report.warningCount}.");
        }

        private static void ValidateMaterials(Renderer renderer, SandroneM6HairEyeProfile profile, Report report,
            Action<string, bool, string> add)
        {
            var map = AssetDatabase.LoadAssetAtPath<SandroneMaterialMap>(SandroneM0Bootstrap.MaterialMapPath);
            foreach (var entry in map.Entries.OrderBy(x => x.sourceIndex))
            {
                var actual = renderer.sharedMaterials[entry.sourceIndex];
                var baseline = SandroneM6Bootstrap.BaselineMaterial(entry.sourceIndex, entry.materialAssetPath);
                var targeted = SandroneM6Bootstrap.TargetSlots.Contains(entry.sourceIndex);
                add($"Slot{entry.sourceIndex:00}_AssetScope", targeted ? actual != baseline : actual == baseline,
                    targeted ? "M6 target material" : "exact M5/M4 baseline reuse");
                add($"Slot{entry.sourceIndex:00}_BaseMap", actual != null && baseline != null && actual.GetTexture("_BaseMap") == baseline.GetTexture("_BaseMap"),
                    actual?.GetTexture("_BaseMap")?.name ?? "missing");
                if (!targeted) continue;
                add($"Slot{entry.sourceIndex:00}_Shader", actual.shader.name == "Sandrone/M6/HairEye", actual.shader.name);
                add($"Slot{entry.sourceIndex:00}_State", actual.renderQueue == baseline.renderQueue &&
                    Mathf.Approximately(actual.GetFloat("_SrcBlend"), baseline.GetFloat("_SrcBlend")) &&
                    Mathf.Approximately(actual.GetFloat("_DstBlend"), baseline.GetFloat("_DstBlend")) &&
                    Mathf.Approximately(actual.GetFloat("_ZWrite"), baseline.GetFloat("_ZWrite")) &&
                    Mathf.Approximately(actual.GetFloat("_Cull"), baseline.GetFloat("_Cull")),
                    $"queue={actual.renderQueue}, blend={actual.GetFloat("_SrcBlend")}/{actual.GetFloat("_DstBlend")}, z={actual.GetFloat("_ZWrite")}, cull={actual.GetFloat("_Cull")}");
                add($"Slot{entry.sourceIndex:00}_BaseTransform", actual.GetTextureScale("_BaseMap") == baseline.GetTextureScale("_BaseMap") &&
                    actual.GetTextureOffset("_BaseMap") == baseline.GetTextureOffset("_BaseMap") && actual.GetColor("_BaseColor") == baseline.GetColor("_BaseColor"),
                    $"ST={actual.GetTextureScale("_BaseMap")}/{actual.GetTextureOffset("_BaseMap")}");
                if (entry.sourceIndex == 6)
                    add("IrisStencilWriter", actual.GetFloat("_M6StencilReadMask") == 0 && actual.GetFloat("_M6StencilWriteMask") == 1 &&
                        actual.GetFloat("_M6StencilComp") == (float)CompareFunction.Always && actual.GetFloat("_M6StencilPass") == (float)StencilOp.Replace,
                        "slot6 opaque authored iris writes the decorative-eye-layer template");
                if (entry.sourceIndex >= 7 && entry.sourceIndex <= 11)
                    add($"EyeStencilReader{entry.sourceIndex}", actual.GetFloat("_M6StencilReadMask") == 1 && actual.GetFloat("_M6StencilComp") == (float)CompareFunction.Equal,
                        "reader requires eye template bit");
            }
        }

        private static bool HasExactM4Prefix(string source)
        {
            var expected = new[]
            {
                "_BaseMap_ST", "_BaseColor", "_RampRow", "_RampRowCount", "_Threshold", "_BandSoftness", "_BandAA",
                "_CastShadowStrength", "_CastShadowLow", "_CastShadowHigh", "_ResponseType", "_FeatureGroup", "_SpecIntensity",
                "_SpecPower", "_MatCapIntensity", "_MetalMaskFallback", "_AOIntensity", "_OverlayColorBoost", "_LayerWeight",
                "_Cutoff", "_AlphaClip", "_SrcBlend", "_DstBlend", "_ZWrite", "_Cull", "_ShadowCull", "_M4DebugMode",
                "_M4FeatureWeight", "_HeadForwardWS", "_HeadRightWS", "_HeadUpWS"
            };
            var match = Regex.Match(source, @"CBUFFER_START\(UnityPerMaterial\)(.*?)CBUFFER_END", RegexOptions.Singleline);
            if (!match.Success) return false;
            var fields = Regex.Matches(match.Groups[1].Value, @"\b(_[A-Za-z0-9_]+)\s*;")
                .Cast<Match>().Select(x => x.Groups[1].Value).ToArray();
            return fields.Length >= expected.Length && fields.Take(expected.Length).SequenceEqual(expected);
        }

        private sealed class Image { public int width, height; public Color32[] pixels; }
        private static Image Read(string path)
        {
            var texture = new Texture2D(2, 2);
            try
            {
                if (!texture.LoadImage(File.ReadAllBytes(path))) throw new InvalidOperationException(path);
                return new Image { width = texture.width, height = texture.height, pixels = texture.GetPixels32() };
            }
            finally { UnityEngine.Object.DestroyImmediate(texture); }
        }
        private static float PairMae(string a, string b)
        {
            var x = Read(Artifact(a)); var y = Read(Artifact(b));
            if (x.width != y.width || x.height != y.height) return float.MaxValue;
            double sum = 0;
            for (var i = 0; i < x.pixels.Length; i++)
                sum += (Math.Abs(x.pixels[i].r - y.pixels[i].r) + Math.Abs(x.pixels[i].g - y.pixels[i].g) + Math.Abs(x.pixels[i].b - y.pixels[i].b)) / 3.0;
            return (float)(sum / x.pixels.Length);
        }
        private static float PairChangedMae(string a, string b, out int changedPixels)
        {
            var x = Read(Artifact(a)); var y = Read(Artifact(b));
            changedPixels = 0;
            if (x.width != y.width || x.height != y.height) return 0f;
            double sum = 0;
            for (var i = 0; i < x.pixels.Length; i++)
            {
                var dr = Math.Abs(x.pixels[i].r - y.pixels[i].r);
                var dg = Math.Abs(x.pixels[i].g - y.pixels[i].g);
                var db = Math.Abs(x.pixels[i].b - y.pixels[i].b);
                if (Math.Max(dr, Math.Max(dg, db)) <= 2) continue;
                sum += (dr + dg + db) / 3.0;
                changedPixels++;
            }
            return changedPixels == 0 ? 0f : (float)(sum / changedPixels);
        }
        private static bool IsCyanFailure(Image image)
        {
            var cyan = image.pixels.Count(x => x.g > 180 && x.b > 180 && x.r < 80);
            return cyan > image.pixels.Length / 20;
        }
        private static string ProjectRoot() => Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        private static string Artifact(string relative) => Path.Combine(ProjectRoot(), "TestArtifacts/M6", relative);
        private static string Absolute(string assetPath) => Path.Combine(ProjectRoot(), assetPath);
    }
}
