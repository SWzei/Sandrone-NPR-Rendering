using System;
using System.Collections.Generic;
using UnityEngine;

namespace SandroneToon
{
    public enum SandroneRampFamily
    {
        SkinFace = 0,
        LightCloth = 1,
        DarkClothHair = 2,
        Metal = 3,
        Eye = 4
    }

    [CreateAssetMenu(menuName = "Sandrone/M2 Ramp Profile", fileName = "SandroneRampProfile_M2")]
    public sealed class SandroneM2RampProfile : ScriptableObject
    {
        [Serializable]
        public sealed class Row
        {
            public SandroneRampFamily family;
            public string displayName;
            public Color shadowMultiplier = Color.white;
            public Color litMultiplier = Color.white;
            [Range(0f, 1f)] public float threshold = 0.5f;
            public int[] materialIndices = Array.Empty<int>();
        }

        [SerializeField] private string contractVersion = "SandroneRampProfile_v1_M2";
        [SerializeField] private Texture2D rampTexture;
        [SerializeField] private List<Row> rows = new();

        public string ContractVersion => contractVersion;
        public Texture2D RampTexture => rampTexture;
        public IReadOnlyList<Row> Rows => rows;

#if UNITY_EDITOR
        public void EditorSet(Texture2D texture, List<Row> value)
        {
            rampTexture = texture;
            rows = value;
        }
#endif
    }
}
