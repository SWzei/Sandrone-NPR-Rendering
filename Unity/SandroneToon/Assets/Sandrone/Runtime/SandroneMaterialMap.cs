using System;
using System.Collections.Generic;
using UnityEngine;

namespace SandroneToon
{
    public enum SandroneMaterialFamily
    {
        Face,
        Eye,
        Hair,
        Skin,
        Cloth,
        Metal,
        Overlay
    }

    public enum SandroneSurfaceMode
    {
        Opaque,
        AlphaClip,
        AlphaBlend
    }

    [CreateAssetMenu(menuName = "Sandrone/Material Map", fileName = "SandroneMaterialMap")]
    public sealed class SandroneMaterialMap : ScriptableObject
    {
        [Serializable]
        public sealed class Entry
        {
            public int sourceIndex;
            public string sourceName;
            public string materialAssetPath;
            public string baseTextureAssetPath;
            public SandroneMaterialFamily family;
            public SandroneSurfaceMode surfaceMode;
            public bool doubleSided;
            [Range(0f, 1f)] public float initialLayerWeight = 1f;
            public string migrationNote;
        }

        [SerializeField] private string contractVersion = "SandroneMaterialMap_v1_M0";
        [SerializeField] private string sourcePmxSha256 = "f73cd498580b0950856536223d57df04eb1164e01836c783cc75188a4c5c7514";
        [SerializeField] private List<Entry> entries = new();

        public string ContractVersion => contractVersion;
        public string SourcePmxSha256 => sourcePmxSha256;
        public IReadOnlyList<Entry> Entries => entries;

#if UNITY_EDITOR
        public void EditorSetEntries(List<Entry> value)
        {
            entries = value;
        }
#endif
    }
}

