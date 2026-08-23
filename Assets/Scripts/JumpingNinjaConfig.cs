using UnityEngine;

namespace JumpingNinja
{
    [CreateAssetMenu(fileName = "JumpingNinjaConfig", menuName = "Jumping Ninja/V1 Config")]
    public sealed class JumpingNinjaConfig : ScriptableObject
    {
        [Header("Brand")]
        public Sprite logo;

        [Header("Ninja Movement")]
        [Range(1f, 45f)] public float steeringAngle = 15f;
        [Min(1f)] public float jumpSpeed = 12f;
        [Min(0.1f)] public float gravityScale = 2.8f;
        [Min(0.1f)] public float playerSize = 1f;

        [Header("Infinite Map")]
        [Min(8)] public int mapWidth = 25;
        [Min(5f)] public float cameraVisibleWidth = 15f;
        [Min(8)] public int layerHeight = 15;
        [Min(1f)] public float playerStartY = 7.5f;
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
    }
}
