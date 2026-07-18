using UnityEditor;

namespace SandroneToon.Editor
{
    public sealed class SandroneModelPostprocessor : AssetPostprocessor
    {
        private void OnPreprocessModel()
        {
            if (assetPath != "Assets/Sandrone/Models/Sandrone_M0.fbx" &&
                assetPath != "Assets/Sandrone/Models/Optional/Sandrone_EyeGear_M6.fbx")
            {
                return;
            }

            var importer = (ModelImporter)assetImporter;
            importer.importBlendShapes = true;
            importer.importNormals = ModelImporterNormals.Import;
            importer.importTangents = ModelImporterTangents.CalculateMikk;
            importer.indexFormat = ModelImporterIndexFormat.Auto;
            importer.animationType = ModelImporterAnimationType.Generic;
            importer.importAnimation = false;
            importer.optimizeGameObjects = false;
            importer.preserveHierarchy = true;
            importer.useFileScale = true;
            importer.globalScale = 1f;
            importer.generateSecondaryUV = false;
            importer.isReadable = false;
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            importer.materialLocation = ModelImporterMaterialLocation.InPrefab;
            importer.materialName = ModelImporterMaterialName.BasedOnMaterialName;
            importer.materialSearch = ModelImporterMaterialSearch.Local;
        }
    }

    public sealed class SandroneTexturePostprocessor : AssetPostprocessor
    {
        private void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith("Assets/Sandrone/Textures/SourceBase/") &&
                !assetPath.StartsWith("Assets/Sandrone/Textures/OptionalEyeGear/"))
            {
                return;
            }

            var importer = (TextureImporter)assetImporter;
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = true;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = true;
            if (assetPath.StartsWith("Assets/Sandrone/Textures/OptionalEyeGear/"))
            {
                importer.mipMapsPreserveCoverage = true;
                importer.alphaTestReferenceValue = 0.5f;
                importer.wrapMode = UnityEngine.TextureWrapMode.Clamp;
            }
            else importer.wrapMode = UnityEngine.TextureWrapMode.Repeat;
            importer.filterMode = UnityEngine.FilterMode.Bilinear;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
        }
    }
}
