using UnityEngine;

namespace JumpingNinja
{
    public sealed class CameraFollower : MonoBehaviour
    {
        private Camera gameCamera;
        private Transform target;
        private JumpingNinjaConfig config;
        private SpriteRenderer backgroundRenderer;
        private int lastScreenWidth;
        private int lastScreenHeight;
        private int currentThemeIndex = -1;

        public void Initialize(Camera cameraComponent, Transform followTarget, JumpingNinjaConfig gameConfig)
        {
            gameCamera = cameraComponent;
            target = followTarget;
            config = gameConfig;
            CreateBackground();
            RefreshProjection();
            SnapToTarget();
            RefreshTheme();
        }

        private void LateUpdate()
        {
            if (target == null || gameCamera == null)
            {
                return;
            }

            if (lastScreenWidth != Screen.width || lastScreenHeight != Screen.height)
            {
                RefreshProjection();
            }

            SnapToTarget();
            RefreshTheme();
        }

        private void CreateBackground()
        {
            if (config.backgroundPatternSprite == null)
            {
                return;
            }

            GameObject backgroundObject = new GameObject("Ninja Pattern Background", typeof(SpriteRenderer));
            backgroundObject.transform.SetParent(transform, false);
            backgroundObject.transform.localPosition = new Vector3(0f, 0f, 11f);
            backgroundRenderer = backgroundObject.GetComponent<SpriteRenderer>();
            backgroundRenderer.sprite = config.backgroundPatternSprite;
            backgroundRenderer.sortingOrder = -100;
        }

        private void RefreshProjection()
        {
            lastScreenWidth = Screen.width;
            lastScreenHeight = Screen.height;
            float safeAspect = Mathf.Max(0.1f, gameCamera.aspect);
            gameCamera.orthographicSize = config.SafeCameraWidth / safeAspect * 0.5f;

            if (backgroundRenderer != null)
            {
                Vector2 spriteSize = backgroundRenderer.sprite.bounds.size;
                float visibleHeight = gameCamera.orthographicSize * 2f;
                backgroundRenderer.transform.localScale = new Vector3(
                    config.SafeCameraWidth / Mathf.Max(0.0001f, spriteSize.x),
                    visibleHeight / Mathf.Max(0.0001f, spriteSize.y),
                    1f);
            }
        }

        private void SnapToTarget()
        {
            float horizontalHalf = config.SafeCameraWidth * 0.5f;
            float verticalHalf = gameCamera.orthographicSize;
            float x = Mathf.Clamp(target.position.x, horizontalHalf, config.SafeMapWidth - horizontalHalf);
            float y = Mathf.Max(target.position.y, verticalHalf - 1f);
            transform.position = new Vector3(x, y, -10f);
        }

        private void RefreshTheme()
        {
            int level = Mathf.Max(0, Mathf.FloorToInt(target.position.y / config.SafeLayerHeight));
            int themeIndex = config.GetVisualThemeIndex(level);
            if (themeIndex == currentThemeIndex)
            {
                return;
            }

            currentThemeIndex = themeIndex;
            Color backgroundColor = config.GetBackgroundColor(level);
            gameCamera.backgroundColor = backgroundColor;

            if (backgroundRenderer != null)
            {
                Color patternTint = Color.Lerp(Color.white, backgroundColor, 0.35f);
                patternTint.a = Mathf.Clamp01(config.backgroundPatternOpacity);
                backgroundRenderer.color = patternTint;
            }
        }
    }
}
