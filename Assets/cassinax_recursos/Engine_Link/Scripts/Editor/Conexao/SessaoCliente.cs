using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Threading;

namespace Cassinax.EngineLink.Editor
{
    /// <summary>Resultado de uma tentativa de conexao.</summary>
    public sealed class ResultadoConexao
    {
        public SessaoCliente Sessao;
        public RespostaHelloOk Resposta;
        /// <summary>Texto do HELLO_OK (para quem precisar do preset completo).</summary>
        public string TextoResposta;
        public RecusaHello Recusa;
        public string Erro;

        public bool Sucesso { get { return Sessao != null; } }
    }

    /// <summary>
    /// Sessao TCP com o app: uma thread de leitura e uma de escrita. Eventos sao disparados
    /// nas threads da sessao; quem usa a API da Unity deve repassar para a thread principal.
    /// </summary>
    public sealed class SessaoCliente
    {
        /// <summary>Padroes; os valores em uso vem de <see cref="ConfiguracoesEngineLink"/>.</summary>
        public const int IntervaloPingMs = 1000;
        public const int TempoLimiteMs = 5000;

        static readonly Stopwatch Relogio = Stopwatch.StartNew();

        /// <summary>Milissegundos de um relogio monotono (carimbos do protocolo).</summary>
        public static long Agora { get { return Relogio.ElapsedMilliseconds; } }

        readonly TcpClient cliente;
        readonly Stream stream;
        readonly object trava = new object();
        readonly Queue<Mensagem> fila = new Queue<Mensagem>();
        Mensagem quadroPendente;
        volatile int intervaloPingMs = IntervaloPingMs;
        volatile bool encerrada;
        string motivoEncerramento;

        public event Action<Mensagem> Recebida;
        public event Action<long> LatenciaMedida;
        /// <summary>Disparado uma vez, com o motivo, quando a sessao termina por qualquer lado.</summary>
        public event Action<string> Encerrada;

        public bool Ativa { get { return !encerrada; } }

        SessaoCliente(TcpClient cliente)
        {
            this.cliente = cliente;
            stream = cliente.GetStream();
        }

        /// <summary>Conecta e faz o handshake. Bloqueia: use fora da thread principal.</summary>
        public static ResultadoConexao Conectar(string host, int porta, Mensagem hello,
            int tempoLimiteMs = TempoLimiteMs, int intervaloPingMs = IntervaloPingMs)
        {
            var resultado = new ResultadoConexao();
            var cliente = new TcpClient { NoDelay = true };
            try
            {
                var tentativa = cliente.BeginConnect(host, porta, null, null);
                if (!tentativa.AsyncWaitHandle.WaitOne(tempoLimiteMs) || !cliente.Connected)
                {
                    resultado.Erro = "Could not reach " + host + ":" + porta + ".";
                    cliente.Close();
                    return resultado;
                }
                cliente.EndConnect(tentativa);
                cliente.ReceiveTimeout = tempoLimiteMs;
                cliente.SendTimeout = tempoLimiteMs;
                var stream = cliente.GetStream();
                CodificadorMensagens.Escrever(stream, hello);

                Mensagem resposta;
                do
                {
                    resposta = CodificadorMensagens.Ler(stream);
                } while (resposta != null && resposta.Tipo != TipoMensagem.HelloOk && resposta.Tipo != TipoMensagem.HelloRecusado);

                if (resposta == null)
                {
                    resultado.Erro = "The app closed the connection before answering.";
                }
                else if (resposta.Tipo == TipoMensagem.HelloRecusado)
                {
                    resultado.Recusa = Handshake.LerRecusa(resposta);
                }
                else
                {
                    var ok = Handshake.LerHelloOk(resposta);
                    if (Handshake.VersaoMaior(ok.protocolo) != InfoEngineLink.ProtocoloMaior)
                    {
                        resultado.Erro = "Incompatible protocol " + ok.protocolo + " (package uses " + InfoEngineLink.Protocolo + ").";
                    }
                    else
                    {
                        resultado.Resposta = ok;
                        resultado.TextoResposta = resposta.Texto();
                        resultado.Sessao = new SessaoCliente(cliente) { intervaloPingMs = intervaloPingMs };
                        return resultado;
                    }
                }
            }
            catch (Exception e)
            {
                if (e is IOException || e is SocketException || e is ProtocoloException || e is ObjectDisposedException)
                {
                    resultado.Erro = e is ProtocoloException ? "Protocol error: " + e.Message : "The app did not answer: " + e.Message;
                }
                else throw;
            }
            cliente.Close();
            return resultado;
        }

