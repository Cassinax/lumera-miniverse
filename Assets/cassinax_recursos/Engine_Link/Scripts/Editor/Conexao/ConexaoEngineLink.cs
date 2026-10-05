using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using UnityEditor;
using UnityEngine;

namespace Cassinax.EngineLink.Editor
{
    /// <summary>
    /// API da conexao para os outros modulos (entrada, captura, diagnostico) e
    /// implementacao de <see cref="IControladorConexao"/> usada pela janela.
    /// Eventos publicos sao disparados na thread principal.
    /// </summary>
    [InitializeOnLoad]
    public sealed class ConexaoEngineLink : IControladorConexao
    {
        const string ChaveAlvo = "Cassinax.EngineLink.AlvoReconexao";
        const string PrefixoToken = "Cassinax.EngineLink.Token.";
        /// <summary>Apos uma queda, tenta de novo neste intervalo ate conectar ou o usuario cancelar (opcao do dev).</summary>
        public static double IntervaloReconexaoSegundos { get { return ConfiguracoesEngineLink.Atual.reconexaoSegundos; } }

        static readonly ConexaoEngineLink Instancia = new ConexaoEngineLink();
        static readonly ConcurrentQueue<Action> Principal = new ConcurrentQueue<Action>();
        static readonly ConcurrentQueue<Mensagem> Recebidas = new ConcurrentQueue<Mensagem>();

        /// <summary>Mensagens do app que nao sao de controle (TOQUE, SENSOR, TECLA, TELA, CONFIG...).</summary>
        public static event Action<Mensagem> MensagemRecebida;
        public static event Action<RespostaHelloOk> Conectou;
        public static event Action<string> Desconectou;

        /// <summary>Dados do aparelho conectado; nulo quando desconectado.</summary>
        public static RespostaHelloOk Aparelho { get; private set; }

        /// <summary>Texto bruto do HELLO_OK (inclui o preset).</summary>
        public static string TextoHelloOk { get; private set; }

        public static bool Conectado { get { return sessao != null && sessao.Ativa; } }

        /// <summary>Segundos ate a proxima tentativa automatica; negativo se nenhuma agendada.</summary>
        public static double SegundosParaProximaTentativa
        {
            get { return proximaReconexao > 0 ? Math.Max(0, proximaReconexao - EditorApplication.timeSinceStartup) : -1; }
        }

        static SessaoCliente sessao;
        static AlvoConexao alvo;
        static int geracao;
        /// <summary>Verdadeiro apos uma queda: falhas agendam nova tentativa em vez de desistir.</summary>
        static bool reconexaoAutomatica;
        static double proximaReconexao;

        [Serializable]
        class AlvoConexao
        {
            public string nome;
            public string endereco;
            public int porta;
            public int meio;
            public string codigo;

            public AparelhoEncontrado Aparelho()
            {
                return new AparelhoEncontrado(nome, endereco, porta, (MeioConexao)meio);
            }
        }

        static ConexaoEngineLink()
        {
            EstadoEngineLink.Controlador = Instancia;
            EditorApplication.update += Bombear;
            AssemblyReloadEvents.beforeAssemblyReload += AntesDeRecarregar;
            EditorApplication.quitting += () => Encerrar("Editor closed", true);

            // Entrar no Play Mode recarrega os scripts: a conexao e refeita em seguida.
            var salvo = SessionState.GetString(ChaveAlvo, "");
            if (salvo.Length > 0)
            {
                alvo = JsonUtility.FromJson<AlvoConexao>(salvo);
                IniciarReconexao("Reconnecting after script reload...");
            }
        }

        /// <summary>Envia ao app. Pode ser chamado de qualquer thread. QUADRO substitui o quadro pendente.</summary>
        public static bool Enviar(Mensagem mensagem)
        {
            var atual = sessao;
            return atual != null && atual.Enviar(mensagem);
        }

        /// <summary>Aplica ping e tempo limite das opcoes na sessao atual.</summary>
        public static void AplicarTempos()
        {
            var atual = sessao;
            var opcoes = ConfiguracoesEngineLink.Atual;
            if (atual != null) atual.DefinirTempos(opcoes.tempoLimiteMs, opcoes.pingMs);
        }

