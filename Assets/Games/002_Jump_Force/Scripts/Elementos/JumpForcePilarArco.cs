using UnityEngine;

namespace Lumera.JumpForce
{
    // Obstáculo sólido que vai e volta em um único eixo do mundo (X ou Y). Não é atravessável:
    // o pilar empurra o personagem, e o JumpForcePlayer não anda para dentro dele.
    // O topo pode contar como chão: uma superfície JumpForcePlatform é criada ao iniciar, então pousar,
    // pular e ser carregado funcionam como em qualquer plataforma.
    [DefaultExecutionOrder(-300), DisallowMultipleComponent, RequireComponent(typeof(Rigidbody))]
    public sealed class JumpForcePilarArco : MonoBehaviour
    {
        public enum Eixo { X, Y }

        [Header("Motor fisico")]
        [Min(0.1f)] public float motorForce = 500f;
        [Min(0.1f)] public float brakeForce = 1200f;
        [Min(0.01f)] public float brakeDistance = 0.35f;
        [Min(0.01f)] public float endpointTolerance = 0.03f;

        public Eixo eixo = Eixo.X;
        [Tooltip("Distância em metros que o pilar percorre para cada lado. Zero mantém o pilar parado.")]
        [Min(0)] public float amplitude = 2;
        [Tooltip("Segundos para ir e voltar.")]
        [Min(0.1f)] public float period = 4;
        [Range(0, 1)] public float phase;
        [Tooltip("Sorteia, ao iniciar, se o pilar vai primeiro para o lado positivo ou negativo do eixo.")]
        public bool randomStartDirection = true;

        [Header("Topo como chão")]
        public bool topoContaComoChao = true;
        [Tooltip("Largura da área de apoio, em fração da largura do collider. Em topo arredondado, menos que 1 evita pisar no ar.")]
        [Range(0.1f, 1)] public float larguraTopo = 0.7f;

        const float EspessuraTopo = 0.1f;
        Rigidbody body;
        Vector3 localOrigin;
        float direction = 1;

        Vector3 Axis => eixo == Eixo.X ? Vector3.right : Vector3.up;
        // Follows the parent (e.g. a moving platform) while oscillating along the world axis.
        Vector3 Origin => transform.parent ? transform.parent.TransformPoint(localOrigin) : localOrigin;

        void Awake()
        {
            if (!TryGetComponent(out body))
                body = gameObject.AddComponent<Rigidbody>();

            body.isKinematic = false;
            body.useGravity = false;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

            RigidbodyConstraints constraints =
                RigidbodyConstraints.FreezeRotation |
                RigidbodyConstraints.FreezePositionZ;

            if (eixo == Eixo.X)
                constraints |= RigidbodyConstraints.FreezePositionY;
            else
                constraints |= RigidbodyConstraints.FreezePositionX;

            body.constraints = constraints;

            localOrigin = transform.localPosition;

            if (!transform.parent)
                localOrigin = body.position;

            if (randomStartDirection)
                direction = Random.value < 0.5f ? -1f : 1f;

            if (topoContaComoChao)
                CriarTopo();
        }

