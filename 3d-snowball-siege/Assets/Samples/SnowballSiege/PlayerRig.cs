using UnityEngine;
using UnityEngine.InputSystem;

namespace Arcade.SnowballSiege
{
    public class PlayerRig : MonoBehaviour
    {
        public float Speed = 6f;
        public float Cooldown = 0.4f;
        public GameObject? SnowballPrefab;
        public Material? SnowballMaterial;

        private Camera? _cam;
        private float _nextFire;
        private bool _locked = true;

        private void Awake()
        {
            _cam = Camera.main;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        private void Update()
        {
            var kb = Keyboard.current;
            if (kb == null) return;
            if (kb.escapeKey.wasPressedThisFrame)
            {
                _locked = !_locked;
                Cursor.lockState = _locked ? CursorLockMode.Locked : CursorLockMode.None;
                Cursor.visible = !_locked;
            }

            Vector3 move = Vector3.zero;
            if (kb.wKey.isPressed) move += Vector3.forward;
            if (kb.sKey.isPressed) move += Vector3.back;
            if (kb.aKey.isPressed) move += Vector3.left;
            if (kb.dKey.isPressed) move += Vector3.right;
            transform.position += transform.TransformDirection(move.normalized) * Speed * Time.deltaTime;

            var mouse = Mouse.current;
            if (mouse != null && _locked)
            {
                var d = mouse.delta.ReadValue();
                transform.Rotate(0f, d.x * 0.1f, 0f);
            }

            if (mouse != null && mouse.leftButton.wasPressedThisFrame && Time.time >= _nextFire)
            {
                _nextFire = Time.time + Cooldown;
                Fire();
            }
        }

        private void Fire()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.transform.localScale = Vector3.one * 0.3f;
            go.transform.position = transform.position + transform.forward;
            var rb = go.AddComponent<Rigidbody>();
            rb.mass = 0.2f;
            rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
            rb.linearVelocity = transform.forward * 24f;
            if (SnowballMaterial != null)
            {
                var r = go.GetComponent<Renderer>();
                if (r != null) r.material = SnowballMaterial;
            }
            var sb = go.AddComponent<Snowball>();
            sb.Damage = 5;
            Destroy(go, 5f);
        }
    }
}