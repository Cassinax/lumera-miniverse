using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Lumera.JumpForce
{
    // Button_Pular: segurar carrega, soltar salta (o toque duplo da carga rapida vem do JumpForcePlayer).
    // Funciona junto com os botoes de direcao, cada um com seu dedo.
    [DisallowMultipleComponent]
    public sealed class JumpForceBotaoPulo : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
    {
        public JumpForcePlayer player;
        readonly HashSet<int> dedos = new HashSet<int>();

        public void OnPointerDown(PointerEventData dados)
        {
            dedos.Add(dados.pointerId);
            if (player && player.input) player.input.SetJumpSource(this, true);
        }

        public void OnPointerUp(PointerEventData dados)
        {
            dedos.Remove(dados.pointerId);
            if (dedos.Count == 0 && player && player.input) player.input.SetJumpSource(this, false);
        }

        void OnDisable()
        {
            dedos.Clear();
            if (player && player.input) player.input.SetJumpSource(this, false);
        }
    }
}
