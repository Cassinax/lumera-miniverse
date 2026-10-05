using System;
using System.IO;
using UnityEngine;

namespace Cassinax.EngineLink.Editor
{
    /// <summary>Como a resolucao da imagem enviada ao app e escolhida.</summary>
    public enum ModoResolucao
    {
        /// <summary>Reduz ate o tamanho da area de exibicao do aparelho (nunca amplia).</summary>
        Aparelho = 0,
        /// <summary>Resolucao atual do Game view, sem reducao.</summary>
        Nativa = 1,
        /// <summary>Porcentagem da resolucao do Game view.</summary>
        Porcentagem = 2,
    }

    /// <summary>
    /// Opcoes do Engine Link escolhidas pelo dev. Ficam em UserSettings/ do projeto (por projeto e
    /// por pessoa, fora do controle de versao). Os campos podem ser lidos de qualquer thread.
    /// </summary>
    [Serializable]
    public sealed class ConfiguracoesEngineLink
    {
        const string Pasta = "UserSettings/CassinaxEngineLink";
        const string Arquivo = Pasta + "/Configuracoes.json";

        /// <summary>Imagens por segundo enviadas; 0 = sem limite (o maximo que captura, rede e aparelho aguentam).</summary>
        public int fpsMaximo = 0;
        /// <summary>Qualidade do JPEG (1-100).</summary>
        public int qualidadeJpeg = 70;
        public ModoResolucao resolucao = ModoResolucao.Aparelho;
        /// <summary>Usado com <see cref="ModoResolucao.Porcentagem"/> (10-100).</summary>
        public int porcentagemResolucao = 50;
        /// <summary>Com Input System: toques chegam ao jogo mesmo com o Game view sem foco.</summary>
        public bool entradaSemFoco = true;

        /// <summary>Intervalo entre tentativas apos uma queda.</summary>
        public int reconexaoSegundos = 10;
        /// <summary>Intervalo do PING que mede a latencia e mantem a sessao viva.</summary>
        public int pingMs = 1000;
        /// <summary>Sem nada recebido por este tempo, a sessao e considerada caida (o app usa o mesmo valor).</summary>
        public int tempoLimiteMs = 5000;
        /// <summary>Quanto tempo o Wi-Fi automatico espera respostas.</summary>
        public int procuraWifiMs = 1500;
        /// <summary>Tempo maximo de cada comando do adb.</summary>
        public int tempoLimiteAdbMs = 8000;

        public static readonly ConfiguracoesEngineLink Padrao = new ConfiguracoesEngineLink();

        static ConfiguracoesEngineLink atual;

        /// <summary>Opcoes em uso (carregadas na primeira leitura).</summary>
        public static ConfiguracoesEngineLink Atual
        {
            get
            {
                if (atual == null) atual = Carregar();
                return atual;
            }
        }

        public static event Action Mudou;

        /// <summary>Corrige valores fora das faixas aceitas.</summary>
        public void Normalizar()
        {
            fpsMaximo = Mathf.Clamp(fpsMaximo, 0, 240);
            qualidadeJpeg = Mathf.Clamp(qualidadeJpeg, 1, 100);
            if (!Enum.IsDefined(typeof(ModoResolucao), resolucao)) resolucao = ModoResolucao.Aparelho;
            porcentagemResolucao = Mathf.Clamp(porcentagemResolucao, 10, 100);
            reconexaoSegundos = Mathf.Clamp(reconexaoSegundos, 1, 3600);
            pingMs = Mathf.Clamp(pingMs, 250, 10000);
            // O app envia PING a cada 1 s: o tempo limite precisa de folga acima disso.
            tempoLimiteMs = Mathf.Clamp(tempoLimiteMs, 2000, 120000);
            procuraWifiMs = Mathf.Clamp(procuraWifiMs, 500, 30000);
            tempoLimiteAdbMs = Mathf.Clamp(tempoLimiteAdbMs, 1000, 120000);
        }

        public ConfiguracoesEngineLink Copia()
        {
            return (ConfiguracoesEngineLink)MemberwiseClone();
        }

        /// <summary>Normaliza, aplica e grava em UserSettings.</summary>
        public static void Salvar(ConfiguracoesEngineLink novas)
        {
            novas.Normalizar();
            atual = novas;
            try
            {
                Directory.CreateDirectory(Pasta);
                File.WriteAllText(Arquivo, JsonUtility.ToJson(novas, true));
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Engine Link] Could not save settings: " + e.Message);
            }
            var mudou = Mudou;
            if (mudou != null) mudou();
        }

        static ConfiguracoesEngineLink Carregar()
        {
            var lidas = new ConfiguracoesEngineLink();
            try
            {
                if (File.Exists(Arquivo)) JsonUtility.FromJsonOverwrite(File.ReadAllText(Arquivo), lidas);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Engine Link] Invalid settings file, using defaults: " + e.Message);
                lidas = new ConfiguracoesEngineLink();
            }
            lidas.Normalizar();
            return lidas;
        }
    }
}
