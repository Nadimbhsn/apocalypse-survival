using UnityEngine;
using Platformer.Mechanics;

namespace Platformer.Survival
{
    public enum PickupType { Coin, Material }

    /// <summary>
    /// Ground pickup collected by the player; credits SaveSystem's persistent wallet.
    /// </summary>
    [RequireComponent(typeof(Collider2D))]
    public class Pickup : MonoBehaviour
    {
        public PickupType type = PickupType.Coin;
        public int value = 1;

        float bobT;
        Vector3 baseScale;
        bool collected;

        void Awake()
        {
            baseScale = transform.localScale;
            GetComponent<Collider2D>().isTrigger = true;
        }

        void Update()
        {
            bobT += Time.deltaTime * 4f;
            transform.localScale = baseScale * (1f + Mathf.Sin(bobT) * 0.08f);
        }

        void OnTriggerEnter2D(Collider2D other)
        {
            if (collected) return;
            var controller = other.GetComponent<PlayerController>();
            if (controller == null) return;

            collected = true;
            if (type == PickupType.Coin) SaveSystem.AddCoins(value);
            else SaveSystem.AddMaterials(value);

            SurvivalDirector.Instance?.OnPickupCollected(this);
            Destroy(gameObject);
        }
    }
}