        /// <summary>Aplica novos tempos na sessao em andamento (opcoes mudaram).</summary>
        public void DefinirTempos(int tempoLimiteMs, int pingMs)
        {
            intervaloPingMs = pingMs;
            try
            {
                cliente.ReceiveTimeout = tempoLimiteMs;
                cliente.SendTimeout = tempoLimiteMs;
            }
            catch (ObjectDisposedException) { }
        }

        /// <summary>Inicia as threads. Assine os eventos antes.</summary>
        public void Iniciar()
        {
            new Thread(Ler) { IsBackground = true, Name = "EngineLink-leitura" }.Start();
            new Thread(Escrever) { IsBackground = true, Name = "EngineLink-escrita" }.Start();
        }

        /// <summary>
        /// Enfileira para envio. QUADRO ocupa uma vaga unica: um quadro novo substitui o
        /// pendente, para a imagem nunca acumular atraso.
        /// </summary>
        public bool Enviar(Mensagem mensagem)
        {
            if (encerrada) return false;
            lock (trava)
            {
                if (mensagem.Tipo == TipoMensagem.Quadro) quadroPendente = mensagem;
                else fila.Enqueue(mensagem);
                Monitor.Pulse(trava);
            }
            return true;
        }

        /// <summary>Verdadeiro se ainda ha um quadro aguardando envio (a captura pode pular o proximo).</summary>
        public bool QuadroPendente
        {
            get { lock (trava) return quadroPendente != null; }
        }

        public void Encerrar(string motivo, bool avisarApp)
        {
            if (avisarApp && !encerrada)
            {
                try
                {
                    lock (stream) CodificadorMensagens.Escrever(stream, Handshake.CriarTchau(motivo));
                }
                catch (Exception)
                {
                    // Conexao ja caiu.
                }
            }
            Finalizar(motivo);
        }

        void Ler()
        {
            var motivo = "The app closed the connection.";
            try
            {
                while (!encerrada)
                {
                    var mensagem = CodificadorMensagens.Ler(stream);
                    if (mensagem == null) break;
                    switch (mensagem.Tipo)
                    {
                        case TipoMensagem.Ping:
                            Enviar(Ping.Responder(mensagem));
                            break;
                        case TipoMensagem.Pong:
                            var latencia = LatenciaMedida;
                            if (latencia != null) latencia(Agora - Ping.Carimbo(mensagem));
                            break;
                        case TipoMensagem.Tchau:
                            var detalhe = Handshake.LerMotivoTchau(mensagem);
                            motivo = "The app ended the session" + (detalhe.Length > 0 ? ": " + detalhe : ".");
                            return;
                        case TipoMensagem.Hello:
                        case TipoMensagem.HelloOk:
                        case TipoMensagem.HelloRecusado:
                            break;
                        default:
                            var recebida = Recebida;
                            if (recebida != null) recebida(mensagem);
                            break;
                    }
                }
            }
            catch (IOException e)
            {
                // ReceiveTimeout tambem chega aqui como IOException.
                if (!encerrada) motivo = "Connection lost: " + MensagemRaiz(e);
            }
            catch (ProtocoloException e)
            {
                motivo = "Protocol error: " + e.Message;
            }
            catch (ObjectDisposedException)
            {
            }
            finally
            {
                Finalizar(motivo);
            }
        }

        void Escrever()
        {
            var proximoPing = Agora;
            try
            {
                while (!encerrada)
                {
                    Mensagem mensagem = null;
                    lock (trava)
                    {
                        if (fila.Count > 0) mensagem = fila.Dequeue();
                        else if (quadroPendente != null)
                        {
                            mensagem = quadroPendente;
                            quadroPendente = null;
                        }
                        else
                        {
                            var espera = (int)Math.Max(1, proximoPing - Agora);
                            Monitor.Wait(trava, espera);
                        }
                    }
                    if (Agora >= proximoPing)
                    {
                        lock (stream) CodificadorMensagens.Escrever(stream, Ping.Codificar(Agora));
                        proximoPing = Agora + intervaloPingMs;
                    }
                    if (mensagem != null)
                    {
                        lock (stream) CodificadorMensagens.Escrever(stream, mensagem);
                    }
                }
            }
            catch (Exception e)
            {
                if (e is IOException || e is ObjectDisposedException || e is InvalidOperationException)
                {
                    Finalizar("Connection lost: " + MensagemRaiz(e));
                }
                else throw;
            }
        }

        void Finalizar(string motivo)
        {
            lock (trava)
            {
                if (encerrada) return;
                encerrada = true;
                motivoEncerramento = motivo;
                Monitor.PulseAll(trava);
            }
            try { cliente.Close(); } catch (Exception) { }
            var encerradaEvento = Encerrada;
            if (encerradaEvento != null) encerradaEvento(motivoEncerramento);
        }

        static string MensagemRaiz(Exception e)
        {
            while (e.InnerException != null) e = e.InnerException;
            return e.Message;
        }
    }
}
