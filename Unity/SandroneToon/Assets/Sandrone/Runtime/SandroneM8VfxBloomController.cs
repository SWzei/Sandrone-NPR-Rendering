using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace SandroneToon
{
    public enum SandroneM8DebugMode
    {
        FinalColor = 0,
        EmissionMask = 1,
        HdrContribution = 2,
        BloomExtraction = 3
    }

    [ExecuteAlways, DisallowMultipleComponent]
    public sealed class SandroneM8VfxBloomController : MonoBehaviour
    {
        [SerializeField] private Renderer characterRenderer;
        [SerializeField] private int eyeMaterialIndex = 10;
        [SerializeField] private Renderer crystalRenderer;
        [SerializeField] private int crystalMaterialIndex = 1;
        [SerializeField] private GameObject crystalRoot;
        [SerializeField] private Volume bloomVolume;
        [SerializeField] private SandroneM8VfxBloomProfile profile;
        [SerializeField] private bool eyeEmissionEnabled = true;
        [SerializeField] private bool crystalEmissionEnabled = true;
        [SerializeField] private bool crystalVisible = true;
        [SerializeField] private bool bloomEnabled = true;
        [SerializeField] private SandroneM8DebugMode debugMode;

        private MaterialPropertyBlock block;
        private readonly List<Material> materials = new();
        private bool cacheValid;
        private Renderer cachedCharacterRenderer,cachedCrystalRenderer;
        private int cachedEyeMaterialIndex,cachedCrystalMaterialIndex;
        private GameObject cachedCrystalRoot;
        private Volume cachedBloomVolume;
        private bool cachedEyeEmission,cachedCrystalEmission,cachedCrystalVisible,cachedBloomEnabled;
        private SandroneM8DebugMode cachedDebugMode;
        private float cachedThreshold;
        private bool directSnapshotCaptured,snapshotCrystalActive;
        private float snapshotBloomWeight;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        [System.NonSerialized] private int auditPropertyBlockWriteCount;
#endif
        private static readonly int WeightId = Shader.PropertyToID("_M8EmissionWeight");
        private static readonly int DebugId = Shader.PropertyToID("_M8DebugMode");
        private static readonly int ThresholdId = Shader.PropertyToID("_M8BloomThreshold");

        public Renderer CharacterRenderer => characterRenderer;
        public Renderer CrystalRenderer => crystalRenderer;
        public GameObject CrystalRoot => crystalRoot;
        public Volume BloomVolume => bloomVolume;
        public SandroneM8VfxBloomProfile Profile => profile;
        public int EyeMaterialIndex => eyeMaterialIndex;
        public int CrystalMaterialIndex => crystalMaterialIndex;
        public bool EyeEmissionEnabled { get => eyeEmissionEnabled; set { eyeEmissionEnabled = value; Apply(true); } }
        public bool CrystalEmissionEnabled { get => crystalEmissionEnabled; set { crystalEmissionEnabled = value; Apply(true); } }
        public bool CrystalVisible { get => crystalVisible; set { crystalVisible = value; Apply(true); } }
        public bool BloomEnabled { get => bloomEnabled; set { bloomEnabled = value; Apply(true); } }
        public SandroneM8DebugMode DebugMode { get => debugMode; set { debugMode = value; Apply(true); } }

        public void Configure(Renderer character, int eyeSlot, Renderer crystal, int crystalSlot,
            GameObject crystalObject, Volume volume, SandroneM8VfxBloomProfile vfxProfile)
        {
            ResetOwnedState();RestoreDirectState();
            characterRenderer = character;
            eyeMaterialIndex = eyeSlot;
            crystalRenderer = crystal;
            crystalMaterialIndex = crystalSlot;
            crystalRoot = crystalObject;
            bloomVolume = volume;
            profile = vfxProfile;
            if(isActiveAndEnabled){CaptureDirectState();Apply(true);}
        }

        public void Apply(bool force = false)
        {
            var threshold=profile!=null?profile.BloomThreshold:1.1f;var desiredVolumeWeight=bloomEnabled&&debugMode==SandroneM8DebugMode.FinalColor?1f:0f;
            var directStateMatches=(crystalRoot==null||crystalRoot.activeSelf==crystalVisible)&&(bloomVolume==null||Mathf.Approximately(bloomVolume.weight,desiredVolumeWeight));
            if(!force&&cacheValid&&directStateMatches&&characterRenderer==cachedCharacterRenderer&&crystalRenderer==cachedCrystalRenderer&&eyeMaterialIndex==cachedEyeMaterialIndex&&
               crystalMaterialIndex==cachedCrystalMaterialIndex&&crystalRoot==cachedCrystalRoot&&bloomVolume==cachedBloomVolume&&eyeEmissionEnabled==cachedEyeEmission&&
               crystalEmissionEnabled==cachedCrystalEmission&&crystalVisible==cachedCrystalVisible&&bloomEnabled==cachedBloomEnabled&&debugMode==cachedDebugMode&&Mathf.Approximately(threshold,cachedThreshold))return;
            if (crystalRoot != null) crystalRoot.SetActive(crystalVisible);
            if (bloomVolume != null) bloomVolume.weight = desiredVolumeWeight;
            block ??= new MaterialPropertyBlock();
            ApplySlot(characterRenderer, eyeMaterialIndex, eyeEmissionEnabled ? 1f : 0f,threshold);
            ApplySlot(crystalRenderer, crystalMaterialIndex, crystalEmissionEnabled ? 1f : 0f,threshold);
            cacheValid=true;cachedCharacterRenderer=characterRenderer;cachedCrystalRenderer=crystalRenderer;cachedEyeMaterialIndex=eyeMaterialIndex;cachedCrystalMaterialIndex=crystalMaterialIndex;
            cachedCrystalRoot=crystalRoot;cachedBloomVolume=bloomVolume;cachedEyeEmission=eyeEmissionEnabled;cachedCrystalEmission=crystalEmissionEnabled;cachedCrystalVisible=crystalVisible;
            cachedBloomEnabled=bloomEnabled;cachedDebugMode=debugMode;cachedThreshold=threshold;
        }

        private void ApplySlot(Renderer renderer, int index, float weight,float threshold)
        {
            if (renderer == null || index < 0) return;materials.Clear();renderer.GetSharedMaterials(materials);if(index>=materials.Count)return;
            block.Clear();
            renderer.GetPropertyBlock(block, index);
            block.SetFloat(WeightId, weight);
            block.SetFloat(DebugId, (float)debugMode);
            block.SetFloat(ThresholdId, threshold);
            renderer.SetPropertyBlock(block, index);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            auditPropertyBlockWriteCount++;
#endif
        }

        private void CaptureDirectState()
        {
            if(directSnapshotCaptured)return;if(crystalRoot!=null)snapshotCrystalActive=crystalRoot.activeSelf;if(bloomVolume!=null)snapshotBloomWeight=bloomVolume.weight;directSnapshotCaptured=true;
        }
        private void RestoreDirectState()
        {
            if(!directSnapshotCaptured)return;if(crystalRoot!=null)crystalRoot.SetActive(snapshotCrystalActive);if(bloomVolume!=null)bloomVolume.weight=snapshotBloomWeight;directSnapshotCaptured=false;cacheValid=false;
        }
        private void ResetOwnedState(){ResetSlot(characterRenderer,eyeMaterialIndex);ResetSlot(crystalRenderer,crystalMaterialIndex);cacheValid=false;}
        private void ResetSlot(Renderer renderer,int index)
        {
            if(renderer==null||index<0)return;materials.Clear();renderer.GetSharedMaterials(materials);if(index>=materials.Count)return;var material=materials[index];if(material==null)return;
            block??=new MaterialPropertyBlock();block.Clear();renderer.GetPropertyBlock(block,index);
            block.SetFloat(WeightId,material.HasProperty(WeightId)?material.GetFloat(WeightId):1f);block.SetFloat(DebugId,material.HasProperty(DebugId)?material.GetFloat(DebugId):0f);
            block.SetFloat(ThresholdId,material.HasProperty(ThresholdId)?material.GetFloat(ThresholdId):1.1f);renderer.SetPropertyBlock(block,index);
        }
        private void OnEnable(){CaptureDirectState();Apply(true);}
        private void OnDisable(){ResetOwnedState();RestoreDirectState();}
        private void OnDestroy(){ResetOwnedState();RestoreDirectState();}
        private void OnValidate(){if(isActiveAndEnabled){CaptureDirectState();Apply(true);}}
        private void LateUpdate() => Apply();
    }
}
