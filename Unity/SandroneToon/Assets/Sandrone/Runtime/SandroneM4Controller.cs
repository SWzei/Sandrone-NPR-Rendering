using UnityEngine;
using System.Collections.Generic;

namespace SandroneToon
{
    public enum SandroneM4DebugMode
    {
        FinalToon = 0, ControlR = 1, ControlG = 2, ControlB = 3, ControlA = 4,
        NDotH = 5, Specular = 6, MatCapUV = 7, MatCapSample = 8,
        MaterialResponse = 9, FinalLitMask = 10, Silhouette = 11
    }

    [ExecuteAlways, DisallowMultipleComponent]
    public sealed class SandroneM4Controller : MonoBehaviour
    {
        [SerializeField] private Renderer targetRenderer;
        [SerializeField] private Transform characterRoot;
        [SerializeField] private Transform head;
        [SerializeField] private Light mainLight;
        [SerializeField] private SandroneM3ShadowProfile shadowProfile;
        [SerializeField] private SandroneM4DebugMode debugMode;
        [SerializeField] private bool metalEnabled = true;
        [SerializeField] private bool stockingEnabled = true;
        [SerializeField] private bool hairOverlayEnabled = true;
        [SerializeField] private Vector3 headForwardLocal = Vector3.forward;
        [SerializeField] private Vector3 headRightLocal = Vector3.right;
        [SerializeField] private Vector3 headUpLocal = Vector3.up;

        private static readonly int DebugId = Shader.PropertyToID("_M4DebugMode");
        private static readonly int FeatureWeightId = Shader.PropertyToID("_M4FeatureWeight");
        private static readonly int CastStrengthId = Shader.PropertyToID("_CastShadowStrength");
        private static readonly int CastLowId = Shader.PropertyToID("_CastShadowLow");
        private static readonly int CastHighId = Shader.PropertyToID("_CastShadowHigh");
        private static readonly int HeadForwardId = Shader.PropertyToID("_HeadForwardWS");
        private static readonly int HeadRightId = Shader.PropertyToID("_HeadRightWS");
        private static readonly int HeadUpId = Shader.PropertyToID("_HeadUpWS");
        private readonly Dictionary<int, float> slotFeatureWeights = new();
        private readonly List<Material> materials = new();
        private MaterialPropertyBlock propertyBlock;
        private bool cacheValid;
        private int cachedMaterialSignature;
        private SandroneM4DebugMode cachedDebugMode;
        private bool cachedMetalEnabled,cachedStockingEnabled,cachedHairOverlayEnabled;
        private float cachedCastStrength,cachedCastLow,cachedCastHigh;
        private Vector3 cachedHeadForward,cachedHeadRight,cachedHeadUp;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        [System.NonSerialized] private int auditPropertyBlockWriteCount;
#endif

        public Renderer TargetRenderer => targetRenderer;
        public Transform CharacterRoot => characterRoot;
        public Transform Head => head;
        public Light MainLight => mainLight;
        public SandroneM4DebugMode DebugMode { get => debugMode; set { debugMode = value; Apply(true); } }
        public bool MetalEnabled { get => metalEnabled; set { metalEnabled = value; Apply(true); } }
        public bool StockingEnabled { get => stockingEnabled; set { stockingEnabled = value; Apply(true); } }
        public bool HairOverlayEnabled { get => hairOverlayEnabled; set { hairOverlayEnabled = value; Apply(true); } }

