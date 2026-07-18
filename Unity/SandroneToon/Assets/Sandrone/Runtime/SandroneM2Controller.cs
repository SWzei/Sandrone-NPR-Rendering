using UnityEngine;

namespace SandroneToon
{
    public enum SandroneM2DebugMode
    {
        FinalToon = 0,
        HalfLambert = 1,
        BandMask = 2,
        RampUV = 3,
        RampSample = 4,
        NdotV = 5,
        HeadAxis = 6,
        Silhouette = 7
    }

    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class SandroneM2Controller : MonoBehaviour
    {
        [SerializeField] private Renderer targetRenderer;
        [SerializeField] private Transform characterRoot;
        [SerializeField] private Transform head;
        [SerializeField] private Light mainLight;
        [SerializeField] private SandroneM2DebugMode debugMode;

        [Header("Calibrated semantic axes in the head bone local space")]
        [SerializeField] private Vector3 headForwardLocal = Vector3.forward;
        [SerializeField] private Vector3 headRightLocal = Vector3.right;
        [SerializeField] private Vector3 headUpLocal = Vector3.up;

        private MaterialPropertyBlock propertyBlock;
        private Vector3 cachedForward;
        private Vector3 cachedRight;
        private Vector3 cachedUp;
        private SandroneM2DebugMode cachedDebugMode = (SandroneM2DebugMode)(-1);

        private static readonly int DebugModeId = Shader.PropertyToID("_M2DebugMode");
        private static readonly int HeadForwardId = Shader.PropertyToID("_HeadForwardWS");
        private static readonly int HeadRightId = Shader.PropertyToID("_HeadRightWS");
        private static readonly int HeadUpId = Shader.PropertyToID("_HeadUpWS");

        public Renderer TargetRenderer => targetRenderer;
        public Transform CharacterRoot => characterRoot;
        public Transform Head => head;
        public Light MainLight => mainLight;
        public Vector3 HeadForwardLocal => headForwardLocal;
        public Vector3 HeadRightLocal => headRightLocal;
        public Vector3 HeadUpLocal => headUpLocal;
        public Vector3 HeadForwardWS => TransformHeadAxis(headForwardLocal);
        public Vector3 HeadRightWS => TransformHeadAxis(headRightLocal);
        public Vector3 HeadUpWS => TransformHeadAxis(headUpLocal);
        public Vector3 MainLightDirectionWS => mainLight != null ? -mainLight.transform.forward : Vector3.zero;

        public SandroneM2DebugMode DebugMode
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

        public void Configure(Renderer renderer, Transform root, Transform headBone, Light directionalLight)
        {
            targetRenderer = renderer;
            characterRoot = root;
            head = headBone;
            mainLight = directionalLight;
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
            if (targetRenderer == null || head == null)
            {
                return;
            }

            var forward = HeadForwardWS;
            var right = HeadRightWS;
            var up = HeadUpWS;
            if (!force && debugMode == cachedDebugMode &&
                Vector3.SqrMagnitude(forward - cachedForward) < 1e-10f &&
                Vector3.SqrMagnitude(right - cachedRight) < 1e-10f &&
                Vector3.SqrMagnitude(up - cachedUp) < 1e-10f)
            {
                return;
            }

            propertyBlock ??= new MaterialPropertyBlock();
            for (var materialIndex = 0; materialIndex < targetRenderer.sharedMaterials.Length; materialIndex++)
            {
                targetRenderer.GetPropertyBlock(propertyBlock, materialIndex);
                propertyBlock.SetFloat(DebugModeId, (float)debugMode);
                propertyBlock.SetVector(HeadForwardId, new Vector4(forward.x, forward.y, forward.z, 0f));
                propertyBlock.SetVector(HeadRightId, new Vector4(right.x, right.y, right.z, 0f));
                propertyBlock.SetVector(HeadUpId, new Vector4(up.x, up.y, up.z, 0f));
                targetRenderer.SetPropertyBlock(propertyBlock, materialIndex);
                propertyBlock.Clear();
            }

            cachedForward = forward;
            cachedRight = right;
            cachedUp = up;
            cachedDebugMode = debugMode;
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
