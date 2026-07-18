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
    public static class SandroneM7Validator
    {
        [Serializable] public sealed class Check { public string name, details; public bool passed; }
        [Serializable] public sealed class Report
        {
            public string generatedUtc, unityVersion, graphicsApi, graphicsDevice;
            public int checkCount, failureCount, warningCount, shaderCompilerMessageCount, eligibleSlotCount;
            public SandroneM7Bootstrap.NormalAudit sourceNormalAudit, outlineNormalAudit;
            public float outlineOnOffMae, m6BaselineMae, outsideChangedRatio, originalSmoothedMae, pcMobileMae;
            public float nearThickness, midThickness, farThickness;
            public int outlineChangedPixels, redSkirtPixelsOff, redSkirtPixelsOn;
            public List<Check> checks = new(); public List<string> failures = new(); public List<string> warnings = new();
            public string[] intentionallyDeferred =
            {
                "Artist-authored per-vertex outline width and dedicated DCC outline-normal channel",
                "M8 HDR emission/Bloom", "M9 build-time variant stripping and target-device GPU/SRP Batcher profiling"
            };
        }

        [MenuItem("Sandrone/M7/Validate Outline")]
        public static void ValidateAndWriteReport()
        {
            var report = new Report
            {
                generatedUtc = DateTime.UtcNow.ToString("O"), unityVersion = Application.unityVersion,
                graphicsApi = SystemInfo.graphicsDeviceType.ToString(), graphicsDevice = SystemInfo.graphicsDeviceName,
                eligibleSlotCount = SandroneM7Bootstrap.EligibleSlots.Length
            };
            void Add(string name, bool passed, string details)
            {
                report.checks.Add(new Check { name = name, passed = passed, details = details });
                if (!passed) report.failures.Add(name + ": " + details);
            }

            Add("UnityVersion", Application.unityVersion == "6000.5.3f1", Application.unityVersion);
            Add("ColorSpaceLinear", PlayerSettings.colorSpace == ColorSpace.Linear, PlayerSettings.colorSpace.ToString());
            var m6ReportPath = Absolute("../TestArtifacts/M6/M6ValidationReport.json");
            Add("M6GateReport", File.Exists(m6ReportPath) && File.ReadAllText(m6ReportPath).Contains("\"failureCount\": 0"), m6ReportPath);
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(SandroneM7Bootstrap.ShaderPath);
            Add("ShaderPresentSupported", shader != null && shader.isSupported, shader?.name ?? "missing");
            if (shader != null)
            {
                var messages = ShaderUtil.GetShaderMessages(shader); report.shaderCompilerMessageCount = messages.Length;
                Add("ShaderCompilerMessages", messages.Length == 0, string.Join(" | ", messages.Select(x => x.message)));
            }
            var sourceText = File.Exists(Absolute(SandroneM7Bootstrap.ShaderPath)) ? File.ReadAllText(Absolute(SandroneM7Bootstrap.ShaderPath)) : "";
            Add("OutlinePass", sourceText.Contains("Name \"M7Outline\"") && sourceText.Contains("\"LightMode\"=\"SRPDefaultUnlit\""), "M7Outline/SRPDefaultUnlit");
            Add("InvertedHullState", sourceText.Contains("Cull Front") && sourceText.Contains("ZWrite Off") && sourceText.Contains("ZTest Less"), "Cull Front, ZWrite Off, strict Less after opaque body");
            Add("PixelSpaceWidth", sourceText.Contains("2.0 / _ScaledScreenParams.xy") && sourceText.Contains("positionCS.w"), "clip/NDC pixel correction");
            Add("GrazingClamp", sourceText.Contains("max(normalLength, 0.05)"), "normal xy clamp");
            Add("NoM8M9Features", !sourceText.Contains("Emission") && !sourceText.Contains("Bloom") && !sourceText.Contains("RendererFeature"), "outline only");

            var profile = AssetDatabase.LoadAssetAtPath<SandroneM7OutlineProfile>(SandroneM7Bootstrap.ProfilePath);
            Add("ProfileContract", profile != null && profile.ContractVersion == "SandroneOutlineProfile_v1_M7", profile?.ContractVersion ?? "missing");
            Add("ProfileNormalSource", profile != null && profile.NormalSource == "GeneratedCoincidentPositionAverage_v1", profile?.NormalSource ?? "missing");
            Add("ProfileSlots", profile != null && profile.Slots.Select(x => x.materialIndex).OrderBy(x => x)
                .SequenceEqual(SandroneM7Bootstrap.EligibleSlots.OrderBy(x => x)), profile == null ? "missing" : string.Join(",", profile.Slots.Select(x => x.materialIndex)));
            if (profile != null)
                foreach (var slot in profile.Slots)
                    Add($"ProfileSlot{slot.materialIndex:00}", slot.widthPixels >= .6f && slot.widthPixels <= 1.5f && slot.color.maxColorComponent < .4f,
                        $"width={slot.widthPixels:F2}, color={slot.color}");

            var scene = EditorSceneManager.OpenScene(SandroneM7Bootstrap.ScenePath, OpenSceneMode.Single);
            Add("SceneOpened", scene.IsValid(), scene.path);
            var m7 = UnityEngine.Object.FindFirstObjectByType<SandroneM7OutlineController>();
            var m6 = UnityEngine.Object.FindFirstObjectByType<SandroneM6Controller>();
            var m5 = UnityEngine.Object.FindFirstObjectByType<SandroneM5Controller>();
            var m0 = UnityEngine.Object.FindFirstObjectByType<SandroneM0Controller>();
            Add("ControllerChain", m0 != null && m5 != null && m6 != null && m7 != null, "M0+M5+M6+M7");
            var sourceRenderer = m7?.SourceRenderer;
            var outlineRenderer = m7?.OutlineRenderer;
            Add("SeparateOutlineRenderer", sourceRenderer != null && outlineRenderer != null && sourceRenderer != outlineRenderer, outlineRenderer?.name ?? "missing");
            Add("OutlineNoShadows", outlineRenderer != null && outlineRenderer.shadowCastingMode == ShadowCastingMode.Off && !outlineRenderer.receiveShadows,
                outlineRenderer == null ? "missing" : $"cast={outlineRenderer.shadowCastingMode}, receive={outlineRenderer.receiveShadows}");
            Add("OutlineDefaultState", m7 != null && m7.OutlineEnabled && Mathf.Approximately(m7.MasterWidth, 1f) && m7.DebugMode == SandroneM7DebugMode.FinalColor, "enabled/final/1x");

            if (sourceRenderer != null && outlineRenderer != null)
            {
                var sourceMesh = sourceRenderer.sharedMesh; var outlineMesh = outlineRenderer.sharedMesh;
                Add("SourceMeshStillFBX", AssetDatabase.GetAssetPath(sourceMesh) == SandroneM0Bootstrap.ModelPath, AssetDatabase.GetAssetPath(sourceMesh));
                Add("GeneratedOutlineMesh", AssetDatabase.GetAssetPath(outlineMesh) == SandroneM7Bootstrap.MeshPath, AssetDatabase.GetAssetPath(outlineMesh));
                Add("MeshVertexParity", sourceMesh.vertexCount == outlineMesh.vertexCount, $"{sourceMesh.vertexCount}/{outlineMesh.vertexCount}");
                Add("CompressedOutlineSubmeshes", sourceMesh.subMeshCount == 31 && outlineMesh.subMeshCount == SandroneM7Bootstrap.EligibleSlots.Length,
                    $"source={sourceMesh.subMeshCount}, outline={outlineMesh.subMeshCount}");
                Add("BlendShapeParity", sourceMesh.blendShapeCount == outlineMesh.blendShapeCount, $"{sourceMesh.blendShapeCount}/{outlineMesh.blendShapeCount}");
                Add("BindposeParity", sourceMesh.bindposes.Length == outlineMesh.bindposes.Length, $"{sourceMesh.bindposes.Length}/{outlineMesh.bindposes.Length}");
                report.sourceNormalAudit = SandroneM7Bootstrap.AnalyzeSourceNormals(sourceMesh);
                report.outlineNormalAudit = SandroneM7Bootstrap.AnalyzeSourceNormals(outlineMesh);
                Add("SourceHasNoOutlineVertexColor", report.sourceNormalAudit.sourceColorCount == 0, $"colors={report.sourceNormalAudit.sourceColorCount}");
                Add("SourceNormalDiscontinuitiesMeasured", report.sourceNormalAudit.discontinuousGroupCount > 0,
                    $"groups={report.sourceNormalAudit.coincidentGroupCount}, discontinuous={report.sourceNormalAudit.discontinuousGroupCount}, max={report.sourceNormalAudit.maxAngularDifferenceDegrees:F1}");
                Add("OutlineNormalsSmoothed", report.outlineNormalAudit.discontinuousGroupCount < report.sourceNormalAudit.discontinuousGroupCount,
                    $"source={report.sourceNormalAudit.discontinuousGroupCount}, outline={report.outlineNormalAudit.discontinuousGroupCount}");
                Add("GeneratedWidthFallback", outlineMesh.colors32.Length == outlineMesh.vertexCount && outlineMesh.colors32.All(x => x.a == 255),
                    $"colors={outlineMesh.colors32.Length}");
                for (var outlineIndex = 0; outlineIndex < SandroneM7Bootstrap.EligibleSlots.Length; outlineIndex++)
                {
                    var sourceSlot = SandroneM7Bootstrap.EligibleSlots[outlineIndex];
                    var expected = sourceMesh.GetIndexCount(sourceSlot);
                    Add($"OutlineSubmesh{outlineIndex:00}_Source{sourceSlot:00}", outlineMesh.GetIndexCount(outlineIndex) == expected,
                        $"indices={outlineMesh.GetIndexCount(outlineIndex)}, expected={expected}");
                }
                var map = AssetDatabase.LoadAssetAtPath<SandroneMaterialMap>(SandroneM0Bootstrap.MaterialMapPath);
                for (var slot = 0; slot < 31; slot++)
                {
                    var entry = map.Entries.First(x => x.sourceIndex == slot);
                    var expectedSource = SandroneM6Bootstrap.TargetSlots.Contains(slot)
                        ? AssetDatabase.LoadAssetAtPath<Material>(SandroneM6Bootstrap.MaterialPath(slot, entry.materialAssetPath))
                        : SandroneM6Bootstrap.BaselineMaterial(slot, entry.materialAssetPath);
                    Add($"SourceMaterial{slot:00}", sourceRenderer.sharedMaterials[slot] == expectedSource, AssetDatabase.GetAssetPath(sourceRenderer.sharedMaterials[slot]));
                }
                Add("OutlineMaterialCount", outlineRenderer.sharedMaterials.Length == SandroneM7Bootstrap.EligibleSlots.Length,
                    outlineRenderer.sharedMaterials.Length.ToString());
                for (var outlineIndex = 0; outlineIndex < SandroneM7Bootstrap.EligibleSlots.Length; outlineIndex++)
                {
                    var sourceSlot = SandroneM7Bootstrap.EligibleSlots[outlineIndex];
                    var actualPath = AssetDatabase.GetAssetPath(outlineRenderer.sharedMaterials[outlineIndex]);
                    Add($"OutlineMaterial{outlineIndex:00}_Source{sourceSlot:00}", actualPath == SandroneM7Bootstrap.MaterialPath(sourceSlot), actualPath);
                }
            }

            var required = new[]
            {
                "ReferenceComparison/M7_FinalToon_Front.png", "ReferenceComparison/M7_Head_ThreeQuarter.png", "ReferenceComparison/M7_Head_Side.png",
                "AB/M7_OutlineOff.png", "AB/M7_OutlineOn.png", "AB/M7_OriginalNormals.png", "AB/M7_SmoothedNormals.png",
                "Debug/M7_FinalColor.png", "Debug/M7_WidthWeight.png", "Debug/M7_OutlineNormal.png", "Debug/M7_MaterialSlot.png",
                "Scale/M7_Near_Off.png", "Scale/M7_Near_On.png", "Scale/M7_Mid_Off.png", "Scale/M7_Mid_On.png",
                "Scale/M7_Far_Off.png", "Scale/M7_Far_On.png", "Pipeline/M7_PC_ForwardPlus.png", "Pipeline/M7_Mobile_Forward.png"
            };
            foreach (var relative in required) Add("Capture_" + Path.GetFileNameWithoutExtension(relative), File.Exists(Artifact(relative)), relative);
            if (required.All(x => File.Exists(Artifact(x)))) ValidateImages(report, Add);

            report.warnings.Add("标准 FBX 没有 Outline Normal/顶点色宽度契约；当前派生 Mesh 使用同位置+骨索引分组平滑法线和白色 A=1 回退，正式 DCC 数据替换后须重测。");
            report.warnings.Add($"M7 增加 {SandroneM7Bootstrap.EligibleSlots.Length} 个角色 Outline draw；GPU 时间、SRP Batcher 和物理移动设备仍未测。");
            report.warnings.Add("透明眼层、皮肤/袜/发叠层、腮红以及已知重叠内裙片不提交 Outline；需要局部内描线时应由贴图或后续独立方案提供。");
            report.warningCount = report.warnings.Count;
            report.checkCount = report.checks.Count; report.failureCount = report.failures.Count;
            Directory.CreateDirectory(Path.GetDirectoryName(ReportPath())!);
            File.WriteAllText(ReportPath(), JsonUtility.ToJson(report, true));
            AssetDatabase.Refresh();
            if (report.failureCount > 0) throw new BuildFailedException("M7 validation failed: " + string.Join("; ", report.failures));
            Debug.Log($"[Sandrone M7] Validation passed: {report.checkCount}/{report.checkCount}, warnings={report.warningCount}.");
        }

        private static void ValidateImages(Report report, Action<string,bool,string> add)
        {
            var off = Load("AB/M7_OutlineOff.png"); var on = Load("AB/M7_OutlineOn.png");
            report.outlineOnOffMae = Mae(off, on); report.outlineChangedPixels = Changed(off, on, 3);
            report.outsideChangedRatio = OutsideChangedRatio(off, on, 3);
            add("OutlineVisible", report.outlineOnOffMae > .05f && report.outlineChangedPixels > 1000,
                $"MAE={report.outlineOnOffMae:F4}, changed={report.outlineChangedPixels}");
            add("ExternalOutlinePresent", report.outsideChangedRatio > .25f && report.outsideChangedRatio < .75f,
                $"outside={report.outsideChangedRatio:F4}; remaining changes are valid inter-part silhouettes");
            var m6Path = Absolute("../TestArtifacts/M6/ReferenceComparison/M6_FinalToon_Front.png");
            if (File.Exists(m6Path))
            {
                var m6 = LoadAbsolute(m6Path); report.m6BaselineMae = Mae(off, m6);
                add("OutlineOffPreservesM6", report.m6BaselineMae < .5f, $"MAE={report.m6BaselineMae:F4}");
            }
            report.originalSmoothedMae = Mae(Load("AB/M7_OriginalNormals.png"), Load("AB/M7_SmoothedNormals.png"));
            add("NormalComparisonCaptured", !float.IsInfinity(report.originalSmoothedMae), $"MAE={report.originalSmoothedMae:F5}; seam reduction is validated from mesh data");
            report.pcMobileMae = Mae(Load("Pipeline/M7_PC_ForwardPlus.png"), Load("Pipeline/M7_Mobile_Forward.png"));
            add("PCMobileCompatible", report.pcMobileMae < 5f, $"MAE={report.pcMobileMae:F4}");
            report.nearThickness = Thickness("Scale/M7_Near_Off.png", "Scale/M7_Near_On.png");
            report.midThickness = Thickness("Scale/M7_Mid_Off.png", "Scale/M7_Mid_On.png");
            report.farThickness = Thickness("Scale/M7_Far_Off.png", "Scale/M7_Far_On.png");
            var min = Mathf.Min(report.nearThickness, Mathf.Min(report.midThickness, report.farThickness));
            var max = Mathf.Max(report.nearThickness, Mathf.Max(report.midThickness, report.farThickness));
            add("PixelWidthScaleStable", min > .05f && max / min < 2.5f,
                $"near/mid/far={report.nearThickness:F3}/{report.midThickness:F3}/{report.farThickness:F3}");
            report.redSkirtPixelsOff = RedPixels(off); report.redSkirtPixelsOn = RedPixels(on);
            var redRatio = report.redSkirtPixelsOff == 0 ? 0f : (float)report.redSkirtPixelsOn / report.redSkirtPixelsOff;
            add("RedSkirtPreserved", redRatio > .94f && redRatio < 1.05f,
                $"off/on={report.redSkirtPixelsOff}/{report.redSkirtPixelsOn}, ratio={redRatio:F4}");
            var debug = new[] { "Debug/M7_FinalColor.png", "Debug/M7_WidthWeight.png", "Debug/M7_OutlineNormal.png", "Debug/M7_MaterialSlot.png" }.Select(Load).ToArray();
            for (var i = 1; i < debug.Length; i++) add($"DebugMode{i}Distinct", Mae(debug[0], debug[i]) > .001f, $"MAE={Mae(debug[0], debug[i]):F5}");
        }

        private static float Thickness(string offPath, string onPath)
        {
            var off = Load(offPath); var on = Load(onPath); var width = off.width; var height = off.height;
            var a = off.GetPixels32(); var b = on.GetPixels32(); var background = a[0]; var changed = 0; var boundary = 0;
            bool Foreground(int i) => Difference(a[i], background) > 12;
            for (var y = 1; y < height - 1; y++) for (var x = 1; x < width - 1; x++)
            {
                var i = y * width + x; if (Difference(a[i], b[i]) > 3) changed++;
                if (!Foreground(i)) continue;
                if (!Foreground(i-1) || !Foreground(i+1) || !Foreground(i-width) || !Foreground(i+width)) boundary++;
            }
            return boundary == 0 ? 0f : (float)changed / boundary;
        }

        private static float OutsideChangedRatio(Texture2D off, Texture2D on, int threshold)
        {
            var a = off.GetPixels32(); var b = on.GetPixels32(); var bg = a[0]; var outside = 0; var changed = 0;
            for (var i = 0; i < a.Length; i++) if (Difference(a[i], b[i]) > threshold)
            { changed++; if (Difference(a[i], bg) <= 12) outside++; }
            return changed == 0 ? 0f : (float)outside / changed;
        }
        private static int RedPixels(Texture2D image) => image.GetPixels32().Count(p => p.r > 65 && p.r > p.g * 1.35f && p.r > p.b * 1.2f);
        private static int Changed(Texture2D a, Texture2D b, int threshold) { var x=a.GetPixels32();var y=b.GetPixels32();var count=0;for(var i=0;i<x.Length;i++)if(Difference(x[i],y[i])>threshold)count++;return count; }
        private static int Difference(Color32 a, Color32 b) => Mathf.Max(Mathf.Abs(a.r-b.r), Mathf.Max(Mathf.Abs(a.g-b.g), Mathf.Abs(a.b-b.b)));
        private static float Mae(Texture2D a, Texture2D b)
        {
            if (a.width != b.width || a.height != b.height) return float.PositiveInfinity;
            var x=a.GetPixels32();var y=b.GetPixels32();double sum=0;for(var i=0;i<x.Length;i++)sum+=Math.Abs(x[i].r-y[i].r)+Math.Abs(x[i].g-y[i].g)+Math.Abs(x[i].b-y[i].b);return (float)(sum/(x.Length*3.0));
        }
        private static Texture2D Load(string relative) => LoadAbsolute(Artifact(relative));
        private static Texture2D LoadAbsolute(string path) { var image=new Texture2D(2,2,TextureFormat.RGB24,false);image.LoadImage(File.ReadAllBytes(path),false);return image; }
        private static string Artifact(string relative) => Absolute("../TestArtifacts/M7/" + relative);
        private static string ReportPath() => Absolute("../TestArtifacts/M7/M7ValidationReport.json");
        private static string Absolute(string projectRelative) => Path.GetFullPath(Path.Combine(Application.dataPath, projectRelative.StartsWith("Assets/") ? ".." : "", projectRelative));
    }
}
