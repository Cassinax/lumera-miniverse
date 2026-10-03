using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;
#if UNITY_ANDROID
using GooglePlayGames;
using GooglePlayGames.BasicApi;
#endif

// Filho "Google_Play_Services" do Objeto Mestre. Conecta o jogador ao Play Games (Android) e expoe nome e foto.
// Sem conexao (outra plataforma, app sem ID configurado, jogador recusou), o Menu mostra o jogador e a foto padrao.
[DisallowMultipleComponent]
public sealed class ControladorPlayGames : MonoBehaviour
{
    [Tooltip("Tenta o login silencioso ao abrir o app.")]
    [SerializeField] bool conectarAoIniciar = true;

    public bool Conectado { get; private set; }
    public string NomeJogador { get; private set; } = "";
    public Texture2D Foto { get; private set; }
    // Avisado ao conectar, desconectar e quando a foto termina de carregar.
    public event Action PerfilAlterado;

    void Start()
    {
        if (conectarAoIniciar) Conectar(false);
    }

    // manual = o jogador tocou num botao de login (abre a janela do Play Games).
    public void Conectar(bool manual)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (!GameInfo.ApplicationIdInitialized())
        {
            Debug.Log("[Play Games] App sem ID configurado (Window > Google Play Games > Setup). Jogador local.", this);
            return;
        }
        PlayGamesPlatform.Activate();
        Action<SignInStatus> concluido = status => AoConcluirLogin(status == SignInStatus.Success);
        if (manual) PlayGamesPlatform.Instance.ManuallyAuthenticate(concluido);
        else PlayGamesPlatform.Instance.Authenticate(concluido);
#endif
    }

    void AoConcluirLogin(bool sucesso)
    {
        Conectado = sucesso;
        NomeJogador = "";
        Foto = null;
#if UNITY_ANDROID && !UNITY_EDITOR
        if (sucesso)
        {
            NomeJogador = PlayGamesPlatform.Instance.GetUserDisplayName() ?? "";
            string url = PlayGamesPlatform.Instance.GetUserImageUrl();
            if (!string.IsNullOrEmpty(url)) StartCoroutine(CarregarFoto(url));
        }
#endif
        PerfilAlterado?.Invoke();
    }

    //---------- Placar e conquistas
    // Os ids vem do Play Console (Leaderboards / Achievements). Id vazio ou jogador desconectado: nada e enviado,
    // so registrado no log. O Play Games guarda o progresso das conquistas incrementais.

    public void EnviarPlacar(string idPlacar, long valor)
    {
        if (!PodeEnviar(idPlacar, "placar " + valor)) return;
#if UNITY_ANDROID && !UNITY_EDITOR
        PlayGamesPlatform.Instance.ReportScore(valor, idPlacar, ok => { if (!ok) Debug.LogWarning("[Play Games] Placar nao enviado.", this); });
#endif
    }

    public void DesbloquearConquista(string idConquista)
    {
        if (!PodeEnviar(idConquista, "conquista")) return;
#if UNITY_ANDROID && !UNITY_EDITOR
        PlayGamesPlatform.Instance.ReportProgress(idConquista, 100.0, ok => { if (!ok) Debug.LogWarning("[Play Games] Conquista nao enviada.", this); });
#endif
    }

    public void IncrementarConquista(string idConquista, int passos)
    {
        if (passos <= 0 || !PodeEnviar(idConquista, "+" + passos)) return;
#if UNITY_ANDROID && !UNITY_EDITOR
        PlayGamesPlatform.Instance.IncrementAchievement(idConquista, passos, ok => { if (!ok) Debug.LogWarning("[Play Games] Incremento nao enviado.", this); });
#endif
    }

    bool PodeEnviar(string id, string descricao)
    {
        if (!string.IsNullOrEmpty(id) && Conectado) return true;
        Debug.Log($"[Play Games] Nao enviado ({(Conectado ? "id vazio" : "desconectado")}): {id} {descricao}", this);
        return false;
    }

    IEnumerator CarregarFoto(string url)
    {
        // So endereco web: o Android pode devolver URIs locais que o UnityWebRequest nao le.
        if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase)) yield break;
        using var pedido = UnityWebRequestTexture.GetTexture(url);
        yield return pedido.SendWebRequest();
        if (pedido.result != UnityWebRequest.Result.Success || !Conectado) yield break;
        Foto = DownloadHandlerTexture.GetContent(pedido);
        PerfilAlterado?.Invoke();
    }
}
