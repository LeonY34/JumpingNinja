using UnityEngine;

namespace JumpingNinja
{
    internal sealed class PortraitViewport : MonoBehaviour
    {
        private Camera targetCamera;
        private int lastWidth;
        private int lastHeight;

        public static bool ShouldLetterbox =>
            Application.platform == RuntimePlatform.WindowsPlayer || Application.isEditor;

        private void Awake()
        {
            targetCamera = GetComponent<Camera>();
            ApplyViewport();
        }

        private void LateUpdate()
        {
            if (lastWidth != Screen.width || lastHeight != Screen.height)
            {
                ApplyViewport();
            }
        }

        private void ApplyViewport()
        {
            lastWidth = Screen.width;
            lastHeight = Screen.height;
            if (targetCamera == null || !ShouldLetterbox || lastWidth <= 0 || lastHeight <= 0)
            {
                if (targetCamera != null)
                {
                    targetCamera.rect = new Rect(0f, 0f, 1f, 1f);
                }
                return;
            }

            float windowAspect = lastWidth / (float)lastHeight;
            if (windowAspect > RuntimeUi.PortraitAspect)
            {
                float width = RuntimeUi.PortraitAspect / windowAspect;
                targetCamera.rect = new Rect((1f - width) * 0.5f, 0f, width, 1f);
            }
            else
            {
                float height = windowAspect / RuntimeUi.PortraitAspect;
                targetCamera.rect = new Rect(0f, (1f - height) * 0.5f, 1f, height);
            }
        }
    }
}
