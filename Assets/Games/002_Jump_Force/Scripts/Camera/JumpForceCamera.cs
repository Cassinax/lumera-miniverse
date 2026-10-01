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
        [Tooltip("Fracao do corpo que precisa sair pela borda de baixo da tela para o personagem morrer.")]
        [Range(0.05f, 1)] public float deathBodyFraction = 0.5f;
        public bool deathEnabled = true;
        public bool LockedUpward { get; private set; }
        public bool InWardrobe { get; private set; }
        public void HoldForWardrobe(Vector3 position)
        {
            InWardrobe = true;
            Restart();
            transform.position = position;
        }
        public void ReleaseFromWardrobe()
        {
            InWardrobe = false;
            // Keep the scene's gameplay framing, then follow changes in the player's height.
            followOffsetCorrection = player ? initialPosition.y - player.FeetY - heightOffset : 0;
            Restart();
        }
        float velocity, followOffsetCorrection;
        Vector3 initialPosition;
        Camera view;
        CapsuleCollider capsule;
        void Awake()
        {
            initialPosition = transform.position;
            view = GetComponent<Camera>();
            if (player) capsule = player.GetComponent<CapsuleCollider>();
        }
        void LateUpdate()
        {
            if (InWardrobe || !player || player.Dead) return;
            if (!capsule) capsule = player.GetComponent<CapsuleCollider>();
            LockedUpward |= player.ReachedPlatform;
            var p = transform.position;
            float desired = player.FeetY + heightOffset + followOffsetCorrection;
            float y = Mathf.SmoothDamp(p.y, desired, ref velocity, smoothTime);
            if (LockedUpward && y < p.y) { y = p.y; velocity = Mathf.Max(0, velocity); }
            transform.position = new Vector3(initialPosition.x, y, initialPosition.z);
            // Dies once the chosen fraction of the capsule (half, by default) is below the lower edge.
            Bounds body = capsule.bounds;
            float cutY = body.min.y + body.size.y * deathBodyFraction;
            Vector3 viewport = view.WorldToViewportPoint(new Vector3(player.transform.position.x, cutY, player.transform.position.z));
            if (deathEnabled && LockedUpward && viewport.z > 0 && viewport.y <= 0) player.Die();
        }
        public void Restart()
        {
            LockedUpward = false;
            velocity = 0;
            transform.position = initialPosition;
        }
    }
}
