using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace Lumera.JumpForce
{
    public sealed class JumpForceHUD : MonoBehaviour
    {
        public JumpForceSpawner spawner;
        public JumpForceWardrobe wardrobe;
        public JumpForcePlayer player;
        public JumpForceCamera followCamera;
        public Text status;
        public GameObject deathPanel;
        public Button restartButton;
        [Tooltip("Controles de toque (Controles: Button_Esquerda, Joystick_Pular, Button_Direita): ocultos no vestiario e apos a morte.")]
        public GameObject[] controlesToque = System.Array.Empty<GameObject>();
        bool showedDeath;
        int restartFrame = -1;
        void Update()
        {
            if (!player) return;
            if (wardrobe && wardrobe.IsOpen)
            {
                MostrarControles(false);
                if (status) status.text = "Escolha sua cor. Toque fora para jogar.\nControle: quadrado escolhe, X inicia. Teclado: Espaco ou Enter inicia.";
                return;
            }
            if (player.Dead && showedDeath && player.input.AnyPressed) { Restart(); return; }
            MostrarControles(!player.Dead);
            if (status) status.text = player.Dead ? "" : player.Charging
                ? "Arraste PULAR: direcao e forca do salto. Solte para saltar; no centro cancela."
                : player.Grounded ? "Mova para andar. Segure e arraste PULAR para carregar o salto." : "Controle a direcao no ar";
            deathPanel.SetActive(player.Dead);
            if (player.Dead && !showedDeath && EventSystem.current)
                EventSystem.current.SetSelectedGameObject(restartButton.gameObject);
            showedDeath = player.Dead;
        }
        public void MostrarControles(bool visiveis)
        {
            foreach (var controle in controlesToque)
                if (controle && controle.activeSelf != visiveis) controle.SetActive(visiveis);
        }

        public void Restart()
        {
            if (restartFrame == Time.frameCount) return;
            restartFrame = Time.frameCount;
            player.Restart();
            if (player.score) player.score.ResetRun();
            if (spawner) spawner.RestartTrail();
            followCamera.Restart();
            deathPanel.SetActive(false);
            showedDeath = false;
            if (EventSystem.current) EventSystem.current.SetSelectedGameObject(null);
            if (wardrobe && wardrobe.reopenOnRetry) wardrobe.Begin();
        }
    }
}
