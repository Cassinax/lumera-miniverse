using System;
using UnityEditor;
using UnityEngine;

namespace Cassinax.EngineLink.Editor
{
    /// <summary>
    /// Mantem iguais, na Unity e no app, as opcoes que os dois lados podem mudar (OPCOES 0x0106):
    /// FPS maximo, qualidade, resolucao e tempo limite. A Unity guarda os valores: envia ao conectar
    /// e a cada mudanca; quando o app pede uma mudanca, ela e validada, salva e devolvida ao app
    /// como confirmacao. Assim os dois lados terminam sempre com os mesmos valores.
    /// </summary>
    [InitializeOnLoad]
    public static class SincronizacaoOpcoes
    {
        const int Versao = 1;

        [Serializable]
        class OpcoesSessao
        {
            public int versao = Versao;
            public int fpsMaximo;
            public int qualidadeJpeg;
            public string resolucao;
            public int porcentagemResolucao;
            public int tempoLimiteMs;
        }

        static SincronizacaoOpcoes()
        {
            ConexaoEngineLink.Conectou += _ => Enviar();
            ConexaoEngineLink.MensagemRecebida += AoReceber;
            ConfiguracoesEngineLink.Mudou += () =>
            {
                ConexaoEngineLink.AplicarTempos();
                Enviar();
            };
        }

        static void Enviar()
        {
            if (!ConexaoEngineLink.Conectado) return;
            var atual = ConfiguracoesEngineLink.Atual;
            var opcoes = new OpcoesSessao
            {
                fpsMaximo = atual.fpsMaximo,
                qualidadeJpeg = atual.qualidadeJpeg,
                resolucao = NomeResolucao(atual.resolucao),
                porcentagemResolucao = atual.porcentagemResolucao,
                tempoLimiteMs = atual.tempoLimiteMs,
            };
            ConexaoEngineLink.Enviar(Mensagem.DeTexto(TipoMensagem.Opcoes, JsonUtility.ToJson(opcoes)));
        }

        static void AoReceber(Mensagem mensagem)
        {
            if (mensagem.Tipo != TipoMensagem.Opcoes) return;
            var novas = ConfiguracoesEngineLink.Atual.Copia();
            try
            {
                if (mensagem.Conteudo.Length > 4096) throw new ArgumentException("too large");
                // Campos ausentes mantem o valor atual.
                var pedido = new OpcoesSessao
                {
                    fpsMaximo = novas.fpsMaximo,
                    qualidadeJpeg = novas.qualidadeJpeg,
                    resolucao = NomeResolucao(novas.resolucao),
                    porcentagemResolucao = novas.porcentagemResolucao,
                    tempoLimiteMs = novas.tempoLimiteMs,
                };
                JsonUtility.FromJsonOverwrite(mensagem.Texto(), pedido);
                if (pedido.versao != Versao) throw new ArgumentException("version " + pedido.versao);
                ModoResolucao modo;
                if (!LerResolucao(pedido.resolucao, out modo)) throw new ArgumentException("resolution " + pedido.resolucao);
                novas.fpsMaximo = pedido.fpsMaximo;
                novas.qualidadeJpeg = pedido.qualidadeJpeg;
                novas.resolucao = modo;
                novas.porcentagemResolucao = pedido.porcentagemResolucao;
                novas.tempoLimiteMs = pedido.tempoLimiteMs;
            }
            catch (ArgumentException e)
            {
                Debug.LogWarning("[Engine Link] Invalid options from the app ignored: " + e.Message);
                Enviar(); // devolve os valores em uso para o app voltar ao estado certo
                return;
            }
            // Salvar normaliza e dispara Mudou, que devolve os valores finais ao app.
            ConfiguracoesEngineLink.Salvar(novas);
        }

        static string NomeResolucao(ModoResolucao modo)
        {
            switch (modo)
            {
                case ModoResolucao.Nativa: return "nativa";
                case ModoResolucao.Porcentagem: return "porcentagem";
                default: return "aparelho";
            }
        }

        static bool LerResolucao(string nome, out ModoResolucao modo)
        {
            switch (nome)
            {
                case "aparelho": modo = ModoResolucao.Aparelho; return true;
                case "nativa": modo = ModoResolucao.Nativa; return true;
                case "porcentagem": modo = ModoResolucao.Porcentagem; return true;
                default: modo = ModoResolucao.Aparelho; return false;
            }
        }
    }
}
