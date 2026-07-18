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
    public static class SandroneM5Bootstrap
    {
        public const string ShaderPath = "Assets/Sandrone/Shaders/SandroneFaceSDFM5.shader";
        public const string ProfilePath = "Assets/Sandrone/Configs/SandroneFaceProfile_M5.asset";
        public const string FaceMapPath = "Assets/Sandrone/Textures/Face/Sandrone_Face_SDF.png";
        public const string MaterialDirectory = "Assets/Sandrone/Materials/M5";
        public const string ScenePath = "Assets/Sandrone/Tests/Scenes/ToonCalibration_M5.unity";
        private static readonly Color Background = new(0.153f, 0.149f, 0.149f, 1f);

        [MenuItem("Sandrone/M5/Build Face SDF")]
        public static void Build()
        {
            Debug.Log("[Sandrone M5] Build started; M4 report is a hard gate.");
            SandroneM4Validator.ValidateAndWriteReport();
            EnsureFolder(MaterialDirectory); EnsureFolder("Assets/Sandrone/Textures/Face");
            EnsureFolder("Assets/Sandrone/Configs"); EnsureFolder("Assets/Sandrone/Tests/Scenes");
            CreateFaceMapIfMissing(); AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            ConfigureFaceMapImporter();
            AssetDatabase.ImportAsset(ShaderPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
            var faceMap = AssetDatabase.LoadAssetAtPath<Texture2D>(FaceMapPath);
            if (shader == null || !shader.isSupported || faceMap == null)
                throw new InvalidOperationException("M5 shader or FaceMap is missing/unsupported.");
            var profile = CreateProfile(faceMap);
            var materials = CreateMaterials(shader, profile);
            CreateScene(materials, profile);
            AssetDatabase.SaveAssets(); AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            SandroneM5SpecialAudit.Run();
            SandroneM5Validator.ValidateAndWriteReport();
            Debug.Log("[Sandrone M5] Build and validation completed.");
        }

        public static string MaterialPath(int index, string sourcePath) =>
            $"{MaterialDirectory}/M5_{index:00}_{Path.GetFileNameWithoutExtension(sourcePath)}.mat";

        private static SandroneM5FaceProfile CreateProfile(Texture2D faceMap)
        {
            var profile = AssetDatabase.LoadAssetAtPath<SandroneM5FaceProfile>(ProfilePath);
            if (profile == null) { profile = ScriptableObject.CreateInstance<SandroneM5FaceProfile>(); AssetDatabase.CreateAsset(profile, ProfilePath); }
            profile.EditorSet(faceMap, new[] { 0, 1 }, 0.02f, 1f, 0.10f); EditorUtility.SetDirty(profile); return profile;
        }

        private static Material[] CreateMaterials(Shader shader, SandroneM5FaceProfile profile)
        {
            var map = AssetDatabase.LoadAssetAtPath<SandroneMaterialMap>(SandroneM0Bootstrap.MaterialMapPath);
            if (map == null || map.Entries.Count != 31) throw new InvalidOperationException("M0 material map is missing/incomplete.");
            var result = new Material[31];
            foreach (var entry in map.Entries.OrderBy(e => e.sourceIndex))
            {
                var sourcePath = SandroneM4Bootstrap.MaterialPath(entry.sourceIndex, entry.materialAssetPath);
                var source = AssetDatabase.LoadAssetAtPath<Material>(sourcePath);
                if (source == null) throw new FileNotFoundException($"M4 material missing: {sourcePath}");
                // M5 is a face-only phase. Reuse the exact M4 asset for every non-face
                // slot so skirt/cloth render state and pass selection cannot drift.
                if (!profile.FaceMaterialIndices.Contains(entry.sourceIndex))
                {
                    result[entry.sourceIndex] = source;
                    continue;
                }
                var path = MaterialPath(entry.sourceIndex, entry.materialAssetPath);
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material == null) { material = new Material(shader); AssetDatabase.CreateAsset(material, path); }
                material.shader = shader; CopyM4Properties(source, material);
                material.name = Path.GetFileNameWithoutExtension(path); material.SetTexture("_FaceMap", profile.FaceMap);
                material.SetFloat("_UseFaceSDF", profile.FaceMaterialIndices.Contains(entry.sourceIndex) ? 1f : 0f);
                material.SetFloat("_FaceSoftness", profile.Softness); material.SetFloat("_FaceAA", profile.DerivativeAA);
                material.SetFloat("_FaceMirrorBlendWidth", profile.MirrorBlendWidth);
                material.SetFloat("_FaceSDFWeight", 1f); material.SetFloat("_M5DebugMode", 0f);
                material.SetFloat("_M4DebugMode", 0f);
                if (profile.FaceMaterialIndices.Contains(entry.sourceIndex)) material.EnableKeyword("_SANDRONE_FACE");
                else material.DisableKeyword("_SANDRONE_FACE");
                material.SetOverrideTag("RenderType", source.GetTag("RenderType", false, "Opaque")); material.renderQueue = source.renderQueue;
                material.SetFloat("_M5AuditSlotId", entry.sourceIndex);
                material.SetShaderPassEnabled("ShadowCaster", source.GetShaderPassEnabled("ShadowCaster"));
                EditorUtility.SetDirty(material); result[entry.sourceIndex] = material;
            }
            return result;
        }

        private static void CopyM4Properties(Material source, Material target)
        {
            foreach (var property in new[] { "_BaseMap", "_RampMap", "_ControlMap", "_MatCapMap" })
                target.SetTexture(property, source.GetTexture(property));
            target.SetTextureScale("_BaseMap", source.GetTextureScale("_BaseMap"));
            target.SetTextureOffset("_BaseMap", source.GetTextureOffset("_BaseMap")); target.SetColor("_BaseColor", source.GetColor("_BaseColor"));
            foreach (var property in new[] { "_RampRow", "_RampRowCount", "_Threshold", "_BandSoftness", "_BandAA",
                "_CastShadowStrength", "_CastShadowLow", "_CastShadowHigh", "_ResponseType", "_FeatureGroup",
                "_SpecIntensity", "_SpecPower", "_MatCapIntensity", "_MetalMaskFallback", "_AOIntensity", "_OverlayColorBoost",
                "_LayerWeight", "_Cutoff", "_AlphaClip", "_SrcBlend", "_DstBlend", "_ZWrite", "_Cull", "_ShadowCull", "_M4FeatureWeight" })
                target.SetFloat(property, source.GetFloat(property));
        }

        private static void CreateScene(Material[] materials, SandroneM5FaceProfile profile)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(SandroneM0Bootstrap.ModelPath);
            var shadowProfile = AssetDatabase.LoadAssetAtPath<SandroneM3ShadowProfile>(SandroneM3Bootstrap.ShadowProfilePath);
            var groundMaterial = AssetDatabase.LoadAssetAtPath<Material>(SandroneM3Bootstrap.ReceiverMaterialPath);
            if (model == null || shadowProfile == null || groundMaterial == null) throw new InvalidOperationException("M0/M3 dependencies missing.");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(model, scene); instance.name = "Sandrone_M5";
            instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var renderer = instance.GetComponentsInChildren<SkinnedMeshRenderer>(true).Single(); renderer.sharedMaterials = materials;
            renderer.shadowCastingMode = ShadowCastingMode.On; renderer.receiveShadows = true;
            var m0 = instance.AddComponent<SandroneM0Controller>();
            m0.Configure(new SandroneM0Controller.LayerBinding { renderer = renderer, materialIndex = 9 },
                new SandroneM0Controller.LayerBinding { renderer = renderer, materialIndex = 30 }, Find(instance.transform, "KeyB02_M"));
            var lightObject = new GameObject("M5_MainDirectionalLight"); var light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional; light.color = Color.white; light.intensity = 1f; light.shadows = LightShadows.Soft;
            light.shadowStrength = 0.85f; light.renderMode = LightRenderMode.ForcePixel;
            RenderSettings.sun = light; RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = Color.black; RenderSettings.reflectionIntensity = 0f;
            var head = Find(instance.transform, "頭"); if (head == null) throw new InvalidOperationException("Head bone 頭 missing.");
            var controller = instance.AddComponent<SandroneM5Controller>(); controller.Configure(renderer, instance.transform, head, light, shadowProfile);
            controller.SetLightDirectionToSource(SandroneM3Bootstrap.DefaultDirectionToLight);
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane); ground.name = "M5_ShadowGround";
            ground.transform.position = new Vector3(0, -0.008f, 0); ground.transform.localScale = new Vector3(0.45f, 1, 0.45f);
            var gr = ground.GetComponent<MeshRenderer>(); gr.sharedMaterial = groundMaterial; gr.shadowCastingMode = ShadowCastingMode.Off; gr.receiveShadows = true;
            var cameraObject = new GameObject("M5_CalibrationCamera"); var camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Background; camera.orthographic = true;
            camera.allowHDR = false; camera.allowMSAA = true; camera.nearClipPlane = 0.1f; camera.farClipPlane = 20f;

            EditorUtility.SetDirty(controller); EditorSceneManager.MarkSceneDirty(scene); AssetDatabase.SaveAssets(); EditorSceneManager.SaveScene(scene, ScenePath);
            scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            controller = UnityEngine.Object.FindFirstObjectByType<SandroneM5Controller>(); camera = UnityEngine.Object.FindFirstObjectByType<Camera>();
            if (controller == null || camera == null) throw new InvalidOperationException("Reloaded M5 scene lost controller/camera.");
            renderer = (SkinnedMeshRenderer)controller.TargetRenderer; instance = controller.CharacterRoot.gameObject; head = controller.Head;

            Capture(camera, controller, Quaternion.identity, SandroneM5DebugMode.FinalToon, true, 0.92f, 994, 1654, "ReferenceComparison/M5_FinalToon_Front.png", false);
            Capture(camera, controller, Quaternion.Euler(0, -35, 0), SandroneM5DebugMode.FinalToon, true, 0.30f, 768, 768, "ReferenceComparison/M5_Face_ThreeQuarter.png", true);
            foreach (SandroneM5DebugMode mode in Enum.GetValues(typeof(SandroneM5DebugMode)))
                Capture(camera, controller, Quaternion.identity, mode, true, mode >= SandroneM5DebugMode.FaceSDF ? 0.30f : 0.92f,
                    512, 768, $"Debug/M5_{mode}.png", mode >= SandroneM5DebugMode.FaceSDF);
            CaptureFaceToggle(camera, controller, true, "AB/M5_FaceSDF_On.png");
            CaptureFaceToggle(camera, controller, false, "AB/M5_FaceSDF_Off.png");
            CaptureFaceMask(camera, controller, "Masks/M5_FaceSlots01_Mask.png", true, 0.30f, 768, 768);
            CaptureFaceMask(camera, controller, "Masks/M5_FaceSlots01_FullMask.png", false, 0.92f, 994, 1654);
            CaptureLight(camera, controller, new Vector3(1f, 0.12f, 0.08f), "Debug/M5_FaceLitMask_RightLight.png");
            CaptureLight(camera, controller, new Vector3(-1f, 0.12f, 0.08f), "Debug/M5_FaceLitMask_LeftLight.png");
            CaptureHeadRotation(camera, controller);
            CaptureSyntheticFaceMap(camera, controller, 0.2f, "Debug/M5_FaceMapSyntheticLow.png");
            CaptureSyntheticFaceMap(camera, controller, 0.8f, "Debug/M5_FaceMapSyntheticHigh.png");
            CapturePipelines(camera, controller);

            instance.transform.rotation = Quaternion.identity; controller.DebugMode = SandroneM5DebugMode.FinalToon; controller.FaceSdfEnabled = true;
            controller.SetLightDirectionToSource(SandroneM3Bootstrap.DefaultDirectionToLight); controller.Apply(true);
            ConfigureCamera(camera, new Vector3(2.6f, 2.25f, 3.8f), new Vector3(0, 0.65f, 0), 1.35f, Background);
            EditorUtility.SetDirty(controller); EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        }

        private static void Capture(Camera camera, SandroneM5Controller c, Quaternion rotation, SandroneM5DebugMode mode,
            bool faceEnabled, float size, int width, int height, string relative, bool headFraming)
        {
            var isolateFace = mode >= SandroneM5DebugMode.FaceSDF;
            var original = c.TargetRenderer.sharedMaterials; Material hidden = null;
            try
            {
                if (isolateFace)
                {
                    var isolation = AssetDatabase.LoadAssetAtPath<Shader>(SandroneM4Bootstrap.IsolationShaderPath);
                    hidden = new Material(isolation); hidden.SetColor("_Color", Color.clear);
                    var materials = (Material[])original.Clone();
                    for (var i = 2; i < materials.Length; i++) materials[i] = hidden;
                    c.TargetRenderer.sharedMaterials = materials;
                }
                c.CharacterRoot.rotation = rotation; c.DebugMode = mode; c.FaceSdfEnabled = faceEnabled;
                c.SetLightDirectionToSource(SandroneM3Bootstrap.DefaultDirectionToLight); c.Apply(true);
                var target = headFraming ? c.Head.position + new Vector3(0, -0.01f, 0) : new Vector3(0, 0.82f, 0);
                ConfigureCamera(camera, target + new Vector3(0, 0, 4), target, size, mode == SandroneM5DebugMode.FinalToon ? Background : Color.black);
                CaptureCamera(camera, relative, width, height);
            }
            finally
            {
                if (isolateFace) c.TargetRenderer.sharedMaterials = original;
                if (hidden != null) UnityEngine.Object.DestroyImmediate(hidden);
                c.Apply(true);
            }
        }

        private static void CaptureLight(Camera camera, SandroneM5Controller c, Vector3 direction, string relative)
        {
            c.CharacterRoot.rotation = Quaternion.identity; c.FaceSdfEnabled = true; c.DebugMode = SandroneM5DebugMode.FaceLitMask;
            c.SetLightDirectionToSource(direction); c.Apply(true); var target = c.Head.position;
            ConfigureCamera(camera, target + new Vector3(0, 0, 4), target, 0.30f, Color.black); CaptureCamera(camera, relative, 768, 768);
        }

        private static void CaptureFaceToggle(Camera camera, SandroneM5Controller c, bool enabled, string relative)
        {
            c.CharacterRoot.rotation = Quaternion.identity; c.FaceSdfEnabled = enabled; c.DebugMode = SandroneM5DebugMode.FinalToon;
            c.SetLightDirectionToSource(new Vector3(0.92f, 0.22f, 0.34f)); c.Apply(true); var target = c.Head.position;
            ConfigureCamera(camera, target + new Vector3(0, 0, 4), target, 0.30f, Background); CaptureCamera(camera, relative, 768, 768);
        }

        private static void CaptureHeadRotation(Camera camera, SandroneM5Controller c)
        {
            var original = c.Head.localRotation; c.CharacterRoot.rotation = Quaternion.identity; c.FaceSdfEnabled = true;
            try
            {
                c.DebugMode = SandroneM5DebugMode.HeadLightAxes; c.SetLightDirectionToSource(new Vector3(0.65f, 0.2f, 0.65f)); c.Apply(true);
                var target = c.Head.position; ConfigureCamera(camera, target + new Vector3(0, 0, 4), target, 0.30f, Color.black);
                CaptureCamera(camera, "Debug/M5_HeadAxes_BeforeHeadRotate.png", 768, 768);
                c.Head.localRotation = original * Quaternion.Euler(0, 25, 0); c.Apply(true);
                CaptureCamera(camera, "Debug/M5_HeadAxes_AfterHeadRotate.png", 768, 768);
            }
            finally { c.Head.localRotation = original; c.Apply(true); }
        }

        private static void CaptureSyntheticFaceMap(Camera camera, SandroneM5Controller c, float value, string relative)
        {
            var original = c.TargetRenderer.sharedMaterials; var materials = (Material[])original.Clone();
            var texture = new Texture2D(2, 2, TextureFormat.R8, false, true);
            texture.SetPixels(Enumerable.Repeat(new Color(value, value, value, 1), 4).ToArray()); texture.Apply();
            materials[0] = new Material(original[0]); materials[1] = new Material(original[1]);
            materials[0].SetTexture("_FaceMap", texture); materials[1].SetTexture("_FaceMap", texture); c.TargetRenderer.sharedMaterials = materials;
            try
            {
                c.CharacterRoot.rotation = Quaternion.identity; c.FaceSdfEnabled = true; c.DebugMode = SandroneM5DebugMode.FaceLitMask;
                c.SetLightDirectionToSource(new Vector3(1f, 0.1f, 0.05f)); c.Apply(true); var target = c.Head.position;
                ConfigureCamera(camera, target + new Vector3(0, 0, 4), target, 0.30f, Color.black); CaptureCamera(camera, relative, 768, 768);
            }
            finally
            {
                c.TargetRenderer.sharedMaterials = original; UnityEngine.Object.DestroyImmediate(materials[0]); UnityEngine.Object.DestroyImmediate(materials[1]);
                UnityEngine.Object.DestroyImmediate(texture); c.Apply(true);
            }
        }

        private static void CaptureFaceMask(Camera camera, SandroneM5Controller c, string relative, bool headFraming, float size, int width, int height)
        {
            var isolation = AssetDatabase.LoadAssetAtPath<Shader>(SandroneM4Bootstrap.IsolationShaderPath);
            var original = c.TargetRenderer.sharedMaterials; var transparent = new Material(isolation); transparent.SetColor("_Color", Color.clear);
            var materials = Enumerable.Repeat(transparent, original.Length).ToArray(); materials[0] = new Material(original[0]); materials[1] = new Material(original[1]);
            c.TargetRenderer.sharedMaterials = materials;
            try
            {
                c.CharacterRoot.rotation = Quaternion.identity; c.DebugMode = SandroneM5DebugMode.Silhouette; c.FaceSdfEnabled = true; c.Apply(true);
                var target = headFraming ? c.Head.position : new Vector3(0, 0.82f, 0);
                ConfigureCamera(camera, target + new Vector3(0, 0, 4), target, size, Color.black); CaptureCamera(camera, relative, width, height);
            }
            finally
            {
                c.TargetRenderer.sharedMaterials = original; UnityEngine.Object.DestroyImmediate(materials[0]); UnityEngine.Object.DestroyImmediate(materials[1]);
                UnityEngine.Object.DestroyImmediate(transparent); c.DebugMode = SandroneM5DebugMode.FinalToon; c.Apply(true);
            }
        }

        private static void CapturePipelines(Camera camera, SandroneM5Controller c)
        {
            var pc = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset");
            var mobile = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/Settings/Mobile_RPAsset.asset");
            var oldDefault = GraphicsSettings.defaultRenderPipeline; var oldQuality = QualitySettings.renderPipeline;
            try
            {
                foreach (var pair in new[] { (pc, "Pipeline/M5_PC_ForwardPlus.png"), (mobile, "Pipeline/M5_Mobile_Forward.png") })
                {
                    if (pair.Item1 == null) throw new InvalidOperationException("URP asset missing.");
                    QualitySettings.renderPipeline = pair.Item1; GraphicsSettings.defaultRenderPipeline = pair.Item1;
                    Capture(camera, c, Quaternion.identity, SandroneM5DebugMode.FinalToon, true, 0.92f, 768, 1280, pair.Item2, false);
                }
            }
            finally { QualitySettings.renderPipeline = oldQuality; GraphicsSettings.defaultRenderPipeline = oldDefault; }
        }

        private static void CaptureCamera(Camera camera, string relative, int width, int height)
        {
            var output = Artifact(relative); Directory.CreateDirectory(Path.GetDirectoryName(output)!); var previous = RenderTexture.active;
            var rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 1 };
            var image = new Texture2D(width, height, TextureFormat.RGB24, false, false);
            try { camera.targetTexture = rt; rt.Create(); camera.Render(); RenderTexture.active = rt; image.ReadPixels(new Rect(0, 0, width, height), 0, 0); image.Apply(); File.WriteAllBytes(output, image.EncodeToPNG()); }
            finally { camera.targetTexture = null; RenderTexture.active = previous; UnityEngine.Object.DestroyImmediate(image); rt.Release(); UnityEngine.Object.DestroyImmediate(rt); }
        }

        private static void CreateFaceMapIfMissing()
        {
            var path = Absolute(FaceMapPath); if (File.Exists(path)) return;
            const int size = 2048; var texture = new Texture2D(size, size, TextureFormat.R8, false, true); var pixels = new Color[size * size];
            try
            {
                for (var y = 0; y < size; y++) for (var x = 0; x < size; x++)
                {
                    var u = (x + 0.5f) / size; var v = (y + 0.5f) / size;
                    var horizontal = (u - 0.5f) * 0.88f;
                    var cheek = 0.07f * (1f - Mathf.Clamp01(Mathf.Abs(v - 0.55f) / 0.42f));
                    var nose = 0.08f * Mathf.Exp(-Mathf.Pow((u - 0.5f) / 0.09f, 2f) - Mathf.Pow((v - 0.56f) / 0.20f, 2f));
                    var jaw = -0.07f * Mathf.Clamp01((0.35f - v) / 0.25f);
                    var sdf = Mathf.Clamp01(0.5f + horizontal + cheek + nose + jaw);
                    pixels[y * size + x] = new Color(sdf, sdf, sdf, 1f);
                }
                texture.SetPixels(pixels); texture.Apply(false, false); File.WriteAllBytes(path, texture.EncodeToPNG());
            }
            finally { UnityEngine.Object.DestroyImmediate(texture); }
        }

        private static void ConfigureFaceMapImporter()
        {
            if (AssetImporter.GetAtPath(FaceMapPath) is not TextureImporter importer) return;
            importer.textureType = TextureImporterType.Default; importer.sRGBTexture = false; importer.alphaSource = TextureImporterAlphaSource.None;
            importer.mipmapEnabled = false; importer.wrapMode = TextureWrapMode.Clamp; importer.filterMode = FilterMode.Bilinear;
            importer.textureCompression = TextureImporterCompression.Uncompressed; importer.SaveAndReimport();
        }

        private static void ConfigureCamera(Camera camera, Vector3 position, Vector3 target, float size, Color background)
        { camera.transform.position = position; camera.transform.rotation = Quaternion.LookRotation((target - position).normalized, Vector3.up); camera.orthographicSize = size; camera.backgroundColor = background; }
        private static Transform Find(Transform root, string name) => root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == name);
        private static string Artifact(string relative) => Path.Combine(ProjectRoot(), "TestArtifacts/M5", relative);
        private static string ProjectRoot() => Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        private static string Absolute(string assetPath) => Path.GetFullPath(Path.Combine(Application.dataPath, "..", assetPath));
        private static void EnsureFolder(string assetPath) => Directory.CreateDirectory(Absolute(assetPath));
    }
}
