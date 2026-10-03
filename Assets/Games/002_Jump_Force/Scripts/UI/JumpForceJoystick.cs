using UnityEngine;
using UnityEngine.EventSystems;

namespace Lumera.JumpForce
{
    // Joystick_Virtual: area onde o joystick pode surgir. Ao tocar, a Joystick_Area nasce sob o dedo (pode ficar
    // parcialmente fora da tela) e o Joystick_Controle acompanha o dedo, limitado ao raio da area. Ao soltar, volta
    // ao centro e fica transparente. O valor vai para o JumpForceInput (movimento e mira do salto).
    [DisallowMultipleComponent]
    public sealed class JumpForceJoystick : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        public JumpForcePlayer player;
        [SerializeField] RectTransform area;
        [SerializeField] RectTransform controle;
        [Tooltip("Abaixo desta fracao do raio o joystick vale zero.")]
        [SerializeField, Range(0, 0.9f)] float zonaMorta = 0.1f;
        [Tooltip("Opacidade do joystick solto.")]
        [SerializeField, Range(0, 1)] float alphaSolto = 0.3f;
        [Tooltip("Opacidade do joystick em uso.")]
        [SerializeField, Range(0, 1)] float alphaPressionado = 1;

        RectTransform regiao;
        CanvasGroup grupo;
        Vector2 centroInicial;
        int dedo = int.MinValue;

        JumpForceInput Input => player ? player.input : null;

        void Awake()
        {
            regiao = (RectTransform)transform;
            if (area)
            {
                centroInicial = area.anchoredPosition;
                grupo = area.GetComponent<CanvasGroup>();
                if (!grupo) grupo = area.gameObject.AddComponent<CanvasGroup>();
                // A area so mostra o joystick; quem recebe o toque e a regiao toda.
                grupo.blocksRaycasts = false;
            }
            Soltar();
        }

        void OnDisable() => Soltar();

        public void OnPointerDown(PointerEventData dados)
        {
            if (dedo != int.MinValue || !area) return;
            dedo = dados.pointerId;
            // A area nasce onde o dedo tocou, mesmo perto da borda.
            if (RectTransformUtility.ScreenPointToWorldPointInRectangle(regiao, dados.position, dados.pressEventCamera, out var ponto))
                area.position = ponto;
            if (grupo) grupo.alpha = alphaPressionado;
            Atualizar(dados);
        }

        public void OnDrag(PointerEventData dados)
        {
            if (dados.pointerId == dedo) Atualizar(dados);
        }

        public void OnPointerUp(PointerEventData dados)
        {
            if (dados.pointerId == dedo) Soltar();
        }

        void Atualizar(PointerEventData dados)
        {
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(area, dados.position, dados.pressEventCamera, out var local)) return;
            float raio = Mathf.Max(1, Mathf.Min(area.rect.width, area.rect.height) * 0.5f);
            var deslocamento = Vector2.ClampMagnitude(local - area.rect.center, raio);
            if (controle) controle.anchoredPosition = deslocamento;
            var valor = deslocamento / raio;
            if (valor.magnitude < zonaMorta) valor = Vector2.zero;
            Input?.SetJoystick(valor, true);
        }

        void Soltar()
        {
            dedo = int.MinValue;
            if (area) area.anchoredPosition = centroInicial;
            if (controle) controle.anchoredPosition = Vector2.zero;
            if (grupo) grupo.alpha = alphaSolto;
            Input?.SetJoystick(Vector2.zero, false);
        }
    }
}
