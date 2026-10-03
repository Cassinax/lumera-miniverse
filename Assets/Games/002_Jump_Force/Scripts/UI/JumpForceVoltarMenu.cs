using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Lumera.JumpForce
{
    // Panel_Volta_Menu: confirmar encerra a partida e volta ao Menu; negar fecha o painel.
    // Com o painel aberto o jogo fica pausado, para o jogador nao morrer enquanto decide.
    [DisallowMultipleComponent]
    public sealed class JumpForceVoltarMenu : MonoBehaviour
    {
        [SerializeField] Button confirmar;
        [SerializeField] Button negar;
        [SerializeField] JumpForcePlataforma plataforma;

        float escalaTempoAnterior = 1;

        void Awake()
        {
            if (confirmar) confirmar.onClick.AddListener(Voltar);
            if (negar) negar.onClick.AddListener(() => gameObject.SetActive(false));
        }

        void OnEnable()
        {
            escalaTempoAnterior = Time.timeScale > 0 ? Time.timeScale : 1;
            Time.timeScale = 0;
        }

        void OnDisable() => Time.timeScale = escalaTempoAnterior;

        void Voltar()
        {
            Time.timeScale = escalaTempoAnterior;
            if (plataforma) plataforma.EncerrarPartida("menu");
            if (ObjetoMestre.Instancia) ObjetoMestre.Instancia.VoltarAoMenu();
            else SceneManager.LoadScene(ObjetoMestre.CenaMenu); // Cena aberta direto no Editor.
        }
    }
}
