using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace SandroneToon.Editor
{
    [InitializeOnLoad]
    public static class SandroneM8GameViewAudit
    {
        private const string StageKey = "Sandrone.M8GameView.Stage";
        private const int Width = 768, Height = 1280;
        private static int ticks, eventCursor;
        private static List<int> eventIndices;
        private static Report pending;

        [Serializable] public sealed class EventRecord
        {
            public int index, mappedSlot = -1, zWrite, stencilRef;
            public float slotId, m8Role, emissionWeight, bloomThreshold, emissionIntensity;
            public string eventName, renderer, material, shader, pass, baseMap, emissionMask, renderTarget, cull, zTest, srcBlend, dstBlend;
        }

        [Serializable] public sealed class Report
        {
            public string generatedUtc, unityVersion, graphicsApi, graphicsDevice, scene;
            public int width = Width, height = Height, frameEventCount, m5ForwardCount, m6ForwardCount, m4ForwardCount;
            public int m8EyeCount, m8VfxCount, m0SwordCount, bloomEventCount, outlineCount, shadowCasterCount, receiverCount, redSkirtPixels;
            public float allOffFinalMae, bloomToggleMae, pcMobileMae;
            public bool enteredPlayMode, exitedPlayMode, sceneReopenedAfterPlay, controllerStateRestored;
            public List<EventRecord> events = new(); public List<string> captures = new(); public List<string> failures = new();
        }

        static SandroneM8GameViewAudit() { EditorApplication.update -= Update; EditorApplication.update += Update; }

        public static void RunFromCommandLine()
        {
            if (Application.isBatchMode) throw new InvalidOperationException("M8 Game View/Frame Debugger audit requires a visible Editor.");
            Directory.CreateDirectory(Root());
            foreach (var path in Directory.GetFiles(Root(), "*.png", SearchOption.AllDirectories)) File.Delete(path);
            foreach (var name in new[] { "M8GameViewAudit.json", "FrameEventIndexMap.tsv", "StageStatus.txt" })
            { var path = Path.Combine(Root(), name); if (File.Exists(path)) File.Delete(path); }
            EditorSceneManager.OpenScene(SandroneM8Bootstrap.ScenePath, OpenSceneMode.Single);
            ConfigureGameView(); SessionState.SetInt(StageKey, 1); ticks = 0; EditorApplication.EnterPlaymode();
        }

        private static void Update()
        {
            var stage = SessionState.GetInt(StageKey, 0); if (stage == 0) return;
            ticks++; ConfigureGameView();
            if (ticks == 1) File.WriteAllText(Path.Combine(Root(), "StageStatus.txt"), $"stage={stage}; playing={EditorApplication.isPlaying}; utc={DateTime.UtcNow:O}");
            if (stage < 90 && !EditorApplication.isPlaying) return;
            if (ticks < (stage == 8 ? 2 : 45)) return;
            switch (stage)
            {
                case 1: Configure(false, false, false, false, false); Shot("GameView_M8_AllOff_768x1280.png"); Next(2); break;
                case 2: if (!Ready("GameView_M8_AllOff_768x1280.png")) return; Configure(true, true, true, false, false); Shot("GameView_M8_BloomOff_768x1280.png"); Next(3); break;
                case 3: if (!Ready("GameView_M8_BloomOff_768x1280.png")) return; Configure(true, true, true, true, false); Shot("GameView_M8_Final_768x1280.png"); Next(4); break;
                case 4: if (!Ready("GameView_M8_Final_768x1280.png")) return; Configure(true, true, true, true, true); Next(5); break;
                case 5: Configure(true, true, true, true, true); Shot("GameView_Mobile_768x1280.png"); Next(6); break;
                case 6: if (!Ready("GameView_Mobile_768x1280.png")) return; Configure(true, true, true, true, false); SetFrameDebugger(true); Next(7); break;
                case 7:
                    if (FrameCount() <= 0) return;
                    pending = CreateReport(); eventIndices = RelevantEventIndices(); eventCursor = 0;
                    if (eventIndices.Count == 0) { pending.failures.Add("Frame Debugger exposed no draw/Bloom events."); Finish(pending); SetFrameDebugger(false); Next(90); EditorApplication.ExitPlaymode(); break; }
                    SetEventLimit(eventIndices[0] + 1); Next(8); break;
                case 8:
                    AppendEvent(pending, eventIndices[eventCursor]); eventCursor++;
                    if (eventCursor < eventIndices.Count) { SetEventLimit(eventIndices[eventCursor] + 1); Next(8); }
                    else { Finish(pending); SetFrameDebugger(false); Next(90); EditorApplication.ExitPlaymode(); }
                    break;
                case 90:
                    if (EditorApplication.isPlayingOrWillChangePlaymode) return;
                    var scene = EditorSceneManager.OpenScene(SandroneM8Bootstrap.ScenePath, OpenSceneMode.Single);
                    var controller = UnityEngine.Object.FindFirstObjectByType<SandroneM8VfxBloomController>(); var report = Load();
                    report.exitedPlayMode = true; report.sceneReopenedAfterPlay = scene.IsValid();
                    report.controllerStateRestored = controller != null && controller.EyeEmissionEnabled && controller.CrystalEmissionEnabled && controller.CrystalVisible && controller.BloomEnabled && controller.DebugMode == SandroneM8DebugMode.FinalColor;
                    if (!report.controllerStateRestored) report.failures.Add("M8 controller state did not restore after Play/scene reopen.");
                    Save(report); SessionState.SetInt(StageKey, 0); EditorApplication.Exit(report.failures.Count == 0 ? 0 : 1); break;
            }
        }

        private static void Configure(bool eye, bool crystalEmission, bool crystalVisible, bool bloom, bool mobile)
        {
            var m8 = UnityEngine.Object.FindFirstObjectByType<SandroneM8VfxBloomController>();
            var m7 = UnityEngine.Object.FindFirstObjectByType<SandroneM7OutlineController>();
            var m6 = UnityEngine.Object.FindFirstObjectByType<SandroneM6Controller>();
            var m5 = UnityEngine.Object.FindFirstObjectByType<SandroneM5Controller>();
            var m0 = UnityEngine.Object.FindFirstObjectByType<SandroneM0Controller>();
            var camera = UnityEngine.Object.FindFirstObjectByType<Camera>();
            var pipeline = AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>(mobile ? "Assets/Settings/Mobile_RPAsset.asset" : "Assets/Settings/PC_RPAsset.asset");
            GraphicsSettings.defaultRenderPipeline = pipeline; QualitySettings.renderPipeline = pipeline;
            m8.CharacterRenderer.transform.root.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            m5.DebugMode = SandroneM5DebugMode.FinalToon; m5.FaceSdfEnabled = true; m5.Apply(true);
            m6.DebugMode = SandroneM6DebugMode.FinalToon; m6.HairSpecularEnabled = true; m6.EyeLayersEnabled = true;
            m6.SetLightDirectionToSource(SandroneM3Bootstrap.DefaultDirectionToLight); m6.Apply(true);
            m0.eyeALWeight = 0f; m0.blushWeight = 0f; m0.Apply();
            m7.OutlineEnabled = true; m7.MasterWidth = 1f; m7.DebugMode = SandroneM7DebugMode.FinalColor; m7.Apply(true);
            m8.EyeEmissionEnabled = eye; m8.CrystalEmissionEnabled = crystalEmission; m8.CrystalVisible = crystalVisible;
            m8.BloomEnabled = bloom; m8.DebugMode = SandroneM8DebugMode.FinalColor; m8.Apply(true);
            camera.orthographic = true; camera.transform.position = new Vector3(0,.82f,4); camera.transform.rotation = Quaternion.LookRotation(Vector3.back, Vector3.up);
            camera.orthographicSize = .92f; camera.backgroundColor = new Color(.153f,.149f,.149f,1); camera.allowHDR = true;
            var data = camera.GetUniversalAdditionalCameraData(); data.renderPostProcessing = true; data.antialiasing = AntialiasingMode.None;
            ConfigureGameView();
        }

        private static Report CreateReport() => new()
        {
            generatedUtc = DateTime.UtcNow.ToString("O"), unityVersion = Application.unityVersion,
            graphicsApi = SystemInfo.graphicsDeviceType.ToString(), graphicsDevice = SystemInfo.graphicsDeviceName,
            scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().path, frameEventCount = FrameCount(), enteredPlayMode = EditorApplication.isPlaying,
            captures = Directory.GetFiles(Root(), "*.png", SearchOption.AllDirectories).Select(x => Path.GetRelativePath(Root(), x)).OrderBy(x => x).ToList()
        };

        private static List<int> RelevantEventIndices()
        {
            var utility = Utility(); var getName = utility.GetMethod("GetFrameEventInfoName", BindingFlags.Static | BindingFlags.Public);
            var getObject = utility.GetMethod("GetFrameEventObject", BindingFlags.Static | BindingFlags.Public); var indices = new List<int>(); var rows = new List<string>();
            for (var i = 0; i < FrameCount(); i++)
            {
                var name = getName.Invoke(null, new object[] { i }) as string ?? ""; var value = getObject.Invoke(null, new object[] { i }) as UnityEngine.Object;
                rows.Add($"{i}\t{name}\t{value?.GetType().Name ?? "null"}\t{value?.name ?? ""}");
                if (name.IndexOf("Draw", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("Bloom", StringComparison.OrdinalIgnoreCase) >= 0 || value is Renderer || value is Material || value is Mesh) indices.Add(i);
            }
            File.WriteAllLines(Path.Combine(Root(), "FrameEventIndexMap.tsv"), rows); return indices.Count > 0 ? indices : Enumerable.Range(0, FrameCount()).ToList();
        }

        private static void AppendEvent(Report report, int index)
        {
            var utility = Utility(); var dataType = typeof(EditorWindow).Assembly.GetType("UnityEditorInternal.FrameDebuggerInternal.FrameDebuggerEventData"); var data = Activator.CreateInstance(dataType);
            if (!(bool)utility.GetMethod("GetFrameEventData", BindingFlags.Static | BindingFlags.Public).Invoke(null, new[] { (object)index, data })) return;
            var shader = Field<string>(dataType,data,"m_OriginalShaderName"); var pass = Field<string>(dataType,data,"m_PassName");
            var blend=Obj(dataType,data,"m_BlendState");var raster=Obj(dataType,data,"m_RasterState");var depth=Obj(dataType,data,"m_DepthState");var shaderInfo=Obj(dataType,data,"m_ShaderInfo");
            var componentId=Field<EntityId>(dataType,data,"m_ComponentEntityId");var component=EditorUtility.EntityIdToObject(componentId) as Component;
            var eventObject=utility.GetMethod("GetFrameEventObject",BindingFlags.Static|BindingFlags.Public)?.Invoke(null,new object[]{index});
            var renderer=component as Renderer??component?.GetComponent<Renderer>()??eventObject as Renderer??(eventObject as Component)?.GetComponent<Renderer>();var material=eventObject as Material;
            var slotId=Float(shaderInfo,"_M7SlotId",Float(shaderInfo,"_M8AuditSlotId",-1f));var mapped=shader=="Sandrone/M7/Outline"&&slotId>=0&&slotId<31?Mathf.RoundToInt(slotId):-1;
            report.events.Add(new EventRecord
            {
                index=index,eventName=utility.GetMethod("GetFrameEventInfoName",BindingFlags.Static|BindingFlags.Public).Invoke(null,new object[]{index}) as string,
                renderer=renderer!=null?renderer.gameObject.name:"",material=material!=null?material.name:"",shader=shader,pass=pass,mappedSlot=mapped,slotId=slotId,
                baseMap=Texture(shaderInfo,"_BaseMap"),emissionMask=Texture(shaderInfo,"_EmissionMask"),
                m8Role=Float(shaderInfo,"_M8Role",-1f),emissionWeight=Float(shaderInfo,"_M8EmissionWeight",-1f),bloomThreshold=Float(shaderInfo,"_M8BloomThreshold",-1f),
                emissionIntensity=Float(shaderInfo,"_EmissionIntensity",-1f),stencilRef=Field<int>(dataType,data,"m_StencilRef"),renderTarget=Field<string>(dataType,data,"m_RenderTargetName"),
                cull=Nested(raster,"m_CullMode"),zWrite=Nested<int>(depth,"m_DepthWrite"),zTest=Nested(depth,"m_DepthFunc"),srcBlend=Nested(blend,"m_SrcBlend"),dstBlend=Nested(blend,"m_DstBlend")
            });
        }

        private static void Finish(Report report)
        {
            report.m5ForwardCount=report.events.Count(x=>x.pass=="M5FaceSDF"&&x.shader=="Sandrone/M5/FaceSDF");
            report.m6ForwardCount=report.events.Count(x=>x.pass=="M6HairEye"&&x.shader=="Sandrone/M6/HairEye");
            report.m4ForwardCount=report.events.Count(x=>x.pass=="M4MaterialResponse"&&x.shader=="Sandrone/M4/MaterialResponse");
            report.m8EyeCount=report.events.Count(x=>x.pass=="M8HairEyeEmission"&&x.shader=="Sandrone/M8/HairEyeEmission");
            report.m8VfxCount=report.events.Count(x=>x.pass=="M8VFXEmission"&&x.shader=="Sandrone/M8/VFXEmission");
            report.m0SwordCount=report.events.Count(x=>x.shader=="Sandrone/M0/UnlitBaseMap"&&x.pass=="M0Unlit");
            report.bloomEventCount=report.events.Count(x=>(x.shader??"").IndexOf("Bloom",StringComparison.OrdinalIgnoreCase)>=0||(x.eventName??"").IndexOf("Bloom",StringComparison.OrdinalIgnoreCase)>=0);
            report.outlineCount=report.events.Count(x=>x.pass=="M7Outline"&&x.shader=="Sandrone/M7/Outline");
            report.shadowCasterCount=report.events.Count(x=>x.pass=="ShadowCaster");report.receiverCount=report.events.Count(x=>x.pass=="M3ShadowReceiver");
            if(report.m5ForwardCount!=2)report.failures.Add($"Expected 2 M5 Forward, got {report.m5ForwardCount}.");
            if(report.m6ForwardCount!=10)report.failures.Add($"Expected 10 M6 Forward after slot 10 replacement, got {report.m6ForwardCount}.");
            if(report.m4ForwardCount!=18)report.failures.Add($"Expected 18 M4 Forward, got {report.m4ForwardCount}.");
            if(report.m8EyeCount!=1)report.failures.Add($"Expected one M8 EyeLight event, got {report.m8EyeCount}.");
            if(report.m8VfxCount!=1)report.failures.Add($"Expected one M8 crystal event, got {report.m8VfxCount}.");
            if(report.m0SwordCount!=1)report.failures.Add($"Expected one M0 sword-base event, got {report.m0SwordCount}.");
            if(report.bloomEventCount<1)report.failures.Add("Frame Debugger exposed no URP Bloom event.");
            if(report.outlineCount!=SandroneM7Bootstrap.EligibleSlots.Length)report.failures.Add($"Expected {SandroneM7Bootstrap.EligibleSlots.Length} outline events, got {report.outlineCount}.");
            if(report.shadowCasterCount<40)report.failures.Add($"ShadowCaster evidence insufficient: {report.shadowCasterCount}.");if(report.receiverCount!=1)report.failures.Add($"Expected one receiver, got {report.receiverCount}.");
            var eye=report.events.SingleOrDefault(x=>x.pass=="M8HairEyeEmission");var crystal=report.events.SingleOrDefault(x=>x.pass=="M8VFXEmission");
            if(eye!=null&&(!Mathf.Approximately(eye.m8Role,1f)||eye.emissionWeight<.99f||eye.bloomThreshold<=1f))report.failures.Add($"Eye MPB/role invalid: role={eye.m8Role}, weight={eye.emissionWeight}, threshold={eye.bloomThreshold}.");
            if(crystal!=null&&(!Mathf.Approximately(crystal.m8Role,2f)||crystal.emissionWeight<.99f||crystal.bloomThreshold<=1f))report.failures.Add($"Crystal MPB/role invalid: role={crystal.m8Role}, weight={crystal.emissionWeight}, threshold={crystal.bloomThreshold}.");
            ValidateCaptures(report); Save(report);
        }

        private static void ValidateCaptures(Report report)
        {
            var allOff=LoadPixels("GameView_M8_AllOff_768x1280.png",report);var bloomOff=LoadPixels("GameView_M8_BloomOff_768x1280.png",report);
            var final=LoadPixels("GameView_M8_Final_768x1280.png",report);var mobile=LoadPixels("GameView_Mobile_768x1280.png",report);
            if(allOff==null||bloomOff==null||final==null||mobile==null)return;
            report.allOffFinalMae=Mae(allOff,final);report.bloomToggleMae=Mae(bloomOff,final);report.pcMobileMae=Mae(final,mobile);report.redSkirtPixels=RedSkirtPixels(final);
            if(report.allOffFinalMae<=.03f)report.failures.Add($"M8 final response missing: {report.allOffFinalMae:F4}.");
            if(report.bloomToggleMae<=.01f)report.failures.Add($"Game View Bloom toggle response missing: {report.bloomToggleMae:F4}.");
            if(report.pcMobileMae>8f)report.failures.Add($"PC/Mobile Game View MAE too high: {report.pcMobileMae:F3}.");
            if(report.redSkirtPixels<50000)report.failures.Add($"Red skirt evidence insufficient: {report.redSkirtPixels}.");
        }

        private static Color32[] LoadPixels(string relative,Report report){var path=Path.Combine(Root(),relative);if(!Ready(relative)){report.failures.Add("Missing capture: "+relative);return null;}var t=new Texture2D(2,2);t.LoadImage(File.ReadAllBytes(path));if(t.width!=Width||t.height!=Height)report.failures.Add($"{relative} resolution {t.width}x{t.height}.");var p=t.GetPixels32();UnityEngine.Object.DestroyImmediate(t);return p;}
        private static float Mae(Color32[] a,Color32[] b){double s=0;for(var i=0;i<a.Length;i++)s+=Math.Abs(a[i].r-b[i].r)+Math.Abs(a[i].g-b[i].g)+Math.Abs(a[i].b-b[i].b);return(float)(s/(a.Length*3.0));}
        private static int RedSkirtPixels(Color32[] p){var c=0;for(var y=80;y<1100;y++)for(var x=32;x<Width-32;x++){var q=p[y*Width+x];if(q.r>48&&q.r>q.g*1.35f&&q.r>q.b*1.2f)c++;}return c;}

        private const BindingFlags AllInstance=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        private static T Field<T>(Type t,object o,string n){var v=t.GetField(n,AllInstance)?.GetValue(o);return v is T r?r:default;}
        private static object Obj(Type t,object o,string n)=>t.GetField(n,AllInstance)?.GetValue(o);
        private static string Nested(object o,string n)=>o?.GetType().GetField(n,AllInstance)?.GetValue(o)?.ToString()??"";
        private static T Nested<T>(object o,string n){var v=o?.GetType().GetField(n,AllInstance)?.GetValue(o);return v is T r?r:default;}
        private static float Float(object info,string property,float fallback){if(info?.GetType().GetField("m_Floats",AllInstance)?.GetValue(info)is not System.Collections.IEnumerable values)return fallback;foreach(var item in values)if(Nested(item,"m_Name")==property)return Nested<float>(item,"m_Value");return fallback;}
        private static string Texture(object info,string property){if(info?.GetType().GetField("m_Textures",BindingFlags.Instance|BindingFlags.Public)?.GetValue(info)is not System.Collections.IEnumerable values)return "";foreach(var item in values)if(Nested(item,"m_Name")==property)return Nested(item,"m_TextureName");return "";}
        private static Type Utility()=>typeof(EditorWindow).Assembly.GetType("UnityEditorInternal.FrameDebuggerInternal.FrameDebuggerUtility");
        private static int FrameCount()=>(int)(Utility().GetProperty("count",BindingFlags.Static|BindingFlags.Public)?.GetValue(null)??0);
        private static void SetEventLimit(int value)=>Utility().GetProperty("limit",BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic)?.SetValue(null,value);
        private static void SetFrameDebugger(bool enabled){var type=typeof(EditorWindow).Assembly.GetType("UnityEditor.FrameDebuggerWindow");var window=EditorWindow.GetWindow(type,false,"Frame Debugger",true);if(enabled){window.Show();type.GetMethod("OpenPlayModeView",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)?.Invoke(window,null);type.GetMethod("EnableFrameDebugger",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)?.Invoke(window,null);}else type.GetMethod("DisableFrameDebugger",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)?.Invoke(window,null);Utility().GetMethod("SetEnabled",BindingFlags.Static|BindingFlags.Public)?.Invoke(null,new object[]{enabled,0});}
        private static void ConfigureGameView(){var type=typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");var window=EditorWindow.GetWindow(type,false,"Game",true);type.GetMethod("SetCustomResolution",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)?.Invoke(window,new object[]{new Vector2(Width,Height),"M8 Audit"});window.Show();window.Focus();window.Repaint();}
        private static void Shot(string relative){var path=Path.Combine(Root(),relative);if(File.Exists(path))File.Delete(path);ScreenCapture.CaptureScreenshot(path);}
        private static bool Ready(string relative)=>File.Exists(Path.Combine(Root(),relative))&&new FileInfo(Path.Combine(Root(),relative)).Length>1024;
        private static void Next(int stage){SessionState.SetInt(StageKey,stage);ticks=0;}
        private static Report Load()=>JsonUtility.FromJson<Report>(File.ReadAllText(Path.Combine(Root(),"M8GameViewAudit.json")));
        private static void Save(Report report)=>File.WriteAllText(Path.Combine(Root(),"M8GameViewAudit.json"),JsonUtility.ToJson(report,true));
        private static string Root()=>Path.GetFullPath(Path.Combine(Application.dataPath,"../TestArtifacts/M8GameViewAudit"));
    }
}
