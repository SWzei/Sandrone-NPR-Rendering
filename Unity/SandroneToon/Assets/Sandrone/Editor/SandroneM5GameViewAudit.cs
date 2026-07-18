using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace SandroneToon.Editor
{
    [InitializeOnLoad]
    public static class SandroneM5GameViewAudit
    {
        private const string ScenePath = "Assets/Sandrone/Tests/Scenes/ToonCalibration_M5.unity";
        private const string PreFixShaderPath = "Assets/Sandrone/Tests/Generated/M5_PreFix_CBuffer_Reproduction.shader";
        private const string PreFixShadowShaderPath = "Assets/Sandrone/Tests/Generated/M4_PreFix_BrokenShadowLayout.shader";
        private const string StageKey = "Sandrone.M5GameView.Stage";
        private const string IndexKey = "Sandrone.M5GameView.Index";
        private const int Width = 768, Height = 1680;
        private static int ticks;
        private static Report pendingReport;
        private static List<int> pendingEventIndices;
        private static int pendingEventCursor;
        private static readonly string[] SourceTextureBySlot =
        {
            "tex/颜.png", "tex/颜.png", "tex/颜.png", "tex/颜.png", "tex/颜.png", "tex/颜.png",
            "tex/目.png", "tex/目齿轮2.png", "tex/目3.png", "tex/目_1.png", "tex/目光.png", "tex/颜.png",
            "tex/髮.png", "tex/髮.png", "tex/髮.png", "tex/体.png", "tex/髮.png", "tex/髮.png",
            "tex/体.png", "tex/肌.png", "tex/体.png", "tex/裙.png", "tex/裙.png", "tex/裙.png",
            "tex/裙.png", "tex/体.png", "tex/裙.png", "tex/裙.png", "tex/sp.png", "tex/sp.png", "tex/脸红.png"
        };
        [Serializable] public sealed class EventRecord
        {
            public int index, submesh, mappedSlot = -1, zWrite, stencilRef, frameSignatureCount;
            public float auditSlotId, responseType, featureGroup, rampRow, layerWeight, matCapIntensity, metalMaskFallback;
            public bool hasBaseMapST, hasBaseColor;
            public Vector4 baseMapST, baseColor;
            public string eventName, renderer, material, shader, realShader, pass, lightMode, renderTarget, cull, zTest, srcBlend, dstBlend, keywords, baseMap, ramp, faceMap, controlMap, matCap, candidateSlots;
        }
        [Serializable] public sealed class SlotRecord
        {
            public int slot, frameDebuggerMatchCount;
            public bool exactExpectedMaterial, exactExpectedBaseMap, sourceBaseHashMatch, mpbIsEmpty, mpbOverridesBaseMap, mpbOverridesBaseMapST, mpbOverridesBaseColor, mpbOverridesControlMap, mpbOverridesRamp, mpbOverridesMatCap;
            public float useFaceSdf, faceWeight, debugMode, mirrorBlend, auditSlotId, responseType, featureGroup, rampRow, layerWeight, matCapIntensity, metalMaskFallback, cullMode;
            public Vector4 baseMapST, baseColor, mpbBaseMapST, mpbBaseColor;
            public string sourceName, sourceTexture, sourceTexturePath, sourceTextureSha256, material, materialPath, materialGuid, expectedMaterialPath, expectedMaterialGuid, shader;
            public string baseMap, baseMapPath, baseMapGuid, baseMapSha256, expectedBaseMapPath, expectedBaseMapGuid;
            public string ramp, rampPath, rampGuid, faceMap, faceMapPath, faceMapGuid, controlMap, controlMapPath, controlMapGuid, matCap, matCapPath, matCapGuid;
            public string mpbBaseMap, mpbControlMap, mpbRamp, mpbMatCap, mpbFloatOverrides, frameDebuggerEvents;
        }
        [Serializable] public sealed class Report
        {
            public string generatedUtc, unityVersion, graphicsApi, graphicsDevice, scene, renderer, mesh, meshAssetPath, meshGuid, materialMapGuid, sourcePmxSha256;
            public int width = Width, height = Height, frameEventCount; public bool enteredPlayMode, exitedPlayMode, sceneReopenedAfterPlay, domainReloadEnabled, controllerStateRestored;
            public float m4M5LowerFrameMae; public int m4RedSkirtPixels, m5RedSkirtPixels;
            public List<EventRecord> events = new(); public List<SlotRecord> slots = new(); public List<string> captures = new(); public List<string> failures = new();
        }
        static SandroneM5GameViewAudit() { EditorApplication.update -= Update; EditorApplication.update += Update; }

        public static void RunFromCommandLine()
        {
            if (Application.isBatchMode) throw new InvalidOperationException("M5 Game View audit requires a visible Editor.");
            Directory.CreateDirectory(Root()); CleanCaptureOutputs(); PreparePreFixShader(); EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            SessionState.SetInt(StageKey, 1); SessionState.SetInt(IndexKey, 0); ticks = 0; ConfigureGameView(); EditorApplication.EnterPlaymode();
        }

        public static void RunFrameDebuggerOnlyFromCommandLine()
        {
            if (Application.isBatchMode) throw new InvalidOperationException("M5 Frame Debugger audit requires a visible Editor.");
            Directory.CreateDirectory(Root()); EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            SessionState.SetInt(StageKey, 40); SessionState.SetInt(IndexKey, 0); ticks = 0; ConfigureGameView(); EditorApplication.EnterPlaymode();
        }

        private static void Update()
        {
            var stage = SessionState.GetInt(StageKey, 0); if (stage == 0) return; ticks++; ConfigureGameView();
            if (ticks == 1) File.WriteAllText(Path.Combine(Root(), "StageStatus.txt"), $"stage={stage}; playing={EditorApplication.isPlaying}; utc={DateTime.UtcNow:O}");
            if (stage < 90 && !EditorApplication.isPlaying) return;
            if (ticks < 45) return;
            switch (stage)
            {
                case 1: ConfigureState("M4", 0, false); Shot("GameView_M4_Baseline_768x1680.png"); Next(2); break;
                case 2: if (!Ready("GameView_M4_Baseline_768x1680.png")) return; ConfigureState("PreFix", 0, true); Shot("GameView_M5_PreFixReproduction_768x1680.png"); Next(3); break;
                case 3: if (!Ready("GameView_M5_PreFixReproduction_768x1680.png")) return; ConfigureState("M5", 0, false); Shot("GameView_M5_AfterFix_768x1680.png"); SessionState.SetInt(IndexKey, 0); Next(10); break;
                case 10:
                    var i = SessionState.GetInt(IndexKey, 0); var angles = Enumerable.Range(0, 18).Select(x => x * 20).ToArray();
                    ConfigureState("M5", angles[i], false); Shot($"Sweep20/GameView_Face_{angles[i]:000}.png"); i++;
                    if (i < angles.Length) { SessionState.SetInt(IndexKey, i); Next(10); } else { SessionState.SetInt(IndexKey, 0); Next(20); } break;
                case 20:
                    i = SessionState.GetInt(IndexKey, 0); var cardinals = new[] { 0, 90, 180, 270 }; ConfigureGroundState(cardinals[i]); Shot($"Ground/GameView_Ground_{cardinals[i]:000}.png"); i++;
                    if (i < cardinals.Length) { SessionState.SetInt(IndexKey, i); Next(20); } else Next(29); break;
                // Pipeline asset changes can require a real rendered frame before ScreenCapture is valid.
                // Prepare each Mobile state in a separate stage so a stale/pre-fix frame cannot be accepted.
                case 29: ConfigureState("Mobile", 0, false); Next(30); break;
                case 30: ConfigureState("Mobile", 0, false); Shot("GameView_Mobile_000.png"); Next(31); break;
                case 31: if (!Ready("GameView_Mobile_000.png")) return; ConfigureState("Mobile", 90, false); Next(32); break;
                case 32: ConfigureState("Mobile", 90, false); Shot("GameView_Mobile_090.png"); Next(40); break;
                case 40: if (!Ready("GameView_Mobile_090.png")) return; ConfigureState("M5", 0, false); SetFrameDebugger(true); Next(41); break;
                case 41:
                    if (FrameCount() <= 0) return;
                    pendingReport = CreateReport(); pendingEventIndices = RelevantEventIndices(); pendingEventCursor = 0;
                    if (pendingEventIndices.Count == 0) { pendingReport.failures.Add("Frame Debugger exposed no character/ground draw events."); FinishReport(pendingReport); SetFrameDebugger(false); Next(90); EditorApplication.ExitPlaymode(); break; }
                    SetEventLimit(pendingEventIndices[0] + 1); Next(42); break;
                case 42:
                    AppendSelectedEvent(pendingReport, pendingEventIndices[pendingEventCursor]); pendingEventCursor++;
                    if (pendingEventCursor < pendingEventIndices.Count) { SetEventLimit(pendingEventIndices[pendingEventCursor] + 1); Next(42); break; }
                    FinishReport(pendingReport); SetFrameDebugger(false); Next(90); EditorApplication.ExitPlaymode(); break;
                case 90:
                    if (EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode) return;
                    var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single); var c = UnityEngine.Object.FindFirstObjectByType<SandroneM5Controller>();
                    var report = LoadReport(); report.exitedPlayMode = true; report.sceneReopenedAfterPlay = scene.IsValid(); report.controllerStateRestored = c != null && c.DebugMode == SandroneM5DebugMode.FinalToon && c.FaceSdfEnabled;
                    if (!report.controllerStateRestored) report.failures.Add("Controller state did not restore after Play/scene reopen."); SaveReport(report); AssetDatabase.DeleteAsset(PreFixShaderPath); AssetDatabase.DeleteAsset(PreFixShadowShaderPath); SessionState.SetInt(StageKey, 0); EditorApplication.Exit(report.failures.Count == 0 ? 0 : 1); break;
            }
        }

        private static void ConfigureState(string mode, int degrees, bool preFix)
        {
            var c = UnityEngine.Object.FindFirstObjectByType<SandroneM5Controller>(); var camera = UnityEngine.Object.FindFirstObjectByType<Camera>(); var renderer = c.TargetRenderer;
            if (mode == "M4") renderer.sharedMaterials = LoadMaterials(4);
            else if (mode == "PreFix") renderer.sharedMaterials = LoadPreFixMaterials();
            else renderer.sharedMaterials = LoadMaterials(5);
            var pipeline = AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>(mode == "Mobile" ? "Assets/Settings/Mobile_RPAsset.asset" : "Assets/Settings/PC_RPAsset.asset"); GraphicsSettings.defaultRenderPipeline = pipeline; QualitySettings.renderPipeline = pipeline;
            c.CharacterRoot.SetPositionAndRotation(Vector3.zero, Quaternion.identity); c.DebugMode = SandroneM5DebugMode.FinalToon; c.FaceSdfEnabled = true; c.MetalEnabled = true; c.StockingEnabled = true; c.HairOverlayEnabled = true;
            var r = degrees * Mathf.Deg2Rad; var direction = new Vector3(Mathf.Sin(r), .28f, Mathf.Cos(r)).normalized; c.SetLightDirectionToSource(direction); c.Apply(true);
            if (mode != "M4") SetMirrorBlock(c, preFix ? .001f : .10f);
            camera.orthographic = true; camera.transform.position = new Vector3(0, .82f, 4); camera.transform.rotation = Quaternion.LookRotation(Vector3.back, Vector3.up); camera.orthographicSize = .92f; camera.backgroundColor = new Color(.153f, .149f, .149f, 1); ConfigureGameView();
        }
        private static void ConfigureGroundState(int degrees)
        {
            ConfigureState("M5", degrees, false); var camera = UnityEngine.Object.FindFirstObjectByType<Camera>(); var target = new Vector3(0, .25f, 0); camera.orthographic = false; camera.fieldOfView = 42; camera.transform.position = new Vector3(2.8f, 2.2f, 4.2f); camera.transform.rotation = Quaternion.LookRotation(target - camera.transform.position, Vector3.up);
        }
        private static void SetMirrorBlock(SandroneM5Controller c, float value)
        {
            var block = new MaterialPropertyBlock(); var id = Shader.PropertyToID("_FaceMirrorBlendWidth"); for (var i = 0; i <= 1; i++) { block.Clear(); c.TargetRenderer.GetPropertyBlock(block, i); block.SetFloat(id, value); c.TargetRenderer.SetPropertyBlock(block, i); }
        }
        private static Material[] LoadMaterials(int phase)
        {
            var map = AssetDatabase.LoadAssetAtPath<SandroneMaterialMap>(SandroneM0Bootstrap.MaterialMapPath); var result = new Material[31];
            foreach (var entry in map.Entries)
            {
                var useM5Face = phase == 5 && entry.sourceIndex <= 1;
                result[entry.sourceIndex] = AssetDatabase.LoadAssetAtPath<Material>(useM5Face
                    ? SandroneM5Bootstrap.MaterialPath(entry.sourceIndex, entry.materialAssetPath)
                    : SandroneM4Bootstrap.MaterialPath(entry.sourceIndex, entry.materialAssetPath));
            }
            return result;
        }
        private static Material[] LoadPreFixMaterials()
        {
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(PreFixShaderPath); if (shader == null) throw new InvalidOperationException("Pre-fix reproduction shader missing.");
            var source = LoadMaterials(5); var result = new Material[source.Length];
            for (var i = 0; i < source.Length; i++)
            {
                // Construct against the legacy layout first. Changing shader after cloning can
                // retain a GPU buffer created for the fixed layout and creates an unrelated cyan debug output.
                result[i] = new Material(shader) { name = source[i].name + "_PreFixCBuffer" };
                result[i].CopyPropertiesFromMaterial(source[i]); result[i].renderQueue = source[i].renderQueue;
                if (i <= 1) result[i].EnableKeyword("_SANDRONE_FACE"); else result[i].DisableKeyword("_SANDRONE_FACE");
            }
            return result;
        }
        private static void PreparePreFixShader()
        {
            var generatedPath = Path.GetFullPath(Path.Combine(Application.dataPath, "../", PreFixShaderPath)); var generatedShadowPath = Path.GetFullPath(Path.Combine(Application.dataPath, "../", PreFixShadowShaderPath));
            Directory.CreateDirectory(Path.GetDirectoryName(generatedPath)!);
            var brokenShadow = File.ReadAllText(Path.Combine(Application.dataPath, "Sandrone/Shaders/SandroneMaterialResponseM4.shader"))
                .Replace("Shader \"Sandrone/M4/MaterialResponse\"", "Shader \"Hidden/Sandrone/M4/PreFixBrokenShadowLayout\"");
            const string sharedField = "                float _M4DebugMode;";
            var shadowField = brokenShadow.LastIndexOf(sharedField, StringComparison.Ordinal);
            if (shadowField < 0 || shadowField == brokenShadow.IndexOf(sharedField, StringComparison.Ordinal))
                throw new InvalidOperationException("Failed to locate the ShadowCaster-only CBUFFER field for the known broken-layout reproduction.");
            // Reproduce only the historical ShadowCaster incompatibility. Removing the Forward
            // declaration too would create an unrelated compile error and contaminate the audit.
            brokenShadow = brokenShadow.Remove(shadowField, sharedField.Length);
            if (brokenShadow.LastIndexOf(sharedField, StringComparison.Ordinal) != brokenShadow.IndexOf(sharedField, StringComparison.Ordinal))
                throw new InvalidOperationException("Broken-layout reproduction must retain exactly one Forward declaration.");
            File.WriteAllText(generatedShadowPath, brokenShadow); AssetDatabase.ImportAsset(PreFixShadowShaderPath, ImportAssetOptions.ForceSynchronousImport);
            var source = File.ReadAllText(Path.Combine(Application.dataPath, "Sandrone/Shaders/SandroneFaceSDFM5.shader"))
                .Replace("Shader \"Sandrone/M5/FaceSDF\"", "Shader \"Hidden/Sandrone/M5/PreFixCBufferReproduction\"")
                .Replace("UsePass \"Sandrone/M4/MaterialResponse/ShadowCaster\"", "UsePass \"Hidden/Sandrone/M4/PreFixBrokenShadowLayout/ShadowCaster\"");
            File.WriteAllText(generatedPath, source); AssetDatabase.ImportAsset(PreFixShaderPath, ImportAssetOptions.ForceSynchronousImport);
        }

        private static Report CreateReport()
        {
            var report = new Report { generatedUtc = DateTime.UtcNow.ToString("O"), unityVersion = Application.unityVersion, graphicsApi = SystemInfo.graphicsDeviceType.ToString(), graphicsDevice = SystemInfo.graphicsDeviceName, scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().path, frameEventCount = FrameCount(), enteredPlayMode = EditorApplication.isPlaying, domainReloadEnabled = !EditorSettings.enterPlayModeOptionsEnabled || (EditorSettings.enterPlayModeOptions & EnterPlayModeOptions.DisableDomainReload) == 0 };
            report.captures = Directory.GetFiles(Root(), "*.png", SearchOption.AllDirectories).Select(x => Path.GetRelativePath(Root(), x)).OrderBy(x => x).ToList();
            var c = UnityEngine.Object.FindFirstObjectByType<SandroneM5Controller>(); var renderer = c.TargetRenderer; var block = new MaterialPropertyBlock();
            var map = AssetDatabase.LoadAssetAtPath<SandroneMaterialMap>(SandroneM0Bootstrap.MaterialMapPath);
            var sharedMesh = (renderer as SkinnedMeshRenderer)?.sharedMesh ?? renderer.GetComponent<MeshFilter>()?.sharedMesh;
            report.renderer = renderer.gameObject.name; report.mesh = sharedMesh != null ? sharedMesh.name : "";
            report.meshAssetPath = AssetDatabase.GetAssetPath(sharedMesh); report.meshGuid = AssetDatabase.AssetPathToGUID(report.meshAssetPath);
            report.materialMapGuid = AssetDatabase.AssetPathToGUID(SandroneM0Bootstrap.MaterialMapPath); report.sourcePmxSha256 = map.SourcePmxSha256;
            for (var i = 0; i < 31; i++)
            {
                var entry = map.Entries.First(x => x.sourceIndex == i); var m = renderer.sharedMaterials[i]; var isFace = m.HasProperty("_UseFaceSDF");
                var expectedMaterialPath = isFace ? SandroneM5Bootstrap.MaterialPath(i, entry.materialAssetPath) : SandroneM4Bootstrap.MaterialPath(i, entry.materialAssetPath);
                var baseMap = m.GetTexture("_BaseMap"); var ramp = m.GetTexture("_RampMap"); var faceMap = isFace ? m.GetTexture("_FaceMap") : null; var control = m.GetTexture("_ControlMap"); var matCap = m.GetTexture("_MatCapMap");
                var sourceTexture = SourceTextureBySlot[i]; var sourceTexturePath = Path.GetFullPath(Path.Combine(Application.dataPath, "../../..", "【桑多涅】", sourceTexture));
                block.Clear(); renderer.GetPropertyBlock(block, i);
                var floatOverrides = new[] { "_M4DebugMode", "_M5DebugMode", "_M4FeatureWeight", "_FaceSDFWeight", "_FaceMirrorBlendWidth", "_M5AuditSlotId" }
                    .Where(x => block.HasFloat(x)).Select(x => $"{x}={block.GetFloat(x):R}")
                    .Concat(new[] { "_HeadForwardWS", "_HeadRightWS", "_HeadUpWS" }.Where(x => block.HasVector(x)).Select(x => $"{x}={block.GetVector(x)}"));
                report.slots.Add(new SlotRecord
                {
                    slot = i, sourceName = entry.sourceName, sourceTexture = sourceTexture, sourceTexturePath = sourceTexturePath, sourceTextureSha256 = Sha256(sourceTexturePath),
                    material = m.name, materialPath = AssetDatabase.GetAssetPath(m), materialGuid = AssetGuid(m), expectedMaterialPath = expectedMaterialPath, expectedMaterialGuid = AssetDatabase.AssetPathToGUID(expectedMaterialPath), exactExpectedMaterial = AssetDatabase.GetAssetPath(m) == expectedMaterialPath,
                    shader = m.shader.name, useFaceSdf = isFace ? m.GetFloat("_UseFaceSDF") : 0f, faceWeight = isFace ? block.GetFloat("_FaceSDFWeight") : 0f, debugMode = block.GetFloat(isFace ? "_M5DebugMode" : "_M4DebugMode"), mirrorBlend = isFace ? block.GetFloat("_FaceMirrorBlendWidth") : 0f, auditSlotId = isFace ? block.GetFloat("_M5AuditSlotId") : -1f,
                    responseType = m.GetFloat("_ResponseType"), featureGroup = m.GetFloat("_FeatureGroup"), rampRow = m.GetFloat("_RampRow"), layerWeight = m.GetFloat("_LayerWeight"), matCapIntensity = m.GetFloat("_MatCapIntensity"), metalMaskFallback = m.GetFloat("_MetalMaskFallback"), cullMode = m.GetFloat("_Cull"),
                    baseMap = baseMap?.name, baseMapPath = AssetDatabase.GetAssetPath(baseMap), baseMapGuid = AssetGuid(baseMap), baseMapSha256 = Sha256(AssetDatabase.GetAssetPath(baseMap)), expectedBaseMapPath = entry.baseTextureAssetPath, expectedBaseMapGuid = AssetDatabase.AssetPathToGUID(entry.baseTextureAssetPath), exactExpectedBaseMap = AssetDatabase.GetAssetPath(baseMap) == entry.baseTextureAssetPath, sourceBaseHashMatch = Sha256(sourceTexturePath) == Sha256(AssetDatabase.GetAssetPath(baseMap)),
                    baseMapST = TextureST(m, "_BaseMap"), baseColor = m.GetColor("_BaseColor"),
                    ramp = ramp?.name, rampPath = AssetDatabase.GetAssetPath(ramp), rampGuid = AssetGuid(ramp), faceMap = faceMap?.name ?? "", faceMapPath = AssetDatabase.GetAssetPath(faceMap), faceMapGuid = AssetGuid(faceMap),
                    controlMap = control?.name, controlMapPath = AssetDatabase.GetAssetPath(control), controlMapGuid = AssetGuid(control), matCap = matCap?.name, matCapPath = AssetDatabase.GetAssetPath(matCap), matCapGuid = AssetGuid(matCap),
                    mpbIsEmpty = block.isEmpty, mpbOverridesBaseMap = block.HasTexture("_BaseMap"), mpbOverridesBaseMapST = block.HasVector("_BaseMap_ST"), mpbOverridesBaseColor = block.HasColor("_BaseColor"), mpbOverridesControlMap = block.HasTexture("_ControlMap"), mpbOverridesRamp = block.HasTexture("_RampMap"), mpbOverridesMatCap = block.HasTexture("_MatCapMap"),
                    mpbBaseMap = block.GetTexture("_BaseMap")?.name ?? "", mpbBaseMapST = block.GetVector("_BaseMap_ST"), mpbBaseColor = block.GetColor("_BaseColor"), mpbControlMap = block.GetTexture("_ControlMap")?.name ?? "", mpbRamp = block.GetTexture("_RampMap")?.name ?? "", mpbMatCap = block.GetTexture("_MatCapMap")?.name ?? "", mpbFloatOverrides = string.Join(";", floatOverrides)
                });
            }
            return report;
        }

        private static List<int> RelevantEventIndices()
        {
            var utility = Utility(); var getName = utility.GetMethod("GetFrameEventInfoName", BindingFlags.Static | BindingFlags.Public); var getObject = utility.GetMethod("GetFrameEventObject", BindingFlags.Static | BindingFlags.Public);
            var indices = new List<int>(); var map = new List<string>();
            for (var i = 0; i < FrameCount(); i++)
            {
                var name = getName.Invoke(null, new object[] { i }) as string ?? ""; var obj = getObject.Invoke(null, new object[] { i }) as UnityEngine.Object;
                map.Add($"{i}\t{name}\t{obj?.GetType().Name ?? "null"}\t{obj?.name ?? ""}");
                if (name.IndexOf("Draw", StringComparison.OrdinalIgnoreCase) >= 0 || obj is Renderer || obj is Material || obj is Mesh) indices.Add(i);
            }
            File.WriteAllLines(Path.Combine(Root(), "FrameEventIndexMap.tsv"), map);
            return indices.Count > 0 ? indices : Enumerable.Range(0, FrameCount()).ToList();
        }

        private static void AppendSelectedEvent(Report report, int i)
        {
            var utility = Utility(); var dataType = typeof(EditorWindow).Assembly.GetType("UnityEditorInternal.FrameDebuggerInternal.FrameDebuggerEventData"); var getData = utility.GetMethod("GetFrameEventData", BindingFlags.Static | BindingFlags.Public); var getName = utility.GetMethod("GetFrameEventInfoName", BindingFlags.Static | BindingFlags.Public);
            var data = Activator.CreateInstance(dataType); if (!(bool)getData.Invoke(null, new[] { (object)i, data })) return;
            var shader = Field<string>(dataType, data, "m_OriginalShaderName"); var realShader = Field<string>(dataType, data, "m_RealShaderName"); var pass = Field<string>(dataType, data, "m_PassName");
            var blend = Obj(dataType, data, "m_BlendState"); var raster = Obj(dataType, data, "m_RasterState"); var depth = Obj(dataType, data, "m_DepthState"); var shaderInfo = Obj(dataType, data, "m_ShaderInfo");
            var submesh = Field<int>(dataType, data, "m_MeshSubset"); var componentId = Field<EntityId>(dataType, data, "m_ComponentEntityId"); var component = EditorUtility.EntityIdToObject(componentId) as Component;
            var eventObject = utility.GetMethod("GetFrameEventObject", BindingFlags.Static | BindingFlags.Public)?.Invoke(null, new object[] { i });
            var eventRenderer = component as Renderer ?? component?.GetComponent<Renderer>() ?? eventObject as Renderer ?? (eventObject as Component)?.GetComponent<Renderer>();
            // Unity 6000.5 reports m_MeshSubset=0 for these split SRP draws, so it is not
            // a valid material-slot identity. Do not manufacture a slot-0 material name.
            var eventMaterial = eventObject as Material;
            var baseMap = Texture(shaderInfo, "_BaseMap"); var controlMap = Texture(shaderInfo, "_ControlMap"); var auditSlotId = Float(shaderInfo, "_M5AuditSlotId", -1); var stencilRef = Field<int>(dataType, data, "m_StencilRef");
            var mappedSlot = shader == "Sandrone/M5/FaceSDF" && stencilRef >= 0 && stencilRef < 31 ? stencilRef : (auditSlotId >= 0 && auditSlotId < 31 ? Mathf.RoundToInt(auditSlotId) : -1);
            if (eventRenderer == null && (shader == "Sandrone/M5/FaceSDF" || shader == "Sandrone/M4/MaterialResponse")) eventRenderer = UnityEngine.Object.FindFirstObjectByType<SandroneM5Controller>()?.TargetRenderer;
            if (eventMaterial == null && mappedSlot >= 0 && eventRenderer != null) eventMaterial = eventRenderer.sharedMaterials[mappedSlot];
            var hasBaseMapST = TryVector(shaderInfo, "_BaseMap_ST", out var baseMapST); var hasBaseColor = TryVector(shaderInfo, "_BaseColor", out var baseColor);
            report.events.Add(new EventRecord { index = i, eventName = getName.Invoke(null, new object[] { i }) as string,
                renderer = eventRenderer != null ? eventRenderer.gameObject.name : "", material = eventMaterial != null ? eventMaterial.name : "",
                shader = shader, realShader = realShader, pass = pass, lightMode = Field<string>(dataType, data, "m_PassLightMode"), submesh = submesh, mappedSlot = mappedSlot, auditSlotId = auditSlotId, stencilRef = stencilRef,
                renderTarget = Field<string>(dataType, data, "m_RenderTargetName"), cull = Nested(raster, "m_CullMode"), zWrite = Nested<int>(depth, "m_DepthWrite"), zTest = Nested(depth, "m_DepthFunc"), srcBlend = Nested(blend, "m_SrcBlend"), dstBlend = Nested(blend, "m_DstBlend"),
                keywords = Keywords(shaderInfo), baseMap = baseMap, ramp = Texture(shaderInfo, "_RampMap"), faceMap = Texture(shaderInfo, "_FaceMap"), controlMap = controlMap, matCap = Texture(shaderInfo, "_MatCapMap"),
                hasBaseMapST = hasBaseMapST, hasBaseColor = hasBaseColor, baseMapST = baseMapST, baseColor = baseColor,
                responseType = Float(shaderInfo, "_ResponseType", float.NaN), featureGroup = Float(shaderInfo, "_FeatureGroup", float.NaN), rampRow = Float(shaderInfo, "_RampRow", float.NaN), layerWeight = Float(shaderInfo, "_LayerWeight", float.NaN), matCapIntensity = Float(shaderInfo, "_MatCapIntensity", float.NaN), metalMaskFallback = Float(shaderInfo, "_MetalMaskFallback", float.NaN) });
        }

        private static void FinishReport(Report report)
        {
            foreach (var slot in new[] { 0, 1 }) if (!report.events.Any(x => x.pass == "M5FaceSDF" && x.mappedSlot == slot)) report.failures.Add($"Frame Debugger M5 face forward event missing for slot {slot}.");
            if (report.events.Count(x => x.pass == "M4MaterialResponse" && x.shader == "Sandrone/M4/MaterialResponse") != 29)
                report.failures.Add("Frame Debugger must expose exactly 29 unchanged M4 non-face Forward events.");
            var forward = report.events.Where(x => x.pass == "M5FaceSDF" || x.pass == "M4MaterialResponse").ToList();
            foreach (var frameEvent in forward)
            {
                var candidates = report.slots.Where(x => FrameSignature(x) == FrameSignature(frameEvent)).Select(x => x.slot).ToArray();
                frameEvent.candidateSlots = string.Join(",", candidates); frameEvent.frameSignatureCount = candidates.Length;
                if (candidates.Length == 1) frameEvent.mappedSlot = candidates[0];
                var candidateRecords = report.slots.Where(x => candidates.Contains(x.slot)).ToArray();
                if (frameEvent.hasBaseMapST && candidateRecords.Length > 0 && !candidateRecords.Any(x => (x.baseMapST - frameEvent.baseMapST).sqrMagnitude < 1e-8f))
                    report.failures.Add($"Frame event {frameEvent.index} effective _BaseMap_ST does not match its Renderer material candidates.");
                if (frameEvent.hasBaseColor && candidateRecords.Length > 0 && !candidateRecords.Any(x => (x.baseColor - frameEvent.baseColor).sqrMagnitude < 1e-8f))
                    report.failures.Add($"Frame event {frameEvent.index} effective _BaseColor does not match its Renderer material candidates.");
            }
            foreach (var slot in report.slots)
            {
                var matches = forward.Where(x => FrameSignature(x) == FrameSignature(slot)).Select(x => x.index).ToArray();
                slot.frameDebuggerMatchCount = matches.Length; slot.frameDebuggerEvents = string.Join(",", matches);
                if (!slot.exactExpectedMaterial) report.failures.Add($"Renderer slot {slot.slot} material path/GUID differs from the phase baseline.");
                if (!slot.exactExpectedBaseMap) report.failures.Add($"Renderer slot {slot.slot} BaseMap differs from SandroneMaterialMap baseline.");
                if (!slot.sourceBaseHashMatch) report.failures.Add($"Renderer slot {slot.slot} BaseMap bytes differ from PMX source texture {slot.sourceTexture}.");
                if (slot.mpbOverridesBaseMap || slot.mpbOverridesBaseMapST || slot.mpbOverridesBaseColor || slot.mpbOverridesControlMap || slot.mpbOverridesRamp || slot.mpbOverridesMatCap)
                    report.failures.Add($"Renderer slot {slot.slot} has an MPB override on a material/texture binding property.");
                if (slot.frameDebuggerMatchCount == 0) report.failures.Add($"Renderer slot {slot.slot} has no matching effective Frame Debugger Forward signature.");
                if (slot.slot > 1 && (slot.shader != "Sandrone/M4/MaterialResponse" || slot.useFaceSdf != 0f))
                    report.failures.Add($"Non-face slot {slot.slot} is not an exact M4 shader binding.");
                if (slot.slot >= 20 && slot.slot <= 27 && (slot.baseMapST - new Vector4(1, 1, 0, 0)).sqrMagnitude > 1e-8f)
                    report.failures.Add($"Skirt slot {slot.slot} has non-baseline _BaseMap_ST {slot.baseMapST}.");
                if (slot.slot >= 20 && slot.slot <= 27 && (slot.baseColor - Vector4.one).sqrMagnitude > 1e-8f)
                    report.failures.Add($"Skirt slot {slot.slot} has non-white _BaseColor {slot.baseColor}.");
                if (slot.slot == 21 && Mathf.RoundToInt(slot.cullMode) != (int)CullMode.Back)
                    report.failures.Add($"Skirt slot 21 dark back-facing layer must use Cull Back, actual={slot.cullMode:R}.");
            }
            foreach (var group in report.slots.GroupBy(FrameSignature))
            {
                var actual = forward.Count(x => FrameSignature(x) == group.Key);
                if (actual != group.Count()) report.failures.Add($"Frame Debugger signature multiset mismatch: renderer slots={group.Count()}, frame events={actual}, signature={group.Key}");
            }
            if (forward.Any(x => !x.hasBaseMapST || !x.hasBaseColor)) report.failures.Add("Frame Debugger did not expose _BaseMap_ST/_BaseColor for every character Forward draw.");
            if (!report.events.Any(x => x.pass == "M3ShadowReceiver")) report.failures.Add("Frame Debugger ground receiver event missing.");
            ValidateCaptureEvidence(report);
            SaveReport(report);
        }

        private static void ValidateCaptureEvidence(Report report)
        {
            var m4 = LoadCapture("GameView_M4_Baseline_768x1680.png", report);
            var pc = LoadCapture("GameView_M5_AfterFix_768x1680.png", report);
            var mobile0 = LoadCapture("GameView_Mobile_000.png", report);
            var mobile90 = LoadCapture("GameView_Mobile_090.png", report);
            if (m4 == null || pc == null || mobile0 == null || mobile90 == null) return;
            report.m4M5LowerFrameMae = MaeRect(m4, pc, 48, 0, Width - 48, 1260);
            if (report.m4M5LowerFrameMae > .001f) report.failures.Add($"M4/M5 lower-frame non-face MAE is {report.m4M5LowerFrameMae:F4}, expected exact preservation.");
            report.m4RedSkirtPixels = RedSkirtPixels(m4); report.m5RedSkirtPixels = RedSkirtPixels(pc);
            if (report.m4RedSkirtPixels < 50000 || report.m5RedSkirtPixels < 50000)
                report.failures.Add($"Long-skirt red evidence is insufficient (M4={report.m4RedSkirtPixels}, M5={report.m5RedSkirtPixels}, expected >=50000 each).");
            var cyan = mobile0.Count(p => p.r < 16 && p.g > 220 && p.b > 220);
            if (cyan > mobile0.Length / 1000) report.failures.Add($"Mobile 0 capture contains controlled/debug cyan output ({cyan} pixels).");
            var pcMobileMae = Mae(pc, mobile0);
            if (pcMobileMae > 5f) report.failures.Add($"PC/Mobile angle 0 full-frame MAE is {pcMobileMae:F3}, expected <= 5.000.");
            var mobileResponseMae = Mae(mobile0, mobile90);
            if (mobileResponseMae <= .05f) report.failures.Add($"Mobile light rotation produced no meaningful response (MAE={mobileResponseMae:F3}).");
        }

        private static Color32[] LoadCapture(string relative, Report report)
        {
            var path = Path.Combine(Root(), relative);
            if (!Ready(relative)) { report.failures.Add($"Missing Game View capture: {relative}."); return null; }
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false, false);
            if (!texture.LoadImage(File.ReadAllBytes(path), false)) { UnityEngine.Object.DestroyImmediate(texture); report.failures.Add($"Unreadable Game View capture: {relative}."); return null; }
            if (texture.width != Width || texture.height != Height) report.failures.Add($"{relative} is {texture.width}x{texture.height}, expected {Width}x{Height}.");
            var pixels = texture.GetPixels32(); UnityEngine.Object.DestroyImmediate(texture); return pixels;
        }

        private static float Mae(Color32[] a, Color32[] b)
        {
            if (a.Length != b.Length) return float.PositiveInfinity;
            double sum = 0; for (var i = 0; i < a.Length; i++) sum += Math.Abs(a[i].r - b[i].r) + Math.Abs(a[i].g - b[i].g) + Math.Abs(a[i].b - b[i].b);
            return (float)(sum / (a.Length * 3.0));
        }

        private static float MaeRect(Color32[] a, Color32[] b, int x0, int y0, int x1, int y1)
        {
            if (a.Length != Width * Height || b.Length != a.Length) return float.PositiveInfinity;
            double sum = 0; long count = 0;
            for (var y = y0; y < y1; y++) for (var x = x0; x < x1; x++)
            {
                var i = y * Width + x; sum += Math.Abs(a[i].r - b[i].r) + Math.Abs(a[i].g - b[i].g) + Math.Abs(a[i].b - b[i].b); count += 3;
            }
            return count == 0 ? float.PositiveInfinity : (float)(sum / count);
        }

        private static int RedSkirtPixels(Color32[] pixels)
        {
            var count = 0;
            for (var y = 160; y < 1260; y++) for (var x = 48; x < Width - 48; x++)
            {
                var p = pixels[y * Width + x];
                if (p.r > 48 && p.r > p.g * 1.35f && p.r > p.b * 1.20f) count++;
            }
            return count;
        }

        private static void CleanCaptureOutputs()
        {
            foreach (var path in Directory.GetFiles(Root(), "*.png", SearchOption.AllDirectories)) File.Delete(path);
            foreach (var name in new[] { "M5GameViewAudit.json", "M5GameViewMetrics.json", "FrameEventIndexMap.tsv", "StageStatus.txt" }) { var path = Path.Combine(Root(), name); if (File.Exists(path)) File.Delete(path); }
        }

        private static string FrameSignature(SlotRecord x) => string.Join("|", x.shader, x.baseMap, x.ramp, x.faceMap, x.controlMap, x.matCap, F(x.responseType), F(x.featureGroup), F(x.rampRow), F(x.layerWeight), F(x.matCapIntensity), F(x.metalMaskFallback), CullName(x.cullMode));
        private static string FrameSignature(EventRecord x) => string.Join("|", x.shader, x.baseMap, x.ramp, x.faceMap, x.controlMap, x.matCap, F(x.responseType), F(x.featureGroup), F(x.rampRow), F(x.layerWeight), F(x.matCapIntensity), F(x.metalMaskFallback), x.cull);
        private static string CullName(float value) => Mathf.RoundToInt(value) switch { 1 => "Front", 2 => "Back", _ => "Off" };
        private static string F(float value) => float.IsNaN(value) ? "NaN" : (Mathf.Round(value * 10000f) / 10000f).ToString("R", System.Globalization.CultureInfo.InvariantCulture);
        private static Vector4 TextureST(Material material, string property)
        {
            var scale = material.GetTextureScale(property); var offset = material.GetTextureOffset(property);
            return new Vector4(scale.x, scale.y, offset.x, offset.y);
        }
        private static string AssetGuid(UnityEngine.Object value)
        {
            var path = AssetDatabase.GetAssetPath(value); return string.IsNullOrEmpty(path) ? "" : AssetDatabase.AssetPathToGUID(path);
        }
        private static string Sha256(string path)
        {
            if (string.IsNullOrEmpty(path)) return "";
            var absolute = Path.IsPathRooted(path) ? path : Path.GetFullPath(Path.Combine(Application.dataPath, "..", path));
            if (!File.Exists(absolute)) return "";
            using var stream = File.OpenRead(absolute); using var sha = SHA256.Create();
            return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }
        private static bool TryVector(object info, string property, out Vector4 result)
        {
            result = default; if (info == null) return false;
            foreach (var fieldName in new[] { "m_Vectors", "m_Colors" })
            {
                if (info.GetType().GetField(fieldName, AllInstance)?.GetValue(info) is not System.Collections.IEnumerable values) continue;
                foreach (var item in values)
                {
                    if (Nested(item, "m_Name") != property) continue;
                    var value = item?.GetType().GetField("m_Value", AllInstance)?.GetValue(item);
                    if (value is Vector4 vector) { result = vector; return true; }
                    if (value is Color color) { result = color; return true; }
                }
            }
            return false;
        }
        private static string Texture(object info, string property) { if (info == null) return ""; if (info.GetType().GetField("m_Textures", BindingFlags.Instance | BindingFlags.Public)?.GetValue(info) is not System.Collections.IEnumerable values) return ""; foreach (var item in values) if (Nested(item, "m_Name") == property) return Nested(item, "m_TextureName"); return ""; }
        private static float Float(object info, string property, float fallback) { if (info == null) return fallback; if (info.GetType().GetField("m_Floats", AllInstance)?.GetValue(info) is not System.Collections.IEnumerable values) return fallback; foreach (var item in values) if (Nested(item, "m_Name") == property) return Nested<float>(item, "m_Value"); return fallback; }
        private static string Keywords(object info) { if (info == null || info.GetType().GetField("m_Keywords", AllInstance)?.GetValue(info) is not System.Collections.IEnumerable values) return ""; return string.Join(" ", values.Cast<object>().Select(x => Nested(x, "m_Name")).Where(x => !string.IsNullOrEmpty(x))); }
        private const BindingFlags AllInstance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private static T Field<T>(Type t, object o, string n) { var v = t.GetField(n, AllInstance)?.GetValue(o); return v is T x ? x : default; }
        private static object Obj(Type t, object o, string n) => t.GetField(n, AllInstance)?.GetValue(o);
        private static string Nested(object o, string n) => o?.GetType().GetField(n, AllInstance)?.GetValue(o)?.ToString() ?? "";
        private static T Nested<T>(object o, string n) { var v = o?.GetType().GetField(n, AllInstance)?.GetValue(o); return v is T x ? x : default; }
        private static Type Utility() => typeof(EditorWindow).Assembly.GetType("UnityEditorInternal.FrameDebuggerInternal.FrameDebuggerUtility");
        private static int FrameCount() => (int)(Utility().GetProperty("count", BindingFlags.Static | BindingFlags.Public)?.GetValue(null) ?? 0);
        private static void SetEventLimit(int value) => Utility().GetProperty("limit", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.SetValue(null, value);
        private static void SetFrameDebugger(bool enabled) { var type = typeof(EditorWindow).Assembly.GetType("UnityEditor.FrameDebuggerWindow"); var w = EditorWindow.GetWindow(type, false, "Frame Debugger", true); if (enabled) { w.Show(); type.GetMethod("OpenPlayModeView", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.Invoke(w, null); type.GetMethod("EnableFrameDebugger", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.Invoke(w, null); } else type.GetMethod("DisableFrameDebugger", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.Invoke(w, null); Utility().GetMethod("SetEnabled", BindingFlags.Static | BindingFlags.Public)?.Invoke(null, new object[] { enabled, 0 }); }
        private static void ConfigureGameView() { var t = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView"); var w = EditorWindow.GetWindow(t, false, "Game", true); t.GetMethod("SetCustomResolution", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.Invoke(w, new object[] { new Vector2(Width, Height), "M5 Audit" }); w.Show(); w.Focus(); w.Repaint(); }
        private static void Shot(string relative) { var path = Path.Combine(Root(), relative); Directory.CreateDirectory(Path.GetDirectoryName(path)!); if (File.Exists(path)) File.Delete(path); ScreenCapture.CaptureScreenshot(path); }
        private static bool Ready(string relative) => File.Exists(Path.Combine(Root(), relative)) && new FileInfo(Path.Combine(Root(), relative)).Length > 1024;
        private static void Next(int stage) { SessionState.SetInt(StageKey, stage); ticks = 0; }
        private static Report LoadReport() => JsonUtility.FromJson<Report>(File.ReadAllText(Path.Combine(Root(), "M5GameViewAudit.json")));
        private static void SaveReport(Report report) => File.WriteAllText(Path.Combine(Root(), "M5GameViewAudit.json"), JsonUtility.ToJson(report, true));
        private static string Root() => Path.GetFullPath(Path.Combine(Application.dataPath, "../TestArtifacts/M5GameViewAudit"));
    }
}
