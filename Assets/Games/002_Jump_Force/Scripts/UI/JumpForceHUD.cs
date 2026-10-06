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
        public GameObject victoryPanel;
        public Button victoryRestartButton;
        public Text victoryText;
        [Tooltip("Controles de toque (Controles: Button_Esquerda, Joystick_Pular, Button_Direita): ocultos no vestiario e apos a morte.")]
        public GameObject[] controlesToque = System.Array.Empty<GameObject>();
        public GameObject joystickPular;
        public GameObject botaoPularSimples;
        bool showedDeath, showedVictory;
        int restartFrame = -1;
        void Update()
        {
            if (!player) return;
            if (player.Victory)
            {
                MostrarControles(false);
                deathPanel.SetActive(false);
                if (victoryPanel) victoryPanel.SetActive(true);
                if (status) status.text = "";
                if (victoryText) victoryText.text = "Vitoria!\nVoce chegou ao nivel " + player.score.Points +
                    "!\nRecompensa: " + Mathf.Max(0, player.score.victoryReward) + " moedas";
                if (!showedVictory && victoryRestartButton && EventSystem.current)
                    EventSystem.current.SetSelectedGameObject(victoryRestartButton.gameObject);
                else if (showedVictory && ReinicioVitoriaPressionado()) { Restart(); return; }
                showedVictory = true;
                return;
            }
            if (wardrobe && wardrobe.IsOpen)
            {
                MostrarControles(false);
                if (status) status.text = "Escolha sua cor. Toque fora para jogar.\nControle: quadrado escolhe, X inicia. Teclado: Espaco ou Enter inicia.";
                return;
            }
#if UNITY_EDITOR
            if (player.EmVooDev)
            {
                MostrarControles(false);
                deathPanel.SetActive(false);
                if (status) status.text = "Modo dev: setas movem em X/Y. Espaco retoma a fisica.";
                return;
            }
#endif
            if (player.Dead && showedDeath && player.input.AnyPressed) { Restart(); return; }
            MostrarControles(!player.Dead);
            if (status) status.text = player.Dead ? "" : player.Charging
                ? "Arraste PULAR: direcao e forca do salto. Solte para saltar; no centro cancela."
                : player.PuloSimplesDisponivel ? "Toque PULAR para saltar para cima."
                : player.JoystickPuloDisponivel ? "Segure e arraste PULAR para carregar o salto."
                : player.Grounded ? "Pare para poder pular." : "Controle a direcao no ar";
            deathPanel.SetActive(player.Dead);
            if (player.Dead && !showedDeath && EventSystem.current)
                EventSystem.current.SetSelectedGameObject(restartButton.gameObject);
            showedDeath = player.Dead;
        }
        static bool ReinicioVitoriaPressionado()
        {
            var teclado = UnityEngine.InputSystem.Keyboard.current;
            var controle = UnityEngine.InputSystem.Gamepad.current;
            return (teclado != null && (teclado.enterKey.wasPressedThisFrame || teclado.spaceKey.wasPressedThisFrame)) ||
                (controle != null && controle.buttonSouth.wasPressedThisFrame);
        }

        public void MostrarControles(bool visiveis)
        {
            foreach (var controle in controlesToque)
                if (controle && controle != joystickPular && controle != botaoPularSimples &&
                    controle.activeSelf != visiveis) controle.SetActive(visiveis);
            DefinirVisibilidade(joystickPular, visiveis && player && player.JoystickPuloDisponivel);
            DefinirVisibilidade(botaoPularSimples, visiveis && player && player.PuloSimplesDisponivel);
        }

        static void DefinirVisibilidade(GameObject controle, bool visivel)
        {
            if (controle && controle.activeSelf != visivel) controle.SetActive(visivel);
        }

        public void Restart()
        {
            if (restartFrame == Time.frameCount) return;
            restartFrame = Time.frameCount;
            if (player.score && player.score.victoryFloat) player.score.victoryFloat.Restore();
            player.Restart();
            if (player.score) player.score.ResetRun();
            if (spawner) spawner.RestartTrail();
            followCamera.Restart();
            deathPanel.SetActive(false);
            showedDeath = showedVictory = false;
            if (victoryPanel) victoryPanel.SetActive(false);
            if (EventSystem.current) EventSystem.current.SetSelectedGameObject(null);
            if (wardrobe && wardrobe.reopenOnRetry) wardrobe.Begin();
        }
    }
}
