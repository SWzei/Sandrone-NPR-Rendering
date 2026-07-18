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
    public static class SandroneM1Bootstrap
    {
        public const string ShaderPath = "Assets/Sandrone/Shaders/SandroneMainLightM1.shader";
        public const string MaterialDirectory = "Assets/Sandrone/Materials/M1";
        public const string ScenePath = "Assets/Sandrone/Tests/Scenes/ToonCalibration_M1.unity";
        public static readonly Vector3 DefaultDirectionToLight = new Vector3(0.35f, 0.45f, 1f).normalized;

        [MenuItem("Sandrone/M1/Build Main Light Baseline")]
        public static void Build()
        {
            Debug.Log("[Sandrone M1] Build started.");

            // M1 is additive. Revalidate M0 first, but never rewrite M0 assets here.
            SandroneM0Validator.ValidateAndWriteReport();

            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.productName = "Sandrone Toon M1";
            QualitySettings.antiAliasing = 4;

            EnsureFolder(MaterialDirectory);
            EnsureFolder("Assets/Sandrone/Tests/Scenes");
            AssetDatabase.ImportAsset(ShaderPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);

            var shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
            if (shader == null || !shader.isSupported)
            {
                throw new InvalidOperationException("M1 shader is missing or unsupported.");
            }

            var materials = CreateMaterials(shader);
            CreateCalibrationScene(materials);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            SandroneM1Validator.ValidateAndWriteReport();
            Debug.Log("[Sandrone M1] Build and validation completed.");
        }

        public static string MaterialPath(int sourceIndex, string sourceMaterialPath)
        {
            var sourceName = Path.GetFileNameWithoutExtension(sourceMaterialPath);
            return $"{MaterialDirectory}/M1_{sourceIndex:00}_{sourceName}.mat";
        }

        private static Material[] CreateMaterials(Shader shader)
        {
            var map = AssetDatabase.LoadAssetAtPath<SandroneMaterialMap>(SandroneM0Bootstrap.MaterialMapPath);
            if (map == null || map.Entries.Count != 31)
            {
                throw new InvalidOperationException("M0 material map is missing or incomplete.");
            }

            var output = new Material[map.Entries.Count];
            foreach (var entry in map.Entries.OrderBy(entry => entry.sourceIndex))
            {
                var source = AssetDatabase.LoadAssetAtPath<Material>(entry.materialAssetPath);
                if (source == null)
                {
                    throw new FileNotFoundException($"M0 material missing: {entry.materialAssetPath}");
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

                material.name = $"M1_{entry.sourceIndex:00}_{Path.GetFileNameWithoutExtension(entry.materialAssetPath)}";
                material.SetTexture("_BaseMap", source.GetTexture("_BaseMap"));
                material.SetColor("_BaseColor", source.GetColor("_BaseColor"));
                material.SetFloat("_LayerWeight", source.GetFloat("_LayerWeight"));
                material.SetFloat("_M1DebugMode", (float)SandroneM1DebugMode.BaseLit);
                material.SetFloat("_Cutoff", source.GetFloat("_Cutoff"));
                material.SetFloat("_AlphaClip", source.GetFloat("_AlphaClip"));
                material.SetFloat("_SrcBlend", source.GetFloat("_SrcBlend"));
                material.SetFloat("_DstBlend", source.GetFloat("_DstBlend"));
                material.SetFloat("_ZWrite", source.GetFloat("_ZWrite"));
                material.SetFloat("_Cull", source.GetFloat("_Cull"));
                material.SetOverrideTag("RenderType", source.GetTag("RenderType", false, "Opaque"));
                material.renderQueue = source.renderQueue;
                EditorUtility.SetDirty(material);
                output[entry.sourceIndex] = material;
            }
            return output;
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
            instance.name = "Sandrone_M1";
            instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            instance.transform.localScale = Vector3.one;

            var renderers = instance.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            if (renderers.Length != 1)
            {
                throw new InvalidOperationException($"Expected one SkinnedMeshRenderer, found {renderers.Length}.");
            }
            var renderer = renderers[0];
            if (renderer.sharedMaterials.Length != 31 || materials.Any(material => material == null))
            {
                throw new InvalidOperationException("M1 requires all 31 material slots.");
            }
            renderer.sharedMaterials = materials;

            var m0Controller = instance.AddComponent<SandroneM0Controller>();
            m0Controller.Configure(
                new SandroneM0Controller.LayerBinding { renderer = renderer, materialIndex = 9 },
                new SandroneM0Controller.LayerBinding { renderer = renderer, materialIndex = 30 },
                FindTransform(instance.transform, "KeyB02_M"));

            var lightObject = new GameObject("M1_MainDirectionalLight");
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

            var controller = instance.AddComponent<SandroneM1Controller>();
            controller.Configure(renderer, instance.transform, FindTransform(instance.transform, "頭"), mainLight);
            controller.SetLightDirectionToSource(DefaultDirectionToLight);
            controller.DebugMode = SandroneM1DebugMode.BaseLit;

            var cameraObject = new GameObject("M1_CalibrationCamera");
            var camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.153f, 0.149f, 0.149f, 1f);
            camera.orthographic = true;
            camera.orthographicSize = 0.92f;
            camera.allowHDR = false;
            camera.allowMSAA = true;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 20f;
            camera.transform.position = new Vector3(0f, 0.82f, 4f);
            camera.transform.LookAt(new Vector3(0f, 0.82f, 0f), Vector3.up);

            EditorSceneManager.SaveScene(scene, ScenePath);

            Capture(camera, controller, Quaternion.identity, SandroneM1DebugMode.BaseLit,
                DefaultDirectionToLight, 0.92f, 994, 1654, "ReferenceComparison/M1_BaseLit_Front.png");
            Capture(camera, controller, Quaternion.Euler(0f, -90f, 0f), SandroneM1DebugMode.BaseLit,
                DefaultDirectionToLight, 0.86f, 662, 1032, "ReferenceComparison/M1_BaseLit_Side.png");

            Capture(camera, controller, Quaternion.identity, SandroneM1DebugMode.NdotL,
                Vector3.forward, 0.92f, 768, 1280, "Debug/M1_NdotL_FrontLight.png");
            Capture(camera, controller, Quaternion.identity, SandroneM1DebugMode.NdotL,
                Vector3.right, 0.92f, 768, 1280, "Debug/M1_NdotL_RightLight.png");
            Capture(camera, controller, Quaternion.identity, SandroneM1DebugMode.NdotL,
                Vector3.back, 0.92f, 768, 1280, "Debug/M1_NdotL_BackLight.png");
            Capture(camera, controller, Quaternion.identity, SandroneM1DebugMode.NdotV,
                DefaultDirectionToLight, 0.92f, 768, 1280, "Debug/M1_NdotV.png");
            Capture(camera, controller, Quaternion.identity, SandroneM1DebugMode.HeadAxis,
                DefaultDirectionToLight, 0.92f, 768, 1280, "Debug/M1_HeadAxis.png");
            Capture(camera, controller, Quaternion.identity, SandroneM1DebugMode.MainLightColor,
                DefaultDirectionToLight, 0.92f, 768, 1280, "Debug/M1_MainLightColor.png");
            Capture(camera, controller, Quaternion.identity, SandroneM1DebugMode.MainLightDistanceAttenuation,
                DefaultDirectionToLight, 0.92f, 768, 1280, "Debug/M1_MainLightDistanceAttenuation.png");

            instance.transform.rotation = Quaternion.identity;
            controller.DebugMode = SandroneM1DebugMode.BaseLit;
            controller.SetLightDirectionToSource(DefaultDirectionToLight);
            controller.Apply(true);
            camera.orthographicSize = 0.92f;
            EditorUtility.SetDirty(controller);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);

            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        }

        private static void Capture(Camera camera, SandroneM1Controller controller, Quaternion modelRotation,
            SandroneM1DebugMode debugMode, Vector3 directionToLight, float orthographicSize,
            int width, int height, string relativePath)
        {
            controller.CharacterRoot.rotation = modelRotation;
            controller.DebugMode = debugMode;
            controller.SetLightDirectionToSource(directionToLight);
            controller.Apply(true);
            camera.orthographicSize = orthographicSize;

            var artifactRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "../TestArtifacts/M1"));
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
            Debug.Log($"[Sandrone M1] Captured {outputPath}");
        }

        private static Transform FindTransform(Transform root, string name)
        {
            return root.GetComponentsInChildren<Transform>(true).FirstOrDefault(transform => transform.name == name);
        }

        private static void EnsureFolder(string assetPath)
        {
            Directory.CreateDirectory(Path.GetFullPath(Path.Combine(Application.dataPath, "..", assetPath)));
        }
    }
}
