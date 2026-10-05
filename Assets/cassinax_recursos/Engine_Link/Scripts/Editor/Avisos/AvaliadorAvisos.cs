using System;
using System.Collections.Generic;

namespace Cassinax.EngineLink.Editor
{
    [Serializable]
    public sealed class DadosAviso { public string origem; public string observado; public string esperado; }
    [Serializable]
    public sealed class AvisoCompatibilidade
    {
        public string id;
        public string nivel;
        public string texto;
        public DadosAviso dados;
    }
    public sealed class EstadoCompatibilidade
    {
        public bool EmPlay;
        public int SleepTimeout;
        public int FpsAlvo;
        public bool? TelaCheia;
        public string ModoTela;
        public bool ForaAreaSegura;
        public bool? OrientacaoPermitida;
        public string OrientacaoTeste;
        public string OrientacoesBuild;
        public int QualidadeAtual;
        public int QualidadeAndroid = -1;
        public string NomeQualidadeAtual;
        public string NomeQualidadeAndroid;
    }

    /// <summary>Regras puras; valores do Editor nao sao medicoes de uma build Android.</summary>
    public static class AvaliadorAvisos
    {
        public static List<AvisoCompatibilidade> Avaliar(EstadoCompatibilidade e)
        {
            var lista = new List<AvisoCompatibilidade>();
            Adicionar(lista, "tela_suspensao", e.EmPlay && e.SleepTimeout != -1 ? "atencao" : "info",
                "Engine Link can keep the device awake independently. Screen.sleepTimeout observed in the Editor is not proof of Android behavior. Set SleepTimeout.NeverSleep when needed and verify a device build.",
                e.EmPlay ? "PlayMode/Editor" : "NaoVerificado", e.EmPlay ? e.SleepTimeout.ToString() : "Not in Play Mode", "SleepTimeout.NeverSleep (-1)");
            if (e.TelaCheia != true)
                Adicionar(lista, "tela_cheia", e.TelaCheia.HasValue ? "atencao" : "info",
                    "Engine Link fullscreen does not enable fullscreen in your game. Check Player Settings > Android > Start in fullscreen and verify system bars in a device build.",
                    e.TelaCheia.HasValue ? "PlayerSettings" : "NaoVerificado",
                    (e.TelaCheia.HasValue ? e.TelaCheia.Value.ToString() : "Unknown") + "; mode=" + e.ModoTela + "; outsideSafeArea=" + e.ForaAreaSegura,
                    "Android startInFullscreen=true; review safe area separately");
            if (e.OrientacaoPermitida != true)
                Adicionar(lista, "orientacao", e.OrientacaoPermitida.HasValue ? "atencao" : "info",
                    "The device orientation may not be allowed by the Android build. Check Default Orientation and autorotation. Portrait/landscape alone cannot distinguish reversed rotations.",
                    e.OrientacaoPermitida.HasValue ? "PlayerSettings+TELA" : "NaoVerificado",
                    e.OrientacaoTeste ?? "Device orientation not received", e.OrientacoesBuild);
            if (!e.EmPlay || e.FpsAlvo <= 0)
                Adicionar(lista, "fps_alvo", e.EmPlay ? "atencao" : "info",
                    "Editor frame rate is not Android performance. Set Application.targetFrameRate explicitly when needed; mobile platforms use their default when unset (typically 30 FPS). Verify refresh-rate limits in a build.",
                    e.EmPlay ? "PlayMode/Editor" : "NaoVerificado", e.EmPlay ? e.FpsAlvo.ToString() : "Not in Play Mode", "Explicit positive targetFrameRate");
            if (e.QualidadeAndroid < 0 || e.QualidadeAndroid != e.QualidadeAtual)
                Adicionar(lista, "qualidade_android", e.QualidadeAndroid < 0 ? "info" : "atencao",
                    "The current Editor quality may differ from the Android default. Review Quality Settings and the render pipeline assigned to each quality level. Build Profiles or game code may override the default.",
                    e.QualidadeAndroid < 0 ? "NaoVerificado" : "QualitySettings",
                    e.NomeQualidadeAtual, e.QualidadeAndroid < 0 ? "Android default not readable" : e.NomeQualidadeAndroid);
            return lista;
        }
        static void Adicionar(List<AvisoCompatibilidade> lista, string id, string nivel, string texto,
            string origem, string observado, string esperado)
        {
            lista.Add(new AvisoCompatibilidade { id = id, nivel = nivel, texto = texto,
                dados = new DadosAviso { origem = origem, observado = observado ?? "Unknown", esperado = esperado ?? "Unknown" } });
        }
    }
}