        /// <summary>Verdadeiro enquanto um quadro anterior ainda nao saiu (a captura pode economizar).</summary>
        public static bool QuadroPendente
        {
            get
            {
                var atual = sessao;
                return atual != null && atual.QuadroPendente;
            }
        }

        // ---- IControladorConexao (janela) ----

        public void Procurar(MeioConexao meio)
        {
            if (meio == MeioConexao.WifiIp) return;
            EstadoEngineLink.DefinirEstado(EstadoConexao.Procurando, null, null);
            var adb = meio == MeioConexao.Usb ? Adb.Localizar() : null;
            if (meio == MeioConexao.Usb && adb == null)
            {
                EstadoEngineLink.DefinirEstado(EstadoConexao.Desconectado, null,
                    "adb not found. Install Android Build Support or set the Android SDK path in Preferences > External Tools.");
                return;
            }
            Executar(() =>
            {
                List<AparelhoEncontrado> lista;
                string erro = null;
                try
                {
                    lista = meio == MeioConexao.Usb ? ListarUsb(adb) : Descoberta.Procurar();
                }
                catch (Exception e)
                {
                    lista = new List<AparelhoEncontrado>();
                    erro = e.Message;
                }
                NaPrincipal(() =>
                {
                    foreach (var aparelho in lista) aparelho.Confiavel = TokenPara(aparelho) != null;
                    EstadoEngineLink.DefinirAparelhos(lista);
                    var dica = erro ?? (lista.Count == 0 ? DicaNenhumAparelho(meio) : null);
                    if (EstadoEngineLink.Estado == EstadoConexao.Procurando) EstadoEngineLink.DefinirEstado(EstadoConexao.Desconectado, null, dica);
                });
            });
        }

        public void Conectar(AparelhoEncontrado aparelho, string codigoPareamento)
        {
            Encerrar("New connection requested", true);
            alvo = new AlvoConexao
            {
                nome = aparelho.Nome,
                endereco = aparelho.Endereco,
                porta = aparelho.Porta,
                meio = (int)aparelho.Meio,
                codigo = codigoPareamento,
            };
            reconexaoAutomatica = false; // tentativa manual: erro aparece na hora
            Tentar(EstadoConexao.Conectando, "Connecting...");
        }

        public void Desconectar()
        {
            alvo = null;
            reconexaoAutomatica = false;
            proximaReconexao = 0;
            SessionState.EraseString(ChaveAlvo);
            Encerrar("Disconnected by the user", true);
            EstadoEngineLink.DefinirEstado(EstadoConexao.Desconectado, null, null);
        }

        // ---- Tentativas e reconexao ----

        static void Tentar(EstadoConexao estado, string mensagem)
        {
            var atual = alvo;
            if (atual == null) return;
            var aparelho = atual.Aparelho();
            EstadoEngineLink.DefinirEstado(estado, aparelho, mensagem);

            var minhaGeracao = ++geracao;
            var meio = (MeioConexao)atual.meio;
            var adb = meio == MeioConexao.Usb ? Adb.Localizar() : null;
            var token = TokenPara(aparelho);
            // Envia token e codigo juntos: se o app nao reconhecer mais o token (app reinstalado),
            // o codigo digitado ainda permite parear de novo.
            var opcoes = ConfiguracoesEngineLink.Atual;
            var tempoLimiteMs = opcoes.tempoLimiteMs;
            var pingMs = opcoes.pingMs;
            var hello = Handshake.CriarHello(atual.codigo, token, Application.unityVersion, Application.productName, tempoLimiteMs);

            Executar(() =>
            {
                ResultadoConexao resultado;
                int portaLocal = -1;
                try
                {
                    var host = aparelho.Endereco;
                    var porta = aparelho.Porta;
                    if (meio == MeioConexao.Usb)
                    {
                        if (adb == null) throw new InvalidOperationException("adb not found.");
                        portaLocal = Adb.PortaLivre();
                        Adb.Encaminhar(adb, aparelho.Endereco, portaLocal, aparelho.Porta);
                        host = "127.0.0.1";
                        porta = portaLocal;
                    }
                    resultado = SessaoCliente.Conectar(host, porta, hello, tempoLimiteMs, pingMs);
                    if (!resultado.Sucesso && meio == MeioConexao.Usb && resultado.Recusa == null)
                    {
                        resultado.Erro = "The app is not waiting for USB. In the app, open Screen > Connect > USB.";
                    }
                }
                catch (Exception e)
                {
                    resultado = new ResultadoConexao { Erro = e.Message };
                }
                if (!resultado.Sucesso && portaLocal > 0) Adb.RemoverEncaminhamento(adb, aparelho.Endereco, portaLocal);
                NaPrincipal(() => AoTerminarTentativa(minhaGeracao, aparelho, resultado));
            });
        }

