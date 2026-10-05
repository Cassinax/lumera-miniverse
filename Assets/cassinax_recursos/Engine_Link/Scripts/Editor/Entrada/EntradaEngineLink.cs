using UnityEditor;
using UnityEngine;
#if CASSINAX_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
#endif
// TouchPhase do UnityEngine (o Input System tem outro com o mesmo nome).
using TouchPhase = UnityEngine.TouchPhase;

namespace Cassinax.EngineLink.Editor
{
    /// <summary>
    /// Aplica no Play Mode a entrada vinda do aparelho:
    /// - sempre em <see cref="EngineLinkInput"/> (projetos com o Input Manager antigo);
    /// - com Input System, numa Touchscreen virtual "Engine Link", para o jogo usar input normal.
    /// Toques chegam normalizados (0-1, origem no canto superior esquerdo) e viram pixels do Game view atual.
    /// </summary>
    [InitializeOnLoad]
    public static class EntradaEngineLink
    {
        const float GravidadePadrao = 9.80665f;
        const int TipoAcelerometroAndroid = 1;

        static AtualizadorEntrada atualizador;

        static EntradaEngineLink()
        {
            ConexaoEngineLink.MensagemRecebida += AoReceber;
            EditorApplication.update += Atualizar;
            EditorApplication.playModeStateChanged += estado =>
            {
                if (estado == PlayModeStateChange.ExitingPlayMode) Desativar();
            };
            AssemblyReloadEvents.beforeAssemblyReload += Desativar;
#if CASSINAX_INPUT_SYSTEM
            ConfiguracoesEngineLink.Mudou += () => { if (atualizador != null) AplicarEntradaSemFoco(); };
#endif
        }

        static bool Ativa
        {
            get { return EditorApplication.isPlaying && ConexaoEngineLink.Conectado; }
        }

        static void Atualizar()
        {
            if (Ativa && atualizador == null)
            {
                var objeto = new GameObject("Engine Link Entrada") { hideFlags = HideFlags.HideAndDontSave };
                atualizador = objeto.AddComponent<AtualizadorEntrada>();
                EngineLinkInput.Active = true;
#if CASSINAX_INPUT_SYSTEM
                AtivarInputSystem();
#endif
            }
            else if (!Ativa && atualizador != null)
            {
                Desativar();
            }
        }

        static void Desativar()
        {
            if (atualizador != null) Object.DestroyImmediate(atualizador.gameObject);
            atualizador = null;
            EngineLinkInput.Active = false;
            EngineLinkInput.Limpar();
#if CASSINAX_INPUT_SYSTEM
            DesativarInputSystem();
#endif
        }

        static void AoReceber(Mensagem mensagem)
        {
            if (!Ativa) return;
            switch (mensagem.Tipo)
            {
                case TipoMensagem.Toque:
                    AplicarToque(Toque.Decodificar(mensagem));
                    break;
                case TipoMensagem.Sensor:
                    AplicarSensor(LeituraSensor.Decodificar(mensagem));
                    break;
            }
        }

        static void AplicarToque(Toque toque)
        {
            // Tamanho atual do Game view (a resolucao dele e dinamica).
            var tamanho = Handles.GetMainGameViewSize();
            foreach (var ponto in toque.Pontos)
            {
                var posicao = new Vector2(ponto.X * tamanho.x, (1f - ponto.Y) * tamanho.y);
                var fase = Fase(ponto.Fase);
                EngineLinkInput.EnfileirarToque(ponto.Id, posicao, fase, ponto.Pressao);
#if CASSINAX_INPUT_SYSTEM
                EnviarAoInputSystem(ponto.Id, posicao, fase, ponto.Pressao);
#endif
            }
        }

