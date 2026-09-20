using UnityEngine;

namespace Platformer.Survival
{
    /// <summary>
    /// Bridge between the on-screen controls (see VirtualControls) and PlayerController /
    /// PlayerCombat, which still read keyboard and gamepad through the Input System.
    /// Horizontal input is keyboard + virtual joystick; jump taps queue press/release events
    /// consumed once; fire is a held state.
    /// </summary>
    public static class MobileInput
    {
        public static bool Active;
        /// <summary>Horizontal axis from the virtual joystick (-1..1).</summary>
        public static float TouchMoveX;
        /// <summary>True while the on-screen FIRE button is held.</summary>
        public static bool FireHeld;
        /// <summary>True while the on-screen JUMP button is held (jetpack thrust).</summary>
        public static bool JumpHeld;

        static bool jumpPressed, jumpReleased;

        public static void PressJump() { jumpPressed = true; JumpHeld = true; }
        public static void ReleaseJump() { jumpReleased = true; JumpHeld = false; }

        public static bool ConsumeJumpPressed()
        {
            bool value = jumpPressed;
            jumpPressed = false;
            return value;
        }

        public static bool ConsumeJumpReleased()
        {
            bool value = jumpReleased;
            jumpReleased = false;
            return value;
        }

        public static float ResolveMoveX(float keyboardX) => Mathf.Clamp(keyboardX + TouchMoveX, -1f, 1f);

        public static void Reset()
        {
            TouchMoveX = 0f;
            FireHeld = false;
            JumpHeld = false;
            jumpPressed = false;
            jumpReleased = false;
        }
    }
}
