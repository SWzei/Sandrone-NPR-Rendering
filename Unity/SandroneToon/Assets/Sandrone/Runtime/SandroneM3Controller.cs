using UnityEngine;

namespace SandroneToon
{
    public enum SandroneM3DebugMode
    {
        FinalToon = 0,
        CastShadowRaw = 1,
        CastShadowStyled = 2,
        FormBand = 3,
        FinalLitMask = 4,
        RampSample = 5,
        CascadeIndex = 6,
        Silhouette = 7
    }

    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class SandroneM3Controller : MonoBehaviour
    {
        [SerializeField] private Renderer targetRenderer;
        [SerializeField] private Transform characterRoot;
        [SerializeField] private Transform head;
        [SerializeField] private Light mainLight;
        [SerializeField] private SandroneM3ShadowProfile shadowProfile;
        [SerializeField] private SandroneM3DebugMode debugMode;

        [Header("Calibrated semantic axes in the head bone local space")]
        [SerializeField] private Vector3 headForwardLocal = Vector3.forward;
        [SerializeField] private Vector3 headRightLocal = Vector3.right;
        [SerializeField] private Vector3 headUpLocal = Vector3.up;

        private MaterialPropertyBlock propertyBlock;
        private Vector3 cachedForward;
        private Vector3 cachedRight;
        private Vector3 cachedUp;
        private SandroneM3DebugMode cachedDebugMode = (SandroneM3DebugMode)(-1);
        private SandroneM3ShadowProfile cachedProfile;
        private float cachedCastShadowStrength = float.NaN;
        private float cachedCastShadowLow = float.NaN;
        private float cachedCastShadowHigh = float.NaN;

        private static readonly int DebugModeId = Shader.PropertyToID("_M3DebugMode");
        private static readonly int CastShadowStrengthId = Shader.PropertyToID("_CastShadowStrength");
        private static readonly int CastShadowLowId = Shader.PropertyToID("_CastShadowLow");
        private static readonly int CastShadowHighId = Shader.PropertyToID("_CastShadowHigh");
        private static readonly int HeadForwardId = Shader.PropertyToID("_HeadForwardWS");
        private static readonly int HeadRightId = Shader.PropertyToID("_HeadRightWS");
        private static readonly int HeadUpId = Shader.PropertyToID("_HeadUpWS");

        public Renderer TargetRenderer => targetRenderer;
        public Transform CharacterRoot => characterRoot;
        public Transform Head => head;
        public Light MainLight => mainLight;
        public SandroneM3ShadowProfile ShadowProfile => shadowProfile;
        public Vector3 HeadForwardLocal => headForwardLocal;
        public Vector3 HeadRightLocal => headRightLocal;
        public Vector3 HeadUpLocal => headUpLocal;
        public Vector3 HeadForwardWS => TransformHeadAxis(headForwardLocal);
        public Vector3 HeadRightWS => TransformHeadAxis(headRightLocal);
        public Vector3 HeadUpWS => TransformHeadAxis(headUpLocal);
        public Vector3 MainLightDirectionWS => mainLight != null ? -mainLight.transform.forward : Vector3.zero;

        public SandroneM3DebugMode DebugMode
        {
            get => debugMode;
            set
            {
                if (debugMode == value)
                {
                    return;
                }
                debugMode = value;
                Apply(true);
            }
        }

        public void Configure(Renderer renderer, Transform root, Transform headBone, Light directionalLight,
            SandroneM3ShadowProfile profile)
        {
            targetRenderer = renderer;
            characterRoot = root;
            head = headBone;
            mainLight = directionalLight;
            shadowProfile = profile;
            if (characterRoot != null && head != null)
            {
                headForwardLocal = head.InverseTransformDirection(characterRoot.forward).normalized;
                headRightLocal = head.InverseTransformDirection(characterRoot.right).normalized;
                headUpLocal = head.InverseTransformDirection(characterRoot.up).normalized;
            }
            Apply(true);
        }

        public void SetLightDirectionToSource(Vector3 directionToLightWS)
        {
            if (mainLight == null || directionToLightWS.sqrMagnitude < 1e-8f)
            {
                return;
            }
            var direction = directionToLightWS.normalized;
            var up = Mathf.Abs(Vector3.Dot(direction, Vector3.up)) > 0.99f ? Vector3.forward : Vector3.up;
            mainLight.transform.rotation = Quaternion.LookRotation(-direction, up);
        }

        public void Apply(bool force = false)
        {
            if (targetRenderer == null || head == null || shadowProfile == null)
            {
                return;
            }

            var forward = HeadForwardWS;
            var right = HeadRightWS;
            var up = HeadUpWS;
            if (!force && debugMode == cachedDebugMode && shadowProfile == cachedProfile &&
                Mathf.Approximately(shadowProfile.CastShadowStrength, cachedCastShadowStrength) &&
                Mathf.Approximately(shadowProfile.CastShadowLow, cachedCastShadowLow) &&
                Mathf.Approximately(shadowProfile.CastShadowHigh, cachedCastShadowHigh) &&
                Vector3.SqrMagnitude(forward - cachedForward) < 1e-10f &&
                Vector3.SqrMagnitude(right - cachedRight) < 1e-10f &&
                Vector3.SqrMagnitude(up - cachedUp) < 1e-10f)
            {
                return;
            }

            // M0 owns per-material blocks on slots 9 and 30. A renderer-wide block is
            // ignored when a per-material block exists, so M3 must update every slot
            // while preserving fields written by earlier phases.
            propertyBlock ??= new MaterialPropertyBlock();
            var materialCount = targetRenderer.sharedMaterials.Length;
            for (var materialIndex = 0; materialIndex < materialCount; materialIndex++)
            {
                propertyBlock.Clear();
                targetRenderer.GetPropertyBlock(propertyBlock, materialIndex);
                propertyBlock.SetFloat(DebugModeId, (float)debugMode);
                propertyBlock.SetFloat(CastShadowStrengthId, shadowProfile.CastShadowStrength);
                propertyBlock.SetFloat(CastShadowLowId, shadowProfile.CastShadowLow);
                propertyBlock.SetFloat(CastShadowHighId, shadowProfile.CastShadowHigh);
                propertyBlock.SetVector(HeadForwardId, forward);
                propertyBlock.SetVector(HeadRightId, right);
                propertyBlock.SetVector(HeadUpId, up);
                targetRenderer.SetPropertyBlock(propertyBlock, materialIndex);
            }

            cachedDebugMode = debugMode;
            cachedProfile = shadowProfile;
            cachedCastShadowStrength = shadowProfile.CastShadowStrength;
            cachedCastShadowLow = shadowProfile.CastShadowLow;
            cachedCastShadowHigh = shadowProfile.CastShadowHigh;
            cachedForward = forward;
            cachedRight = right;
            cachedUp = up;
        }

        private Vector3 TransformHeadAxis(Vector3 localAxis)
        {
            return head != null ? head.TransformDirection(localAxis).normalized : Vector3.zero;
        }

        private void OnEnable() => Apply(true);
        private void OnValidate() => Apply(true);
        private void LateUpdate() => Apply();
    }
}
