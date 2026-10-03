using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

// Tela cheia no Android. Problema conhecido no Milo CI (bug UUM-149900 da Unity 6000.5.9f1, mesma versao do
// Lumera): depois de puxar a barra de notificacoes, as barras do sistema podiam ficar presas sobre o jogo.
// Aqui, se as barras ficam visiveis por alguns segundos com o app em foco, sao escondidas de novo.
public sealed partial class ObjetoMestre
{
    [Header("Tela cheia Android")]
    [Tooltip("Esconde de novo as barras do sistema que ficarem presas depois de um gesto.")]
    [SerializeField] bool recuperarTelaCheia = true;
    [Tooltip("Segundos com as barras visiveis antes de esconde-las (tempo real, inclusive pausado).")]
    [SerializeField, Min(1)] float atrasoOcultarBarras = 3;

    // Anuncios, login do Google e teclado virtual registram aqui enquanto estao na tela: nada e escondido.
    public static readonly HashSet<object> BloqueiosTelaCheia = new HashSet<object>();

    void PrepararTelaCheiaAndroid()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        StartCoroutine(RecuperarBarrasAndroid());
#endif
    }

#if UNITY_ANDROID && !UNITY_EDITOR
    bool aplicacaoSuspensa;
    void OnApplicationPause(bool pausado) => aplicacaoSuspensa = pausado;

    IEnumerator RecuperarBarrasAndroid()
    {
        var intervalo = new WaitForSecondsRealtime(0.5f);
        double visiveisDesde = double.NaN;
        bool erroRegistrado = false;
        while (true)
        {
            yield return intervalo;
            try
            {
                bool permitido = recuperarTelaCheia && Application.isFocused && !aplicacaoSuspensa &&
                    !TouchScreenKeyboard.visible && BloqueiosTelaCheia.Count == 0 && !AndroidApplication.isInMultiWindowMode;
                var insets = permitido ? AndroidApplication.currentWindowInsets : null;
                bool visiveis = insets != null &&
                    (insets.IsVisible(AndroidWindowInsets.Type.StatusBars) || insets.IsVisible(AndroidWindowInsets.Type.NavigationBars));
                // O atraso nao acumula enquanto o jogador esta em outra janela ou num anuncio.
                if (!visiveis) { visiveisDesde = double.NaN; continue; }
                double agora = Time.realtimeSinceStartupAsDouble;
                if (double.IsNaN(visiveisDesde)) visiveisDesde = agora;
                if (agora - visiveisDesde < Math.Max(1, atrasoOcultarBarras)) continue;
                visiveisDesde = agora;
                // Screen.fullScreen nao tem efeito nesta versao da Unity.
                insets.SetSystemBarsBehavior(AndroidWindowInsets.SystemBarsBehavior.ShowTransientBarsBySwipe);
                insets.Hide(AndroidWindowInsets.Type.StatusBars | AndroidWindowInsets.Type.NavigationBars);
                erroRegistrado = false;
            }
            catch (Exception erro)
            {
                if (!erroRegistrado) Debug.LogWarning($"[Tela Android] Falha ao recuperar barras: {erro.Message}", this);
                erroRegistrado = true;
            }
        }
    }
#endif
}
