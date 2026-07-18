using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace SandroneToon.Editor
{
    [InitializeOnLoad]
    public static class SandroneM6GameViewAudit
    {
        private const string StageKey = "Sandrone.M6GameView.Stage";
        private const int Width = 768, Height = 1680;
        private static int ticks, eventCursor;
        private static List<int> eventIndices;
        private static Report pending;

        [Serializable] public sealed class SlotRecord
        {
            public int slot;
            public bool exactExpectedMaterial, exactExpectedBaseMap, mpbTextureBindingOverride;
            public string material, materialPath, materialGuid, expectedPath, expectedGuid, shader, baseMap, baseMapPath, baseMapGuid;
        }
        [Serializable] public sealed class EventRecord
        {
            public int index, mappedSlot = -1, stencilRef, zWrite;
            public float auditSlotId, role, eyeFlatLighting, hairSpecIntensity, layerWeight;
            public string eventName, renderer, material, shader, pass, renderTarget, cull, zTest, srcBlend, dstBlend, baseMap, controlMap, ramp, faceMap, matCap, candidateSlots;
        }
        [Serializable] public sealed class Report
        {
            public string generatedUtc, unityVersion, graphicsApi, graphicsDevice, scene;
            public int width = Width, height = Height, frameEventCount, m5ForwardCount, m6ForwardCount, m4ForwardCount, shadowCasterCount, receiverCount, redSkirtPixels;
            public float m5M6Mae, hairToggleMae, pcMobileMae;
            public bool enteredPlayMode, exitedPlayMode, sceneReopenedAfterPlay, controllerStateRestored;
            public List<SlotRecord> slots = new();
            public List<EventRecord> events = new();
            public List<string> captures = new();
            public List<string> failures = new();
        }

        static SandroneM6GameViewAudit()
        {
            EditorApplication.update -= Update;
            EditorApplication.update += Update;
        }

        public static void RunFromCommandLine()
        {
            if (Application.isBatchMode) throw new InvalidOperationException("M6 Game View/Frame Debugger audit requires a visible Editor.");
            Directory.CreateDirectory(Root());
            foreach (var path in Directory.GetFiles(Root(), "*.png", SearchOption.AllDirectories)) File.Delete(path);
            foreach (var name in new[] { "M6GameViewAudit.json", "FrameEventIndexMap.tsv", "StageStatus.txt" })
            {
                var path = Path.Combine(Root(), name); if (File.Exists(path)) File.Delete(path);
            }
            EditorSceneManager.OpenScene(SandroneM6Bootstrap.ScenePath, OpenSceneMode.Single);
            ConfigureGameView();
            SessionState.SetInt(StageKey, 1);
            ticks = 0;
            EditorApplication.EnterPlaymode();
        }

        private static void Update()
        {
            var stage = SessionState.GetInt(StageKey, 0);
            if (stage == 0) return;
            ticks++;
            ConfigureGameView();
            if (ticks == 1) File.WriteAllText(Path.Combine(Root(), "StageStatus.txt"), $"stage={stage}; playing={EditorApplication.isPlaying}; utc={DateTime.UtcNow:O}");
            if (stage < 90 && !EditorApplication.isPlaying) return;
            // Captures and pipeline switches need a long warm-up. Once Frame Debugger is
            // enabled, each selected draw only needs two editor updates for event data to settle.
            if (ticks < (stage == 8 ? 2 : 45)) return;
            switch (stage)
            {
                case 1:
                    Configure("M5", true);
                    Shot("GameView_M5_Baseline_768x1680.png");
                    Next(2);
                    break;
                case 2:
                    if (!Ready("GameView_M5_Baseline_768x1680.png")) return;
                    Configure("M6", true);
                    Shot("GameView_M6_768x1680.png");
                    Next(3);
                    break;
                case 3:
                    if (!Ready("GameView_M6_768x1680.png")) return;
                    Configure("M6", false);
                    Shot("GameView_M6_HairSpecOff_768x1680.png");
                    Next(4);
                    break;
                case 4:
                    if (!Ready("GameView_M6_HairSpecOff_768x1680.png")) return;
                    Configure("Mobile", true);
                    Next(5);
                    break;
                case 5:
                    Configure("Mobile", true);
                    Shot("GameView_Mobile_768x1680.png");
                    Next(6);
                    break;
                case 6:
                    if (!Ready("GameView_Mobile_768x1680.png")) return;
                    Configure("M6", true);
                    SetFrameDebugger(true);
                    Next(7);
                    break;
                case 7:
                    if (FrameCount() <= 0) return;
                    pending = CreateReport();
                    eventIndices = RelevantEventIndices();
                    eventCursor = 0;
                    if (eventIndices.Count == 0)
                    {
                        pending.failures.Add("Frame Debugger exposed no draw events.");
                        Finish(pending); SetFrameDebugger(false); Next(90); EditorApplication.ExitPlaymode(); break;
                    }
                    SetEventLimit(eventIndices[0] + 1);
                    Next(8);
                    break;
                case 8:
                    AppendEvent(pending, eventIndices[eventCursor]);
                    eventCursor++;
                    if (eventCursor < eventIndices.Count)
                    {
                        SetEventLimit(eventIndices[eventCursor] + 1);
                        Next(8);
                    }
                    else
                    {
                        Finish(pending);
                        SetFrameDebugger(false);
                        Next(90);
                        EditorApplication.ExitPlaymode();
                    }
                    break;
                case 90:
                    if (EditorApplication.isPlayingOrWillChangePlaymode) return;
                    var scene = EditorSceneManager.OpenScene(SandroneM6Bootstrap.ScenePath, OpenSceneMode.Single);
                    var controller = UnityEngine.Object.FindFirstObjectByType<SandroneM6Controller>();
                    var report = Load();
                    report.exitedPlayMode = true;
                    report.sceneReopenedAfterPlay = scene.IsValid();
                    report.controllerStateRestored = controller != null && controller.DebugMode == SandroneM6DebugMode.FinalToon &&
                        controller.HairSpecularEnabled && controller.EyeLayersEnabled;
                    if (!report.controllerStateRestored) report.failures.Add("M6 controller state did not restore after Play/scene reopen.");
                    Save(report);
                    SessionState.SetInt(StageKey, 0);
                    EditorApplication.Exit(report.failures.Count == 0 ? 0 : 1);
                    break;
            }
        }

        private static void Configure(string mode, bool hairEnabled)
        {
            var m6 = UnityEngine.Object.FindFirstObjectByType<SandroneM6Controller>();
            var m5 = UnityEngine.Object.FindFirstObjectByType<SandroneM5Controller>();
            var m0 = UnityEngine.Object.FindFirstObjectByType<SandroneM0Controller>();
            var camera = UnityEngine.Object.FindFirstObjectByType<Camera>();
            m6.TargetRenderer.sharedMaterials = mode == "M5" ? LoadM5Materials() : LoadM6Materials();
            var pipeline = AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>(mode == "Mobile" ?
                "Assets/Settings/Mobile_RPAsset.asset" : "Assets/Settings/PC_RPAsset.asset");
            GraphicsSettings.defaultRenderPipeline = pipeline;
            QualitySettings.renderPipeline = pipeline;
            m6.CharacterRoot.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            m5.DebugMode = SandroneM5DebugMode.FinalToon;
            m5.FaceSdfEnabled = true;
            m5.Apply(true);
            m6.DebugMode = SandroneM6DebugMode.FinalToon;
            m6.HairSpecularEnabled = hairEnabled;
            m6.EyeLayersEnabled = true;
            m6.SetLightDirectionToSource(SandroneM3Bootstrap.DefaultDirectionToLight);
            m6.Apply(true);
            m0.eyeALWeight = 0f;
            m0.blushWeight = 0f;
            m0.Apply();
            camera.orthographic = true;
            camera.transform.position = new Vector3(0, .82f, 4);
            camera.transform.rotation = Quaternion.LookRotation(Vector3.back, Vector3.up);
            camera.orthographicSize = .92f;
            camera.backgroundColor = new Color(.153f, .149f, .149f, 1);
            ConfigureGameView();
        }

        private static Material[] LoadM5Materials()
        {
            var map = AssetDatabase.LoadAssetAtPath<SandroneMaterialMap>(SandroneM0Bootstrap.MaterialMapPath);
            var result = new Material[31];
            foreach (var entry in map.Entries)
                result[entry.sourceIndex] = SandroneM6Bootstrap.BaselineMaterial(entry.sourceIndex, entry.materialAssetPath);
            return result;
        }

        private static Material[] LoadM6Materials()
        {
            var map = AssetDatabase.LoadAssetAtPath<SandroneMaterialMap>(SandroneM0Bootstrap.MaterialMapPath);
            var result = LoadM5Materials();
            foreach (var entry in map.Entries.Where(x => SandroneM6Bootstrap.TargetSlots.Contains(x.sourceIndex)))
                result[entry.sourceIndex] = AssetDatabase.LoadAssetAtPath<Material>(SandroneM6Bootstrap.MaterialPath(entry.sourceIndex, entry.materialAssetPath));
            return result;
        }

        private static Report CreateReport()
        {
            var report = new Report
            {
                generatedUtc = DateTime.UtcNow.ToString("O"), unityVersion = Application.unityVersion,
                graphicsApi = SystemInfo.graphicsDeviceType.ToString(), graphicsDevice = SystemInfo.graphicsDeviceName,
                scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().path, frameEventCount = FrameCount(),
                enteredPlayMode = EditorApplication.isPlaying
            };
            report.captures = Directory.GetFiles(Root(), "*.png", SearchOption.AllDirectories)
                .Select(x => Path.GetRelativePath(Root(), x)).OrderBy(x => x).ToList();
            var renderer = UnityEngine.Object.FindFirstObjectByType<SandroneM6Controller>().TargetRenderer;
            var map = AssetDatabase.LoadAssetAtPath<SandroneMaterialMap>(SandroneM0Bootstrap.MaterialMapPath);
            var block = new MaterialPropertyBlock();
            for (var i = 0; i < renderer.sharedMaterials.Length; i++)
            {
                var entry = map.Entries.First(x => x.sourceIndex == i);
                var actual = renderer.sharedMaterials[i];
                var expected = SandroneM6Bootstrap.TargetSlots.Contains(i)
                    ? AssetDatabase.LoadAssetAtPath<Material>(SandroneM6Bootstrap.MaterialPath(i, entry.materialAssetPath))
                    : SandroneM6Bootstrap.BaselineMaterial(i, entry.materialAssetPath);
                var baseMap = actual.GetTexture("_BaseMap");
                block.Clear(); renderer.GetPropertyBlock(block, i);
                var materialPath = AssetDatabase.GetAssetPath(actual);
                var expectedPath = AssetDatabase.GetAssetPath(expected);
                report.slots.Add(new SlotRecord
                {
                    slot = i, material = actual.name, materialPath = materialPath, materialGuid = AssetDatabase.AssetPathToGUID(materialPath),
                    expectedPath = expectedPath, expectedGuid = AssetDatabase.AssetPathToGUID(expectedPath), exactExpectedMaterial = actual == expected,
                    shader = actual.shader.name, baseMap = baseMap?.name ?? "", baseMapPath = AssetDatabase.GetAssetPath(baseMap),
                    baseMapGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(baseMap)),
                    exactExpectedBaseMap = baseMap == expected.GetTexture("_BaseMap"),
                    mpbTextureBindingOverride = block.HasTexture("_BaseMap") || block.HasTexture("_RampMap") ||
                        block.HasTexture("_ControlMap") || block.HasTexture("_MatCapMap") || block.HasVector("_BaseMap_ST") || block.HasColor("_BaseColor")
                });
            }
            return report;
        }

        private static List<int> RelevantEventIndices()
        {
            var utility = Utility();
            var getName = utility.GetMethod("GetFrameEventInfoName", BindingFlags.Static | BindingFlags.Public);
            var getObject = utility.GetMethod("GetFrameEventObject", BindingFlags.Static | BindingFlags.Public);
            var indices = new List<int>(); var rows = new List<string>();
            for (var i = 0; i < FrameCount(); i++)
            {
                var name = getName.Invoke(null, new object[] { i }) as string ?? "";
                var value = getObject.Invoke(null, new object[] { i }) as UnityEngine.Object;
                rows.Add($"{i}\t{name}\t{value?.GetType().Name ?? "null"}\t{value?.name ?? ""}");
                if (name.IndexOf("Draw", StringComparison.OrdinalIgnoreCase) >= 0 || value is Renderer || value is Material || value is Mesh) indices.Add(i);
            }
            File.WriteAllLines(Path.Combine(Root(), "FrameEventIndexMap.tsv"), rows);
            return indices.Count > 0 ? indices : Enumerable.Range(0, FrameCount()).ToList();
        }

        private static void AppendEvent(Report report, int index)
        {
            var utility = Utility();
            var dataType = typeof(EditorWindow).Assembly.GetType("UnityEditorInternal.FrameDebuggerInternal.FrameDebuggerEventData");
            var data = Activator.CreateInstance(dataType);
            if (!(bool)utility.GetMethod("GetFrameEventData", BindingFlags.Static | BindingFlags.Public).Invoke(null, new[] { (object)index, data })) return;
            var shader = Field<string>(dataType, data, "m_OriginalShaderName");
            var pass = Field<string>(dataType, data, "m_PassName");
            var blend = Obj(dataType, data, "m_BlendState");
            var raster = Obj(dataType, data, "m_RasterState");
            var depth = Obj(dataType, data, "m_DepthState");
            var shaderInfo = Obj(dataType, data, "m_ShaderInfo");
            var componentId = Field<EntityId>(dataType, data, "m_ComponentEntityId");
            var component = EditorUtility.EntityIdToObject(componentId) as Component;
            var eventObject = utility.GetMethod("GetFrameEventObject", BindingFlags.Static | BindingFlags.Public)?.Invoke(null, new object[] { index });
            var renderer = component as Renderer ?? component?.GetComponent<Renderer>() ?? eventObject as Renderer ?? (eventObject as Component)?.GetComponent<Renderer>();
            var material = eventObject as Material;
            var auditSlot = Float(shaderInfo, "_M6AuditSlotId", -1f);
            var mappedSlot = shader == "Sandrone/M6/HairEye" && auditSlot >= 0 && auditSlot < 31 ? Mathf.RoundToInt(auditSlot) : -1;
            if (material == null && mappedSlot >= 0 && renderer != null) material = renderer.sharedMaterials[mappedSlot];
            report.events.Add(new EventRecord
            {
                index = index, eventName = utility.GetMethod("GetFrameEventInfoName", BindingFlags.Static | BindingFlags.Public).Invoke(null, new object[] { index }) as string,
                renderer = renderer != null ? renderer.gameObject.name : "", material = material != null ? material.name : "",
                shader = shader, pass = pass, mappedSlot = mappedSlot, auditSlotId = auditSlot,
                stencilRef = Field<int>(dataType, data, "m_StencilRef"), renderTarget = Field<string>(dataType, data, "m_RenderTargetName"),
                cull = Nested(raster, "m_CullMode"), zWrite = Nested<int>(depth, "m_DepthWrite"), zTest = Nested(depth, "m_DepthFunc"),
                srcBlend = Nested(blend, "m_SrcBlend"), dstBlend = Nested(blend, "m_DstBlend"),
                baseMap = Texture(shaderInfo, "_BaseMap"), controlMap = Texture(shaderInfo, "_ControlMap"), ramp = Texture(shaderInfo, "_RampMap"),
                faceMap = Texture(shaderInfo, "_FaceMap"), matCap = Texture(shaderInfo, "_MatCapMap"),
                role = Float(shaderInfo, "_M6Role", float.NaN), eyeFlatLighting = Float(shaderInfo, "_EyeFlatLighting", float.NaN),
                hairSpecIntensity = Float(shaderInfo, "_HairSpecIntensity", float.NaN), layerWeight = Float(shaderInfo, "_LayerWeight", float.NaN)
            });
        }

        private static void Finish(Report report)
        {
            report.m5ForwardCount = report.events.Count(x => x.pass == "M5FaceSDF" && x.shader == "Sandrone/M5/FaceSDF");
            report.m6ForwardCount = report.events.Count(x => x.pass == "M6HairEye" && x.shader == "Sandrone/M6/HairEye");
            report.m4ForwardCount = report.events.Count(x => x.pass == "M4MaterialResponse" && x.shader == "Sandrone/M4/MaterialResponse");
            report.shadowCasterCount = report.events.Count(x => x.pass == "ShadowCaster");
            report.receiverCount = report.events.Count(x => x.pass == "M3ShadowReceiver");
            if (report.m5ForwardCount != 2) report.failures.Add($"Expected 2 M5 face Forward events, got {report.m5ForwardCount}.");
            if (report.m6ForwardCount != 11) report.failures.Add($"Expected 11 M6 hair/eye Forward events, got {report.m6ForwardCount}.");
            if (report.m4ForwardCount != 18) report.failures.Add($"Expected 18 exact M4 baseline Forward events, got {report.m4ForwardCount}.");
            if (report.shadowCasterCount < 40) report.failures.Add($"Character ShadowCaster evidence insufficient: {report.shadowCasterCount}.");
            if (report.receiverCount != 1) report.failures.Add($"Expected one formal ground receiver, got {report.receiverCount}.");
            foreach (var slot in report.slots)
            {
                if (!slot.exactExpectedMaterial || !slot.exactExpectedBaseMap) report.failures.Add($"Renderer slot {slot.slot} differs from expected M6/baseline binding.");
                if (slot.mpbTextureBindingOverride) report.failures.Add($"Renderer slot {slot.slot} has an MPB texture/ST/color binding override.");
            }
            var targetRenderer = UnityEngine.Object.FindFirstObjectByType<SandroneM6Controller>().TargetRenderer;
            foreach (var frameEvent in report.events.Where(x => x.pass == "M6HairEye"))
            {
                var candidates = SandroneM6Bootstrap.TargetSlots.Where(slot => Signature(targetRenderer.sharedMaterials[slot]) == Signature(frameEvent)).ToArray();
                frameEvent.candidateSlots = string.Join(",", candidates);
                if (candidates.Length == 1) frameEvent.mappedSlot = candidates[0];
            }
            foreach (var slot in SandroneM6Bootstrap.TargetSlots)
                if (!report.events.Any(x => x.pass == "M6HairEye" && x.candidateSlots.Split(',').Contains(slot.ToString())))
                    report.failures.Add($"Frame Debugger M6 slot {slot} effective signature missing.");
            var writer = report.events.FirstOrDefault(x => x.pass == "M6HairEye" && x.mappedSlot == 6);
            if (writer == null || writer.stencilRef != 1) report.failures.Add("Opaque iris stencil writer did not expose Ref=1.");
            foreach (var slot in new[] { 7, 8, 9, 10, 11 })
            {
                var reader = report.events.FirstOrDefault(x => x.pass == "M6HairEye" && x.mappedSlot == slot);
                if (reader == null || reader.stencilRef != 1) report.failures.Add($"Eye layer slot {slot} did not expose Stencil Ref=1.");
            }
            ValidateCaptures(report);
            Save(report);
        }

        private static string Signature(Material material) => string.Join("|", material.GetTexture("_BaseMap")?.name ?? "",
            Mathf.RoundToInt(material.GetFloat("_ZWrite")), Mathf.RoundToInt(material.GetFloat("_M6StencilRef")),
            F(material.GetFloat("_M6Role")), F(material.GetFloat("_EyeFlatLighting")), F(material.GetFloat("_HairSpecIntensity")),
            F(material.GetFloat("_LayerWeight")), CullName(material.GetFloat("_Cull")), BlendName(material.GetFloat("_SrcBlend")), BlendName(material.GetFloat("_DstBlend")));
        private static string Signature(EventRecord value) => string.Join("|", value.baseMap, value.zWrite, value.stencilRef,
            F(value.role), F(value.eyeFlatLighting), F(value.hairSpecIntensity), F(value.layerWeight), value.cull, value.srcBlend, value.dstBlend);
        private static string F(float value) => float.IsNaN(value) ? "NaN" : (Mathf.Round(value * 10000f) / 10000f).ToString("R", System.Globalization.CultureInfo.InvariantCulture);
        private static string CullName(float value) => Mathf.RoundToInt(value) switch { 1 => "Front", 2 => "Back", _ => "Off" };
        private static string BlendName(float value) => ((BlendMode)Mathf.RoundToInt(value)).ToString();

        private static void ValidateCaptures(Report report)
        {
            var m5 = LoadPixels("GameView_M5_Baseline_768x1680.png", report);
            var m6 = LoadPixels("GameView_M6_768x1680.png", report);
            var hairOff = LoadPixels("GameView_M6_HairSpecOff_768x1680.png", report);
            var mobile = LoadPixels("GameView_Mobile_768x1680.png", report);
            if (m5 == null || m6 == null || hairOff == null || mobile == null) return;
            report.m5M6Mae = Mae(m5, m6);
            report.hairToggleMae = Mae(m6, hairOff);
            report.pcMobileMae = Mae(m6, mobile);
            report.redSkirtPixels = RedSkirtPixels(m6);
            if (report.m5M6Mae <= .005f) report.failures.Add($"M6 produced no measurable hair/eye response (MAE={report.m5M6Mae:F4}).");
            if (report.hairToggleMae <= .005f) report.failures.Add($"Game View hair toggle response missing (MAE={report.hairToggleMae:F4}).");
            if (report.pcMobileMae > 8f) report.failures.Add($"PC/Mobile Game View MAE too high: {report.pcMobileMae:F3}.");
            if (report.redSkirtPixels < 50000) report.failures.Add($"Red long-skirt evidence insufficient: {report.redSkirtPixels} pixels.");
            var cyan = mobile.Count(x => x.r < 32 && x.g > 200 && x.b > 200);
            if (cyan > mobile.Length / 1000) report.failures.Add($"Mobile capture contains cyan failure output: {cyan} pixels.");
        }

        private static Color32[] LoadPixels(string relative, Report report)
        {
            var path = Path.Combine(Root(), relative);
            if (!Ready(relative)) { report.failures.Add("Missing Game View capture: " + relative); return null; }
            var texture = new Texture2D(2, 2);
            if (!texture.LoadImage(File.ReadAllBytes(path))) { UnityEngine.Object.DestroyImmediate(texture); report.failures.Add("Unreadable capture: " + relative); return null; }
            if (texture.width != Width || texture.height != Height) report.failures.Add($"{relative} is {texture.width}x{texture.height}, expected {Width}x{Height}.");
            var pixels = texture.GetPixels32(); UnityEngine.Object.DestroyImmediate(texture); return pixels;
        }
        private static float Mae(Color32[] a, Color32[] b)
        {
            if (a.Length != b.Length) return float.PositiveInfinity;
            double sum = 0; for (var i = 0; i < a.Length; i++) sum += Math.Abs(a[i].r - b[i].r) + Math.Abs(a[i].g - b[i].g) + Math.Abs(a[i].b - b[i].b);
            return (float)(sum / (a.Length * 3.0));
        }
        private static int RedSkirtPixels(Color32[] pixels)
        {
            var count = 0;
            for (var y = 160; y < 1260; y++) for (var x = 48; x < Width - 48; x++)
            {
                var p = pixels[y * Width + x]; if (p.r > 48 && p.r > p.g * 1.35f && p.r > p.b * 1.2f) count++;
            }
            return count;
        }

        private const BindingFlags AllInstance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private static T Field<T>(Type t, object o, string n) { var value = t.GetField(n, AllInstance)?.GetValue(o); return value is T result ? result : default; }
        private static object Obj(Type t, object o, string n) => t.GetField(n, AllInstance)?.GetValue(o);
        private static string Nested(object o, string n) => o?.GetType().GetField(n, AllInstance)?.GetValue(o)?.ToString() ?? "";
        private static T Nested<T>(object o, string n) { var value = o?.GetType().GetField(n, AllInstance)?.GetValue(o); return value is T result ? result : default; }
        private static float Float(object info, string property, float fallback)
        {
            if (info?.GetType().GetField("m_Floats", AllInstance)?.GetValue(info) is not System.Collections.IEnumerable values) return fallback;
            foreach (var item in values) if (Nested(item, "m_Name") == property) return Nested<float>(item, "m_Value");
            return fallback;
        }
        private static string Texture(object info, string property)
        {
            if (info?.GetType().GetField("m_Textures", BindingFlags.Instance | BindingFlags.Public)?.GetValue(info) is not System.Collections.IEnumerable values) return "";
            foreach (var item in values) if (Nested(item, "m_Name") == property) return Nested(item, "m_TextureName");
            return "";
        }
        private static Type Utility() => typeof(EditorWindow).Assembly.GetType("UnityEditorInternal.FrameDebuggerInternal.FrameDebuggerUtility");
        private static int FrameCount() => (int)(Utility().GetProperty("count", BindingFlags.Static | BindingFlags.Public)?.GetValue(null) ?? 0);
        private static void SetEventLimit(int value) => Utility().GetProperty("limit", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.SetValue(null, value);
        private static void SetFrameDebugger(bool enabled)
        {
            var type = typeof(EditorWindow).Assembly.GetType("UnityEditor.FrameDebuggerWindow");
            var window = EditorWindow.GetWindow(type, false, "Frame Debugger", true);
            if (enabled)
            {
                window.Show();
                type.GetMethod("OpenPlayModeView", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.Invoke(window, null);
                type.GetMethod("EnableFrameDebugger", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.Invoke(window, null);
            }
            else type.GetMethod("DisableFrameDebugger", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.Invoke(window, null);
            Utility().GetMethod("SetEnabled", BindingFlags.Static | BindingFlags.Public)?.Invoke(null, new object[] { enabled, 0 });
        }
        private static void ConfigureGameView()
        {
            var type = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");
            var window = EditorWindow.GetWindow(type, false, "Game", true);
            type.GetMethod("SetCustomResolution", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.Invoke(window, new object[] { new Vector2(Width, Height), "M6 Audit" });
            window.Show(); window.Focus(); window.Repaint();
        }
        private static void Shot(string relative)
        {
            var path = Path.Combine(Root(), relative); if (File.Exists(path)) File.Delete(path); ScreenCapture.CaptureScreenshot(path);
        }
        private static bool Ready(string relative) => File.Exists(Path.Combine(Root(), relative)) && new FileInfo(Path.Combine(Root(), relative)).Length > 1024;
        private static void Next(int stage) { SessionState.SetInt(StageKey, stage); ticks = 0; }
        private static Report Load() => JsonUtility.FromJson<Report>(File.ReadAllText(Path.Combine(Root(), "M6GameViewAudit.json")));
        private static void Save(Report report) => File.WriteAllText(Path.Combine(Root(), "M6GameViewAudit.json"), JsonUtility.ToJson(report, true));
        private static string Root() => Path.GetFullPath(Path.Combine(Application.dataPath, "../TestArtifacts/M6GameViewAudit"));
    }
}
