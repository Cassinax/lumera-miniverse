using UnityEngine;

namespace Lumera.JumpForce
{
    [DefaultExecutionOrder(-300), DisallowMultipleComponent, RequireComponent(typeof(JumpForcePlatform), typeof(Rigidbody))]
    public sealed class JumpForcePlatformMotion : MonoBehaviour
    {
        Rigidbody body;

        [Header("Motor fisico")]
        [Min(0.1f)] public float motorForce = 500f;
        [Min(0.1f)] public float brakeForce = 1200f;
        [Min(0.01f)] public float brakeDistance = 0.35f;
        [Min(0.01f)] public float endpointTolerance = 0.03f;

        [Tooltip("Amplitude em metros no espaco global. Zero mantem a plataforma parada.")]
        public Vector3 amplitude = Vector3.zero;
        [Min(0.1f)] public float period = 4;
        [Range(0, 1)] public float phase;
        [Tooltip("Sorteia, ao iniciar, se a plataforma vai primeiro no sentido da amplitude ou no oposto.")]
        public bool randomStartDirection = true;
        Vector3 origin;
        Quaternion initialRotation;
        float elapsed, direction = 1;

        void Awake()
        {
            body = GetComponent<Rigidbody>();

            origin = body.position;
            initialRotation = body.rotation;

            body.isKinematic = false;
            body.useGravity = false;
            body.interpolation = RigidbodyInterpolation.Interpolate;

            // A plataforma se move em apenas um eixo do plano X/Y.
            // O eixo Z permanece sempre travado.
            RigidbodyConstraints constraints =
                RigidbodyConstraints.FreezeRotation |
                RigidbodyConstraints.FreezePositionZ;

            if (Mathf.Abs(amplitude.x) >= Mathf.Abs(amplitude.y))
                constraints |= RigidbodyConstraints.FreezePositionY;
            else
                constraints |= RigidbodyConstraints.FreezePositionX;

            body.constraints = constraints;

            if (randomStartDirection)
                direction = Random.value < 0.5f ? -1f : 1f;
        }

        void FixedUpdate()
        {
            Vector3 eixo;
            float distanciaMaxima;

            if (Mathf.Abs(amplitude.x) >= Mathf.Abs(amplitude.y))
            {
                eixo = Vector3.right;
                distanciaMaxima = Mathf.Abs(amplitude.x);
            }
            else
            {
                eixo = Vector3.up;
                distanciaMaxima = Mathf.Abs(amplitude.y);
            }

            if (distanciaMaxima <= 0.0001f)
            {
                body.linearVelocity = Vector3.zero;
                return;
            }

            Vector3 destino = origin + eixo * distanciaMaxima * direction;

            float distanciaRestante =
                Vector3.Dot(destino - body.position, eixo) * direction;

            float velocidadeNoEixo =
                Vector3.Dot(body.linearVelocity, eixo);

            // Velocidade de cruzeiro equivalente ao antigo "period":
            // percorre ida e volta aproximadamente dentro do período configurado.
            float velocidadeMaxima =
                Mathf.Max(0.1f, 4f * distanciaMaxima / Mathf.Max(0.1f, period));

            // Chegou ao extremo: para e começa o retorno.
            if (distanciaRestante <= endpointTolerance)
            {
                Vector3 velocidade = body.linearVelocity;
                velocidade -= eixo * velocidadeNoEixo;
                body.linearVelocity = velocidade;

                body.position = destino;

                direction = -direction;
                return;
            }

            // Freio curto e forte perto do destino.
            if (distanciaRestante <= brakeDistance)
            {
                if (Mathf.Abs(velocidadeNoEixo) > 0.01f)
                {
                    body.AddForce(
                        -eixo * Mathf.Sign(velocidadeNoEixo) * brakeForce,
                        ForceMode.Force
                    );
                }

                return;
            }

            // Motor: tenta atingir a velocidade de cruzeiro no sentido atual.
            float velocidadeDesejada = velocidadeMaxima * direction;
            float erroVelocidade = velocidadeDesejada - velocidadeNoEixo;

            if (Mathf.Abs(erroVelocidade) > 0.01f)
            {
                body.AddForce(
                    eixo * Mathf.Sign(erroVelocidade) * motorForce,
                    ForceMode.Force
                );
            }
        }
        public void SetOrigin(Vector3 newOrigin)
        {
            origin = newOrigin;

            if (!body)
                body = GetComponent<Rigidbody>();

            if (body)
            {
                body.position = newOrigin;
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;

                initialRotation = body.rotation;
            }
            else
            {
                transform.position = newOrigin;
                initialRotation = transform.rotation;
            }

            direction = randomStartDirection
                ? (Random.value < 0.5f ? -1f : 1f)
                : 1f;
        }
    }
}
