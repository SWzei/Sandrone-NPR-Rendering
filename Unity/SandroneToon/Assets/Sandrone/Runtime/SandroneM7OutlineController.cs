using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace SandroneToon
{
    public enum SandroneM7DebugMode
    {
        FinalColor = 0,
        WidthWeight = 1,
        OutlineNormal = 2,
        MaterialSlot = 3
    }

    [ExecuteAlways, DisallowMultipleComponent]
    public sealed class SandroneM7OutlineController : MonoBehaviour
    {
        [SerializeField] private SkinnedMeshRenderer sourceRenderer;
        [SerializeField] private SkinnedMeshRenderer outlineRenderer;
        [SerializeField] private SandroneM7OutlineProfile profile;
        [SerializeField] private bool outlineEnabled = true;
        [SerializeField, Range(0f, 2f)] private float masterWidth = 1f;
        [SerializeField] private SandroneM7DebugMode debugMode;

        private MaterialPropertyBlock block;
        private readonly List<Material> materials = new();
        private bool cacheValid;
        private int cachedMaterialSignature;
        private bool cachedOutlineEnabled;
        private float cachedMasterWidth;
        private SandroneM7DebugMode cachedDebugMode;
        private bool directSnapshotCaptured,snapshotRendererEnabled,snapshotReceiveShadows;
        private ShadowCastingMode snapshotShadowCastingMode;
        private float[] snapshotBlendShapes;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        [System.NonSerialized] private int auditPropertyBlockWriteCount;
#endif
        private static readonly int MasterWeightId = Shader.PropertyToID("_OutlineMasterWeight");
        private static readonly int DebugModeId = Shader.PropertyToID("_M7DebugMode");

        public SkinnedMeshRenderer SourceRenderer => sourceRenderer;
        public SkinnedMeshRenderer OutlineRenderer => outlineRenderer;
        public SandroneM7OutlineProfile Profile => profile;
        public bool OutlineEnabled { get => outlineEnabled; set { outlineEnabled = value; Apply(true); } }
        public float MasterWidth { get => masterWidth; set { masterWidth = Mathf.Clamp(value, 0f, 2f); Apply(true); } }
        public SandroneM7DebugMode DebugMode { get => debugMode; set { debugMode = value; Apply(true); } }

        public void Configure(SkinnedMeshRenderer source, SkinnedMeshRenderer outline, SandroneM7OutlineProfile outlineProfile)
        {
            ResetOwnedState();RestoreDirectState();
            sourceRenderer = source;
            outlineRenderer = outline;
            profile = outlineProfile;
            masterWidth = profile != null ? profile.MasterWidth : 1f;
            if(isActiveAndEnabled){CaptureDirectState();Apply(true);}
        }

        public void Apply(bool force = false)
        {
            if (outlineRenderer == null) return;
            materials.Clear();outlineRenderer.GetSharedMaterials(materials);var signature=MaterialSignature(materials);
            var stateMatches=outlineRenderer.enabled==outlineEnabled&&outlineRenderer.shadowCastingMode==ShadowCastingMode.Off&&!outlineRenderer.receiveShadows;
            if(!force&&cacheValid&&stateMatches&&signature==cachedMaterialSignature&&outlineEnabled==cachedOutlineEnabled&&Mathf.Approximately(masterWidth,cachedMasterWidth)&&debugMode==cachedDebugMode){SyncBlendShapes();return;}
            outlineRenderer.enabled = outlineEnabled;
            outlineRenderer.shadowCastingMode = ShadowCastingMode.Off;
            outlineRenderer.receiveShadows = false;
            block ??= new MaterialPropertyBlock();
            for (var i = 0; i < materials.Count; i++)
            {
                block.Clear();
                outlineRenderer.GetPropertyBlock(block, i);
                block.SetFloat(MasterWeightId, outlineEnabled ? masterWidth : 0f);
                block.SetFloat(DebugModeId, (float)debugMode);
                outlineRenderer.SetPropertyBlock(block, i);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                auditPropertyBlockWriteCount++;
#endif
            }
            cacheValid=true;cachedMaterialSignature=signature;cachedOutlineEnabled=outlineEnabled;cachedMasterWidth=masterWidth;cachedDebugMode=debugMode;
            SyncBlendShapes();
        }

        private static int MaterialSignature(List<Material> values)
        {
            unchecked{var hash=17;foreach(var material in values)hash=hash*31+(material!=null?material.GetEntityId().GetHashCode():0);return hash;}
        }

        private void CaptureDirectState()
        {
            if(directSnapshotCaptured||outlineRenderer==null)return;snapshotRendererEnabled=outlineRenderer.enabled;snapshotShadowCastingMode=outlineRenderer.shadowCastingMode;snapshotReceiveShadows=outlineRenderer.receiveShadows;
            var count=outlineRenderer.sharedMesh!=null?outlineRenderer.sharedMesh.blendShapeCount:0;snapshotBlendShapes=new float[count];for(var i=0;i<count;i++)snapshotBlendShapes[i]=outlineRenderer.GetBlendShapeWeight(i);directSnapshotCaptured=true;
        }
        private void RestoreDirectState()
        {
            if(!directSnapshotCaptured||outlineRenderer==null){directSnapshotCaptured=false;return;}outlineRenderer.enabled=snapshotRendererEnabled;outlineRenderer.shadowCastingMode=snapshotShadowCastingMode;outlineRenderer.receiveShadows=snapshotReceiveShadows;
            if(snapshotBlendShapes!=null)for(var i=0;i<snapshotBlendShapes.Length;i++)outlineRenderer.SetBlendShapeWeight(i,snapshotBlendShapes[i]);directSnapshotCaptured=false;cacheValid=false;
        }
        private void ResetOwnedState()
        {
            if(outlineRenderer==null)return;materials.Clear();outlineRenderer.GetSharedMaterials(materials);block??=new MaterialPropertyBlock();
            for(var i=0;i<materials.Count;i++){var material=materials[i];if(material==null)continue;block.Clear();outlineRenderer.GetPropertyBlock(block,i);block.SetFloat(MasterWeightId,material.HasProperty(MasterWeightId)?material.GetFloat(MasterWeightId):1f);block.SetFloat(DebugModeId,material.HasProperty(DebugModeId)?material.GetFloat(DebugModeId):0f);outlineRenderer.SetPropertyBlock(block,i);}cacheValid=false;
        }

        private void SyncBlendShapes()
        {
            if (sourceRenderer == null || outlineRenderer == null || sourceRenderer.sharedMesh == null || outlineRenderer.sharedMesh == null) return;
            var count = Mathf.Min(sourceRenderer.sharedMesh.blendShapeCount, outlineRenderer.sharedMesh.blendShapeCount);
            for (var i = 0; i < count; i++) outlineRenderer.SetBlendShapeWeight(i, sourceRenderer.GetBlendShapeWeight(i));
        }

        private void OnEnable(){CaptureDirectState();Apply(true);}
        private void OnDisable(){ResetOwnedState();RestoreDirectState();}
        private void OnDestroy(){ResetOwnedState();RestoreDirectState();}
        private void OnValidate(){if(isActiveAndEnabled){CaptureDirectState();Apply(true);}}
        private void LateUpdate() => Apply();
    }
}
