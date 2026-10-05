using UnityEngine;
using UnityEngine.Serialization;

namespace Lumera.JumpForce
{
    // Impulso instantaneo na direcao do eixo Y local do trampolim.
    // O jogador recebe o impulso no proprio Rigidbody (JumpForcePlayer.Launch).
    [DisallowMultipleComponent]
    public sealed class JumpForceTrampolim : MonoBehaviour
    {
        [Tooltip("Velocidade de saida em m/s ao longo do eixo Y local (substitui a velocidade nessa direcao). Negativo impulsiona para o -Y local.")]
        [SerializeField, FormerlySerializedAs("forcaMaxima")]
        private float forca = 20f;

        public Vector2 Impulso
        {
            get
            {
                Vector2 eixo = transform.up;

                return eixo.sqrMagnitude > 0.0001f
                    ? eixo.normalized * forca
                    : Vector2.zero;
            }
        }

        public void Impulsionar(JumpForcePlayer jogador)
        {
            if (!jogador)
                return;

            Vector2 impulso = Impulso;

            if (impulso == Vector2.zero)
                return;

            jogador.Launch(impulso);
            JumpForceEventos.AvisarTrampolim();
        }

        // Corpos fisicos que nao sao o jogador recebem o mesmo impulso diretamente.
        // O jogador e tratado por JumpForcePlayer para preservar seus estados de gameplay.
        void OnCollisionEnter(Collision collision)
        {
            Rigidbody rb = collision.rigidbody;

            if (rb == null || rb.TryGetComponent(out JumpForcePlayer _))
                return;

            rb.AddForce((Vector3)Impulso, ForceMode.VelocityChange);
        }

        void OnDrawGizmosSelected()
        {
            Vector3 impulso = Impulso;

            if (impulso == Vector3.zero)
                return;

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
