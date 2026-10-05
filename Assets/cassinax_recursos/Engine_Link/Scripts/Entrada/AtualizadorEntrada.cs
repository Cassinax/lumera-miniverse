#if UNITY_EDITOR
using UnityEngine;

namespace Cassinax.EngineLink
{
    /// <summary>
    /// Avanca o estado de <see cref="EngineLinkInput"/> no inicio de cada quadro do Play Mode,
    /// antes dos scripts do jogo. Criado pela parte de Editor; nao precisa ir para cenas.
    /// </summary>
    [AddComponentMenu("")]
    [DefaultExecutionOrder(-32000)]
    public sealed class AtualizadorEntrada : MonoBehaviour
    {
        void Update()
        {
            EngineLinkInput.AvancarQuadro();
        }
    }
}
#endif
