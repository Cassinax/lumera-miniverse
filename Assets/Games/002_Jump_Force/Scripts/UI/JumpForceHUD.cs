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
        public GameObject touchControls;
        bool showedDeath;
        int restartFrame = -1;
        void Update()
        {
            if (!player) return;
            if (player.Dead && showedDeath && player.input.AnyPressed) { Restart(); return; }
            if (touchControls) touchControls.SetActive(!player.Dead);
            charge.gameObject.SetActive(player.Charging);
            charge.value = player.Charge01;
            status.text = player.Dead ? "" : player.Grounded
                ? "Mova para andar. Segure PULAR e solte para saltar."
                : "Controle a direcao no ar";
            deathPanel.SetActive(player.Dead);
            if (player.Dead && !showedDeath && EventSystem.current)
                EventSystem.current.SetSelectedGameObject(restartButton.gameObject);
            showedDeath = player.Dead;
        }
        public void Restart()
        {
            if (restartFrame == Time.frameCount) return;
            restartFrame = Time.frameCount;
            player.Restart();
            followCamera.Restart();
            deathPanel.SetActive(false);
            showedDeath = false;
            if (EventSystem.current) EventSystem.current.SetSelectedGameObject(null);
        }
    }
}
