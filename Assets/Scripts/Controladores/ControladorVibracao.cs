using UnityEngine;

// Filho "Vibracao_Controller" do Objeto Mestre. Os jogos pedem a vibracao; a opcao das configuracoes decide.
// No Android usa a API nativa (duracao e intensidade controladas). Em outras plataformas, Handheld.Vibrate.
[DisallowMultipleComponent]
public sealed class ControladorVibracao : MonoBehaviour
{
    [Tooltip("Duracao padrao de Vibrar(), em milissegundos.")]
    [SerializeField, Min(1)] int duracaoPadrao = 40;
    [Tooltip("Intensidade padrao de Vibrar(), de 1 (fraca) a 255 (maxima).")]
    [SerializeField, Range(1, 255)] int intensidadePadrao = 120;

    public bool Ativa { get; private set; } = true;
    SaveAdapter save;
#if UNITY_ANDROID && !UNITY_EDITOR
    AndroidJavaObject vibrador;
    bool vibradorProcurado, temIntensidade;
    int sdk;
#endif

    public void Configurar(SaveAdapter adapter)
    {
        save = adapter;
        Ativa = !save || save.ObterBoolConfig(SaveAdapter.CHAVE_VIBRACAO, true);
    }

    public void DefinirAtiva(bool ativa, bool salvar = true)
    {
        Ativa = ativa;
        if (salvar && save) save.SalvarConfig(SaveAdapter.CHAVE_VIBRACAO, ativa);
    }

    public void Vibrar() => Vibrar(duracaoPadrao, intensidadePadrao);

    // duracao em ms; intensidade de 1 a 255 (aparelhos sem controle de intensidade usam so a duracao).
    public void Vibrar(int duracao, int intensidade)
    {
        if (!Ativa || duracao <= 0) return;
#if UNITY_ANDROID && !UNITY_EDITOR
        if (VibrarAndroid(duracao, Mathf.Clamp(intensidade, 1, 255))) return;
#endif
#if UNITY_ANDROID || UNITY_IOS
        // Tambem garante a permissao VIBRATE no manifesto gerado pela Unity.
        Handheld.Vibrate();
#endif
    }

#if UNITY_ANDROID && !UNITY_EDITOR
    bool VibrarAndroid(int duracao, int intensidade)
    {
        try
        {
            if (!vibradorProcurado)
            {
                vibradorProcurado = true;
                using var versao = new AndroidJavaClass("android.os.Build$VERSION");
                sdk = versao.GetStatic<int>("SDK_INT");
                using var jogador = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                using var atividade = jogador.GetStatic<AndroidJavaObject>("currentActivity");
                vibrador = atividade.Call<AndroidJavaObject>("getSystemService", "vibrator");
                temIntensidade = vibrador != null && sdk >= 26 && vibrador.Call<bool>("hasAmplitudeControl");
            }
            if (vibrador == null || !vibrador.Call<bool>("hasVibrator")) return false;
            if (sdk >= 26)
            {
                using var efeito = new AndroidJavaClass("android.os.VibrationEffect");
                using var vibracao = efeito.CallStatic<AndroidJavaObject>("createOneShot", (long)duracao, temIntensidade ? intensidade : -1);
                vibrador.Call("vibrate", vibracao);
            }
            else vibrador.Call("vibrate", (long)duracao);
            return true;
        }
        catch (System.Exception erro)
        {
            Debug.LogWarning("[Vibracao] Falha na vibracao nativa: " + erro.Message, this);
            return false;
        }
    }
#endif
}
