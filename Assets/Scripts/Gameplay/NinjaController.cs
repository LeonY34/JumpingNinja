using UnityEngine;

namespace JumpingNinja
{
    public sealed class NinjaController : MonoBehaviour
    {
        private GameController game;
        private JumpingNinjaConfig config;
        private Rigidbody2D body;
        private bool alive = true;

        public Vector2 Position => transform.position;

        public void Initialize(GameController owner, JumpingNinjaConfig gameConfig, Sprite sprite, PhysicsMaterial2D physicsMaterial)
        {
            game = owner;
            config = gameConfig;

            transform.localScale = Vector3.one * config.playerSize;
            SpriteRenderer renderer = gameObject.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.color = config.ninjaColor;
            renderer.sortingOrder = 10;

            BoxCollider2D collider = gameObject.AddComponent<BoxCollider2D>();
            collider.sharedMaterial = physicsMaterial;

            body = gameObject.AddComponent<Rigidbody2D>();
            body.gravityScale = config.gravityScale;
            body.freezeRotation = true;
            body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            body.interpolation = RigidbodyInterpolation2D.Interpolate;
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
        }

        public void StopForDeath()
        {
            alive = false;
            if (body != null)
            {
                body.linearVelocity = Vector2.zero;
                body.simulated = false;
            }
        }

        private void OnCollisionEnter2D(Collision2D collision)
        {
            ResolveCollision(collision.collider);
        }

        private void OnCollisionStay2D(Collision2D collision)
        {
            ResolveCollision(collision.collider);
        }

        private void ResolveCollision(Collider2D other)
        {
            if (!alive)
            {
                return;
            }

            if (other.GetComponent<HazardBlock>() != null)
            {
                game.KillPlayer();
                return;
            }

            if (other.GetComponent<SideWall>() != null)
            {
                Vector2 velocity = body.linearVelocity;
                body.linearVelocity = new Vector2(0f, velocity.y);
            }
        }
    }
}
