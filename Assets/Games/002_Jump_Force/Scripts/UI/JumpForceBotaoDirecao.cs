using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Lumera.JumpForce
{
    // Button_Esquerda / Button_Direita: botoes fixos de direcao. Segurar anda e, carregando o salto, gira a mira
    // (como as setas do teclado). Funciona junto com o Button_Pular, cada um com seu dedo.
    [DisallowMultipleComponent]
    public sealed class JumpForceBotaoDirecao : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
    {
        public JumpForcePlayer player;
        [Tooltip("-1 = esquerda da tela, 1 = direita da tela.")]
        [SerializeField] int lado = 1;
        readonly HashSet<int> dedos = new HashSet<int>();

        JumpForceInput Input => player ? player.input : null;

        public void OnPointerDown(PointerEventData dados)
        {
            dedos.Add(dados.pointerId);
            Input?.SetDirectionSource(this, lado, true);
        }

        public void OnPointerUp(PointerEventData dados)
        {
            dedos.Remove(dados.pointerId);
            if (dedos.Count == 0) Input?.SetDirectionSource(this, lado, false);
        }

        void OnDisable()
        {
            dedos.Clear();
            Input?.SetDirectionSource(this, lado, false);
        }
    }
}
