using System;
using UnityEngine;

namespace SandroneToon
{
    public enum SandroneM4ResponseType { Matte = 0, Skin = 1, Silk = 2, Metal = 3 }
    public enum SandroneM4FeatureGroup { None = 0, Metal = 1, StockingOverlay = 2, HairOverlay = 3 }

    [Serializable]
    public struct SandroneM4MaterialResponse
    {
        public int materialIndex;
        public SandroneM4ResponseType responseType;
        public SandroneM4FeatureGroup featureGroup;
        [Range(0f, 2f)] public float specularIntensity;
        [Range(1f, 128f)] public float specularPower;
        [Range(0f, 2f)] public float matCapIntensity;
        [Range(0f, 1f)] public float metalMaskFallback;
        [Range(0.5f, 2f)] public float overlayColorBoost;
    }

    [CreateAssetMenu(menuName = "Sandrone/M4 Material Response Profile", fileName = "SandroneMaterialResponse_M4")]
    public sealed class SandroneM4MaterialResponseProfile : ScriptableObject
    {
        [SerializeField] private string contractVersion = "SandroneMaterialResponseProfile_v1_M4";
        [SerializeField] private Texture2D bodyControlMap;
        [SerializeField] private Texture2D skirtControlMap;
        [SerializeField] private Texture2D hairControlMap;
        [SerializeField] private Texture2D neutralControlMap;
        [SerializeField] private Texture2D metalMatCap;
        [SerializeField] private SandroneM4MaterialResponse[] materials = Array.Empty<SandroneM4MaterialResponse>();

        public string ContractVersion => contractVersion;
        public Texture2D BodyControlMap => bodyControlMap;
        public Texture2D SkirtControlMap => skirtControlMap;
        public Texture2D HairControlMap => hairControlMap;
        public Texture2D NeutralControlMap => neutralControlMap;
        public Texture2D MetalMatCap => metalMatCap;
        public SandroneM4MaterialResponse[] Materials => materials;

        public bool TryGet(int materialIndex, out SandroneM4MaterialResponse response)
        {
            foreach (var item in materials)
            {
                if (item.materialIndex == materialIndex)
                {
                    response = item;
                    return true;
                }
            }
            response = default;
            return false;
        }

#if UNITY_EDITOR
        public void EditorSet(Texture2D body, Texture2D skirt, Texture2D hair, Texture2D neutral,
            Texture2D matCap, SandroneM4MaterialResponse[] entries)
        {
            contractVersion = "SandroneMaterialResponseProfile_v1_M4";
            bodyControlMap = body;
            skirtControlMap = skirt;
            hairControlMap = hair;
            neutralControlMap = neutral;
            metalMatCap = matCap;
            materials = entries ?? Array.Empty<SandroneM4MaterialResponse>();
        }
#endif
    }
}
