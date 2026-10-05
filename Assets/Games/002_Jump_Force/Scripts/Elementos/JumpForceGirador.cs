using UnityEngine;

namespace Lumera.JumpForce
{
    // The class name must match the file name, or Unity cannot load the component.
    public sealed class JumpForceGirador : MonoBehaviour
    {
        [SerializeField] private Vector3 velocidadeRotacao;

        public void DefinirVelocidadeY(float grausPorSegundo) => velocidadeRotacao.y = grausPorSegundo;
        public Vector3 VelocidadeRotacao => velocidadeRotacao;

        private void Update()
        {
            transform.Rotate(velocidadeRotacao * Time.deltaTime, Space.Self);
        }
    }
}
