using UnityEngine;

namespace Arcade.SnowballSiege
{
    public class BossTarget : MonoBehaviour
    {
        public string Part = "body";

        public int DamageForPart()
        {
            return Part switch
            {
                "head" => 30,
                "body" => 10,
                _ => 5,
            };
        }
    }
}