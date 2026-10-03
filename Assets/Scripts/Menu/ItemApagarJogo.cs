using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Um jogo com save na lista do Painel_ApagarDados (Apagar_Jogo_Tamplete). O Button e o Button_Negar ja
// alternam o Painel_Confirmar pela cena; aqui so o Button_Confirmar, que apaga o save daquele jogo.
[DisallowMultipleComponent]
public sealed class ItemApagarJogo : MonoBehaviour
{
    [SerializeField] Image capa;
    [SerializeField] TMP_Text nome;
    [Tooltip("Button que abre a confirmacao.")]
    [SerializeField] GameObject botaoApagar;
    [SerializeField] GameObject painelConfirmar;
    [SerializeField] Button confirmar;

    public JogoLumera Jogo { get; private set; }
    Action<ItemApagarJogo> aoConfirmar;

    void Awake()
    {
        if (confirmar) confirmar.onClick.AddListener(() => aoConfirmar?.Invoke(this));
    }

    // Toda vez que aparece, comeca sem a confirmacao aberta.
    void OnEnable()
    {
        if (painelConfirmar) painelConfirmar.SetActive(false);
        if (botaoApagar) botaoApagar.SetActive(true);
    }

    public void Configurar(JogoLumera jogo, IdiomaLumera idioma, Action<ItemApagarJogo> confirmado)
    {
        Jogo = jogo;
        aoConfirmar = confirmado;
        // Sem capa propria, fica a padrao do template.
        if (capa && jogo.capa) capa.sprite = jogo.capa;
        if (nome) nome.text = jogo.nome.Obter(idioma);
    }
}
