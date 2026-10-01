using UnityEngine;

namespace Lumera.JumpForce
{
    // The class name must match the file name, or Unity cannot load the component.
    // Quem detecta o toque é o JumpForcePlayer (funciona mesmo dentro de plataformas com Rigidbody),
    // pelo collider deste objeto: trigger (efeito de vento) ou sólido.
    [DisallowMultipleComponent]
    public sealed class JumpForceTrampolim : MonoBehaviour
    {
        [Header("Referência")]
        [SerializeField] private SphereCollider colisor;

        [Header("Força - velocidade de saída em m/s (não depende da massa)")]
        [SerializeField, Min(0)] private float forcaMinima = 5f;
        [SerializeField, Min(0)] private float forcaMaxima = 20f;

        [Tooltip("0 = batida na lateral, 1 = batida exatamente no topo.")]
        [SerializeField]
        private AnimationCurve curvaForca = new AnimationCurve(
            new Keyframe(0f, 0f),
            new Keyframe(0.5f, 0.15f),
            new Keyframe(0.8f, 0.5f),
            new Keyframe(1f, 1f)
        );

        [Header("Gizmo do campo de força")]
        [Tooltip("Mostra o leque mesmo sem o trampolim estar selecionado.")]
        [SerializeField] private bool gizmoSempreVisivel;
        [Tooltip("Metros de leque desenhados para cada m/s de força.")]
        [SerializeField, Min(0.001f)] private float gizmoMetrosPorForca = 0.1f;

        private void Reset() => colisor = GetComponent<SphereCollider>();

        private Vector3 Centro => colisor.transform.TransformPoint(colisor.center);

        // Colisão sólida: usa o ponto de contato real.
        public bool Impulsionar(JumpForcePlayer jogador, Collider outro, Vector3 pontoContato)
        {
            if (!colisor || outro != colisor || !CalcularImpulso(pontoContato, out Vector2 velocidade))
                return false;
            jogador.Launch(velocidade);
            return true;
        }

        // Trigger não tem ponto de contato: usa o ponto do corpo do jogador mais próximo do centro do trampolim.
        public bool Impulsionar(JumpForcePlayer jogador, Collider outro, Collider corpoJogador)
        {
            if (!colisor || outro != colisor)
                return false;
            Vector3 centro = Centro;
            Vector3 ponto = corpoJogador.ClosestPoint(centro);
            // Centro do trampolim já dentro do corpo: cai para o centro do corpo.
            if ((ponto - centro).sqrMagnitude < 0.0001f)
                ponto = corpoJogador.bounds.center;
            return Impulsionar(jogador, outro, ponto);
        }

        // Outros corpos físicos com colisão sólida (o jogador é tratado em Impulsionar). Só chega aqui
        // se o trampolim não estiver dentro de um Rigidbody de outro objeto.
        private void OnCollisionEnter(Collision collision)
        {
            if (!colisor || collision.contactCount == 0)
                return;

            ContactPoint contato = collision.GetContact(0);

            // Garante que a colisão aconteceu no collider do trampolim
            if (contato.thisCollider != colisor)
                return;

            Rigidbody rb = collision.rigidbody;
            if (rb == null || rb.TryGetComponent(out JumpForcePlayer _))
                return;

            if (CalcularImpulso(contato.point, out Vector2 velocidade))
                rb.AddForce((Vector3)velocidade, ForceMode.VelocityChange);
        }

        private bool CalcularImpulso(Vector3 pontoContato, out Vector2 velocidade)
        {
            velocidade = Vector2.zero;

            // O jogo é 2D no plano X/Y: a direção ignora o Z.
            Vector2 direcao = pontoContato - Centro;
            if (direcao.sqrMagnitude < 0.0001f)
                return false;
            direcao.Normalize();

            // Converte a direção para um ângulo:
            // 0°   = direita
            // 90°  = cima
            // 180° = esquerda
            float angulo = Mathf.Atan2(direcao.y, direcao.x) * Mathf.Rad2Deg;

            if (angulo < 0f)
                angulo += 360f;

            // Metade inferior não faz nada
            if (angulo > 180f)
                return false;

            velocidade = direcao * ForcaNoAngulo(angulo);
            return true;
        }

        // 0 nas laterais e 1 exatamente no topo
        private float ForcaNoAngulo(float angulo)
        {
            float proximidadeTopo = 1f - Mathf.Abs(angulo - 90f) / 90f;
            float intensidade = curvaForca.Evaluate(proximidadeTopo);
            return Mathf.Lerp(forcaMinima, forcaMaxima, intensidade);
        }

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            if (gizmoSempreVisivel) DesenharCampo();
        }

        private void OnDrawGizmosSelected()
        {
            if (!gizmoSempreVisivel) DesenharCampo();
        }

        // Leque no plano X/Y: cada fatia de 5° tem o comprimento e a cor da força naquele ângulo.
        private void DesenharCampo()
        {
            if (!colisor)
                return;
            const int fatias = 36;
            const float passo = 180f / fatias;
            Vector3 centro = Centro;
            Vector3 escala = colisor.transform.lossyScale;
            float raio = colisor.radius * Mathf.Max(Mathf.Abs(escala.x), Mathf.Abs(escala.y), Mathf.Abs(escala.z));
            float maximo = Mathf.Max(forcaMinima, forcaMaxima, 0.001f);
            var contorno = new Vector3[fatias + 1];
            var fraca = new Color(0.25f, 0.6f, 1f);
            var forte = new Color(1f, 0.3f, 0.1f);

            for (int i = 0; i <= fatias; i++)
            {
                float angulo = i * passo;
                float forca = ForcaNoAngulo(angulo);
                Vector3 direcao = Quaternion.Euler(0, 0, angulo) * Vector3.right;
                contorno[i] = centro + direcao * (raio + forca * gizmoMetrosPorForca);
                if (i == fatias)
                    break;
                // Cor e tamanho pela força no meio da fatia.
                float forcaMeio = ForcaNoAngulo(angulo + passo * 0.5f);
                var cor = Color.Lerp(fraca, forte, forcaMeio / maximo);
                cor.a = 0.35f;
                UnityEditor.Handles.color = cor;
                UnityEditor.Handles.DrawSolidArc(centro, Vector3.forward, direcao, passo, raio + forcaMeio * gizmoMetrosPorForca);
            }

            UnityEditor.Handles.color = Color.white;
            UnityEditor.Handles.DrawPolyLine(contorno);
            // Metade de baixo: sem efeito.
            UnityEditor.Handles.color = new Color(0.5f, 0.5f, 0.5f, 0.6f);
            UnityEditor.Handles.DrawWireArc(centro, Vector3.forward, Vector3.left, 180f, raio);

            UnityEditor.Handles.Label(contorno[fatias / 2], $"{ForcaNoAngulo(90f):0.#} m/s");
            UnityEditor.Handles.Label(contorno[0], $"{ForcaNoAngulo(0f):0.#} m/s");
            UnityEditor.Handles.Label(contorno[fatias], $"{ForcaNoAngulo(180f):0.#} m/s");
        }
#endif
    }
}
