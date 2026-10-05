using UnityEditor;
using UnityEngine;

namespace Cassinax.EngineLink.Editor
{
    /// <summary>Connection, device screen information and diagnostics in the Editor.</summary>
    public class JanelaEngineLink : EditorWindow
    {
        const string ChaveMeio = "Cassinax.EngineLink.Meio";
        const string ChaveIp = "Cassinax.EngineLink.Ip";
        const string ChavePorta = "Cassinax.EngineLink.Porta";
        const string ChavePortaUsb = "Cassinax.EngineLink.PortaUsb";

        static readonly GUIContent[] NomesMeios =
        {
            new GUIContent("USB", "Cable with USB debugging (adb)."),
            new GUIContent("Wi-Fi (auto)", "Finds the device on the local network."),
            new GUIContent("Wi-Fi (IP)", "Type the IP and port shown in the app."),
        };

        MeioConexao meio;
        string ip = "";
        string porta = InfoEngineLink.PortaPadrao.ToString();
        string portaUsb = InfoEngineLink.PortaPadrao.ToString();
        string codigo = "";
        Vector2 rolagem;
        enum Aba { Conexao, Tela, Opcoes, Diagnostico, Configuracoes, Sobre }
        static readonly string[] NomesAbas = { "Connection", "Screen", "Options", "Diagnostics", "Settings", "About" };
        static readonly GUIContent[] NomesResolucao =
        {
            new GUIContent("Device screen", "Reduced to the device display area (never enlarged)."),
            new GUIContent("Native (Game view)", "Current Game view resolution, no reduction."),
            new GUIContent("Percentage", "A percentage of the Game view resolution."),
        };
        [SerializeField] bool mostrarAvancado;
        [SerializeField] Aba aba;
        [SerializeField] AparelhoRemoto ultimoAparelho;
        [SerializeField] TelaRemota ultimaTela;
        [SerializeField] string versaoApp;
        [SerializeField] string protocoloApp;
        [SerializeField] bool temAparelho;
        bool telaInvalida;

        [MenuItem("Window/Cassinax/Engine Link")]
        public static void Abrir()
        {
            var janela = GetWindow<JanelaEngineLink>();
            janela.titleContent = new GUIContent("Engine Link");
            janela.minSize = new Vector2(320, 280);
            janela.Show();
        }

        void OnEnable()
        {
            meio = (MeioConexao)EditorPrefs.GetInt(ChaveMeio, (int)MeioConexao.Usb);
            ip = EditorPrefs.GetString(ChaveIp, "");
            porta = EditorPrefs.GetString(ChavePorta, InfoEngineLink.PortaPadrao.ToString());
            portaUsb = EditorPrefs.GetString(ChavePortaUsb, InfoEngineLink.PortaPadrao.ToString());
            EstadoEngineLink.Mudou += Repaint;
            AvisosEngineLink.Mudou += Repaint;
            ConfiguracoesEngineLink.Mudou += Repaint;
            ConexaoEngineLink.Conectou += ReceberAparelho;
            ConexaoEngineLink.MensagemRecebida += ReceberMensagem;
            if (ConexaoEngineLink.Aparelho != null) ReceberAparelho(ConexaoEngineLink.Aparelho);
        }

        void OnDisable()
        {
            EstadoEngineLink.Mudou -= Repaint;
            AvisosEngineLink.Mudou -= Repaint;
            ConfiguracoesEngineLink.Mudou -= Repaint;
            ConexaoEngineLink.Conectou -= ReceberAparelho;
            ConexaoEngineLink.MensagemRecebida -= ReceberMensagem;
        }

        void OnInspectorUpdate()
        {
            // Atualiza a contagem regressiva da reconexao.
            if (EstadoEngineLink.Estado == EstadoConexao.Reconectando) Repaint();
        }

