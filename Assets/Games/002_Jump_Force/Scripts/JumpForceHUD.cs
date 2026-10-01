using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace Lumera.JumpForce
{
    public sealed class JumpForceHUD : MonoBehaviour
    {
        public JumpForcePlayer player;
        public JumpForceCamera followCamera;
        public Text status;
        public Slider charge;
        public GameObject deathPanel;
        public Button restartButton;
        bool showedDeath;
        void Update()
        {
            if (!player) return;
            charge.gameObject.SetActive(player.Charging);
            charge.value = player.Charge01;
            status.text = player.Dead ? "" : player.Grounded
                ? "Segure para carregar o pulo. Solte para saltar."
                : "Esquerda / direita: contornar a plataforma";
            deathPanel.SetActive(player.Dead);
            if (player.Dead && !showedDeath && EventSystem.current)
                EventSystem.current.SetSelectedGameObject(restartButton.gameObject);
            showedDeath = player.Dead;
        }
        public void Restart()
        {
            player.Restart();
            followCamera.Restart();
            deathPanel.SetActive(false);
            showedDeath = false;
            if (EventSystem.current) EventSystem.current.SetSelectedGameObject(null);
        }
    }
}
