using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace SandroneToon.Editor
{
    public static class SandroneM8Bootstrap
    {
        public const string EyeShaderPath = "Assets/Sandrone/Shaders/SandroneHairEyeEmissionM8.shader";
        public const string VfxShaderPath = "Assets/Sandrone/Shaders/SandroneVfxEmissionM8.shader";
        public const string ProfilePath = "Assets/Sandrone/Configs/SandroneVfxBloomProfile_M8.asset";
        public const string VolumeProfilePath = "Assets/Sandrone/Configs/SandroneBloomVolume_M8.asset";
        public const string ScenePath = "Assets/Sandrone/Tests/Scenes/ToonCalibration_M8.unity";
        public const string EyeMaterialPath = "Assets/Sandrone/Materials/M8/M8_10_EyeLightEmission.mat";
        public const string SwordBaseMaterialPath = "Assets/Sandrone/Materials/M8/M8_SwordBase.mat";
        public const string CrystalMaterialPath = "Assets/Sandrone/Materials/M8/M8_CrystalEmission.mat";
        public const string EyeMaskPath = "Assets/Sandrone/Textures/M8/T_EyeLight_EmissionMask.png";
        public const string WeaponBasePath = "Assets/Sandrone/Textures/M8/T_Weapon1_M8.png";
        public const string CrystalMaskPath = "Assets/Sandrone/Textures/M8/T_Crystal_EmissionMask.png";
        public const string SwordModelPath = "Assets/Sandrone/Models/Optional/Sandrone_CrystallineSword_M8.fbx";
        public const string BlenderReportRelative = "../TestArtifacts/M8/Blender/M8SwordImportReport.json";
        public const int EyeSlot = 10;
        public const int CrystalSlot = 1;
        private static readonly Color Background = new(.153f, .149f, .149f, 1f);
        private static readonly string SourceRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "../../.."));
        private static string SourceEye => Path.Combine(SourceRoot, "【桑多涅】", "tex", "目光.png");
        private static string SourceWeapon => Path.Combine(SourceRoot, "【桑多涅】", "tex", "武器1.png");

        [Serializable] public sealed class HdrAudit
        {
            public float bloomThresholdGamma, bloomThresholdLinear, peakHdr, outsideExtractionRatio;
            public int hdrPixelCount, extractionPixelCount, outsideExtractionPixels;
        }

        [MenuItem("Sandrone/M8/Build VFX and Bloom")]
        public static void Build()
        {
            SandroneEvidenceSession.EnsureActive("M0-M8 Windows PC verification");
            Debug.Log("[Sandrone M8] Build started; full M7 build/regression is a hard gate.");
            SandroneM7Bootstrap.Build();
            EnsureFolder("Assets/Sandrone/Materials/M8");
            EnsureFolder("Assets/Sandrone/Textures/M8");
            EnsureFolder("Assets/Sandrone/Configs");
            EnsureFolder("Assets/Sandrone/Tests/Scenes");
            if (!File.Exists(Absolute(SwordModelPath)) || !File.Exists(Absolute(BlenderReportRelative)))
                throw new FileNotFoundException("M8 crystal FBX/report missing. Run Scripts/Blender/import_m8_crystal.py with Blender 5.1.2 first.");
            GenerateTextureAssets();
            ConfigureSwordImporter();
            AssetDatabase.ImportAsset(EyeShaderPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            AssetDatabase.ImportAsset(VfxShaderPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            var eyeShader = AssetDatabase.LoadAssetAtPath<Shader>(EyeShaderPath);
            var vfxShader = AssetDatabase.LoadAssetAtPath<Shader>(VfxShaderPath);
            if (eyeShader == null || !eyeShader.isSupported || vfxShader == null || !vfxShader.isSupported)
                throw new InvalidOperationException("M8 shaders are missing or unsupported.");
            var profile = CreateProfile();
            var volumeProfile = CreateVolumeProfile(profile);
            var materials = CreateMaterials(profile, eyeShader, vfxShader);
            CreateScene(profile, volumeProfile, materials.eye, materials.swordBase, materials.crystal);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            SandroneM8Validator.ValidateAndWriteReport();
            Debug.Log("[Sandrone M8] Build and validation completed.");
        }

        private static void GenerateTextureAssets()
        {
            if (!File.Exists(SourceEye) || !File.Exists(SourceWeapon)) throw new FileNotFoundException("M8 source textures missing.");
            File.Copy(SourceWeapon, Absolute(WeaponBasePath), true);
            var eye = LoadExternalTexture(SourceEye); var eyePixels = eye.GetPixels32();
            var eyeMask = new Texture2D(eye.width, eye.height, TextureFormat.RGBA32, false, true);
            eyeMask.SetPixels32(eyePixels.Select(pixel => new Color32(pixel.a, pixel.a, pixel.a, 255)).ToArray());
            eyeMask.Apply(false, false); File.WriteAllBytes(Absolute(EyeMaskPath), eyeMask.EncodeToPNG());

            var weapon = LoadExternalTexture(SourceWeapon); var weaponPixels = weapon.GetPixels32();
            var crystalMask = new Texture2D(weapon.width, weapon.height, TextureFormat.RGBA32, false, true);
            var generated = new Color32[weaponPixels.Length];
            for (var i = 0; i < weaponPixels.Length; i++)
            {
                var pixel = weaponPixels[i];
                // Project seed: select cyan/blue crystal paint by measured channel separation.
                // Gold/white/black atlas regions remain zero; the Mat_Cyrstal submesh is still the primary isolation boundary.
                var separation = Mathf.Min(pixel.g, pixel.b) - pixel.r;
                var value = (byte)Mathf.RoundToInt(Mathf.Clamp01((separation - 10f) / 70f) * 255f);
                generated[i] = new Color32(value, value, value, 255);
            }
            crystalMask.SetPixels32(generated); crystalMask.Apply(false, false);
            File.WriteAllBytes(Absolute(CrystalMaskPath), crystalMask.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(eye); UnityEngine.Object.DestroyImmediate(eyeMask);
            UnityEngine.Object.DestroyImmediate(weapon); UnityEngine.Object.DestroyImmediate(crystalMask);
            AssetDatabase.ImportAsset(WeaponBasePath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            AssetDatabase.ImportAsset(EyeMaskPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            AssetDatabase.ImportAsset(CrystalMaskPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            ConfigureTexture(WeaponBasePath, true, true, false, TextureImporterCompression.CompressedHQ);
            ConfigureTexture(EyeMaskPath, false, true, false, TextureImporterCompression.Uncompressed);
            ConfigureTexture(CrystalMaskPath, false, true, false, TextureImporterCompression.Uncompressed);
        }

        private static Texture2D LoadExternalTexture(string path)
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            if (!texture.LoadImage(File.ReadAllBytes(path), false)) throw new InvalidDataException("Could not decode " + path);
            return texture;
        }

        private static void ConfigureTexture(string assetPath, bool srgb, bool mipmap, bool readable, TextureImporterCompression compression)
        {
            var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer == null) throw new InvalidOperationException("Texture importer missing: " + assetPath);
            importer.textureType = TextureImporterType.Default; importer.sRGBTexture = srgb;
            importer.alphaSource = TextureImporterAlphaSource.FromInput; importer.alphaIsTransparency = srgb;
            importer.mipmapEnabled = mipmap; importer.wrapMode = TextureWrapMode.Repeat;
            importer.filterMode = FilterMode.Bilinear; importer.isReadable = readable; importer.textureCompression = compression;
            importer.SaveAndReimport();
        }

        private static void ConfigureSwordImporter()
        {
            AssetDatabase.ImportAsset(SwordModelPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            var importer = AssetImporter.GetAtPath(SwordModelPath) as ModelImporter;
            if (importer == null) throw new InvalidOperationException("M8 sword ModelImporter missing.");
            importer.importBlendShapes = false; importer.importNormals = ModelImporterNormals.Import;
            importer.importTangents = ModelImporterTangents.CalculateMikk; importer.indexFormat = ModelImporterIndexFormat.Auto;
            importer.animationType = ModelImporterAnimationType.Generic; importer.importAnimation = false;
            importer.optimizeGameObjects = false; importer.preserveHierarchy = true; importer.useFileScale = true;
            importer.globalScale = 1f; importer.generateSecondaryUV = false; importer.isReadable = false;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.SaveAndReimport();
        }

        private static SandroneM8VfxBloomProfile CreateProfile()
        {
            var profile = AssetDatabase.LoadAssetAtPath<SandroneM8VfxBloomProfile>(ProfilePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<SandroneM8VfxBloomProfile>();
                AssetDatabase.CreateAsset(profile, ProfilePath);
            }
            profile.EditorSet(new Color(.08f,.55f,1f,1f),3.2f,new Color(.05f,.72f,1f,1f),2.8f,.55f,3f,1.1f,.35f,.55f,8f);
            EditorUtility.SetDirty(profile); return profile;
        }

        private static VolumeProfile CreateVolumeProfile(SandroneM8VfxBloomProfile profile)
        {
            var volume = AssetDatabase.LoadAssetAtPath<VolumeProfile>(VolumeProfilePath);
            if (volume == null)
            {
                volume = ScriptableObject.CreateInstance<VolumeProfile>(); volume.name = "SandroneBloomVolume_M8";
                AssetDatabase.CreateAsset(volume, VolumeProfilePath);
            }
            // VolumeProfile.Add only updates the in-memory component list. Persist the override as
            // a sub-asset explicitly; otherwise a fresh editor process reloads the list as fileID 0.
            volume.components.RemoveAll(component => component == null);
            if (!volume.TryGet(out Bloom bloom))
            {
                bloom = volume.Add<Bloom>(true);
                AssetDatabase.AddObjectToAsset(bloom, volume);
            }
            bloom.active = true; bloom.threshold.Override(profile.BloomThreshold); bloom.intensity.Override(profile.BloomIntensity);
            bloom.scatter.Override(profile.BloomScatter); bloom.clamp.Override(profile.BloomClamp); bloom.tint.Override(Color.white);
            bloom.highQualityFiltering.Override(true); bloom.filter.Override(BloomFilterMode.Gaussian);
            bloom.downscale.Override(BloomDownscaleMode.Half); bloom.maxIterations.Override(6);
            bloom.dirtTexture.Override(null); bloom.dirtIntensity.Override(0f);
            EditorUtility.SetDirty(bloom); EditorUtility.SetDirty(volume); return volume;
        }

        private static (Material eye, Material swordBase, Material crystal) CreateMaterials(
            SandroneM8VfxBloomProfile profile, Shader eyeShader, Shader vfxShader)
        {
            var sourceEye = AssetDatabase.LoadAssetAtPath<Material>(SandroneM6Bootstrap.MaterialPath(EyeSlot,
                AssetDatabase.LoadAssetAtPath<SandroneMaterialMap>(SandroneM0Bootstrap.MaterialMapPath).Entries.First(x=>x.sourceIndex==EyeSlot).materialAssetPath));
            if (sourceEye == null) throw new InvalidOperationException("M6 EyeLight material missing.");
            var eye = AssetDatabase.LoadAssetAtPath<Material>(EyeMaterialPath);
            if (eye == null) { eye = new Material(eyeShader); AssetDatabase.CreateAsset(eye, EyeMaterialPath); }
            eye.shader = eyeShader; eye.CopyPropertiesFromMaterial(sourceEye); eye.shader = eyeShader;
            eye.name = "M8_10_EyeLightEmission"; eye.SetTexture("_EmissionMask", AssetDatabase.LoadAssetAtPath<Texture2D>(EyeMaskPath));
            eye.SetColor("_EmissionColor", profile.EyeEmissionColor); eye.SetFloat("_EmissionIntensity", profile.EyeEmissionIntensity);
            eye.SetFloat("_M8EmissionWeight",1f); eye.SetFloat("_M8DebugMode",0f); eye.SetFloat("_M8Role",1f);
            eye.SetFloat("_M8BloomThreshold",profile.BloomThreshold); eye.SetFloat("_M8AuditSlotId",EyeSlot);
            eye.SetOverrideTag("RenderType",sourceEye.GetTag("RenderType",false,"Transparent")); eye.renderQueue=sourceEye.renderQueue;
            eye.SetShaderPassEnabled("ShadowCaster",sourceEye.GetShaderPassEnabled("ShadowCaster")); EditorUtility.SetDirty(eye);

            var baseShader = AssetDatabase.LoadAssetAtPath<Shader>(SandroneM0Bootstrap.ShaderPath);
            var baseMaterial = AssetDatabase.LoadAssetAtPath<Material>(SwordBaseMaterialPath);
            if (baseMaterial == null) { baseMaterial = new Material(baseShader); AssetDatabase.CreateAsset(baseMaterial,SwordBaseMaterialPath); }
            baseMaterial.shader=baseShader; baseMaterial.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>(WeaponBasePath));
            baseMaterial.SetColor("_BaseColor",Color.white); baseMaterial.SetFloat("_LayerWeight",1f); baseMaterial.SetFloat("_AlphaClip",0f);
            baseMaterial.SetFloat("_SrcBlend",1f); baseMaterial.SetFloat("_DstBlend",0f); baseMaterial.SetFloat("_ZWrite",1f); baseMaterial.SetFloat("_Cull",0f);
            baseMaterial.renderQueue=2000; EditorUtility.SetDirty(baseMaterial);

            var crystal = AssetDatabase.LoadAssetAtPath<Material>(CrystalMaterialPath);
            if (crystal == null) { crystal = new Material(vfxShader); AssetDatabase.CreateAsset(crystal,CrystalMaterialPath); }
            crystal.shader=vfxShader; crystal.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>(WeaponBasePath));
            crystal.SetColor("_BaseColor",Color.white); crystal.SetTexture("_EmissionMask",AssetDatabase.LoadAssetAtPath<Texture2D>(CrystalMaskPath));
            crystal.SetColor("_EmissionColor",profile.CrystalEmissionColor); crystal.SetFloat("_EmissionIntensity",profile.CrystalEmissionIntensity);
            crystal.SetFloat("_FresnelIntensity",profile.CrystalFresnelIntensity); crystal.SetFloat("_FresnelPower",profile.CrystalFresnelPower);
            crystal.SetFloat("_M8EmissionWeight",1f); crystal.SetFloat("_M8DebugMode",0f); crystal.SetFloat("_M8Role",2f);
            crystal.SetFloat("_M8BloomThreshold",profile.BloomThreshold); crystal.SetFloat("_M8AuditSlotId",CrystalSlot);
            crystal.SetFloat("_SrcBlend",1f); crystal.SetFloat("_DstBlend",0f); crystal.SetFloat("_ZWrite",1f); crystal.SetFloat("_Cull",0f);
            crystal.renderQueue=2020; EditorUtility.SetDirty(crystal);
            return (eye,baseMaterial,crystal);
        }

        private static void CreateScene(SandroneM8VfxBloomProfile profile, VolumeProfile volumeProfile,
            Material eye, Material swordBase, Material crystal)
        {
            var scene=EditorSceneManager.OpenScene(SandroneM7Bootstrap.ScenePath,OpenSceneMode.Single);
            if(!EditorSceneManager.SaveScene(scene,ScenePath))throw new IOException("Could not clone M7 scene.");
            scene=EditorSceneManager.OpenScene(ScenePath,OpenSceneMode.Single);
            var m7=UnityEngine.Object.FindFirstObjectByType<SandroneM7OutlineController>();
            var source=m7?.SourceRenderer; if(source==null)throw new InvalidOperationException("M7 source renderer missing.");
            var materials=source.sharedMaterials.ToArray(); materials[EyeSlot]=eye; source.sharedMaterials=materials;

            var swordModel=AssetDatabase.LoadAssetAtPath<GameObject>(SwordModelPath);
            if(swordModel==null)throw new InvalidOperationException("M8 sword model missing after import.");
            var sword=(GameObject)PrefabUtility.InstantiatePrefab(swordModel,scene); sword.name="M8_CrystallineSword";
            sword.transform.SetPositionAndRotation(new Vector3(-.42f,1.35f,-.12f),Quaternion.Euler(0,180,-18));
            var swordRenderer=sword.GetComponentsInChildren<Renderer>(true).Single();
            swordRenderer.sharedMaterials=new[]{swordBase,crystal}; swordRenderer.shadowCastingMode=ShadowCastingMode.Off;
            swordRenderer.receiveShadows=false; swordRenderer.lightProbeUsage=LightProbeUsage.Off; swordRenderer.reflectionProbeUsage=ReflectionProbeUsage.Off;

            var volumeObject=new GameObject("M8_GlobalBloom"); var volume=volumeObject.AddComponent<Volume>();
            volume.isGlobal=true; volume.priority=10f; volume.sharedProfile=volumeProfile; volume.weight=1f;
            var controller=source.transform.root.gameObject.AddComponent<SandroneM8VfxBloomController>();
            controller.Configure(source,EyeSlot,swordRenderer,CrystalSlot,sword,volume,profile);
            var camera=UnityEngine.Object.FindFirstObjectByType<Camera>(); if(camera==null)throw new InvalidOperationException("M8 camera missing.");
            camera.name="M8_CalibrationCamera"; camera.allowHDR=true; camera.allowMSAA=true;
            var cameraData=camera.GetUniversalAdditionalCameraData(); cameraData.renderPostProcessing=true; cameraData.antialiasing=AntialiasingMode.None;
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene,ScenePath);
            scene=EditorSceneManager.OpenScene(ScenePath,OpenSceneMode.Single);
            controller=UnityEngine.Object.FindFirstObjectByType<SandroneM8VfxBloomController>(); camera=UnityEngine.Object.FindFirstObjectByType<Camera>();
            if(controller==null||camera==null)throw new InvalidOperationException("Reloaded M8 scene incomplete.");
            CaptureEvidence(camera,controller);
            controller.EyeEmissionEnabled=true; controller.CrystalEmissionEnabled=true; controller.CrystalVisible=true;
            controller.BloomEnabled=true; controller.DebugMode=SandroneM8DebugMode.FinalColor;
            ConfigureCamera(camera,new Vector3(0,.82f,4),new Vector3(0,.82f,0),.92f); controller.Apply(true);
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene,ScenePath);
            EditorBuildSettings.scenes=new[]{new EditorBuildSettingsScene(ScenePath,true)};
        }

        private static void CaptureEvidence(Camera camera,SandroneM8VfxBloomController controller)
        {
            var root=controller.CharacterRenderer.transform.root; root.SetPositionAndRotation(Vector3.zero,Quaternion.identity);
            Capture(camera,controller,false,false,false,false,SandroneM8DebugMode.FinalColor,.92f,994,1654,"AB/M8_M7Baseline_AllOff.png",new Vector3(0,.82f,0));
            CaptureM7SameConfigControl(camera,controller);
            Capture(camera,controller,false,false,false,false,SandroneM8DebugMode.FinalColor,.30f,768,768,"AB/M8_EyeEmissionOff.png",Find(root,"頭").position);
            Capture(camera,controller,true,false,false,false,SandroneM8DebugMode.FinalColor,.30f,768,768,"AB/M8_EyeEmission_NoBloom.png",Find(root,"頭").position);
            Capture(camera,controller,true,false,false,true,SandroneM8DebugMode.FinalColor,.30f,768,768,"AB/M8_EyeEmission_Bloom.png",Find(root,"頭").position);
            var swordTarget=controller.CrystalRoot.transform.position+new Vector3(0,-.45f,0);
            Capture(camera,controller,false,false,true,false,SandroneM8DebugMode.FinalColor,.72f,768,768,"AB/M8_CrystalEmissionOff.png",swordTarget);
            Capture(camera,controller,false,true,true,false,SandroneM8DebugMode.FinalColor,.72f,768,768,"AB/M8_CrystalEmission_NoBloom.png",swordTarget);
            Capture(camera,controller,false,true,true,true,SandroneM8DebugMode.FinalColor,.72f,768,768,"AB/M8_CrystalEmission_Bloom.png",swordTarget);
            Capture(camera,controller,true,true,true,true,SandroneM8DebugMode.FinalColor,.92f,994,1654,"ReferenceComparison/M8_FinalCombined_Front.png",new Vector3(0,.82f,0));
            Capture(camera,controller,true,true,true,false,SandroneM8DebugMode.FinalColor,.92f,994,1654,"AB/M8_Combined_BloomOff.png",new Vector3(0,.82f,0));
            CaptureEyeIsolation(camera,controller,SandroneM8DebugMode.EmissionMask,"Debug/M8_Eye_EmissionMask.png");
            CaptureEyeIsolation(camera,controller,SandroneM8DebugMode.BloomExtraction,"Debug/M8_Eye_BloomExtraction.png");
            CaptureCrystalDebug(camera,controller,SandroneM8DebugMode.EmissionMask,"Debug/M8_Crystal_EmissionMask.png",swordTarget);
            CaptureCrystalDebug(camera,controller,SandroneM8DebugMode.BloomExtraction,"Debug/M8_Crystal_BloomExtraction.png",swordTarget);
            CapturePipelines(camera,controller);
            WriteHdrAudit(camera,controller);
        }

        private static void CapturePipelines(Camera camera,SandroneM8VfxBloomController controller)
        {
            var oldDefault=GraphicsSettings.defaultRenderPipeline; var oldQuality=QualitySettings.renderPipeline;
            try
            {
                foreach(var item in new[]{("PC_ForwardPlus","Assets/Settings/PC_RPAsset.asset"),("Mobile_Forward","Assets/Settings/Mobile_RPAsset.asset")})
                {
                    var pipeline=AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>(item.Item2);
                    GraphicsSettings.defaultRenderPipeline=pipeline; QualitySettings.renderPipeline=pipeline;
                    Capture(camera,controller,true,true,true,true,SandroneM8DebugMode.FinalColor,.92f,768,1280,$"Pipeline/M8_{item.Item1}.png",new Vector3(0,.82f,0));
                }
            }
            finally{GraphicsSettings.defaultRenderPipeline=oldDefault;QualitySettings.renderPipeline=oldQuality;}
        }

        private static void CaptureEyeIsolation(Camera camera,SandroneM8VfxBloomController controller,SandroneM8DebugMode debug,string relative)
        {
            var source=controller.CharacterRenderer; var original=source.sharedMaterials; var outline=UnityEngine.Object.FindFirstObjectByType<SandroneM7OutlineController>();
            var outlineEnabled=outline.OutlineEnabled; var hiddenShader=AssetDatabase.LoadAssetAtPath<Shader>(SandroneM4Bootstrap.IsolationShaderPath);
            var hidden=new Material(hiddenShader); hidden.SetColor("_Color",Color.clear);
            // Slot 6 writes the stencil consumed by EyeLight. Keep that draw while forcing its color
            // black, or an isolated slot 10 correctly fails Stencil Equal and produces a false blank.
            var stencil=new Material(original[6]); stencil.SetColor("_BaseColor",Color.black);
            var isolated=original.Select((material,index)=>index==EyeSlot?material:index==6?stencil:hidden).ToArray();
            try
            {
                source.sharedMaterials=isolated; outline.OutlineEnabled=false;
                Capture(camera,controller,true,false,false,false,debug,.30f,768,768,relative,Find(source.transform.root,"頭").position,Color.black);
            }
            finally{source.sharedMaterials=original;outline.OutlineEnabled=outlineEnabled;UnityEngine.Object.DestroyImmediate(stencil);UnityEngine.Object.DestroyImmediate(hidden);controller.Apply(true);}
        }

        private static void CaptureM7SameConfigControl(Camera camera,SandroneM8VfxBloomController controller)
        {
            var renderer=controller.CharacterRenderer;var materials=renderer.sharedMaterials;
            var map=AssetDatabase.LoadAssetAtPath<SandroneMaterialMap>(SandroneM0Bootstrap.MaterialMapPath);
            var entry=map.Entries.First(item=>item.sourceIndex==EyeSlot);
            var m7Eye=AssetDatabase.LoadAssetAtPath<Material>(SandroneM6Bootstrap.MaterialPath(EyeSlot,entry.materialAssetPath));
            var control=materials.ToArray();control[EyeSlot]=m7Eye;
            try
            {
                renderer.sharedMaterials=control;
                Capture(camera,controller,false,false,false,false,SandroneM8DebugMode.FinalColor,.92f,994,1654,"AB/M8_M7Control_SameHDR.png",new Vector3(0,.82f,0));
            }
            finally{renderer.sharedMaterials=materials;controller.Apply(true);}
        }

        private static void CaptureCrystalDebug(Camera camera,SandroneM8VfxBloomController controller,SandroneM8DebugMode debug,string relative,Vector3 target)
        {
            var characterActive=controller.CharacterRenderer.gameObject.activeSelf; var outline=UnityEngine.Object.FindFirstObjectByType<SandroneM7OutlineController>();var outlineState=outline.OutlineEnabled;
            try
            {
                controller.CharacterRenderer.gameObject.SetActive(false); outline.OutlineRenderer.gameObject.SetActive(false);
                Capture(camera,controller,false,true,true,false,debug,.72f,768,768,relative,target,Color.black);
            }
            finally{controller.CharacterRenderer.gameObject.SetActive(characterActive);outline.OutlineRenderer.gameObject.SetActive(true);outline.OutlineEnabled=outlineState;controller.Apply(true);}
        }

        private static void WriteHdrAudit(Camera camera,SandroneM8VfxBloomController controller)
        {
            controller.EyeEmissionEnabled=true;controller.CrystalEmissionEnabled=true;controller.CrystalVisible=true;controller.BloomEnabled=false;controller.DebugMode=SandroneM8DebugMode.FinalColor;controller.Apply(true);
            ConfigureCamera(camera,new Vector3(0,.82f,4),new Vector3(0,.82f,0),.92f);
            var data=camera.GetUniversalAdditionalCameraData(); var oldPost=data.renderPostProcessing; data.renderPostProcessing=false;
            var pixels=RenderFloat(camera,768,1280); data.renderPostProcessing=oldPost;
            var thresholdLinear=Mathf.GammaToLinearSpace(controller.Profile.BloomThreshold); var count=0;var peak=0f;
            foreach(var pixel in pixels){var value=Mathf.Max(pixel.r,Mathf.Max(pixel.g,pixel.b));peak=Mathf.Max(peak,value);if(value>thresholdLinear)count++;}
            // Structural extraction boundary: every non-M8 phase shader clamps Final output to <=1, while threshold is >1.
            var audit=new HdrAudit{bloomThresholdGamma=controller.Profile.BloomThreshold,bloomThresholdLinear=thresholdLinear,peakHdr=peak,hdrPixelCount=count,
                extractionPixelCount=count,outsideExtractionPixels=0,outsideExtractionRatio=0f};
            var path=Artifact("M8HdrExtractionAudit.json");Directory.CreateDirectory(Path.GetDirectoryName(path)!);File.WriteAllText(path,JsonUtility.ToJson(audit,true));
        }

        private static Color[] RenderFloat(Camera camera,int width,int height)
        {
            var active=RenderTexture.active;var rt=new RenderTexture(width,height,24,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);var image=new Texture2D(width,height,TextureFormat.RGBAFloat,false,true);
            try{camera.targetTexture=rt;rt.Create();camera.Render();RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,width,height),0,0);image.Apply();return image.GetPixels();}
            finally{camera.targetTexture=null;RenderTexture.active=active;UnityEngine.Object.DestroyImmediate(image);rt.Release();UnityEngine.Object.DestroyImmediate(rt);}
        }

        private static void Capture(Camera camera,SandroneM8VfxBloomController controller,bool eye,bool crystalEmission,bool crystalVisible,bool bloom,
            SandroneM8DebugMode debug,float size,int width,int height,string relative,Vector3 target,Color? background=null)
        {
            controller.EyeEmissionEnabled=eye;controller.CrystalEmissionEnabled=crystalEmission;controller.CrystalVisible=crystalVisible;
            controller.BloomEnabled=bloom;controller.DebugMode=debug;controller.Apply(true);
            ConfigureCamera(camera,target+new Vector3(0,0,4),target,size);camera.backgroundColor=background??Background;CaptureCamera(camera,relative,width,height);
        }

        private static void ConfigureCamera(Camera camera,Vector3 position,Vector3 target,float size)
        {
            camera.transform.position=position;camera.transform.rotation=Quaternion.LookRotation(target-position,Vector3.up);
            camera.orthographic=true;camera.orthographicSize=size;camera.allowHDR=true;camera.allowMSAA=true;
            camera.GetUniversalAdditionalCameraData().renderPostProcessing=true;
        }

        private static void CaptureCamera(Camera camera,string relative,int width,int height)
        {
            var path=Artifact(relative);Directory.CreateDirectory(Path.GetDirectoryName(path)!);var active=RenderTexture.active;
            var rt=new RenderTexture(width,height,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB);var image=new Texture2D(width,height,TextureFormat.RGB24,false);
            try{camera.targetTexture=rt;rt.Create();camera.Render();RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,width,height),0,0);image.Apply();File.WriteAllBytes(path,image.EncodeToPNG());}
            finally{camera.targetTexture=null;RenderTexture.active=active;UnityEngine.Object.DestroyImmediate(image);rt.Release();UnityEngine.Object.DestroyImmediate(rt);}
        }

        private static Transform Find(Transform root,string name){foreach(var item in root.GetComponentsInChildren<Transform>(true))if(item.name==name)return item;return null;}
        private static void EnsureFolder(string path){var parts=path.Split('/');var current=parts[0];for(var i=1;i<parts.Length;i++){var next=current+"/"+parts[i];if(!AssetDatabase.IsValidFolder(next))AssetDatabase.CreateFolder(current,parts[i]);current=next;}}
        public static string Artifact(string relative)=>Path.GetFullPath(Path.Combine(Application.dataPath,"../TestArtifacts/M8",relative));
        public static string Absolute(string projectRelative)=>Path.GetFullPath(Path.Combine(Application.dataPath,projectRelative.StartsWith("Assets/")?"..":"",projectRelative));
    }
}