        public void Configure(Renderer renderer, Transform root, Transform headBone, Light directionalLight,
            SandroneM3ShadowProfile profile)
        {
            targetRenderer = renderer; characterRoot = root; head = headBone; mainLight = directionalLight;
            shadowProfile = profile;
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
            materials.Clear();targetRenderer.GetSharedMaterials(materials);
            var headForward=head!=null?head.TransformDirection(headForwardLocal).normalized:Vector3.zero;
            var headRight=head!=null?head.TransformDirection(headRightLocal).normalized:Vector3.zero;
            var headUp=head!=null?head.TransformDirection(headUpLocal).normalized:Vector3.zero;
            var materialSignature=MaterialSignature(materials);
            if(!force&&cacheValid&&materialSignature==cachedMaterialSignature&&debugMode==cachedDebugMode&&metalEnabled==cachedMetalEnabled&&
               stockingEnabled==cachedStockingEnabled&&hairOverlayEnabled==cachedHairOverlayEnabled&&
               Mathf.Approximately(shadowProfile.CastShadowStrength,cachedCastStrength)&&Mathf.Approximately(shadowProfile.CastShadowLow,cachedCastLow)&&
               Mathf.Approximately(shadowProfile.CastShadowHigh,cachedCastHigh)&&headForward==cachedHeadForward&&headRight==cachedHeadRight&&headUp==cachedHeadUp)return;
            propertyBlock ??= new MaterialPropertyBlock();
            for (var materialIndex = 0; materialIndex < materials.Count; materialIndex++)
            {
                var material = materials[materialIndex];
                if(material==null || !material.HasProperty("_FeatureGroup")) continue;
                var feature=Mathf.RoundToInt(material.GetFloat("_FeatureGroup"));
                var featureWeight = 1f;
                if(feature==(int)SandroneM4FeatureGroup.Metal && !metalEnabled) featureWeight = 0f;
                else if(feature==(int)SandroneM4FeatureGroup.StockingOverlay && !stockingEnabled) featureWeight = 0f;
                else if(feature==(int)SandroneM4FeatureGroup.HairOverlay && !hairOverlayEnabled) featureWeight = 0f;
                if(slotFeatureWeights.TryGetValue(materialIndex,out var slotWeight)) featureWeight *= Mathf.Clamp01(slotWeight);

                // Preserve M0 layer weights and any earlier-phase data already stored
                // in a material-index property block.
                propertyBlock.Clear();
                targetRenderer.GetPropertyBlock(propertyBlock,materialIndex);
                propertyBlock.SetFloat(DebugId,(float)debugMode);
                propertyBlock.SetFloat(FeatureWeightId,featureWeight);
                propertyBlock.SetFloat(CastStrengthId,shadowProfile.CastShadowStrength);
                propertyBlock.SetFloat(CastLowId,shadowProfile.CastShadowLow);
                propertyBlock.SetFloat(CastHighId,shadowProfile.CastShadowHigh);
                if(head!=null)
                {
                    propertyBlock.SetVector(HeadForwardId,headForward);
                    propertyBlock.SetVector(HeadRightId,headRight);
                    propertyBlock.SetVector(HeadUpId,headUp);
                }
                targetRenderer.SetPropertyBlock(propertyBlock,materialIndex);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                auditPropertyBlockWriteCount++;
#endif
            }
            cacheValid=true;cachedMaterialSignature=materialSignature;cachedDebugMode=debugMode;cachedMetalEnabled=metalEnabled;cachedStockingEnabled=stockingEnabled;cachedHairOverlayEnabled=hairOverlayEnabled;
            cachedCastStrength=shadowProfile.CastShadowStrength;cachedCastLow=shadowProfile.CastShadowLow;cachedCastHigh=shadowProfile.CastShadowHigh;cachedHeadForward=headForward;cachedHeadRight=headRight;cachedHeadUp=headUp;
        }

        private static int MaterialSignature(List<Material> values)
        {
            unchecked{var hash=17;foreach(var material in values){hash=hash*31+(material!=null?material.GetEntityId().GetHashCode():0);if(material!=null&&material.HasProperty("_FeatureGroup"))hash=hash*31+material.GetFloat("_FeatureGroup").GetHashCode();}return hash;}
        }

        public void SetMaterialSlotFeatureWeight(int materialIndex,float weight)
        {
            if(materialIndex<0 || targetRenderer==null || materialIndex>=targetRenderer.sharedMaterials.Length)
                throw new System.ArgumentOutOfRangeException(nameof(materialIndex));
            slotFeatureWeights[materialIndex]=Mathf.Clamp01(weight);
            Apply(true);
        }

        public void ClearMaterialSlotFeatureWeights()
        {
            slotFeatureWeights.Clear();
            Apply(true);
        }

        private void OnEnable() => Apply(true);
        private void OnValidate() => Apply(true);
        private void LateUpdate() => Apply();
    }
}
