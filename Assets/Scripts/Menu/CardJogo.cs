using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Card de um jogo no Menu (Card_Tamplete). O botao funcional e o proprio card; o color tint vai na capa.
[DisallowMultipleComponent, RequireComponent(typeof(Button))]
public sealed class CardJogo : MonoBehaviour
{
    [SerializeField] Image capa;
    [SerializeField] TMP_Text nome;
    [Tooltip("Objeto Preco (moeda + texto). Fica oculto nos jogos gratis.")]
    [SerializeField] GameObject preco;
    [SerializeField] TMP_Text textoPreco;
    [Tooltip("Interativo/Button_Jogar: so decorativo. Perde a interacao para nao roubar o toque do card.")]
    [SerializeField] Button botaoDecorativo;

    public JogoLumera Jogo { get; private set; }
    public event Action<CardJogo> Escolhido;

    void Awake()
    {
        var botao = GetComponent<Button>();
        if (capa)
        {
            botao.targetGraphic = capa;
            botao.transition = Selectable.Transition.ColorTint;
        }
        // Um Button filho consome o clique mesmo sem acao; desligado, o toque sobe ate o card.
        if (botaoDecorativo)
        {
            botaoDecorativo.enabled = false;
            if (botaoDecorativo.targetGraphic) botaoDecorativo.targetGraphic.raycastTarget = false;
        }
        botao.onClick.AddListener(() => Escolhido?.Invoke(this));
    }

    public void Configurar(JogoLumera jogo, IdiomaLumera idioma)
    {
        Jogo = jogo;
        // Sem capa propria, fica a padrao do template.
        if (capa && jogo.capa) capa.sprite = jogo.capa;
        if (preco) preco.SetActive(!jogo.Gratis);
        if (textoPreco) textoPreco.text = jogo.preco.ToString();
        Traduzir(idioma);
    }

    public void Traduzir(IdiomaLumera idioma)
    {
        if (nome && Jogo) nome.text = Jogo.nome.Obter(idioma);
    }
}
