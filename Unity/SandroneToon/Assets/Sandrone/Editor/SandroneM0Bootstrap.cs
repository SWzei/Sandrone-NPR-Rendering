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
    public static class SandroneM0Bootstrap
    {
        public const string ModelPath = "Assets/Sandrone/Models/Sandrone_M0.fbx";
        public const string ShaderPath = "Assets/Sandrone/Shaders/SandroneUnlitM0.shader";
        public const string MaterialDirectory = "Assets/Sandrone/Materials/M0";
        public const string MaterialMapPath = "Assets/Sandrone/Configs/SandroneMaterialMap.asset";
        public const string ScenePath = "Assets/Sandrone/Tests/Scenes/ToonCalibration_M0.unity";

        private sealed class MaterialSpec
        {
            public readonly int index;
            public readonly string sourceName;
            public readonly string assetName;
            public readonly string texturePath;
            public readonly SandroneMaterialFamily family;
            public readonly SandroneSurfaceMode surface;
            public readonly bool doubleSided;
            public readonly float initialWeight;
            public readonly string note;

            public MaterialSpec(int index, string sourceName, string assetName, string texturePath,
                SandroneMaterialFamily family, SandroneSurfaceMode surface, bool doubleSided = true,
                float initialWeight = 1f, string note = "")
            {
                this.index = index;
                this.sourceName = sourceName;
                this.assetName = assetName;
                this.texturePath = texturePath;
                this.family = family;
                this.surface = surface;
                this.doubleSided = doubleSided;
                this.initialWeight = initialWeight;
                this.note = note;
            }
        }

        private static readonly MaterialSpec[] Specs =
        {
            S(0, "颜", "Face", "T_Face.png", SandroneMaterialFamily.Face),
            S(1, "颜2", "FaceSecondary", "T_Face.png", SandroneMaterialFamily.Face),
            S(2, "睫眉", "BrowLash", "T_Face.png", SandroneMaterialFamily.Face),
            S(3, "白目", "EyeWhite", "T_Face.png", SandroneMaterialFamily.Eye),
            S(4, "口舌", "MouthTongue", "T_Face.png", SandroneMaterialFamily.Face),
            S(5, "齿", "Teeth", "T_Face.png", SandroneMaterialFamily.Face),
            S(6, "目", "Iris", "T_Eye.png", SandroneMaterialFamily.Eye),
            S(7, "目2", "EyeGear", "T_EyeGear.png", SandroneMaterialFamily.Eye, SandroneSurfaceMode.AlphaBlend),
            S(8, "目3", "EyeLayer", "T_EyeLayer.png", SandroneMaterialFamily.Eye, SandroneSurfaceMode.AlphaBlend),
            S(9, "目al+", "EyeAL", "T_EyeAL.png", SandroneMaterialFamily.Eye, SandroneSurfaceMode.AlphaBlend, true, 0f,
                "PMX Eye AL material morph replacement; initial alpha is zero."),
            S(10, "目光", "EyeLight", "T_EyeLight.png", SandroneMaterialFamily.Eye, SandroneSurfaceMode.AlphaBlend),
            S(11, "星目", "StarEye", "T_Face.png", SandroneMaterialFamily.Eye),
            S(12, "前髪", "HairFront", "T_Hair.png", SandroneMaterialFamily.Hair),
            S(13, "髮", "HairBack", "T_Hair.png", SandroneMaterialFamily.Hair),
            S(14, "头饰", "HeadOrnament", "T_Hair.png", SandroneMaterialFamily.Cloth),
            S(15, "体", "Body", "T_Body.png", SandroneMaterialFamily.Cloth),
            S(16, "体2", "BodySecondary", "T_Hair.png", SandroneMaterialFamily.Cloth),
            S(17, "首", "Neck", "T_Hair.png", SandroneMaterialFamily.Skin),
            S(18, "肌", "Skin", "T_Body.png", SandroneMaterialFamily.Skin),
            S(19, "肌2", "SkinOverlay", "T_SkinOverlay.png", SandroneMaterialFamily.Skin, SandroneSurfaceMode.AlphaBlend),
            S(20, "裙", "Skirt", "T_Body.png", SandroneMaterialFamily.Cloth),
            S(21, "裙2", "SkirtSecondary", "T_Skirt.png", SandroneMaterialFamily.Cloth),
            S(22, "裙3", "SkirtTertiary", "T_Skirt.png", SandroneMaterialFamily.Cloth),
            S(23, "裙饰", "SkirtOrnament", "T_Skirt.png", SandroneMaterialFamily.Cloth),
            S(24, "饰", "MetalOrnament", "T_Skirt.png", SandroneMaterialFamily.Metal,
                SandroneSurfaceMode.Opaque, true, 1f, "M0 preserves BaseMap only; sphere-map response is deferred to M4."),
            S(25, "饰内", "OrnamentInner", "T_Body.png", SandroneMaterialFamily.Cloth, SandroneSurfaceMode.Opaque, false),
            S(26, "裙内", "SkirtInner", "T_Skirt.png", SandroneMaterialFamily.Cloth, SandroneSurfaceMode.Opaque, false),
            S(27, "裙饰内", "SkirtOrnamentInner", "T_Skirt.png", SandroneMaterialFamily.Cloth, SandroneSurfaceMode.Opaque, false),
            S(28, "袜+", "StockingOverlay", "T_Overlay.png", SandroneMaterialFamily.Overlay, SandroneSurfaceMode.AlphaBlend),
            S(29, "髮+", "HairHighlightOverlay", "T_Overlay.png", SandroneMaterialFamily.Overlay, SandroneSurfaceMode.AlphaBlend,
                true, 1f, "M0 reproduces source alpha shape; sphere-map response is deferred to M6."),
            S(30, "照れ+", "BlushOverlay", "T_Blush.png", SandroneMaterialFamily.Overlay, SandroneSurfaceMode.AlphaBlend, true, 0f,
                "PMX blush material morph replacement; initial alpha is zero and full morph adds 0.8 alpha."),
        };

        private static MaterialSpec S(int index, string sourceName, string assetName, string textureFile,
            SandroneMaterialFamily family, SandroneSurfaceMode surface = SandroneSurfaceMode.Opaque,
            bool doubleSided = true, float initialWeight = 1f, string note = "")
        {
            return new MaterialSpec(index, sourceName, assetName,
                $"Assets/Sandrone/Textures/SourceBase/{textureFile}", family, surface, doubleSided, initialWeight, note);
        }

        [MenuItem("Sandrone/M0/Build Baseline")]
        public static void Build()
        {
            Debug.Log("[Sandrone M0] Build started.");
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.productName = "Sandrone Toon M0";
            PlayerSettings.companyName = "Sandrone Toon Study";
            QualitySettings.antiAliasing = 4;

            EnsureFolder("Assets/Sandrone/Materials/M0");
            EnsureFolder("Assets/Sandrone/Configs");
            EnsureFolder("Assets/Sandrone/Tests/Scenes");

            AssetDatabase.ImportAsset(ShaderPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            foreach (var texturePath in Specs.Select(spec => spec.texturePath).Distinct(StringComparer.Ordinal))
            {
                AssetDatabase.ImportAsset(texturePath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            }
            AssetDatabase.ImportAsset(ModelPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);

            var shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
            if (shader == null || !shader.isSupported)
            {
                throw new InvalidOperationException("M0 shader is missing or unsupported.");
            }

            var materials = CreateMaterials(shader);
            CreateMaterialMap();
            CreateCalibrationScene(materials);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            SandroneM0Validator.ValidateAndWriteReport();
            Debug.Log("[Sandrone M0] Build and validation completed.");
        }

        private static Material[] CreateMaterials(Shader shader)
        {
            var output = new Material[Specs.Length];
            foreach (var spec in Specs)
            {
                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(spec.texturePath);
                if (texture == null)
                {
                    throw new FileNotFoundException($"Texture not imported: {spec.texturePath}");
                }

                var path = MaterialPath(spec);
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

                material.name = $"M{spec.index:00}_{spec.assetName}";
                material.SetTexture("_BaseMap", texture);
                material.SetColor("_BaseColor", Color.white);
                material.SetFloat("_LayerWeight", spec.initialWeight);
                material.SetFloat("_Cull", spec.doubleSided ? (float)CullMode.Off : (float)CullMode.Back);
                ConfigureSurface(material, spec.surface, spec.index);
                EditorUtility.SetDirty(material);
                output[spec.index] = material;
            }
            return output;
        }

        private static void ConfigureSurface(Material material, SandroneSurfaceMode surface, int index)
        {
            material.SetFloat("_AlphaClip", surface == SandroneSurfaceMode.AlphaClip ? 1f : 0f);
            material.SetFloat("_Cutoff", 0.5f);
            if (surface == SandroneSurfaceMode.AlphaBlend)
            {
                material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                material.SetFloat("_ZWrite", 0f);
                material.SetOverrideTag("RenderType", "Transparent");
                material.renderQueue = (int)RenderQueue.Transparent + index;
            }
            else if (surface == SandroneSurfaceMode.AlphaClip)
            {
                material.SetFloat("_SrcBlend", (float)BlendMode.One);
                material.SetFloat("_DstBlend", (float)BlendMode.Zero);
                material.SetFloat("_ZWrite", 1f);
                material.SetOverrideTag("RenderType", "TransparentCutout");
                material.renderQueue = (int)RenderQueue.AlphaTest + index;
            }
            else
            {
                material.SetFloat("_SrcBlend", (float)BlendMode.One);
                material.SetFloat("_DstBlend", (float)BlendMode.Zero);
                material.SetFloat("_ZWrite", 1f);
                material.SetOverrideTag("RenderType", "Opaque");
                material.renderQueue = (int)RenderQueue.Geometry;
            }
        }

        private static void CreateMaterialMap()
        {
            var map = AssetDatabase.LoadAssetAtPath<SandroneMaterialMap>(MaterialMapPath);
            if (map == null)
            {
                map = ScriptableObject.CreateInstance<SandroneMaterialMap>();
                AssetDatabase.CreateAsset(map, MaterialMapPath);
            }

            map.EditorSetEntries(Specs.Select(spec => new SandroneMaterialMap.Entry
            {
                sourceIndex = spec.index,
                sourceName = spec.sourceName,
                materialAssetPath = MaterialPath(spec),
                baseTextureAssetPath = spec.texturePath,
                family = spec.family,
                surfaceMode = spec.surface,
                doubleSided = spec.doubleSided,
                initialLayerWeight = spec.initialWeight,
                migrationNote = spec.note
            }).ToList());
            EditorUtility.SetDirty(map);
        }

        private static void CreateCalibrationScene(Material[] materials)
        {
            var modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (modelAsset == null)
            {
                throw new FileNotFoundException($"FBX model not imported: {ModelPath}");
            }

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(modelAsset, scene);
            instance.name = "Sandrone_M0";
            instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            instance.transform.localScale = Vector3.one;

            var renderers = instance.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            if (renderers.Length != 1)
            {
                throw new InvalidOperationException($"Expected one SkinnedMeshRenderer, found {renderers.Length}.");
            }
            var renderer = renderers[0];
            var importedNames = renderer.sharedMaterials.Select(m => m != null ? m.name : "<null>").ToArray();
            if (importedNames.Length != Specs.Length)
            {
                throw new InvalidOperationException($"Expected 31 imported material slots, found {importedNames.Length}.");
            }
            for (var i = 0; i < importedNames.Length; i++)
            {
                if (!importedNames[i].StartsWith($"M{i:00}_", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException($"FBX material order mismatch at {i}: {importedNames[i]}");
                }
            }
            renderer.sharedMaterials = materials;

            var controller = instance.AddComponent<SandroneM0Controller>();
            controller.Configure(
                new SandroneM0Controller.LayerBinding { renderer = renderer, materialIndex = 9 },
                new SandroneM0Controller.LayerBinding { renderer = renderer, materialIndex = 30 },
                FindTransform(instance.transform, "KeyB02_M"));

            var cameraObject = new GameObject("M0_CalibrationCamera");
            var camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.153f, 0.149f, 0.149f, 1f);
            camera.orthographic = true;
            camera.orthographicSize = 0.92f;
            camera.allowHDR = false;
            camera.allowMSAA = true;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 20f;
            camera.transform.position = new Vector3(0f, 0.82f, -4f);
            camera.transform.LookAt(new Vector3(0f, 0.82f, 0f), Vector3.up);

            EditorSceneManager.SaveScene(scene, ScenePath);
            Capture(camera, instance.transform, Quaternion.Euler(0f, 180f, 0f), 0.92f, 994, 1654, "M0_Front.png");
            Capture(camera, instance.transform, Quaternion.Euler(0f, 90f, 0f), 0.86f, 662, 1032, "M0_Side.png");
            instance.transform.rotation = Quaternion.identity;
            camera.transform.position = new Vector3(0f, 0.82f, 4f);
            camera.transform.LookAt(new Vector3(0f, 0.82f, 0f), Vector3.up);
            camera.orthographicSize = 0.92f;
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);

            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        }

        private static void Capture(Camera camera, Transform model, Quaternion rotation, float orthographicSize,
            int width, int height, string fileName)
        {
            model.rotation = rotation;
            camera.orthographicSize = orthographicSize;
            var outputDirectory = Path.GetFullPath(Path.Combine(Application.dataPath, "../TestArtifacts/M0/ReferenceComparison"));
            Directory.CreateDirectory(outputDirectory);
            var outputPath = Path.Combine(outputDirectory, fileName);

            var previous = RenderTexture.active;
            var texture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
            {
                antiAliasing = 1,
                name = $"Sandrone_{fileName}_RT"
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
            Debug.Log($"[Sandrone M0] Captured {outputPath}");
        }

        private static Transform FindTransform(Transform root, string name)
        {
            foreach (var transform in root.GetComponentsInChildren<Transform>(true))
            {
                if (transform.name == name)
                {
                    return transform;
                }
            }
            return null;
        }

        private static string MaterialPath(MaterialSpec spec) => $"{MaterialDirectory}/M{spec.index:00}_{spec.assetName}.mat";

        private static void EnsureFolder(string assetPath)
        {
            var absolute = Path.GetFullPath(Path.Combine(Application.dataPath, "..", assetPath));
            Directory.CreateDirectory(absolute);
        }
    }
}