        static void AoTerminarTentativa(int minhaGeracao, AparelhoEncontrado aparelho, ResultadoConexao resultado)
        {
            if (minhaGeracao != geracao || alvo == null)
            {
                if (resultado.Sucesso) resultado.Sessao.Encerrar("Attempt superseded", true);
                return;
            }
            if (!resultado.Sucesso)
            {
                var mensagem = resultado.Recusa != null ? Handshake.Explicar(resultado.Recusa) : resultado.Erro;
                if (resultado.Recusa != null && resultado.Recusa.motivo == "codigo")
                {
                    // Token salvo nao vale mais: esquece para o proximo Connect pedir o codigo.
                    EsquecerToken(aparelho);
                    mensagem += " (saved pairing was reset; type the code shown in the app)";
                }
                Debug.LogWarning("[Engine Link] Connection failed: " + mensagem);
                // Recusa (codigo, versao, ocupado) nao melhora tentando de novo.
                if (resultado.Recusa == null && reconexaoAutomatica)
                {
                    EstadoEngineLink.DefinirEstado(EstadoConexao.Reconectando, aparelho, mensagem);
                    proximaReconexao = EditorApplication.timeSinceStartup + IntervaloReconexaoSegundos;
                    return;
                }
                alvo = null;
                SessionState.EraseString(ChaveAlvo);
                EstadoEngineLink.DefinirEstado(EstadoConexao.Desconectado, null, mensagem);
                return;
            }

            var resposta = resultado.Resposta;
            if (!string.IsNullOrEmpty(resposta.token))
            {
                if (resposta.aparelho != null && !string.IsNullOrEmpty(resposta.aparelho.nome)) GuardarToken(resposta.aparelho.nome, resposta.token);
                GuardarToken(aparelho.Endereco, resposta.token);
                GuardarToken(aparelho.Nome, resposta.token);
            }
            alvo.codigo = null; // ja pareado: reconexoes usam o token
            if (resposta.aparelho != null && !string.IsNullOrEmpty(resposta.aparelho.nome)) alvo.nome = resposta.aparelho.nome;

            var nova = resultado.Sessao;
            nova.Recebida += m => Recebidas.Enqueue(m);
            nova.LatenciaMedida += ms => NaPrincipal(() => { if (sessao == nova) EstadoEngineLink.DefinirLatencia(ms); });
            nova.Encerrada += motivo => NaPrincipal(() => AoEncerrarSessao(nova, motivo));
            sessao = nova;
            Aparelho = resposta;
            proximaReconexao = 0;
            TextoHelloOk = resultado.TextoResposta;
            SessionState.SetString(ChaveAlvo, JsonUtility.ToJson(alvo));
            nova.Iniciar();
            EstadoEngineLink.DefinirEstado(EstadoConexao.Conectado, alvo.Aparelho(), null);
            Debug.Log("[Engine Link] Connected to " + alvo.nome + " (" + alvo.endereco + ").");
            var conectou = Conectou;
            if (conectou != null) conectou(resposta);
        }

        static void AoEncerrarSessao(SessaoCliente encerrada, string motivo)
        {
            if (sessao != encerrada) return;
            Debug.LogWarning("[Engine Link] Session ended: " + motivo);
            sessao = null;
            Aparelho = null;
            TextoHelloOk = null;
            var desconectou = Desconectou;
            if (desconectou != null) desconectou(motivo);
            if (alvo != null) IniciarReconexao(motivo);
            else EstadoEngineLink.DefinirEstado(EstadoConexao.Desconectado, null, motivo);
        }

