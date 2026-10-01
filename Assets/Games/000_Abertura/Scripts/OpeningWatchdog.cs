using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

/*
 * Rede de segurança da abertura.
 *
 * OpeningVideoNative e OpeningVideoWeb se destroem quando a
 * plataforma não é a deles. O desenho prevê que exatamente um
 * sobreviva — mas se uma combinação inesperada de plataforma e
 * define derrubar os dois, ninguém trocaria de cena e o jogo
 * ficaria preso na abertura para sempre.
 *
 * Este script espera alguns segundos e, se não encontrar
 * nenhum dos dois vivo, abre a próxima cena por conta própria.
 *
 * Ele NÃO interrompe uma abertura que esteja funcionando: se
 * qualquer um dos dois existir, sai de cena sem fazer nada,
 * por mais longo que o vídeo seja.
 */
public sealed class OpeningWatchdog : MonoBehaviour
{

    [SerializeField]
    private GameObject OpeningController;

    [Header("Verificação")]
    [Tooltip(
        "Tempo de espera antes de conferir se algum controlador " +
        "da abertura sobreviveu."
    )]
    [SerializeField, Min(0.5f)]
    private float checkDelay = 3f;

    [Header("Navegação")]
    [Tooltip("Cena aberta se nenhum controlador existir.")]
    [SerializeField]
    private string nextSceneName = "Pre-menu";

    [Tooltip(
        "Usada apenas se a cena principal não estiver no Build " +
        "Profile. Deixe vazio se não houver alternativa."
    )]
    [SerializeField]
    private string fallbackSceneName = "";

    private IEnumerator Start()
    {
        /*
         * Tempo real: os controladores mexem no targetFrameRate
         * e podem alterar o ritmo do jogo durante a abertura.
         */
        yield return new WaitForSecondsRealtime(
            Mathf.Max(0.5f, checkDelay)
        );

        if (HasLivingController())
        {
            /*
             * Alguém assumiu a abertura. Nada a fazer.
             */
            yield break;
        }

        Debug.LogWarning(
            $"{nameof(OpeningWatchdog)}: nenhum controlador da " +
            $"abertura sobreviveu depois de {checkDelay:0.##}s. " +
            "Seguindo para a próxima cena.",
            this
        );

        LoadNextScene();
    }

    private bool HasLivingController()
    {

        if(OpeningController == null) return false;

        /*
         * Os dois se destroem com Destroy(this), que remove o
         * componente e mantém o GameObject. Procurar no próprio
         * objeto basta e evita varrer a cena inteira.
         */
        if (OpeningController.GetComponent<OpeningVideoNative>() != null)
            return true;

        if (OpeningController.GetComponent<OpeningVideoWeb>() != null)
            return true;

        return false;
    }

    private void LoadNextScene()
    {
        if (TryLoad(nextSceneName))
            return;

        if (TryLoad(fallbackSceneName))
            return;

        int nextIndex =
            SceneManager.GetActiveScene().buildIndex + 1;

        if (nextIndex < SceneManager.sceneCountInBuildSettings)
        {
            SceneManager.LoadScene(nextIndex);
            return;
        }

        Debug.LogError(
            $"{nameof(OpeningWatchdog)}: não há próxima cena " +
            "válida para carregar. Confira o Build Profile.",
            this
        );
    }

    private bool TryLoad(string sceneName)
    {
        if (string.IsNullOrWhiteSpace(sceneName))
            return false;

        if (!Application.CanStreamedLevelBeLoaded(sceneName))
            return false;

        SceneManager.LoadScene(sceneName);
        return true;
    }
}
