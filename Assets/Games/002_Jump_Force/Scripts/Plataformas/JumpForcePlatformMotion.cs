using UnityEngine;

namespace Lumera.JumpForce
{
    // Plataforma movel por forca (Rigidbody dinamico, sem gravidade): vai e volta em um eixo do plano X/Y.
    // Colocada na cena, usa Amplitude e Period daqui; gerada pelo Spawner, recebe o plano do gerador em Configurar.
    [DefaultExecutionOrder(-300), DisallowMultipleComponent, RequireComponent(typeof(JumpForcePlatform), typeof(Rigidbody))]
    public sealed class JumpForcePlatformMotion : MonoBehaviour
    {
        [Header("Motor fisico (newtons: a massa da plataforma e de quem esta em cima contam)")]
        [Tooltip("Forca maxima para acelerar ate a velocidade de cruzeiro.")]
        [Min(0.1f)] public float motorForce = 900f;
        [Tooltip("Forca maxima do freio perto dos extremos. Abaixo do atrito do jogador (cerca de 10 m/s2 de desaceleracao) ele e levado sem escorregar.")]
        [Min(0.1f)] public float brakeForce = 1200f;
        [Tooltip("Distancia ao extremo que ja conta como chegada (inverte o sentido).")]
        [Min(0.01f)] public float endpointTolerance = 0.03f;

        [Header("Percurso (plataforma colocada na cena)")]
        [Tooltip("Amplitude em metros no espaco global, para cada lado. Zero mantem a plataforma parada.")]
        public Vector3 amplitude = Vector3.zero;
        [Tooltip("Segundos para ir e voltar; define a velocidade maxima (4 x amplitude / periodo).")]
        [Min(0.1f)] public float period = 4;
        [Range(0, 1)] public float phase;
        [Tooltip("Sorteia, ao iniciar, se a plataforma vai primeiro no sentido da amplitude ou no oposto.")]
        public bool randomStartDirection = true;

        public Vector3 Centro => centro;
        public float VelocidadeMaxima => velocidadeMaxima;

        Rigidbody body;
        Vector3 centro, eixo = Vector3.right;
        float distancia, velocidadeMaxima, sentido = 1;

        void Awake()
        {
            body = GetComponent<Rigidbody>();
            body.isKinematic = false;
            body.useGravity = false;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            bool horizontal = Mathf.Abs(amplitude.x) >= Mathf.Abs(amplitude.y);
            float metros = horizontal ? Mathf.Abs(amplitude.x) : Mathf.Abs(amplitude.y);
            Configurar(body.position, horizontal ? Vector3.right : Vector3.up, metros,
                4f * metros / Mathf.Max(0.1f, period), randomStartDirection ? (Random.value < 0.5f ? -1 : 1) : 1);
        }

        // centro: meio do percurso. eixo: Vector3.right ou Vector3.up. distancia: metros para cada lado (0 = parada).
        // Coloca a plataforma no centro, parada, com as travas do eixo.
        public void Configurar(Vector3 novoCentro, Vector3 novoEixo, float novaDistancia, float novaVelocidade, int novoSentido)
        {
            if (!body) body = GetComponent<Rigidbody>();
            centro = novoCentro;
            eixo = novoEixo.sqrMagnitude > 0.0001f ? novoEixo.normalized : Vector3.right;
            distancia = Mathf.Max(0, novaDistancia);
            velocidadeMaxima = Mathf.Max(0.1f, novaVelocidade);
            sentido = novoSentido < 0 ? -1 : 1;
            amplitude = eixo * distancia;
            body.constraints = JumpForceMotorFisico.Travas(eixo, distancia);
            body.position = centro;
            if (!body.isKinematic)
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }
        }

        void FixedUpdate() =>
            JumpForceMotorFisico.Aplicar(body, centro, eixo, distancia, velocidadeMaxima, motorForce, brakeForce, endpointTolerance, ref sentido);

#if UNITY_EDITOR
        void OnDrawGizmosSelected()
        {
            Vector3 meio = Application.isPlaying ? centro : transform.position;
            Vector3 lado = Application.isPlaying ? eixo * distancia : amplitude;
            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(meio - lado, meio + lado);
        }
#endif
    }
}
