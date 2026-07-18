using UnityEngine;

namespace SandroneToon
{
    [CreateAssetMenu(menuName = "Sandrone/M8 VFX Bloom Profile", fileName = "SandroneVfxBloomProfile_M8")]
    public sealed class SandroneM8VfxBloomProfile : ScriptableObject
    {
        [SerializeField] private string contractVersion = "SandroneVfxBloomProfile_v1_M8";
        [SerializeField] private string selectedModules = "EyeLight+CrystallineSword";
        [Header("Eye Light")]
        [SerializeField] private Color eyeEmissionColor = new(0.08f, 0.55f, 1f, 1f);
        [SerializeField, Range(0f, 8f)] private float eyeEmissionIntensity = 3.2f;
        [Header("Crystal")]
        [SerializeField] private Color crystalEmissionColor = new(0.05f, 0.72f, 1f, 1f);
        [SerializeField, Range(0f, 8f)] private float crystalEmissionIntensity = 2.8f;
        [SerializeField, Range(0f, 2f)] private float crystalFresnelIntensity = 0.55f;
        [SerializeField, Range(0.25f, 8f)] private float crystalFresnelPower = 3f;
        [Header("URP Bloom")]
        [SerializeField, Range(0f, 4f)] private float bloomThreshold = 1.1f;
        [SerializeField, Range(0f, 2f)] private float bloomIntensity = 0.35f;
        [SerializeField, Range(0f, 1f)] private float bloomScatter = 0.55f;
        [SerializeField, Range(1f, 32f)] private float bloomClamp = 8f;

        public string ContractVersion => contractVersion;
        public string SelectedModules => selectedModules;
        public Color EyeEmissionColor => eyeEmissionColor;
        public float EyeEmissionIntensity => eyeEmissionIntensity;
        public Color CrystalEmissionColor => crystalEmissionColor;
        public float CrystalEmissionIntensity => crystalEmissionIntensity;
        public float CrystalFresnelIntensity => crystalFresnelIntensity;
        public float CrystalFresnelPower => crystalFresnelPower;
        public float BloomThreshold => bloomThreshold;
        public float BloomIntensity => bloomIntensity;
        public float BloomScatter => bloomScatter;
        public float BloomClamp => bloomClamp;

#if UNITY_EDITOR
        public void EditorSet(Color eyeColor, float eyeIntensity, Color crystalColor, float crystalIntensity,
            float fresnelIntensity, float fresnelPower, float threshold, float bloomIntensityValue,
            float scatter, float clamp)
        {
            contractVersion = "SandroneVfxBloomProfile_v1_M8";
            selectedModules = "EyeLight+CrystallineSword";
            eyeEmissionColor = eyeColor;
            eyeEmissionIntensity = Mathf.Clamp(eyeIntensity, 0f, 8f);
            crystalEmissionColor = crystalColor;
            crystalEmissionIntensity = Mathf.Clamp(crystalIntensity, 0f, 8f);
            crystalFresnelIntensity = Mathf.Clamp(fresnelIntensity, 0f, 2f);
            crystalFresnelPower = Mathf.Clamp(fresnelPower, .25f, 8f);
            bloomThreshold = Mathf.Clamp(threshold, 0f, 4f);
            bloomIntensity = Mathf.Clamp(bloomIntensityValue, 0f, 2f);
            bloomScatter = Mathf.Clamp01(scatter);
            bloomClamp = Mathf.Clamp(clamp, 1f, 32f);
        }
#endif
    }
}
