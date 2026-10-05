using System;
using System.Collections.Generic;

namespace Cassinax.EngineLink.Editor
{
    /// <summary>Meios de conexao, iguais aos do app.</summary>
    public enum MeioConexao
    {
        Usb = 0,
        WifiAutomatico = 1,
        WifiIp = 2,
    }

    public enum EstadoConexao
    {
        Desconectado,
        Procurando,
        Conectando,
        Conectado,
        Reconectando,
    }

    /// <summary>Aparelho com o app Engine Link encontrado por um meio de conexao.</summary>
    [Serializable]
    public class AparelhoEncontrado
    {
        public string Nome;
        /// <summary>Serial do adb (USB) ou endereco IP (Wi-Fi).</summary>
        public string Endereco;
        public int Porta;
        public MeioConexao Meio;
        /// <summary>Verdadeiro quando ja existe token de confianca (dispensa codigo).</summary>
        public bool Confiavel;

        public AparelhoEncontrado(string nome, string endereco, int porta, MeioConexao meio, bool confiavel = false)
        {
            Nome = nome;
            Endereco = endereco;
            Porta = porta;
            Meio = meio;
            Confiavel = confiavel;
        }
    }

    /// <summary>
    /// Operacoes de conexao. Implementado pela parte de protocolo/conexao e
    /// registrado em <see cref="EstadoEngineLink.Controlador"/>; a janela so chama.
    /// </summary>
    public interface IControladorConexao
    {
        /// <summary>Atualiza <see cref="EstadoEngineLink.Aparelhos"/> para o meio informado.</summary>
        void Procurar(MeioConexao meio);

        /// <summary>Codigo de pareamento: obrigatorio no Wi-Fi na primeira conexao; nulo no USB.</summary>
        void Conectar(AparelhoEncontrado aparelho, string codigoPareamento);

        void Desconectar();
    }

    /// <summary>
    /// Estado compartilhado do Engine Link no Editor. Deve ser alterado na thread
    /// principal; quem estiver em outra thread usa <see cref="UnityEditor.EditorApplication.delayCall"/>.
    /// </summary>
    public static class EstadoEngineLink
    {
        static readonly List<AparelhoEncontrado> aparelhos = new List<AparelhoEncontrado>();

        /// <summary>Disparado quando qualquer valor muda; a janela se redesenha.</summary>
        public static event Action Mudou;

        public static IControladorConexao Controlador { get; set; }

        public static EstadoConexao Estado { get; private set; }
        public static AparelhoEncontrado AparelhoAtual { get; private set; }
        public static IList<AparelhoEncontrado> Aparelhos { get { return aparelhos.AsReadOnly(); } }

        /// <summary>Latencia de ida e volta em ms; negativa quando desconhecida.</summary>
        public static long LatenciaMs { get; private set; }

        /// <summary>Mensagem curta para o usuario (erro, motivo da recusa, dica).</summary>
        public static string Mensagem { get; private set; }

        static EstadoEngineLink()
        {
            LatenciaMs = -1;
        }

        public static void DefinirEstado(EstadoConexao estado, AparelhoEncontrado aparelho = null, string mensagem = null)
        {
            Estado = estado;
            AparelhoAtual = estado == EstadoConexao.Desconectado ? null : aparelho ?? AparelhoAtual;
            if (estado != EstadoConexao.Conectado) LatenciaMs = -1;
            Mensagem = mensagem;
            Notificar();
        }

        public static void DefinirAparelhos(IEnumerable<AparelhoEncontrado> encontrados)
        {
            aparelhos.Clear();
            if (encontrados != null) aparelhos.AddRange(encontrados);
            Notificar();
        }

        public static void DefinirLatencia(long milissegundos)
        {
            LatenciaMs = milissegundos;
            Notificar();
        }

        public static void DefinirMensagem(string mensagem)
        {
            Mensagem = mensagem;
            Notificar();
        }

        static void Notificar()
        {
            var ouvintes = Mudou;
            if (ouvintes != null) ouvintes();
        }
    }
}
