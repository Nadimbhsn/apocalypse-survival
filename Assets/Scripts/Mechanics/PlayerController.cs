using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Platformer.Gameplay;
using static Platformer.Core.Simulation;
using Platformer.Model;
using Platformer.Core;
using Platformer.Survival;
using UnityEngine.InputSystem;

namespace Platformer.Mechanics
{
    /// <summary>
    /// This is the main class used to implement control of the player.
    /// It is a superset of the AnimationController class, but is inlined to allow for any kind of customisation.
    ///
    /// Survival-mode additions: input is blended with MobileInput (on-screen joystick and
    /// jump button), jumps get coyote time (a jump pressed just after running off a ledge
    /// still fires) and a jump buffer (a press just before landing fires on landing), and
    /// upgrades can grant extra mid-air jumps.
    /// </summary>
    public class PlayerController : KinematicObject
    {
        public AudioClip jumpAudio;
        public AudioClip respawnAudio;
        public AudioClip ouchAudio;

        /// <summary>
        /// Max horizontal speed of the player.
        /// </summary>
        public float maxSpeed = 7;
        /// <summary>
        /// Initial jump velocity at the start of a jump.
        /// </summary>
        public float jumpTakeOffSpeed = 7;

        [Header("Feel")]
        public float coyoteTime = 0.1f;
        public float jumpBufferTime = 0.12f;
        /// <summary>Extra jumps allowed while airborne (granted by the Double Jump upgrade).</summary>
        public int airJumps = 0;

        [Header("Jetpack (runner Survol section)")]
        public bool jetpackActive;
        public float jetpackThrust = 26f;
        public float jetpackMaxRise = 6.5f;
        public float jetpackCeilingY = float.MaxValue;
        /// <summary>True while the jetpack is actually pushing (for the flame effect).</summary>
        public bool JetpackThrusting { get; private set; }

        [Header("Rhythm section (runner)")]
        /// <summary>
        /// Geometry-Dash rules for the runner's rhythm section: the character runs forward on
        /// its own, every jump is the same fixed arc (releasing early no longer cuts it short,
        /// so a jump can be timed to the beat), holding the button jumps again on landing,
        /// and the Double Jump upgrade is ignored - an extra jump would skip the section.
        /// </summary>
        public bool rhythmMode;
        public float rhythmJumpVelocity = 9.3f;

        /// <summary>A jump press not yet used by a jump, within the buffer window (jump orbs read it).</summary>
        public bool JumpPressedRecently => Time.time - jumpPressedTime <= jumpBufferTime;
        public bool JumpHeldNow => controlEnabled && (m_JumpAction.IsPressed() || MobileInput.JumpHeld);

        /// <summary>
        /// Launches the player along gravity's "up" as a pad or an orb does, using up the
        /// pending press so it does not also trigger a jump on the next landing.
        /// </summary>
        public void RhythmLaunch(float speed)
        {
            velocity.y = speed * gravitySign;
            jumpPressedTime = -10f;
            stopJump = false;
            jump = false;
            jumpState = JumpState.InFlight;
        }

        public JumpState jumpState = JumpState.Grounded;
        private bool stopJump;
        /*internal new*/ public Collider2D collider2d;
        /*internal new*/ public AudioSource audioSource;
        public Health health;
        public bool controlEnabled = true;

        bool jump;
        Vector2 move;
        SpriteRenderer spriteRenderer;
        internal Animator animator;
        readonly PlatformerModel model = Simulation.GetModel<PlatformerModel>();

        private InputAction m_MoveAction;
        private InputAction m_JumpAction;

        float lastGroundedTime = -10f;
        float jumpPressedTime = -10f;
        int airJumpsUsed;

        public Bounds Bounds => collider2d.bounds;
        /// <summary>Horizontal input currently applied (-1..1), after auto-run/touch blending.</summary>
        public float MoveX => move.x;

        void Awake()
        {
            health = GetComponent<Health>();
            audioSource = GetComponent<AudioSource>();
            collider2d = GetComponent<Collider2D>();
            spriteRenderer = GetComponent<SpriteRenderer>();
            animator = GetComponent<Animator>();

            m_MoveAction = InputSystem.actions.FindAction("Player/Move");
            m_JumpAction = InputSystem.actions.FindAction("Player/Jump");
            
            m_MoveAction.Enable();
            m_JumpAction.Enable();
        }

