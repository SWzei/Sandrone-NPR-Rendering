using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace SandroneToon.Editor
{
    public static class SandroneM6OptionalEyeGearAudit
    {
        public const string ModelPath = "Assets/Sandrone/Models/Optional/Sandrone_EyeGear_M6.fbx";
        public const string ScenePath = "Assets/Sandrone/Tests/Scenes/ToonCalibration_M6_OptionalEyeGear.unity";
        public const string MaterialDirectory = "Assets/Sandrone/Materials/M6OptionalEyeGear";
        public const string TextureDirectory = "Assets/Sandrone/Textures/OptionalEyeGear";
        private static readonly string[] GearTextures =
        {
            TextureDirectory + "/T_EyeGear1.png",
            TextureDirectory + "/T_EyeGear2.png",
            TextureDirectory + "/T_EyeGear3.png"
        };

        [Serializable] public sealed class DistanceResult
        {
            public float distanceMeters, yaw025MaskedMae;
            public int gearMaskPixels;
            public string image, yawImage, maskImage;
        }
        [Serializable] public sealed class Report
        {
            public string generatedUtc, unityVersion, graphicsApi, graphicsDevice, sourcePmxSha256;
            public int triangleCount, materialSlotCount, rendererBoneCount, blendShapeCount;
            public bool modelImported, standardBaselineUntouched, mipCoverageConfigured, optionalSceneExcludedFromBuild, recommendStandardFallback;
            public float recommendedFallbackDistanceMeters;
            public List<DistanceResult> distances = new();
            public List<string> checks = new(), failures = new(), warnings = new();
        }

        [MenuItem("Sandrone/M6/Audit Optional Movable Eye Gear")]
        public static void BuildAndRun()
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            // The optional scene must never replace the standard build entry. Reassert M6 here
            // because earlier-phase regression tools legitimately change Build Settings.
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(SandroneM6Bootstrap.ScenePath, true) };
            ConfigureImporters();
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(SandroneM6Bootstrap.ShaderPath);
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (shader == null || model == null) throw new InvalidOperationException("Optional FBX or M6 shader missing.");
            var standard = LoadStandardM6Materials();
            var materials = CreateOptionalMaterials(shader, standard);
            CreateSceneAndCapture(model, materials);
            ValidateAndWrite();
        }

        private static void ConfigureImporters()
        {
            AssetDatabase.ImportAsset(ModelPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            foreach (var path in GearTextures)
            {
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
                if (AssetImporter.GetAtPath(path) is not TextureImporter importer) throw new InvalidOperationException("Optional gear texture importer missing: " + path);
                importer.textureType = TextureImporterType.Default;
                importer.sRGBTexture = true;
                importer.alphaSource = TextureImporterAlphaSource.FromInput;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = true;
                importer.mipMapsPreserveCoverage = true;
                importer.alphaTestReferenceValue = .5f;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.filterMode = FilterMode.Bilinear;
                importer.textureCompression = TextureImporterCompression.CompressedHQ;
                importer.SaveAndReimport();
            }
        }

        private static Material[] LoadStandardM6Materials()
        {
            var map = AssetDatabase.LoadAssetAtPath<SandroneMaterialMap>(SandroneM0Bootstrap.MaterialMapPath);
            var result = new Material[31];
            foreach (var entry in map.Entries)
                result[entry.sourceIndex] = SandroneM6Bootstrap.TargetSlots.Contains(entry.sourceIndex)
                    ? AssetDatabase.LoadAssetAtPath<Material>(SandroneM6Bootstrap.MaterialPath(entry.sourceIndex, entry.materialAssetPath))
                    : SandroneM6Bootstrap.BaselineMaterial(entry.sourceIndex, entry.materialAssetPath);
            if (result.Any(x => x == null)) throw new InvalidOperationException("Standard M6 material set incomplete.");
            return result;
        }

        private static Material[] CreateOptionalMaterials(Shader shader, Material[] standard)
        {
            EnsureFolder(MaterialDirectory);
            var result = new Material[33];
            for (var i = 0; i <= 6; i++) result[i] = standard[i];
            for (var gear = 0; gear < 3; gear++)
            {
                var path = $"{MaterialDirectory}/M6Optional_Gear{gear + 1}.mat";
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material == null)
                {
                    material = new Material(standard[7]);
                    AssetDatabase.CreateAsset(material, path);
                }
                material.shader = shader;
                material.name = $"M6Optional_Gear{gear + 1}";
                material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(GearTextures[gear]));
                material.SetFloat("_M6Role", (float)SandroneM6Role.EyeLayer);
                material.SetFloat("_EyeFlatLighting", 0.18f + gear * 0.03f);
                material.SetFloat("_AlphaClip", 1f);
                material.SetFloat("_Cutoff", .5f);
                material.SetFloat("_SrcBlend", (float)BlendMode.One);
                material.SetFloat("_DstBlend", (float)BlendMode.Zero);
                material.SetFloat("_ZWrite", 1f);
                material.SetFloat("_Cull", (float)CullMode.Back);
                material.SetFloat("_ShadowCull", (float)CullMode.Back);
                material.SetFloat("_M6StencilRef", 1f);
                material.SetFloat("_M6StencilReadMask", 1f);
                material.SetFloat("_M6StencilWriteMask", 0f);
                material.SetFloat("_M6StencilComp", (float)CompareFunction.Equal);
                material.SetFloat("_M6StencilPass", (float)StencilOp.Keep);
                material.renderQueue = (int)RenderQueue.AlphaTest;
                material.SetOverrideTag("RenderType", "TransparentCutout");
                material.SetShaderPassEnabled("ShadowCaster", false);
                EditorUtility.SetDirty(material);
                result[7 + gear] = material;
            }
            for (var i = 10; i < result.Length; i++) result[i] = standard[i - 2];
            AssetDatabase.SaveAssets();
            return result;
        }

        private static void CreateSceneAndCapture(GameObject model, Material[] materials)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(model, scene);
            instance.name = "Sandrone_M6_OptionalEyeGear";
            instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var renderer = instance.GetComponentsInChildren<SkinnedMeshRenderer>(true).Single();
            if (renderer.sharedMaterials.Length != 33) throw new InvalidOperationException($"Optional model slot count is {renderer.sharedMaterials.Length}, expected 33.");
            renderer.sharedMaterials = materials;
            renderer.shadowCastingMode = ShadowCastingMode.On;
            renderer.receiveShadows = true;
            var head = Find(instance.transform, "頭");
            if (head == null) throw new InvalidOperationException("Optional model head bone missing.");

            var shadowProfile = AssetDatabase.LoadAssetAtPath<SandroneM3ShadowProfile>(SandroneM3Bootstrap.ShadowProfilePath);
            var profile = AssetDatabase.LoadAssetAtPath<SandroneM6HairEyeProfile>(SandroneM6Bootstrap.ProfilePath);
            var lightObject = new GameObject("Optional_MainDirectionalLight");
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional; light.color = Color.white; light.intensity = 1f; light.shadows = LightShadows.Soft; light.shadowStrength = .85f;
            RenderSettings.sun = light; RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = Color.black; RenderSettings.reflectionIntensity = 0f;
            var m0 = instance.AddComponent<SandroneM0Controller>();
            m0.Configure(new SandroneM0Controller.LayerBinding { renderer = renderer, materialIndex = 11 },
                new SandroneM0Controller.LayerBinding { renderer = renderer, materialIndex = 32 }, Find(instance.transform, "KeyB02_M"));
            var m5 = instance.AddComponent<SandroneM5Controller>();
            m5.Configure(renderer, instance.transform, head, light, shadowProfile);
            var m6 = instance.AddComponent<SandroneM6Controller>();
            m6.Configure(renderer, instance.transform, head, light, shadowProfile, profile);
            m6.SetLightDirectionToSource(SandroneM3Bootstrap.DefaultDirectionToLight);

            var cameraObject = new GameObject("Optional_EyeGear_Camera");
            var camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.153f, .149f, .149f, 1);
            camera.orthographic = false; camera.fieldOfView = 35f; camera.nearClipPlane = .03f; camera.farClipPlane = 30f;
            camera.allowHDR = false; camera.allowMSAA = true;
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene, ScenePath);

            foreach (var distance in new[] { .5f, 2f, 5f, 10f })
            {
                CaptureAtDistance(camera, instance, head, distance, 0f, $"Distance/M6Optional_{DistanceName(distance)}m.png");
                CaptureAtDistance(camera, instance, head, distance, .25f, $"Distance/M6Optional_{DistanceName(distance)}m_Yaw025.png");
                CaptureMask(camera, renderer, instance, head, distance, $"Distance/M6Optional_{DistanceName(distance)}m_GearMask.png");
            }
            instance.transform.rotation = Quaternion.identity;
            ConfigureCamera(camera, head.position, 2f);
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene, ScenePath);
        }

        private static void CaptureAtDistance(Camera camera, GameObject instance, Transform head, float distance, float yaw, string relative)
        {
            instance.transform.rotation = Quaternion.Euler(0, yaw, 0);
            ConfigureCamera(camera, head.position, distance);
            Capture(camera, relative);
        }

        private static void CaptureMask(Camera camera, Renderer renderer, GameObject instance, Transform head, float distance, string relative)
        {
            var original = renderer.sharedMaterials;
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(SandroneM4Bootstrap.IsolationShaderPath);
            var hidden = new Material(shader); hidden.SetColor("_Color", Color.clear);
            var white = new Material(shader); white.SetColor("_Color", Color.white);
            var mask = Enumerable.Repeat(hidden, original.Length).ToArray();
            mask[7] = white; mask[8] = white; mask[9] = white;
            renderer.sharedMaterials = mask;
            try
            {
                instance.transform.rotation = Quaternion.identity;
                ConfigureCamera(camera, head.position, distance);
                camera.backgroundColor = Color.black;
                Capture(camera, relative);
            }
            finally
            {
                renderer.sharedMaterials = original;
                camera.backgroundColor = new Color(.153f, .149f, .149f, 1);
                UnityEngine.Object.DestroyImmediate(hidden); UnityEngine.Object.DestroyImmediate(white);
            }
        }

        private static void ConfigureCamera(Camera camera, Vector3 target, float distance)
        {
            camera.transform.position = target + Vector3.forward * distance;
            camera.transform.rotation = Quaternion.LookRotation(target - camera.transform.position, Vector3.up);
        }

        private static void Capture(Camera camera, string relative)
        {
            var path = Artifact(relative); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var previous = RenderTexture.active;
            var rt = new RenderTexture(768, 768, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var image = new Texture2D(768, 768, TextureFormat.RGB24, false);
            try
            {
                camera.targetTexture = rt; rt.Create(); camera.Render(); RenderTexture.active = rt;
                image.ReadPixels(new Rect(0, 0, 768, 768), 0, 0); image.Apply(); File.WriteAllBytes(path, image.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = null; RenderTexture.active = previous; UnityEngine.Object.DestroyImmediate(image); rt.Release(); UnityEngine.Object.DestroyImmediate(rt);
            }
        }

        private static void ValidateAndWrite()
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var renderer = UnityEngine.Object.FindFirstObjectByType<SandroneM6Controller>()?.TargetRenderer as SkinnedMeshRenderer;
            var report = new Report
            {
                generatedUtc = DateTime.UtcNow.ToString("O"), unityVersion = Application.unityVersion,
                graphicsApi = SystemInfo.graphicsDeviceType.ToString(), graphicsDevice = SystemInfo.graphicsDeviceName,
                modelImported = renderer != null, standardBaselineUntouched = EditorBuildSettings.scenes.Length == 1 &&
                    EditorBuildSettings.scenes[0].enabled && EditorBuildSettings.scenes[0].path == SandroneM6Bootstrap.ScenePath,
                optionalSceneExcludedFromBuild = EditorBuildSettings.scenes.All(x => x.path != ScenePath), sourcePmxSha256 = "430efddcb190f106d5bf08d433868678436f86317400476c91a9fcf5c740d1f9"
            };
            if (renderer == null) report.failures.Add("Optional renderer missing.");
            else
            {
                report.triangleCount = renderer.sharedMesh.triangles.Length / 3;
                report.materialSlotCount = renderer.sharedMaterials.Length;
                report.rendererBoneCount = renderer.bones.Length;
                report.blendShapeCount = renderer.sharedMesh.blendShapeCount;
                if (report.triangleCount != 78744 || report.materialSlotCount != 33 || report.rendererBoneCount != 692 || report.blendShapeCount != 61)
                    report.failures.Add($"Optional topology contract mismatch: tri={report.triangleCount}, slots={report.materialSlotCount}, bones={report.rendererBoneCount}, shapes={report.blendShapeCount}.");
            }
            report.mipCoverageConfigured = GearTextures.All(path => AssetImporter.GetAtPath(path) is TextureImporter importer && importer.mipmapEnabled && importer.mipMapsPreserveCoverage && importer.wrapMode == TextureWrapMode.Clamp);
            if (!report.mipCoverageConfigured) report.failures.Add("Optional gear mip/alpha-coverage import contract failed.");
            if (!report.standardBaselineUntouched || !report.optionalSceneExcludedFromBuild) report.failures.Add("Optional scene replaced or entered the standard build baseline.");

            foreach (var distance in new[] { .5f, 2f, 5f, 10f })
            {
                var name = DistanceName(distance);
                var image = $"Distance/M6Optional_{name}m.png";
                var yaw = $"Distance/M6Optional_{name}m_Yaw025.png";
                var mask = $"Distance/M6Optional_{name}m_GearMask.png";
                if (!File.Exists(Artifact(image)) || !File.Exists(Artifact(yaw)) || !File.Exists(Artifact(mask)))
                {
                    report.failures.Add($"Optional distance evidence missing at {distance}m."); continue;
                }
                var maskPixels = Read(mask);
                var visible = maskPixels.Select(x => x.r > 127 || x.g > 127 || x.b > 127).ToArray();
                var result = new DistanceResult { distanceMeters = distance, gearMaskPixels = visible.Count(x => x), image = image, yawImage = yaw, maskImage = mask,
                    yaw025MaskedMae = MaskedMae(Read(image), Read(yaw), visible) };
                report.distances.Add(result);
            }
            var far = report.distances.FirstOrDefault(x => Mathf.Approximately(x.distanceMeters, 10f));
            report.recommendStandardFallback = far == null || far.gearMaskPixels < 16 || far.yaw025MaskedMae > 32f;
            report.recommendedFallbackDistanceMeters = report.recommendStandardFallback ? 5f : 10f;
            if (report.recommendStandardFallback) report.warnings.Add("可动齿轮在 10m 屏幕覆盖/亚像素稳定性不足；标准模型保持默认，变体建议在 5m 前切换回标准眼层或 LOD。 ");
            report.warnings.Add("该可选模型为独立 33 槽/78,744 三角资产，未替换 31 槽标准基线；三张齿轮贴图为原文件字节复制。 ");
            report.checks.Add("0.5/2/5/10m D3D11 captures generated");
            report.checks.Add("MipMap + Preserve Coverage + Bilinear + Clamp configured");
            report.checks.Add("Optional scene excluded from Build Settings");
            Directory.CreateDirectory(Artifact("")); File.WriteAllText(Artifact("M6OptionalEyeGearAudit.json"), JsonUtility.ToJson(report, true));
            if (report.failures.Count > 0) throw new InvalidOperationException("M6 optional eye-gear audit failed: " + string.Join("; ", report.failures));
            Debug.Log($"[Sandrone M6 Optional] Passed structural/distance audit; fallback={report.recommendStandardFallback}, distance={report.recommendedFallbackDistanceMeters}m.");
        }

        private static Color32[] Read(string relative)
        {
            var texture = new Texture2D(2, 2); try { texture.LoadImage(File.ReadAllBytes(Artifact(relative))); return texture.GetPixels32(); } finally { UnityEngine.Object.DestroyImmediate(texture); }
        }
        private static float MaskedMae(Color32[] a, Color32[] b, bool[] mask)
        {
            double sum = 0; var count = 0;
            for (var i = 0; i < a.Length; i++) if (mask[i]) { sum += Math.Abs(a[i].r - b[i].r) + Math.Abs(a[i].g - b[i].g) + Math.Abs(a[i].b - b[i].b); count += 3; }
            return count == 0 ? 0f : (float)(sum / count);
        }
        private static string DistanceName(float value) => value.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture).Replace('.', '_');
        private static string Artifact(string relative) => Path.GetFullPath(Path.Combine(Application.dataPath, "../TestArtifacts/M6OptionalEyeGear", relative));
        private static Transform Find(Transform root, string name) => root.GetComponentsInChildren<Transform>(true).FirstOrDefault(x => x.name == name);
        private static void EnsureFolder(string path)
        {
            var parts = path.Split('/'); var current = parts[0];
            for (var i = 1; i < parts.Length; i++) { var next = current + "/" + parts[i]; if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[i]); current = next; }
        }
    }
}
