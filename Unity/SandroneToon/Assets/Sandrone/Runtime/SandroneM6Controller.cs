using System.Collections.Generic;
using UnityEngine;

namespace SandroneToon
{
    public enum SandroneM6DebugMode
    {
        FinalToon = 0,
        HairControlMask = 1,
        HairTangentLobe = 2,
        HairSpecular = 3,
        EyeRole = 4,
        EyeLayerAlpha = 5,
        StyledCastShadow = 6,
        StencilRole = 7
    }

    [ExecuteAlways, DisallowMultipleComponent]
    public sealed class SandroneM6Controller : MonoBehaviour
    {
        [SerializeField] private Renderer targetRenderer;
        [SerializeField] private Transform characterRoot;
        [SerializeField] private Transform head;
        [SerializeField] private Light mainLight;
        [SerializeField] private SandroneM3ShadowProfile shadowProfile;
        [SerializeField] private SandroneM6HairEyeProfile profile;
        [SerializeField] private SandroneM6DebugMode debugMode;
        [SerializeField] private bool hairSpecularEnabled = true;
        [SerializeField] private bool eyeLayersEnabled = true;
        [SerializeField] private Vector3 headForwardLocal = Vector3.forward;
        [SerializeField] private Vector3 headRightLocal = Vector3.right;
        [SerializeField] private Vector3 headUpLocal = Vector3.up;

        private MaterialPropertyBlock propertyBlock;
        private readonly List<Material> materials = new();
        private bool cacheValid;
        private int cachedMaterialSignature;
        private SandroneM6DebugMode cachedDebugMode;
        private bool cachedHairSpecularEnabled,cachedEyeLayersEnabled;
        private float cachedCastStrength,cachedCastLow,cachedCastHigh;
        private Vector3 cachedHeadForward,cachedHeadRight,cachedHeadUp;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        [System.NonSerialized] private int auditPropertyBlockWriteCount;
#endif
        private static readonly int M6DebugId = Shader.PropertyToID("_M6DebugMode");
        private static readonly int HairWeightId = Shader.PropertyToID("_M6HairSpecWeight");
        private static readonly int EyeWeightId = Shader.PropertyToID("_M6EyeLayerWeight");
        private static readonly int CastStrengthId = Shader.PropertyToID("_CastShadowStrength");
        private static readonly int CastLowId = Shader.PropertyToID("_CastShadowLow");
        private static readonly int CastHighId = Shader.PropertyToID("_CastShadowHigh");
        private static readonly int HeadForwardId = Shader.PropertyToID("_HeadForwardWS");
        private static readonly int HeadRightId = Shader.PropertyToID("_HeadRightWS");
        private static readonly int HeadUpId = Shader.PropertyToID("_HeadUpWS");

        public Renderer TargetRenderer => targetRenderer;
        public Transform CharacterRoot => characterRoot;
        public Transform Head => head;
        public Light MainLight => mainLight;
        public SandroneM6HairEyeProfile Profile => profile;
        public SandroneM6DebugMode DebugMode { get => debugMode; set { debugMode = value; Apply(true); } }
        public bool HairSpecularEnabled { get => hairSpecularEnabled; set { hairSpecularEnabled = value; Apply(true); } }
        public bool EyeLayersEnabled { get => eyeLayersEnabled; set { eyeLayersEnabled = value; Apply(true); } }

        public void Configure(Renderer renderer, Transform root, Transform headBone, Light directionalLight,
            SandroneM3ShadowProfile shadows, SandroneM6HairEyeProfile hairEyeProfile)
        {
            targetRenderer = renderer;
            characterRoot = root;
            head = headBone;
            mainLight = directionalLight;
            shadowProfile = shadows;
            profile = hairEyeProfile;
            if (root != null && headBone != null)
            {
                headForwardLocal = headBone.InverseTransformDirection(root.forward).normalized;
                headRightLocal = headBone.InverseTransformDirection(root.right).normalized;
                headUpLocal = headBone.InverseTransformDirection(root.up).normalized;
            }
            Apply(true);
        }

