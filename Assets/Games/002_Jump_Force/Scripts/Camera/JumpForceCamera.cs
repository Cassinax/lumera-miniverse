using System;
using UnityEngine;

namespace Lumera.JumpForce
{
    // Estados de foco da camera. Cada um aponta para uma ancora (filha do Ancorador_Camera).
    public enum JumpForceFocoCamera { Jogo, Vestiario }

    // Fica no Ancorador_Camera, o controlador mestre da camera. Dois movimentos separados:
    // - o ancorador segue o personagem (so a altura; X e Z ficam onde ele esta na cena), mais o Offset;
    // - a Main Camera (filha) vai suavemente ate a ancora do estado de foco atual (posicao e rotacao).
    // As paredes invisiveis, filhas do ancorador, acompanham a altura junto com ele.
    [DisallowMultipleComponent]
    public sealed class JumpForceCamera : MonoBehaviour
    {
        [Serializable]
        public struct Ancora
        {
            public JumpForceFocoCamera foco;
            [Tooltip("Pose da Main Camera neste foco. De preferencia filha do Ancorador_Camera.")]
            public Transform ponto;
            [Tooltip("Tempo de suavizacao da camera ate esta ancora, em segundos.")]
            [Min(0.01f)] public float tempo;
        }

        public JumpForcePlayer player;
        [Tooltip("Main Camera, filha do ancorador. Vazio: a primeira camera filha.")]
        public Camera visao;

        [Header("Seguimento do personagem (ancorador)")]
        [Tooltip("Somado a posicao seguida: Y = altura sobre os pes do personagem; X e Z deslocam a partir da posicao do ancorador na cena.")]
        public Vector3 offset = Vector3.zero;
        [Tooltip("Tempo de suavizacao do ancorador ate a posicao seguida, em segundos.")]
        [Min(0.01f)] public float smoothTime = 0.3f;

        [Tooltip("Atraso de altura desejado em subidas rapidas. Reduz o tempo de suavizacao conforme a velocidade de subida.")]
        [Min(0.1f)] public float atrasoMaximoSubida = 2f;

        [Header("Foco (ancoras da camera)")]
        [Tooltip("Uma ancora por estado de foco. Foco sem ancora usa a de Jogo.")]
        public Ancora[] ancoras = Array.Empty<Ancora>();
        public JumpForceFocoCamera Foco { get; private set; } = JumpForceFocoCamera.Jogo;

        [Header("Morte")]
        [Tooltip("Fracao do corpo que precisa sair pela borda de baixo da tela para o personagem morrer.")]
        [Range(0.05f, 1)] public float deathBodyFraction = 0.5f;
        public bool deathEnabled = true;

        public Camera Visao => visao;
        public bool LockedUpward { get; private set; }
        public bool InWardrobe => Foco == JumpForceFocoCamera.Vestiario;

        public void DefinirFoco(JumpForceFocoCamera foco) => Foco = foco;
        public void HoldForWardrobe()
        {
            DefinirFoco(JumpForceFocoCamera.Vestiario);
            Restart();
        }
        public void ReleaseFromWardrobe()
        {
            DefinirFoco(JumpForceFocoCamera.Jogo);
            Restart();
        }

        // How far below the last support the player can fall before the death rule triggers, in the
        // gameplay framing (also before leaving the wardrobe, when the route is first planned).
        public float SurvivableFall()
        {
            if (!deathEnabled || !player || !visao) return float.PositiveInfinity;
            if (!capsule) capsule = player.GetComponent<CapsuleCollider>();
            Vector3 cameraJogo = PoseLocal(JumpForceFocoCamera.Jogo, out _);
            float depth = Mathf.Abs(player.transform.position.z - (initialPosition.z + offset.z + cameraJogo.z));
            float halfHeight = visao.orthographic ? visao.orthographicSize : depth * Mathf.Tan(visao.fieldOfView * 0.5f * Mathf.Deg2Rad);
            float cameraAboveFeet = offset.y + cameraJogo.y;
            // Capsule size from its settings: bounds can be stale while physics is off in the wardrobe.
            float bodyHeight = capsule.height * Mathf.Abs(capsule.transform.lossyScale.y);
            return halfHeight - cameraAboveFeet + bodyHeight * deathBodyFraction;
        }
        // Feet height of the last support the player stood on. After the first platform the camera follows
        // jumps up and back down, but never below this floor: missing it means falling into the abyss.
        public float FloorFeet => LockedUpward ? floorFeet : float.NegativeInfinity;
        // Lowest height of the ancorador (the camera rides on it).
        public float LowestY => LockedUpward ? floorFeet + offset.y : float.NegativeInfinity;
        // True when no part of the bounds can appear on screen again, since the ancorador never goes below LowestY.
        public bool OutOfReachBelow(Bounds bounds, float viewportMargin)
        {
            if (!LockedUpward || !visao) return false;
            // Seen from the lowest camera, everything sits this much higher. Never test from above the current
            // camera: something visible now must not disappear while the camera is still rising to the floor.
            float lift = Mathf.Max(0, transform.position.y - LowestY);
            for (int i = 0; i < 8; i++)
            {
                var sign = new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1);
                var screen = visao.WorldToViewportPoint(bounds.center + Vector3.Scale(bounds.extents, sign) + Vector3.up * lift);
                if (screen.z <= visao.nearClipPlane || screen.y >= -viewportMargin) return false;
            }
            return true;
        }

#if UNITY_EDITOR
        bool estavaEmVooDev;
#endif
        float floorFeet;
        Vector3 initialPosition, velocidadeCamera;
        Vector3 cameraInicial;
        Quaternion rotacaoCameraInicial = Quaternion.identity;
        CapsuleCollider capsule;

