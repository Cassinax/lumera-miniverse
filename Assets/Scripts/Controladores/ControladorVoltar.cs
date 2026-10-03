using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

// Filho "Voltar_Controller" do Objeto Mestre. Le o Voltar em qualquer cena: ESC do teclado (o botao Voltar
// do Android chega como ESC) e "O" do controle (botao leste). Quem decide o que acontece e a AcaoVoltar ativa.
[DisallowMultipleComponent]
public sealed class ControladorVoltar : MonoBehaviour
{
    ObjetoMestre mestre;
    bool dropdownAberto;

    void Awake() => mestre = GetComponentInParent<ObjetoMestre>();

    void Update()
    {
        if (!Pressionado()) return;
        // Sem acao durante carregamento. Dropdown aberto: o proprio dropdown fecha com o Cancelar da interface.
        if ((mestre && mestre.CarregandoCena) || dropdownAberto) return;
        AcaoVoltar.ExecutarAtual();
    }

    // Estado do quadro anterior: no quadro do Voltar o dropdown pode ja ter fechado.
    // Dropdown aberto seleciona um item da propria lista, que e filha dele.
    void LateUpdate()
    {
        var sistema = EventSystem.current;
        var selecionado = sistema ? sistema.currentSelectedGameObject : null;
        var dropdown = selecionado ? selecionado.GetComponentInParent<TMP_Dropdown>() : null;
        dropdownAberto = dropdown && dropdown.IsExpanded;
    }

    static bool Pressionado()
    {
        var teclado = Keyboard.current;
        if (teclado != null && teclado.escapeKey.wasPressedThisFrame) return true;
        var controle = Gamepad.current;
        return controle != null && controle.buttonEast.wasPressedThisFrame;
    }
}
