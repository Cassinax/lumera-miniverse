using System;
using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.SceneManagement;
using UnityEngine.Video;

public sealed class OpeningVideoNative : MonoBehaviour
{
    private const string OpeningWatchedKey = "OpeningVideoWatched";

    [Header("Navegação")]
    [Tooltip("Nome da cena que será carregada depois da abertura.")]
    [SerializeField]
    private string nextSceneName = "Pre-menu";

    [Tooltip("Cena alternativa caso a cena principal não esteja disponível.")]
    [SerializeField]
    private string fallbackSceneName = "Menu";

    [Header("Vídeo")]
    [SerializeField]
    private VideoPlayer videoPlayer;

    [Tooltip(
        "Câmera que exibe o vídeo em tela cheia. " +
        "Vazio usa a Camera.main."
    )]
    [SerializeField]
    private Camera displayCamera;


    [Tooltip("Tempo máximo para o vídeo ser preparado.")]
    [SerializeField, Min(1f)]
    private float prepareTimeout = 15f;

    [Tooltip("Tempo máximo para o vídeo começar depois de chamar Play.")]
    [SerializeField, Min(1f)]
    private float playbackStartTimeout = 8f;

    [Tooltip("Tempo máximo sem avanço do vídeo antes de considerá-lo travado.")]
    [SerializeField, Min(1f)]
    private float playbackStallTimeout = 8f;

    [Tooltip("Tempo adicional permitido além da duração esperada do vídeo.")]
    [SerializeField, Min(1f)]
    private float playbackEndTolerance = 10f;

    [Header("Comportamento")]
    [Tooltip("Permite pular a abertura depois que ela já foi assistida.")]
    [SerializeField]
    private bool allowSkipAfterFirstView = true;

    [Tooltip("Ajusta o FPS da aplicação para a taxa atual da tela no Android.")]
    [SerializeField]
    private bool matchScreenRefreshRate = true;

    [Tooltip(
        "Trava o FPS da aplicação na taxa do vídeo durante a reprodução " +
        "e restaura os valores originais ao terminar."
    )]
    [SerializeField]
    private bool matchVideoFrameRate = true;

    [Tooltip(
        "FPS provisório aplicado já no Awake, antes de o vídeo " +
        "ser lido. Assim o VideoPlayer nunca inicializa na taxa " +
        "alta do monitor."
    )]
    [SerializeField, Range(24, 60)]
    private int provisionalOpeningFrameRate = 30;

    [Header("Estabilização antes do Play")]
    [Tooltip(
        "Frames completos aguardados depois da preparação, antes " +
        "de iniciar o vídeo. Dá tempo de a cena, a câmera e os " +
        "recursos terminarem de carregar."
    )]
    [SerializeField, Range(0, 30)]
    private int framesToStabilize = 5;

    [Tooltip(
        "Tempo mínimo, em segundos, entre a cena abrir e o vídeo " +
        "começar. Evita a travada inicial quando o carregamento " +
        "ainda está acontecendo."
    )]
    [SerializeField, Min(0f)]
    private float minimumTimeBeforePlay = 0.5f;

    private bool hasWatchedBefore;
    private bool canSkip;

    private bool videoStarted;
    private bool videoFinished;
    private bool videoFailed;

    private bool isLeavingScene;

    /*
     * Marcado quando o script se destrói por ser a plataforma
     * errada. Destroy() só tem efeito no fim do frame, então
     * Start e Update precisam saber que não devem agir.
     */
    private bool selfDestroyed;

    private float sceneStartedAt;

    private bool frameRateLocked;
    private int vSyncBeforeLock;

    private long lastObservedFrame = -1;
    private double lastObservedVideoTime;
    private float lastPlaybackProgressTime;