        static void AplicarSensor(LeituraSensor leitura)
        {
            // Android informa m/s2 com o aparelho deitado em (0, 0, +9.8); a Unity usa g com (0, 0, -1).
            if (leitura.Tipo == TipoAcelerometroAndroid && leitura.Valores.Length >= 3)
            {
                var g = new Vector3(leitura.Valores[0], leitura.Valores[1], leitura.Valores[2]) / -GravidadePadrao;
                EngineLinkInput.DefinirAceleracao(g);
#if CASSINAX_INPUT_SYSTEM
                if (acelerometro != null) InputSystem.QueueDeltaStateEvent(acelerometro.acceleration, g);
#endif
            }
        }

        static TouchPhase Fase(FaseToque fase)
        {
            switch (fase)
            {
                case FaseToque.Inicio: return TouchPhase.Began;
                case FaseToque.Movimento: return TouchPhase.Moved;
                case FaseToque.Fim: return TouchPhase.Ended;
                case FaseToque.Cancelado: return TouchPhase.Canceled;
                default: return TouchPhase.Stationary;
            }
        }

#if CASSINAX_INPUT_SYSTEM
        static Touchscreen telaDeToque;
        static Accelerometer acelerometro;
        static InputSettings configuracaoOriginal;

        static void AtivarInputSystem()
        {
            if (telaDeToque == null) telaDeToque = InputSystem.AddDevice<Touchscreen>("Engine Link Touchscreen");
            if (acelerometro == null) acelerometro = InputSystem.AddDevice<Accelerometer>("Engine Link Accelerometer");
            AplicarEntradaSemFoco();
        }

        /// <summary>
        /// Por padrao, toques so chegam ao jogo com o Game view em foco; o dev esta tocando no
        /// celular. Com a opcao ligada (padrao), usa uma copia das configuracoes durante a sessao:
        /// o asset do projeto nao muda.
        /// </summary>
        static void AplicarEntradaSemFoco()
        {
            if (!ConfiguracoesEngineLink.Atual.entradaSemFoco)
            {
                RestaurarConfiguracao();
                return;
            }
            if (configuracaoOriginal == null)
            {
                configuracaoOriginal = InputSystem.settings;
                var copia = Object.Instantiate(configuracaoOriginal);
                copia.hideFlags = HideFlags.HideAndDontSave;
                copia.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
                copia.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
                InputSystem.settings = copia;
            }
        }

        static void DesativarInputSystem()
        {
            if (telaDeToque != null && telaDeToque.added) InputSystem.RemoveDevice(telaDeToque);
            if (acelerometro != null && acelerometro.added) InputSystem.RemoveDevice(acelerometro);
            telaDeToque = null;
            acelerometro = null;
            RestaurarConfiguracao();
        }

        static void RestaurarConfiguracao()
        {
            if (configuracaoOriginal != null)
            {
                var copia = InputSystem.settings;
                InputSystem.settings = configuracaoOriginal;
                configuracaoOriginal = null;
                if (copia != null && copia != InputSystem.settings) Object.DestroyImmediate(copia);
            }
        }

        static void EnviarAoInputSystem(int id, Vector2 posicao, TouchPhase fase, float pressao)
        {
            if (telaDeToque == null) return;
            InputSystem.QueueStateEvent(telaDeToque, new TouchState
            {
                touchId = id + 1, // 0 e reservado pelo Input System
                position = posicao,
                pressure = pressao,
                phase = Converter(fase),
            });
        }

        static UnityEngine.InputSystem.TouchPhase Converter(TouchPhase fase)
        {
            switch (fase)
            {
                case TouchPhase.Began: return UnityEngine.InputSystem.TouchPhase.Began;
                case TouchPhase.Moved: return UnityEngine.InputSystem.TouchPhase.Moved;
                case TouchPhase.Ended: return UnityEngine.InputSystem.TouchPhase.Ended;
                case TouchPhase.Canceled: return UnityEngine.InputSystem.TouchPhase.Canceled;
                default: return UnityEngine.InputSystem.TouchPhase.Stationary;
            }
        }
#endif
    }
}
