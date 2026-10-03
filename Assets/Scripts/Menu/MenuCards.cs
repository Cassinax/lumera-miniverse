using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Events;

// Area_Cards: cria um card por jogo a partir do Card_Tamplete (desativado ao iniciar).
// O Card_EmBreve fica sempre por ultimo.
[DisallowMultipleComponent]
public sealed class MenuCards : MonoBehaviour
{
    [Tooltip("Content do Scroll View, onde os cards ficam.")]
    [SerializeField] RectTransform conteudo;
    [Tooltip("Card_Tamplete: fonte de todos os cards.")]
    [SerializeField] CardJogo modelo;
    [SerializeField] RectTransform cardEmBreve;
    [Tooltip("Jogos da plataforma. A posicao vem do campo Ordem de cada jogo.")]
    [SerializeField] List<JogoLumera> jogos = new List<JogoLumera>();
    [Tooltip("Chamado quando o jogador toca num jogo sem moedas para a entrada (ex.: abrir oferta de anuncio).")]
    [SerializeField] UnityEvent<JogoLumera> aoFaltarMoedas = new UnityEvent<JogoLumera>();

    readonly List<CardJogo> cards = new List<CardJogo>();
    ObjetoMestre mestre;

    // Ordem padrao do catalogo (campo Ordem); um filtro futuro pode reordenar aqui. Tambem usada pela lista de saves.
    public IEnumerable<JogoLumera> JogosOrdenados => jogos.Where(j => j).Distinct().OrderBy(j => j.ordem);

    void Start()
    {
        if (modelo) modelo.gameObject.SetActive(false);
        mestre = ObjetoMestre.Instancia;
        if (mestre) mestre.IdiomaAlterado += Traduzir;
        Montar();
    }

    void OnDestroy()
    {
        if (mestre) mestre.IdiomaAlterado -= Traduzir;
    }

    void Montar()
    {
        if (!modelo || !conteudo) return;
        var idioma = LocalizacaoLumera.Atual;
        foreach (var jogo in JogosOrdenados)
        {
            var card = Instantiate(modelo, conteudo);
            card.name = "Card_" + (string.IsNullOrEmpty(jogo.id) ? jogo.name : jogo.id);
            card.gameObject.SetActive(true);
            card.Configurar(jogo, idioma);
            card.Escolhido += AoEscolher;
            cards.Add(card);
        }
        if (cardEmBreve) cardEmBreve.SetAsLastSibling();
    }

    void Traduzir(IdiomaLumera idioma)
    {
        foreach (var card in cards) card.Traduzir(idioma);
    }

    // Cada abertura cobra a entrada da partida (preco do jogo).
    void AoEscolher(CardJogo card)
    {
        var jogo = card.Jogo;
        if (!jogo) return;
        if (!mestre)
        {
            Debug.LogWarning("[Menu] Sem Objeto Mestre: o jogo deve ser aberto a partir do Menu.", this);
            return;
        }
        if (mestre.EntrarNoJogo(jogo) == ObjetoMestre.ResultadoEntrada.MoedasInsuficientes)
        {
            Debug.Log($"[Menu] Moedas insuficientes para '{jogo.id}' ({jogo.preco}).", this);
            aoFaltarMoedas.Invoke(jogo);
        }
    }
}
