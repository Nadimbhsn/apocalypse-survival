using System.Collections.Generic;
using UnityEngine;
using Platformer.Mechanics;

namespace Platformer.Survival
{
    /// <summary>
    /// The hero of the Apogée book, as two painted sprite sheets (Resources/Hero/hero_sheet_0
    /// and _1, 4 x 4 frames of 512 px each): thirty-two frames cut from the character's
    /// reference portrait and posed like a puppet - idle, run, jump, fall, land, shoot, hurt,
    /// death and victory. Mipmapped, so he stays smooth while the camera zooms out.
    /// </summary>
    public static class HeroSprites
    {
        public const int Frame = 512, Columns = 4, PerSheet = 16, Sheets = 2;
        /// <summary>
        /// About 1.2 units tall (he is drawn 455 px high), feet where the old sprite's were:
        /// 0.4 units under the player's origin, so the collider is unchanged.
        /// </summary>
        const float PixelsPerUnit = 379f;
        static readonly Vector2 Pivot = new Vector2(0.47f, 0.338f);

        public static readonly Dictionary<string, (int start, int count)> Anims = new()
        {
            { "idle", (0, 6) }, { "run", (6, 8) }, { "jump", (14, 2) }, { "fall", (16, 2) }, { "land", (18, 2) },
            { "shoot", (20, 2) }, { "hurt", (22, 2) }, { "death", (24, 4) }, { "victory", (28, 4) },
        };

        static Sprite[] frames;
        static bool loaded;

        public static bool Available
        {
            get { Load(); return frames != null; }
        }

        static void Load()
        {
            if (loaded) return;
            loaded = true;
            var list = new List<Sprite>();
            for (int s = 0; s < Sheets; s++)
            {
                var tex = Resources.Load<Texture2D>($"Hero/hero_sheet_{s}");
                if (tex == null) return;
                for (int k = 0; k < PerSheet; k++)
                {
                    int cx = k % Columns, cy = k / Columns;
                    // Rows are laid out from the top of the sheet.
                    var rect = new Rect(cx * Frame, tex.height - (cy + 1) * Frame, Frame, Frame);
                    list.Add(Sprite.Create(tex, rect, Pivot, PixelsPerUnit));
                }
            }
            frames = list.ToArray();
        }

        public static Sprite Get(string anim, int index)
        {
            Load();
            if (frames == null || !Anims.TryGetValue(anim, out var a)) return null;
            int i = a.start + Mathf.Clamp(index, 0, a.count - 1);
            return i < frames.Length ? frames[i] : null;
        }

        public static int Count(string anim) => Anims.TryGetValue(anim, out var a) ? a.count : 1;
    }

    /// <summary>
    /// Shows the hero's frames on the player in place of the sample character: it reads the
    /// player's state (grounded, moving, rising, falling, hurt, dead, firing) and sets the
    /// sprite after the Animator has run, so the rest of the game - the facing flip, the
    /// skin tint, the rhythm section's copy of the sprite - simply follows. On an object with
    /// no PlayerController (the home screen's preview) it just breathes in its idle loop.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public class HeroAnimator : MonoBehaviour
    {
        PlayerController player;
        PlayerCombat combat;
        SpriteRenderer sr;
        string current = "idle";
        float time, landUntil, hurtUntil;
        int lastHp = -1;
        bool wasGrounded = true;

        static readonly Dictionary<string, float> Fps = new()
        {
            { "idle", 6f }, { "run", 13f }, { "jump", 6f }, { "fall", 6f }, { "land", 14f },
            { "shoot", 12f }, { "hurt", 10f }, { "death", 7f }, { "victory", 6f },
        };
        static readonly HashSet<string> Once = new() { "land", "death", "hurt" };

        void Awake()
        {
            player = GetComponent<PlayerController>();
            combat = GetComponent<PlayerCombat>();
            sr = GetComponent<SpriteRenderer>();
            if (!HeroSprites.Available) enabled = false;
        }

        void LateUpdate()
        {
            if (sr == null) return;
            string next = player != null ? PickState() : "idle";
            if (next != current) { current = next; time = 0f; }
            time += Time.deltaTime;

            float fps = Fps[current];
            if (current == "run" && player != null)
                fps *= Mathf.Clamp(Mathf.Abs(player.velocity.x) / 3.6f, 0.6f, 1.6f);   // feet keep up with the speed
            int count = HeroSprites.Count(current);
            int index = Mathf.FloorToInt(time * fps);
            index = Once.Contains(current) ? Mathf.Min(index, count - 1) : index % count;
            var sprite = HeroSprites.Get(current, index);
            if (sprite != null) sr.sprite = sprite;
        }

        string PickState()
        {
            var health = player.health;
            if (health != null)
            {
                int hp = Mathf.RoundToInt(health.NormalizedHP * 1000f);
                if (lastHp >= 0 && hp < lastHp && health.IsAlive) hurtUntil = Time.time + 0.3f;
                lastHp = hp;
                if (!health.IsAlive) return "death";
            }
            bool grounded = player.IsGrounded;
            if (!wasGrounded && grounded) landUntil = Time.time + 0.14f;
            wasGrounded = grounded;

            if (Time.time < hurtUntil) return "hurt";
            if (!grounded) return player.velocity.y * player.gravitySign > 0.5f ? "jump" : "fall";
            if (Time.time < landUntil) return "land";
            if (Mathf.Abs(player.velocity.x) > 0.3f) return "run";
            if (combat != null && Time.time - combat.LastShotTime < 0.3f) return "shoot";
            return "idle";
        }
    }
}
