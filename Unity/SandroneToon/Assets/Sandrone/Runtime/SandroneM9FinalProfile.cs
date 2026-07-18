using UnityEngine;

namespace SandroneToon
{
    [CreateAssetMenu(menuName = "Sandrone/M9 Final Profile", fileName = "SandroneFinalProfile_M9")]
    public sealed class SandroneM9FinalProfile : ScriptableObject
    {
        [SerializeField] private string contractVersion = "SandroneFinalProfile_v1_M9";
        [SerializeField] private string toneMapping = "Neutral";
        [SerializeField, Range(-2f, 2f)] private float postExposure = -0.08f;
        [SerializeField, Range(-100f, 100f)] private float contrast;
        [SerializeField, Range(-180f, 180f)] private float hueShift;
        [SerializeField, Range(-100f, 100f)] private float saturation = -18f;
        [SerializeField] private Color colorFilter = Color.white;
        [SerializeField] private string desktopAntiAliasing = "SMAA High";
        [SerializeField] private string mobileAntiAliasing = "FXAA";
        [SerializeField] private string taaDecision = "Deferred_NoMotionVectorAndOutlineStabilityEvidence";

        public string ContractVersion => contractVersion;
        public string ToneMapping => toneMapping;
        public float PostExposure => postExposure;
        public float Contrast => contrast;
        public float HueShift => hueShift;
        public float Saturation => saturation;
        public Color ColorFilter => colorFilter;
        public string DesktopAntiAliasing => desktopAntiAliasing;
        public string MobileAntiAliasing => mobileAntiAliasing;
        public string TaaDecision => taaDecision;

#if UNITY_EDITOR
        public void EditorSet(float exposure, float contrastValue, float hue, float saturationValue, Color filter)
        {
            contractVersion = "SandroneFinalProfile_v1_M9";
            toneMapping = "Neutral";
            postExposure = Mathf.Clamp(exposure, -2f, 2f);
            contrast = Mathf.Clamp(contrastValue, -100f, 100f);
            hueShift = Mathf.Clamp(hue, -180f, 180f);
            saturation = Mathf.Clamp(saturationValue, -100f, 100f);
            colorFilter = filter;
            desktopAntiAliasing = "SMAA High";
            mobileAntiAliasing = "FXAA";
            taaDecision = "Deferred_NoMotionVectorAndOutlineStabilityEvidence";
        }
#endif
    }
}
