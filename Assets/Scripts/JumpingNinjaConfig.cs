using UnityEngine;

namespace JumpingNinja
{
    [CreateAssetMenu(fileName = "JumpingNinjaConfig", menuName = "Jumping Ninja/V1 Config")]
    public sealed class JumpingNinjaConfig : ScriptableObject
    {
        [Header("Brand")]
        public Sprite logo;

        [Header("Ninja Appearance")]
        public Sprite ninjaSprite;

        [Header("World Appearance")]
        public Sprite backgroundPatternSprite;
        public Sprite hazardBlockSprite;
        public Sprite wallBlockSprite;
        [Min(1)] public int visualThemeLayerInterval = 10;
        [Range(0f, 0.5f)] public float backgroundPatternOpacity = 0.18f;
        public Color[] backgroundThemeColors =
        {
            new Color(0.22f, 0.30f, 0.40f, 1f),
            new Color(0.29f, 0.23f, 0.39f, 1f),
            new Color(0.18f, 0.34f, 0.32f, 1f),
            new Color(0.40f, 0.28f, 0.22f, 1f),
            new Color(0.20f, 0.30f, 0.23f, 1f),
            new Color(0.35f, 0.21f, 0.28f, 1f)
        };
        public Color[] hazardThemeTints =
        {
            new Color(0.72f, 0.80f, 0.96f, 1f),
            new Color(0.86f, 0.72f, 0.96f, 1f),
            new Color(0.66f, 0.92f, 0.86f, 1f),
            new Color(0.96f, 0.78f, 0.64f, 1f),
            new Color(0.72f, 0.90f, 0.70f, 1f),
            new Color(0.94f, 0.68f, 0.76f, 1f)
        };

        [Header("Ninja Movement")]
        [Range(1f, 45f)] public float steeringAngle = 15f;
        [Min(1f)] public float jumpSpeed = 12f;
        [Min(0.1f)] public float gravityScale = 2.8f;
        [Min(0.1f)] public float playerSize = 1f;

        [Header("Ninja Animation")]
        [Min(0.05f)] public float jumpAnimationDuration = 0.24f;
        [Range(0f, 0.4f)] public float jumpStretch = 0.18f;
        [Min(0.1f)] public float deathAnimationDuration = 0.75f;
        [Range(0f, 0.3f)] public float deathShake = 0.12f;

        [Header("Infinite Map")]
        [Min(8)] public int mapWidth = 15;
        [Min(5f)] public float cameraVisibleWidth = 9f;
        [Min(8)] public int layerHeight = 9;
        [Min(1f)] public float playerStartY = 4.5f;
        [Min(2)] public int generateAheadLayers = 3;
        [Tooltip("Use 0 to create a different map for every run. Use another value for a repeatable map.")]
        public int randomSeed;

        [Header("Presentation")]
        [Min(0f)] public float loadingDuration = 1.25f;
        [Min(0.5f)] public float notificationDuration = 2.5f;
        public Color worldBackground = new Color(0.58f, 0.72f, 0.82f, 1f);
        public Color ninjaColor = new Color(0.88f, 0.18f, 0.14f, 1f);
        public Color wallColor = Color.white;
        public Color hazardColor = new Color(0.025f, 0.03f, 0.04f, 1f);

        public int SafeMapWidth => Mathf.Max(8, mapWidth);
        public int SafeLayerHeight => Mathf.Max(8, layerHeight);
        public float SafeCameraWidth => Mathf.Clamp(cameraVisibleWidth, 5f, SafeMapWidth);

        public Color GetBackgroundColor(int level)
        {
            return GetThemeColor(backgroundThemeColors, level, worldBackground);
        }

        public Color GetHazardTint(int level)
        {
            return GetThemeColor(hazardThemeTints, level, Color.white);
        }

        public int GetVisualThemeIndex(int level)
        {
            return Mathf.Max(0, level) / Mathf.Max(1, visualThemeLayerInterval);
        }

        private Color GetThemeColor(Color[] colors, int level, Color fallback)
        {
            if (colors == null || colors.Length == 0)
            {
                return fallback;
            }

            int index = GetVisualThemeIndex(level) % colors.Length;
            return colors[index];
        }
    }
}
