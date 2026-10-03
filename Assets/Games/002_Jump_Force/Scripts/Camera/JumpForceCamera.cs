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
            released = false;
            Restart();
        }
        public void ReleaseFromWardrobe()
        {
            InWardrobe = false;
            released = true;
            // Keep the scene's gameplay framing, then follow changes in the player's height.
            followOffsetCorrection = player ? initialPosition.y - player.FeetY - heightOffset : 0;
            Restart();
        }
        // How far below the last support the player can fall before the death rule triggers, in the
        // gameplay framing (also before leaving the wardrobe, when the route is first planned).
        public float SurvivableFall()
        {
            if (!deathEnabled || !player || !view) return float.PositiveInfinity;
            if (!capsule) capsule = player.GetComponent<CapsuleCollider>();
            float depth = Mathf.Abs(player.transform.position.z - initialPosition.z);
            float halfHeight = view.orthographic ? view.orthographicSize : depth * Mathf.Tan(view.fieldOfView * 0.5f * Mathf.Deg2Rad);
            float cameraAboveFeet = released ? FollowHeight : initialPosition.y - player.FeetY;
            // Capsule size from its settings: bounds can be stale while physics is off in the wardrobe.
            float bodyHeight = capsule.height * Mathf.Abs(capsule.transform.lossyScale.y);
            return halfHeight - cameraAboveFeet + bodyHeight * deathBodyFraction;
        }
        // Feet height of the last support the player stood on. After the first platform the camera follows
        // jumps up and back down, but never below this floor: missing it means falling into the abyss.
        public float FloorFeet => LockedUpward ? floorFeet : float.NegativeInfinity;
        public float LowestY => LockedUpward ? floorFeet + FollowHeight : float.NegativeInfinity;
        float FollowHeight => heightOffset + followOffsetCorrection;
        // True when no part of the bounds can appear on screen again, since the camera never goes below LowestY.
        public bool OutOfReachBelow(Bounds bounds, float viewportMargin)
        {
            if (!LockedUpward || !view) return false;
            // Seen from the lowest camera, everything sits this much higher. Never test from above the current
            // camera: something visible now must not disappear while the camera is still rising to the floor.
            float lift = Mathf.Max(0, transform.position.y - LowestY);
            for (int i = 0; i < 8; i++)
            {
                var sign = new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1);
                var screen = view.WorldToViewportPoint(bounds.center + Vector3.Scale(bounds.extents, sign) + Vector3.up * lift);
                if (screen.z <= view.nearClipPlane || screen.y >= -viewportMargin) return false;
            }
            return true;
        }
        float followOffsetCorrection, floorFeet;
        bool released;
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
        // Tremor leve (feedback de pouso forte, trampolim). Desfeito antes do seguimento, para nao contaminar a
        // suavizacao nem a regra do piso.
        public void Tremer(float intensidade, float duracao)
        {
            if (intensidade <= 0 || duracao <= 0) return;
            tremorIntensidade = Mathf.Max(tremorIntensidade * TremorRestante, intensidade);
            tremorDuracao = duracao;
            tremorFim = Time.unscaledTime + duracao;
        }
        float tremorIntensidade, tremorDuracao, tremorFim;
        Vector3 tremorAplicado;
        float TremorRestante => tremorDuracao > 0 ? Mathf.Clamp01((tremorFim - Time.unscaledTime) / tremorDuracao) : 0;

        void LateUpdate()
        {
            transform.position -= tremorAplicado;
            tremorAplicado = Vector3.zero;
            if (!InWardrobe && (!player || player.Dead)) return;
            Vector3 target = wardrobePosition;
            if (!InWardrobe)
            {
                LockedUpward |= player.ReachedPlatform;
                // Standing on something moves the floor to it (also down, with a sinking pillar); in the air it holds.
                if (player.Grounded) floorFeet = player.FeetY;
                float feet = LockedUpward ? Mathf.Max(player.FeetY, floorFeet) : player.FeetY;
                target = new Vector3(initialPosition.x, feet + FollowHeight, initialPosition.z);
            }

            // Anchor changes only redirect the camera; all displacement happens smoothly here.
            Vector3 position = Vector3.SmoothDamp(transform.position, target, ref velocity, Mathf.Max(0.01f, smoothTime));
            // Coming back down from a jump, the camera stops at the floor instead of overshooting below it.
            if (!InWardrobe && LockedUpward && position.y < LowestY && position.y < transform.position.y)
            {
                position.y = Mathf.Min(transform.position.y, LowestY);
                velocity.y = Mathf.Max(0, velocity.y);
            }
            transform.position = position;
            float restante = TremorRestante;
            if (restante > 0 && !InWardrobe)
            {
                var deslocamento = Random.insideUnitCircle * tremorIntensidade * restante;
                tremorAplicado = new Vector3(deslocamento.x, deslocamento.y, 0);
                transform.position += tremorAplicado;
            }
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
            floorFeet = player ? player.FeetY : 0;
        }
    }
}
