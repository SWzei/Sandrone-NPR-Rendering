using System;
using UnityEngine;

namespace SandroneToon
{
    public enum SandroneM6Role
    {
        None = 0,
        BrowLash = 1,
        EyeStencilWriter = 2,
        EyeLayer = 3,
        HairBase = 4,
        HairOverlay = 5
    }

    [Serializable]
    public struct SandroneM6Slot
    {
        public int materialIndex;
        public SandroneM6Role role;
        [Range(0f, 1f)] public float eyeFlatLighting;
        [Range(0f, 1f)] public float hairSpecularWeight;
    }

    [CreateAssetMenu(menuName = "Sandrone/M6 Hair Eye Profile", fileName = "SandroneHairEyeProfile_M6")]
    public sealed class SandroneM6HairEyeProfile : ScriptableObject
    {
        [SerializeField] private string contractVersion = "SandroneHairEyeProfile_v1_M6";
        [SerializeField] private SandroneM6Slot[] slots = Array.Empty<SandroneM6Slot>();
        [SerializeField, Range(0f, 1f)] private float hairSpecularIntensity = 0.16f;
        [SerializeField, Range(1f, 128f)] private float hairSpecularPower = 28f;
        [SerializeField, Range(0f, 1f)] private float hairSpecularThreshold = 0.52f;
        [SerializeField, Range(0.001f, 0.25f)] private float hairSpecularSoftness = 0.06f;
        [SerializeField] private Color hairSpecularColor = new(0.82f, 0.76f, 0.68f, 1f);

        public string ContractVersion => contractVersion;
        public SandroneM6Slot[] Slots => slots;
        public float HairSpecularIntensity => hairSpecularIntensity;
        public float HairSpecularPower => hairSpecularPower;
        public float HairSpecularThreshold => hairSpecularThreshold;
        public float HairSpecularSoftness => hairSpecularSoftness;
        public Color HairSpecularColor => hairSpecularColor;

        public bool TryGet(int materialIndex, out SandroneM6Slot slot)
        {
            foreach (var item in slots)
            {
                if (item.materialIndex == materialIndex)
                {
                    slot = item;
                    return true;
                }
            }
            slot = default;
            return false;
        }

#if UNITY_EDITOR
        public void EditorSet(SandroneM6Slot[] entries, float intensity, float power, float threshold,
            float softness, Color color)
        {
            contractVersion = "SandroneHairEyeProfile_v1_M6";
            slots = entries ?? Array.Empty<SandroneM6Slot>();
            hairSpecularIntensity = Mathf.Clamp01(intensity);
            hairSpecularPower = Mathf.Clamp(power, 1f, 128f);
            hairSpecularThreshold = Mathf.Clamp01(threshold);
            hairSpecularSoftness = Mathf.Clamp(softness, 0.001f, 0.25f);
            hairSpecularColor = color;
        }
#endif
    }
}
