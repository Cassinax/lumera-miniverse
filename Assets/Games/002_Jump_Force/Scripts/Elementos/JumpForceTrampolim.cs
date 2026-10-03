using UnityEngine;
using UnityEngine.Serialization;

namespace Lumera.JumpForce
{
    // The class name must match the file name, or Unity cannot load the component.
    // Empurrão fixo ao longo do eixo Y local (para cima), como o ventilador: sem cálculo de área nem de ângulo,
    // qualquer toque lança com a força toda.
    // Quem detecta o toque é o JumpForcePlayer (funciona mesmo dentro de plataformas com Rigidbody),
    // por qualquer collider deste objeto: trigger ou sólido.
    [DisallowMultipleComponent]
    public sealed class JumpForceTrampolim : MonoBehaviour
    {
        [Tooltip("Velocidade de saída em m/s ao longo do eixo Y local. Negativo lança para o -Y. Não depende da massa.")]
        [SerializeField, FormerlySerializedAs("forcaMaxima")] private float forca = 20f;

        // O jogo é 2D no plano X/Y: o Z do eixo é descartado.
        public Vector2 Impulso
        {
            get
            {
                Vector2 eixo = transform.up;
                return eixo.sqrMagnitude > 0.0001f ? eixo.normalized * forca : Vector2.zero;
            }
        }

        public void Impulsionar(JumpForcePlayer jogador)
        {
            Vector2 impulso = Impulso;
            if (impulso == Vector2.zero)
                return;
            jogador.Launch(impulso);
            JumpForceEventos.AvisarTrampolim();
        }

        // Outros corpos físicos com colisão sólida (o jogador é tratado em Impulsionar). Só chega aqui
        // se o trampolim não estiver dentro de um Rigidbody de outro objeto.
        private void OnCollisionEnter(Collision collision)
        {
            Rigidbody rb = collision.rigidbody;
            if (rb == null || rb.TryGetComponent(out JumpForcePlayer _))
                return;
            rb.AddForce((Vector3)Impulso, ForceMode.VelocityChange);
        }

        private void OnDrawGizmosSelected()
        {
            Vector3 impulso = Impulso;
            if (impulso == Vector3.zero)
                return;
            // Seta do tamanho de 1 metro a cada 5 m/s, no sentido real do impulso.
            Vector3 inicio = transform.position;
            Vector3 fim = inicio + impulso / 5f;
            Vector3 lado = Vector3.Cross(impulso.normalized, Vector3.forward) * 0.2f;
            Vector3 recuo = -impulso.normalized * 0.35f;
            Gizmos.color = Color.green;
            Gizmos.DrawLine(inicio, fim);
            Gizmos.DrawLine(fim, fim + recuo + lado);
            Gizmos.DrawLine(fim, fim + recuo - lado);
        }
    }
}