        void OnGUI()
        {
            DesenharCabecalho();
            DesenharEstado();
            var novaAba = (Aba)GUILayout.SelectionGrid((int)aba, NomesAbas, position.width < 600 ? 3 : 6);
            if (novaAba != aba) { aba = novaAba; rolagem = Vector2.zero; GUI.FocusControl(null); }
            EditorGUILayout.Space();
            rolagem = EditorGUILayout.BeginScrollView(rolagem);
            switch (aba)
            {
                case Aba.Conexao: DesenharConexao(); break;
                case Aba.Tela: DesenharTela(); break;
                case Aba.Opcoes: DesenharPendente("Device options", "Resource controls and presets will be available after configuration synchronization is implemented."); break;
                case Aba.Diagnostico:
                    DesenharAvisos();
                    EditorGUILayout.HelpBox("Performance metrics are not available yet.", MessageType.Info);
                    break;
                case Aba.Configuracoes: DesenharPendente("App settings", "Remote app settings are not available yet. Use Settings on the device."); break;
                case Aba.Sobre: DesenharSobre(); break;
            }
            EditorGUILayout.EndScrollView();
        }

        void DesenharConexao()
        {
            var controlador = EstadoEngineLink.Controlador;
            if (controlador == null)
            {
                EditorGUILayout.HelpBox("Connection is not available in this version yet.", MessageType.Warning);
            }

            var ocupado = EstadoEngineLink.Estado != EstadoConexao.Desconectado &&
                          EstadoEngineLink.Estado != EstadoConexao.Procurando;
            if (ocupado)
            {
                var reconectando = EstadoEngineLink.Estado == EstadoConexao.Reconectando;
                if (reconectando)
                {
                    var segundos = ConexaoEngineLink.SegundosParaProximaTentativa;
                    EditorGUILayout.LabelField(segundos >= 0
                        ? "Trying again in " + Mathf.CeilToInt((float)segundos) + " s (every " + ConexaoEngineLink.IntervaloReconexaoSegundos + " s)."
                        : "Trying again...", EditorStyles.miniLabel);
                }
                if (GUILayout.Button(reconectando ? "Cancel" : "Disconnect") && controlador != null) controlador.Desconectar();
            }
            else
            {
                using (new EditorGUI.DisabledScope(controlador == null))
                {
                    DesenharMeio();
                    if (meio == MeioConexao.WifiIp) DesenharIpManual(controlador);
                    else DesenharAparelhos(controlador);
                }
            }
            EditorGUILayout.Space();
            DesenharAvancado();
        }

        /// <summary>Tempos da conexao. O tempo limite e sincronizado com o app; os demais sao so da Unity.</summary>
        void DesenharAvancado()
        {
            mostrarAvancado = EditorGUILayout.Foldout(mostrarAvancado, "Advanced", true);
            if (!mostrarAvancado) return;
            var novas = ConfiguracoesEngineLink.Atual.Copia();
            EditorGUI.BeginChangeCheck();
            novas.reconexaoSegundos = EditorGUILayout.DelayedIntField(
                new GUIContent("Reconnect every (s)", "After a drop, tries again at this interval until it connects or you cancel."), novas.reconexaoSegundos);
            novas.tempoLimiteMs = EditorGUILayout.DelayedIntField(
                new GUIContent("Timeout (ms)", "Without data for this long, the session is considered lost. Synchronized with the app. 2000-120000."), novas.tempoLimiteMs);
            novas.pingMs = EditorGUILayout.DelayedIntField(
                new GUIContent("Ping interval (ms)", "How often latency is measured. 250-10000."), novas.pingMs);
            novas.procuraWifiMs = EditorGUILayout.DelayedIntField(
                new GUIContent("Wi-Fi search time (ms)", "How long Wi-Fi (auto) waits for devices to answer."), novas.procuraWifiMs);
            novas.tempoLimiteAdbMs = EditorGUILayout.DelayedIntField(
                new GUIContent("adb timeout (ms)", "Maximum time for each adb command."), novas.tempoLimiteAdbMs);
            if (EditorGUI.EndChangeCheck()) ConfiguracoesEngineLink.Salvar(novas);
            if (GUILayout.Button("Restore connection defaults"))
            {
                var padrao = ConfiguracoesEngineLink.Padrao;
                novas.reconexaoSegundos = padrao.reconexaoSegundos;
                novas.tempoLimiteMs = padrao.tempoLimiteMs;
                novas.pingMs = padrao.pingMs;
                novas.procuraWifiMs = padrao.procuraWifiMs;
                novas.tempoLimiteAdbMs = padrao.tempoLimiteAdbMs;
                ConfiguracoesEngineLink.Salvar(novas);
            }
        }

