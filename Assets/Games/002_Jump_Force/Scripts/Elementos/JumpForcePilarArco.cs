using UnityEngine;

namespace Lumera.JumpForce
{
    // Obstáculo sólido que vai e volta em um único eixo do mundo (X ou Y), movido por força (Rigidbody dinâmico,
    // sem gravidade). Não é atravessável: o pilar empurra o personagem.
    // O topo pode contar como chão: uma superfície JumpForcePlatform é criada ao iniciar, então pousar,
    // pular e ser levado funcionam como em qualquer plataforma.
    // Colocado na cena, usa Eixo, Amplitude e Period daqui; gerado pelo Spawner, recebe o plano em Configurar.
    [DefaultExecutionOrder(-300), DisallowMultipleComponent, RequireComponent(typeof(Rigidbody))]
    public sealed class JumpForcePilarArco : MonoBehaviour
    {
        public enum Eixo { X, Y }

        [Header("Motor fisico (newtons: a massa do pilar e de quem esta em cima contam)")]
        [Tooltip("Forca maxima para acelerar ate a velocidade de cruzeiro. No eixo Y tambem sustenta o jogador em cima.")]
        [Min(0.1f)] public float motorForce = 3000f;
        [Tooltip("Forca maxima do freio perto dos extremos.")]
        [Min(0.1f)] public float brakeForce = 5000f;
        [Tooltip("Distancia ao extremo que ja conta como chegada (inverte o sentido).")]
        [Min(0.01f)] public float endpointTolerance = 0.03f;

        [Header("Percurso (pilar colocado na cena)")]
        public Eixo eixo = Eixo.X;
        [Tooltip("Distância em metros que o pilar percorre para cada lado. Zero mantém o pilar parado.")]
        [Min(0)] public float amplitude = 2;
        [Tooltip("Segundos para ir e voltar; define a velocidade maxima (4 x amplitude / periodo).")]
        [Min(0.1f)] public float period = 4;
        [Range(0, 1)] public float phase;
        [Tooltip("Sorteia, ao iniciar, se o pilar vai primeiro para o lado positivo ou negativo do eixo.")]
        public bool randomStartDirection = true;

        [Header("Topo como chão")]
        public bool topoContaComoChao = true;
        [Tooltip("Largura da área de apoio, em fração da largura do collider. Em topo arredondado, menos que 1 evita pisar no ar.")]
        [Range(0.1f, 1)] public float larguraTopo = 0.7f;
        [Tooltip("Physics Material do topo (onde o jogador fica em pe). A lateral usa o material do collider do pilar.")]
        public PhysicsMaterial materialTopo;

        const float EspessuraTopo = 0.1f;
        Rigidbody body;
        Vector3 centro;
        float velocidadeMaxima, sentido = 1;

        Vector3 Axis => eixo == Eixo.X ? Vector3.right : Vector3.up;

        void Awake()
        {
            if (!TryGetComponent(out body))
                body = gameObject.AddComponent<Rigidbody>();

            body.isKinematic = false;
            body.useGravity = false;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

            if (topoContaComoChao)
                CriarTopo();

            Configurar(body.position, eixo, amplitude, 4f * amplitude / Mathf.Max(0.1f, period),
                randomStartDirection ? (Random.value < 0.5f ? -1 : 1) : 1);
        }

        // novoCentro: meio do percurso. novaAmplitude: metros para cada lado (0 = parado).
        // Coloca o pilar no centro, parado, com as travas do eixo.
        public void Configurar(Vector3 novoCentro, Eixo novoEixo, float novaAmplitude, float novaVelocidade, int novoSentido)
        {
            if (!body) body = GetComponent<Rigidbody>();
            centro = novoCentro;
            eixo = novoEixo;
            amplitude = Mathf.Max(0, novaAmplitude);
            velocidadeMaxima = Mathf.Max(0.1f, novaVelocidade);
            sentido = novoSentido < 0 ? -1 : 1;
            body.constraints = JumpForceMotorFisico.Travas(Axis, amplitude);
            body.position = centro;
            if (!body.isKinematic)
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }
        }

        void FixedUpdate() =>
            JumpForceMotorFisico.Aplicar(body, centro, Axis, amplitude, velocidadeMaxima, motorForce, brakeForce, endpointTolerance, ref sentido);

        bool TopoArea(out Vector3 centroTopo, out Vector3 tamanho)
        {
            centroTopo = tamanho = Vector3.zero;
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
            centroTopo = new Vector3(bounds.center.x, bounds.max.y - EspessuraTopo * 0.5f, bounds.center.z);
            return true;
        }

        void CriarTopo()
        {
            if (!TopoArea(out Vector3 centroTopo, out Vector3 tamanho)) return;
            var topo = new GameObject("Topo_Chao");
            topo.layer = gameObject.layer;
            topo.transform.SetParent(transform, false);
            topo.transform.SetPositionAndRotation(centroTopo, Quaternion.identity);
            var escala = topo.transform.lossyScale;
            var box = topo.AddComponent<BoxCollider>();
            box.sharedMaterial = materialTopo;
            box.size = new Vector3(tamanho.x / Mathf.Abs(escala.x), tamanho.y / Mathf.Abs(escala.y), tamanho.z / Mathf.Abs(escala.z));
            topo.AddComponent<JumpForcePlatform>();
        }

#if UNITY_EDITOR
        // Path and both extremes of the pillar, drawn from where it is placed (or its center, in Play).
        void OnDrawGizmosSelected()
        {
            Vector3 origin = Application.isPlaying && body ? centro : transform.position;
            Vector3 a = origin - Axis * amplitude;
            Vector3 b = origin + Axis * amplitude;
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
            if (topoContaComoChao && !Application.isPlaying && TopoArea(out Vector3 centroTopo, out Vector3 tamanho))
            {
                Gizmos.color = new Color(0.2f, 1f, 0.3f, 0.9f);
                Gizmos.DrawWireCube(centroTopo, tamanho);
            }
        }
#endif
    }
}