        protected override void Update()
        {
            if (controlEnabled)
            {
                float inputX = m_MoveAction.ReadValue<Vector2>().x;
                move.x = MobileInput.Active ? MobileInput.ResolveMoveX(inputX) : inputX;
                if (rhythmMode) move.x = 1f; // the rhythm section runs by itself

                bool pressed = m_JumpAction.WasPressedThisFrame() || MobileInput.ConsumeJumpPressed();
                bool released = m_JumpAction.WasReleasedThisFrame() || MobileInput.ConsumeJumpReleased();
                if (pressed) jumpPressedTime = Time.time;
                if (released && !rhythmMode)
                {
                    stopJump = true;
                    Schedule<PlayerStopJump>().player = this;
                }
            }
            else
            {
                move.x = 0;
                MobileInput.ConsumeJumpPressed();
                MobileInput.ConsumeJumpReleased();
            }
            UpdateJumpState();
            base.Update();
        }

        void UpdateJumpState()
        {
            jump = false;
            if (IsGrounded)
            {
                lastGroundedTime = Time.time;
                airJumpsUsed = 0;
            }

            if (jetpackActive)
            {
                // Flight replaces the jump state machine entirely.
                jumpPressedTime = -10f;
                jumpState = IsGrounded ? JumpState.Grounded : JumpState.InFlight;
                return;
            }

            bool wantsJump = Time.time - jumpPressedTime <= jumpBufferTime
                             || (rhythmMode && IsGrounded && JumpHeldNow); // hold to keep jumping
            bool withinCoyote = Time.time - lastGroundedTime <= coyoteTime;

            switch (jumpState)
            {
                case JumpState.Grounded:
                    if (wantsJump && (IsGrounded || withinCoyote))
                    {
                        jumpState = JumpState.PrepareToJump;
                        goto case JumpState.PrepareToJump;
                    }
                    if (!IsGrounded && !withinCoyote)
                        jumpState = JumpState.InFlight; // walked off a ledge
                    break;
                case JumpState.PrepareToJump:
                    jumpState = JumpState.Jumping;
                    jump = true;
                    stopJump = false;
                    jumpPressedTime = -10f;
                    break;
                case JumpState.Jumping:
                    if (!IsGrounded)
                    {
                        Schedule<PlayerJumped>().player = this;
                        jumpState = JumpState.InFlight;
                    }
                    break;
                case JumpState.InFlight:
                    if (IsGrounded)
                    {
                        Schedule<PlayerLanded>().player = this;
                        jumpState = JumpState.Landed;
                    }
                    else if (wantsJump && airJumpsUsed < (rhythmMode ? 0 : airJumps))
                    {
                        airJumpsUsed++;
                        jump = true;
                        stopJump = false;
                        jumpPressedTime = -10f;
                        Schedule<PlayerJumped>().player = this;
                    }
                    break;
                case JumpState.Landed:
                    jumpState = JumpState.Grounded;
                    break;
            }
        }

        protected override void ComputeVelocity()
        {
            if (jetpackActive)
            {
                bool held = controlEnabled && (m_JumpAction.IsPressed() || MobileInput.JumpHeld);
                JetpackThrusting = held;
                if (held) velocity.y = Mathf.Min(velocity.y + jetpackThrust * Time.deltaTime, jetpackMaxRise);
                if (transform.position.y > jetpackCeilingY && velocity.y > 0f) velocity.y = 0f;
                stopJump = false;
                jump = false;
            }
            else
            {
                JetpackThrusting = false;
            }

            if (jump)
            {
                float takeOff = rhythmMode ? rhythmJumpVelocity : jumpTakeOffSpeed * model.jumpModifier;
                velocity.y = takeOff * gravitySign;
                jump = false;
            }
            else if (stopJump)
            {
                stopJump = false;
                if (velocity.y * gravitySign > 0)
                {
                    velocity.y = velocity.y * model.jumpDeceleration;
                }
            }

            // Upside down on the ceiling, the sprite hangs upside down too.
            spriteRenderer.flipY = gravitySign < 0f;

            if (move.x > 0.01f)
                spriteRenderer.flipX = false;
            else if (move.x < -0.01f)
                spriteRenderer.flipX = true;

            animator.SetBool("grounded", IsGrounded);
            animator.SetFloat("velocityX", Mathf.Abs(velocity.x) / maxSpeed);

            targetVelocity = move * maxSpeed;
        }

        public enum JumpState
        {
            Grounded,
            PrepareToJump,
            Jumping,
            InFlight,
            Landed
        }
    }
}