        void ReceberAparelho(RespostaHelloOk hello)
        {
            // Keep only display data. Pairing tokens never belong to window state.
            ultimoAparelho = hello.aparelho;
            ultimaTela = hello.tela;
            versaoApp = hello.app;
            protocoloApp = hello.protocolo;
            temAparelho = true;
            telaInvalida = false;
            Repaint();
        }

        void ReceberMensagem(Mensagem mensagem)
        {
            if (mensagem.Tipo != TipoMensagem.Tela) return;
            try
            {
                if (mensagem.Conteudo.Length > 4096) throw new System.ArgumentException();
                var tela = JsonUtility.FromJson<TelaRemota>(mensagem.Texto());
                if (tela == null || tela.largura <= 0 || tela.altura <= 0 || tela.dpi < 0)
                    throw new System.ArgumentException();
                ultimaTela = tela;
                telaInvalida = false;
            }
            catch (System.ArgumentException) { telaInvalida = true; }
            Repaint();
        }

        void DesenharUltimoEstado()
        {
            if (!temAparelho)
                EditorGUILayout.HelpBox("Connect a device to receive its information.", MessageType.Info);
            else if (!ConexaoEngineLink.Conectado)
                EditorGUILayout.HelpBox("Disconnected. Showing the last received device information (read-only).", MessageType.Warning);
        }

        void DesenharTela()
        {
            DesenharUltimoEstado();
            EditorGUILayout.LabelField("Device screen", EditorStyles.boldLabel);
            if (temAparelho && ultimoAparelho != null)
                EditorGUILayout.LabelField("Device", Texto(ultimoAparelho.nome));
            if (ultimaTela != null)
            {
                EditorGUILayout.LabelField("Resolution", ultimaTela.largura > 0 && ultimaTela.altura > 0
                    ? ultimaTela.largura + " x " + ultimaTela.altura : "Not reported");
                EditorGUILayout.LabelField("DPI", ultimaTela.dpi > 0 ? ultimaTela.dpi.ToString() : "Not reported");
                EditorGUILayout.LabelField("Orientation", Texto(ultimaTela.orientacao));
                EditorGUILayout.LabelField("Last received from the app; not the streaming resolution.", EditorStyles.wordWrappedMiniLabel);
            }
            if (telaInvalida) EditorGUILayout.HelpBox("Invalid screen update. Previous data retained.", MessageType.Warning);
            EditorGUILayout.Space();
            DesenharOpcoesImagem();
        }

