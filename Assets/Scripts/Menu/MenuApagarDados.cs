using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// Painel_ApagarDados: lista os jogos que tem save (um item por jogo, a partir do Apagar_Jogo_Tamplete,
// desativado) e oferece Apagar Tudo. Abrir e voltar ficam nos botoes da cena.
[DisallowMultipleComponent]
public sealed class MenuApagarDados : MonoBehaviour
{
    [Tooltip("Content do Scroll View da lista.")]
    [SerializeField] RectTransform conteudo;
    [Tooltip("Apagar_Jogo_Tamplete: fonte dos itens.")]
    [SerializeField] ItemApagarJogo modelo;
    [Tooltip("Catalogo de jogos (MenuCards do Area_Cards).")]
    [SerializeField] MenuCards catalogo;
    [Tooltip("Painel_Apagar_Tudo: sempre comeca fechado.")]
    [SerializeField] GameObject painelApagarTudo;
    [SerializeField] Button confirmarApagarTudo;

    readonly List<ItemApagarJogo> itens = new List<ItemApagarJogo>();

    void Awake()
    {
        if (confirmarApagarTudo) confirmarApagarTudo.onClick.AddListener(ApagarTudo);
    }

    void OnEnable()
    {
        if (painelApagarTudo) painelApagarTudo.SetActive(false);
        Montar();
    }

    // Refeita a cada abertura: o save pode ter mudado desde a ultima vez.
    void Montar()
    {
        if (modelo) modelo.gameObject.SetActive(false);
        foreach (var item in itens) if (item) Destroy(item.gameObject);
        itens.Clear();
        var mestre = ObjetoMestre.Instancia;
        if (!modelo || !conteudo || !catalogo || !mestre || !mestre.Save) return;
        int posicao = modelo.transform.GetSiblingIndex();
        foreach (var jogo in catalogo.JogosOrdenados)
        {
            if (string.IsNullOrWhiteSpace(jogo.id) || !mestre.Save.TemDadosDoJogo(jogo.id)) continue;
            var item = Instantiate(modelo, conteudo);
            item.name = "Apagar_" + jogo.id;
            item.transform.SetSiblingIndex(++posicao);
            item.gameObject.SetActive(true);
            item.Configurar(jogo, mestre.Idioma, ApagarJogo);
            itens.Add(item);
        }
    }

    void ApagarJogo(ItemApagarJogo item)
    {
        var mestre = ObjetoMestre.Instancia;
        if (!item || !mestre || !mestre.Save) return;
        if (!mestre.Save.ApagarDadosDoJogo(item.Jogo.id))
        {
            Debug.LogWarning($"[Apagar dados] Nao foi possivel apagar o save de '{item.Jogo.id}'.", this);
            return;
        }
        itens.Remove(item);
        Destroy(item.gameObject);
    }

    void ApagarTudo()
    {
        if (painelApagarTudo) painelApagarTudo.SetActive(false);
        var mestre = ObjetoMestre.Instancia;
        if (!mestre || !mestre.ApagarDados())
            Debug.LogWarning("[Apagar dados] Nao foi possivel apagar os dados agora.", this);
    }
}
