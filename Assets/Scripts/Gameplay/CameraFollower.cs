using UnityEngine;

namespace JumpingNinja
{
    public sealed class CameraFollower : MonoBehaviour
    {
        private Camera gameCamera;
        private Transform target;
        private JumpingNinjaConfig config;
        private int lastScreenWidth;
        private int lastScreenHeight;

        public void Initialize(Camera cameraComponent, Transform followTarget, JumpingNinjaConfig gameConfig)
        {
            gameCamera = cameraComponent;
            target = followTarget;
            config = gameConfig;
            RefreshProjection();
            SnapToTarget();
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
        }

        private void RefreshProjection()
        {
            lastScreenWidth = Screen.width;
            lastScreenHeight = Screen.height;
            float safeAspect = Mathf.Max(0.1f, gameCamera.aspect);
            gameCamera.orthographicSize = config.SafeCameraWidth / safeAspect * 0.5f;
        }

        private void SnapToTarget()
        {
            float horizontalHalf = config.SafeCameraWidth * 0.5f;
            float verticalHalf = gameCamera.orthographicSize;
            float x = Mathf.Clamp(target.position.x, horizontalHalf, config.SafeMapWidth - horizontalHalf);
            float y = Mathf.Max(target.position.y, verticalHalf - 1f);
            transform.position = new Vector3(x, y, -10f);
        }
    }
}
