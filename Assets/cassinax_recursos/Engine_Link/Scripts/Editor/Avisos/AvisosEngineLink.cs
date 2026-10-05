using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Cassinax.EngineLink.Editor
{
    [InitializeOnLoad]
    public static class AvisosEngineLink
    {
        const string Prefixo = "Cassinax.EngineLink.AvisoCiente.";
        static readonly string[] Ids = { "tela_suspensao", "tela_cheia", "orientacao", "fps_alvo", "qualidade_android" };
        static readonly PropertyInfo TelaCheia = typeof(PlayerSettings.Android).GetProperty("startInFullscreen", BindingFlags.Public | BindingFlags.Static);
        static readonly List<AvisoCompatibilidade> visiveis = new List<AvisoCompatibilidade>();
        static readonly Dictionary<string, string> anteriores = new Dictionary<string, string>();
        static string jsonAtual = "[]", jsonEnviado;
        static TelaRemota tela;
        static double proxima;
        public static event Action Mudou;
        public static IList<AvisoCompatibilidade> Visiveis { get { return visiveis.AsReadOnly(); } }

        static AvisosEngineLink()
        {
            EditorApplication.update += Atualizar;
            EditorApplication.projectChanged += ProjetoMudou;
            EditorApplication.playModeStateChanged += PlayMudou;
            ConexaoEngineLink.Conectou += Conectou;
            ConexaoEngineLink.Desconectou += Desconectou;
            ConexaoEngineLink.MensagemRecebida += Receber;
            AssemblyReloadEvents.beforeAssemblyReload += Encerrar;
            if (ConexaoEngineLink.Aparelho != null) tela = ConexaoEngineLink.Aparelho.tela;
        }
        static void Encerrar()
        {
            EditorApplication.update -= Atualizar;
            EditorApplication.projectChanged -= ProjetoMudou;
            EditorApplication.playModeStateChanged -= PlayMudou;
            ConexaoEngineLink.Conectou -= Conectou;
            ConexaoEngineLink.Desconectou -= Desconectou;
            ConexaoEngineLink.MensagemRecebida -= Receber;
            AssemblyReloadEvents.beforeAssemblyReload -= Encerrar;
        }
        static void PlayMudou(PlayModeStateChange estado) { proxima = 0; }
        static void ProjetoMudou() { Restaurar(); }
        static void Conectou(RespostaHelloOk hello) { tela = hello.tela; jsonEnviado = null; proxima = 0; }
        static void Desconectou(string motivo) { tela = null; jsonEnviado = null; proxima = 0; }
        static void Receber(Mensagem mensagem)
        {
            if (mensagem.Tipo != TipoMensagem.Tela || mensagem.Conteudo.Length > 4096) return;
            try { tela = JsonUtility.FromJson<TelaRemota>(mensagem.Texto()); proxima = 0; }
            catch (ArgumentException) { /* Mensagem invalida nao muda configuracoes do projeto. */ }
        }
        public static void Ciente(AvisoCompatibilidade aviso)
        {
            SessionState.SetString(Prefixo + aviso.id, JsonUtility.ToJson(aviso));
            proxima = 0;
        }
        public static void Restaurar()
        {
            foreach (var id in Ids) SessionState.EraseString(Prefixo + id);
            proxima = 0;
        }
        static void Atualizar()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.timeSinceStartup < proxima) return;
            proxima = EditorApplication.timeSinceStartup + 2;
            var todos = AvaliadorAvisos.Avaliar(Ler());
            var novos = new Dictionary<string, string>();
            visiveis.Clear();
            foreach (var aviso in todos)
            {
                var assinatura = JsonUtility.ToJson(aviso);
                string anterior;
                if (anteriores.TryGetValue(aviso.id, out anterior) && anterior != assinatura) SessionState.EraseString(Prefixo + aviso.id);
                novos[aviso.id] = assinatura;
                if (SessionState.GetString(Prefixo + aviso.id, "") != assinatura) visiveis.Add(aviso);
            }
            foreach (var id in anteriores.Keys) if (!novos.ContainsKey(id)) SessionState.EraseString(Prefixo + id);
            anteriores.Clear();
            foreach (var par in novos) anteriores[par.Key] = par.Value;
            var json = new StringBuilder("[");
            for (var i = 0; i < visiveis.Count; i++) { if (i > 0) json.Append(','); json.Append(JsonUtility.ToJson(visiveis[i])); }
            json.Append(']');
            var valor = json.ToString();
            if (valor != jsonAtual) { jsonAtual = valor; if (Mudou != null) Mudou(); }
            if (ConexaoEngineLink.Conectado && jsonEnviado != jsonAtual &&
                ConexaoEngineLink.Enviar(Mensagem.DeTexto(TipoMensagem.Avisos, jsonAtual))) jsonEnviado = jsonAtual;
        }
        static EstadoCompatibilidade Ler()
        {
            var e = new EstadoCompatibilidade { EmPlay = EditorApplication.isPlaying,
                SleepTimeout = Screen.sleepTimeout, FpsAlvo = Application.targetFrameRate,
                ModoTela = PlayerSettings.fullScreenMode.ToString(), ForaAreaSegura = PlayerSettings.Android.renderOutsideSafeArea,
                QualidadeAtual = QualitySettings.GetQualityLevel() };
            if (TelaCheia != null) { try { e.TelaCheia = (bool)TelaCheia.GetValue(null, null); } catch (Exception) { e.TelaCheia = null; } }
            var orientacao = PlayerSettings.defaultInterfaceOrientation;
            var auto = orientacao == UIOrientation.AutoRotation;
            var retrato = auto ? PlayerSettings.allowedAutorotateToPortrait || PlayerSettings.allowedAutorotateToPortraitUpsideDown :
                orientacao == UIOrientation.Portrait || orientacao == UIOrientation.PortraitUpsideDown;
            var paisagem = auto ? PlayerSettings.allowedAutorotateToLandscapeLeft || PlayerSettings.allowedAutorotateToLandscapeRight :
                orientacao == UIOrientation.LandscapeLeft || orientacao == UIOrientation.LandscapeRight;
            e.OrientacoesBuild = orientacao + "; portrait=" + retrato + "; landscape=" + paisagem;
            if (tela != null)
            {
                e.OrientacaoTeste = tela.orientacao;
                if (tela.orientacao == "retrato" || tela.orientacao == "portrait") e.OrientacaoPermitida = retrato;
                else if (tela.orientacao == "paisagem" || tela.orientacao == "landscape") e.OrientacaoPermitida = paisagem;
            }
            e.QualidadeAndroid = QualidadePadraoAndroid();
            var nomes = QualitySettings.names;
            e.NomeQualidadeAtual = Nome(nomes, e.QualidadeAtual);
            e.NomeQualidadeAndroid = Nome(nomes, e.QualidadeAndroid);
            return e;
        }
        static string Nome(string[] nomes, int indice) { return indice >= 0 && indice < nomes.Length ? nomes[indice] : "Unknown"; }
        static int QualidadePadraoAndroid()
        {
            try
            {
                var assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/QualitySettings.asset");
                if (assets.Length == 0) return -1;
                var serialized = new SerializedObject(assets[0]);
                try
                {
                    var mapa = serialized.FindProperty("m_PerPlatformDefaultQuality");
                    if (mapa == null || !mapa.isArray) return -1;
                    for (var i = 0; i < mapa.arraySize; i++)
                    {
                        var par = mapa.GetArrayElementAtIndex(i);
                        var chave = par.FindPropertyRelative("first");
                        var valor = par.FindPropertyRelative("second");
                        if (chave != null && valor != null && chave.stringValue == "Android") return valor.intValue;
                    }
                }
                finally { serialized.Dispose(); }
            }
            catch (Exception) { /* Layout nao reconhecido: reportar desconhecido, sem adivinhar. */ }
            return -1;
        }
    }
}