        static void IniciarReconexao(string motivo)
        {
            reconexaoAutomatica = true;
            EstadoEngineLink.DefinirEstado(EstadoConexao.Reconectando, alvo.Aparelho(), motivo);
            proximaReconexao = EditorApplication.timeSinceStartup;
        }

        static void Encerrar(string motivo, bool avisarApp)
        {
            geracao++;
            var atual = sessao;
            sessao = null;
            Aparelho = null;
            TextoHelloOk = null;
            if (atual != null) atual.Encerrar(motivo, avisarApp);
        }

        static void AntesDeRecarregar()
        {
            // Mantem o alvo em SessionState (ja salvo) e fecha a sessao; reconecta apos a recarga.
            Encerrar("Script reload", true);
        }

        // ---- Thread principal ----

        static void Bombear()
        {
            Action acao;
            while (Principal.TryDequeue(out acao))
            {
                try { acao(); }
                catch (Exception e) { Debug.LogException(e); }
            }

            var assinantes = MensagemRecebida;
            if (assinantes != null)
            {
                Mensagem mensagem;
                while (Recebidas.TryDequeue(out mensagem))
                {
                    try { assinantes(mensagem); }
                    catch (Exception e) { Debug.LogException(e); }
                }
            }
            else
            {
                Mensagem descartada;
                while (Recebidas.TryDequeue(out descartada)) { }
            }

            if (EstadoEngineLink.Estado == EstadoConexao.Reconectando && sessao == null && alvo != null &&
                proximaReconexao > 0 && EditorApplication.timeSinceStartup >= proximaReconexao)
            {
                proximaReconexao = 0;
                Tentar(EstadoConexao.Reconectando, EstadoEngineLink.Mensagem);
            }
        }

        static void NaPrincipal(Action acao)
        {
            Principal.Enqueue(acao);
        }

        static void Executar(Action trabalho)
        {
            new Thread(() => trabalho()) { IsBackground = true, Name = "EngineLink-tarefa" }.Start();
        }

        // ---- Aparelhos e tokens ----

        static List<AparelhoEncontrado> ListarUsb(string adb)
        {
            var lista = new List<AparelhoEncontrado>();
            foreach (var aparelho in Adb.Listar(adb))
            {
                var nome = aparelho.Modelo;
                if (aparelho.Estado == "unauthorized") nome += " (allow USB debugging on the device)";
                else if (aparelho.Estado != "device") nome += " (" + aparelho.Estado + ")";
                lista.Add(new AparelhoEncontrado(nome, aparelho.Serial, InfoEngineLink.PortaPadrao, MeioConexao.Usb));
            }
            return lista;
        }

        static string DicaNenhumAparelho(MeioConexao meio)
        {
            return meio == MeioConexao.Usb
                ? "No device found. Check the cable and that USB debugging is enabled."
                : "No device answered. In the app choose Wi-Fi (automatic) and use the same network, or try Wi-Fi (IP).";
        }

        static string Chave(string valor)
        {
            return PrefixoToken + (valor ?? "").Trim().ToLowerInvariant();
        }

        static string TokenPara(AparelhoEncontrado aparelho)
        {
            // USB tambem usa token: o app pode exigir o codigo pelo cabo (opcao no app).
            var porNome = EditorPrefs.GetString(Chave(aparelho.Nome), "");
            if (porNome.Length > 0) return porNome;
            var porEndereco = EditorPrefs.GetString(Chave(aparelho.Endereco), "");
            return porEndereco.Length > 0 ? porEndereco : null;
        }

        static void EsquecerToken(AparelhoEncontrado aparelho)
        {
            EditorPrefs.DeleteKey(Chave(aparelho.Nome));
            EditorPrefs.DeleteKey(Chave(aparelho.Endereco));
        }

        static void GuardarToken(string chave, string token)
        {
            if (!string.IsNullOrEmpty(chave)) EditorPrefs.SetString(Chave(chave), token);
        }
    }
}
