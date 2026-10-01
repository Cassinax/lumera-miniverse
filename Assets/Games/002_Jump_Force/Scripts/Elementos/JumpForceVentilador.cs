using UnityEngine;

namespace Lumera.JumpForce
{
    // Empurrão fixo ao longo do eixo X local. Sem variação de força nem cálculo de ângulo.
    // Quem detecta o toque é o JumpForcePlayer (funciona mesmo dentro de plataformas com Rigidbody),
    // pelo collider deste objeto: trigger (recomendado) ou sólido.
    [DisallowMultipleComponent]
    public sealed class JumpForceVentilador : MonoBehaviour
    {
        [Tooltip("Velocidade do empurrão em m/s ao longo do eixo X local. Negativo empurra para o -X. Não depende da massa.")]
        [SerializeField] private float forca = -12f;

        // O jogo é 2D no plano X/Y: o Z do eixo é descartado.
        public Vector2 Empurrao
        {
            get
            {
                Vector2 eixo = transform.right;
                return eixo.sqrMagnitude > 0.0001f ? eixo.normalized * forca : Vector2.zero;
            }
        }

        public void Empurrar(JumpForcePlayer jogador)
        {
            Vector2 empurrao = Empurrao;
            if (empurrao != Vector2.zero)
                jogador.Launch(empurrao);
        }

        private void OnDrawGizmosSelected()
        {
            Vector3 empurrao = Empurrao;
            if (empurrao == Vector3.zero)
                return;
            // Seta do tamanho de 1 metro a cada 5 m/s, no sentido real do empurrão.
            Vector3 inicio = transform.position;
            Vector3 fim = inicio + empurrao / 5f;
            Vector3 lado = Vector3.Cross(empurrao.normalized, Vector3.forward) * 0.2f;
            Vector3 recuo = -empurrao.normalized * 0.35f;
            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(inicio, fim);
            Gizmos.DrawLine(fim, fim + recuo + lado);
            Gizmos.DrawLine(fim, fim + recuo - lado);
        }
    }
}
