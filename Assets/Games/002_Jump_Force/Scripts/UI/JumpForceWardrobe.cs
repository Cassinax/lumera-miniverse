using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace Lumera.JumpForce
{
    [DefaultExecutionOrder(-200), DisallowMultipleComponent]
    public sealed class JumpForceWardrobe : MonoBehaviour
    {
        [Header("Cena")]
        public JumpForceHUD hud;
        public GameObject wardrobePanel;
        public GameObject returnMenuPanel;
        public ScrollRect scroll;
        public Toggle toggleTemplate;
        public ToggleGroup toggleGroup;
        public Renderer characterRenderer;
        public JumpForcePaletteCatalog palettes;
        [Tooltip("Guarda a paleta escolhida no save do jogo. Vazio: procura na cena.")]
        public JumpForcePlataforma plataforma;
        [Header("Loja de cores")]
        public Button buyButton;
        public Button playButton;
        [Min(0)] public int freePaletteCount = 3;
        [Min(0)] public int defaultPrice = 15;
        [Header("Abertura")]
        [Tooltip("Volta ao vestiario a cada reinicio. A pose da camera aqui e a ancora Vestiario do Ancorador_Camera.")]
        public bool reopenOnRetry = true;
        public bool IsOpen { get; private set; }
        public int SelectedPalette { get; private set; }
        public IReadOnlyList<Toggle> Toggles => toggles;

        readonly List<Toggle> toggles = new();
        readonly List<TMPro.TMP_Text> prices = new();
        readonly List<RaycastResult> hits = new();
        MaterialPropertyBlock properties;
        static readonly int BaseMapST = Shader.PropertyToID("_BaseMap_ST");
        InputSystemUIInputModule uiModule;
        InputActionReference originalSubmit, wardrobeSubmitReference;
        InputAction wardrobeSubmit;
        InputActionAsset wardrobeActions;
        JumpForcePlatformVisibility visibility;
        bool restoreVisibility;
        int openedFrame;
        string paletaGravada;

        void Start()
        {
            if (!hud || !hud.player || !hud.followCamera || !wardrobePanel ||
                !toggleTemplate || !toggleGroup || !characterRenderer || !palettes || palettes.options.Length == 0)
            {
                Debug.LogError("Vestiario: faltam referencias no Inspector.", this);
                enabled = false;
                return;
            }
            if (!plataforma) plataforma = FindAnyObjectByType<JumpForcePlataforma>();
            if (buyButton) buyButton.onClick.AddListener(BuySelectedPalette);
            if (playButton) playButton.onClick.AddListener(StartGame);
            BuildOptions();
            visibility = hud.followCamera.GetComponent<JumpForcePlatformVisibility>();
            Begin();
        }

        void BuildOptions()
        {
            toggleTemplate.gameObject.SetActive(false);
            toggleGroup.allowSwitchOff = false;
            for (int i = 0; i < palettes.options.Length; i++)
            {
                int index = i;
                var toggle = Instantiate(toggleTemplate, toggleTemplate.transform.parent);
                toggle.name = "Paleta_" + (i + 1) + "_" + palettes.options[i].name.Replace("/", "_");
                toggle.group = toggleGroup;
                toggle.SetIsOnWithoutNotify(false);
                var background = toggle.transform.Find("Background")?.GetComponent<Image>();
                if (background) background.color = palettes.options[i].swatch;
                toggle.onValueChanged.AddListener(on => { if (on) SelectPalette(index); });
                toggle.gameObject.SetActive(true);
                toggles.Add(toggle);
                prices.Add(toggle.transform.Find("Text_Valor")?.GetComponent<TMPro.TMP_Text>());
            }
            for (int i = 0; i < toggles.Count; i++)
            {
                // Keep navigation inside the palette list, away from hidden game controls.
                var navigation = new Navigation { mode = Navigation.Mode.Explicit };
                navigation.selectOnLeft = toggles[(i + toggles.Count - 1) % toggles.Count];
                navigation.selectOnRight = toggles[(i + 1) % toggles.Count];
                toggles[i].navigation = navigation;
            }
            // A ultima paleta escolhida (save do jogo); sem save ou paleta removida, a padrao.
            paletaGravada = plataforma ? plataforma.PaletaSalva : "";
            int salva = System.Array.FindIndex(palettes.options, o => !string.IsNullOrEmpty(paletaGravada) && o.name == paletaGravada);
            int padrao = System.Array.FindIndex(palettes.options, o => o.name == "Lumera/Aqua");
            if (padrao < 0) padrao = Mathf.Clamp(palettes.defaultIndex, 0, toggles.Count - 1);
            SelectedPalette = salva >= 0 && IsPaletteUnlocked(salva) ? salva : padrao;
            SelectPalette(SelectedPalette);
        }

        public void SelectPalette(int index)
        {
            if (!palettes || index < 0 || index >= palettes.options.Length || !characterRenderer) return;
            SelectedPalette = index;
            // Per-renderer override: one atlas and one shared material, including after leaving Play Mode.
            properties ??= new MaterialPropertyBlock();
            characterRenderer.GetPropertyBlock(properties);
            properties.SetVector(BaseMapST, new Vector4(1, 1f / palettes.options.Length, 0, index / (float)palettes.options.Length));
            characterRenderer.SetPropertyBlock(properties);
            // A seta da mira do salto usa a cor da camisa.
            if (hud && hud.player) hud.player.DefinirCorSeta(palettes.options[index].swatch);
            for (int i = 0; i < toggles.Count; i++) toggles[i].SetIsOnWithoutNotify(i == index);
            RefreshShop();
        }

        public bool IsPaletteUnlocked(int index) => palettes && index >= 0 && index < palettes.options.Length &&
            (index < Mathf.Max(0, freePaletteCount) ||
                (plataforma && plataforma.PaletaComprada(palettes.options[index].name)));

        public void BuySelectedPalette()
        {
            if (!IsOpen || (returnMenuPanel && returnMenuPanel.activeInHierarchy) ||
                IsPaletteUnlocked(SelectedPalette) || !plataforma) return;
            if (!plataforma.ComprarPaleta(palettes.options[SelectedPalette].name, defaultPrice))
            {
                RefreshShop();
                return;
            }
            RefreshShop();
            if (EventSystem.current && buyButton &&
                EventSystem.current.currentSelectedGameObject == buyButton.gameObject)
                EventSystem.current.SetSelectedGameObject(toggles[SelectedPalette].gameObject);
        }

        void RefreshShop()
        {
            for (int i = 0; i < prices.Count; i++)
            {
                if (!prices[i]) continue;
                bool bloqueada = !IsPaletteUnlocked(i);
                prices[i].gameObject.SetActive(bloqueada);
                if (bloqueada) prices[i].text = Mathf.Max(0, defaultPrice).ToString();
            }
            bool liberada = IsPaletteUnlocked(SelectedPalette);
            if (buyButton)
            {
                buyButton.gameObject.SetActive(!liberada);
                buyButton.interactable = plataforma && plataforma.CarteiraDisponivel &&
                    plataforma.SaldoMoedas >= Mathf.Max(0, defaultPrice);
            }
            if (playButton) playButton.interactable = liberada;
            // Navegacao horizontal acompanha a nova faixa de cards; cima leva aos botoes.
            if (toggles.Count == 0) return;
            Selectable acima = !liberada && buyButton && buyButton.interactable
                ? buyButton : playButton && playButton.interactable ? playButton : null;
            foreach (var toggle in toggles)
            {
                var navigation = toggle.navigation;
                navigation.selectOnUp = acima;
                toggle.navigation = navigation;
            }
            if (playButton)
            {
                var navigation = new Navigation { mode = Navigation.Mode.Explicit };
                navigation.selectOnDown = acima == buyButton ? buyButton : toggles[SelectedPalette];
                playButton.navigation = navigation;
            }
            if (buyButton)
            {
                var navigation = new Navigation { mode = Navigation.Mode.Explicit };
                navigation.selectOnUp = playButton;
                navigation.selectOnDown = toggles[SelectedPalette];
                buyButton.navigation = navigation;
            }
        }

        public void Begin()
        {
            if (toggles.Count == 0) return;
            IsOpen = true;
            openedFrame = Time.frameCount;
            wardrobePanel.SetActive(true);
            hud.MostrarGameplay(false);
            RefreshShop();
            hud.player.SetGameplayEnabled(false);
            hud.player.input.SetGameplayEnabled(false);
            hud.player.animationDriver.Preview = true;
            hud.player.animationDriver.ResetIntro();
            hud.followCamera.HoldForWardrobe();
            if (visibility && visibility.enabled) { restoreVisibility = true; visibility.enabled = false; }
            hud.MostrarControles(false);
            if (hud.deathPanel) hud.deathPanel.SetActive(false);
            ConfigureSubmit();
            if (EventSystem.current) EventSystem.current.SetSelectedGameObject(toggles[SelectedPalette].gameObject);
            KeepFocusedToggleVisible();
        }

        public void StartGame()
        {
            if (!IsOpen || !IsPaletteUnlocked(SelectedPalette) ||
                (returnMenuPanel && returnMenuPanel.activeInHierarchy)) return;
            IsOpen = false;
            if (EventSystem.current) EventSystem.current.SetSelectedGameObject(null);
            RestoreSubmit();
            wardrobePanel.SetActive(false);
            hud.MostrarGameplay(true);
            hud.player.animationDriver.Preview = false;
            hud.player.input.SetGameplayEnabled(true);
            hud.player.SetGameplayEnabled(true);
            hud.followCamera.ReleaseFromWardrobe();
            if (visibility) visibility.enabled = restoreVisibility;
            hud.MostrarControles(true);
            GravarPaleta();
        }

        // Grava so quando muda: ao comecar a partida, ao sair da cena e ao ir para o fundo.
        void GravarPaleta()
        {
            // Ao sair da cena a Plataforma pode ja ter sido destruida: SalvarPaleta so usa o id e o save do
            // Objeto Mestre, entao vale a referencia C# (nao a checagem de objeto vivo da Unity).
            if (ReferenceEquals(plataforma, null) || !IsPaletteUnlocked(SelectedPalette)) return;
            string nome = palettes.options[SelectedPalette].name;
            if (nome == paletaGravada) return;
            plataforma.SalvarPaleta(nome);
            paletaGravada = nome;
        }
        void OnApplicationPause(bool pausado) { if (pausado) GravarPaleta(); }

        void Update()
        {
            if (!IsOpen || Time.frameCount == openedFrame) return;
            if (returnMenuPanel && returnMenuPanel.activeInHierarchy) return;
            var keyboard = Keyboard.current;
            var pad = Gamepad.current;
            if ((keyboard != null && (keyboard.spaceKey.wasPressedThisFrame || keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame)) ||
                (pad != null && pad.buttonSouth.wasPressedThisFrame))
            {
                bool teclado = keyboard != null && (keyboard.spaceKey.wasPressedThisFrame ||
                    keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame);
                if (teclado && buyButton && EventSystem.current &&
                    EventSystem.current.currentSelectedGameObject == buyButton.gameObject)
                    BuySelectedPalette();
                else StartGame();
                return;
            }
            if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame &&
                CanStartAt(Mouse.current.position.ReadValue()))
            {
                StartGame();
                return;
            }
            if (Touchscreen.current != null)
                foreach (var touch in Touchscreen.current.touches)
                    if (touch.press.wasPressedThisFrame && CanStartAt(touch.position.ReadValue()))
                    {
                        StartGame();
                        return;
                    }
        }

        public bool CanStartAt(Vector2 position)
        {
            if (!IsOpen || (returnMenuPanel && returnMenuPanel.activeInHierarchy)) return false;
            var canvas = wardrobePanel.GetComponentInParent<Canvas>();
            Camera uiCamera = canvas && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            if (scroll && RectTransformUtility.RectangleContainsScreenPoint((RectTransform)scroll.transform, position, uiCamera)) return false;
            if (!EventSystem.current) return true;
            hits.Clear();
            EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = position }, hits);
            foreach (var hit in hits)
                if (hit.gameObject.GetComponentInParent<Selectable>() ||
                    (scroll && hit.gameObject.transform.IsChildOf(scroll.transform))) return false;
            return true;
        }

        void LateUpdate()
        {
            if (IsOpen) KeepFocusedToggleVisible();
        }

        GameObject lastFocused;
        void KeepFocusedToggleVisible()
        {
            if (!scroll || !scroll.viewport || !scroll.content || !EventSystem.current) return;
            var focused = EventSystem.current.currentSelectedGameObject;
            if (!focused || focused == lastFocused || !toggles.Contains(focused.GetComponent<Toggle>())) return;
            lastFocused = focused;
            Canvas.ForceUpdateCanvases();
            var bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(scroll.viewport, focused.transform);
            var rect = scroll.viewport.rect;
            float shiftX = scroll.horizontal ? bounds.min.x < rect.xMin ? rect.xMin - bounds.min.x :
                bounds.max.x > rect.xMax ? rect.xMax - bounds.max.x : 0 : 0;
            float shiftY = scroll.vertical ? bounds.min.y < rect.yMin ? rect.yMin - bounds.min.y :
                bounds.max.y > rect.yMax ? rect.yMax - bounds.max.y : 0 : 0;
            scroll.content.anchoredPosition += new Vector2(shiftX, shiftY);
        }

        void ConfigureSubmit()
        {
            if (!EventSystem.current) return;
            uiModule = EventSystem.current.GetComponent<InputSystemUIInputModule>();
            if (!uiModule || wardrobeSubmit != null) return;
            originalSubmit = uiModule.submit;
            wardrobeActions = ScriptableObject.CreateInstance<InputActionAsset>();
            wardrobeSubmit = wardrobeActions.AddActionMap("Vestiario").AddAction("ConfirmarPaleta", InputActionType.Button);
            wardrobeSubmit.AddBinding("<Gamepad>/buttonWest");

            wardrobeSubmitReference = InputActionReference.Create(wardrobeSubmit);
            uiModule.submit = wardrobeSubmitReference;
        }

        void RestoreSubmit()
        {
            if (wardrobeSubmit == null) return;
            if (uiModule) uiModule.submit = originalSubmit;
            wardrobeActions.Disable();
            Destroy(wardrobeActions);
            Destroy(wardrobeSubmitReference);
            wardrobeSubmit = null;
            wardrobeSubmitReference = null;
        }
        void OnDestroy()
        {
            RestoreSubmit();
            GravarPaleta();
            if (buyButton) buyButton.onClick.RemoveListener(BuySelectedPalette);
            if (playButton) playButton.onClick.RemoveListener(StartGame);
        }
    }
}