using System;
using UnityEngine;

namespace SandroneToon
{
    [Serializable]
    public struct SandroneM7OutlineSlot
    {
        public int materialIndex;
        [Range(0f, 3f)] public float widthPixels;
        public Color color;
    }

    [CreateAssetMenu(menuName = "Sandrone/M7 Outline Profile", fileName = "SandroneOutlineProfile_M7")]
    public sealed class SandroneM7OutlineProfile : ScriptableObject
    {
        [SerializeField] private string contractVersion = "SandroneOutlineProfile_v1_M7";
        [SerializeField] private string normalSource = "GeneratedCoincidentPositionAverage_v1";
        [SerializeField, Range(0f, 2f)] private float masterWidth = 1f;
        [SerializeField] private SandroneM7OutlineSlot[] slots = Array.Empty<SandroneM7OutlineSlot>();

        public string ContractVersion => contractVersion;
        public string NormalSource => normalSource;
        public float MasterWidth => masterWidth;
        public SandroneM7OutlineSlot[] Slots => slots;

        public bool TryGet(int materialIndex, out SandroneM7OutlineSlot slot)
        {
            foreach (var item in slots)
            {
                if (item.materialIndex != materialIndex) continue;
                slot = item;
                return true;
            }
            slot = default;
            return false;
        }

#if UNITY_EDITOR
        public void EditorSet(float width, SandroneM7OutlineSlot[] entries)
        {
            contractVersion = "SandroneOutlineProfile_v1_M7";
            normalSource = "GeneratedCoincidentPositionAverage_v1";
            masterWidth = Mathf.Clamp(width, 0f, 2f);
            slots = entries ?? Array.Empty<SandroneM7OutlineSlot>();
        }
#endif
    }
}
