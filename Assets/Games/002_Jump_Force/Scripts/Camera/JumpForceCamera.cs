using UnityEngine;

namespace Lumera.JumpForce
{
    [DisallowMultipleComponent, RequireComponent(typeof(Camera))]
    public sealed class JumpForceCamera : MonoBehaviour
    {
        public JumpForcePlayer player;
        [Tooltip("Tempo de suavizacao ate a ancoragem atual, em segundos.")]
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
            wardrobePosition = position;
            InWardrobe = true;
            Restart();
        }
        public void ReleaseFromWardrobe()
        {
            InWardrobe = false;
            // Keep the scene's gameplay framing, then follow changes in the player's height.
            followOffsetCorrection = player ? initialPosition.y - player.FeetY - heightOffset : 0;
            Restart();
        }
        float followOffsetCorrection;
        Vector3 initialPosition, wardrobePosition, velocity;
        Camera view;
        Transform[] wallTransforms;
        Vector3[] wallOrigins;
        CapsuleCollider capsule;
        void Awake()
        {
            initialPosition = transform.position;
            wardrobePosition = initialPosition;
            view = GetComponent<Camera>();
            if (player)
            {
                capsule = player.GetComponent<CapsuleCollider>();
                wallTransforms = new Transform[player.invisibleWalls.Length];
                wallOrigins = new Vector3[wallTransforms.Length];
                for (int i = 0; i < wallTransforms.Length; i++)
                {
                    var wall = player.invisibleWalls[i];
                    if (!wall || !wall.transform.IsChildOf(transform)) continue;
                    wallTransforms[i] = wall.transform;
                    wallOrigins[i] = wall.transform.position;
                }
            }
        }
        void LateUpdate()
        {
            if (!InWardrobe && (!player || player.Dead)) return;
            Vector3 target = wardrobePosition;
            if (!InWardrobe)
            {
                LockedUpward |= player.ReachedPlatform;
                target = new Vector3(initialPosition.x, player.FeetY + heightOffset + followOffsetCorrection, initialPosition.z);
                if (LockedUpward) target.y = Mathf.Max(target.y, transform.position.y);
            }

            // Anchor changes only redirect the camera; all displacement happens smoothly here.
            Vector3 position = Vector3.SmoothDamp(transform.position, target, ref velocity, Mathf.Max(0.01f, smoothTime));
            if (!InWardrobe && LockedUpward && position.y < transform.position.y)
            {
                position.y = transform.position.y;
                velocity.y = Mathf.Max(0, velocity.y);
            }
            transform.position = position;
            // The walls follow height only. Wardrobe X/Z transitions must not move the gameplay corridor.
            if (wallTransforms != null)
                for (int i = 0; i < wallTransforms.Length; i++)
                    if (wallTransforms[i])
                    {
                        var wallPosition = wallTransforms[i].position;
                        wallPosition.x = wallOrigins[i].x;
                        wallPosition.z = wallOrigins[i].z;
                        wallTransforms[i].position = wallPosition;
                    }
            if (InWardrobe) return;

            if (!capsule) capsule = player.GetComponent<CapsuleCollider>();
            // Dies once the chosen fraction of the capsule (half, by default) is below the lower edge.
            Bounds body = capsule.bounds;
            float cutY = body.min.y + body.size.y * deathBodyFraction;
            Vector3 viewport = view.WorldToViewportPoint(new Vector3(player.transform.position.x, cutY, player.transform.position.z));
            if (deathEnabled && LockedUpward && viewport.z > 0 && viewport.y <= 0) player.Die();
        }
        public void Restart()
        {
            LockedUpward = false;
            velocity = Vector3.zero;
        }
    }
}
