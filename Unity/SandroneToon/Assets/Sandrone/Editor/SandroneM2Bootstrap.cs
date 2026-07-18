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
    public static class SandroneM2Bootstrap
    {
        public const string ShaderPath = "Assets/Sandrone/Shaders/SandroneToonRampM2.shader";
        public const string RampTexturePath = "Assets/Sandrone/Textures/Ramps/Sandrone_Ramp_WarmCool.png";
        public const string RampProfilePath = "Assets/Sandrone/Configs/SandroneRampProfile_M2.asset";
        public const string MaterialDirectory = "Assets/Sandrone/Materials/M2";
        public const string ScenePath = "Assets/Sandrone/Tests/Scenes/ToonCalibration_M2.unity";
        public const int RampWidth = 256;
        public const int RampHeight = 64;
        public const int RampRowCount = 5;
        public const float BandSoftness = 0.015f;
        public const float BandAA = 1f;
        public static readonly Vector3 DefaultDirectionToLight = new Vector3(0.35f, 0.45f, 1f).normalized;

        private sealed class RowSpec
        {
            public readonly SandroneRampFamily family;
            public readonly string displayName;
            public readonly Color shadow;
            public readonly Color lit;
            public readonly float threshold;
            public readonly int[] materialIndices;

            public RowSpec(SandroneRampFamily family, string displayName, Color shadow, Color lit,
                float threshold, params int[] materialIndices)
            {
                this.family = family;
                this.displayName = displayName;
                this.shadow = shadow;
                this.lit = lit;
                this.threshold = threshold;
                this.materialIndices = materialIndices;
            }
        }

        private static readonly RowSpec[] Rows =
        {
            new(SandroneRampFamily.SkinFace, "Skin / Face",
                new Color(0.72f, 0.62f, 0.66f, 1f), new Color(0.90f, 0.87f, 0.86f, 1f), 0.52f,
                0, 1, 2, 4, 5, 17, 18, 19, 30),
            new(SandroneRampFamily.LightCloth, "Light Cloth",
                new Color(0.62f, 0.64f, 0.72f, 1f), new Color(0.92f, 0.91f, 0.88f, 1f), 0.50f,
                14, 15, 20, 21, 22, 23, 25, 26, 27),
            new(SandroneRampFamily.DarkClothHair, "Dark Cloth / Hair",
                new Color(0.52f, 0.50f, 0.60f, 1f), new Color(0.86f, 0.84f, 0.82f, 1f), 0.48f,
                12, 13, 16, 28, 29),
            new(SandroneRampFamily.Metal, "Metal",
                new Color(0.57f, 0.53f, 0.64f, 1f), new Color(0.92f, 0.86f, 0.72f, 1f), 0.50f,
                24),
            new(SandroneRampFamily.Eye, "Eye",
                new Color(0.66f, 0.63f, 0.76f, 1f), new Color(0.92f, 0.90f, 0.93f, 1f), 0.46f,
                3, 6, 7, 8, 9, 10, 11)
        };

        [MenuItem("Sandrone/M2/Build Toon Ramp")]
        public static void Build()
        {
            Debug.Log("[Sandrone M2] Build started.");

            // M2 is additive: prove M1 still works without rewriting its assets.
            SandroneM1Validator.ValidateAndWriteReport();
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.productName = "Sandrone Toon M2";
            QualitySettings.antiAliasing = 4;

            EnsureFolder(MaterialDirectory);
            EnsureFolder("Assets/Sandrone/Textures/Ramps");
            EnsureFolder("Assets/Sandrone/Configs");
            EnsureFolder("Assets/Sandrone/Tests/Scenes");

            CreateRampPngIfMissing();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            AssetDatabase.ImportAsset(RampTexturePath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            AssetDatabase.ImportAsset(ShaderPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);

            var ramp = AssetDatabase.LoadAssetAtPath<Texture2D>(RampTexturePath);
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
            if (ramp == null || shader == null || !shader.isSupported)
            {
                throw new InvalidOperationException("M2 Ramp or Shader is missing/unsupported.");
            }

            CreateRampProfile(ramp);
            var materials = CreateMaterials(shader, ramp);
            CreateCalibrationScene(materials);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            SandroneM2Validator.ValidateAndWriteReport();
            Debug.Log("[Sandrone M2] Build and validation completed.");
        }

        public static string MaterialPath(int sourceIndex, string sourceMaterialPath)
        {
            var sourceName = Path.GetFileNameWithoutExtension(sourceMaterialPath);
            return $"{MaterialDirectory}/M2_{sourceIndex:00}_{sourceName}.mat";
        }

        public static int RowForMaterial(int materialIndex)
        {
            for (var row = 0; row < Rows.Length; row++)
            {
                if (Rows[row].materialIndices.Contains(materialIndex))
                {
                    return row;
                }
            }
            return -1;
        }

        public static float ThresholdForRow(int row) => Rows[row].threshold;
        public static Color ShadowForRow(int row) => Rows[row].shadow;
        public static Color LitForRow(int row) => Rows[row].lit;
        public static int[] MaterialIndicesForRow(int row) => Rows[row].materialIndices.ToArray();

        private static void CreateRampPngIfMissing()
        {
            var absolutePath = AssetPathToAbsolute(RampTexturePath);
            if (File.Exists(absolutePath))
            {
                return;
            }

            var texture = new Texture2D(RampWidth, RampHeight, TextureFormat.RGBA32, false, true);
            try
            {
                for (var y = 0; y < RampHeight; y++)
                {
                    var row = Mathf.Min(RampRowCount - 1, y * RampRowCount / RampHeight);
                    for (var x = 0; x < RampWidth; x++)
                    {
                        var u = (x + 0.5f) / RampWidth;
                        var t = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.25f, 0.75f, u));
                        texture.SetPixel(x, y, Color.Lerp(Rows[row].shadow, Rows[row].lit, t));
                    }
                }
                texture.Apply(false, false);
                File.WriteAllBytes(absolutePath, texture.EncodeToPNG());
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }

        private static void CreateRampProfile(Texture2D ramp)
        {
            var profile = AssetDatabase.LoadAssetAtPath<SandroneM2RampProfile>(RampProfilePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<SandroneM2RampProfile>();
                AssetDatabase.CreateAsset(profile, RampProfilePath);
            }

            profile.EditorSet(ramp, Rows.Select(row => new SandroneM2RampProfile.Row
            {
                family = row.family,
                displayName = row.displayName,
                shadowMultiplier = row.shadow,
                litMultiplier = row.lit,
                threshold = row.threshold,
                materialIndices = row.materialIndices.ToArray()
            }).ToList());
            EditorUtility.SetDirty(profile);
        }

        private static Material[] CreateMaterials(Shader shader, Texture2D ramp)
        {
            var map = AssetDatabase.LoadAssetAtPath<SandroneMaterialMap>(SandroneM0Bootstrap.MaterialMapPath);
            if (map == null || map.Entries.Count != 31)
            {
                throw new InvalidOperationException("M0 material map is missing or incomplete.");
            }

            var output = new Material[map.Entries.Count];
            foreach (var entry in map.Entries.OrderBy(entry => entry.sourceIndex))
            {
                var m1Path = SandroneM1Bootstrap.MaterialPath(entry.sourceIndex, entry.materialAssetPath);
                var source = AssetDatabase.LoadAssetAtPath<Material>(m1Path);
                if (source == null)
                {
                    throw new FileNotFoundException($"M1 material missing: {m1Path}");
                }
                var row = RowForMaterial(entry.sourceIndex);
                if (row < 0)
                {
                    throw new InvalidOperationException($"No M2 Ramp row for material {entry.sourceIndex}.");
                }

                var path = MaterialPath(entry.sourceIndex, entry.materialAssetPath);
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material == null)
                {
                    material = new Material(shader);
                    AssetDatabase.CreateAsset(material, path);
                }
                else
                {
                    material.shader = shader;
                }

                material.name = $"M2_{entry.sourceIndex:00}_{Path.GetFileNameWithoutExtension(entry.materialAssetPath)}";
                CopySurfaceAndBase(source, material);
                material.SetTexture("_RampMap", ramp);
                material.SetFloat("_RampRow", row);
                material.SetFloat("_RampRowCount", RampRowCount);
                material.SetFloat("_Threshold", Rows[row].threshold);
                material.SetFloat("_BandSoftness", BandSoftness);
                material.SetFloat("_BandAA", BandAA);
                material.SetFloat("_M2DebugMode", (float)SandroneM2DebugMode.FinalToon);
                EditorUtility.SetDirty(material);
                output[entry.sourceIndex] = material;
            }
            return output;
        }

        private static void CopySurfaceAndBase(Material source, Material destination)
        {
            destination.SetTexture("_BaseMap", source.GetTexture("_BaseMap"));
            destination.SetColor("_BaseColor", source.GetColor("_BaseColor"));
            destination.SetFloat("_LayerWeight", source.GetFloat("_LayerWeight"));
            destination.SetFloat("_Cutoff", source.GetFloat("_Cutoff"));
            destination.SetFloat("_AlphaClip", source.GetFloat("_AlphaClip"));
            destination.SetFloat("_SrcBlend", source.GetFloat("_SrcBlend"));
            destination.SetFloat("_DstBlend", source.GetFloat("_DstBlend"));
            destination.SetFloat("_ZWrite", source.GetFloat("_ZWrite"));
            destination.SetFloat("_Cull", source.GetFloat("_Cull"));
            destination.SetOverrideTag("RenderType", source.GetTag("RenderType", false, "Opaque"));
            destination.renderQueue = source.renderQueue;
        }

        private static void CreateCalibrationScene(Material[] materials)
        {
            var modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(SandroneM0Bootstrap.ModelPath);
            if (modelAsset == null)
            {
                throw new FileNotFoundException($"FBX model not imported: {SandroneM0Bootstrap.ModelPath}");
            }

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(modelAsset, scene);
            instance.name = "Sandrone_M2";
            instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            instance.transform.localScale = Vector3.one;

            var renderers = instance.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            if (renderers.Length != 1 || materials.Length != 31 || materials.Any(material => material == null))
            {
                throw new InvalidOperationException("M2 requires one renderer and all 31 materials.");
            }
            var renderer = renderers[0];
            renderer.sharedMaterials = materials;

            var m0Controller = instance.AddComponent<SandroneM0Controller>();
            m0Controller.Configure(
                new SandroneM0Controller.LayerBinding { renderer = renderer, materialIndex = 9 },
                new SandroneM0Controller.LayerBinding { renderer = renderer, materialIndex = 30 },
                FindTransform(instance.transform, "KeyB02_M"));

            var lightObject = new GameObject("M2_MainDirectionalLight");
            var mainLight = lightObject.AddComponent<Light>();
            mainLight.type = LightType.Directional;
            mainLight.color = Color.white;
            mainLight.intensity = 1f;
            mainLight.shadows = LightShadows.None;
            mainLight.renderMode = LightRenderMode.ForcePixel;
            RenderSettings.sun = mainLight;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = Color.black;
            RenderSettings.reflectionIntensity = 0f;

            var controller = instance.AddComponent<SandroneM2Controller>();
            controller.Configure(renderer, instance.transform, FindTransform(instance.transform, "頭"), mainLight);
            controller.SetLightDirectionToSource(DefaultDirectionToLight);
            controller.DebugMode = SandroneM2DebugMode.FinalToon;

            var cameraObject = new GameObject("M2_CalibrationCamera");
            var camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = CalibrationBackground;
            camera.orthographic = true;
            camera.orthographicSize = 0.92f;
            camera.allowHDR = false;
            camera.allowMSAA = true;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 20f;
            camera.transform.position = new Vector3(0f, 0.82f, 4f);
            camera.transform.LookAt(new Vector3(0f, 0.82f, 0f), Vector3.up);

            EditorSceneManager.SaveScene(scene, ScenePath);
            Capture(camera, controller, Quaternion.identity, SandroneM2DebugMode.FinalToon,
                DefaultDirectionToLight, 0.92f, 994, 1654, "ReferenceComparison/M2_FinalToon_Front.png");
            Capture(camera, controller, Quaternion.Euler(0f, -90f, 0f), SandroneM2DebugMode.FinalToon,
                DefaultDirectionToLight, 0.86f, 662, 1032, "ReferenceComparison/M2_FinalToon_Side.png");

            Capture(camera, controller, Quaternion.identity, SandroneM2DebugMode.HalfLambert,
                Vector3.forward, 0.92f, 768, 1280, "Debug/M2_HalfLambert.png");
            Capture(camera, controller, Quaternion.identity, SandroneM2DebugMode.BandMask,
                Vector3.forward, 0.92f, 768, 1280, "Debug/M2_BandMask_FrontLight.png");
            Capture(camera, controller, Quaternion.identity, SandroneM2DebugMode.BandMask,
                Vector3.right, 0.92f, 768, 1280, "Debug/M2_BandMask_RightLight.png");
            Capture(camera, controller, Quaternion.identity, SandroneM2DebugMode.BandMask,
                Vector3.back, 0.92f, 768, 1280, "Debug/M2_BandMask_BackLight.png");
            Capture(camera, controller, Quaternion.identity, SandroneM2DebugMode.RampUV,
                DefaultDirectionToLight, 0.92f, 768, 1280, "Debug/M2_RampUV.png");
            Capture(camera, controller, Quaternion.identity, SandroneM2DebugMode.RampSample,
                DefaultDirectionToLight, 0.92f, 768, 1280, "Debug/M2_RampSample.png");
            Capture(camera, controller, Quaternion.identity, SandroneM2DebugMode.NdotV,
                DefaultDirectionToLight, 0.92f, 768, 1280, "Debug/M2_NdotV.png");
            Capture(camera, controller, Quaternion.identity, SandroneM2DebugMode.HeadAxis,
                DefaultDirectionToLight, 0.92f, 768, 1280, "Debug/M2_HeadAxis.png");
            Capture(camera, controller, Quaternion.identity, SandroneM2DebugMode.Silhouette,
                DefaultDirectionToLight, 0.92f, 768, 1280, "Debug/M2_Silhouette.png");
            Capture(camera, controller, Quaternion.identity, SandroneM2DebugMode.BandMask,
                DefaultDirectionToLight, 0.62f, 768, 1280, "Debug/M2_BandMask_Near.png");
            Capture(camera, controller, Quaternion.identity, SandroneM2DebugMode.Silhouette,
                DefaultDirectionToLight, 0.62f, 768, 1280, "Debug/M2_Silhouette_Near.png");
            Capture(camera, controller, Quaternion.identity, SandroneM2DebugMode.BandMask,
                DefaultDirectionToLight, 1.35f, 768, 1280, "Debug/M2_BandMask_Far.png");
            Capture(camera, controller, Quaternion.identity, SandroneM2DebugMode.Silhouette,
                DefaultDirectionToLight, 1.35f, 768, 1280, "Debug/M2_Silhouette_Far.png");
            Capture(camera, controller, Quaternion.identity, SandroneM2DebugMode.FinalToon,
                Vector3.forward, 0.92f, 768, 1280, "Debug/M2_FinalToon_FrontLight.png");
            Capture(camera, controller, Quaternion.identity, SandroneM2DebugMode.FinalToon,
                Vector3.back, 0.92f, 768, 1280, "Debug/M2_FinalToon_BackLight.png");

            instance.transform.rotation = Quaternion.identity;
            controller.DebugMode = SandroneM2DebugMode.FinalToon;
            controller.SetLightDirectionToSource(DefaultDirectionToLight);
            controller.Apply(true);
            camera.orthographicSize = 0.92f;
            camera.backgroundColor = CalibrationBackground;
            EditorUtility.SetDirty(controller);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        }

        private static readonly Color CalibrationBackground = new(0.153f, 0.149f, 0.149f, 1f);

        private static void Capture(Camera camera, SandroneM2Controller controller, Quaternion modelRotation,
            SandroneM2DebugMode debugMode, Vector3 directionToLight, float orthographicSize,
            int width, int height, string relativePath)
        {
            controller.CharacterRoot.rotation = modelRotation;
            controller.DebugMode = debugMode;
            controller.SetLightDirectionToSource(directionToLight);
            controller.Apply(true);
            camera.orthographicSize = orthographicSize;
            camera.backgroundColor = debugMode == SandroneM2DebugMode.FinalToon ? CalibrationBackground : Color.black;

            var artifactRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "../TestArtifacts/M2"));
            var outputPath = Path.Combine(artifactRoot, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? artifactRoot);

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
            Debug.Log($"[Sandrone M2] Captured {outputPath}");
        }

        private static Transform FindTransform(Transform root, string name)
        {
            return root.GetComponentsInChildren<Transform>(true).FirstOrDefault(transform => transform.name == name);
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
