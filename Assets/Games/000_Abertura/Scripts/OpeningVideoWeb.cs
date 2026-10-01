using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

/*
 * Abertura no WebGL.
 *
 * O navegador não toca VideoClip embutido — o importador do
 * Unity nem chega a gerar o asset quando o alvo de build é
 * WebGL. Em vez de tentar reproduzir e falhar, esta versão
 * apenas segue para a próxima cena.
 *
 * Vive no mesmo GameObject que o OpeningVideoNative. Exatamente
 * um dos dois sobrevive ao Awake; o outro se destrói.
 */
public sealed class OpeningVideoWeb : MonoBehaviour
{
    [Header("Navegação")]
    [Tooltip("Cena carregada em seguida.")]
    [SerializeField]
    private string nextSceneName = "Pre-menu";

    [Tooltip(
        "Usada apenas se a cena principal não estiver no Build " +
        "Profile. Deixe vazio se não houver alternativa."
    )]
    [SerializeField]
    private string fallbackSceneName = "";

    [Tooltip(
        "Espera antes de trocar de cena. Dá tempo de o canvas " +
        "de carregamento aparecer em vez de piscar."
    )]
    [SerializeField, Min(0f)]
    private float delayBeforeNextScene = 0.25f;

    private bool selfDestroyed;
    private bool isLeavingScene;

    private void Awake()
    {
#if UNITY_WEBGL
        /*
         * Plataforma correta: segue.
         */
#else
        /*
         * Qualquer outra plataforma: quem manda é o
         * OpeningVideoNative, que sabe tocar o vídeo.
         *
         * Destrói apenas o COMPONENTE. O GameObject é
         * compartilhado pelos três scripts da abertura.
         */
        selfDestroyed = true;
        Destroy(this);
#endif
    }

    private void Start()
    {
        if (selfDestroyed)
            return;

        StartCoroutine(GoToNextScene());
    }

    private IEnumerator GoToNextScene()
    {
        if (delayBeforeNextScene > 0f)
        {
            yield return new WaitForSecondsRealtime(
                delayBeforeNextScene
            );
        }

        LoadNextScene();
    }

    private void LoadNextScene()
    {
        if (isLeavingScene)
            return;

        if (TryLoad(nextSceneName))
            return;

        if (TryLoad(fallbackSceneName))
        {
            Debug.LogWarning(
                $"A cena \"{nextSceneName}\" não está no Build " +
                $"Profile. Usando \"{fallbackSceneName}\".",
                this
            );

            return;
        }

        /*
         * Último recurso: a cena seguinte por índice.
         */
        int nextIndex =
            SceneManager.GetActiveScene().buildIndex + 1;

        if (nextIndex < SceneManager.sceneCountInBuildSettings)
        {
            isLeavingScene = true;
            SceneManager.LoadScene(nextIndex);
            return;
        }

        Debug.LogError(
            "Não há próxima cena válida depois da abertura. " +
            "Confira o Build Profile.",
            this
        );
    }

    private bool TryLoad(string sceneName)
    {
        if (string.IsNullOrWhiteSpace(sceneName))
            return false;

        if (!Application.CanStreamedLevelBeLoaded(sceneName))
            return false;

        isLeavingScene = true;
        SceneManager.LoadScene(sceneName);
        return true;
    }
}