    private void Awake()
    {
#if UNITY_WEBGL
        /*
         * Build de WebGL: este script não tem o que fazer.
         *
         * VideoClip não existe no WebGL — o importador do
         * Unity nem gera o asset — então qualquer tentativa
         * daqui pra frente falharia. Quem assume é o
         * OpeningVideoWeb.
         *
         * Destrói apenas o COMPONENTE, nunca o GameObject: os
         * outros dois scripts da abertura vivem no mesmo
         * objeto e precisam continuar.
         *
         * O teste não leva !UNITY_EDITOR de propósito. Com o
         * alvo de build em WebGL, o Editor importa os assets
         * como WebGL e o vídeo também não funciona nele; se
         * comportar igual à build é o que se quer testando.
         */
        selfDestroyed = true;
        Destroy(this);
        return;
#else
        /*
         * Marco zero da cena, usado para o tempo mínimo
         * antes do Play.
         */
        sceneStartedAt = Time.realtimeSinceStartup;

        if (matchScreenRefreshRate)
            ConfigureTargetFrameRate();

        /*
         * Antes de tocar no VideoPlayer: ele não pode chegar
         * a inicializar rodando na taxa cheia do monitor.
         */
        ApplyProvisionalFrameRateLock();

        /*
         * O fundo preto sólido da câmera é a tela preta: com
         * renderMode CameraNearPlane, enquanto não houver
         * frame válido não há nada desenhado no near plane, e
         * o que aparece é o próprio fundo da câmera.
         */
        ConfigureCamera();

        if (videoPlayer == null)
            videoPlayer = GetComponent<VideoPlayer>();

        if (videoPlayer == null)
        {
            Debug.LogError(
                $"{nameof(OpeningVideoNative)}: " +
                "nenhum VideoPlayer foi configurado.",
                this
            );

            return;
        }

        ConfigureVideoPlayer();
#endif
    }

    private void Start()
    {
        if (selfDestroyed)
            return;

        if (videoPlayer == null)
        {
            RequestNextScene();
            return;
        }

        StartCoroutine(PrepareAndPlayVideo());
    }

    private void Update()
    {
        if (selfDestroyed)
            return;

        if (isLeavingScene || !canSkip)
            return;

        if (WasSkipInputPressed())
        {
            Debug.Log("Abertura pulada pelo usuário.", this);

            /*
             * O usuário só pode pular quando já assistiu
             * à abertura anteriormente.
             */
            FinishOpening(markAsWatched: true);
        }
    }

    private void ConfigureVideoPlayer()
    {
        videoPlayer.playOnAwake = false;
        videoPlayer.waitForFirstFrame = true;
        videoPlayer.isLooping = false;

        /*
         * Renderiza direto no near plane da câmera, em tela
         * cheia, sem depender de RenderTexture nem de RawImage.
         */
        videoPlayer.renderMode =
            VideoRenderMode.CameraNearPlane;

        videoPlayer.targetCamera = displayCamera;
        videoPlayer.aspectRatio = VideoAspectRatio.FitOutside;

        /*
         * sendFrameReadyEvents fica DESLIGADO. Além do custo
         * de um callback por frame, alterar essa propriedade
         * com o vídeo rodando derruba a entrega de frames: a
         * imagem congela enquanto o relógio continua correndo
         * e só o frame final aparece.
         */
        videoPlayer.sendFrameReadyEvents = false;

        if (videoPlayer.canSetPlaybackSpeed)
            videoPlayer.playbackSpeed = 1f;

        /*
         * Usa o relógio não escalado do jogo.
         *
         * A reprodução não é afetada por Time.timeScale,
         * pausas ou câmera lenta.
         */
        if (videoPlayer.canSetTimeUpdateMode)
        {
            videoPlayer.timeUpdateMode =
                VideoTimeUpdateMode.UnscaledGameTime;
        }

        /*
         * Impede que o VideoPlayer descarte muitos frames
         * para tentar alcançar o relógio de reprodução.
         *
         * Isso prioriza a exibição sequencial dos frames.
         */
        if (videoPlayer.canSetSkipOnDrop)
            videoPlayer.skipOnDrop = false;

        /*
         * Peça central contra o travamento em alta taxa de
         * atualização: o player passa a decidir a hora de
         * exibir cada frame pelo relógio interno dele, e não
         * pelo ritmo do loop do jogo.
         *
         * Sem isto, uma tela de 144 Hz faz o jogo pedir
         * frames muito mais rápido do que o vídeo produz.
         */
        videoPlayer.timeReference =
            VideoTimeReference.InternalTime;

        videoPlayer.prepareCompleted += HandlePrepareCompleted;
        videoPlayer.started += HandleVideoStarted;
        videoPlayer.loopPointReached += HandleVideoFinished;
        videoPlayer.errorReceived += HandleVideoError;
    }

