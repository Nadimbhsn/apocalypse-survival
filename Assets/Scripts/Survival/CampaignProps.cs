using System;
using UnityEngine;
using Platformer.Mechanics;

namespace Platformer.Survival
{
    /// <summary>
    /// An invisible (or barely visible) trigger placed by an authored level: checkpoints,
    /// the storm's start and end, hint signs, the exit gate. Fires once when the player
    /// walks into it, then stays inert - re-entering after a respawn must not re-fire it.
    /// </summary>
    public class LevelTrigger : MonoBehaviour
    {
        public Action OnEntered;
        bool spent;

        public static LevelTrigger Create(Transform parent, string name, float x, float y, float width, float height)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = new Vector3(x, y, 0f);

            var col = go.AddComponent<BoxCollider2D>();
            col.isTrigger = true;
            col.size = new Vector2(width, height);

            return go.AddComponent<LevelTrigger>();
        }

        void OnTriggerEnter2D(Collider2D other)
        {
            if (spent) return;
            if (other.GetComponent<PlayerController>() == null) return;
            spent = true;
            OnEntered?.Invoke();
        }
    }

    /// <summary>
    /// The treasure at the end of a secret passage: a chest of coins and materials that
    /// also counts toward the level's "every secret found" star. Deliberately generous -
    /// the point of a secret is that finding it feels worth the detour.
    /// </summary>
    public class SecretStash : MonoBehaviour
    {
        public int coins = 25;
        public int materials = 4;
        public Action OnFound;

        bool taken;

        public static SecretStash Create(Transform parent, float x, float y)
        {
            var go = new GameObject("SecretStash");
            go.transform.SetParent(parent, false);
            go.transform.position = new Vector3(x, y, 0f);
            go.transform.localScale = new Vector3(0.75f, 0.6f, 1f);

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = PlaceholderVisuals.Square(new Color(0.42f, 0.26f, 0.12f));
            sr.sortingOrder = 4;

            // Gold lid, so it reads as a chest rather than a crate.
            var lid = new GameObject("Lid");
            lid.transform.SetParent(go.transform, false);
            lid.transform.localPosition = new Vector3(0f, 0.32f, 0f);
            lid.transform.localScale = new Vector3(1.08f, 0.34f, 1f);
            var lidSr = lid.AddComponent<SpriteRenderer>();
            lidSr.sprite = PlaceholderVisuals.Square(ApogeeTheme.Gold);
            lidSr.sortingOrder = 5;

            var col = go.AddComponent<BoxCollider2D>();
            col.isTrigger = true;
            col.size = new Vector2(1.6f, 1.8f); // generous: the reward must never be missed by a pixel

            return go.AddComponent<SecretStash>();
        }

        void Update()
        {
            // A slow glow so the chest is unmistakable once the player is in the room.
            float pulse = 0.85f + Mathf.Sin(Time.time * 3f) * 0.15f;
            var sr = GetComponent<SpriteRenderer>();
            if (sr != null) sr.color = new Color(pulse, pulse * 0.92f, pulse * 0.8f);
        }

        void OnTriggerEnter2D(Collider2D other)
        {
            if (taken) return;
            if (other.GetComponent<PlayerController>() == null) return;
            taken = true;

            SaveSystem.AddCoins(coins);
            SaveSystem.AddMaterials(materials);
            Sfx.Milestone();
            Fx.Burst(transform.position, ApogeeTheme.Gold, 30, 5f, 0.13f, 0.6f);
            Fx.Text(transform.position + Vector3.up * 1.5f, "SECRET !", ApogeeTheme.Gold, 1.3f);
            RewardPopup.Show(transform.position + Vector3.up * 0.6f, coins, materials);
            OnFound?.Invoke();
            Destroy(gameObject);
        }
    }

    /// <summary>
    /// A cracked stone slab that only the player's gun opens. It is solid ground until it
    /// breaks, so a sealed secret reads as ordinary floor until someone thinks to shoot
    /// it - the visible cracks are the only clue, which is exactly the point.
    /// </summary>
    public class BreakableWall : MonoBehaviour
    {
        public int hp = 2;
        bool broken;

        public static BreakableWall Create(Transform parent, float centerX, float centerY, float width, float height)
        {
            var go = new GameObject("BreakableWall");
            go.transform.SetParent(parent, false);
            go.transform.position = new Vector3(centerX, centerY, 0f);
            go.transform.localScale = new Vector3(width, height, 1f);

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = PlaceholderVisuals.Square(new Color(0.34f, 0.30f, 0.27f));
            sr.sortingOrder = 1;

            go.AddComponent<BoxCollider2D>();

            // The slab is solid ground the player walks on, so its collider is not a
            // trigger - but a plain static collider would never report the player's
            // projectile hitting it (a kinematic trigger only contacts other bodies).
            // A kinematic body with full contacts enabled keeps it walkable AND shootable.
            var rb = go.AddComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Kinematic;
            rb.gravityScale = 0f;
            rb.useFullKinematicContacts = true;

            // Cracks: a few dark slivers across the slab, the tell that it can be opened.
            for (int i = 0; i < 3; i++)
            {
                var crack = new GameObject("Crack");
                crack.transform.SetParent(go.transform, false);
                crack.transform.localPosition = new Vector3(-0.3f + i * 0.3f, 0f, 0f);
                crack.transform.localScale = new Vector3(0.06f, 0.8f, 1f);
                crack.transform.localRotation = Quaternion.Euler(0f, 0f, i % 2 == 0 ? 14f : -11f);
                var csr = crack.AddComponent<SpriteRenderer>();
                csr.sprite = PlaceholderVisuals.Square(new Color(0.12f, 0.10f, 0.09f));
                csr.sortingOrder = 2;
            }

            return go.AddComponent<BreakableWall>();
        }

        /// <summary>Called by Projectile when a player shot lands on the slab.</summary>
        public void Hit(int damage)
        {
            if (broken) return;
            hp -= Mathf.Max(1, damage);
            Fx.Burst(transform.position, new Color(0.55f, 0.50f, 0.45f), 10, 3f, 0.09f);
            Sfx.Hit();
            if (hp > 0)
            {
                var sr = GetComponent<SpriteRenderer>();
                if (sr != null) sr.color = new Color(1.3f, 1.1f, 1.0f);
                return;
            }

            broken = true;
            Fx.Burst(transform.position, new Color(0.55f, 0.50f, 0.45f), 26, 5f, 0.12f, 0.9f);
            Fx.Shake(0.2f, 0.2f);
            Sfx.Kill();
            Destroy(gameObject);
        }
    }
}
