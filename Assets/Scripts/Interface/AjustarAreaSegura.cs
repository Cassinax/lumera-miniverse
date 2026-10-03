using UnityEngine;
using UnityEngine.UI;

// Safe_Area: a altura do Espacador acompanha o recorte do topo da tela (notch, camera, barra de status),
// empurrando o Painel_Topo para baixo so o necessario. Reage a rotacao e mudanca de resolucao.
[DisallowMultipleComponent]
public sealed class AjustarAreaSegura : MonoBehaviour
{
    [Tooltip("Objeto cuja altura vira o recorte do topo (Espacador).")]
    [SerializeField] RectTransform espacador;
    [Tooltip("Altura extra, em unidades do Canvas, alem do recorte.")]
    [SerializeField, Min(0)] float margemExtra;

    Canvas canvas;
    LayoutElement layout;
    Rect ultimaArea;
    Vector2Int ultimaTela;

    void Awake()
    {
        canvas = GetComponentInParent<Canvas>();
        if (espacador) layout = espacador.GetComponent<LayoutElement>();
    }

    void OnEnable() => Aplicar();

    void Update()
    {
        if (Screen.safeArea != ultimaArea || Screen.width != ultimaTela.x || Screen.height != ultimaTela.y) Aplicar();
    }

    void Aplicar()
    {
        ultimaArea = Screen.safeArea;
        ultimaTela = new Vector2Int(Screen.width, Screen.height);
        if (!espacador) return;
        float escala = canvas ? canvas.rootCanvas.scaleFactor : 1;
        float recorte = Mathf.Max(0, Screen.height - ultimaArea.yMax) / Mathf.Max(0.0001f, escala);
        float altura = recorte + margemExtra;
        // O layout le a altura preferida; o RectTransform, a altura real. As duas precisam concordar.
        espacador.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, altura);
        if (layout) layout.preferredHeight = altura;
        LayoutRebuilder.MarkLayoutForRebuild(transform as RectTransform);
    }
}