    private IEnumerator PrepareAndPlayVideo()
    {
        ResetRuntimeState();

        hasWatchedBefore =
            PlayerPrefs.GetInt(OpeningWatchedKey, 0) == 1;

        /*
         * Na primeira execução, canSkip será falso.
         *
         * Nas execuções seguintes, o vídeo poderá ser
         * pulado por toque, clique, teclado ou controle.
         */
        canSkip =
            allowSkipAfterFirstView &&
            hasWatchedBefore;

        try
        {
            videoPlayer.Prepare();
        }
        catch (Exception exception)
        {
            Debug.LogError(
                "Exceção ao preparar o vídeo de abertura.",
                this
            );

            Debug.LogException(exception, this);

            FinishOpening(markAsWatched: false);
            yield break;
        }

        float prepareStartedAt =
            Time.realtimeSinceStartup;

        while (
            !videoPlayer.isPrepared &&
            !videoFailed &&
            !isLeavingScene
        )
        {
            float elapsed =
                Time.realtimeSinceStartup -
                prepareStartedAt;

            if (elapsed >= prepareTimeout)
            {
                Debug.LogWarning(
                    $"O vídeo não foi preparado em " +
                    $"{prepareTimeout:0.##} segundos. " +
                    "Pulando a abertura.",
                    this
                );

                videoFailed = true;
                break;
            }

            yield return null;
        }

        if (isLeavingScene)
            yield break;

        if (videoFailed || !videoPlayer.isPrepared)
        {
            FinishOpening(markAsWatched: false);
            yield break;
        }

        /*
         * O vídeo já está preparado, então a taxa dele é
         * conhecida e o FPS pode ser travado antes do Play.
         */
        LockFrameRateToVideo();

        /*
         * Só agora o vídeo pode começar.
         *
         * A travada inicial acontece porque os primeiros
         * frames disputam CPU com o resto da abertura da
         * cena: carregamento de recursos, primeira montagem
         * da câmera e compilação de shaders.
         *
         * Esperar aqui, com a tela ainda parada, troca uma
         * travada visível por um atraso que ninguém percebe.
         */
        yield return StartCoroutine(WaitForSceneToStabilize());

        if (isLeavingScene)
            yield break;

        try
        {
            videoPlayer.Play();
        }
        catch (Exception exception)
        {
            Debug.LogError(
                "Exceção ao iniciar o vídeo de abertura.",
                this
            );

            Debug.LogException(exception, this);

            FinishOpening(markAsWatched: false);
            yield break;
        }

        /*
         * Alguns dispositivos podem aceitar Play(), mas nunca
         * iniciar efetivamente a reprodução.
         */
        float playRequestedAt =
            Time.realtimeSinceStartup;

        while (
            !videoStarted &&
            !videoPlayer.isPlaying &&
            !videoFinished &&
            !videoFailed &&
            !isLeavingScene
        )
        {
            float elapsed =
                Time.realtimeSinceStartup -
                playRequestedAt;

            if (elapsed >= playbackStartTimeout)
            {
                Debug.LogWarning(
                    $"O vídeo não começou em " +
                    $"{playbackStartTimeout:0.##} segundos. " +
                    "Pulando a abertura.",
                    this
                );

                videoFailed = true;
                break;
            }

            yield return null;
        }

        if (isLeavingScene)
            yield break;

        /*
         * Um vídeo muito curto pode terminar antes de o loop
         * detectar que a reprodução começou.
         */
        if (videoFinished)
        {
            FinishOpening(markAsWatched: true);
            yield break;
        }

        if (videoFailed)
        {
            FinishOpening(markAsWatched: false);
            yield break;
        }

        videoStarted = true;

        lastObservedFrame = videoPlayer.frame;
        lastObservedVideoTime = videoPlayer.time;
        lastPlaybackProgressTime =
            Time.realtimeSinceStartup;

        float playbackStartedAt =
            Time.realtimeSinceStartup;

        double expectedDuration =
            GetSafeVideoDuration();

        while (
            !videoFinished &&
            !videoFailed &&
            !isLeavingScene
        )
        {
            MonitorPlaybackProgress();

            /*
             * Proteção adicional caso loopPointReached
             * nunca seja chamado.
             */
            if (expectedDuration > 0)
            {
                double totalElapsed =
                    Time.realtimeSinceStartup -
                    playbackStartedAt;

                double maximumExpectedDuration =
                    expectedDuration +
                    playbackEndTolerance;

                if (totalElapsed > maximumExpectedDuration)
                {
                    Debug.LogWarning(
                        "O vídeo ultrapassou o tempo máximo " +
                        "esperado. Pulando a abertura.",
                        this
                    );

                    videoFailed = true;
                }
            }

            yield return null;
        }

        if (isLeavingScene)
            yield break;

        FinishOpening(markAsWatched: !videoFailed);
    }

