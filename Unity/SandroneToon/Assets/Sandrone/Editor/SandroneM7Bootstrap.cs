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
    public static class SandroneM7Bootstrap
    {
        public const string ShaderPath = "Assets/Sandrone/Shaders/SandroneOutlineM7.shader";
        public const string ProfilePath = "Assets/Sandrone/Configs/SandroneOutlineProfile_M7.asset";
        public const string MeshPath = "Assets/Sandrone/Models/Generated/Sandrone_Outline_M7.asset";
        public const string MaterialDirectory = "Assets/Sandrone/Materials/M7Outline";
        public const string ScenePath = "Assets/Sandrone/Tests/Scenes/ToonCalibration_M7.unity";
        public static readonly int[] EligibleSlots = { 0, 1, 12, 13, 14, 15, 16, 17, 18, 20, 22, 23, 24, 26 };
        public static readonly int[] ExcludedTransparentSlots = { 7, 8, 9, 10, 19, 28, 29, 30 };
        public static readonly int[] ExcludedDetailOrInnerSlots = { 2, 3, 4, 5, 6, 11, 21, 25, 27 };
        private static readonly Color Background = new(0.153f, 0.149f, 0.149f, 1f);

        [Serializable]
        public struct NormalAudit
        {
            public int vertexCount, sourceColorCount, coincidentGroupCount, discontinuousGroupCount, maxGroupSize;
            public float discontinuousGroupRatio, maxAngularDifferenceDegrees;
        }

        [MenuItem("Sandrone/M7/Build Outline")]
        public static void Build()
        {
            Debug.Log("[Sandrone M7] Build started; full M6 build/regression is a hard gate.");
            SandroneM6Bootstrap.Build();
            EnsureFolder(MaterialDirectory);
            EnsureFolder("Assets/Sandrone/Models/Generated");
            EnsureFolder("Assets/Sandrone/Configs");
            AssetDatabase.ImportAsset(ShaderPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
            if (shader == null || !shader.isSupported) throw new InvalidOperationException("M7 outline shader is missing or unsupported.");
            var profile = CreateProfile();
            var sourceRenderer = UnityEngine.Object.FindFirstObjectByType<SandroneM6Controller>()?.TargetRenderer as SkinnedMeshRenderer;
            if (sourceRenderer == null || sourceRenderer.sharedMesh == null) throw new InvalidOperationException("M6 source skinned mesh is missing.");
            CreateOutlineMesh(sourceRenderer.sharedMesh);
            var materials = CreateMaterials(shader, profile);
            CreateScene(profile, materials);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            SandroneM7Validator.ValidateAndWriteReport();
            Debug.Log("[Sandrone M7] Build and validation completed.");
        }

        public static string MaterialPath(int index) => $"{MaterialDirectory}/M7_{index:00}_Outline.mat";
        public static string DisabledMaterialPath => $"{MaterialDirectory}/M7_Disabled.mat";

        private static SandroneM7OutlineProfile CreateProfile()
        {
            var profile = AssetDatabase.LoadAssetAtPath<SandroneM7OutlineProfile>(ProfilePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<SandroneM7OutlineProfile>();
                AssetDatabase.CreateAsset(profile, ProfilePath);
            }
            profile.EditorSet(1f, new[]
            {
                Slot(0, .72f, new Color(.22f,.12f,.15f,1)), Slot(1, .72f, new Color(.22f,.12f,.15f,1)),
                Slot(12, 1.00f, new Color(.15f,.12f,.14f,1)), Slot(13, 1.08f, new Color(.15f,.12f,.14f,1)),
                Slot(14, .90f, new Color(.24f,.20f,.16f,1)), Slot(15, 1.12f, new Color(.08f,.08f,.13f,1)),
                Slot(16, 1.10f, new Color(.10f,.09f,.13f,1)), Slot(17, .76f, new Color(.24f,.13f,.15f,1)),
                Slot(18, .82f, new Color(.24f,.13f,.15f,1)), Slot(20, 1.18f, new Color(.22f,.04f,.07f,1)),
                Slot(22, 1.28f, new Color(.22f,.04f,.07f,1)), Slot(23, 1.02f, new Color(.19f,.08f,.08f,1)),
                Slot(24, .92f, new Color(.25f,.19f,.09f,1)), Slot(26, 1.30f, new Color(.22f,.04f,.07f,1))
            });
            EditorUtility.SetDirty(profile);
            return profile;
        }

        private static SandroneM7OutlineSlot Slot(int index, float width, Color color) =>
            new() { materialIndex = index, widthPixels = width, color = color };

        private static Material[] CreateMaterials(Shader shader, SandroneM7OutlineProfile profile)
        {
            if (AssetDatabase.LoadAssetAtPath<Material>(DisabledMaterialPath) != null) AssetDatabase.DeleteAsset(DisabledMaterialPath);
            var ordered = profile.Slots.OrderBy(x => Array.IndexOf(EligibleSlots, x.materialIndex)).ToArray();
            var result = new Material[ordered.Length];
            for (var i = 0; i < ordered.Length; i++)
            {
                var slot = ordered[i];
                var path = MaterialPath(slot.materialIndex);
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material == null)
                {
                    material = new Material(shader);
                    AssetDatabase.CreateAsset(material, path);
                }
                ConfigureMaterial(material, slot.materialIndex, slot.widthPixels, slot.color);
                result[i] = material;
            }
            return result;
        }

        private static void ConfigureMaterial(Material material, int slot, float width, Color color)
        {
            material.shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
            material.SetColor("_OutlineColor", color);
            material.SetFloat("_OutlinePixels", width);
            material.SetFloat("_OutlineWidthWeight", 1f);
            material.SetFloat("_OutlineMasterWeight", 1f);
            material.SetFloat("_M7DebugMode", 0f);
            material.SetFloat("_M7SlotId", slot);
            material.renderQueue = 2010;
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
        }

        public static NormalAudit AnalyzeSourceNormals(Mesh mesh)
        {
            var groups = BuildGroups(mesh);
            var normals = mesh.normals;
            var audit = new NormalAudit { vertexCount = mesh.vertexCount, sourceColorCount = mesh.colors32.Length };
            foreach (var indices in groups.Values)
            {
                if (indices.Count < 2) continue;
                audit.coincidentGroupCount++;
                audit.maxGroupSize = Mathf.Max(audit.maxGroupSize, indices.Count);
                var maxAngle = 0f;
                for (var i = 0; i < indices.Count; i++)
                    for (var j = i + 1; j < indices.Count; j++)
                    {
                        var angle = Vector3.Angle(normals[indices[i]], normals[indices[j]]);
                        // Opposite normals on an intentional double-sided shell share the
                        // same expansion axis and are not a UV/hard-edge seam.
                        maxAngle = Mathf.Max(maxAngle, Mathf.Min(angle, 180f - angle));
                    }
                audit.maxAngularDifferenceDegrees = Mathf.Max(audit.maxAngularDifferenceDegrees, maxAngle);
                if (maxAngle > 15f) audit.discontinuousGroupCount++;
            }
            audit.discontinuousGroupRatio = audit.coincidentGroupCount == 0 ? 0f :
                (float)audit.discontinuousGroupCount / audit.coincidentGroupCount;
            return audit;
        }

        public static Vector3[] GenerateSmoothedNormals(Mesh mesh)
        {
            var source = mesh.normals;
            var result = (Vector3[])source.Clone();
            foreach (var indices in BuildGroups(mesh).Values)
            {
                if (indices.Count < 2) continue;
                var reference = source[indices[0]].normalized;
                var sum = Vector3.zero;
                foreach (var index in indices)
                {
                    var normal = source[index].normalized;
                    if (Vector3.Dot(reference, normal) < 0f) normal = -normal;
                    sum += normal;
                }
                if (sum.sqrMagnitude < 1e-8f) continue;
                var average = sum.normalized;
                foreach (var index in indices) result[index] = Vector3.Dot(source[index], average) < 0f ? -average : average;
            }
            return result;
        }

        private static Dictionary<string, List<int>> BuildGroups(Mesh mesh)
        {
            var vertices = mesh.vertices;
            var weights = mesh.boneWeights;
            var groups = new Dictionary<string, List<int>>(vertices.Length);
            for (var i = 0; i < vertices.Length; i++)
            {
                var p = vertices[i];
                var bone = weights.Length == vertices.Length ? weights[i] : default;
                var key = $"{Mathf.RoundToInt(p.x * 100000f)},{Mathf.RoundToInt(p.y * 100000f)},{Mathf.RoundToInt(p.z * 100000f)}|" +
                    $"{bone.boneIndex0},{bone.boneIndex1},{bone.boneIndex2},{bone.boneIndex3}";
                if (!groups.TryGetValue(key, out var list)) groups[key] = list = new List<int>(2);
                list.Add(i);
            }
            return groups;
        }

        private static void CreateOutlineMesh(Mesh source)
        {
            var generated = UnityEngine.Object.Instantiate(source);
            generated.name = "Sandrone_Outline_M7";
            generated.normals = GenerateSmoothedNormals(source);
            if (generated.colors32.Length != generated.vertexCount)
                generated.colors32 = Enumerable.Repeat(new Color32(255, 255, 255, 255), generated.vertexCount).ToArray();
            generated.subMeshCount = EligibleSlots.Length;
            for (var outlineIndex = 0; outlineIndex < EligibleSlots.Length; outlineIndex++)
            {
                var sourceSlot = EligibleSlots[outlineIndex];
                generated.SetIndices(source.GetIndices(sourceSlot), source.GetTopology(sourceSlot), outlineIndex, false);
            }
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath);
            if (existing == null) AssetDatabase.CreateAsset(generated, MeshPath);
            else
            {
                EditorUtility.CopySerialized(generated, existing);
                UnityEngine.Object.DestroyImmediate(generated);
                EditorUtility.SetDirty(existing);
            }
        }

        private static void CreateScene(SandroneM7OutlineProfile profile, Material[] outlineMaterials)
        {
            var scene = EditorSceneManager.OpenScene(SandroneM6Bootstrap.ScenePath, OpenSceneMode.Single);
            if (!EditorSceneManager.SaveScene(scene, ScenePath)) throw new IOException("Could not create M7 scene from M6 baseline.");
            scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var m6 = UnityEngine.Object.FindFirstObjectByType<SandroneM6Controller>();
            var source = m6?.TargetRenderer as SkinnedMeshRenderer;
            if (source == null) throw new InvalidOperationException("M7 scene has no M6 source renderer.");
            source.gameObject.name = source.gameObject.name.Replace("M6", "M7");
            var outlineObject = new GameObject("Sandrone_M7_Outline");
            outlineObject.transform.SetParent(source.transform.parent, false);
            outlineObject.transform.localPosition = source.transform.localPosition;
            outlineObject.transform.localRotation = source.transform.localRotation;
            outlineObject.transform.localScale = source.transform.localScale;
            var outline = outlineObject.AddComponent<SkinnedMeshRenderer>();
            outline.sharedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath);
            outline.sharedMaterials = outlineMaterials;
            outline.bones = source.bones;
            outline.rootBone = source.rootBone;
            outline.localBounds = source.localBounds;
            outline.quality = source.quality;
            outline.updateWhenOffscreen = source.updateWhenOffscreen;
            outline.skinnedMotionVectors = source.skinnedMotionVectors;
            outline.shadowCastingMode = ShadowCastingMode.Off;
            outline.receiveShadows = false;
            outline.lightProbeUsage = LightProbeUsage.Off;
            outline.reflectionProbeUsage = ReflectionProbeUsage.Off;
            var controller = source.transform.root.gameObject.AddComponent<SandroneM7OutlineController>();
            controller.Configure(source, outline, profile);
            var camera = UnityEngine.Object.FindFirstObjectByType<Camera>();
            if (camera == null) throw new InvalidOperationException("M7 scene camera missing.");
            camera.name = "M7_CalibrationCamera";
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            controller = UnityEngine.Object.FindFirstObjectByType<SandroneM7OutlineController>();
            camera = UnityEngine.Object.FindFirstObjectByType<Camera>();
            if (controller == null || camera == null) throw new InvalidOperationException("Reloaded M7 scene is incomplete.");
            CaptureEvidence(camera, controller);
            controller.OutlineEnabled = true;
            controller.MasterWidth = 1f;
            controller.DebugMode = SandroneM7DebugMode.FinalColor;
            ConfigureCamera(camera, new Vector3(2.6f, 2.25f, 3.8f), new Vector3(0, .65f, 0), 1.35f, true);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        }

        private static void CaptureEvidence(Camera camera, SandroneM7OutlineController controller)
        {
            var root = controller.SourceRenderer.transform.root;
            root.rotation = Quaternion.identity;
            Capture(camera, controller, true, 1f, SandroneM7DebugMode.FinalColor, .92f, 994, 1654, "ReferenceComparison/M7_FinalToon_Front.png", new Vector3(0,.82f,0));
            root.rotation = Quaternion.Euler(0, -35, 0);
            Capture(camera, controller, true, 1f, SandroneM7DebugMode.FinalColor, .42f, 768, 768, "ReferenceComparison/M7_Head_ThreeQuarter.png", Find(root, "頭").position);
            root.rotation = Quaternion.Euler(0, -82, 0);
            Capture(camera, controller, true, 1f, SandroneM7DebugMode.FinalColor, .42f, 768, 768, "ReferenceComparison/M7_Head_Side.png", Find(root, "頭").position);
            root.rotation = Quaternion.identity;
            Capture(camera, controller, false, 1f, SandroneM7DebugMode.FinalColor, .92f, 994, 1654, "AB/M7_OutlineOff.png", new Vector3(0,.82f,0));
            Capture(camera, controller, true, 1f, SandroneM7DebugMode.FinalColor, .92f, 994, 1654, "AB/M7_OutlineOn.png", new Vector3(0,.82f,0));
            foreach (SandroneM7DebugMode mode in Enum.GetValues(typeof(SandroneM7DebugMode)))
                Capture(camera, controller, true, 1f, mode, .42f, 768, 768, $"Debug/M7_{mode}.png", Find(root, "頭").position, Color.black);
            CaptureNormalComparison(camera, controller);
            foreach (var item in new[] { ("Near", .52f), ("Mid", .92f), ("Far", 1.65f) })
            {
                Capture(camera, controller, false, 1f, SandroneM7DebugMode.FinalColor, item.Item2, 768, 768, $"Scale/M7_{item.Item1}_Off.png", new Vector3(0,.82f,0));
                Capture(camera, controller, true, 1f, SandroneM7DebugMode.FinalColor, item.Item2, 768, 768, $"Scale/M7_{item.Item1}_On.png", new Vector3(0,.82f,0));
            }
            CapturePipelines(camera, controller);
        }

        private static void CaptureNormalComparison(Camera camera, SandroneM7OutlineController controller)
        {
            var outline = controller.OutlineRenderer;
            var smooth = outline.sharedMesh;
            var original = UnityEngine.Object.Instantiate(smooth);
            original.normals = controller.SourceRenderer.sharedMesh.normals;
            try
            {
                outline.sharedMesh = original;
                Capture(camera, controller, true, 1f, SandroneM7DebugMode.FinalColor, .42f, 768, 768, "AB/M7_OriginalNormals.png", Find(controller.SourceRenderer.transform.root, "頭").position);
                outline.sharedMesh = smooth;
                Capture(camera, controller, true, 1f, SandroneM7DebugMode.FinalColor, .42f, 768, 768, "AB/M7_SmoothedNormals.png", Find(controller.SourceRenderer.transform.root, "頭").position);
            }
            finally
            {
                outline.sharedMesh = smooth;
                UnityEngine.Object.DestroyImmediate(original);
            }
        }

        private static void CapturePipelines(Camera camera, SandroneM7OutlineController controller)
        {
            var pc = AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset");
            var mobile = AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>("Assets/Settings/Mobile_RPAsset.asset");
            var oldDefault = GraphicsSettings.defaultRenderPipeline;
            var oldQuality = QualitySettings.renderPipeline;
            try
            {
                GraphicsSettings.defaultRenderPipeline = pc; QualitySettings.renderPipeline = pc;
                Capture(camera, controller, true, 1f, SandroneM7DebugMode.FinalColor, .42f, 768, 768, "Pipeline/M7_PC_ForwardPlus.png", Find(controller.SourceRenderer.transform.root, "頭").position);
                GraphicsSettings.defaultRenderPipeline = mobile; QualitySettings.renderPipeline = mobile;
                Capture(camera, controller, true, 1f, SandroneM7DebugMode.FinalColor, .42f, 768, 768, "Pipeline/M7_Mobile_Forward.png", Find(controller.SourceRenderer.transform.root, "頭").position);
            }
            finally { GraphicsSettings.defaultRenderPipeline = oldDefault; QualitySettings.renderPipeline = oldQuality; }
        }

        private static void Capture(Camera camera, SandroneM7OutlineController controller, bool enabled, float width,
            SandroneM7DebugMode debug, float size, int imageWidth, int imageHeight, string relative, Vector3 target, Color? background = null)
        {
            controller.OutlineEnabled = enabled;
            controller.MasterWidth = width;
            controller.DebugMode = debug;
            ConfigureCamera(camera, target + new Vector3(0,0,4), target, size, true);
            camera.backgroundColor = background ?? Background;
            CaptureCamera(camera, relative, imageWidth, imageHeight);
        }

        private static void ConfigureCamera(Camera camera, Vector3 position, Vector3 target, float size, bool orthographic)
        {
            camera.transform.position = position;
            camera.transform.rotation = Quaternion.LookRotation(target - position, Vector3.up);
            camera.orthographic = orthographic;
            camera.orthographicSize = size;
            camera.allowHDR = false;
            camera.allowMSAA = true;
        }

        private static void CaptureCamera(Camera camera, string relative, int width, int height)
        {
            var path = Path.GetFullPath(Path.Combine(Application.dataPath, "../TestArtifacts/M7", relative));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var oldActive = RenderTexture.active;
            var rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var image = new Texture2D(width, height, TextureFormat.RGB24, false);
            try
            {
                camera.targetTexture = rt; rt.Create(); camera.Render(); RenderTexture.active = rt;
                image.ReadPixels(new Rect(0,0,width,height),0,0); image.Apply(); File.WriteAllBytes(path, image.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = null; RenderTexture.active = oldActive; UnityEngine.Object.DestroyImmediate(image);
                rt.Release(); UnityEngine.Object.DestroyImmediate(rt);
            }
        }

        private static Transform Find(Transform root, string name)
        {
            foreach (var item in root.GetComponentsInChildren<Transform>(true)) if (item.name == name) return item;
            return null;
        }

        private static void EnsureFolder(string path)
        {
            var parts = path.Split('/'); var current = parts[0];
            for (var i = 1; i < parts.Length; i++)
            {
                var next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }
    }
}
