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
    public static class SandroneM9GameViewAudit
    {
        private const string StageKey="Sandrone.M9GameView.Stage";private const int Width=768,Height=1280;private static int ticks,cursor;private static List<int> indices;private static Report pending;
        [Serializable] public sealed class EventRecord
        {
            public int index,zWrite,stencilRef;public string eventName,shader,pass,renderTarget,cull,zTest,srcBlend,dstBlend;
        }
        [Serializable] public sealed class Report
        {
            public string generatedUtc,evidenceSessionId,unityVersion,graphicsApi,graphicsDevice,scene;public int width=Width,height=Height,frameEventCount,m5ForwardCount,m6ForwardCount,m4ForwardCount,m8EyeCount,m8VfxCount,m0SwordCount;
            public int bloomEventCount,finalPostEventCount,smaaEventCount,outlineCount,shadowCasterCount,receiverCount,redSkirtPixels,magentaPixels;
            public float gradingMae,aaMae,pcMobileMae;public bool enteredPlayMode,exitedPlayMode,sceneReopenedAfterPlay,controllerStateRestored;public List<EventRecord> events=new();public List<string> captures=new();public List<string> failures=new();
        }

        static SandroneM9GameViewAudit(){EditorApplication.update-=Update;EditorApplication.update+=Update;}
        public static void RunFromCommandLine()
        {
            if(Application.isBatchMode)throw new InvalidOperationException("M9 Game View/Frame Debugger audit requires a visible Editor.");SandroneEvidenceSession.EnsureActive("M0-M9 Windows PC verification");Directory.CreateDirectory(Root());
            foreach(var path in Directory.GetFiles(Root(),"*.png",SearchOption.AllDirectories))File.Delete(path);foreach(var name in new[]{"M9GameViewAudit.json","FrameEventIndexMap.tsv","StageStatus.txt"}){var path=Path.Combine(Root(),name);if(File.Exists(path))File.Delete(path);}
            EditorSceneManager.OpenScene(SandroneM9Bootstrap.ScenePath,OpenSceneMode.Single);ConfigureGameView();SessionState.SetInt(StageKey,1);ticks=0;EditorApplication.EnterPlaymode();
        }

        private static void Update()
        {
            var stage=SessionState.GetInt(StageKey,0);if(stage==0)return;ticks++;ConfigureGameView();if(ticks==1)File.WriteAllText(Path.Combine(Root(),"StageStatus.txt"),$"stage={stage}; playing={EditorApplication.isPlaying}; utc={DateTime.UtcNow:O}");
            if(stage<90&&!EditorApplication.isPlaying)return;if(ticks<(stage==8?2:45))return;
            switch(stage)
            {
                case 1:Configure(false,false,false);Shot("GameView_M8Baseline_PostOff_AAOff_768x1280.png");Next(2);break;
                case 2:if(!Ready("GameView_M8Baseline_PostOff_AAOff_768x1280.png"))return;Configure(true,false,false);Shot("GameView_M9_Neutral_AAOff_768x1280.png");Next(3);break;
                case 3:if(!Ready("GameView_M9_Neutral_AAOff_768x1280.png"))return;Configure(true,true,false);Shot("GameView_M9_Final_SMAA_768x1280.png");Next(4);break;
                case 4:if(!Ready("GameView_M9_Final_SMAA_768x1280.png"))return;Configure(true,true,true);Next(5);break;
                case 5:Configure(true,true,true);Shot("GameView_Mobile_FXAA_768x1280.png");Next(6);break;
                case 6:if(!Ready("GameView_Mobile_FXAA_768x1280.png"))return;Configure(true,true,false);SetFrameDebugger(true);Next(7);break;
                case 7:
                    if(FrameCount()<=0)return;pending=CreateReport();indices=RelevantIndices();cursor=0;if(indices.Count==0){pending.failures.Add("Frame Debugger exposed no relevant events.");Finish(pending);SetFrameDebugger(false);Next(90);EditorApplication.ExitPlaymode();break;}SetLimit(indices[0]+1);Next(8);break;
                case 8:
                    Append(pending,indices[cursor]);cursor++;if(cursor<indices.Count){SetLimit(indices[cursor]+1);Next(8);}else{Finish(pending);SetFrameDebugger(false);Next(90);EditorApplication.ExitPlaymode();}break;
                case 90:
                    if(EditorApplication.isPlayingOrWillChangePlaymode)return;var scene=EditorSceneManager.OpenScene(SandroneM9Bootstrap.ScenePath,OpenSceneMode.Single);var controller=UnityEngine.Object.FindFirstObjectByType<SandroneM9FinalController>();var report=Load();
                    report.exitedPlayMode=true;report.sceneReopenedAfterPlay=scene.IsValid();report.controllerStateRestored=controller!=null&&controller.GradingEnabled&&controller.AntiAliasingEnabled&&!controller.MobileQuality;
                    if(!report.controllerStateRestored)report.failures.Add("M9 controller state did not restore after Play/scene reopen.");Save(report);SessionState.SetInt(StageKey,0);EditorApplication.Exit(report.failures.Count==0?0:1);break;
            }
        }

        private static void Configure(bool grading,bool aa,bool mobile)
        {
            var m9=UnityEngine.Object.FindFirstObjectByType<SandroneM9FinalController>();var m8=UnityEngine.Object.FindFirstObjectByType<SandroneM8VfxBloomController>();var m7=UnityEngine.Object.FindFirstObjectByType<SandroneM7OutlineController>();
            var m6=UnityEngine.Object.FindFirstObjectByType<SandroneM6Controller>();var m5=UnityEngine.Object.FindFirstObjectByType<SandroneM5Controller>();var m0=UnityEngine.Object.FindFirstObjectByType<SandroneM0Controller>();var camera=UnityEngine.Object.FindFirstObjectByType<Camera>();
            var pipeline=AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>(mobile?"Assets/Settings/Mobile_RPAsset.asset":"Assets/Settings/PC_RPAsset.asset");GraphicsSettings.defaultRenderPipeline=pipeline;QualitySettings.renderPipeline=pipeline;
            m8.CharacterRenderer.transform.root.SetPositionAndRotation(Vector3.zero,Quaternion.identity);m5.DebugMode=SandroneM5DebugMode.FinalToon;m5.FaceSdfEnabled=true;m5.Apply(true);m6.DebugMode=SandroneM6DebugMode.FinalToon;m6.HairSpecularEnabled=true;m6.EyeLayersEnabled=true;m6.SetLightDirectionToSource(SandroneM3Bootstrap.DefaultDirectionToLight);m6.Apply(true);
            m0.eyeALWeight=0;m0.blushWeight=0;m0.Apply();m7.OutlineEnabled=true;m7.MasterWidth=1;m7.DebugMode=SandroneM7DebugMode.FinalColor;m7.Apply(true);m8.EyeEmissionEnabled=true;m8.CrystalEmissionEnabled=true;m8.CrystalVisible=true;m8.BloomEnabled=true;m8.DebugMode=SandroneM8DebugMode.FinalColor;m8.Apply(true);
            m9.GradingEnabled=grading;m9.AntiAliasingEnabled=aa;m9.MobileQuality=mobile;m9.Apply();camera.orthographic=true;camera.transform.position=new Vector3(0,.82f,4);camera.transform.rotation=Quaternion.LookRotation(Vector3.back,Vector3.up);camera.orthographicSize=.92f;camera.backgroundColor=new Color(.153f,.149f,.149f,1);camera.allowHDR=true;camera.GetUniversalAdditionalCameraData().renderPostProcessing=true;ConfigureGameView();
        }

        private static Report CreateReport()=>new(){generatedUtc=DateTime.UtcNow.ToString("O"),evidenceSessionId=SandroneEvidenceSession.CurrentSessionId,unityVersion=Application.unityVersion,graphicsApi=SystemInfo.graphicsDeviceType.ToString(),graphicsDevice=SystemInfo.graphicsDeviceName,scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene().path,frameEventCount=FrameCount(),enteredPlayMode=EditorApplication.isPlaying,captures=Directory.GetFiles(Root(),"*.png").Select(Path.GetFileName).OrderBy(x=>x).ToList()};
        private static List<int> RelevantIndices()
        {
            var utility=Utility();var getName=utility.GetMethod("GetFrameEventInfoName",BindingFlags.Static|BindingFlags.Public);var getObject=utility.GetMethod("GetFrameEventObject",BindingFlags.Static|BindingFlags.Public);var result=new List<int>();var rows=new List<string>();
            for(var i=0;i<FrameCount();i++){var name=getName.Invoke(null,new object[]{i})as string??"";var value=getObject.Invoke(null,new object[]{i})as UnityEngine.Object;rows.Add($"{i}\t{name}\t{value?.GetType().Name??"null"}\t{value?.name??""}");if(name.IndexOf("Draw",StringComparison.OrdinalIgnoreCase)>=0||name.IndexOf("Bloom",StringComparison.OrdinalIgnoreCase)>=0||name.IndexOf("Post",StringComparison.OrdinalIgnoreCase)>=0||name.IndexOf("SMAA",StringComparison.OrdinalIgnoreCase)>=0||value is Material||value is Renderer||value is Mesh)result.Add(i);}
            File.WriteAllLines(Path.Combine(Root(),"FrameEventIndexMap.tsv"),rows);return result.Count>0?result:Enumerable.Range(0,FrameCount()).ToList();
        }

        private static void Append(Report report,int index)
        {
            var utility=Utility();var dataType=typeof(EditorWindow).Assembly.GetType("UnityEditorInternal.FrameDebuggerInternal.FrameDebuggerEventData");var data=Activator.CreateInstance(dataType);if(!(bool)utility.GetMethod("GetFrameEventData",BindingFlags.Static|BindingFlags.Public).Invoke(null,new[]{(object)index,data}))return;
            var blend=Obj(dataType,data,"m_BlendState");var raster=Obj(dataType,data,"m_RasterState");var depth=Obj(dataType,data,"m_DepthState");report.events.Add(new EventRecord{index=index,eventName=utility.GetMethod("GetFrameEventInfoName",BindingFlags.Static|BindingFlags.Public).Invoke(null,new object[]{index})as string,
                shader=Field<string>(dataType,data,"m_OriginalShaderName"),pass=Field<string>(dataType,data,"m_PassName"),renderTarget=Field<string>(dataType,data,"m_RenderTargetName"),stencilRef=Field<int>(dataType,data,"m_StencilRef"),cull=Nested(raster,"m_CullMode"),zWrite=Nested<int>(depth,"m_DepthWrite"),zTest=Nested(depth,"m_DepthFunc"),srcBlend=Nested(blend,"m_SrcBlend"),dstBlend=Nested(blend,"m_DstBlend")});
        }

        private static void Finish(Report report)
        {
            report.m5ForwardCount=report.events.Count(x=>x.pass=="M5FaceSDF"&&x.shader=="Sandrone/M5/FaceSDF");report.m6ForwardCount=report.events.Count(x=>x.pass=="M6HairEye"&&x.shader=="Sandrone/M6/HairEye");report.m4ForwardCount=report.events.Count(x=>x.pass=="M4MaterialResponse"&&x.shader=="Sandrone/M4/MaterialResponse");
            report.m8EyeCount=report.events.Count(x=>x.pass=="M8HairEyeEmission");report.m8VfxCount=report.events.Count(x=>x.pass=="M8VFXEmission");report.m0SwordCount=report.events.Count(x=>x.pass=="M0Unlit"&&x.shader=="Sandrone/M0/UnlitBaseMap");
            report.bloomEventCount=report.events.Count(x=>Contains(x,"Bloom"));report.smaaEventCount=report.events.Count(x=>Contains(x,"SMAA")||Contains(x,"SubpixelMorphological"));report.finalPostEventCount=report.events.Count(x=>Contains(x,"UberPost")||Contains(x,"FinalPost")||Contains(x,"Post-process"));
            report.outlineCount=report.events.Count(x=>x.pass=="M7Outline");report.shadowCasterCount=report.events.Count(x=>x.pass=="ShadowCaster");report.receiverCount=report.events.Count(x=>x.pass=="M3ShadowReceiver");
            if(report.m5ForwardCount!=2)report.failures.Add($"Expected 2 M5, got {report.m5ForwardCount}.");if(report.m6ForwardCount!=10)report.failures.Add($"Expected 10 M6, got {report.m6ForwardCount}.");if(report.m4ForwardCount!=18)report.failures.Add($"Expected 18 M4, got {report.m4ForwardCount}.");
            if(report.m8EyeCount!=1||report.m8VfxCount!=1||report.m0SwordCount!=1)report.failures.Add($"M8/sword counts invalid: {report.m8EyeCount}/{report.m8VfxCount}/{report.m0SwordCount}.");if(report.bloomEventCount<1)report.failures.Add("Bloom event missing.");if(report.smaaEventCount<1)report.failures.Add("SMAA event missing.");if(report.finalPostEventCount<1)report.failures.Add("Final post event missing.");
            if(report.outlineCount!=14||report.shadowCasterCount<40||report.receiverCount!=1)report.failures.Add($"Outline/shadow/receiver invalid: {report.outlineCount}/{report.shadowCasterCount}/{report.receiverCount}.");ValidateCaptures(report);Save(report);
        }

        private static bool Contains(EventRecord e,string value)=>(e.eventName??"").IndexOf(value,StringComparison.OrdinalIgnoreCase)>=0||(e.shader??"").IndexOf(value,StringComparison.OrdinalIgnoreCase)>=0||(e.pass??"").IndexOf(value,StringComparison.OrdinalIgnoreCase)>=0;
        private static void ValidateCaptures(Report report)
        {
            var baseline=Pixels("GameView_M8Baseline_PostOff_AAOff_768x1280.png",report);var neutral=Pixels("GameView_M9_Neutral_AAOff_768x1280.png",report);var final=Pixels("GameView_M9_Final_SMAA_768x1280.png",report);var mobile=Pixels("GameView_Mobile_FXAA_768x1280.png",report);if(baseline==null||neutral==null||final==null||mobile==null)return;
            report.gradingMae=Mae(baseline,neutral);report.aaMae=Mae(neutral,final);report.pcMobileMae=Mae(final,mobile);report.redSkirtPixels=Red(final);report.magentaPixels=Magenta(final);
            if(report.gradingMae<=.05f)report.failures.Add($"Game View grading response missing: {report.gradingMae:F4}.");if(report.aaMae<=.001f)report.failures.Add($"Game View AA response missing: {report.aaMae:F4}.");if(report.pcMobileMae>10)report.failures.Add($"PC/Mobile MAE too high: {report.pcMobileMae:F3}.");if(report.redSkirtPixels<50000)report.failures.Add($"Red skirt insufficient: {report.redSkirtPixels}.");if(report.magentaPixels>=10)report.failures.Add($"Pink material pixels: {report.magentaPixels}.");
        }

        private static Color32[] Pixels(string name,Report report){var path=Path.Combine(Root(),name);if(!Ready(name)){report.failures.Add("Missing capture: "+name);return null;}var image=new Texture2D(2,2);image.LoadImage(File.ReadAllBytes(path));if(image.width!=Width||image.height!=Height)report.failures.Add($"{name} resolution {image.width}x{image.height}.");var pixels=image.GetPixels32();UnityEngine.Object.DestroyImmediate(image);return pixels;}
        private static float Mae(Color32[] a,Color32[] b){double sum=0;for(var i=0;i<a.Length;i++)sum+=Math.Abs(a[i].r-b[i].r)+Math.Abs(a[i].g-b[i].g)+Math.Abs(a[i].b-b[i].b);return(float)(sum/(a.Length*3.0));}
        private static int Red(Color32[] p)=>p.Count(x=>x.r>48&&x.r>x.g*1.35f&&x.r>x.b*1.2f);private static int Magenta(Color32[] p)=>p.Count(x=>x.r>220&&x.b>220&&x.g<64);
        private const BindingFlags All=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;private static T Field<T>(Type t,object o,string n){var value=t.GetField(n,All)?.GetValue(o);return value is T result?result:default;}private static object Obj(Type t,object o,string n)=>t.GetField(n,All)?.GetValue(o);private static string Nested(object o,string n)=>o?.GetType().GetField(n,All)?.GetValue(o)?.ToString()??"";private static T Nested<T>(object o,string n){var value=o?.GetType().GetField(n,All)?.GetValue(o);return value is T result?result:default;}
        private static Type Utility()=>typeof(EditorWindow).Assembly.GetType("UnityEditorInternal.FrameDebuggerInternal.FrameDebuggerUtility");private static int FrameCount()=>(int)(Utility().GetProperty("count",BindingFlags.Static|BindingFlags.Public)?.GetValue(null)??0);private static void SetLimit(int value)=>Utility().GetProperty("limit",BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic)?.SetValue(null,value);
        private static void SetFrameDebugger(bool enabled){var type=typeof(EditorWindow).Assembly.GetType("UnityEditor.FrameDebuggerWindow");var window=EditorWindow.GetWindow(type,false,"Frame Debugger",true);if(enabled){window.Show();type.GetMethod("OpenPlayModeView",All)?.Invoke(window,null);type.GetMethod("EnableFrameDebugger",All)?.Invoke(window,null);}else type.GetMethod("DisableFrameDebugger",All)?.Invoke(window,null);Utility().GetMethod("SetEnabled",BindingFlags.Static|BindingFlags.Public)?.Invoke(null,new object[]{enabled,0});}
        private static void ConfigureGameView(){var type=typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");var window=EditorWindow.GetWindow(type,false,"Game",true);type.GetMethod("SetCustomResolution",All)?.Invoke(window,new object[]{new Vector2(Width,Height),"M9 Audit"});window.Show();window.Focus();window.Repaint();}
        private static void Shot(string name){var path=Path.Combine(Root(),name);if(File.Exists(path))File.Delete(path);ScreenCapture.CaptureScreenshot(path);}private static bool Ready(string name)=>File.Exists(Path.Combine(Root(),name))&&new FileInfo(Path.Combine(Root(),name)).Length>1024;private static void Next(int stage){SessionState.SetInt(StageKey,stage);ticks=0;}
        private static Report Load()=>JsonUtility.FromJson<Report>(File.ReadAllText(Path.Combine(Root(),"M9GameViewAudit.json")));private static void Save(Report report)=>File.WriteAllText(Path.Combine(Root(),"M9GameViewAudit.json"),JsonUtility.ToJson(report,true));private static string Root()=>Path.GetFullPath(Path.Combine(Application.dataPath,"../TestArtifacts/M9GameViewAudit",SystemInfo.graphicsDeviceType==GraphicsDeviceType.Direct3D12?"D3D12":"D3D11"));
    }
}
