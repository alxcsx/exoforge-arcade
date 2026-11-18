using UnityEngine;

namespace Arcade.SnowballSiege
{
    public class Snowball : MonoBehaviour
    {
        public int Damage = 5;

        private void OnCollisionEnter(Collision collision)
        {
            var target = collision.collider.GetComponent<BossTarget>();
            if (target == null) return;
            var game = FindObjectOfType<BossGame>();
            if (game == null || !game.BossAlive) return;
            game.NotifyHit(target.DamageForPart(), target.Part);
            Destroy(gameObject);
        }
    }
}