    private void MonitorPlaybackProgress()
    {
        if (videoPlayer == null)
            return;

        long currentFrame =
            videoPlayer.frame;

        double currentVideoTime =
            videoPlayer.time;

        bool frameAdvanced =
            currentFrame >= 0 &&
            currentFrame != lastObservedFrame;

        bool timeAdvanced =
            currentVideoTime >
            lastObservedVideoTime + 0.001d;

        if (frameAdvanced || timeAdvanced)
        {
            lastObservedFrame = currentFrame;
            lastObservedVideoTime = currentVideoTime;
            lastPlaybackProgressTime =
                Time.realtimeSinceStartup;

            return;
        }

        float timeWithoutProgress =
            Time.realtimeSinceStartup -
            lastPlaybackProgressTime;

        if (timeWithoutProgress < playbackStallTimeout)
            return;

        Debug.LogWarning(
            $"O vídeo ficou sem avançar por " +
            $"{playbackStallTimeout:0.##} segundos. " +
            "Pulando a abertura.",
            this
        );

        videoFailed = true;
    }

    private double GetSafeVideoDuration()
    {
        if (videoPlayer == null)
            return 0;

        try
        {
            double duration =
                videoPlayer.length;

            if (
                duration > 0 &&
                !double.IsNaN(duration) &&
                !double.IsInfinity(duration)
            )
            {
                return duration;
            }
        }
        catch (Exception exception)
        {
            Debug.LogWarning(
                $"Não foi possível obter a duração do vídeo: " +
                $"{exception.Message}",
                this
            );
        }

        return 0;
    }

    private void FinishOpening(bool markAsWatched)
    {
        if (isLeavingScene)
            return;

        canSkip = false;

        /*
         * Registra como assistido quando:
         *
         * 1. O vídeo terminou normalmente; ou
         * 2. O usuário pulou depois de já ter assistido.
         *
         * Em caso de erro na primeira abertura, o jogo
         * continua, mas tentará mostrar o vídeo novamente.
         */
        if (markAsWatched)
        {
            try
            {
                PlayerPrefs.SetInt(
                    OpeningWatchedKey,
                    1
                );

                PlayerPrefs.Save();
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    $"Não foi possível salvar o PlayerPrefs: " +
                    $"{exception.Message}",
                    this
                );
            }
        }

        /*
         * Antes de qualquer coisa: a trava de FPS existe só
         * enquanto o vídeo toca, e este é o único caminho de
         * saída (fim natural, pulo ou erro).
         */
        RestoreFrameRate();

