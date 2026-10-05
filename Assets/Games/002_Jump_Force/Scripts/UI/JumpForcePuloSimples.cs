using UnityEngine;
using UnityEngine.EventSystems;

namespace Lumera.JumpForce
{
    [DisallowMultipleComponent]
    public sealed class JumpForcePuloSimples : MonoBehaviour, IPointerDownHandler
    {
        public JumpForcePlayer player;

        public void OnPointerDown(PointerEventData dados)
        {
            if (player && player.PuloSimplesDisponivel)
                player.BeginCharge();
        }
    }
}
