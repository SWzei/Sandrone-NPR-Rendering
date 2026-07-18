using System;
using UnityEngine;

namespace SandroneToon
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class SandroneM0Controller : MonoBehaviour
    {
        [Serializable]
        public sealed class LayerBinding
        {
            public Renderer renderer;
            public int materialIndex;
        }

        [Header("PMX material morph replacements")]
        [Range(0f, 1f)] public float eyeALWeight;
        [Range(0f, 1f)] public float blushWeight;
        [SerializeField] private LayerBinding eyeAL;
        [SerializeField] private LayerBinding blush;

        [Header("PMX bone morph replacement")]
        [Range(0f, 1f)] public float clockworkRotationWeight;
        [SerializeField] private Transform clockworkTarget;
        [SerializeField] private Quaternion clockworkBaseLocalRotation = Quaternion.identity;
        [SerializeField] private Quaternion clockworkDelta = new(-0.0000011176f, -0.02393058f, -0.0002069324f, 0.9997137f);

        private MaterialPropertyBlock propertyBlock;
        private static readonly int LayerWeightId = Shader.PropertyToID("_LayerWeight");

        public Transform ClockworkTarget => clockworkTarget;
        public LayerBinding EyeALBinding => eyeAL;
        public LayerBinding BlushBinding => blush;

        public void Configure(LayerBinding eyeALBinding, LayerBinding blushBinding, Transform target)
        {
            eyeAL = eyeALBinding;
            blush = blushBinding;
            clockworkTarget = target;
            if (clockworkTarget != null)
            {
                clockworkBaseLocalRotation = clockworkTarget.localRotation;
            }
            Apply();
        }

        public void Apply()
        {
            ApplyLayer(eyeAL, eyeALWeight);
            ApplyLayer(blush, blushWeight);
            if (clockworkTarget != null)
            {
                clockworkTarget.localRotation = clockworkBaseLocalRotation * Quaternion.SlerpUnclamped(Quaternion.identity, clockworkDelta, clockworkRotationWeight);
            }
        }

        private void OnEnable() => Apply();
        private void OnValidate() => Apply();

        private void ApplyLayer(LayerBinding binding, float weight)
        {
            if (binding?.renderer == null || binding.materialIndex < 0 || binding.materialIndex >= binding.renderer.sharedMaterials.Length)
            {
                return;
            }
            propertyBlock ??= new MaterialPropertyBlock();
            binding.renderer.GetPropertyBlock(propertyBlock, binding.materialIndex);
            propertyBlock.SetFloat(LayerWeightId, weight);
            binding.renderer.SetPropertyBlock(propertyBlock, binding.materialIndex);
            propertyBlock.Clear();
        }
    }
}
