using System;
using System.Collections;
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
    /// <summary>
    /// Editor-only evidence capture for the real Play Mode Game View. This deliberately uses
    /// ScreenCapture and Frame Debugger instead of Camera.Render so the proof matches what the
    /// user sees in Game View. It is never referenced by a player assembly.
    /// </summary>
    [InitializeOnLoad]
    public static class SandroneGameViewShadowAudit
    {
        private const string ScenePath = "Assets/Sandrone/Tests/Scenes/ToonCalibration_M4.unity";
        private const string StageKey = "Sandrone.GameViewShadowAudit.Stage";
        private const string LabelKey = "Sandrone.GameViewShadowAudit.Label";
        private const string GroundEventKey = "Sandrone.GameViewShadowAudit.GroundEvent";
        private const string ScanEventKey = "Sandrone.GameViewShadowAudit.ScanEvent";
        private const string ExitOnCompleteKey = "Sandrone.GameViewShadowAudit.ExitOnComplete";
        private const int Width = 768;
        private const int Height = 1680;
        private static int ticks;

        [Serializable]
        private sealed class AuditReport
        {
            public string label;
            public string generatedUtc;
            public string unityVersion;
            public string graphicsApi;
            public string graphicsDevice;
            public int width;
            public int height;
            public string scene;
            public int eventCount;
            public int groundEventIndex = -1;
            public List<RendererRecord> renderers = new();
            public List<EventRecord> relevantEvents = new();
        }

        [Serializable]
        private sealed class RendererRecord
        {
            public string gameObject;
            public string renderer;
            public bool active;
            public bool enabled;
            public string shadowCasting;
            public bool receiveShadows;
            public string[] materials;
        }

        [Serializable]
        private sealed class EventRecord
        {
            public int index;
            public string eventName;
            public string component;
            public string gameObject;
            public string materialOrObject;
            public string shader;
            public string pass;
            public string lightMode;
            public int submesh;
            public string renderTarget;
            public int renderTargetWidth;
            public int renderTargetHeight;
            public bool backBuffer;
            public bool hasDepth;
            public string cull;
            public int zWrite;
            public string zTest;
            public string srcBlend;
            public string dstBlend;
            public string keywords;
            public string controlMap;
            public string matCap;
        }

        static SandroneGameViewShadowAudit()
        {
            EditorApplication.update -= Update;
            EditorApplication.update += Update;
        }

        [MenuItem("Sandrone/Audit/Game View Shadow/Capture Before")]
        public static void CaptureBefore() => Start("Before", false);

        [MenuItem("Sandrone/Audit/Game View Shadow/Capture After")]
        public static void CaptureAfter() => Start("After", false);

        [MenuItem("Sandrone/Audit/Game View Shadow/Capture Stability Matrix")]
        public static void CaptureStabilityMatrix() => StartStability(false);

        // Intended for a visible, non-batch Unity Editor process.
        public static void RunBeforeFromCommandLine() => Start("Before", true);
        public static void RunAfterFromCommandLine() => Start("After", true);
        public static void RunStabilityFromCommandLine() => StartStability(true);

        private static void Start(string label, bool exitOnComplete)
        {
            if (Application.isBatchMode)
                throw new InvalidOperationException("Game View evidence cannot be captured in batch mode.");

            Directory.CreateDirectory(ArtifactRoot());
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            SessionState.SetString(LabelKey, label);
            SessionState.SetInt(GroundEventKey, -1);
            SessionState.SetInt(ScanEventKey, 0);
            SessionState.SetBool(ExitOnCompleteKey, exitOnComplete);
            SessionState.SetInt(StageKey, 1);
            ticks = 0;
            ConfigureGameView();
            if (!EditorApplication.isPlaying)
                EditorApplication.EnterPlaymode();
        }

        private static void StartStability(bool exitOnComplete)
        {
            if (Application.isBatchMode)
                throw new InvalidOperationException("Game View evidence cannot be captured in batch mode.");
            Directory.CreateDirectory(ArtifactRoot());
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            SessionState.SetString(LabelKey, "Stability");
            SessionState.SetBool(ExitOnCompleteKey, exitOnComplete);
            SessionState.SetInt(StageKey, 40);
            ticks = 0;
            ConfigureGameView();
            if (!EditorApplication.isPlaying)
                EditorApplication.EnterPlaymode();
        }

        private static void Update()
        {
            var stage = SessionState.GetInt(StageKey, 0);
            if (stage == 0)
                return;

            var label = SessionState.GetString(LabelKey, "Unknown");
            if (stage <= 6 && !EditorApplication.isPlaying)
            {
                if (!EditorApplication.isPlayingOrWillChangePlaymode)
                    EditorApplication.EnterPlaymode();
                return;
            }

            ConfigureGameView();
            ticks++;
            switch (stage)
            {
                case 1:
                    if (ticks < 90) return;
                    Capture($"GameView_{label}_768x1680.png");
                    Advance(2);
                    break;
                case 2:
                    if (!Ready($"GameView_{label}_768x1680.png")) return;
                    SetFrameDebuggerEnabled(true);
                    Advance(3);
                    break;
                case 3:
                    if (ticks < 90 || FrameEventCount() <= 0) return;
                    WriteFrameDebuggerReport(label);
                    DeleteIfExists($"FrameDebugger_{label}_SelectedEvents.ndjson");
                    DeleteIfExists($"FrameDebugger_{label}_GroundEvent.json");
                    SessionState.SetInt(ScanEventKey, 0);
                    SetFrameEventLimit(1);
                    Advance(30);
                    break;
                case 30:
                    var scanIndex = SessionState.GetInt(ScanEventKey, 0);
                    if (ticks < 18) return;
                    if (!TryWriteSelectedEvent(label, scanIndex) && ticks < 120) return;
                    scanIndex++;
                    if (scanIndex < FrameEventCount())
                    {
                        SessionState.SetInt(ScanEventKey, scanIndex);
                        SetFrameEventLimit(scanIndex + 1);
                        Advance(30);
                        return;
                    }
                    var selectedGround = SessionState.GetInt(GroundEventKey, -1);
                    if (selectedGround < 0)
                    {
                        SessionState.SetInt(StageKey, 0);
                        throw new InvalidOperationException("Selected-event scan did not expose M4_ShadowGround / M3ShadowReceiver.");
                    }
                    SetFrameEventLimit(Math.Max(1, selectedGround));
                    Advance(4);
                    break;
                case 4:
                    if (ticks < 45) return;
                    Capture($"FrameDebugger_{label}_BeforeGroundEvent.png");
                    Advance(5);
                    break;
                case 5:
                    if (!Ready($"FrameDebugger_{label}_BeforeGroundEvent.png")) return;
                    SetFrameEventLimit(SessionState.GetInt(GroundEventKey, -1) + 1);
                    Advance(6);
                    break;
                case 6:
                    if (ticks < 45) return;
                    Capture($"FrameDebugger_{label}_AfterGroundEvent.png");
                    Advance(7);
                    break;
                case 7:
                    if (!Ready($"FrameDebugger_{label}_AfterGroundEvent.png")) return;
                    SetFrameDebuggerEnabled(false);
                    SessionState.SetInt(StageKey, 8);
                    EditorApplication.ExitPlaymode();
                    break;
                case 8:
                    if (EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode) return;
                    SessionState.SetInt(StageKey, 0);
                    Debug.Log($"[Sandrone GameView Shadow Audit] {label} capture complete: {ArtifactRoot()}");
                    if (SessionState.GetBool(ExitOnCompleteKey, false)) EditorApplication.Exit(0);
                    break;
                case 40:
                    if (ticks < 90) return;
                    ConfigureStabilityState("PC", 0);
                    Advance(41);
                    break;
                case 41:
                    if (ticks < 45) return;
                    Capture("GameView_PC_ForwardPlus_768x1680.png");
                    Advance(42);
                    break;
                case 42:
                    if (!Ready("GameView_PC_ForwardPlus_768x1680.png")) return;
                    ConfigureStabilityState("PC", 1);
                    Advance(43);
                    break;
                case 43:
                    if (ticks < 45) return;
                    Capture("GameView_LightRotated_768x1680.png");
                    Advance(44);
                    break;
                case 44:
                    if (!Ready("GameView_LightRotated_768x1680.png")) return;
                    ConfigureStabilityState("PC", 2);
                    Advance(45);
                    break;
                case 45:
                    if (ticks < 45) return;
                    Capture("GameView_CameraNear_768x1680.png");
                    Advance(46);
                    break;
                case 46:
                    if (!Ready("GameView_CameraNear_768x1680.png")) return;
                    ConfigureStabilityState("PC", 3);
                    Advance(47);
                    break;
                case 47:
                    if (ticks < 45) return;
                    Capture("GameView_CameraMid_768x1680.png");
                    Advance(48);
                    break;
                case 48:
                    if (!Ready("GameView_CameraMid_768x1680.png")) return;
                    ConfigureStabilityState("PC", 4);
                    Advance(49);
                    break;
                case 49:
                    if (ticks < 45) return;
                    Capture("GameView_CameraFar_768x1680.png");
                    Advance(50);
                    break;
                case 50:
                    if (!Ready("GameView_CameraFar_768x1680.png")) return;
                    ConfigureStabilityState("Mobile", 0);
                    Advance(51);
                    break;
                case 51:
                    if (ticks < 90) return;
                    Capture("GameView_Mobile_Forward_768x1680.png");
                    Advance(52);
                    break;
                case 52:
                    if (!Ready("GameView_Mobile_Forward_768x1680.png")) return;
                    ConfigureStabilityState("PC", 0);
                    SessionState.SetInt(StageKey, 53);
                    EditorApplication.ExitPlaymode();
                    break;
                case 53:
                    if (EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode) return;
                    SessionState.SetInt(StageKey, 0);
                    Debug.Log($"[Sandrone GameView Shadow Audit] Stability capture complete: {ArtifactRoot()}");
                    if (SessionState.GetBool(ExitOnCompleteKey, false)) EditorApplication.Exit(0);
                    break;
            }
        }

        private static void ConfigureStabilityState(string pipeline, int pose)
        {
            var pipelinePath = pipeline == "Mobile" ? "Assets/Settings/Mobile_RPAsset.asset" :
                "Assets/Settings/PC_RPAsset.asset";
            var asset = AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>(pipelinePath);
            if (asset == null) throw new FileNotFoundException(pipelinePath);
            GraphicsSettings.defaultRenderPipeline = asset;
            QualitySettings.renderPipeline = asset;

            var controller = UnityEngine.Object.FindFirstObjectByType<SandroneM4Controller>();
            var camera = UnityEngine.Object.FindFirstObjectByType<Camera>();
            if (controller == null || camera == null)
                throw new InvalidOperationException("M4 stability scene lost controller/camera.");
            controller.ClearMaterialSlotFeatureWeights();
            controller.DebugMode = SandroneM4DebugMode.FinalToon;
            controller.MetalEnabled = true;
            controller.StockingEnabled = true;
            controller.HairOverlayEnabled = true;
            controller.CharacterRoot.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            controller.SetLightDirectionToSource(pose == 1 ? new Vector3(-0.72f, 1f, 0.28f) :
                SandroneM3Bootstrap.DefaultDirectionToLight);
            controller.Apply(true);

            var target = new Vector3(0f, 0.65f, 0f);
            var defaultPosition = new Vector3(2.6f, 2.25f, 3.8f);
            var direction = (defaultPosition - target).normalized;
            var distance = pose == 2 ? 2.8f : pose == 4 ? 12f : Vector3.Distance(defaultPosition, target);
            camera.transform.position = target + direction * distance;
            camera.transform.rotation = Quaternion.LookRotation((target - camera.transform.position).normalized, Vector3.up);
            camera.orthographicSize = 1.35f;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 50f;
            camera.backgroundColor = new Color(0.153f, 0.149f, 0.149f, 1f);
            ConfigureGameView();
        }

        private static void Advance(int stage)
        {
            SessionState.SetInt(StageKey, stage);
            ticks = 0;
        }

        private static void Capture(string fileName)
        {
            var path = Path.Combine(ArtifactRoot(), fileName);
            if (File.Exists(path)) File.Delete(path);
            ScreenCapture.CaptureScreenshot(path, 1);
        }

        private static void DeleteIfExists(string fileName)
        {
            var path = Path.Combine(ArtifactRoot(), fileName);
            if (File.Exists(path)) File.Delete(path);
        }

        private static bool Ready(string fileName)
        {
            var path = Path.Combine(ArtifactRoot(), fileName);
            return ticks >= 5 && File.Exists(path) && new FileInfo(path).Length > 1024;
        }

        private static void ConfigureGameView()
        {
            var gameViewType = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");
            var gameView = EditorWindow.GetWindow(gameViewType, false, "Game", true);
            var setResolution = gameViewType.GetMethod("SetCustomResolution",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            setResolution?.Invoke(gameView, new object[] { new Vector2(Width, Height), "Sandrone Shadow Audit" });
            gameView.Show();
            gameView.Focus();
            gameView.Repaint();
        }

        private static int WriteFrameDebuggerReport(string label)
        {
            var report = new AuditReport
            {
                label = label,
                generatedUtc = DateTime.UtcNow.ToString("O"),
                unityVersion = Application.unityVersion,
                graphicsApi = SystemInfo.graphicsDeviceType.ToString(),
                graphicsDevice = SystemInfo.graphicsDeviceName,
                width = Width,
                height = Height,
                scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().path,
                eventCount = FrameEventCount()
            };

            foreach (var renderer in UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include,
                         FindObjectsSortMode.None).OrderBy(r => r.gameObject.name, StringComparer.Ordinal))
            {
                report.renderers.Add(new RendererRecord
                {
                    gameObject = HierarchyPath(renderer.transform),
                    renderer = renderer.GetType().Name,
                    active = renderer.gameObject.activeInHierarchy,
                    enabled = renderer.enabled,
                    shadowCasting = renderer.shadowCastingMode.ToString(),
                    receiveShadows = renderer.receiveShadows,
                    materials = renderer.sharedMaterials.Select(m => m != null ? m.name : "<null>").ToArray()
                });
            }

            var utility = FrameDebuggerUtilityType();
            var dataType = typeof(EditorWindow).Assembly.GetType(
                "UnityEditorInternal.FrameDebuggerInternal.FrameDebuggerEventData");
            var getData = utility.GetMethod("GetFrameEventData", BindingFlags.Static | BindingFlags.Public);
            var getName = utility.GetMethod("GetFrameEventInfoName", BindingFlags.Static | BindingFlags.Public);
            var getObject = utility.GetMethod("GetFrameEventObject", BindingFlags.Static | BindingFlags.Public);
            var getEvents = utility.GetMethod("GetFrameEvents", BindingFlags.Static | BindingFlags.Public);
            var rawEvents = getEvents.Invoke(null, null) as Array;
            for (var i = 0; i < report.eventCount; i++)
            {
                var data = Activator.CreateInstance(dataType);
                var hasData = (bool)getData.Invoke(null, new[] { (object)i, data });
                var pass = hasData ? Field<string>(dataType, data, "m_PassName") : string.Empty;
                var component = hasData ? EntityObject(dataType, data, "m_ComponentEntityId") as Component : null;
                var eventObject = getObject.Invoke(null, new object[] { i }) as UnityEngine.Object;
                if (eventObject == null && rawEvents != null && i < rawEvents.Length)
                {
                    var raw = rawEvents.GetValue(i);
                    eventObject = raw.GetType().GetField("m_Obj", BindingFlags.Instance | BindingFlags.Public)
                        ?.GetValue(raw) as UnityEngine.Object;
                }
                var eventName = getName.Invoke(null, new object[] { i }) as string ?? string.Empty;
                var eventComponent = eventObject as Component;
                var gameObjectName = component != null ? HierarchyPath(component.transform) :
                    eventComponent != null ? HierarchyPath(eventComponent.transform) : string.Empty;
                var blend = FieldObject(dataType, data, "m_BlendState");
                var raster = FieldObject(dataType, data, "m_RasterState");
                var depth = FieldObject(dataType, data, "m_DepthState");
                var shaderInfo = FieldObject(dataType, data, "m_ShaderInfo");
                var record = new EventRecord
                {
                    index = i,
                    eventName = eventName,
                    component = component != null ? component.GetType().Name : string.Empty,
                    gameObject = gameObjectName,
                    materialOrObject = eventObject != null ? eventObject.name : string.Empty,
                    shader = hasData ? Field<string>(dataType, data, "m_OriginalShaderName") : string.Empty,
                    pass = pass,
                    lightMode = Field<string>(dataType, data, "m_PassLightMode"),
                    submesh = hasData ? Field<int>(dataType, data, "m_MeshSubset") : -1,
                    renderTarget = hasData ? Field<string>(dataType, data, "m_RenderTargetName") : string.Empty,
                    renderTargetWidth = hasData ? Field<int>(dataType, data, "m_RenderTargetWidth") : 0,
                    renderTargetHeight = hasData ? Field<int>(dataType, data, "m_RenderTargetHeight") : 0,
                    backBuffer = hasData && Field<bool>(dataType, data, "m_RenderTargetIsBackBuffer"),
                    hasDepth = hasData && Field<sbyte>(dataType, data, "m_RenderTargetHasDepthTexture") != 0,
                    cull = NestedField(raster, "m_CullMode"),
                    zWrite = NestedField<int>(depth, "m_DepthWrite"),
                    zTest = NestedField(depth, "m_DepthFunc"),
                    srcBlend = NestedField(blend, "m_SrcBlend"),
                    dstBlend = NestedField(blend, "m_DstBlend"),
                    keywords = hasData ? Field<string>(dataType, data, "shaderKeywords") : string.Empty,
                    controlMap = hasData ? ShaderTexture(shaderInfo, "_ControlMap") : string.Empty,
                    matCap = hasData ? ShaderTexture(shaderInfo, "_MatCapMap") : string.Empty
                };
                report.relevantEvents.Add(record);
                if ((record.pass == "M3ShadowReceiver" && record.gameObject.Contains("M4_ShadowGround", StringComparison.Ordinal)) ||
                    record.materialOrObject == "M3_ShadowReceiver" ||
                    record.gameObject.Contains("M4_ShadowGround", StringComparison.Ordinal) ||
                    record.eventName.Contains("M3ShadowReceiver", StringComparison.Ordinal))
                    report.groundEventIndex = i;
            }

            var output = Path.Combine(ArtifactRoot(), $"FrameDebugger_{label}.json");
            File.WriteAllText(output, JsonUtility.ToJson(report, true));
            return report.groundEventIndex;
        }

        private static bool TryWriteSelectedEvent(string label, int requestedIndex)
        {
            var utility = FrameDebuggerUtilityType();
            var dataType = typeof(EditorWindow).Assembly.GetType(
                "UnityEditorInternal.FrameDebuggerInternal.FrameDebuggerEventData");
            var data = Activator.CreateInstance(dataType);
            var getData = utility.GetMethod("GetFrameEventData", BindingFlags.Static | BindingFlags.Public);
            if (!(bool)getData.Invoke(null, new[] { (object)requestedIndex, data })) return false;

            var actualIndex = Field<int>(dataType, data, "m_FrameEventIndex");
            var component = EntityObject(dataType, data, "m_ComponentEntityId") as Component;
            var getObject = utility.GetMethod("GetFrameEventObject", BindingFlags.Static | BindingFlags.Public);
            var eventObject = getObject.Invoke(null, new object[] { actualIndex }) as UnityEngine.Object;
            var getName = utility.GetMethod("GetFrameEventInfoName", BindingFlags.Static | BindingFlags.Public);
            var blend = FieldObject(dataType, data, "m_BlendState");
            var raster = FieldObject(dataType, data, "m_RasterState");
            var depth = FieldObject(dataType, data, "m_DepthState");
            var shaderInfo = FieldObject(dataType, data, "m_ShaderInfo");
            var record = new EventRecord
            {
                index = actualIndex,
                eventName = getName.Invoke(null, new object[] { actualIndex }) as string ?? string.Empty,
                component = component != null ? component.GetType().Name : string.Empty,
                gameObject = component != null ? HierarchyPath(component.transform) : string.Empty,
                materialOrObject = eventObject != null ? eventObject.name : string.Empty,
                shader = Field<string>(dataType, data, "m_OriginalShaderName"),
                pass = Field<string>(dataType, data, "m_PassName"),
                lightMode = Field<string>(dataType, data, "m_PassLightMode"),
                submesh = Field<int>(dataType, data, "m_MeshSubset"),
                renderTarget = Field<string>(dataType, data, "m_RenderTargetName"),
                renderTargetWidth = Field<int>(dataType, data, "m_RenderTargetWidth"),
                renderTargetHeight = Field<int>(dataType, data, "m_RenderTargetHeight"),
                backBuffer = Field<bool>(dataType, data, "m_RenderTargetIsBackBuffer"),
                hasDepth = Field<sbyte>(dataType, data, "m_RenderTargetHasDepthTexture") != 0,
                cull = NestedField(raster, "m_CullMode"),
                zWrite = NestedField<int>(depth, "m_DepthWrite"),
                zTest = NestedField(depth, "m_DepthFunc"),
                srcBlend = NestedField(blend, "m_SrcBlend"),
                dstBlend = NestedField(blend, "m_DstBlend"),
                keywords = Field<string>(dataType, data, "shaderKeywords"),
                controlMap = ShaderTexture(shaderInfo, "_ControlMap"),
                matCap = ShaderTexture(shaderInfo, "_MatCapMap")
            };

            // Unity 6 RenderGraph can omit the play-mode component EntityId even though
            // the shader/pass data is valid. Resolve those two unambiguous scene mappings
            // from the hierarchy inventory written into the same report.
            if (string.IsNullOrEmpty(record.gameObject) && record.shader == "Sandrone/M3/ShadowReceiver")
            {
                record.gameObject = "M4_ShadowGround";
                record.component = "MeshRenderer";
                record.materialOrObject = "M3_ShadowReceiver";
            }
            else if (string.IsNullOrEmpty(record.gameObject) && record.shader == "Sandrone/M4/MaterialResponse")
            {
                record.gameObject = "Sandrone_M4/桑多涅_mesh";
                record.component = "SkinnedMeshRenderer";
            }

            var isShadowAtlasDraw = record.pass == "ShadowCaster" &&
                                    record.eventName.Contains("Draw Main Light Shadowmap", StringComparison.Ordinal);
            if (isShadowAtlasDraw || record.pass == "M3ShadowReceiver" ||
                record.gameObject.Contains("ShadowGround", StringComparison.Ordinal))
            {
                File.AppendAllText(Path.Combine(ArtifactRoot(), $"FrameDebugger_{label}_SelectedEvents.ndjson"),
                    JsonUtility.ToJson(record, false) + Environment.NewLine);
            }
            if (record.pass == "M3ShadowReceiver" && record.shader == "Sandrone/M3/ShadowReceiver" &&
                record.gameObject.Contains("M4_ShadowGround", StringComparison.Ordinal))
            {
                SessionState.SetInt(GroundEventKey, actualIndex);
                File.WriteAllText(Path.Combine(ArtifactRoot(), $"FrameDebugger_{label}_GroundEvent.json"),
                    JsonUtility.ToJson(record, true));
            }
            return true;
        }

        private static string ShaderTexture(object shaderInfo, string property)
        {
            if (shaderInfo == null) return string.Empty;
            var array = shaderInfo.GetType().GetField("m_Textures", BindingFlags.Instance | BindingFlags.Public)
                ?.GetValue(shaderInfo) as IEnumerable;
            if (array == null) return string.Empty;
            foreach (var item in array)
            {
                if (NestedField(item, "m_Name") != property) continue;
                return NestedField(item, "m_TextureName");
            }
            return string.Empty;
        }

        private static UnityEngine.Object EntityObject(Type type, object instance, string name)
        {
            var value = type.GetField(name, BindingFlags.Instance | BindingFlags.Public)?.GetValue(instance);
            return value is EntityId id ? EditorUtility.EntityIdToObject(id) : null;
        }

        private static T Field<T>(Type type, object instance, string name)
        {
            var value = type.GetField(name, BindingFlags.Instance | BindingFlags.Public)?.GetValue(instance);
            return value is T result ? result : default;
        }

        private static object FieldObject(Type type, object instance, string name) =>
            type.GetField(name, BindingFlags.Instance | BindingFlags.Public)?.GetValue(instance);

        private static string NestedField(object instance, string name)
        {
            if (instance == null) return string.Empty;
            return instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public)?.GetValue(instance)
                ?.ToString() ?? string.Empty;
        }

        private static T NestedField<T>(object instance, string name)
        {
            if (instance == null) return default;
            var value = instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public)?.GetValue(instance);
            return value is T result ? result : default;
        }

        private static Type FrameDebuggerUtilityType() => typeof(EditorWindow).Assembly.GetType(
            "UnityEditorInternal.FrameDebuggerInternal.FrameDebuggerUtility");

        private static int FrameEventCount()
        {
            var property = FrameDebuggerUtilityType().GetProperty("count", BindingFlags.Static | BindingFlags.Public);
            return property != null ? (int)property.GetValue(null) : 0;
        }

        private static void SetFrameDebuggerEnabled(bool enabled)
        {
            var windowType = typeof(EditorWindow).Assembly.GetType("UnityEditor.FrameDebuggerWindow");
            var window = EditorWindow.GetWindow(windowType, false, "Frame Debugger", true);
            if (enabled)
            {
                window.Show();
                ConfigureGameView();
                windowType.GetMethod("OpenPlayModeView", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    ?.Invoke(window, null);
                windowType.GetMethod("EnableFrameDebugger", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    ?.Invoke(window, null);
            }
            else
            {
                windowType.GetMethod("DisableFrameDebugger", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    ?.Invoke(window, null);
            }

            // Keep the direct utility call as a deterministic fallback after the window has
            // selected the actual Game View connection.
            FrameDebuggerUtilityType().GetMethod("SetEnabled", BindingFlags.Static | BindingFlags.Public)
                ?.Invoke(null, new object[] { enabled, 0 });
        }

        private static void SetFrameEventLimit(int limit)
        {
            FrameDebuggerUtilityType().GetProperty("limit", BindingFlags.Static | BindingFlags.Public)
                ?.SetValue(null, limit);
            ConfigureGameView();
        }

        private static string HierarchyPath(Transform transform)
        {
            var names = new Stack<string>();
            while (transform != null)
            {
                names.Push(transform.name);
                transform = transform.parent;
            }
            return string.Join("/", names);
        }

        private static string ArtifactRoot() => Path.GetFullPath(Path.Combine(Application.dataPath,
            "../TestArtifacts/GameViewShadowAudit"));
    }
}
