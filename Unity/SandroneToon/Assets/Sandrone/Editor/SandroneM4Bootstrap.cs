using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace SandroneToon.Editor
{
    public static class SandroneM4Bootstrap
    {
        public const string ShaderPath = "Assets/Sandrone/Shaders/SandroneMaterialResponseM4.shader";
        public const string IsolationShaderPath = "Assets/Sandrone/Shaders/SandroneM4Isolation.shader";
        public const string ProfilePath = "Assets/Sandrone/Configs/SandroneMaterialResponse_M4.asset";
        public const string MaterialDirectory = "Assets/Sandrone/Materials/M4";
        public const string ControlDirectory = "Assets/Sandrone/Textures/Control";
        public const string MatCapDirectory = "Assets/Sandrone/Textures/MatCap";
        public const string BodyControlPath = ControlDirectory + "/Sandrone_Body_Control.png";
        public const string SkirtControlPath = ControlDirectory + "/Sandrone_Skirt_Control.png";
        public const string HairControlPath = ControlDirectory + "/Sandrone_Hair_Control.png";
        public const string NeutralControlPath = ControlDirectory + "/Sandrone_Neutral_Control.png";
        public const string MatCapPath = MatCapDirectory + "/Sandrone_Metal_MatCap.png";
        public const string ScenePath = "Assets/Sandrone/Tests/Scenes/ToonCalibration_M4.unity";
        private static readonly Color Background = new(0.153f, 0.149f, 0.149f, 1f);

        [MenuItem("Sandrone/M4/Build Material Response")]
        public static void Build()
        {
            Debug.Log("[Sandrone M4] Build started; M3 report is a hard gate.");
            SandroneM3Validator.ValidateAndWriteReport();
            EnsureFolder(MaterialDirectory); EnsureFolder(ControlDirectory); EnsureFolder(MatCapDirectory);
            EnsureFolder("Assets/Sandrone/Configs"); EnsureFolder("Assets/Sandrone/Tests/Scenes");
            CreateControlIfMissing(BodyControlPath, "Assets/Sandrone/Textures/SourceBase/T_Body.png");
            CreateControlIfMissing(SkirtControlPath, "Assets/Sandrone/Textures/SourceBase/T_Skirt.png");
            CreateControlIfMissing(HairControlPath, "Assets/Sandrone/Textures/SourceBase/T_Hair.png");
            CreateNeutralIfMissing(); CreateMatCapIfMissing();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            ConfigureTextureImporter(BodyControlPath, false); ConfigureTextureImporter(SkirtControlPath, false);
            ConfigureTextureImporter(HairControlPath, false); ConfigureTextureImporter(NeutralControlPath, false);
            ConfigureTextureImporter(MatCapPath, true);
            AssetDatabase.ImportAsset(ShaderPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            AssetDatabase.ImportAsset(IsolationShaderPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);

            var shader=AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
            var isolationShader=AssetDatabase.LoadAssetAtPath<Shader>(IsolationShaderPath);
            var body=AssetDatabase.LoadAssetAtPath<Texture2D>(BodyControlPath);
            var skirt=AssetDatabase.LoadAssetAtPath<Texture2D>(SkirtControlPath);
            var hair=AssetDatabase.LoadAssetAtPath<Texture2D>(HairControlPath);
            var neutral=AssetDatabase.LoadAssetAtPath<Texture2D>(NeutralControlPath);
            var matCap=AssetDatabase.LoadAssetAtPath<Texture2D>(MatCapPath);
            if (shader==null || !shader.isSupported || isolationShader==null || !isolationShader.isSupported || body==null || skirt==null || hair==null || neutral==null || matCap==null)
                throw new InvalidOperationException("M4 shader or generated data textures are missing/unsupported.");
            var entries=CreateResponseEntries();
            var profile=CreateProfile(body,skirt,hair,neutral,matCap,entries);
            var materials=CreateMaterials(shader,profile);
            CreateScene(materials,profile,isolationShader);
            AssetDatabase.SaveAssets(); AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            SandroneM4Validator.ValidateAndWriteReport();
            Debug.Log("[Sandrone M4] Build and validation completed.");
        }

        public static string MaterialPath(int index,string sourcePath) =>
            $"{MaterialDirectory}/M4_{index:00}_{Path.GetFileNameWithoutExtension(sourcePath)}.mat";

        private static SandroneM4MaterialResponse[] CreateResponseEntries()
        {
            var result=new SandroneM4MaterialResponse[31];
            for(var i=0;i<result.Length;i++) result[i]=Entry(i,SandroneM4ResponseType.Matte,SandroneM4FeatureGroup.None,0,32,0,0);
            result[17]=Entry(17,SandroneM4ResponseType.Skin,SandroneM4FeatureGroup.None,0.20f,24,0,0);
            result[18]=Entry(18,SandroneM4ResponseType.Skin,SandroneM4FeatureGroup.None,0.22f,24,0,0);
            foreach(var i in new[]{14,15,20,21,22,23,25,27}) result[i]=Entry(i,SandroneM4ResponseType.Metal,SandroneM4FeatureGroup.Metal,0.62f,54,0.34f,0);
            result[24]=Entry(24,SandroneM4ResponseType.Metal,SandroneM4FeatureGroup.Metal,0.78f,68,0.46f,1);
            result[28]=Entry(28,SandroneM4ResponseType.Silk,SandroneM4FeatureGroup.StockingOverlay,0.32f,34,0,0);
            result[29]=Entry(29,SandroneM4ResponseType.Matte,SandroneM4FeatureGroup.HairOverlay,0,32,0,0,1.65f);
            return result;
        }

        private static SandroneM4MaterialResponse Entry(int index,SandroneM4ResponseType type,
            SandroneM4FeatureGroup feature,float spec,float power,float matCap,float fallback,float overlayBoost=1f) =>
            new() { materialIndex=index,responseType=type,featureGroup=feature,specularIntensity=spec,
                specularPower=power,matCapIntensity=matCap,metalMaskFallback=fallback,overlayColorBoost=overlayBoost };

        private static SandroneM4MaterialResponseProfile CreateProfile(Texture2D body,Texture2D skirt,
            Texture2D hair,Texture2D neutral,Texture2D matCap,SandroneM4MaterialResponse[] entries)
        {
            var profile=AssetDatabase.LoadAssetAtPath<SandroneM4MaterialResponseProfile>(ProfilePath);
            if(profile==null){ profile=ScriptableObject.CreateInstance<SandroneM4MaterialResponseProfile>(); AssetDatabase.CreateAsset(profile,ProfilePath); }
            profile.EditorSet(body,skirt,hair,neutral,matCap,entries); EditorUtility.SetDirty(profile); return profile;
        }

        private static Material[] CreateMaterials(Shader shader,SandroneM4MaterialResponseProfile profile)
        {
            var map=AssetDatabase.LoadAssetAtPath<SandroneMaterialMap>(SandroneM0Bootstrap.MaterialMapPath);
            if(map==null || map.Entries.Count!=31) throw new InvalidOperationException("M0 material map is missing/incomplete.");
            var output=new Material[31];
            foreach(var sourceEntry in map.Entries.OrderBy(e=>e.sourceIndex))
            {
                var sourcePath=SandroneM3Bootstrap.MaterialPath(sourceEntry.sourceIndex,sourceEntry.materialAssetPath);
                var source=AssetDatabase.LoadAssetAtPath<Material>(sourcePath);
                if(source==null) throw new FileNotFoundException($"M3 material missing: {sourcePath}");
                var path=MaterialPath(sourceEntry.sourceIndex,sourceEntry.materialAssetPath);
                var material=AssetDatabase.LoadAssetAtPath<Material>(path);
                if(material==null){ material=new Material(shader); AssetDatabase.CreateAsset(material,path); }
                material.shader=shader; CopyM3MaterialProperties(source,material); material.name=Path.GetFileNameWithoutExtension(path);
                var baseMap=source.GetTexture("_BaseMap") as Texture2D;
                material.SetTexture("_ControlMap",SelectControl(profile,baseMap)); material.SetTexture("_MatCapMap",profile.MetalMatCap);
                profile.TryGet(sourceEntry.sourceIndex,out var response);
                material.SetFloat("_ResponseType",(float)response.responseType);
                material.SetFloat("_FeatureGroup",(float)response.featureGroup);
                material.SetFloat("_SpecIntensity",response.specularIntensity); material.SetFloat("_SpecPower",response.specularPower);
                material.SetFloat("_MatCapIntensity",response.matCapIntensity); material.SetFloat("_MetalMaskFallback",response.metalMaskFallback);
                material.SetFloat("_AOIntensity",1f);
                material.SetFloat("_OverlayColorBoost",response.overlayColorBoost);
                material.SetFloat("_M4DebugMode",(float)SandroneM4DebugMode.FinalToon);
                material.SetFloat("_M4FeatureWeight",1f);
                // Slot 21 contains the dark back-facing skirt layer.  The source
                // also supplies slot 26 as the red front-facing layer; rendering
                // slot 21 double-sided makes both opaque layers depth-compete.
                if(sourceEntry.sourceIndex==21) material.SetFloat("_Cull",(float)CullMode.Back);
                material.SetFloat("_ShadowCull",(float)CullMode.Back);
                material.SetOverrideTag("RenderType",source.GetTag("RenderType",false,"Opaque")); material.renderQueue=source.renderQueue;
                material.SetShaderPassEnabled("ShadowCaster",source.GetFloat("_ZWrite")>0.5f);
                EditorUtility.SetDirty(material); output[sourceEntry.sourceIndex]=material;
            }
            return output;
        }

        private static void CopyM3MaterialProperties(Material source,Material target)
        {
            target.SetTexture("_BaseMap",source.GetTexture("_BaseMap"));
            target.SetTextureScale("_BaseMap",source.GetTextureScale("_BaseMap"));
            target.SetTextureOffset("_BaseMap",source.GetTextureOffset("_BaseMap"));
            target.SetColor("_BaseColor",source.GetColor("_BaseColor"));
            target.SetTexture("_RampMap",source.GetTexture("_RampMap"));
            string[] floats={"_RampRow","_RampRowCount","_Threshold","_BandSoftness","_BandAA",
                "_CastShadowStrength","_CastShadowLow","_CastShadowHigh","_LayerWeight","_Cutoff",
                "_AlphaClip","_SrcBlend","_DstBlend","_ZWrite","_Cull"};
            foreach(var property in floats) target.SetFloat(property,source.GetFloat(property));
        }

        private static Texture2D SelectControl(SandroneM4MaterialResponseProfile p,Texture2D baseMap)
        {
            if(baseMap==null) return p.NeutralControlMap;
            if(baseMap.name=="T_Body") return p.BodyControlMap;
            if(baseMap.name=="T_Skirt") return p.SkirtControlMap;
            if(baseMap.name=="T_Hair") return p.HairControlMap;
            return p.NeutralControlMap;
        }

        private static void CreateScene(Material[] materials,SandroneM4MaterialResponseProfile responseProfile,Shader isolationShader)
        {
            var model=AssetDatabase.LoadAssetAtPath<GameObject>(SandroneM0Bootstrap.ModelPath);
            var shadowProfile=AssetDatabase.LoadAssetAtPath<SandroneM3ShadowProfile>(SandroneM3Bootstrap.ShadowProfilePath);
            var groundMaterial=AssetDatabase.LoadAssetAtPath<Material>(SandroneM3Bootstrap.ReceiverMaterialPath);
            if(model==null || shadowProfile==null || groundMaterial==null) throw new InvalidOperationException("M0 model/M3 shadow assets missing.");
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var instance=(GameObject)PrefabUtility.InstantiatePrefab(model,scene); instance.name="Sandrone_M4";
            instance.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);
            var renderer=instance.GetComponentsInChildren<SkinnedMeshRenderer>(true).Single(); renderer.sharedMaterials=materials;
            renderer.shadowCastingMode=ShadowCastingMode.On; renderer.receiveShadows=true;
            var m0=instance.AddComponent<SandroneM0Controller>();
            m0.Configure(new SandroneM0Controller.LayerBinding{renderer=renderer,materialIndex=9},
                new SandroneM0Controller.LayerBinding{renderer=renderer,materialIndex=30},Find(instance.transform,"KeyB02_M"));
            var lightObject=new GameObject("M4_MainDirectionalLight"); var light=lightObject.AddComponent<Light>();
            light.type=LightType.Directional; light.color=Color.white; light.intensity=1; light.shadows=LightShadows.Soft;
            light.shadowStrength=0.85f; light.renderMode=LightRenderMode.ForcePixel;
            RenderSettings.sun=light; RenderSettings.ambientMode=AmbientMode.Flat; RenderSettings.ambientLight=Color.black; RenderSettings.reflectionIntensity=0;
            var controller=instance.AddComponent<SandroneM4Controller>();
            controller.Configure(renderer,instance.transform,Find(instance.transform,"頭"),light,shadowProfile);
            controller.SetLightDirectionToSource(SandroneM3Bootstrap.DefaultDirectionToLight);
            var ground=GameObject.CreatePrimitive(PrimitiveType.Plane); ground.name="M4_ShadowGround";
            ground.transform.position=new Vector3(0,-0.008f,0); ground.transform.localScale=new Vector3(0.45f,1,0.45f);
            var gr=ground.GetComponent<MeshRenderer>(); gr.sharedMaterial=groundMaterial; gr.shadowCastingMode=ShadowCastingMode.Off; gr.receiveShadows=true;
            var cameraObject=new GameObject("M4_CalibrationCamera"); var camera=cameraObject.AddComponent<Camera>();
            camera.clearFlags=CameraClearFlags.SolidColor; camera.backgroundColor=Background; camera.orthographic=true;
            camera.allowHDR=false; camera.allowMSAA=true; camera.nearClipPlane=0.1f; camera.farClipPlane=20;

            // Renderer material-index MPBs are not reliable on this freshly
            // constructed edit-mode object until the scene has completed one
            // serialization/registration cycle. Persist assets, reload the scene,
            // and only then capture debug/A-B evidence.
            EditorUtility.SetDirty(controller); EditorSceneManager.MarkSceneDirty(scene);
            AssetDatabase.SaveAssets(); EditorSceneManager.SaveScene(scene,ScenePath);
            scene=EditorSceneManager.OpenScene(ScenePath,OpenSceneMode.Single);
            controller=UnityEngine.Object.FindFirstObjectByType<SandroneM4Controller>();
            camera=UnityEngine.Object.FindFirstObjectByType<Camera>();
            if(controller==null||camera==null) throw new InvalidOperationException("Reloaded M4 scene lost controller/camera.");
            instance=controller.CharacterRoot.gameObject;
            renderer=(SkinnedMeshRenderer)controller.TargetRenderer;
            ground=GameObject.Find("M4_ShadowGround");
            if(ground==null) throw new InvalidOperationException("Reloaded M4 scene lost shadow ground.");

            Capture(camera,controller,Quaternion.identity,SandroneM4DebugMode.FinalToon,true,true,true,0.92f,994,1654,"ReferenceComparison/M4_FinalToon_Front.png");
            Capture(camera,controller,Quaternion.Euler(0,-90,0),SandroneM4DebugMode.FinalToon,true,true,true,0.86f,662,1032,"ReferenceComparison/M4_FinalToon_Side.png");
            foreach(SandroneM4DebugMode mode in Enum.GetValues(typeof(SandroneM4DebugMode)))
                if(mode!=SandroneM4DebugMode.FinalToon) Capture(camera,controller,Quaternion.identity,mode,true,true,true,0.92f,768,1280,$"Debug/M4_{mode}.png");
            Capture(camera,controller,Quaternion.identity,SandroneM4DebugMode.FinalToon,true,true,true,0.92f,768,1280,"AB/M4_AllOn.png");
            Capture(camera,controller,Quaternion.identity,SandroneM4DebugMode.FinalToon,false,true,true,0.92f,768,1280,"AB/M4_MetalOff.png");
            Capture(camera,controller,Quaternion.identity,SandroneM4DebugMode.FinalToon,true,false,true,0.92f,768,1280,"AB/M4_StockingOff.png");
            Capture(camera,controller,Quaternion.identity,SandroneM4DebugMode.FinalToon,true,true,false,0.92f,768,1280,"AB/M4_HairOverlayOff.png");
            CaptureIsolation(camera,controller,isolationShader,24,new Color(1f,0.72f,0.12f,1f),true,"AB/M4_MetalSlot24_IsolationOn.png");
            CaptureIsolation(camera,controller,isolationShader,24,new Color(1f,0.72f,0.12f,1f),false,"AB/M4_MetalSlot24_IsolationOff.png");
            CaptureIsolation(camera,controller,isolationShader,28,new Color(0.25f,0.55f,1f,1f),true,"AB/M4_StockingSlot28_IsolationOn.png");
            CaptureIsolation(camera,controller,isolationShader,28,new Color(0.25f,0.55f,1f,1f),false,"AB/M4_StockingSlot28_IsolationOff.png");
            CaptureIsolation(camera,controller,isolationShader,29,new Color(1f,0.25f,0.72f,1f),true,"AB/M4_HairSlot29_IsolationOn.png");
            CaptureIsolation(camera,controller,isolationShader,29,new Color(1f,0.25f,0.72f,1f),false,"AB/M4_HairSlot29_IsolationOff.png");
            CaptureMaterialMask(camera,controller,isolationShader,24,"Masks/M4_Slot24_AlphaAwareMask.png");
            foreach(var skirtSlot in Enumerable.Range(20,8))
                CaptureMaterialColor(camera,controller,isolationShader,skirtSlot,$"AB/SkirtSlots/M4_Slot{skirtSlot}_Color.png");
            foreach(var cull in new[]{0f,1f,2f})
            {
                CaptureMaterialColor(camera,controller,isolationShader,21,$"AB/SkirtSlots/M4_Slot21_Cull{cull:0}.png",cull);
                CaptureMaterialColor(camera,controller,isolationShader,26,$"AB/SkirtSlots/M4_Slot26_Cull{cull:0}.png",cull);
            }
            CaptureMaterialMask(camera,controller,isolationShader,28,"Masks/M4_Slot28_AlphaAwareMask.png");
            CaptureMaterialMask(camera,controller,isolationShader,29,"Masks/M4_Slot29_AlphaAwareMask.png");
            CaptureSlotFeature(camera,controller,24,true,"AB/M4_Event24_MetalSlotOn.png");
            CaptureSlotFeature(camera,controller,24,false,"AB/M4_Event24_MetalSlotOff.png");
            // Regression evidence for the M5 skirt-color incident: slot 21 is the
            // long outer skirt panel identified by the alpha-aware slot audit.
            CaptureSlotFeature(camera,controller,21,true,"AB/M4_Event21_SkirtMetalWeightOn.png");
            CaptureSlotFeature(camera,controller,21,false,"AB/M4_Event21_SkirtMetalWeightOff.png");
            CaptureSlotFeature(camera,controller,28,true,"AB/M4_Event28_StockingSlotOn.png");
            CaptureSlotFeature(camera,controller,28,false,"AB/M4_Event28_StockingSlotOff.png");
            CaptureSlotFeature(camera,controller,29,true,"AB/M4_Event29_HairSlotOn.png");
            CaptureSlotFeature(camera,controller,29,false,"AB/M4_Event29_HairSlotOff.png");
            Capture(camera,controller,Quaternion.Euler(0,-35,0),SandroneM4DebugMode.MatCapSample,true,true,true,0.92f,768,1280,"Debug/M4_MatCapSample_ThreeQuarter.png");
            instance.transform.rotation=Quaternion.identity; ground.SetActive(true); controller.DebugMode=SandroneM4DebugMode.FinalToon;
            controller.MetalEnabled=true; controller.StockingEnabled=true; controller.HairOverlayEnabled=true;
            controller.SetLightDirectionToSource(SandroneM3Bootstrap.DefaultDirectionToLight); controller.Apply(true);
            ConfigureCamera(camera,new Vector3(2.6f,2.25f,3.8f),new Vector3(0,0.65f,0),1.35f,Background);
            EditorUtility.SetDirty(controller); EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene,ScenePath);
            EditorBuildSettings.scenes=new[]{new EditorBuildSettingsScene(ScenePath,true)};
        }

        private static void CaptureIsolation(Camera camera,SandroneM4Controller c,Shader shader,int slot,Color color,bool enabled,string relative)
        {
            var original=c.TargetRenderer.sharedMaterials; var transparent=new Material(shader){name="M4_Isolation_Transparent"};
            transparent.SetColor("_Color",new Color(0,0,0,0)); var visible=new Material(shader){name="M4_Isolation_Visible"};
            visible.SetColor("_Color",color); var materials=Enumerable.Repeat(transparent,original.Length).ToArray();
            if(enabled) materials[slot]=visible; c.TargetRenderer.sharedMaterials=materials; c.CharacterRoot.rotation=Quaternion.identity;
            ConfigureCamera(camera,new Vector3(0,0.82f,4),new Vector3(0,0.82f,0),0.92f,Color.black);
            var root=Path.GetFullPath(Path.Combine(Application.dataPath,"../TestArtifacts/M4")); var output=Path.Combine(root,relative);
            Directory.CreateDirectory(Path.GetDirectoryName(output)??root); var previous=RenderTexture.active;
            var rt=new RenderTexture(768,1280,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB); var image=new Texture2D(768,1280,TextureFormat.RGB24,false,false);
            try{ camera.targetTexture=rt; rt.Create(); camera.Render(); RenderTexture.active=rt; image.ReadPixels(new Rect(0,0,768,1280),0,0); image.Apply(); File.WriteAllBytes(output,image.EncodeToPNG()); }
            finally{ camera.targetTexture=null; RenderTexture.active=previous; c.TargetRenderer.sharedMaterials=original; UnityEngine.Object.DestroyImmediate(image); rt.Release(); UnityEngine.Object.DestroyImmediate(rt); UnityEngine.Object.DestroyImmediate(transparent); UnityEngine.Object.DestroyImmediate(visible); }
        }

        private static void CaptureSlotFeature(Camera camera,SandroneM4Controller c,int slot,bool enabled,string relative)
        {
            c.ClearMaterialSlotFeatureWeights();
            c.DebugMode=SandroneM4DebugMode.FinalToon; c.MetalEnabled=true; c.StockingEnabled=true; c.HairOverlayEnabled=true;
            if(!enabled) c.SetMaterialSlotFeatureWeight(slot,0f);
            c.CharacterRoot.rotation=Quaternion.identity;
            c.SetLightDirectionToSource(SandroneM3Bootstrap.DefaultDirectionToLight); c.Apply(true);
            var appliedBlock=new MaterialPropertyBlock(); c.TargetRenderer.GetPropertyBlock(appliedBlock,slot);
            var featureWeightId=Shader.PropertyToID("_M4FeatureWeight");
            var slotMaterial=c.TargetRenderer.sharedMaterials[slot];
            var hasFeature=slotMaterial.HasProperty("_FeatureGroup"); var featureGroup=slotMaterial.GetFloat("_FeatureGroup");
            var appliedWeight=appliedBlock.GetFloat(featureWeightId);
            var expectedWeight=enabled?1f:0f;
            if(!hasFeature||!Mathf.Approximately(appliedWeight,expectedWeight))
                throw new InvalidOperationException($"M4 slot {slot} MPB update failed: shader={slotMaterial.shader.name}, group={featureGroup}, expected={expectedWeight}, actual={appliedWeight}");
            ConfigureCamera(camera,new Vector3(0,0.82f,4),new Vector3(0,0.82f,0),0.92f,Background);
            var root=Path.GetFullPath(Path.Combine(Application.dataPath,"../TestArtifacts/M4")); var output=Path.Combine(root,relative);
            Directory.CreateDirectory(Path.GetDirectoryName(output)??root); var previous=RenderTexture.active;
            var rt=new RenderTexture(768,1280,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB){antiAliasing=1};
            var image=new Texture2D(768,1280,TextureFormat.RGB24,false,false);
            try{ camera.targetTexture=rt; rt.Create(); camera.Render(); RenderTexture.active=rt; image.ReadPixels(new Rect(0,0,768,1280),0,0); image.Apply(); File.WriteAllBytes(output,image.EncodeToPNG()); }
            finally{ camera.targetTexture=null; RenderTexture.active=previous; UnityEngine.Object.DestroyImmediate(image); rt.Release(); UnityEngine.Object.DestroyImmediate(rt); c.ClearMaterialSlotFeatureWeights(); }
        }

        private static void CaptureMaterialMask(Camera camera,SandroneM4Controller c,Shader isolationShader,int slot,string relative)
        {
            var original=c.TargetRenderer.sharedMaterials;
            var transparent=new Material(isolationShader){name="M4_Mask_Transparent"}; transparent.SetColor("_Color",new Color(0,0,0,0));
            var target=new Material(original[slot]){name=$"M4_Slot{slot}_AlphaAwareMask"};
            var materials=Enumerable.Repeat(transparent,original.Length).ToArray(); materials[slot]=target;
            c.TargetRenderer.sharedMaterials=materials; c.ClearMaterialSlotFeatureWeights(); c.DebugMode=SandroneM4DebugMode.Silhouette;
            c.MetalEnabled=true; c.StockingEnabled=true; c.HairOverlayEnabled=true; c.CharacterRoot.rotation=Quaternion.identity; c.Apply(true);
            ConfigureCamera(camera,new Vector3(0,0.82f,4),new Vector3(0,0.82f,0),0.92f,Color.black);
            var root=Path.GetFullPath(Path.Combine(Application.dataPath,"../TestArtifacts/M4")); var output=Path.Combine(root,relative);
            Directory.CreateDirectory(Path.GetDirectoryName(output)??root); var previous=RenderTexture.active;
            var rt=new RenderTexture(768,1280,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB){antiAliasing=1}; var image=new Texture2D(768,1280,TextureFormat.RGB24,false,false);
            try{camera.targetTexture=rt;rt.Create();camera.Render();RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,768,1280),0,0);image.Apply();File.WriteAllBytes(output,image.EncodeToPNG());}
            finally{camera.targetTexture=null;RenderTexture.active=previous;c.TargetRenderer.sharedMaterials=original;UnityEngine.Object.DestroyImmediate(image);rt.Release();UnityEngine.Object.DestroyImmediate(rt);UnityEngine.Object.DestroyImmediate(transparent);UnityEngine.Object.DestroyImmediate(target);c.DebugMode=SandroneM4DebugMode.FinalToon;c.Apply(true);}
        }

        private static void CaptureMaterialColor(Camera camera,SandroneM4Controller c,Shader isolationShader,int slot,string relative,float? cullOverride=null)
        {
            var original=c.TargetRenderer.sharedMaterials;
            var transparent=new Material(isolationShader){name="M4_ColorIsolation_Transparent"};
            transparent.SetColor("_Color",new Color(0,0,0,0));
            Material target=null;
            if(cullOverride.HasValue){target=new Material(original[slot]){name=$"M4_Slot{slot}_Cull{cullOverride.Value:0}"};target.SetFloat("_Cull",cullOverride.Value);}
            var materials=Enumerable.Repeat(transparent,original.Length).ToArray(); materials[slot]=target!=null?target:original[slot];
            c.TargetRenderer.sharedMaterials=materials; c.ClearMaterialSlotFeatureWeights(); c.DebugMode=SandroneM4DebugMode.FinalToon;
            c.MetalEnabled=true; c.StockingEnabled=true; c.HairOverlayEnabled=true; c.CharacterRoot.rotation=Quaternion.identity; c.Apply(true);
            ConfigureCamera(camera,new Vector3(0,0.82f,4),new Vector3(0,0.82f,0),0.92f,Background);
            var root=Path.GetFullPath(Path.Combine(Application.dataPath,"../TestArtifacts/M4")); var output=Path.Combine(root,relative);
            Directory.CreateDirectory(Path.GetDirectoryName(output)??root); var previous=RenderTexture.active;
            var rt=new RenderTexture(994,1654,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB){antiAliasing=1}; var image=new Texture2D(994,1654,TextureFormat.RGB24,false,false);
            try{camera.targetTexture=rt;rt.Create();camera.Render();RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,994,1654),0,0);image.Apply();File.WriteAllBytes(output,image.EncodeToPNG());}
            finally{camera.targetTexture=null;RenderTexture.active=previous;c.TargetRenderer.sharedMaterials=original;UnityEngine.Object.DestroyImmediate(image);rt.Release();UnityEngine.Object.DestroyImmediate(rt);UnityEngine.Object.DestroyImmediate(transparent);if(target!=null)UnityEngine.Object.DestroyImmediate(target);c.Apply(true);}
        }

        private static void Capture(Camera camera,SandroneM4Controller c,Quaternion rotation,SandroneM4DebugMode mode,
            bool metal,bool stocking,bool hair,float size,int width,int height,string relative)
        {
            var originalMaterials=c.TargetRenderer.sharedMaterials;
            var captureMaterials=originalMaterials.Select(m=>new Material(m){name=m.name+"_CaptureInstance"}).ToArray();
            foreach(var material in captureMaterials)
            {
                var feature=Mathf.RoundToInt(material.GetFloat("_FeatureGroup"));
                if(feature==(int)SandroneM4FeatureGroup.Metal && !metal) material.SetFloat("_ResponseType",(float)SandroneM4ResponseType.Matte);
                if(feature==(int)SandroneM4FeatureGroup.StockingOverlay && !stocking) material.SetFloat("_LayerWeight",0f);
                if(feature==(int)SandroneM4FeatureGroup.HairOverlay && !hair) material.SetFloat("_LayerWeight",0f);
            }
            c.TargetRenderer.sharedMaterials=captureMaterials;
            c.CharacterRoot.rotation=rotation; c.DebugMode=mode; c.MetalEnabled=metal; c.StockingEnabled=stocking; c.HairOverlayEnabled=hair;
            c.SetLightDirectionToSource(SandroneM3Bootstrap.DefaultDirectionToLight); c.Apply(true);
            ConfigureCamera(camera,new Vector3(0,0.82f,4),new Vector3(0,0.82f,0),size,mode==SandroneM4DebugMode.FinalToon?Background:Color.black);
            var root=Path.GetFullPath(Path.Combine(Application.dataPath,"../TestArtifacts/M4")); var output=Path.Combine(root,relative);
            Directory.CreateDirectory(Path.GetDirectoryName(output)??root);
            var previous=RenderTexture.active; var rt=new RenderTexture(width,height,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB){antiAliasing=1};
            var image=new Texture2D(width,height,TextureFormat.RGB24,false,false);
            try{ camera.targetTexture=rt; rt.Create(); camera.Render(); RenderTexture.active=rt; image.ReadPixels(new Rect(0,0,width,height),0,0); image.Apply(); File.WriteAllBytes(output,image.EncodeToPNG()); }
            finally
            {
                camera.targetTexture=null; RenderTexture.active=previous; UnityEngine.Object.DestroyImmediate(image); rt.Release(); UnityEngine.Object.DestroyImmediate(rt);
                c.TargetRenderer.sharedMaterials=originalMaterials;
                foreach(var material in captureMaterials) UnityEngine.Object.DestroyImmediate(material);
                c.ClearMaterialSlotFeatureWeights();
            }
        }

        private static void ConfigureCamera(Camera c,Vector3 position,Vector3 target,float size,Color background)
        { c.transform.position=position; c.transform.rotation=Quaternion.LookRotation((target-position).normalized,Vector3.up); c.orthographicSize=size; c.backgroundColor=background; }

        private static void CreateControlIfMissing(string outputPath,string sourcePath)
        {
            var absolute=Absolute(outputPath); if(File.Exists(absolute)) return;
            var sourceAbsolute=Absolute(sourcePath); if(!File.Exists(sourceAbsolute)) throw new FileNotFoundException(sourceAbsolute);
            var source=new Texture2D(2,2,TextureFormat.RGBA32,false,false);
            try
            {
                if(!source.LoadImage(File.ReadAllBytes(sourceAbsolute),false)) throw new InvalidOperationException($"Cannot decode {sourcePath}");
                var input=source.GetPixels32(); var output=new Texture2D(source.width,source.height,TextureFormat.RGBA32,false,true);
                var pixels=new Color32[input.Length];
                for(var i=0;i<input.Length;i++)
                {
                    var r=input[i].r/255f; var g=input[i].g/255f; var b=input[i].b/255f;
                    var luminance=0.2126f*r+0.7152f*g+0.0722f*b;
                    var spec=Mathf.Clamp01(0.24f+0.82f*Mathf.Pow(luminance,0.72f));
                    var gold=(r>g*1.025f && g>b*1.08f && r>0.30f)?Mathf.Clamp01((r-b)*2.2f):0f;
                    pixels[i]=new Color(spec,1f,gold,0f);
                }
                output.SetPixels32(pixels); output.Apply(false,false); File.WriteAllBytes(absolute,output.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(output);
            }
            finally{ UnityEngine.Object.DestroyImmediate(source); }
        }

        private static void CreateNeutralIfMissing()
        {
            var path=Absolute(NeutralControlPath); if(File.Exists(path)) return;
            var t=new Texture2D(16,16,TextureFormat.RGBA32,false,true);
            try{ var pixels=Enumerable.Repeat(new Color(0.65f,1,0,0),256).ToArray(); t.SetPixels(pixels); t.Apply(); File.WriteAllBytes(path,t.EncodeToPNG()); }
            finally{ UnityEngine.Object.DestroyImmediate(t); }
        }

        private static void CreateMatCapIfMissing()
        {
            var path=Absolute(MatCapPath); if(File.Exists(path)) return;
            const int size=512; var t=new Texture2D(size,size,TextureFormat.RGB24,false,false);
            try
            {
                var pixels=new Color[size*size];
                for(var y=0;y<size;y++) for(var x=0;x<size;x++)
                {
                    var u=(x+0.5f)/size*2-1; var v=(y+0.5f)/size*2-1; var rr=u*u+v*v;
                    if(rr>1){ pixels[y*size+x]=new Color(0.015f,0.02f,0.028f); continue; }
                    var z=Mathf.Sqrt(1-rr); var warm=Mathf.Pow(Mathf.Clamp01(u*-0.42f+v*0.62f+z*0.72f),12);
                    var cool=Mathf.Pow(Mathf.Clamp01(u*0.55f-v*0.25f+z*0.35f),5); var rim=Mathf.Pow(1-z,2.4f);
                    pixels[y*size+x]=new Color(0.035f+0.78f*warm+0.12f*rim,0.045f+0.52f*warm+0.18f*cool,0.07f+0.22f*warm+0.42f*cool);
                }
                t.SetPixels(pixels); t.Apply(); File.WriteAllBytes(path,t.EncodeToPNG());
            }
            finally{ UnityEngine.Object.DestroyImmediate(t); }
        }

        private static void ConfigureTextureImporter(string path,bool srgb)
        {
            if(AssetImporter.GetAtPath(path) is not TextureImporter i) return;
            i.textureType=TextureImporterType.Default; i.sRGBTexture=srgb; i.alphaSource=TextureImporterAlphaSource.FromInput;
            i.mipmapEnabled=false; i.wrapMode=TextureWrapMode.Clamp; i.filterMode=FilterMode.Bilinear;
            i.textureCompression=TextureImporterCompression.Uncompressed; i.SaveAndReimport();
        }

        private static Transform Find(Transform root,string name) => root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t=>t.name==name);
        private static string Absolute(string assetPath) => Path.GetFullPath(Path.Combine(Application.dataPath,"..",assetPath));
        private static void EnsureFolder(string assetPath) => Directory.CreateDirectory(Absolute(assetPath));
    }
}
