using UnityEngine;

namespace Lumera.JumpForce
{
    // Impulso instantaneo na direcao do eixo X local do ventilador.
    // O trigger/contato apenas adiciona velocidade ao Rigidbody do jogador.
    [DisallowMultipleComponent]
    public sealed class JumpForceVentilador : MonoBehaviour
    {
        [Tooltip("Mudanca de velocidade em m/s ao longo do eixo X local. Negativo empurra para o -X local.")]
        [SerializeField] private float forca = -12f;

        public Vector2 Empurrao
        {
            get
            {
                Vector2 eixo = transform.right;

                return eixo.sqrMagnitude > 0.0001f
                    ? eixo.normalized * forca
                    : Vector2.zero;
            }
        }

        public void Empurrar(JumpForcePlayer jogador)
        {
            if (!jogador)
                return;

            Vector2 empurrao = Empurrao;

            if (empurrao == Vector2.zero)
                return;

            jogador.Launch(empurrao);
            JumpForceEventos.AvisarVento();
        }

        void OnDrawGizmosSelected()
        {
            Vector3 empurrao = Empurrao;

            if (empurrao == Vector3.zero)
                return;

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