        void Awake()
        {
            initialPosition = transform.position;
            if (!visao) visao = GetComponentInChildren<Camera>(true);
            if (visao)
            {
                cameraInicial = visao.transform.localPosition;
                rotacaoCameraInicial = visao.transform.localRotation;
            }
            if (player) capsule = player.GetComponent<CapsuleCollider>();
        }

        // Pose da ancora no espaco do ancorador. Sem ancora para o foco: a de Jogo; sem nenhuma: a pose inicial da camera.
        Vector3 PoseLocal(JumpForceFocoCamera foco, out Quaternion rotacao)
        {
            if (TryAncora(foco, out var ancora) || TryAncora(JumpForceFocoCamera.Jogo, out ancora))
            {
                rotacao = Quaternion.Inverse(transform.rotation) * ancora.ponto.rotation;
                return transform.InverseTransformPoint(ancora.ponto.position);
            }
            rotacao = rotacaoCameraInicial;
            return cameraInicial;
        }
        bool TryAncora(JumpForceFocoCamera foco, out Ancora encontrada)
        {
            foreach (var ancora in ancoras)
                if (ancora.foco == foco && ancora.ponto) { encontrada = ancora; return true; }
            encontrada = default;
            return false;
        }
        float TempoFoco => TryAncora(Foco, out var ancora) && ancora.tempo > 0 ? ancora.tempo : smoothTime;

        // Tremor leve (feedback de pouso forte, trampolim). Desfeito antes da suavizacao, para nao contaminar
        // o foco nem a regra do piso.
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
            if (visao) visao.transform.localPosition -= tremorAplicado;
            tremorAplicado = Vector3.zero;
            if (!InWardrobe && (!player || player.Dead)) return;
#if UNITY_EDITOR
            if (estavaEmVooDev && (!player || !player.EmVooDev)) Restart();
            estavaEmVooDev = player && player.EmVooDev;
#endif
            Seguir();
            Focar();
#if UNITY_EDITOR
            if (player && player.EmVooDev) return;
#endif
            if (InWardrobe || !visao) return;

            float restante = TremorRestante;
            if (restante > 0)
            {
                var deslocamento = UnityEngine.Random.insideUnitCircle * tremorIntensidade * restante;
                tremorAplicado = new Vector3(deslocamento.x, deslocamento.y, 0);
                visao.transform.localPosition += tremorAplicado;
            }

            if (!capsule) capsule = player.GetComponent<CapsuleCollider>();
            // Dies once the chosen fraction of the capsule (half, by default) is below the lower edge.
            Bounds body = capsule.bounds;
            float cutY = body.min.y + body.size.y * deathBodyFraction;
            Vector3 viewport = visao.WorldToViewportPoint(new Vector3(player.transform.position.x, cutY, player.transform.position.z));
            if (deathEnabled && LockedUpward && viewport.z > 0 && viewport.y <= 0) player.Die();
        }

        // Ancorador: segue a altura dos pes do personagem, com o piso da ultima plataforma.
        void Seguir()
        {
            if (!player) return;
            float feet = player.FeetY;
#if UNITY_EDITOR
            if (player.EmVooDev) LockedUpward = false;
#endif
            if (!InWardrobe
#if UNITY_EDITOR
                && !player.EmVooDev
#endif
            )
            {
                LockedUpward |= player.ReachedPlatform;
                // Standing on something moves the floor to it (also down, with a sinking pillar); in the air it holds.
                if (player.Grounded) floorFeet = player.FeetY;
                if (LockedUpward) feet = Mathf.Max(feet, floorFeet);
            }
            var target = new Vector3(initialPosition.x, feet, initialPosition.z) + offset;
            float tempo = Mathf.Max(0.01f, smoothTime);
            if (!InWardrobe && player.Body && player.Body.linearVelocity.y > 0f)
                tempo = Mathf.Min(tempo, Mathf.Max(0.01f, atrasoMaximoSubida / player.Body.linearVelocity.y));
            // Proportional following: (target - current) * response * dt.
            // Exponential form keeps the response consistent across frame rates and never overshoots.
            Vector3 diferenca = target - transform.position;
            float fracao = 1f - Mathf.Exp(-Time.deltaTime / tempo);
            Vector3 position = transform.position + diferenca * fracao;
            // Coming back down from a jump, the ancorador stops at the floor instead of overshooting below it.
            if (!InWardrobe && LockedUpward && position.y < LowestY && position.y < transform.position.y)
            {
                position.y = Mathf.Min(transform.position.y, LowestY);
            }
            transform.position = position;
        }

        // Main Camera: vai suavemente ate a ancora do foco atual. Trocar de foco so muda o destino.
        void Focar()
        {
            if (!visao) return;
            Vector3 alvo = PoseLocal(Foco, out var rotacao);
            float tempo = Mathf.Max(0.01f, TempoFoco);
            var camera = visao.transform;
            camera.localPosition = Vector3.SmoothDamp(camera.localPosition, alvo, ref velocidadeCamera, tempo);
            camera.localRotation = Quaternion.Slerp(camera.localRotation, rotacao, 1 - Mathf.Exp(-3 * Time.deltaTime / tempo));
        }

        public void Restart()
        {
            LockedUpward = false;
            floorFeet = player ? player.FeetY : 0;
            transform.position = new Vector3(initialPosition.x, floorFeet, initialPosition.z) + offset;
            velocidadeCamera = Vector3.zero;
        }
    }
}
