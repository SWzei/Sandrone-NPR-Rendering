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
    public static class SandroneM3Bootstrap
    {
        public const string ShaderPath = "Assets/Sandrone/Shaders/SandroneToonShadowM3.shader";
        public const string ReceiverShaderPath = "Assets/Sandrone/Shaders/SandroneShadowReceiverM3.shader";
        public const string ShadowProfilePath = "Assets/Sandrone/Configs/SandroneShadowProfile_M3.asset";
        public const string MaterialDirectory = "Assets/Sandrone/Materials/M3";
        public const string ReceiverMaterialPath = MaterialDirectory + "/M3_ShadowReceiver.mat";
        public const string BlockerMaterialPath = MaterialDirectory + "/M3_ValidationBlocker.mat";
        public const string AlphaProbeMaterialPath = MaterialDirectory + "/M3_AlphaClipProbe.mat";
        public const string AlphaProbeTexturePath = "Assets/Sandrone/Tests/Textures/M3_AlphaClipProbe.png";
        public const string ScenePath = "Assets/Sandrone/Tests/Scenes/ToonCalibration_M3.unity";
        public const float CastShadowStrength = 0.85f;
        public const float CastShadowLow = 0.20f;
        public const float CastShadowHigh = 0.80f;
        public const float AlphaCutoff = 0.5f;
        public static readonly Vector3 DefaultDirectionToLight = new Vector3(0.35f, 0.65f, 1f).normalized;

        private static readonly Color CalibrationBackground = new(0.153f, 0.149f, 0.149f, 1f);

        [MenuItem("Sandrone/M3/Build Real Shadows")]
        public static void Build()
        {
            Debug.Log("[Sandrone M3] Build started.");
            SandroneM2Validator.ValidateAndWriteReport();
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.productName = "Sandrone Toon M3";
            QualitySettings.antiAliasing = 4;

            EnsureFolder(MaterialDirectory);
            EnsureFolder("Assets/Sandrone/Configs");
            EnsureFolder("Assets/Sandrone/Tests/Scenes");
            EnsureFolder("Assets/Sandrone/Tests/Textures");
            CreateAlphaProbeTextureIfMissing();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            ConfigureAlphaProbeImporter();
            AssetDatabase.ImportAsset(ShaderPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            AssetDatabase.ImportAsset(ReceiverShaderPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);

            var shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
            var receiverShader = AssetDatabase.LoadAssetAtPath<Shader>(ReceiverShaderPath);
            var ramp = AssetDatabase.LoadAssetAtPath<Texture2D>(SandroneM2Bootstrap.RampTexturePath);
            var alphaProbe = AssetDatabase.LoadAssetAtPath<Texture2D>(AlphaProbeTexturePath);
            if (shader == null || receiverShader == null || ramp == null || alphaProbe == null ||
                !shader.isSupported || !receiverShader.isSupported)
            {
                throw new InvalidOperationException("M3 shaders, M2 Ramp or alpha probe are missing/unsupported.");
            }

            var profile = CreateShadowProfile();
            var materials = CreateCharacterMaterials(shader);
            var receiverMaterial = CreateReceiverMaterial(receiverShader);
            var blockerMaterial = CreateBlockerMaterial(shader, ramp);
            var alphaProbeMaterial = CreateAlphaProbeMaterial(shader, ramp, alphaProbe);
            CreateCalibrationScene(materials, profile, receiverMaterial, blockerMaterial, alphaProbeMaterial);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            SandroneM3Validator.ValidateAndWriteReport();
            Debug.Log("[Sandrone M3] Build and validation completed.");
        }

        public static string MaterialPath(int sourceIndex, string sourceMaterialPath)
        {
            return $"{MaterialDirectory}/M3_{sourceIndex:00}_{Path.GetFileNameWithoutExtension(sourceMaterialPath)}.mat";
        }

        private static SandroneM3ShadowProfile CreateShadowProfile()
        {
            var profile = AssetDatabase.LoadAssetAtPath<SandroneM3ShadowProfile>(ShadowProfilePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<SandroneM3ShadowProfile>();
                AssetDatabase.CreateAsset(profile, ShadowProfilePath);
            }
            profile.EditorSet(CastShadowStrength, CastShadowLow, CastShadowHigh);
            EditorUtility.SetDirty(profile);
            return profile;
        }

        private static Material[] CreateCharacterMaterials(Shader shader)
        {
            var map = AssetDatabase.LoadAssetAtPath<SandroneMaterialMap>(SandroneM0Bootstrap.MaterialMapPath);
            if (map == null || map.Entries.Count != 31)
            {
                throw new InvalidOperationException("M0 material map is missing or incomplete.");
            }

            var output = new Material[31];
            foreach (var entry in map.Entries.OrderBy(item => item.sourceIndex))
            {
                var sourcePath = SandroneM2Bootstrap.MaterialPath(entry.sourceIndex, entry.materialAssetPath);
                var source = AssetDatabase.LoadAssetAtPath<Material>(sourcePath);
                if (source == null)
                {
                    throw new FileNotFoundException($"M2 material missing: {sourcePath}");
                }

                var path = MaterialPath(entry.sourceIndex, entry.materialAssetPath);
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material == null)
                {
                    material = new Material(shader);
                    AssetDatabase.CreateAsset(material, path);
                }
                material.shader = shader;
                CopyM2MaterialProperties(source, material);
                material.name = $"M3_{entry.sourceIndex:00}_{Path.GetFileNameWithoutExtension(entry.materialAssetPath)}";
                material.SetFloat("_CastShadowStrength", CastShadowStrength);
                material.SetFloat("_CastShadowLow", CastShadowLow);
                material.SetFloat("_CastShadowHigh", CastShadowHigh);
                material.SetFloat("_M3DebugMode", (float)SandroneM3DebugMode.FinalToon);
                material.SetFloat("_ShadowCull", (float)CullMode.Back);
                material.SetOverrideTag("RenderType", source.GetTag("RenderType", false, "Opaque"));
                material.renderQueue = source.renderQueue;
                material.SetShaderPassEnabled("ShadowCaster",source.GetFloat("_ZWrite")>0.5f);
                EditorUtility.SetDirty(material);
                output[entry.sourceIndex] = material;
            }
            return output;
        }

        private static void CopyM2MaterialProperties(Material source, Material target)
        {
            target.SetTexture("_BaseMap", source.GetTexture("_BaseMap"));
            target.SetTextureScale("_BaseMap", source.GetTextureScale("_BaseMap"));
            target.SetTextureOffset("_BaseMap", source.GetTextureOffset("_BaseMap"));
            target.SetColor("_BaseColor", source.GetColor("_BaseColor"));
            target.SetTexture("_RampMap", source.GetTexture("_RampMap"));
            string[] floats = { "_RampRow", "_RampRowCount", "_Threshold", "_BandSoftness", "_BandAA",
                "_LayerWeight", "_Cutoff", "_AlphaClip", "_SrcBlend", "_DstBlend", "_ZWrite", "_Cull" };
            foreach (var property in floats) target.SetFloat(property, source.GetFloat(property));
        }

        private static Material CreateReceiverMaterial(Shader shader)
        {
            var material = LoadOrCreateMaterial(ReceiverMaterialPath, shader);
            material.SetColor("_BaseColor", new Color(0.34f, 0.33f, 0.34f, 1f));
            material.SetColor("_ShadowTint", new Color(0.38f, 0.42f, 0.56f, 1f));
            material.SetFloat("_ReceiverDebug", 0f);
            material.renderQueue = (int)RenderQueue.Geometry;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Material CreateBlockerMaterial(Shader shader, Texture ramp)
        {
            var material = LoadOrCreateMaterial(BlockerMaterialPath, shader);
            ConfigureOpaqueProbeMaterial(material, ramp, Texture2D.whiteTexture);
            material.SetFloat("_M3DebugMode", (float)SandroneM3DebugMode.FinalToon);
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Material CreateAlphaProbeMaterial(Shader shader, Texture ramp, Texture alphaProbe)
        {
            var material = LoadOrCreateMaterial(AlphaProbeMaterialPath, shader);
            ConfigureOpaqueProbeMaterial(material, ramp, alphaProbe);
            material.SetFloat("_AlphaClip", 1f);
            material.SetFloat("_Cutoff", AlphaCutoff);
            // A single-sided foliage/card mesh may face away from the light's
            // shadow camera while remaining intentionally visible from both sides.
            // Keep character shells back-face culled, but make this cutout card
            // explicitly double-sided in the ShadowCaster pass.
            material.SetFloat("_ShadowCull", (float)CullMode.Off);
            material.SetFloat("_M3DebugMode", (float)SandroneM3DebugMode.Silhouette);
            material.SetOverrideTag("RenderType", "TransparentCutout");
            material.renderQueue = (int)RenderQueue.AlphaTest;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static void ConfigureOpaqueProbeMaterial(Material material, Texture ramp, Texture baseMap)
        {
            material.SetTexture("_BaseMap", baseMap);
            material.SetColor("_BaseColor", Color.white);
            material.SetTexture("_RampMap", ramp);
            material.SetFloat("_RampRow", (float)SandroneRampFamily.LightCloth);
            material.SetFloat("_RampRowCount", SandroneM2Bootstrap.RampRowCount);
            material.SetFloat("_Threshold", 0.5f);
            material.SetFloat("_BandSoftness", SandroneM2Bootstrap.BandSoftness);
            material.SetFloat("_BandAA", SandroneM2Bootstrap.BandAA);
            material.SetFloat("_CastShadowStrength", CastShadowStrength);
            material.SetFloat("_CastShadowLow", CastShadowLow);
            material.SetFloat("_CastShadowHigh", CastShadowHigh);
            material.SetFloat("_LayerWeight", 1f);
            material.SetFloat("_AlphaClip", 0f);
            material.SetFloat("_Cutoff", AlphaCutoff);
            material.SetFloat("_SrcBlend", (float)BlendMode.One);
            material.SetFloat("_DstBlend", (float)BlendMode.Zero);
            material.SetFloat("_ZWrite", 1f);
            material.SetFloat("_Cull", (float)CullMode.Off);
            material.SetFloat("_ShadowCull", (float)CullMode.Back);
            material.SetOverrideTag("RenderType", "Opaque");
            material.renderQueue = (int)RenderQueue.Geometry;
        }

        private static Material LoadOrCreateMaterial(string path, Shader shader)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }
            material.shader = shader;
            material.name = Path.GetFileNameWithoutExtension(path);
            return material;
        }

        private static void CreateCalibrationScene(Material[] materials, SandroneM3ShadowProfile profile,
            Material receiverMaterial, Material blockerMaterial, Material alphaProbeMaterial)
        {
            var modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(SandroneM0Bootstrap.ModelPath);
            if (modelAsset == null)
            {
                throw new FileNotFoundException($"FBX model missing: {SandroneM0Bootstrap.ModelPath}");
            }

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(modelAsset, scene);
            instance.name = "Sandrone_M3";
            instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            instance.transform.localScale = Vector3.one;
            var renderer = instance.GetComponentsInChildren<SkinnedMeshRenderer>(true).Single();
            renderer.sharedMaterials = materials;
            renderer.shadowCastingMode = ShadowCastingMode.On;
            renderer.receiveShadows = true;

            var m0Controller = instance.AddComponent<SandroneM0Controller>();
            m0Controller.Configure(
                new SandroneM0Controller.LayerBinding { renderer = renderer, materialIndex = 9 },
                new SandroneM0Controller.LayerBinding { renderer = renderer, materialIndex = 30 },
                FindTransform(instance.transform, "KeyB02_M"));

            var lightObject = new GameObject("M3_MainDirectionalLight");
            var mainLight = lightObject.AddComponent<Light>();
            mainLight.type = LightType.Directional;
            mainLight.color = Color.white;
            mainLight.intensity = 1f;
            mainLight.shadows = LightShadows.Soft;
            mainLight.shadowStrength = 0.85f;
            mainLight.renderMode = LightRenderMode.ForcePixel;
            RenderSettings.sun = mainLight;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = Color.black;
            RenderSettings.reflectionIntensity = 0f;

            var controller = instance.AddComponent<SandroneM3Controller>();
            controller.Configure(renderer, instance.transform, FindTransform(instance.transform, "頭"), mainLight, profile);
            controller.SetLightDirectionToSource(DefaultDirectionToLight);
            controller.DebugMode = SandroneM3DebugMode.FinalToon;

            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "M3_ShadowGround";
            ground.transform.position = new Vector3(0f, -0.008f, 0f);
            ground.transform.localScale = new Vector3(0.45f, 1f, 0.45f);
            var groundRenderer = ground.GetComponent<MeshRenderer>();
            groundRenderer.sharedMaterial = receiverMaterial;
            groundRenderer.shadowCastingMode = ShadowCastingMode.Off;
            groundRenderer.receiveShadows = true;

            var blocker = GameObject.CreatePrimitive(PrimitiveType.Cube);
            blocker.name = "M3_ValidationBlocker";
            blocker.transform.position = new Vector3(0.55f, 0.92f, 0f);
            blocker.transform.localScale = new Vector3(0.18f, 1.35f, 0.55f);
            var blockerRenderer = blocker.GetComponent<MeshRenderer>();
            blockerRenderer.sharedMaterial = blockerMaterial;
            blockerRenderer.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
            blockerRenderer.receiveShadows = false;
            blocker.SetActive(false);

            var alphaProbe = GameObject.CreatePrimitive(PrimitiveType.Quad);
            alphaProbe.name = "M3_AlphaClipProbe";
            // Keep the validation caster close to its receiver. A large gap amplifies
            // directional shadow bias/cascade quantisation and tests projection drift
            // instead of the forward/ShadowCaster alpha contract we want to verify.
            alphaProbe.transform.position = new Vector3(0f, 0.12f, 0f);
            // Unity's built-in Quad faces local +Z. -90 degrees maps that normal
            // to world +Y; +90 would face it down and make URP normal bias push
            // the caster toward the receiver, producing triangle-shaped artifacts.
            alphaProbe.transform.rotation = Quaternion.Euler(-90f, 0f, 0f);
            alphaProbe.transform.localScale = new Vector3(1.2f, 1.2f, 1f);
            var alphaRenderer = alphaProbe.GetComponent<MeshRenderer>();
            alphaRenderer.sharedMaterial = alphaProbeMaterial;
            alphaRenderer.shadowCastingMode = ShadowCastingMode.On;
            alphaRenderer.receiveShadows = false;
            alphaProbe.SetActive(false);

            var cameraObject = new GameObject("M3_CalibrationCamera");
            var camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = CalibrationBackground;
            camera.orthographic = true;
            camera.orthographicSize = 0.92f;
            camera.allowHDR = false;
            camera.allowMSAA = true;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 20f;

            CaptureCharacter(camera, controller, blocker, ground, Quaternion.identity, SandroneM3DebugMode.FinalToon,
                DefaultDirectionToLight, false, false, 0.92f, 994, 1654, "ReferenceComparison/M3_FinalToon_Front.png");
            CaptureCharacter(camera, controller, blocker, ground, Quaternion.Euler(0f, -90f, 0f), SandroneM3DebugMode.FinalToon,
                DefaultDirectionToLight, false, false, 0.86f, 662, 1032, "ReferenceComparison/M3_FinalToon_Side.png");

            CaptureCharacter(camera, controller, blocker, ground, Quaternion.identity, SandroneM3DebugMode.CastShadowRaw,
                DefaultDirectionToLight, false, false, 0.92f, 768, 1280, "Debug/M3_CastShadowRaw_Self.png");
            CaptureCharacter(camera, controller, blocker, ground, Quaternion.identity, SandroneM3DebugMode.CastShadowStyled,
                DefaultDirectionToLight, false, false, 0.92f, 768, 1280, "Debug/M3_CastShadowStyled_Self.png");
            CaptureCharacter(camera, controller, blocker, ground, Quaternion.identity, SandroneM3DebugMode.FormBand,
                DefaultDirectionToLight, false, false, 0.92f, 768, 1280, "Debug/M3_FormBand.png");
            CaptureCharacter(camera, controller, blocker, ground, Quaternion.identity, SandroneM3DebugMode.FinalLitMask,
                DefaultDirectionToLight, false, false, 0.92f, 768, 1280, "Debug/M3_FinalLitMask.png");
            CaptureCharacter(camera, controller, blocker, ground, Quaternion.identity, SandroneM3DebugMode.RampSample,
                DefaultDirectionToLight, false, false, 0.92f, 768, 1280, "Debug/M3_RampSample.png");
            CaptureCharacter(camera, controller, blocker, ground, Quaternion.identity, SandroneM3DebugMode.CascadeIndex,
                DefaultDirectionToLight, false, false, 0.92f, 768, 1280, "Debug/M3_CascadeIndex.png");
            CaptureCharacter(camera, controller, blocker, ground, Quaternion.identity, SandroneM3DebugMode.Silhouette,
                DefaultDirectionToLight, false, false, 0.92f, 768, 1280, "Debug/M3_Silhouette.png");
            CaptureShadowDistance(camera,controller,blocker,ground,0f,"Near");
            CaptureShadowDistance(camera,controller,blocker,ground,-12f,"Mid");
            CaptureShadowDistance(camera,controller,blocker,ground,-32f,"Far");
            CaptureCharacter(camera, controller, blocker, ground, Quaternion.identity, SandroneM3DebugMode.CastShadowRaw,
                Vector3.right, false, false, 0.92f, 768, 1280, "Debug/M3_Receiver_NoBlocker.png");
            CaptureCharacter(camera, controller, blocker, ground, Quaternion.identity, SandroneM3DebugMode.CastShadowRaw,
                Vector3.right, true, false, 0.92f, 768, 1280, "Debug/M3_Receiver_WithBlocker.png");

            CaptureGround(camera, controller, ground, blocker, renderer, true,
                "Debug/M3_Ground_NoCaster.png");
            CaptureGround(camera, controller, ground, blocker, renderer, false,
                "Debug/M3_Ground_WithCaster.png");
            CaptureAlphaProbe(camera, controller, instance, ground, alphaProbe, alphaRenderer, receiverMaterial);

            instance.SetActive(true);
            alphaProbe.SetActive(false);
            blocker.SetActive(false);
            ground.SetActive(true);
            renderer.shadowCastingMode = ShadowCastingMode.On;
            mainLight.shadows = LightShadows.Soft;
            instance.transform.rotation = Quaternion.identity;
            controller.DebugMode = SandroneM3DebugMode.FinalToon;
            controller.SetLightDirectionToSource(DefaultDirectionToLight);
            controller.Apply(true);
            ConfigureCamera(camera, new Vector3(2.6f, 2.25f, 3.8f), new Vector3(0f, 0.65f, 0f), 1.35f, CalibrationBackground);

            EditorUtility.SetDirty(controller);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        }

        private static void CaptureCharacter(Camera camera, SandroneM3Controller controller, GameObject blocker,
            GameObject ground, Quaternion rotation, SandroneM3DebugMode debugMode, Vector3 lightDirection,
            bool blockerActive, bool groundActive, float orthographicSize, int width, int height, string relativePath)
        {
            controller.CharacterRoot.gameObject.SetActive(true);
            controller.CharacterRoot.rotation = rotation;
            controller.DebugMode = debugMode;
            controller.SetLightDirectionToSource(lightDirection);
            controller.MainLight.shadows = LightShadows.Soft;
            controller.Apply(true);
            blocker.SetActive(blockerActive);
            ground.SetActive(groundActive);
            ConfigureCamera(camera, new Vector3(0f, 0.82f, 4f), new Vector3(0f, 0.82f, 0f), orthographicSize,
                debugMode == SandroneM3DebugMode.FinalToon ? CalibrationBackground : Color.black);
            Capture(camera, width, height, relativePath);
        }

        private static void CaptureGround(Camera camera, SandroneM3Controller controller, GameObject ground,
            GameObject blocker, SkinnedMeshRenderer characterRenderer, bool disableCaster, string relativePath)
        {
            controller.CharacterRoot.gameObject.SetActive(true);
            controller.CharacterRoot.rotation = Quaternion.identity;
            controller.DebugMode = SandroneM3DebugMode.FinalToon;
            controller.SetLightDirectionToSource(new Vector3(0.65f, 1f, 0.45f));
            controller.MainLight.shadows = LightShadows.Soft;
            controller.Apply(true);
            blocker.SetActive(false);
            ground.SetActive(true);
            characterRenderer.shadowCastingMode = disableCaster ? ShadowCastingMode.Off : ShadowCastingMode.On;
            ConfigureCamera(camera, new Vector3(2.6f, 2.25f, 3.8f), new Vector3(0f, 0.55f, 0f), 1.35f, CalibrationBackground);
            Capture(camera, 960, 960, relativePath);
            characterRenderer.shadowCastingMode = ShadowCastingMode.On;
        }

        private static void CaptureShadowDistance(Camera camera,SandroneM3Controller controller,GameObject blocker,
            GameObject ground,float characterZ,string label)
        {
            var originalPosition=controller.CharacterRoot.position;
            var originalFar=camera.farClipPlane;
            try
            {
                controller.CharacterRoot.position=new Vector3(originalPosition.x,originalPosition.y,characterZ);
                camera.farClipPlane=50f;
                CaptureCharacter(camera,controller,blocker,ground,Quaternion.identity,SandroneM3DebugMode.CastShadowRaw,
                    DefaultDirectionToLight,false,false,0.92f,768,1280,$"Debug/M3_CastShadowRaw_{label}.png");
                controller.DebugMode=SandroneM3DebugMode.CascadeIndex;
                controller.Apply(true);
                Capture(camera,768,1280,$"Debug/M3_CascadeIndex_{label}.png");
            }
            finally
            {
                controller.CharacterRoot.position=originalPosition;
                camera.farClipPlane=originalFar;
            }
        }

        private static void CaptureAlphaProbe(Camera camera, SandroneM3Controller controller, GameObject character,
            GameObject ground, GameObject alphaProbe, MeshRenderer alphaRenderer, Material receiverMaterial)
        {
            var probePosition=alphaProbe.transform.position; var probeRotation=alphaProbe.transform.rotation;
            var groundPosition=ground.transform.position; var groundRotation=ground.transform.rotation;
            try
            {
                // Keep the production cascade configuration. Switching to one
                // cascade hid the original rectangular truncation and was not a
                // valid proof of the Forward/ShadowCaster alpha contract.
                character.SetActive(false);
                ground.SetActive(false);
                alphaProbe.SetActive(true);
                // Keep caster and receiver at almost identical camera depth so a
                // four-cascade shadow atlas cannot cull one from the other's tile.
                // The old top-down layout crossed cascade culling volumes and
                // produced the reported rectangular/diagonal truncation.
                alphaProbe.transform.SetPositionAndRotation(new Vector3(0f,0.82f,0f),Quaternion.identity);
                alphaRenderer.shadowCastingMode = ShadowCastingMode.On;
                controller.SetLightDirectionToSource(Vector3.forward);
                controller.MainLight.shadows = LightShadows.Hard;
                ConfigureCamera(camera,new Vector3(0f,0.82f,3f),new Vector3(0f,0.82f,0f),0.85f,Color.black);
                Capture(camera, 768, 768, "Debug/M3_AlphaClip_Visual.png");

                ground.SetActive(true);
                ground.transform.SetPositionAndRotation(new Vector3(0f,0.82f,-0.12f),Quaternion.Euler(90f,0f,0f));
                receiverMaterial.SetFloat("_ReceiverDebug", 1f);
                alphaRenderer.shadowCastingMode = ShadowCastingMode.Off;
                Capture(camera, 768, 768, "Debug/M3_AlphaClip_NoCaster.png");
                alphaRenderer.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
                Capture(camera, 768, 768, "Debug/M3_AlphaClip_Shadow.png");
            }
            finally
            {
                receiverMaterial.SetFloat("_ReceiverDebug", 0f);
                alphaRenderer.shadowCastingMode = ShadowCastingMode.On;
                alphaProbe.transform.SetPositionAndRotation(probePosition,probeRotation);
                ground.transform.SetPositionAndRotation(groundPosition,groundRotation);
            }
        }

        private static void ConfigureCamera(Camera camera, Vector3 position, Vector3 target, float orthographicSize,
            Color background, Vector3? up = null)
        {
            camera.transform.position = position;
            camera.transform.rotation = Quaternion.LookRotation((target - position).normalized, up ?? Vector3.up);
            camera.orthographicSize = orthographicSize;
            camera.backgroundColor = background;
        }

        private static void Capture(Camera camera, int width, int height, string relativePath)
        {
            var root = Path.GetFullPath(Path.Combine(Application.dataPath, "../TestArtifacts/M3"));
            var outputPath = Path.Combine(root, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? root);
            var previous = RenderTexture.active;
            var texture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
            {
                antiAliasing = 1,
                name = $"Sandrone_{Path.GetFileNameWithoutExtension(relativePath)}_RT"
            };
            var image = new Texture2D(width, height, TextureFormat.RGB24, false, false);
            try
            {
                camera.targetTexture = texture;
                texture.Create();
                camera.Render();
                RenderTexture.active = texture;
                image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                image.Apply(false, false);
                File.WriteAllBytes(outputPath, image.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = null;
                RenderTexture.active = previous;
                UnityEngine.Object.DestroyImmediate(image);
                texture.Release();
                UnityEngine.Object.DestroyImmediate(texture);
            }
            Debug.Log($"[Sandrone M3] Captured {outputPath}");
        }

        private static void CreateAlphaProbeTextureIfMissing()
        {
            var path = AssetPathToAbsolute(AlphaProbeTexturePath);
            if (File.Exists(path))
            {
                return;
            }
            var texture = new Texture2D(128, 128, TextureFormat.RGBA32, false, false);
            try
            {
                for (var y = 0; y < 128; y++)
                {
                    for (var x = 0; x < 128; x++)
                    {
                        var dx = (x + 0.5f - 64f) / 64f;
                        var dy = (y + 0.5f - 64f) / 64f;
                        var outer = dx * dx + dy * dy < 0.78f * 0.78f;
                        var inner = dx * dx + dy * dy < 0.28f * 0.28f;
                        var notch = Mathf.Abs(dx) < 0.10f && dy > 0.15f;
                        var alpha = outer && !inner && !notch ? 1f : 0f;
                        texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                    }
                }
                texture.Apply(false, false);
                File.WriteAllBytes(path, texture.EncodeToPNG());
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }

        private static void ConfigureAlphaProbeImporter()
        {
            var importer = AssetImporter.GetAtPath(AlphaProbeTexturePath) as TextureImporter;
            if (importer == null)
            {
                return;
            }
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = true;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = false;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Point;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
        }

        private static Transform FindTransform(Transform root, string name)
        {
            return root.GetComponentsInChildren<Transform>(true).FirstOrDefault(item => item.name == name);
        }

        private static string AssetPathToAbsolute(string assetPath)
        {
            return Path.GetFullPath(Path.Combine(Application.dataPath, "..", assetPath));
        }

        private static void EnsureFolder(string assetPath)
        {
            Directory.CreateDirectory(AssetPathToAbsolute(assetPath));
        }
    }
}
