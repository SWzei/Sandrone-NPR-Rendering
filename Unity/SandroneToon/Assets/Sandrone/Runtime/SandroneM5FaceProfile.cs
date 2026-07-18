using UnityEngine;

namespace SandroneToon
{
    [CreateAssetMenu(menuName = "Sandrone/M5 Face Profile", fileName = "SandroneFaceProfile_M5")]
    public sealed class SandroneM5FaceProfile : ScriptableObject
    {
        [SerializeField] private string contractVersion = "SandroneFaceProfile_v1_M5";
        [SerializeField] private Texture2D faceMap;
        [SerializeField] private int[] faceMaterialIndices = { 0, 1 };
        [SerializeField, Range(0f, 0.1f)] private float softness = 0.02f;
        [SerializeField, Range(0f, 4f)] private float derivativeAA = 1f;
        [SerializeField, Range(0.001f, 0.25f)] private float mirrorBlendWidth = 0.10f;

        public string ContractVersion => contractVersion;
        public Texture2D FaceMap => faceMap;
        public int[] FaceMaterialIndices => faceMaterialIndices;
        public float Softness => softness;
        public float DerivativeAA => derivativeAA;
        public float MirrorBlendWidth => mirrorBlendWidth;

#if UNITY_EDITOR
        public void EditorSet(Texture2D map, int[] indices, float faceSoftness, float faceAA, float faceMirrorBlendWidth = 0.10f)
        {
            contractVersion = "SandroneFaceProfile_v1_M5";
            faceMap = map;
            faceMaterialIndices = indices ?? new[] { 0, 1 };
            softness = Mathf.Clamp(faceSoftness, 0f, 0.1f);
            derivativeAA = Mathf.Clamp(faceAA, 0f, 4f);
            mirrorBlendWidth = Mathf.Clamp(faceMirrorBlendWidth, 0.001f, 0.25f);
        }
#endif
    }
}
