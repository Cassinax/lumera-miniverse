using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

// O que o Voltar (ESC, botao Voltar do Android, "O" do controle) faz enquanto este objeto esta ativo.
// Responde a acao ativa de maior prioridade; empate: a ativada por ultimo. Cada cena tem uma acao padrao
// (prioridade baixa) e cada painel aberto, a sua.
[DisallowMultipleComponent]
public sealed class AcaoVoltar : MonoBehaviour
{
    [Tooltip("Maior responde primeiro. Acao padrao da cena: -1. Paineis dentro de paineis: maior que o pai.")]
    [SerializeField] int prioridade;
    [Tooltip("Botao cujo clique o Voltar repete (ex.: Button_Voltar, Button_Negar). Tem precedencia sobre o evento.")]
    [SerializeField] Button botao;
    [Tooltip("Usado quando nao ha botao. Sem botao e sem evento: desativa este objeto (fecha o painel).")]
    [SerializeField] UnityEvent aoVoltar = new UnityEvent();

    public UnityEvent AoVoltar => aoVoltar;

    static readonly List<AcaoVoltar> ativas = new List<AcaoVoltar>();
    static int contador;
    int ordem;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Limpar()
    {
        ativas.Clear();
        contador = 0;
    }

    void OnEnable()
    {
        ordem = ++contador;
        ativas.Add(this);
    }

    void OnDisable() => ativas.Remove(this);

    // Falso quando nenhuma acao esta ativa na cena.
    public static bool ExecutarAtual()
    {
        AcaoVoltar atual = null;
        foreach (var acao in ativas)
            if (acao && (!atual || acao.prioridade > atual.prioridade ||
                (acao.prioridade == atual.prioridade && acao.ordem > atual.ordem)))
                atual = acao;
        if (!atual) return false;
        atual.Voltar();
        return true;
    }

    public void Voltar()
    {
        if (botao)
        {
            if (botao.IsActive() && botao.IsInteractable()) botao.onClick.Invoke();
            return;
        }
        if (aoVoltar.GetPersistentEventCount() > 0) aoVoltar.Invoke();
        else gameObject.SetActive(false);
    }
}