        /// <summary>Opcoes da imagem enviada ao app (sincronizadas: mudar aqui ou no app atualiza os dois).</summary>
        static void DesenharOpcoesImagem()
        {
            EditorGUILayout.LabelField("Streaming", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Synchronized with the app: a change on either side updates the other.", EditorStyles.wordWrappedMiniLabel);
            var novas = ConfiguracoesEngineLink.Atual.Copia();
            EditorGUI.BeginChangeCheck();
            var semLimite = EditorGUILayout.Toggle(
                new GUIContent("Unlimited FPS", "Sends as many images as capture, network and device allow."), novas.fpsMaximo == 0);
            if (semLimite) novas.fpsMaximo = 0;
            else novas.fpsMaximo = EditorGUILayout.IntSlider("Max FPS", novas.fpsMaximo == 0 ? 60 : novas.fpsMaximo, 1, 240);
            novas.qualidadeJpeg = EditorGUILayout.IntSlider(
                new GUIContent("JPEG quality", "Higher looks better but uses more network and CPU."), novas.qualidadeJpeg, 1, 100);
            novas.resolucao = (ModoResolucao)EditorGUILayout.Popup(new GUIContent("Resolution"), (int)novas.resolucao, NomesResolucao);
            if (novas.resolucao == ModoResolucao.Porcentagem)
                novas.porcentagemResolucao = EditorGUILayout.IntSlider("Percentage", novas.porcentagemResolucao, 10, 100);
#if CASSINAX_INPUT_SYSTEM
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Input", EditorStyles.boldLabel);
            novas.entradaSemFoco = EditorGUILayout.Toggle(new GUIContent("Touches without focus",
                "Input System: device touches reach the game even when the Game view is not focused. Uses a temporary copy of the Input System settings; the project asset is not changed."),
                novas.entradaSemFoco);
#endif
            if (EditorGUI.EndChangeCheck()) ConfiguracoesEngineLink.Salvar(novas);
        }

        void DesenharPendente(string titulo, string mensagem)
        {
            DesenharUltimoEstado();
            EditorGUILayout.LabelField(titulo, EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(mensagem, MessageType.Info);
        }

        static string Texto(string valor) { return string.IsNullOrEmpty(valor) ? "Not reported" : valor; }

        void DesenharSobre()
        {
            EditorGUILayout.LabelField("Cassinax Softwares", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Package", InfoEngineLink.Versao);
            EditorGUILayout.LabelField("Package protocol", InfoEngineLink.Protocolo);
            EditorGUILayout.LabelField("Unity", Application.unityVersion);
            DesenharUltimoEstado();
            if (temAparelho)
            {
                EditorGUILayout.LabelField("App", Texto(versaoApp));
                EditorGUILayout.LabelField("App protocol", Texto(protocoloApp));
                if (ultimoAparelho != null)
                {
                    EditorGUILayout.LabelField("Device", Texto(ultimoAparelho.nome));
                    EditorGUILayout.LabelField("Model", Texto(ultimoAparelho.modelo));
                    EditorGUILayout.LabelField("Android", Texto(ultimoAparelho.android));
                }
            }
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Developer: Lord Dralkhy (Magno da SIlva Gomes)", EditorStyles.wordWrappedLabel);
            if (GUILayout.Button("Email: contato.oficial@cassinax.com"))
                Application.OpenURL("mailto:contato.oficial@cassinax.com");
            if (GUILayout.Button("Privacy Policy"))
                Application.OpenURL("https://cassinax.com/termos-e-politicas/documento.php?slug=engine-link-politicas-e-eula&produto=ferramenta&documento=engine-link-politica-de-privacidade");
            if (GUILayout.Button("EULA"))
                Application.OpenURL("https://cassinax.com/termos-e-politicas/documento.php?slug=engine-link-politicas-e-eula&produto=ferramenta&documento=engine-link-eula");
        }

        static void DesenharAvisos()
        {
            EditorGUILayout.LabelField("Android compatibility", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Editor observations do not replace a device build.", EditorStyles.wordWrappedMiniLabel);
            if (AvisosEngineLink.Visiveis.Count == 0)
                EditorGUILayout.HelpBox("No visible warnings. Acknowledged warnings may be hidden.", MessageType.Info);
            foreach (var aviso in AvisosEngineLink.Visiveis)
            {
                EditorGUILayout.HelpBox(aviso.texto, aviso.nivel == "atencao" ? MessageType.Warning : MessageType.Info);
                EditorGUILayout.LabelField("Observed: " + aviso.dados.observado + "\nExpected: " + aviso.dados.esperado,
                    EditorStyles.wordWrappedMiniLabel);
                if (GUILayout.Button("Acknowledged / Ciente")) AvisosEngineLink.Ciente(aviso);
            }
            if (GUILayout.Button("Show acknowledged warnings again")) AvisosEngineLink.Restaurar();
            EditorGUILayout.Space();
        }

        void DesenharCabecalho()
        {
            EditorGUILayout.LabelField("Cassinax Engine Link", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                "Package " + InfoEngineLink.Versao + "  ·  Protocol " + InfoEngineLink.Protocolo, EditorStyles.miniLabel);
        }

        static void DesenharEstado()
        {
            var texto = "Status: " + NomeEstado(EstadoEngineLink.Estado);
            var atual = EstadoEngineLink.AparelhoAtual;
            if (atual != null) texto += "\nDevice: " + atual.Nome + " (" + atual.Endereco + ")";
            if (EstadoEngineLink.LatenciaMs >= 0) texto += "\nLatency: " + EstadoEngineLink.LatenciaMs + " ms";
            if (!string.IsNullOrEmpty(EstadoEngineLink.Mensagem)) texto += "\n" + EstadoEngineLink.Mensagem;
            var tipo = EstadoEngineLink.Estado == EstadoConexao.Conectado ? MessageType.Info : MessageType.None;
            EditorGUILayout.HelpBox(texto, tipo);
        }

        void DesenharMeio()
        {
            var novo = (MeioConexao)GUILayout.Toolbar((int)meio, NomesMeios);
            if (novo != meio)
            {
                meio = novo;
                EditorPrefs.SetInt(ChaveMeio, (int)meio);
                EstadoEngineLink.DefinirAparelhos(null);
            }
            EditorGUILayout.HelpBox(Instrucoes(meio), MessageType.None);
        }

        void DesenharAparelhos(IControladorConexao controlador)
        {
            if (GUILayout.Button(EstadoEngineLink.Estado == EstadoConexao.Procurando ? "Searching..." : "Search devices"))
            {
                if (controlador != null) controlador.Procurar(meio);
            }

            if (meio == MeioConexao.WifiAutomatico) DesenharCodigo();
            // USB so pede codigo se o app estiver configurado para exigir.
            if (meio == MeioConexao.Usb) DesenharCodigo("Pairing code (only if the app requires it over USB)");
            // USB: a porta e a escolhida no app (lapis no Inicio); padrao 47100.
            if (meio == MeioConexao.Usb) portaUsb = CampoSalvo("Port (shown in the app)", portaUsb, ChavePortaUsb);

            var encontrados = 0;
            foreach (var aparelho in EstadoEngineLink.Aparelhos)
            {
                if (aparelho.Meio != meio) continue;
                encontrados++;
                using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.LabelField(aparelho.Nome + "\n" + aparelho.Endereco, EditorStyles.wordWrappedMiniLabel);
                    if (GUILayout.Button("Connect", GUILayout.Width(80)) && controlador != null)
                    {
                        int numeroPorta;
                        if (aparelho.Meio == MeioConexao.Usb && PortaValida(portaUsb, out numeroPorta)) aparelho.Porta = numeroPorta;
                        controlador.Conectar(aparelho, CodigoPara(aparelho));
                    }
                }
            }
            if (encontrados == 0 && EstadoEngineLink.Estado != EstadoConexao.Procurando)
            {
                EditorGUILayout.LabelField("No devices found.", EditorStyles.centeredGreyMiniLabel);
            }
        }

        void DesenharIpManual(IControladorConexao controlador)
        {
            ip = CampoSalvo("Device IP", ip, ChaveIp);
            porta = CampoSalvo("Port", porta, ChavePorta);
            DesenharCodigo();

            int numeroPorta;
            var portaValida = PortaValida(porta, out numeroPorta);
            using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(ip.Trim()) || !portaValida))
            {
                if (GUILayout.Button("Connect") && controlador != null)
                {
                    var aparelho = new AparelhoEncontrado(ip.Trim(), ip.Trim(), numeroPorta, MeioConexao.WifiIp);
                    controlador.Conectar(aparelho, CodigoLimpo());
                }
            }
        }

        void DesenharCodigo(string rotulo = "Pairing code")
        {
            codigo = EditorGUILayout.TextField(
                new GUIContent(rotulo, "6-digit code shown in the app. Needed only the first time."), codigo);
        }

        static bool PortaValida(string texto, out int numero)
        {
            return int.TryParse(texto, out numero) && numero > 0 && numero <= 65535;
        }

        string CodigoPara(AparelhoEncontrado aparelho)
        {
            return aparelho.Confiavel ? null : CodigoLimpo();
        }

        string CodigoLimpo()
        {
            var limpo = codigo == null ? "" : codigo.Replace(" ", "").Trim();
            return limpo.Length == 0 ? null : limpo;
        }

        static string CampoSalvo(string rotulo, string valor, string chave)
        {
            var novo = EditorGUILayout.TextField(rotulo, valor);
            if (novo != valor) EditorPrefs.SetString(chave, novo);
            return novo;
        }

        static string Instrucoes(MeioConexao meio)
        {
            switch (meio)
            {
                case MeioConexao.Usb:
                    return "Connect the device by cable with USB debugging enabled. Open Engine Link on the device: Home automatically waits for the engine. Use the port shown in Home.";
                case MeioConexao.WifiAutomatico:
                    return "Use the same network as this computer. Open Engine Link on the device: Home automatically waits for the engine. Search here and enter the pairing code shown in Home when requested.";
                default:
                    return "Open Engine Link on the device and copy the IP, port and pairing code from Home. The app automatically waits for the engine.";
            }
        }

        static string NomeEstado(EstadoConexao estado)
        {
            switch (estado)
            {
                case EstadoConexao.Procurando: return "Searching";
                case EstadoConexao.Conectando: return "Connecting";
                case EstadoConexao.Conectado: return "Connected";
                case EstadoConexao.Reconectando: return "Reconnecting";
                default: return "Disconnected";
            }
        }
    }
}
