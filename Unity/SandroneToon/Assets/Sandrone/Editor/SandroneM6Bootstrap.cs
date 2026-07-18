using System;
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
    public static class SandroneM6Bootstrap
    {
        public const string ShaderPath = "Assets/Sandrone/Shaders/SandroneHairEyeM6.shader";
        public const string ProfilePath = "Assets/Sandrone/Configs/SandroneHairEyeProfile_M6.asset";
        public const string MaterialDirectory = "Assets/Sandrone/Materials/M6";
        public const string ScenePath = "Assets/Sandrone/Tests/Scenes/ToonCalibration_M6.unity";
        public static readonly int[] TargetSlots = { 2, 3, 6, 7, 8, 9, 10, 11, 12, 13, 29 };
        public static readonly int[] EyeSlots = { 2, 3, 6, 7, 8, 9, 10, 11 };
        public static readonly int[] HairSlots = { 12, 13, 29 };
        private static readonly Color Background = new(0.153f, 0.149f, 0.149f, 1f);

        [MenuItem("Sandrone/M6/Build Hair and Eyes")]
        public static void Build()
        {
            Debug.Log("[Sandrone M6] Build started; fresh M5 validation is a hard gate.");
            SandroneM5Validator.ValidateAndWriteReport();
            EnsureFolder(MaterialDirectory);
            EnsureFolder("Assets/Sandrone/Configs");
            EnsureFolder("Assets/Sandrone/Tests/Scenes");
            AssetDatabase.ImportAsset(ShaderPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
            if (shader == null || !shader.isSupported) throw new InvalidOperationException("M6 shader is missing or unsupported.");
            var profile = CreateProfile();
            var materials = CreateMaterials(shader, profile);
            CreateScene(materials, profile);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            SandroneM0M4RegressionAudit.RunFullRegression();
            // The regression audit intentionally opens/rebuilds earlier phases and therefore
            // changes Build Settings. Restore the completed phase as the only enabled baseline.
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            SandroneM6Validator.ValidateAndWriteReport();
            Debug.Log("[Sandrone M6] Build and validation completed.");
        }

        public static string MaterialPath(int index, string sourcePath) =>
            $"{MaterialDirectory}/M6_{index:00}_{Path.GetFileNameWithoutExtension(sourcePath)}.mat";

        public static Material BaselineMaterial(int index, string sourcePath)
        {
            var path = index <= 1
                ? SandroneM5Bootstrap.MaterialPath(index, sourcePath)
                : SandroneM4Bootstrap.MaterialPath(index, sourcePath);
            return AssetDatabase.LoadAssetAtPath<Material>(path);
        }

        private static SandroneM6HairEyeProfile CreateProfile()
        {
            var profile = AssetDatabase.LoadAssetAtPath<SandroneM6HairEyeProfile>(ProfilePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<SandroneM6HairEyeProfile>();
                AssetDatabase.CreateAsset(profile, ProfilePath);
            }
            profile.EditorSet(new[]
            {
                Slot(2, SandroneM6Role.BrowLash, 0f, 0f),
                Slot(3, SandroneM6Role.None, 0f, 0f),
                // The source eye textures already contain most of their authored value and
                // chroma structure. Keep flattening deliberately weak so M6 does not wash
                // the saturated blue iris toward white; M8 emission remains separate.
                Slot(6, SandroneM6Role.EyeStencilWriter, 0.18f, 0f),
                Slot(7, SandroneM6Role.EyeLayer, 0.20f, 0f),
                Slot(8, SandroneM6Role.EyeLayer, 0.20f, 0f),
                Slot(9, SandroneM6Role.EyeLayer, 0.25f, 0f),
                Slot(10, SandroneM6Role.EyeLayer, 0.30f, 0f),
                Slot(11, SandroneM6Role.EyeLayer, 0.22f, 0f),
                Slot(12, SandroneM6Role.HairBase, 0f, 1f),
                Slot(13, SandroneM6Role.HairBase, 0f, 0.85f),
                Slot(29, SandroneM6Role.HairOverlay, 0f, 0f)
            }, 0.16f, 28f, 0.52f, 0.06f, new Color(0.82f, 0.76f, 0.68f, 1f));
            EditorUtility.SetDirty(profile);
            return profile;
        }

        private static SandroneM6Slot Slot(int index, SandroneM6Role role, float eye, float hair) =>
            new() { materialIndex = index, role = role, eyeFlatLighting = eye, hairSpecularWeight = hair };

        private static Material[] CreateMaterials(Shader shader, SandroneM6HairEyeProfile profile)
        {
            var map = AssetDatabase.LoadAssetAtPath<SandroneMaterialMap>(SandroneM0Bootstrap.MaterialMapPath);
            if (map == null || map.Entries.Count != 31) throw new InvalidOperationException("M0 material map is missing/incomplete.");
            var result = new Material[31];
            foreach (var entry in map.Entries.OrderBy(x => x.sourceIndex))
            {
                var baseline = BaselineMaterial(entry.sourceIndex, entry.materialAssetPath);
                if (baseline == null) throw new FileNotFoundException($"M5/M4 baseline material missing for slot {entry.sourceIndex}.");
                if (!profile.TryGet(entry.sourceIndex, out var slot))
                {
                    result[entry.sourceIndex] = baseline;
                    continue;
                }

                var path = MaterialPath(entry.sourceIndex, entry.materialAssetPath);
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material == null)
                {
                    material = new Material(shader);
                    AssetDatabase.CreateAsset(material, path);
                }
                material.shader = shader;
                CopyM4Properties(baseline, material);
                material.name = Path.GetFileNameWithoutExtension(path);
                material.SetFloat("_M6Role", (float)slot.role);
                material.SetFloat("_EyeFlatLighting", slot.eyeFlatLighting);
                material.SetFloat("_HairSpecIntensity", profile.HairSpecularIntensity * slot.hairSpecularWeight);
                material.SetFloat("_HairSpecPower", profile.HairSpecularPower);
                material.SetFloat("_HairSpecThreshold", profile.HairSpecularThreshold);
                material.SetFloat("_HairSpecSoftness", profile.HairSpecularSoftness);
                material.SetColor("_HairSpecColor", profile.HairSpecularColor);
                material.SetFloat("_M6HairSpecWeight", 1f);
                material.SetFloat("_M6EyeLayerWeight", 1f);
                material.SetFloat("_M6DebugMode", 0f);
                material.SetFloat("_M6AuditSlotId", entry.sourceIndex);
                ConfigureStencil(material, entry.sourceIndex);
                material.SetOverrideTag("RenderType", baseline.GetTag("RenderType", false, "Opaque"));
                material.renderQueue = baseline.renderQueue;
                material.SetShaderPassEnabled("ShadowCaster", baseline.GetShaderPassEnabled("ShadowCaster"));
                EditorUtility.SetDirty(material);
                result[entry.sourceIndex] = material;
            }
            return result;
        }

        private static void ConfigureStencil(Material material, int index)
        {
            material.SetFloat("_M6StencilRef", index >= 6 && index <= 11 ? 1f : 0f);
            if (index == 6)
            {
                material.SetFloat("_M6StencilReadMask", 0f);
                material.SetFloat("_M6StencilWriteMask", 1f);
                material.SetFloat("_M6StencilComp", (float)CompareFunction.Always);
                material.SetFloat("_M6StencilPass", (float)StencilOp.Replace);
            }
            else if (index >= 7 && index <= 11)
            {
                material.SetFloat("_M6StencilReadMask", 1f);
                material.SetFloat("_M6StencilWriteMask", 0f);
                material.SetFloat("_M6StencilComp", (float)CompareFunction.Equal);
                material.SetFloat("_M6StencilPass", (float)StencilOp.Keep);
            }
            else
            {
                material.SetFloat("_M6StencilReadMask", 0f);
                material.SetFloat("_M6StencilWriteMask", 0f);
                material.SetFloat("_M6StencilComp", (float)CompareFunction.Always);
                material.SetFloat("_M6StencilPass", (float)StencilOp.Keep);
            }
        }

        private static void CopyM4Properties(Material source, Material target)
        {
            foreach (var property in new[] { "_BaseMap", "_RampMap", "_ControlMap", "_MatCapMap" })
                target.SetTexture(property, source.GetTexture(property));
            target.SetTextureScale("_BaseMap", source.GetTextureScale("_BaseMap"));
            target.SetTextureOffset("_BaseMap", source.GetTextureOffset("_BaseMap"));
            target.SetColor("_BaseColor", source.GetColor("_BaseColor"));
            foreach (var property in new[]
            {
                "_RampRow", "_RampRowCount", "_Threshold", "_BandSoftness", "_BandAA",
                "_CastShadowStrength", "_CastShadowLow", "_CastShadowHigh", "_ResponseType", "_FeatureGroup",
                "_SpecIntensity", "_SpecPower", "_MatCapIntensity", "_MetalMaskFallback", "_AOIntensity",
                "_OverlayColorBoost", "_LayerWeight", "_Cutoff", "_AlphaClip", "_SrcBlend", "_DstBlend",
                "_ZWrite", "_Cull", "_ShadowCull", "_M4FeatureWeight"
            }) target.SetFloat(property, source.GetFloat(property));
            target.SetFloat("_M4DebugMode", 0f);
        }

        private static void CreateScene(Material[] materials, SandroneM6HairEyeProfile profile)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(SandroneM0Bootstrap.ModelPath);
            var shadowProfile = AssetDatabase.LoadAssetAtPath<SandroneM3ShadowProfile>(SandroneM3Bootstrap.ShadowProfilePath);
            var groundMaterial = AssetDatabase.LoadAssetAtPath<Material>(SandroneM3Bootstrap.ReceiverMaterialPath);
            if (model == null || shadowProfile == null || groundMaterial == null) throw new InvalidOperationException("M0/M3 dependencies missing.");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(model, scene);
            instance.name = "Sandrone_M6";
            instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var renderer = instance.GetComponentsInChildren<SkinnedMeshRenderer>(true).Single();
            renderer.sharedMaterials = materials;
            renderer.shadowCastingMode = ShadowCastingMode.On;
            renderer.receiveShadows = true;

            var m0 = instance.AddComponent<SandroneM0Controller>();
            m0.Configure(new SandroneM0Controller.LayerBinding { renderer = renderer, materialIndex = 9 },
                new SandroneM0Controller.LayerBinding { renderer = renderer, materialIndex = 30 }, Find(instance.transform, "KeyB02_M"));
            var lightObject = new GameObject("M6_MainDirectionalLight");
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = Color.white;
            light.intensity = 1f;
            light.shadows = LightShadows.Soft;
            light.shadowStrength = 0.85f;
            light.renderMode = LightRenderMode.ForcePixel;
            RenderSettings.sun = light;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = Color.black;
            RenderSettings.reflectionIntensity = 0f;

            var head = Find(instance.transform, "頭");
            if (head == null) throw new InvalidOperationException("Head bone 頭 missing.");
            var faceController = instance.AddComponent<SandroneM5Controller>();
            faceController.Configure(renderer, instance.transform, head, light, shadowProfile);
            faceController.SetLightDirectionToSource(SandroneM3Bootstrap.DefaultDirectionToLight);
            var controller = instance.AddComponent<SandroneM6Controller>();
            controller.Configure(renderer, instance.transform, head, light, shadowProfile, profile);
            controller.SetLightDirectionToSource(SandroneM3Bootstrap.DefaultDirectionToLight);

            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "M6_ShadowGround";
            UnityEngine.Object.DestroyImmediate(ground.GetComponent<Collider>());
            ground.transform.position = new Vector3(0, -0.008f, 0);
            ground.transform.localScale = new Vector3(0.45f, 1, 0.45f);
            var groundRenderer = ground.GetComponent<MeshRenderer>();
            groundRenderer.sharedMaterial = groundMaterial;
            groundRenderer.shadowCastingMode = ShadowCastingMode.Off;
            groundRenderer.receiveShadows = true;

            var cameraObject = new GameObject("M6_CalibrationCamera");
            var camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Background;
            camera.orthographic = true;
            camera.allowHDR = false;
            camera.allowMSAA = true;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 20f;

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            controller = UnityEngine.Object.FindFirstObjectByType<SandroneM6Controller>();
            faceController = UnityEngine.Object.FindFirstObjectByType<SandroneM5Controller>();
            m0 = UnityEngine.Object.FindFirstObjectByType<SandroneM0Controller>();
            camera = UnityEngine.Object.FindFirstObjectByType<Camera>();
            if (controller == null || faceController == null || m0 == null || camera == null)
                throw new InvalidOperationException("Reloaded M6 scene is incomplete.");

            Capture(camera, controller, Quaternion.identity, SandroneM6DebugMode.FinalToon, 0.92f, 994, 1654,
                "ReferenceComparison/M6_FinalToon_Front.png", new Vector3(0, 0.82f, 0));
            Capture(camera, controller, Quaternion.Euler(0, -35, 0), SandroneM6DebugMode.FinalToon, 0.42f, 768, 768,
                "ReferenceComparison/M6_Head_ThreeQuarter.png", controller.Head.position);
            Capture(camera, controller, Quaternion.Euler(0, -82, 0), SandroneM6DebugMode.FinalToon, 0.42f, 768, 768,
                "ReferenceComparison/M6_Head_Side.png", controller.Head.position);
            foreach (SandroneM6DebugMode mode in Enum.GetValues(typeof(SandroneM6DebugMode)))
                CaptureDebug(camera, controller, mode);
            CaptureToggle(camera, controller, true, true, "AB/M6_AllOn.png");
            CaptureToggle(camera, controller, false, true, "AB/M6_HairSpecOff.png");
            CaptureToggle(camera, controller, true, false, "AB/M6_EyeLayersOff.png");
            CaptureEyeAL(camera, controller, m0, 0f, "AB/M6_EyeAL_0.png");
            CaptureEyeAL(camera, controller, m0, 1f, "AB/M6_EyeAL_1.png");
            CaptureBangShadow(camera, controller, true, "AB/M6_BangShadow_On.png");
            CaptureBangShadow(camera, controller, false, "AB/M6_BangShadow_Off.png");
            CapturePipelines(camera, controller);

            controller.CharacterRoot.rotation = Quaternion.identity;
            controller.DebugMode = SandroneM6DebugMode.FinalToon;
            controller.HairSpecularEnabled = true;
            controller.EyeLayersEnabled = true;
            m0.eyeALWeight = 0f;
            m0.blushWeight = 0f;
            m0.Apply();
            faceController.DebugMode = SandroneM5DebugMode.FinalToon;
            faceController.FaceSdfEnabled = true;
            faceController.Apply(true);
            controller.SetLightDirectionToSource(SandroneM3Bootstrap.DefaultDirectionToLight);
            controller.Apply(true);
            ConfigureCamera(camera, new Vector3(2.6f, 2.25f, 3.8f), new Vector3(0, 0.65f, 0), 1.35f, Background);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        }

        private static void CaptureDebug(Camera camera, SandroneM6Controller controller, SandroneM6DebugMode mode)
        {
            var original = controller.TargetRenderer.sharedMaterials;
            var isolation = AssetDatabase.LoadAssetAtPath<Shader>(SandroneM4Bootstrap.IsolationShaderPath);
            var hidden = new Material(isolation);
            hidden.SetColor("_Color", Color.clear);
            var isolated = (Material[])original.Clone();
            for (var i = 0; i < isolated.Length; i++) if (!TargetSlots.Contains(i)) isolated[i] = hidden;
            controller.TargetRenderer.sharedMaterials = isolated;
            try
            {
                Capture(camera, controller, Quaternion.identity, mode, 0.42f, 768, 768,
                    $"Debug/M6_{mode}.png", controller.Head.position, mode == SandroneM6DebugMode.FinalToon ? Background : Color.black);
            }
            finally
            {
                controller.TargetRenderer.sharedMaterials = original;
                UnityEngine.Object.DestroyImmediate(hidden);
                controller.Apply(true);
            }
        }

        private static void CaptureToggle(Camera camera, SandroneM6Controller controller, bool hair, bool eyes, string relative)
        {
            controller.HairSpecularEnabled = hair;
            controller.EyeLayersEnabled = eyes;
            Capture(camera, controller, Quaternion.identity, SandroneM6DebugMode.FinalToon, 0.42f, 768, 768, relative, controller.Head.position);
        }

        private static void CaptureEyeAL(Camera camera, SandroneM6Controller controller, SandroneM0Controller m0, float weight, string relative)
        {
            m0.eyeALWeight = weight;
            m0.Apply();
            controller.EyeLayersEnabled = true;
            Capture(camera, controller, Quaternion.identity, SandroneM6DebugMode.FinalToon, 0.30f, 768, 768, relative, controller.Head.position);
        }

        private static void CaptureBangShadow(Camera camera, SandroneM6Controller controller, bool enabled, string relative)
        {
            var original = controller.TargetRenderer.sharedMaterials;
            var materials = (Material[])original.Clone();
            var frontHair = new Material(original[12]);
            frontHair.SetShaderPassEnabled("ShadowCaster", enabled);
            materials[12] = frontHair;
            controller.TargetRenderer.sharedMaterials = materials;
            try
            {
                Capture(camera, controller, Quaternion.identity, SandroneM6DebugMode.FinalToon, 0.30f, 768, 768, relative, controller.Head.position);
            }
            finally
            {
                controller.TargetRenderer.sharedMaterials = original;
                UnityEngine.Object.DestroyImmediate(frontHair);
                controller.Apply(true);
            }
        }

        private static void CapturePipelines(Camera camera, SandroneM6Controller controller)
        {
            var pc = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset");
            var mobile = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/Settings/Mobile_RPAsset.asset");
            var oldDefault = GraphicsSettings.defaultRenderPipeline;
            var oldQuality = QualitySettings.renderPipeline;
            try
            {
                GraphicsSettings.defaultRenderPipeline = pc;
                QualitySettings.renderPipeline = pc;
                Capture(camera, controller, Quaternion.identity, SandroneM6DebugMode.FinalToon, 0.42f, 768, 768,
                    "Pipeline/M6_PC_ForwardPlus.png", controller.Head.position);
                GraphicsSettings.defaultRenderPipeline = mobile;
                QualitySettings.renderPipeline = mobile;
                Capture(camera, controller, Quaternion.identity, SandroneM6DebugMode.FinalToon, 0.42f, 768, 768,
                    "Pipeline/M6_Mobile_Forward.png", controller.Head.position);
            }
            finally
            {
                GraphicsSettings.defaultRenderPipeline = oldDefault;
                QualitySettings.renderPipeline = oldQuality;
            }
        }

        private static void Capture(Camera camera, SandroneM6Controller controller, Quaternion rotation,
            SandroneM6DebugMode mode, float size, int width, int height, string relative, Vector3 target,
            Color? background = null)
        {
            controller.CharacterRoot.rotation = rotation;
            controller.DebugMode = mode;
            controller.SetLightDirectionToSource(SandroneM3Bootstrap.DefaultDirectionToLight);
            controller.Apply(true);
            ConfigureCamera(camera, target + new Vector3(0, 0, 4), target, size, background ?? Background);
            CaptureCamera(camera, relative, width, height);
        }

        private static void ConfigureCamera(Camera camera, Vector3 position, Vector3 target, float size, Color background)
        {
            camera.transform.position = position;
            camera.transform.rotation = Quaternion.LookRotation(target - position, Vector3.up);
            camera.orthographic = true;
            camera.orthographicSize = size;
            camera.backgroundColor = background;
        }

        private static void CaptureCamera(Camera camera, string relative, int width, int height)
        {
            var path = Path.GetFullPath(Path.Combine(Application.dataPath, "../TestArtifacts/M6", relative));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var previous = RenderTexture.active;
            var rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var image = new Texture2D(width, height, TextureFormat.RGB24, false);
            try
            {
                camera.targetTexture = rt;
                rt.Create();
                camera.Render();
                RenderTexture.active = rt;
                image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                image.Apply();
                File.WriteAllBytes(path, image.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = null;
                RenderTexture.active = previous;
                UnityEngine.Object.DestroyImmediate(image);
                rt.Release();
                UnityEngine.Object.DestroyImmediate(rt);
            }
        }

        private static Transform Find(Transform root, string name)
        {
            foreach (var transform in root.GetComponentsInChildren<Transform>(true)) if (transform.name == name) return transform;
            return null;
        }

        private static void EnsureFolder(string path)
        {
            var parts = path.Split('/');
            var current = parts[0];
            for (var i = 1; i < parts.Length; i++)
            {
                var next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }
    }
}