        void FixedUpdate()
        {
            float distanciaMaxima = Mathf.Max(0f, amplitude);

            if (distanciaMaxima <= 0.0001f)
            {
                Vector3 velocidade = body.linearVelocity;
                velocidade -= Axis * Vector3.Dot(velocidade, Axis);
                body.linearVelocity = velocidade;
                return;
            }

            Vector3 eixoMovimento = Axis;
            Vector3 destino =
                Origin + eixoMovimento * distanciaMaxima * direction;

            float distanciaRestante =
                Vector3.Dot(
                    destino - body.position,
                    eixoMovimento
                ) * direction;

            float velocidadeNoEixo =
                Vector3.Dot(
                    body.linearVelocity,
                    eixoMovimento
                );

            float velocidadeMaxima =
                Mathf.Max(
                    0.1f,
                    4f * distanciaMaxima / Mathf.Max(0.1f, period)
                );

            // Chegou ao extremo.
            if (distanciaRestante <= endpointTolerance)
            {
                Vector3 velocidade = body.linearVelocity;

                velocidade -=
                    eixoMovimento * velocidadeNoEixo;

                body.linearVelocity = velocidade;
                body.position = destino;

                direction = -direction;
                return;
            }

            // Freio curto e forte próximo ao extremo.
            if (distanciaRestante <= brakeDistance)
            {
                // Só freia enquanto ainda estiver indo em direção ao destino.
                if (velocidadeNoEixo * direction > 0.01f)
                {
                    body.AddForce(
                        -eixoMovimento *
                        Mathf.Sign(velocidadeNoEixo) *
                        brakeForce,
                        ForceMode.Force
                    );
                }
                else
                {
                    // Se o freio já fez o corpo começar a voltar antes da hora,
                    // mantém uma pequena correção em direção ao extremo.
                    body.AddForce(
                        eixoMovimento *
                        direction *
                        motorForce,
                        ForceMode.Force
                    );
                }

                return;
            }

            float velocidadeDesejada =
                velocidadeMaxima * direction;

            float erroVelocidade =
                velocidadeDesejada - velocidadeNoEixo;

            if (Mathf.Abs(erroVelocidade) > 0.01f)
            {
                body.AddForce(
                    eixoMovimento *
                    Mathf.Sign(erroVelocidade) *
                    motorForce,
                    ForceMode.Force
                );
            }
        }

        bool TopoArea(out Vector3 centro, out Vector3 tamanho)
        {
            centro = tamanho = Vector3.zero;
            bool achou = false;
            Bounds bounds = default;
            foreach (var c in GetComponentsInChildren<Collider>())
            {
                if (c.isTrigger || c.GetComponent<JumpForcePlatform>()) continue;
                if (!achou) { bounds = c.bounds; achou = true; }
                else bounds.Encapsulate(c.bounds);
            }
            if (!achou) return false;
            tamanho = new Vector3(bounds.size.x * larguraTopo, EspessuraTopo, bounds.size.z * larguraTopo);
            centro = new Vector3(bounds.center.x, bounds.max.y - EspessuraTopo * 0.5f, bounds.center.z);
            return true;
        }

        void CriarTopo()
        {
            if (!TopoArea(out Vector3 centro, out Vector3 tamanho)) return;
            var topo = new GameObject("Topo_Chao");
            topo.layer = gameObject.layer;
            topo.transform.SetParent(transform, false);
            topo.transform.SetPositionAndRotation(centro, Quaternion.identity);
            var escala = topo.transform.lossyScale;
            var box = topo.AddComponent<BoxCollider>();
            box.size = new Vector3(tamanho.x / Mathf.Abs(escala.x), tamanho.y / Mathf.Abs(escala.y), tamanho.z / Mathf.Abs(escala.z));
            topo.AddComponent<JumpForcePlatform>();
        }

#if UNITY_EDITOR
        // Path and both extremes of the pillar, drawn from where it is placed (or started, in Play).
        void OnDrawGizmosSelected()
        {
            Vector3 origin = Application.isPlaying && body ? Origin : transform.position;
            float shift = Mathf.Sin(phase * 2 * Mathf.PI);
            Vector3 a = origin + Axis * (amplitude * (-1 - shift));
            Vector3 b = origin + Axis * (amplitude * (1 - shift));
            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(a, b);
            var colliders = GetComponentsInChildren<Collider>();
            if (colliders.Length == 0) return;
            Bounds bounds = colliders[0].bounds;
            foreach (var c in colliders) bounds.Encapsulate(c.bounds);
            Vector3 fromPivot = bounds.center - transform.position;
            Gizmos.color = new Color(1f, 0.85f, 0f, 0.5f);
            Gizmos.DrawWireCube(a + fromPivot, bounds.size);
            Gizmos.DrawWireCube(b + fromPivot, bounds.size);
            // Standable area on the top.
            if (topoContaComoChao && !Application.isPlaying && TopoArea(out Vector3 centro, out Vector3 tamanho))
            {
                Gizmos.color = new Color(0.2f, 1f, 0.3f, 0.9f);
                Gizmos.DrawWireCube(centro, tamanho);
            }
        }
#endif
    }
}
