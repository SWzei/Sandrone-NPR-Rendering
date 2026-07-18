using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace SandroneToon.Editor
{
    public static class SandroneMpbLifecycleAudit
    {
        [Serializable] public sealed class Check { public string name,details;public bool passed; }
        [Serializable] public sealed class Report
        {
            public string generatedUtc,evidenceSessionId,unityVersion,graphicsApi;
            public int checkCount,failureCount;
            public bool sharedMpbOwnershipRequiresCoordinator=true;
            public List<Check> checks=new();public List<string> failures=new();public List<string> architectureDeferred=new();
        }

        [MenuItem("Sandrone/Audit/MPB Lifecycle And Multi Instance")]
        public static void Run()
        {
            var session=SandroneEvidenceSession.EnsureActive("M0-M9 Windows PC verification");var report=new Report{generatedUtc=DateTime.UtcNow.ToString("O"),evidenceSessionId=session.sessionId,unityVersion=Application.unityVersion,graphicsApi=SystemInfo.graphicsDeviceType.ToString()};
            void Add(string name,bool passed,string details){report.checks.Add(new Check{name=name,passed=passed,details=details});if(!passed)report.failures.Add(name+": "+details);}
            var scene=EditorSceneManager.OpenScene(SandroneM9Bootstrap.ScenePath,OpenSceneMode.Single);Add("M9Scene",scene.IsValid(),scene.path);
            var m4=UnityEngine.Object.FindFirstObjectByType<SandroneM4Controller>();var m5=UnityEngine.Object.FindFirstObjectByType<SandroneM5Controller>();var m6=UnityEngine.Object.FindFirstObjectByType<SandroneM6Controller>();
            var m7=UnityEngine.Object.FindFirstObjectByType<SandroneM7OutlineController>();var m8=UnityEngine.Object.FindFirstObjectByType<SandroneM8VfxBloomController>();var m9=UnityEngine.Object.FindFirstObjectByType<SandroneM9FinalController>();
            Add("M9ControllerSet",m5!=null&&m6!=null&&m7!=null&&m8!=null&&m9!=null,$"M4 replaced={m4==null}, M5={m5!=null}, M6={m6!=null}, M7={m7!=null}, M8={m8!=null}, M9={m9!=null}");
            if(m5!=null&&m6!=null&&m7!=null&&m8!=null&&m9!=null)
            {
                VerifyNoOpWrites(m5,()=>m5.Apply(false),()=>m5.Apply(true),Add);VerifyNoOpWrites(m6,()=>m6.Apply(false),()=>m6.Apply(true),Add);
                VerifyNoOpWrites(m7,()=>m7.Apply(false),()=>m7.Apply(true),Add);VerifyNoOpWrites(m8,()=>m8.Apply(false),()=>m8.Apply(true),Add);
                VerifyDynamicRefresh(null,m5,m6,m8,Add);VerifyMaterialIdentity(m5.TargetRenderer,new Action[]{()=>m5.Apply(true),()=>m6.Apply(true),()=>m8.Apply(true)},Add);
                VerifyM7Lifecycle(m7,Add);VerifyM8Lifecycle(m8,Add);VerifyM9Lifecycle(m9,Add);
            }
            VerifyMpbInstanceIsolation(Add);VerifyM9InstanceIsolation(Add);
            EditorSceneManager.OpenScene(SandroneM4Bootstrap.ScenePath,OpenSceneMode.Single);m4=UnityEngine.Object.FindFirstObjectByType<SandroneM4Controller>();Add("M4SceneController",m4!=null,m4!=null?m4.name:"missing");
            if(m4!=null){VerifyNoOpWrites(m4,()=>m4.Apply(false),()=>m4.Apply(true),Add);var rotation=m4.Head.localRotation;var before=Counter(m4);try{m4.Head.localRotation=rotation*Quaternion.Euler(0,1,0);m4.Apply(false);Add("M4HeadTransformRefresh",Counter(m4)>before,$"writes={Counter(m4)-before}");}finally{m4.Head.localRotation=rotation;m4.Apply(true);}}
            EditorSceneManager.OpenScene(SandroneM8Bootstrap.ScenePath,OpenSceneMode.Single);EditorSceneManager.OpenScene(SandroneM9Bootstrap.ScenePath,OpenSceneMode.Single);
            Add("SceneSwitchState",UnityEngine.Object.FindObjectsByType<SandroneM9FinalController>(FindObjectsInactive.Include,FindObjectsSortMode.None).Length==1,"exactly one M9 controller after M8 -> M9 scene switch");
            report.architectureDeferred.Add("M0-M6 share material-index property blocks and overlapping _Head* / _CastShadow* fields. Removing one component's keys cannot be expressed by MaterialPropertyBlock without reconstructing all owners; destructive per-component clearing is intentionally not implemented.");
            report.checkCount=report.checks.Count;report.failureCount=report.failures.Count;var root=Path.GetFullPath(Path.Combine(Application.dataPath,"../TestArtifacts/Audit/Lifecycle"));Directory.CreateDirectory(root);File.WriteAllText(Path.Combine(root,"MpbLifecycleAudit.json"),JsonUtility.ToJson(report,true));
            if(report.failureCount>0)throw new BuildFailedException("MPB/lifecycle audit failed:\n"+string.Join("\n",report.failures));Debug.Log($"[Sandrone Lifecycle] Passed {report.checkCount}/{report.checkCount}; shared M0-M6 cleanup ownership remains architecture-deferred.");
        }

        private static void VerifyNoOpWrites(object controller,Action apply,Action force,Action<string,bool,string> add)
        {
            force();var before=Counter(controller);apply();var after=Counter(controller);add(controller.GetType().Name+"NoOpWriteSuppressed",after==before,$"before={before}, after={after}");
        }

        private static void VerifyDynamicRefresh(SandroneM4Controller m4,SandroneM5Controller m5,SandroneM6Controller m6,SandroneM8VfxBloomController m8,Action<string,bool,string> add)
        {
            var head=m5.Head;var rotation=head.localRotation;var before4=m4!=null?Counter(m4):0;var before5=Counter(m5);var before6=Counter(m6);
            try{head.localRotation=rotation*Quaternion.Euler(0,1,0);m4?.Apply(false);m5.Apply(false);m6.Apply(false);var refreshed=(m4==null||Counter(m4)>before4)&&Counter(m5)>before5&&Counter(m6)>before6;add("HeadTransformRefresh",refreshed,$"writes M4/M5/M6={(m4!=null?Counter(m4)-before4:0)}/{Counter(m5)-before5}/{Counter(m6)-before6}");}
            finally{head.localRotation=rotation;m4?.Apply(true);m5.Apply(true);m6.Apply(true);}
            var old=m8.EyeEmissionEnabled;var before8=Counter(m8);m8.EyeEmissionEnabled=!old;var changed=Counter(m8)>before8;m8.EyeEmissionEnabled=old;add("M8ParameterRefresh",changed,$"writes={Counter(m8)-before8}");
        }

        private static void VerifyMaterialIdentity(Renderer renderer,IEnumerable<Action> actions,Action<string,bool,string> add)
        {
            var before=renderer.sharedMaterials.Select(x=>x!=null?x.GetEntityId().GetHashCode():0).ToArray();foreach(var action in actions)action();var after=renderer.sharedMaterials.Select(x=>x!=null?x.GetEntityId().GetHashCode():0).ToArray();
            add("NoMaterialInstantiation",before.SequenceEqual(after),$"slots={before.Length}, unchanged={before.SequenceEqual(after)}");
        }

        private static void VerifyM7Lifecycle(SandroneM7OutlineController controller,Action<string,bool,string> add)
        {
            controller.enabled=false;var renderer=controller.OutlineRenderer;renderer.enabled=false;renderer.shadowCastingMode=ShadowCastingMode.TwoSided;renderer.receiveShadows=true;controller.enabled=true;
            var applied=renderer.enabled&&renderer.shadowCastingMode==ShadowCastingMode.Off&&!renderer.receiveShadows;controller.enabled=false;
            var restored=!renderer.enabled&&renderer.shadowCastingMode==ShadowCastingMode.TwoSided&&renderer.receiveShadows;controller.enabled=true;add("M7LifecycleRestore",applied&&restored,$"applied={applied}, restored={restored}");
        }

        private static void VerifyM8Lifecycle(SandroneM8VfxBloomController controller,Action<string,bool,string> add)
        {
            controller.enabled=false;controller.CrystalRoot.SetActive(false);controller.BloomVolume.weight=.37f;controller.enabled=true;var applied=controller.CrystalRoot.activeSelf&&Mathf.Approximately(controller.BloomVolume.weight,1f);controller.enabled=false;
            var restored=!controller.CrystalRoot.activeSelf&&Mathf.Approximately(controller.BloomVolume.weight,.37f);controller.enabled=true;add("M8LifecycleRestore",applied&&restored,$"applied={applied}, restored={restored}");
        }

        private static void VerifyM9Lifecycle(SandroneM9FinalController controller,Action<string,bool,string> add)
        {
            controller.enabled=false;var camera=controller.TargetCamera;var data=camera.GetUniversalAdditionalCameraData();camera.allowHDR=false;data.renderPostProcessing=false;data.antialiasing=AntialiasingMode.FastApproximateAntialiasing;data.antialiasingQuality=AntialiasingQuality.Low;controller.GradingVolume.weight=.23f;
            controller.enabled=true;var applied=camera.allowHDR&&data.renderPostProcessing&&data.antialiasing==AntialiasingMode.SubpixelMorphologicalAntiAliasing&&Mathf.Approximately(controller.GradingVolume.weight,1f);controller.enabled=false;
            var restored=!camera.allowHDR&&!data.renderPostProcessing&&data.antialiasing==AntialiasingMode.FastApproximateAntialiasing&&data.antialiasingQuality==AntialiasingQuality.Low&&Mathf.Approximately(controller.GradingVolume.weight,.23f);controller.enabled=true;add("M9LifecycleRestore",applied&&restored,$"applied={applied}, restored={restored}");
        }

        private static void VerifyMpbInstanceIsolation(Action<string,bool,string> add)
        {
            var material=AssetDatabase.LoadAssetAtPath<Material>(SandroneM8Bootstrap.EyeMaterialPath);var a=GameObject.CreatePrimitive(PrimitiveType.Quad);var b=GameObject.CreatePrimitive(PrimitiveType.Quad);
            try
            {
                var ra=a.GetComponent<MeshRenderer>();var rb=b.GetComponent<MeshRenderer>();ra.sharedMaterial=material;rb.sharedMaterial=material;var ca=a.AddComponent<SandroneM8VfxBloomController>();var cb=b.AddComponent<SandroneM8VfxBloomController>();ca.Configure(ra,0,null,-1,null,null,null);cb.Configure(rb,0,null,-1,null,null,null);
                ca.EyeEmissionEnabled=false;cb.EyeEmissionEnabled=true;var block=new MaterialPropertyBlock();ra.GetPropertyBlock(block,0);var av=block.GetFloat(Shader.PropertyToID("_M8EmissionWeight"));block.Clear();rb.GetPropertyBlock(block,0);var bv=block.GetFloat(Shader.PropertyToID("_M8EmissionWeight"));
                add("MpbMultiInstanceIsolation",Mathf.Approximately(av,0)&&Mathf.Approximately(bv,1)&&ra.sharedMaterial==material&&rb.sharedMaterial==material,$"A={av}, B={bv}, shared={ra.sharedMaterial==rb.sharedMaterial}");
            }
            finally{UnityEngine.Object.DestroyImmediate(a);UnityEngine.Object.DestroyImmediate(b);}
        }

        private static void VerifyM9InstanceIsolation(Action<string,bool,string> add)
        {
            var a=new GameObject("M9AuditA");var b=new GameObject("M9AuditB");
            try
            {
                var ca=a.AddComponent<Camera>();var va=a.AddComponent<Volume>();va.weight=.31f;ca.allowHDR=false;var da=ca.GetUniversalAdditionalCameraData();da.renderPostProcessing=false;da.antialiasing=AntialiasingMode.None;
                var cb=b.AddComponent<Camera>();var vb=b.AddComponent<Volume>();vb.weight=.62f;cb.allowHDR=false;var db=cb.GetUniversalAdditionalCameraData();db.renderPostProcessing=false;db.antialiasing=AntialiasingMode.FastApproximateAntialiasing;
                var ma=a.AddComponent<SandroneM9FinalController>();var mb=b.AddComponent<SandroneM9FinalController>();ma.Configure(ca,va,null);mb.Configure(cb,vb,null);ma.enabled=false;
                var isolated=Mathf.Approximately(va.weight,.31f)&&!ca.allowHDR&&!da.renderPostProcessing&&Mathf.Approximately(vb.weight,1f)&&cb.allowHDR&&db.renderPostProcessing;add("M9MultiInstanceIsolation",isolated,$"A={va.weight}/{ca.allowHDR}, B={vb.weight}/{cb.allowHDR}");
            }
            finally{UnityEngine.Object.DestroyImmediate(a);UnityEngine.Object.DestroyImmediate(b);}
        }

        private static int Counter(object target)
        {
            var field=target.GetType().GetField("auditPropertyBlockWriteCount",BindingFlags.Instance|BindingFlags.NonPublic);return field!=null?(int)field.GetValue(target):-1;
        }
    }
}
