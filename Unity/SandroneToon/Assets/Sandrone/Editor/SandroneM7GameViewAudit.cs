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
    public static class SandroneM7GameViewAudit
    {
        private const string StageKey = "Sandrone.M7GameView.Stage";
        private const int Width = 768, Height = 1680;
        private static int ticks, eventCursor;
        private static List<int> eventIndices;
        private static Report pending;

        [Serializable] public sealed class EventRecord
        {
            public int index, mappedSlot = -1, zWrite, stencilRef;
            public float outlinePixels, outlineWidthWeight, outlineMasterWeight, slotId;
            public string eventName, renderer, material, shader, pass, renderTarget, cull, zTest, srcBlend, dstBlend;
        }
        [Serializable] public sealed class Report
        {
            public string generatedUtc, unityVersion, graphicsApi, graphicsDevice, scene;
            public int width = Width, height = Height, frameEventCount, m5ForwardCount, m6ForwardCount, m4ForwardCount;
            public int outlineCount, shadowCasterCount, receiverCount, redSkirtPixels;
            public float m6M7Mae, outlineToggleMae, outsideChangedRatio, pcMobileMae;
            public bool enteredPlayMode, exitedPlayMode, sceneReopenedAfterPlay, controllerStateRestored;
            public List<EventRecord> events = new(); public List<string> captures = new(); public List<string> failures = new();
        }

        static SandroneM7GameViewAudit() { EditorApplication.update -= Update; EditorApplication.update += Update; }

        public static void RunFromCommandLine()
        {
            if (Application.isBatchMode) throw new InvalidOperationException("M7 Game View/Frame Debugger audit requires a visible Editor.");
            Directory.CreateDirectory(Root());
            foreach (var path in Directory.GetFiles(Root(), "*.png", SearchOption.AllDirectories)) File.Delete(path);
            foreach (var name in new[] { "M7GameViewAudit.json", "FrameEventIndexMap.tsv", "StageStatus.txt" })
            { var path = Path.Combine(Root(), name); if (File.Exists(path)) File.Delete(path); }
            EditorSceneManager.OpenScene(SandroneM7Bootstrap.ScenePath, OpenSceneMode.Single);
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
                case 1: Configure(false, false); Shot("GameView_M6_Baseline_768x1680.png"); Next(2); break;
                case 2: if (!Ready("GameView_M6_Baseline_768x1680.png")) return; Configure(true, false); Shot("GameView_M7_768x1680.png"); Next(3); break;
                case 3: if (!Ready("GameView_M7_768x1680.png")) return; Configure(false, false); Shot("GameView_M7_OutlineOff_768x1680.png"); Next(4); break;
                case 4: if (!Ready("GameView_M7_OutlineOff_768x1680.png")) return; Configure(true, true); Next(5); break;
                case 5: Configure(true, true); Shot("GameView_Mobile_768x1680.png"); Next(6); break;
                case 6: if (!Ready("GameView_Mobile_768x1680.png")) return; Configure(true, false); SetFrameDebugger(true); Next(7); break;
                case 7:
                    if (FrameCount() <= 0) return;
                    pending = CreateReport(); eventIndices = RelevantEventIndices(); eventCursor = 0;
                    if (eventIndices.Count == 0) { pending.failures.Add("Frame Debugger exposed no draw events."); Finish(pending); SetFrameDebugger(false); Next(90); EditorApplication.ExitPlaymode(); break; }
                    SetEventLimit(eventIndices[0] + 1); Next(8); break;
                case 8:
                    AppendEvent(pending, eventIndices[eventCursor]); eventCursor++;
                    if (eventCursor < eventIndices.Count) { SetEventLimit(eventIndices[eventCursor] + 1); Next(8); }
                    else { Finish(pending); SetFrameDebugger(false); Next(90); EditorApplication.ExitPlaymode(); }
                    break;
                case 90:
                    if (EditorApplication.isPlayingOrWillChangePlaymode) return;
                    var scene = EditorSceneManager.OpenScene(SandroneM7Bootstrap.ScenePath, OpenSceneMode.Single);
                    var controller = UnityEngine.Object.FindFirstObjectByType<SandroneM7OutlineController>(); var report = Load();
                    report.exitedPlayMode = true; report.sceneReopenedAfterPlay = scene.IsValid();
                    report.controllerStateRestored = controller != null && controller.OutlineEnabled && Mathf.Approximately(controller.MasterWidth, 1f) && controller.DebugMode == SandroneM7DebugMode.FinalColor;
                    if (!report.controllerStateRestored) report.failures.Add("M7 controller state did not restore after Play/scene reopen.");
                    Save(report); SessionState.SetInt(StageKey, 0); EditorApplication.Exit(report.failures.Count == 0 ? 0 : 1); break;
            }
        }

        private static void Configure(bool outlineEnabled, bool mobile)
        {
            var m7 = UnityEngine.Object.FindFirstObjectByType<SandroneM7OutlineController>();
            var m6 = UnityEngine.Object.FindFirstObjectByType<SandroneM6Controller>();
            var m5 = UnityEngine.Object.FindFirstObjectByType<SandroneM5Controller>();
            var m0 = UnityEngine.Object.FindFirstObjectByType<SandroneM0Controller>();
            var camera = UnityEngine.Object.FindFirstObjectByType<Camera>();
            var pipeline = AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>(mobile ? "Assets/Settings/Mobile_RPAsset.asset" : "Assets/Settings/PC_RPAsset.asset");
            GraphicsSettings.defaultRenderPipeline = pipeline; QualitySettings.renderPipeline = pipeline;
            var root = m7.SourceRenderer.transform.root; root.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            m5.DebugMode = SandroneM5DebugMode.FinalToon; m5.FaceSdfEnabled = true; m5.Apply(true);
            m6.DebugMode = SandroneM6DebugMode.FinalToon; m6.HairSpecularEnabled = true; m6.EyeLayersEnabled = true;
            m6.SetLightDirectionToSource(SandroneM3Bootstrap.DefaultDirectionToLight); m6.Apply(true);
            m0.eyeALWeight = 0f; m0.blushWeight = 0f; m0.Apply();
            m7.OutlineEnabled = outlineEnabled; m7.MasterWidth = 1f; m7.DebugMode = SandroneM7DebugMode.FinalColor; m7.Apply(true);
            camera.orthographic = true; camera.transform.position = new Vector3(0,.82f,4); camera.transform.rotation = Quaternion.LookRotation(Vector3.back, Vector3.up);
            camera.orthographicSize = .92f; camera.backgroundColor = new Color(.153f,.149f,.149f,1); ConfigureGameView();
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
                if (name.IndexOf("Draw", StringComparison.OrdinalIgnoreCase) >= 0 || value is Renderer || value is Material || value is Mesh) indices.Add(i);
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
            var slotId=Float(shaderInfo,"_M7SlotId",-1f);var mapped=shader=="Sandrone/M7/Outline"&&slotId>=0&&slotId<31?Mathf.RoundToInt(slotId):-1;
            report.events.Add(new EventRecord
            {
                index=index,eventName=utility.GetMethod("GetFrameEventInfoName",BindingFlags.Static|BindingFlags.Public).Invoke(null,new object[]{index}) as string,
                renderer=renderer!=null?renderer.gameObject.name:"",material=material!=null?material.name:"",shader=shader,pass=pass,mappedSlot=mapped,slotId=slotId,
                stencilRef=Field<int>(dataType,data,"m_StencilRef"),renderTarget=Field<string>(dataType,data,"m_RenderTargetName"),
                cull=Nested(raster,"m_CullMode"),zWrite=Nested<int>(depth,"m_DepthWrite"),zTest=Nested(depth,"m_DepthFunc"),srcBlend=Nested(blend,"m_SrcBlend"),dstBlend=Nested(blend,"m_DstBlend"),
                outlinePixels=Float(shaderInfo,"_OutlinePixels",-1f),outlineWidthWeight=Float(shaderInfo,"_OutlineWidthWeight",-1f),outlineMasterWeight=Float(shaderInfo,"_OutlineMasterWeight",-1f)
            });
        }

        private static void Finish(Report report)
        {
            report.m5ForwardCount=report.events.Count(x=>x.pass=="M5FaceSDF"&&x.shader=="Sandrone/M5/FaceSDF");
            report.m6ForwardCount=report.events.Count(x=>x.pass=="M6HairEye"&&x.shader=="Sandrone/M6/HairEye");
            report.m4ForwardCount=report.events.Count(x=>x.pass=="M4MaterialResponse"&&x.shader=="Sandrone/M4/MaterialResponse");
            report.outlineCount=report.events.Count(x=>x.pass=="M7Outline"&&x.shader=="Sandrone/M7/Outline");
            report.shadowCasterCount=report.events.Count(x=>x.pass=="ShadowCaster");report.receiverCount=report.events.Count(x=>x.pass=="M3ShadowReceiver");
            if(report.m5ForwardCount!=2)report.failures.Add($"Expected 2 M5 Forward, got {report.m5ForwardCount}.");
            if(report.m6ForwardCount!=11)report.failures.Add($"Expected 11 M6 Forward, got {report.m6ForwardCount}.");
            if(report.m4ForwardCount!=18)report.failures.Add($"Expected 18 M4 Forward, got {report.m4ForwardCount}.");
            if(report.outlineCount!=SandroneM7Bootstrap.EligibleSlots.Length)report.failures.Add($"Expected {SandroneM7Bootstrap.EligibleSlots.Length} outline events, got {report.outlineCount}.");
            if(report.shadowCasterCount<40)report.failures.Add($"ShadowCaster evidence insufficient: {report.shadowCasterCount}.");if(report.receiverCount!=1)report.failures.Add($"Expected one receiver, got {report.receiverCount}.");
            foreach(var slot in SandroneM7Bootstrap.EligibleSlots)
            {
                var e=report.events.FirstOrDefault(x=>x.pass=="M7Outline"&&x.mappedSlot==slot);
                if(e==null)report.failures.Add($"Outline slot {slot} effective event missing.");
                else
                {
                    if(e.cull!="Front")report.failures.Add($"Outline slot {slot} Cull={e.cull}, expected Front.");
                    if(e.zWrite!=0)report.failures.Add($"Outline slot {slot} ZWrite={e.zWrite}, expected 0.");
                    if(!(e.outlinePixels>.5f&&e.outlinePixels<1.6f))report.failures.Add($"Outline slot {slot} width constant invalid: {e.outlinePixels}.");
                }
            }
            ValidateCaptures(report); Save(report);
        }

        private static void ValidateCaptures(Report report)
        {
            var baseline=LoadPixels("GameView_M6_Baseline_768x1680.png",report);var m7=LoadPixels("GameView_M7_768x1680.png",report);
            var off=LoadPixels("GameView_M7_OutlineOff_768x1680.png",report);var mobile=LoadPixels("GameView_Mobile_768x1680.png",report);
            if(baseline==null||m7==null||off==null||mobile==null)return;
            report.m6M7Mae=Mae(baseline,m7);report.outlineToggleMae=Mae(m7,off);report.pcMobileMae=Mae(m7,mobile);report.outsideChangedRatio=OutsideRatio(baseline,m7);report.redSkirtPixels=RedSkirtPixels(m7);
            if(report.m6M7Mae<=.03f)report.failures.Add($"M7 outline response missing: {report.m6M7Mae:F4}.");
            if(report.outlineToggleMae<=.03f)report.failures.Add($"Outline toggle response missing: {report.outlineToggleMae:F4}.");
            if(report.outsideChangedRatio<.25f||report.outsideChangedRatio>.75f)report.failures.Add($"External/inter-part outline distribution is implausible: outside={report.outsideChangedRatio:F3}.");
            if(report.pcMobileMae>8f)report.failures.Add($"PC/Mobile MAE too high: {report.pcMobileMae:F3}.");
            if(report.redSkirtPixels<50000)report.failures.Add($"Red skirt evidence insufficient: {report.redSkirtPixels}.");
        }

        private static Color32[] LoadPixels(string relative,Report report){var path=Path.Combine(Root(),relative);if(!Ready(relative)){report.failures.Add("Missing capture: "+relative);return null;}var t=new Texture2D(2,2);t.LoadImage(File.ReadAllBytes(path));if(t.width!=Width||t.height!=Height)report.failures.Add($"{relative} resolution {t.width}x{t.height}.");var p=t.GetPixels32();UnityEngine.Object.DestroyImmediate(t);return p;}
        private static float Mae(Color32[] a,Color32[] b){double s=0;for(var i=0;i<a.Length;i++)s+=Math.Abs(a[i].r-b[i].r)+Math.Abs(a[i].g-b[i].g)+Math.Abs(a[i].b-b[i].b);return(float)(s/(a.Length*3.0));}
        private static int Diff(Color32 a,Color32 b)=>Mathf.Max(Mathf.Abs(a.r-b.r),Mathf.Max(Mathf.Abs(a.g-b.g),Mathf.Abs(a.b-b.b)));
        private static float OutsideRatio(Color32[] a,Color32[] b){var bg=a[0];var changed=0;var outside=0;for(var i=0;i<a.Length;i++)if(Diff(a[i],b[i])>3){changed++;if(Diff(a[i],bg)<=12)outside++;}return changed==0?0f:(float)outside/changed;}
        private static int RedSkirtPixels(Color32[] p){var c=0;for(var y=160;y<1260;y++)for(var x=48;x<Width-48;x++){var q=p[y*Width+x];if(q.r>48&&q.r>q.g*1.35f&&q.r>q.b*1.2f)c++;}return c;}

        private const BindingFlags AllInstance=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        private static T Field<T>(Type t,object o,string n){var v=t.GetField(n,AllInstance)?.GetValue(o);return v is T r?r:default;}
        private static object Obj(Type t,object o,string n)=>t.GetField(n,AllInstance)?.GetValue(o);
        private static string Nested(object o,string n)=>o?.GetType().GetField(n,AllInstance)?.GetValue(o)?.ToString()??"";
        private static T Nested<T>(object o,string n){var v=o?.GetType().GetField(n,AllInstance)?.GetValue(o);return v is T r?r:default;}
        private static float Float(object info,string property,float fallback){if(info?.GetType().GetField("m_Floats",AllInstance)?.GetValue(info)is not System.Collections.IEnumerable values)return fallback;foreach(var item in values)if(Nested(item,"m_Name")==property)return Nested<float>(item,"m_Value");return fallback;}
        private static Type Utility()=>typeof(EditorWindow).Assembly.GetType("UnityEditorInternal.FrameDebuggerInternal.FrameDebuggerUtility");
        private static int FrameCount()=>(int)(Utility().GetProperty("count",BindingFlags.Static|BindingFlags.Public)?.GetValue(null)??0);
        private static void SetEventLimit(int value)=>Utility().GetProperty("limit",BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic)?.SetValue(null,value);
        private static void SetFrameDebugger(bool enabled){var type=typeof(EditorWindow).Assembly.GetType("UnityEditor.FrameDebuggerWindow");var window=EditorWindow.GetWindow(type,false,"Frame Debugger",true);if(enabled){window.Show();type.GetMethod("OpenPlayModeView",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)?.Invoke(window,null);type.GetMethod("EnableFrameDebugger",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)?.Invoke(window,null);}else type.GetMethod("DisableFrameDebugger",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)?.Invoke(window,null);Utility().GetMethod("SetEnabled",BindingFlags.Static|BindingFlags.Public)?.Invoke(null,new object[]{enabled,0});}
        private static void ConfigureGameView(){var type=typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");var window=EditorWindow.GetWindow(type,false,"Game",true);type.GetMethod("SetCustomResolution",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)?.Invoke(window,new object[]{new Vector2(Width,Height),"M7 Audit"});window.Show();window.Focus();window.Repaint();}
        private static void Shot(string relative){var path=Path.Combine(Root(),relative);if(File.Exists(path))File.Delete(path);ScreenCapture.CaptureScreenshot(path);}
        private static bool Ready(string relative)=>File.Exists(Path.Combine(Root(),relative))&&new FileInfo(Path.Combine(Root(),relative)).Length>1024;
        private static void Next(int stage){SessionState.SetInt(StageKey,stage);ticks=0;}
        private static Report Load()=>JsonUtility.FromJson<Report>(File.ReadAllText(Path.Combine(Root(),"M7GameViewAudit.json")));
        private static void Save(Report report)=>File.WriteAllText(Path.Combine(Root(),"M7GameViewAudit.json"),JsonUtility.ToJson(report,true));
        private static string Root()=>Path.GetFullPath(Path.Combine(Application.dataPath,"../TestArtifacts/M7GameViewAudit"));
    }
}
