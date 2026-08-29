using UnityEngine;

namespace JumpingNinja
{
    public sealed class NinjaController : MonoBehaviour
    {
        private GameController game;
        private JumpingNinjaConfig config;
        private Rigidbody2D body;
        private Transform visual;
        private SpriteRenderer spriteRenderer;
        private Vector3 baseVisualScale = Vector3.one;
        private Color baseVisualColor = Color.white;
        private float jumpAnimationStartedAt = float.NegativeInfinity;
        private float deathAnimationStartedAt;
        private float deathSpinDirection = 1f;
        private int jumpDirection = 1;
        private bool alive = true;
        private bool dying;

        public Vector2 Position => transform.position;

        public void Initialize(GameController owner, JumpingNinjaConfig gameConfig, Sprite sprite, PhysicsMaterial2D physicsMaterial)
        {
            game = owner;
            config = gameConfig;

            transform.localScale = Vector3.one * config.playerSize;
            GameObject visualObject = new GameObject("Visual", typeof(SpriteRenderer));
            visualObject.transform.SetParent(transform, false);
            visual = visualObject.transform;
            spriteRenderer = visualObject.GetComponent<SpriteRenderer>();
            spriteRenderer.sprite = config.ninjaSprite != null ? config.ninjaSprite : sprite;
            spriteRenderer.color = config.ninjaSprite != null ? Color.white : config.ninjaColor;
            spriteRenderer.sortingOrder = 10;

            Vector2 spriteSize = spriteRenderer.sprite.bounds.size;
            float largestDimension = Mathf.Max(0.0001f, spriteSize.x, spriteSize.y);
            baseVisualScale = Vector3.one / largestDimension;
            baseVisualColor = spriteRenderer.color;
            ResetVisual();

            BoxCollider2D bodyCollider = gameObject.AddComponent<BoxCollider2D>();
            bodyCollider.size = Vector2.one * config.SafePlayerColliderScale;
            bodyCollider.isTrigger = false;
            bodyCollider.sharedMaterial = physicsMaterial;

            body = gameObject.AddComponent<Rigidbody2D>();
            body.bodyType = RigidbodyType2D.Dynamic;
            body.simulated = true;
            body.gravityScale = config.gravityScale;
            body.freezeRotation = true;
            body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            body.interpolation = RigidbodyInterpolation2D.Interpolate;

            Physics2D.SyncTransforms();
        }

        public void Steer(bool moveRight)
        {
            if (!alive || game == null || !game.AcceptsInput)
            {
                return;
            }

            float radians = config.steeringAngle * Mathf.Deg2Rad;
            float horizontal = Mathf.Sin(radians) * config.jumpSpeed * (moveRight ? 1f : -1f);
            float vertical = Mathf.Cos(radians) * config.jumpSpeed;
            body.linearVelocity = new Vector2(horizontal, vertical);
            jumpDirection = moveRight ? 1 : -1;
            jumpAnimationStartedAt = Time.time;
        }

        public float StopForDeath()
        {
            if (dying)
            {
                return Mathf.Max(0.1f, config.deathAnimationDuration);
            }

            alive = false;
            dying = true;
            deathAnimationStartedAt = Time.time;
            if (body != null)
            {
                deathSpinDirection = body.linearVelocity.x >= 0f ? -1f : 1f;
                body.linearVelocity = Vector2.zero;
                body.simulated = false;
            }

            return Mathf.Max(0.1f, config.deathAnimationDuration);
        }

        private void Update()
        {
            if (visual == null || spriteRenderer == null)
            {
                return;
            }

            if (dying)
            {
                AnimateDeath();
                return;
            }

            AnimateJump();
        }

        private void AnimateJump()
        {
            float duration = Mathf.Max(0.05f, config.jumpAnimationDuration);
            float normalizedTime = (Time.time - jumpAnimationStartedAt) / duration;
            if (normalizedTime < 0f || normalizedTime >= 1f)
            {
                ResetVisual();
                return;
            }

            float strength = Mathf.Clamp(config.jumpStretch, 0f, 0.4f);
            float horizontalScale;
            float verticalScale;
            if (normalizedTime < 0.25f)
            {
                float squash = Mathf.Sin(normalizedTime / 0.25f * Mathf.PI);
                horizontalScale = 1f + strength * 0.55f * squash;
                verticalScale = 1f - strength * 0.4f * squash;
            }
            else
            {
                float stretch = Mathf.Sin((normalizedTime - 0.25f) / 0.75f * Mathf.PI);
                horizontalScale = 1f - strength * 0.45f * stretch;
                verticalScale = 1f + strength * stretch;
            }

            visual.localScale = Vector3.Scale(baseVisualScale, new Vector3(horizontalScale, verticalScale, 1f));
            visual.localRotation = Quaternion.Euler(0f, 0f, -jumpDirection * 10f * Mathf.Sin(normalizedTime * Mathf.PI));
            visual.localPosition = new Vector3(0f, strength * 0.08f * Mathf.Sin(normalizedTime * Mathf.PI), 0f);
        }

        private void AnimateDeath()
        {
            float duration = Mathf.Max(0.1f, config.deathAnimationDuration);
            float normalizedTime = Mathf.Clamp01((Time.time - deathAnimationStartedAt) / duration);
            float easedRotation = 1f - Mathf.Pow(1f - normalizedTime, 3f);
            float shake = Mathf.Sin(normalizedTime * Mathf.PI * 12f) * config.deathShake * (1f - normalizedTime);
            float hop = Mathf.Sin(normalizedTime * Mathf.PI) * 0.18f;
            float pop = Mathf.Sin(Mathf.Clamp01(normalizedTime / 0.22f) * Mathf.PI) * 0.22f;
            float shrink = 1f - Mathf.SmoothStep(0f, 1f, normalizedTime);
            float scale = Mathf.Max(0.001f, (1f + pop) * shrink);

            visual.localPosition = new Vector3(shake, hop, 0f);
            visual.localRotation = Quaternion.Euler(0f, 0f, deathSpinDirection * 540f * easedRotation);
            visual.localScale = baseVisualScale * scale;

            Color color = baseVisualColor;
            color.a *= 1f - Mathf.SmoothStep(0.3f, 1f, normalizedTime);
            spriteRenderer.color = color;
        }

        private void ResetVisual()
        {
            visual.localPosition = Vector3.zero;
            visual.localRotation = Quaternion.identity;
            visual.localScale = baseVisualScale;
            spriteRenderer.color = baseVisualColor;
        }

        private void OnCollisionEnter2D(Collision2D collision)
        {
            if (!alive)
            {
                return;
            }

            if (HasMarker<HazardBlock>(collision.collider) ||
                HasMarker<HazardBlock>(collision.otherCollider))
            {
                game.KillPlayer();
            }
        }

        private static bool HasMarker<T>(Collider2D collider) where T : Component
        {
            return collider != null && collider.GetComponentInParent<T>() != null;
        }
    }
}
