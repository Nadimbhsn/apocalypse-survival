using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Platformer.Mechanics
{
    /// <summary>
    /// Implements game physics for some in game entity.
    /// </summary>
    public class KinematicObject : MonoBehaviour
    {
        /// <summary>
        /// The minimum normal (dot product) considered suitable for the entity sit on.
        /// </summary>
        public float minGroundNormalY = .65f;

        /// <summary>
        /// A custom gravity coefficient applied to this entity.
        /// </summary>
        public float gravityModifier = 1f;

        /// <summary>
        /// Terminal fall speed. Keeps long drops (tower, shaft) readable and stops thin
        /// triggers from being skipped between physics steps.
        /// </summary>
        public float maxFallSpeed = 16f;

        /// <summary>
        /// Which way gravity pulls: 1 is down, -1 flips the entity onto the ceiling (the
        /// runner's rhythm section uses it). With 1 every line below behaves exactly as it
        /// did before this existed. Change it through SetGravitySign.
        /// </summary>
        public float gravitySign { get; private set; } = 1f;

        /// <summary>Multiplier on world gravity for this entity alone (snappier jumps in the rhythm section).</summary>
        public float gravityScale = 1f;

        /// <summary>
        /// Flips gravity. The ground normal is turned with it, because horizontal motion is
        /// projected along the last ground: left as the floor's normal, a body falling up to
        /// the ceiling would be pushed backwards until it landed.
        /// </summary>
        public void SetGravitySign(float sign)
        {
            gravitySign = sign < 0f ? -1f : 1f;
            groundNormal = new Vector2(0f, gravitySign);
        }

        /// <summary>
        /// The current velocity of the entity.
        /// </summary>
        public Vector2 velocity;

        /// <summary>
        /// Is the entity currently sitting on a surface?
        /// </summary>
        /// <value></value>
        public bool IsGrounded { get; private set; }

        protected Vector2 targetVelocity;
        protected Vector2 groundNormal;
        protected Rigidbody2D body;
        protected ContactFilter2D contactFilter;
        protected RaycastHit2D[] hitBuffer = new RaycastHit2D[16];

        protected const float minMoveDistance = 0.001f;
        protected const float shellRadius = 0.01f;


        /// <summary>
        /// Bounce the object's vertical velocity.
        /// </summary>
        /// <param name="value"></param>
        public void Bounce(float value)
        {
            velocity.y = value;
        }

        /// <summary>
        /// Bounce the objects velocity in a direction.
        /// </summary>
        /// <param name="dir"></param>
        public void Bounce(Vector2 dir)
        {
            velocity.y = dir.y;
            velocity.x = dir.x;
        }

        /// <summary>
        /// Teleport to some position.
        /// </summary>
        /// <param name="position"></param>
        public void Teleport(Vector3 position)
        {
            body.position = position;
            velocity *= 0;
            body.linearVelocity *= 0;
        }

        protected virtual void OnEnable()
        {
            body = GetComponent<Rigidbody2D>();
            body.bodyType = RigidbodyType2D.Kinematic;
        }

        protected virtual void OnDisable()
        {
            body.bodyType = RigidbodyType2D.Dynamic;
        }

        protected virtual void Start()
        {
            contactFilter.useTriggers = false;
            contactFilter.SetLayerMask(Physics2D.GetLayerCollisionMask(gameObject.layer));
            contactFilter.useLayerMask = true;
        }

        protected virtual void Update()
        {
            targetVelocity = Vector2.zero;
            ComputeVelocity();
        }

        protected virtual void ComputeVelocity()
        {

        }

        protected virtual void FixedUpdate()
        {
            //if already falling, fall faster than the jump speed, otherwise use normal gravity.
            // "Falling" and "terminal speed" are measured along gravity, whichever way it points.
            var gravity = Physics2D.gravity * (gravityScale * gravitySign);
            if (velocity.y * gravitySign < 0)
                velocity += gravityModifier * gravity * Time.deltaTime;
            else
                velocity += gravity * Time.deltaTime;

            if (velocity.y * gravitySign < -maxFallSpeed) velocity.y = -maxFallSpeed * gravitySign;
            velocity.x = targetVelocity.x;

            IsGrounded = false;

            var deltaPosition = velocity * Time.deltaTime;

            // Along the ground, forward: on a ceiling the normal points down, so the tangent
            // is flipped back to keep "forward" forward.
            var moveAlongGround = new Vector2(groundNormal.y, -groundNormal.x) * gravitySign;

            var move = moveAlongGround * deltaPosition.x;

            PerformMovement(move, false);

            move = Vector2.up * deltaPosition.y;

            PerformMovement(move, true);

        }

        void PerformMovement(Vector2 move, bool yMovement)
        {
            var distance = move.magnitude;

            if (distance > minMoveDistance)
            {
                //check if we hit anything in current direction of travel
                var count = body.Cast(move, contactFilter, hitBuffer, distance + shellRadius);
                for (var i = 0; i < count; i++)
                {
                    var currentNormal = hitBuffer[i].normal;

                    //is this surface flat enough to land on? (a ceiling counts once gravity is flipped)
                    if (currentNormal.y * gravitySign > minGroundNormalY)
                    {
                        IsGrounded = true;
                        // if moving up, change the groundNormal to new surface normal.
                        if (yMovement)
                        {
                            groundNormal = currentNormal;
                            currentNormal.x = 0;
                        }
                    }
                    if (IsGrounded)
                    {
                        //how much of our velocity aligns with surface normal?
                        var projection = Vector2.Dot(velocity, currentNormal);
                        if (projection < 0)
                        {
                            //slower velocity if moving against the normal (up a hill).
                            velocity = velocity - projection * currentNormal;
                        }
                    }
                    else
                    {
                        //We are airborne, but hit something, so cancel vertical up and horizontal velocity.
                        velocity.x *= 0;
                        velocity.y = gravitySign > 0 ? Mathf.Min(velocity.y, 0) : Mathf.Max(velocity.y, 0);
                    }
                    //remove shellDistance from actual move distance.
                    var modifiedDistance = hitBuffer[i].distance - shellRadius;
                    distance = modifiedDistance < distance ? modifiedDistance : distance;
                }
            }
            body.position = body.position + move.normalized * distance;
        }

    }
}