        public void SetLightDirectionToSource(Vector3 directionToLightWS)
        {
            if (mainLight == null || directionToLightWS.sqrMagnitude < 1e-8f) return;
            var direction = directionToLightWS.normalized;
            var up = Mathf.Abs(Vector3.Dot(direction, Vector3.up)) > 0.99f ? Vector3.forward : Vector3.up;
            mainLight.transform.rotation = Quaternion.LookRotation(-direction, up);
        }

        public void Apply(bool force = false)
        {
            if (targetRenderer == null || shadowProfile == null) return;
            propertyBlock ??= new MaterialPropertyBlock();
            materials.Clear();targetRenderer.GetSharedMaterials(materials);
            var headForward=head!=null?head.TransformDirection(headForwardLocal).normalized:Vector3.zero;
            var headRight=head!=null?head.TransformDirection(headRightLocal).normalized:Vector3.zero;
            var headUp=head!=null?head.TransformDirection(headUpLocal).normalized:Vector3.zero;
            var materialSignature=MaterialSignature(materials);
            if(!force&&cacheValid&&materialSignature==cachedMaterialSignature&&debugMode==cachedDebugMode&&hairSpecularEnabled==cachedHairSpecularEnabled&&eyeLayersEnabled==cachedEyeLayersEnabled&&
               Mathf.Approximately(shadowProfile.CastShadowStrength,cachedCastStrength)&&Mathf.Approximately(shadowProfile.CastShadowLow,cachedCastLow)&&
               Mathf.Approximately(shadowProfile.CastShadowHigh,cachedCastHigh)&&headForward==cachedHeadForward&&headRight==cachedHeadRight&&headUp==cachedHeadUp)return;
            for (var i = 0; i < materials.Count; i++)
            {
                var material = materials[i];
                if (material == null || !material.HasProperty("_M6Role")) continue;
                var role = (SandroneM6Role)Mathf.RoundToInt(material.GetFloat("_M6Role"));
                var isEyeLayer = role == SandroneM6Role.EyeLayer;
                propertyBlock.Clear();
                targetRenderer.GetPropertyBlock(propertyBlock, i);
                propertyBlock.SetFloat(M6DebugId, (float)debugMode);
                propertyBlock.SetFloat(HairWeightId, hairSpecularEnabled ? 1f : 0f);
                propertyBlock.SetFloat(EyeWeightId, !isEyeLayer || eyeLayersEnabled ? 1f : 0f);
                propertyBlock.SetFloat(CastStrengthId, shadowProfile.CastShadowStrength);
                propertyBlock.SetFloat(CastLowId, shadowProfile.CastShadowLow);
                propertyBlock.SetFloat(CastHighId, shadowProfile.CastShadowHigh);
                if (head != null)
                {
                    propertyBlock.SetVector(HeadForwardId, headForward);
                    propertyBlock.SetVector(HeadRightId, headRight);
                    propertyBlock.SetVector(HeadUpId, headUp);
                }
                targetRenderer.SetPropertyBlock(propertyBlock, i);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                auditPropertyBlockWriteCount++;
#endif
            }
            cacheValid=true;cachedMaterialSignature=materialSignature;cachedDebugMode=debugMode;cachedHairSpecularEnabled=hairSpecularEnabled;cachedEyeLayersEnabled=eyeLayersEnabled;
            cachedCastStrength=shadowProfile.CastShadowStrength;cachedCastLow=shadowProfile.CastShadowLow;cachedCastHigh=shadowProfile.CastShadowHigh;cachedHeadForward=headForward;cachedHeadRight=headRight;cachedHeadUp=headUp;
        }

        private static int MaterialSignature(List<Material> values)
        {
            unchecked{var hash=17;foreach(var material in values){hash=hash*31+(material!=null?material.GetEntityId().GetHashCode():0);if(material!=null&&material.HasProperty("_M6Role"))hash=hash*31+material.GetFloat("_M6Role").GetHashCode();}return hash;}
        }

        private void OnEnable() => Apply(true);
        private void OnValidate() => Apply(true);
        private void LateUpdate() => Apply();
    }
}
