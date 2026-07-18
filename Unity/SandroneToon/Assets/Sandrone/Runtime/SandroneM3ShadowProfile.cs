using UnityEngine;

namespace SandroneToon
{
    [CreateAssetMenu(menuName = "Sandrone/M3 Shadow Profile", fileName = "SandroneShadowProfile_M3")]
    public sealed class SandroneM3ShadowProfile : ScriptableObject
    {
        [SerializeField] private string contractVersion = "SandroneShadowProfile_v1_M3";
        [Range(0f, 1f)] [SerializeField] private float castShadowStrength = 0.85f;
        [Range(0f, 1f)] [SerializeField] private float castShadowLow = 0.20f;
        [Range(0f, 1f)] [SerializeField] private float castShadowHigh = 0.80f;

        public string ContractVersion => contractVersion;
        public float CastShadowStrength => castShadowStrength;
        public float CastShadowLow => castShadowLow;
        public float CastShadowHigh => castShadowHigh;

#if UNITY_EDITOR
        public void EditorSet(float strength, float low, float high)
        {
            contractVersion = "SandroneShadowProfile_v1_M3";
            castShadowStrength = Mathf.Clamp01(strength);
            castShadowLow = Mathf.Clamp01(low);
            castShadowHigh = Mathf.Clamp01(high);
        }
#endif
    }
}