        StopVideoSafely();
        RequestNextScene();
    }

    private void StopVideoSafely()
    {
        if (videoPlayer == null)
            return;

        try
        {
            if (videoPlayer.isPlaying)
                videoPlayer.Stop();
        }
        catch (Exception exception)
        {
            /*
             * Mesmo que Stop falhe, o carregamento da
             * próxima cena continuará normalmente.
             */
            Debug.LogWarning(
                $"Erro ao parar o vídeo: " +
                $"{exception.Message}",
                this
            );
        }
    }

    private void RequestNextScene()
    {
        if (isLeavingScene)
            return;

        if (
            TryGetValidSceneName(
                nextSceneName,
                out string targetScene
            )
        )
        {
            StartCoroutine(
                LoadSceneRoutine(targetScene)
            );

            return;
        }

        if (
            TryGetValidSceneName(
                fallbackSceneName,
                out targetScene
            )
        )
        {
            Debug.LogWarning(
                $"A cena principal \"{nextSceneName}\" " +
                $"não está disponível. Carregando a cena " +
                $"alternativa \"{targetScene}\".",
                this
            );

            StartCoroutine(
                LoadSceneRoutine(targetScene)
            );

            return;
        }

        /*
         * Último fallback: tenta carregar a próxima
         * cena por índice.
         */
        int currentBuildIndex =
            SceneManager.GetActiveScene().buildIndex;

        int nextBuildIndex =
            currentBuildIndex + 1;

        if (
            nextBuildIndex >= 0 &&
            nextBuildIndex <
            SceneManager.sceneCountInBuildSettings
        )
        {
            Debug.LogWarning(
                "As cenas configuradas por nome não estão " +
                $"disponíveis. Tentando carregar a cena de " +
                $"índice {nextBuildIndex}.",
                this
            );

            StartCoroutine(
                LoadSceneRoutine(nextBuildIndex)
            );

            return;
        }

        Debug.LogError(
            "Não existe nenhuma próxima cena válida para carregar. " +
            "Configure Next Scene Name, Fallback Scene Name ou " +
            "adicione uma cena depois da abertura no Build Profile.",
            this
        );
    }

    private static bool TryGetValidSceneName(
        string sceneName,
        out string validSceneName
    )
    {
        validSceneName = null;

        if (string.IsNullOrWhiteSpace(sceneName))
            return false;

        sceneName = sceneName.Trim();

        if (!Application.CanStreamedLevelBeLoaded(sceneName))
            return false;

        validSceneName = sceneName;
        return true;
    }

    private IEnumerator LoadSceneRoutine(
        string sceneName
    )
    {
        isLeavingScene = true;

        AsyncOperation operation;

        try
        {
            operation =
                SceneManager.LoadSceneAsync(sceneName);
        }
        catch (Exception exception)
        {
            isLeavingScene = false;

            Debug.LogError(
                $"Exceção ao carregar a cena " +
                $"\"{sceneName}\".",
                this
            );

            Debug.LogException(exception, this);
            yield break;
        }

        if (operation == null)
        {
            isLeavingScene = false;

            Debug.LogError(
                $"LoadSceneAsync retornou null para " +
                $"a cena \"{sceneName}\".",
                this
            );

            yield break;
        }

        while (!operation.isDone)
            yield return null;
    }

    private IEnumerator LoadSceneRoutine(
        int buildIndex
    )
    {
        isLeavingScene = true;

        AsyncOperation operation;

        try
        {
            operation =
                SceneManager.LoadSceneAsync(buildIndex);
        }
        catch (Exception exception)
        {
            isLeavingScene = false;

            Debug.LogError(
                $"Exceção ao carregar a cena de " +
                $"índice {buildIndex}.",
                this
            );

            Debug.LogException(exception, this);
            yield break;
        }

        if (operation == null)
        {
            isLeavingScene = false;

            Debug.LogError(
                $"LoadSceneAsync retornou null para " +
                $"o índice {buildIndex}.",
                this
            );

            yield break;
        }

        while (!operation.isDone)
            yield return null;
    }

    private void ResetRuntimeState()
    {
        videoStarted = false;
        videoFinished = false;
        videoFailed = false;

        lastObservedFrame = -1;
        lastObservedVideoTime = 0;

        lastPlaybackProgressTime =
            Time.realtimeSinceStartup;
    }

    private void HandlePrepareCompleted(
        VideoPlayer source
    )
    {
        Debug.Log(
            "Vídeo de abertura preparado.",
            this
        );
    }

    private void HandleVideoStarted(
        VideoPlayer source
    )
    {
        videoStarted = true;

        lastObservedFrame = source.frame;
        lastObservedVideoTime = source.time;

        lastPlaybackProgressTime =
            Time.realtimeSinceStartup;

        Debug.Log(
            "Vídeo de abertura iniciado.",
            this
        );
    }

    private void HandleVideoFinished(
        VideoPlayer source
    )
    {
        videoFinished = true;

        Debug.Log(
            "Vídeo de abertura finalizado.",
            this
        );
    }

    private void HandleVideoError(
        VideoPlayer source,
        string errorMessage
    )
    {
        Debug.LogError(
            $"Erro no vídeo de abertura: " +
            $"{errorMessage}",
            this
        );

        videoFailed = true;
    }

    private static bool WasSkipInputPressed()
    {
        bool inputDetected = false;

        /*
         * Novo Input System.
         */
        if (
            Touchscreen.current != null &&
            Touchscreen.current
                .primaryTouch
                .press
                .wasPressedThisFrame
        )
        {
            inputDetected = true;
        }

        if (
            Mouse.current != null &&
            Mouse.current
                .leftButton
                .wasPressedThisFrame
        )
        {
            inputDetected = true;
        }

        if (
            Keyboard.current != null &&
            Keyboard.current
                .anyKey
                .wasPressedThisFrame
        )
        {
            inputDetected = true;
        }

        if (Gamepad.current != null)
        {
            foreach (
                UnityEngine.InputSystem.InputControl control
                in Gamepad.current.allControls
            )
            {
                if (
                    control is ButtonControl button &&
                    button.wasPressedThisFrame
                )
                {
                    inputDetected = true;
                    break;
                }
            }
        }

        return inputDetected;
    }

    /*
     * Trava o FPS da aplicação na taxa do vídeo.
     *
     * Em telas de alta taxa de atualização a aplicação roda
     * muito acima do vídeo, e o descompasso entre os dois
     * relógios atropela a apresentação dos frames.
     *
     * Só pode ser chamado depois de isPrepared: antes disso
     * videoPlayer.frameRate ainda não tem valor válido.
     */
    /*
     * Aplica a trava de FPS, guardando os valores originais
     * apenas na primeira chamada.
     *
     * Pode ser chamado duas vezes: uma no Awake, com um valor
     * provisório, e outra depois da preparação, com a taxa
     * real lida do vídeo.
     */
    /*
     * Segura o Play até a cena estar de fato pronta.
     *
     * Três condições, nesta ordem:
     *
     * 1. A cena ativa terminou de carregar;
     * 2. Passaram N frames completos, contados no fim do
     *    frame para incluir renderização e não só o Update;
     * 3. Decorreu o tempo mínimo desde o início da cena.
     */
    private void ConfigureCamera()
    {
        if (displayCamera == null)
            displayCamera = Camera.main;

        if (displayCamera == null)
            return;

        /*
         * Fundo preto sólido: enquanto o vídeo prepara, a
         * câmera não pode mostrar skybox nem lixo do buffer.
         */
        displayCamera.clearFlags = CameraClearFlags.SolidColor;
        displayCamera.backgroundColor = Color.black;
    }

    private IEnumerator WaitForSceneToStabilize()
    {
        while (
            !isLeavingScene &&
            !SceneManager.GetActiveScene().isLoaded
        )
        {
            yield return null;
        }

        int frames = Mathf.Max(0, framesToStabilize);

        for (int index = 0; index < frames; index++)
        {
            if (isLeavingScene)
                yield break;

            yield return new WaitForEndOfFrame();
        }

        /*
         * Tempo real: a trava de FPS já mexeu no ritmo do
         * jogo, e Time.time não serve de referência aqui.
         */
        while (!isLeavingScene)
        {
            float elapsed =
                Time.realtimeSinceStartup - sceneStartedAt;

            if (elapsed >= minimumTimeBeforePlay)
                break;

            yield return null;
        }
    }

    private void ApplyFrameRateLock(int framesPerSecond)
    {
        if (!frameRateLocked)
        {
            /*
             * Só o vSync é guardado. O targetFrameRate não
             * precisa ser lembrado: ao final ele volta para -1
             * e quem decide passa a ser o sistema.
             */
            vSyncBeforeLock = QualitySettings.vSyncCount;
            frameRateLocked = true;
        }

        /*
         * Com vSync ligado o targetFrameRate é ignorado,
         * então ele precisa sair do caminho.
         */
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = Mathf.Max(1, framesPerSecond);
    }

    /*
     * Trava provisória, aplicada antes de o vídeo existir.
     *
     * A taxa real só é conhecida depois da preparação, mas
     * esperar até lá deixaria o VideoPlayer inicializar na
     * taxa do monitor — que é justamente o que trava a
     * reprodução. Este é o passo que faltava na tentativa
     * anterior.
     */
    private void ApplyProvisionalFrameRateLock()
    {
        if (!matchVideoFrameRate)
            return;

        ApplyFrameRateLock(provisionalOpeningFrameRate);
    }

    private void LockFrameRateToVideo()
    {
        if (!matchVideoFrameRate)
            return;

        if (videoPlayer == null || !videoPlayer.isPrepared)
            return;

        double videoFrameRate = videoPlayer.frameRate;

        if (
            videoFrameRate <= 0 ||
            double.IsNaN(videoFrameRate) ||
            double.IsInfinity(videoFrameRate)
        )
        {
            Debug.LogWarning(
                "O vídeo não informou uma taxa de quadros " +
                "válida. A trava provisória do Awake " +
                $"({provisionalOpeningFrameRate} FPS) continua " +
                "valendo.",
                this
            );

            return;
        }

        ApplyFrameRateLock(
            Mathf.RoundToInt((float)videoFrameRate)
        );

        Debug.Log(
            $"FPS travado em {Application.targetFrameRate} " +
            $"para acompanhar o vídeo " +
            $"({videoFrameRate:0.##} fps).",
            this
        );
    }

    private void RestoreFrameRate()
    {
        if (!frameRateLocked)
            return;

        frameRateLocked = false;

        /*
         * -1 devolve a decisão ao sistema: a plataforma passa a
         * escolher o ritmo, como se ninguém tivesse mexido.
         *
         * Não restaura o valor capturado no Awake de propósito.
         * Aquele valor pode ser lixo de uma execução anterior:
         * basta sair do Play no meio do vídeo para o
         * targetFrameRate ficar em 24, e aí a execução seguinte
         * capturaria 24 como "original" e o travamento viraria
         * permanente.
         */
        Application.targetFrameRate = -1;
        QualitySettings.vSyncCount = vSyncBeforeLock;

        /*
         * No Android a taxa da tela é reaplicada, que é o
         * comportamento pedido por matchScreenRefreshRate.
         * Fora do Android este método não faz nada.
         */
        if (matchScreenRefreshRate)
            ConfigureTargetFrameRate();

        Debug.Log(
            "FPS restaurado para " +
            $"{Application.targetFrameRate} " +
            $"(vSync {QualitySettings.vSyncCount}).",
            this
        );
    }

    private static void ConfigureTargetFrameRate()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            double refreshRate =
                Screen.currentResolution
                    .refreshRateRatio
                    .value;

            if (
                refreshRate > 0 &&
                !double.IsNaN(refreshRate) &&
                !double.IsInfinity(refreshRate)
            )
            {
                Application.targetFrameRate =
                    Mathf.Max(
                        30,
                        Mathf.RoundToInt(
                            (float)refreshRate
                        )
                    );

                Debug.Log(
                    $"Android configurado para " +
                    $"{Application.targetFrameRate} FPS. " +
                    $"Tela detectada: {refreshRate:0.##} Hz."
                );

                return;
            }

            Debug.LogWarning(
                "A taxa de atualização informada pelo " +
                "dispositivo é inválida. Usando 60 FPS."
            );
        }
        catch (Exception exception)
        {
            Debug.LogWarning(
                "Não foi possível detectar a taxa de " +
                $"atualização da tela: {exception.Message}"
            );
        }

        /*
         * Fallback seguro para Android.
         */
        Application.targetFrameRate = 60;
#endif
    }

    private void OnDestroy()
    {
        /*
         * Rede de segurança: se a cena for destruída por um
         * caminho que não passou por FinishOpening, o jogo
         * não pode herdar o FPS travado do vídeo.
         */
        RestoreFrameRate();

        if (videoPlayer == null)
            return;

        videoPlayer.prepareCompleted -=
            HandlePrepareCompleted;

        videoPlayer.started -=
            HandleVideoStarted;

        videoPlayer.loopPointReached -=
            HandleVideoFinished;

        videoPlayer.errorReceived -=
            HandleVideoError;
    }

#if UNITY_EDITOR
    [ContextMenu("Apagar registro da abertura")]
    private void ResetOpeningPreference()
    {
        PlayerPrefs.DeleteKey(OpeningWatchedKey);
        PlayerPrefs.Save();

        Debug.Log(
            "Registro da abertura apagado. " +
            "A próxima execução será considerada " +
            "a primeira.",
            this
        );
    }
#endif
}