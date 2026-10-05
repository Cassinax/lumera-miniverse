using System;
using System.Threading;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace Cassinax.EngineLink.Editor
{
    /// <summary>
    /// Envia a imagem do Play Mode ao app: cria o <see cref="CapturadorQuadros"/> enquanto
    /// houver Play Mode e conexao, aplica FPS, qualidade e resolucao escolhidos pelo dev
    /// (<see cref="ConfiguracoesEngineLink"/>), comprime em JPEG fora da thread principal e manda
    /// so o quadro mais recente (QUADRO usa vaga unica na sessao). Tambem avisa o app quando o
    /// Play Mode comeca, pausa ou termina (ESTADO_JOGO), para ele limpar a imagem.
    /// </summary>
    [InitializeOnLoad]
    public static class CapturaEngineLink
    {
        /// <summary>Tamanho maximo padrao quando o app ainda nao informou a area de exibicao.</summary>
        const int LadoMaximoPadrao = 1280;
        /// <summary>Limite do protocolo (largura/altura em uint16).</summary>
        const int LadoMaximoProtocolo = 0xFFFF;

        /// <summary>Maior lado da area de exibicao informada pelo app (0 = ainda nao informada).</summary>
        static int ladoAparelho;

        static CapturadorQuadros capturador;
        static double ultimoEnvio;
        static int proximoId;
        static volatile bool comprimindo;

        [Serializable]
        class TelaApp
        {
            public int largura;
            public int altura;
        }

        static CapturaEngineLink()
        {
            CapturadorQuadros.PodeCapturar = PodeCapturar;
            CapturadorQuadros.QuadroPronto = AoCapturar;
            EditorApplication.update += Atualizar;
            ConexaoEngineLink.MensagemRecebida += AoReceber;
            ConexaoEngineLink.Conectou += resposta =>
            {
                if (resposta.tela != null) DefinirArea(resposta.tela.largura, resposta.tela.altura);
                InformarEstado();
            };
            EditorApplication.playModeStateChanged += estado =>
            {
                if (estado == PlayModeStateChange.EnteredPlayMode || estado == PlayModeStateChange.ExitingPlayMode) InformarEstado();
            };
            EditorApplication.pauseStateChanged += _ => InformarEstado();
        }

        /// <summary>Envia ESTADO_JOGO com o estado atual do Play Mode.</summary>
        static void InformarEstado()
        {
            // Ao sair, isPlaying ainda e verdadeiro durante ExitingPlayMode.
            var jogando = EditorApplication.isPlaying && EditorApplication.isPlayingOrWillChangePlaymode;
            ConexaoEngineLink.Enviar(EstadoJogo.Codificar(jogando, jogando && EditorApplication.isPaused));
        }

        static void Atualizar()
        {
            var deveCapturar = EditorApplication.isPlaying && ConexaoEngineLink.Conectado;
            if (deveCapturar && capturador == null)
            {
                var objeto = new GameObject("Engine Link Captura") { hideFlags = HideFlags.HideAndDontSave };
                capturador = objeto.AddComponent<CapturadorQuadros>();
            }
            else if (!deveCapturar && capturador != null)
            {
                UnityEngine.Object.DestroyImmediate(capturador.gameObject);
                capturador = null;
            }
            if (capturador != null) AplicarResolucao();
        }

        /// <summary>Tamanho maximo da imagem conforme a opcao de resolucao (a proporcao e mantida).</summary>
        static void AplicarResolucao()
        {
            var opcoes = ConfiguracoesEngineLink.Atual;
            int largura, altura;
            switch (opcoes.resolucao)
            {
                case ModoResolucao.Nativa:
                    largura = altura = LadoMaximoProtocolo;
                    break;
                case ModoResolucao.Porcentagem:
                    var tamanho = Handles.GetMainGameViewSize();
                    largura = Mathf.Max(1, Mathf.RoundToInt(tamanho.x * opcoes.porcentagemResolucao / 100f));
                    altura = Mathf.Max(1, Mathf.RoundToInt(tamanho.y * opcoes.porcentagemResolucao / 100f));
                    break;
                default:
                    largura = altura = ladoAparelho > 0 ? ladoAparelho : LadoMaximoPadrao;
                    break;
            }
            CapturadorQuadros.LarguraMaxima = Mathf.Min(largura, LadoMaximoProtocolo);
            CapturadorQuadros.AlturaMaxima = Mathf.Min(altura, LadoMaximoProtocolo);
        }

        static bool PodeCapturar()
        {
            if (!ConexaoEngineLink.Conectado || comprimindo || ConexaoEngineLink.QuadroPendente) return false;
            // 0 = sem limite: o ritmo fica com a captura, a compressao e a vaga de envio.
            var fps = ConfiguracoesEngineLink.Atual.fpsMaximo;
            var agora = EditorApplication.timeSinceStartup;
            if (fps > 0 && agora - ultimoEnvio < 1.0 / fps) return false;
            ultimoEnvio = agora;
            return true;
        }

        static void AoCapturar(byte[] rgba, int largura, int altura)
        {
            comprimindo = true;
            var id = ++proximoId;
            var carimbo = SessaoCliente.Agora;
            var qualidade = Mathf.Clamp(ConfiguracoesEngineLink.Atual.qualidadeJpeg, 1, 100);
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    var jpeg = ImageConversion.EncodeArrayToJPG(rgba, GraphicsFormat.R8G8B8A8_UNorm,
                        (uint)largura, (uint)altura, (uint)(largura * 4), qualidade);
                    var quadro = new QuadroImagem
                    {
                        Id = id, Largura = largura, Altura = altura, Carimbo = carimbo, Imagem = jpeg,
                    };
                    ConexaoEngineLink.Enviar(quadro.Codificar());
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[Engine Link] Frame capture failed: " + e.Message);
                }
                finally
                {
                    comprimindo = false;
                }
            });
        }

        static void AoReceber(Mensagem mensagem)
        {
            if (mensagem.Tipo != TipoMensagem.Tela) return;
            try
            {
                var tela = JsonUtility.FromJson<TelaApp>(mensagem.Texto());
                if (tela != null) DefinirArea(tela.largura, tela.altura);
            }
            catch (ArgumentException)
            {
                // TELA invalida: mantem o tamanho anterior.
            }
        }

        /// <summary>Guarda a area de exibicao do app (usada no modo de resolucao do aparelho).</summary>
        static void DefinirArea(int largura, int altura)
        {
            ladoAparelho = Mathf.Max(0, Mathf.Max(largura, altura));
        }
    }
}
