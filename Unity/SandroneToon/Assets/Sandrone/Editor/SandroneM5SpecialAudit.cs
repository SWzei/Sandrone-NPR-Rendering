using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace SandroneToon.Editor
{
    public static class SandroneM5SpecialAudit
    {
        [Serializable] public sealed class SlotResult { public int slot; public string material, comparisonBasis; public float targetMae; public int maskPixels; public bool passed; }
        [Serializable] public sealed class SweepResult { public int degrees; public float lightTransformDot; public float litFraction; public float adjacentMae; public float boundaryCentroidX = -1; public int boundaryPixels; }
        [Serializable] public sealed class DebugResult { public int mode; public string name; public bool semanticPassed; public string details; }
        [Serializable] public sealed class Report
        {
            public string generatedUtc, unityVersion, graphicsApi, graphicsDevice;
            public float nonFaceMae, faceMae, maxNonFaceSlotMae, maxSkirtSlotMae, mirrorMinusPlusMae, wrap359To0Mae, maxBoundaryStepPixels;
            public int newNearBlackPixels, largestNewNearBlackComponent, failedSlotCount, failedDebugCount;
            public bool lightTransformSynchronized, sweepContinuous, mirrorCrossingContinuous, noNewBlackRegions;
            public List<SlotResult> slots = new(); public List<SweepResult> sweep = new(); public List<DebugResult> debugModes = new(); public List<string> failures = new();
        }

        public static void Run()
        {
            EditorSceneManager.OpenScene(SandroneM5Bootstrap.ScenePath, OpenSceneMode.Single);
            var controller = UnityEngine.Object.FindFirstObjectByType<SandroneM5Controller>();
            var camera = UnityEngine.Object.FindFirstObjectByType<Camera>();
            var renderer = controller?.TargetRenderer as SkinnedMeshRenderer;
            if (controller == null || camera == null || renderer == null) throw new InvalidOperationException("M5 audit scene incomplete.");
            var report = new Report { generatedUtc = DateTime.UtcNow.ToString("O"), unityVersion = Application.unityVersion,
                graphicsApi = SystemInfo.graphicsDeviceType.ToString(), graphicsDevice = SystemInfo.graphicsDeviceName };
            var original = renderer.sharedMaterials; var m4 = LoadM4Materials();
            const int width = 768, height = 1280;
            try
            {
                ConfigureFull(camera); controller.CharacterRoot.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                controller.SetLightDirectionToSource(SandroneM3Bootstrap.DefaultDirectionToLight);
                renderer.sharedMaterials = m4; Capture(camera, "Regression/M4_SameScene.png", width, height);
                renderer.sharedMaterials = original; ResetController(controller); Capture(camera, "Regression/M5_SameScene.png", width, height);
                var a = Read(Artifact("Regression/M4_SameScene.png")); var b = Read(Artifact("Regression/M5_SameScene.png"));
                var unionFace = new bool[a.pixels.Length];
                for (var slot = 0; slot < 31; slot++)
                {
                    var maskPath = $"Regression/SlotMasks/M5_Slot{slot:00}.png"; CaptureSlotMask(camera, controller, slot, maskPath, width, height);
                    var maskImage = Read(Artifact(maskPath)); var mask = Mask(maskImage); if (slot <= 1) for (var i = 0; i < mask.Length; i++) unionFace[i] |= mask[i];
                    // Transparent overlays share pixels with opaque slots underneath. Comparing the final frame on
                    // their geometry mask attributes a legal slot-0/1 Face SDF change to the unchanged overlay.
                    // Compare the overlay's isolated contribution instead; keep the full-frame non-face gate below.
                    var transparent = original[slot].renderQueue >= (int)RenderQueue.Transparent;
                    var mae = transparent
                        ? CaptureIsolatedSlotMae(camera, controller, m4, original, slot, mask, width, height)
                        : MaskedMae(a.pixels, b.pixels, mask);
                    var entry = new SlotResult { slot = slot, material = original[slot].name,
                        comparisonBasis = transparent ? "isolated transparent contribution" : "visible final-frame pixels",
                        targetMae = mae, maskPixels = mask.Count(x => x), passed = slot <= 1 || mae < 0.5f };
                    report.slots.Add(entry); if (!entry.passed) report.failures.Add($"Non-face slot {slot} MAE={mae:F3}");
                }
                report.faceMae = MaskedMae(a.pixels, b.pixels, unionFace); report.nonFaceMae = MaskedMae(a.pixels, b.pixels, unionFace.Select(x => !x).ToArray());
                report.maxNonFaceSlotMae = report.slots.Where(x => x.slot > 1).Max(x => x.targetMae);
                report.maxSkirtSlotMae = report.slots.Where(x => x.slot >= 20 && x.slot <= 27).Max(x => x.targetMae);
                report.failedSlotCount = report.slots.Count(x => !x.passed);
                FindNewNearBlack(a, b, unionFace, out report.newNearBlackPixels, out report.largestNewNearBlackComponent);
                report.noNewBlackRegions = report.newNearBlackPixels < 256 && report.largestNewNearBlackComponent < 64;
                if (report.nonFaceMae >= 0.5f) report.failures.Add($"Non-face full image MAE={report.nonFaceMae:F3}");
                if (!report.noNewBlackRegions) report.failures.Add($"New near-black pixels={report.newNearBlackPixels}, largest component={report.largestNewNearBlackComponent}");

                RunSweep(controller, camera, report);
                ValidateDebugSemantics(report);
                report.failedDebugCount = report.debugModes.Count(x => !x.semanticPassed);
                if (report.failedDebugCount > 0) report.failures.Add($"Debug semantic failures={report.failedDebugCount}");
            }
            finally
            {
                renderer.sharedMaterials = original; ResetController(controller); controller.SetLightDirectionToSource(SandroneM3Bootstrap.DefaultDirectionToLight);
                foreach (var material in m4) { /* assets, do not destroy */ }
            }
            Directory.CreateDirectory(Artifact("")); File.WriteAllText(Artifact("M5SpecialAuditReport.json"), JsonUtility.ToJson(report, true));
            if (report.failures.Count > 0) throw new InvalidOperationException("M5 special audit failed: " + string.Join("; ", report.failures));
            Debug.Log($"[Sandrone M5 Special Audit] Passed: non-face={report.nonFaceMae:F3}, max slot={report.maxNonFaceSlotMae:F3}, mirror={report.mirrorMinusPlusMae:F3}, debug=17/17.");
        }

        private static Material[] LoadM4Materials()
        {
            var map = AssetDatabase.LoadAssetAtPath<SandroneMaterialMap>(SandroneM0Bootstrap.MaterialMapPath); var result = new Material[31];
            foreach (var entry in map.Entries) result[entry.sourceIndex] = AssetDatabase.LoadAssetAtPath<Material>(SandroneM4Bootstrap.MaterialPath(entry.sourceIndex, entry.materialAssetPath));
            if (result.Any(x => x == null)) throw new InvalidOperationException("M4 material set incomplete."); return result;
        }

        private static void RunSweep(SandroneM5Controller c, Camera camera, Report report)
        {
            ConfigureFace(camera, c.Head.position); c.CharacterRoot.rotation = Quaternion.identity; c.FaceSdfEnabled = true; c.DebugMode = SandroneM5DebugMode.FaceLitMask;
            Image previous = null; float previousCentroid = -1; var maxTransformError = 0f;
            for (var degrees = 0; degrees < 360; degrees += 20)
            {
                var direction = Direction(degrees); c.SetLightDirectionToSource(direction); c.Apply(true);
                var actual = -c.MainLight.transform.forward; var dot = Vector3.Dot(actual.normalized, direction); maxTransformError = Mathf.Max(maxTransformError, 1f - dot);
                var relative = $"Sweep20/FaceLit_{degrees:000}.png"; Capture(camera, relative, 512, 512); var image = Read(Artifact(relative));
                var mask = ForegroundMask(image); var lit = 0; var boundary = 0; double centroid = 0;
                for (var i = 0; i < image.pixels.Length; i++) if (mask[i])
                {
                    var v = image.pixels[i].r; if (v >= 128) lit++; if (v > 12 && v < 243) { boundary++; centroid += i % image.width; }
                }
                var result = new SweepResult { degrees = degrees, lightTransformDot = dot, litFraction = mask.Count(x => x) == 0 ? 0 : (float)lit / mask.Count(x => x),
                    adjacentMae = previous == null ? 0 : MaskedMae(previous.pixels, image.pixels, mask), boundaryPixels = boundary, boundaryCentroidX = boundary == 0 ? -1 : (float)(centroid / boundary) };
                if (previousCentroid >= 0 && result.boundaryCentroidX >= 0) report.maxBoundaryStepPixels = Mathf.Max(report.maxBoundaryStepPixels, Mathf.Abs(result.boundaryCentroidX - previousCentroid));
                previousCentroid = result.boundaryCentroidX; report.sweep.Add(result); previous = image;
            }
            report.lightTransformSynchronized = maxTransformError < 1e-5f;
            report.sweepContinuous = report.maxBoundaryStepPixels < 96f && report.sweep.Count(x => x.boundaryPixels > 20) >= 6;
            if (!report.lightTransformSynchronized) report.failures.Add($"Directional light Transform mismatch error={maxTransformError}");
            if (!report.sweepContinuous) report.failures.Add($"20-degree sweep discontinuity: max boundary step={report.maxBoundaryStepPixels:F2}");

            c.DebugMode = SandroneM5DebugMode.FaceSDF;
            var cross = new Dictionary<int, Image>();
            foreach (var degrees in new[] { 359, 0, 1 })
            {
                c.SetLightDirectionToSource(Direction(degrees)); c.Apply(true); var relative = $"Sweep20/FaceSDF_Cross_{degrees:000}.png"; Capture(camera, relative, 512, 512); cross[degrees] = Read(Artifact(relative));
            }
            var crossMask = ForegroundMask(cross[0]); report.mirrorMinusPlusMae = MaskedMae(cross[359].pixels, cross[1].pixels, crossMask);
            report.wrap359To0Mae = MaskedMae(cross[359].pixels, cross[0].pixels, crossMask);
            report.mirrorCrossingContinuous = report.mirrorMinusPlusMae < 15f && report.wrap359To0Mae < 10f;
            if (!report.mirrorCrossingContinuous) report.failures.Add($"HeadRight sign crossing popped: -1/+1={report.mirrorMinusPlusMae:F3}, 359/0={report.wrap359To0Mae:F3}");
        }

        private static void ValidateDebugSemantics(Report report)
        {
            foreach (SandroneM5DebugMode mode in Enum.GetValues(typeof(SandroneM5DebugMode)))
            {
                var image = Read(Path.GetFullPath(Path.Combine(Application.dataPath, $"../TestArtifacts/M5/Debug/M5_{mode}.png"))); var px = image.pixels.Where(p => p.r + p.g + p.b > 12).ToArray();
                var pass = px.Length > 100; var detail = $"pixels={px.Length}";
                bool Gray(float tolerance = 2f) => px.Length > 0 && px.Average(p => Math.Abs(p.r - p.g) + Math.Abs(p.g - p.b)) <= tolerance;
                switch (mode)
                {
                    case SandroneM5DebugMode.ControlR: pass &= px.Average(p => p.g + p.b) < 3; detail += "; red-only"; break;
                    case SandroneM5DebugMode.ControlG: pass &= px.Average(p => p.r + p.b) < 3; detail += "; green-only"; break;
                    case SandroneM5DebugMode.ControlB: pass &= px.Average(p => p.r + p.g) < 3; detail += "; blue-only"; break;
                    case SandroneM5DebugMode.ControlA: pass = image.pixels.All(p => p.r + p.g + p.b < 4); detail += "; reserved A=0 gives black"; break;
                    case SandroneM5DebugMode.NDotH:
                    case SandroneM5DebugMode.Specular:
                    case SandroneM5DebugMode.FinalLitMask:
                    case SandroneM5DebugMode.Silhouette:
                    case SandroneM5DebugMode.FaceSDF:
                    case SandroneM5DebugMode.FaceThreshold:
                    case SandroneM5DebugMode.FaceLitMask:
                    case SandroneM5DebugMode.FaceVsLambert: pass &= Gray(); detail += "; scalar grayscale"; break;
                    case SandroneM5DebugMode.MatCapUV: pass &= px.Average(p => p.b) < 3 && px.Select(p => p.r).Distinct().Count() > 16 && px.Select(p => p.g).Distinct().Count() > 16; detail += "; RG varying,B=0"; break;
                    case SandroneM5DebugMode.MatCapSample:
                    case SandroneM5DebugMode.MaterialResponse:
                    case SandroneM5DebugMode.HeadLightAxes: pass &= !Gray(1f); detail += "; encoded RGB"; break;
                    case SandroneM5DebugMode.FinalToon: pass &= !Gray(1f); detail += "; final color"; break;
                }
                report.debugModes.Add(new DebugResult { mode = (int)mode, name = mode.ToString(), semanticPassed = pass, details = detail });
            }
        }

        private static Vector3 Direction(int degrees) { var r = degrees * Mathf.Deg2Rad; return new Vector3(Mathf.Sin(r), 0.28f, Mathf.Cos(r)).normalized; }
        private static void ResetController(SandroneM5Controller c) { c.DebugMode = SandroneM5DebugMode.FinalToon; c.FaceSdfEnabled = true; c.MetalEnabled = true; c.StockingEnabled = true; c.HairOverlayEnabled = true; c.ClearMaterialSlotFeatureWeights(); c.Apply(true); }
        private static void ConfigureFull(Camera camera) { camera.orthographic = true; camera.transform.position = new Vector3(0, .82f, 4); camera.transform.rotation = Quaternion.LookRotation(Vector3.back, Vector3.up); camera.orthographicSize = .92f; camera.backgroundColor = new Color(.153f, .149f, .149f, 1); }
        private static void ConfigureFace(Camera camera, Vector3 target) { camera.orthographic = true; camera.transform.position = target + new Vector3(0, 0, 4); camera.transform.rotation = Quaternion.LookRotation(Vector3.back, Vector3.up); camera.orthographicSize = .30f; camera.backgroundColor = Color.black; }
        private static void CaptureSlotMask(Camera camera, SandroneM5Controller c, int slot, string relative, int width, int height)
        {
            var isolation = AssetDatabase.LoadAssetAtPath<Shader>(SandroneM4Bootstrap.IsolationShaderPath); var original = c.TargetRenderer.sharedMaterials; var materials = (Material[])original.Clone();
            var marker = new Material(isolation); marker.SetColor("_Color", Color.magenta); materials[slot] = marker; c.TargetRenderer.sharedMaterials = materials;
            var temporary = relative + ".marker.png";
            try
            {
                ResetController(c); ConfigureFull(camera); Capture(camera, temporary, width, height); var marked = Read(Artifact(temporary));
                var mask = new Texture2D(width, height, TextureFormat.RGB24, false); var pixels = new Color32[marked.pixels.Length];
                try { for (var i = 0; i < pixels.Length; i++) pixels[i] = marked.pixels[i].r > 220 && marked.pixels[i].b > 220 && marked.pixels[i].g < 80 ? Color.white : Color.black; mask.SetPixels32(pixels); mask.Apply(); File.WriteAllBytes(Artifact(relative), mask.EncodeToPNG()); }
                finally { UnityEngine.Object.DestroyImmediate(mask); }
            }
            finally { c.TargetRenderer.sharedMaterials = original; UnityEngine.Object.DestroyImmediate(marker); if (File.Exists(Artifact(temporary))) File.Delete(Artifact(temporary)); ResetController(c); ConfigureFull(camera); }
        }
        private static float CaptureIsolatedSlotMae(Camera camera, SandroneM5Controller c, Material[] m4, Material[] m5, int slot, bool[] visibleMask, int width, int height)
        {
            var isolation = AssetDatabase.LoadAssetAtPath<Shader>(SandroneM4Bootstrap.IsolationShaderPath);
            var hidden = new Material(isolation); hidden.SetColor("_Color", new Color(0, 0, 0, 0));
            var materials = Enumerable.Repeat(hidden, m5.Length).ToArray();
            try
            {
                materials[slot] = m4[slot]; c.TargetRenderer.sharedMaterials = materials; ResetController(c); ConfigureFull(camera);
                Capture(camera, $"Regression/SlotContributions/M4_Slot{slot:00}.png", width, height);
                materials[slot] = m5[slot]; c.TargetRenderer.sharedMaterials = materials; ResetController(c); ConfigureFull(camera);
                Capture(camera, $"Regression/SlotContributions/M5_Slot{slot:00}.png", width, height);
                var a = Read(Artifact($"Regression/SlotContributions/M4_Slot{slot:00}.png"));
                var b = Read(Artifact($"Regression/SlotContributions/M5_Slot{slot:00}.png"));
                return MaskedMae(a.pixels, b.pixels, visibleMask);
            }
            finally { c.TargetRenderer.sharedMaterials = m5; ResetController(c); ConfigureFull(camera); UnityEngine.Object.DestroyImmediate(hidden); }
        }
        private static void Capture(Camera camera, string relative, int width, int height)
        {
            var path = Artifact(relative); Directory.CreateDirectory(Path.GetDirectoryName(path)!); var active = RenderTexture.active; var rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB); var image = new Texture2D(width, height, TextureFormat.RGB24, false);
            try { camera.targetTexture = rt; rt.Create(); camera.Render(); RenderTexture.active = rt; image.ReadPixels(new Rect(0, 0, width, height), 0, 0); image.Apply(); File.WriteAllBytes(path, image.EncodeToPNG()); }
            finally { camera.targetTexture = null; RenderTexture.active = active; UnityEngine.Object.DestroyImmediate(image); rt.Release(); UnityEngine.Object.DestroyImmediate(rt); }
        }
        private sealed class Image { public int width, height; public Color32[] pixels; }
        private static Image Read(string path) { var t = new Texture2D(2, 2); try { if (!t.LoadImage(File.ReadAllBytes(path))) throw new InvalidOperationException(path); return new Image { width = t.width, height = t.height, pixels = t.GetPixels32() }; } finally { UnityEngine.Object.DestroyImmediate(t); } }
        private static bool[] Mask(Image image) => image.pixels.Select(p => p.r + p.g + p.b > 30).ToArray();
        private static bool[] ForegroundMask(Image image) => image.pixels.Select(p => p.r + p.g + p.b > 12).ToArray();
        private static float MaskedMae(Color32[] a, Color32[] b, bool[] mask) { double sum = 0; var n = 0; for (var i = 0; i < a.Length; i++) if (mask[i]) { sum += (Math.Abs(a[i].r - b[i].r) + Math.Abs(a[i].g - b[i].g) + Math.Abs(a[i].b - b[i].b)) / 3.0; n++; } return n == 0 ? 0 : (float)(sum / n); }
        private static void FindNewNearBlack(Image a, Image b, bool[] excluded, out int count, out int largest)
        {
            var bad = new bool[a.pixels.Length]; count = 0; for (var i = 0; i < bad.Length; i++) { var al = a.pixels[i].r + a.pixels[i].g + a.pixels[i].b; var bl = b.pixels[i].r + b.pixels[i].g + b.pixels[i].b; bad[i] = !excluded[i] && bl < 36 && al >= 36; if (bad[i]) count++; }
            largest = 0; var visited = new bool[bad.Length]; var q = new Queue<int>(); for (var i = 0; i < bad.Length; i++) if (bad[i] && !visited[i]) { visited[i] = true; q.Enqueue(i); var size = 0; while (q.Count > 0) { var p = q.Dequeue(); size++; var x = p % a.width; foreach (var n in new[] { p - 1, p + 1, p - a.width, p + a.width }) if (n >= 0 && n < bad.Length && !visited[n] && bad[n] && Math.Abs(n % a.width - x) <= 1) { visited[n] = true; q.Enqueue(n); } } largest = Math.Max(largest, size); }
        }
        private static string Artifact(string relative) => Path.GetFullPath(Path.Combine(Application.dataPath, "../TestArtifacts/M5SpecialAudit", relative));
    }
}
