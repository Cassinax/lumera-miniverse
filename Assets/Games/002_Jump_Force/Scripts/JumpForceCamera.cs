using UnityEngine;

namespace Lumera.JumpForce
{
    [DisallowMultipleComponent, RequireComponent(typeof(Camera))]
    public sealed class JumpForceCamera : MonoBehaviour
    {
        public JumpForcePlayer player;
        [Min(0.01f)] public float smoothTime = 0.3f;
        [Tooltip("Altura da camera acima dos pes.")]
        public float heightOffset = 4;
        [Tooltip("Margem abaixo da tela, em fracao da altura da tela.")]
        [Range(0, 0.5f)] public float deathViewportMargin = 0.08f;
        public bool deathEnabled = true;
        public bool LockedUpward { get; private set; }
        float velocity;
        Vector3 initialPosition;
        Camera view;
        void Awake() { initialPosition = transform.position; view = GetComponent<Camera>(); }
        void LateUpdate()
        {
            if (!player || player.Dead) return;
            LockedUpward |= player.ReachedPlatform;
            var p = transform.position;
            float desired = player.FeetY + heightOffset;
            float y = Mathf.SmoothDamp(p.y, desired, ref velocity, smoothTime);
            if (LockedUpward && y < p.y) { y = p.y; velocity = Mathf.Max(0, velocity); }
            transform.position = new Vector3(initialPosition.x, y, initialPosition.z);
            // Wait for the entire capsule to leave the lower edge.
            Vector3 head = player.GetComponent<CapsuleCollider>().bounds.max;
            Vector3 viewport = view.WorldToViewportPoint(new Vector3(player.transform.position.x, head.y, player.transform.position.z));
            if (deathEnabled && LockedUpward && viewport.z > 0 && viewport.y < -deathViewportMargin) player.Die();
        }
        public void Restart()
        {
            LockedUpward = false;
            velocity = 0;
            transform.position = initialPosition;
        }
    }
}
