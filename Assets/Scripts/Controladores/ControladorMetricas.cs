using System.Collections.Generic;
using System.Text;
using UnityEngine;

// Filho "Metricas" do Objeto Mestre: ponto unico de eventos de analise da plataforma e dos jogos.
// O Firebase ainda nao esta no projeto: um destino (IDestinoMetricas) recebera os eventos quando for integrado.
// Ate la os eventos so vao para o log (Registrar No Log), para conferir o que cada jogo envia.
//
// Eventos mapeados (nome, parametros):
//   partida_iniciada      jogo
//   partida_terminada     jogo, causa (morte|menu|saida), nivel, altura_m, moedas, duracao_s, recorde (1|0)
//   faixa_alcancada       jogo, faixa, nivel
//   entrada_paga          jogo, preco
//   moedas_insuficientes  jogo, preco
//   graficos_reduzidos    de, para
//   configuracao_alterada nome, valor
[DisallowMultipleComponent]
public sealed class ControladorMetricas : MonoBehaviour
{
    public interface IDestinoMetricas
    {
        void Enviar(string evento, IReadOnlyDictionary<string, object> parametros);
    }

    [Tooltip("Mostra cada evento no Console. Util ate o Firebase ser integrado.")]
    [SerializeField] bool registrarNoLog = true;

    // Firebase (ou outro) entra aqui sem mudar quem registra.
    public IDestinoMetricas Destino { get; set; }

    readonly Dictionary<string, object> parametros = new Dictionary<string, object>();

    public void Registrar(string evento, params (string nome, object valor)[] valores)
    {
        if (string.IsNullOrEmpty(evento)) return;
        parametros.Clear();
        foreach (var (nome, valor) in valores) parametros[nome] = valor;
        if (registrarNoLog)
        {
            var texto = new StringBuilder("[Metricas] ").Append(evento);
            foreach (var par in parametros) texto.Append(' ').Append(par.Key).Append('=').Append(par.Value);
            Debug.Log(texto.ToString(), this);
        }
        Destino?.Enviar(evento, parametros);
    }
}
