using UnityEngine;
using UnityEngine.EventSystems;

namespace Lumera.JumpForce
{
    // Joystick_Pular: joystick fixo do pulo. Tocar na imagem pressiona o pulo e o Button_Pular (filho) segue o dedo,
    // limitado ao raio. O angulo do botao e a direcao do salto; a distancia ao centro e o teto da carga (no meio do
    // caminho, carrega so ate 50%). Soltar salta; soltar na zona morta cancela. Solto, o botao volta ao centro.
    // Setas, direcional e analogico tambem movem o botao (com o pulo fisico pressionado), e o botao mostra isso.
    // Funciona junto com os botoes de direcao, cada um com seu dedo.
    [DisallowMultipleComponent]
    public sealed class JumpForceJoystickPulo : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        public JumpForcePlayer player;
        [Tooltip("Button_Pular: o botao que se move dentro da imagem. A posicao dele na cena e o centro do joystick.")]
        [SerializeField] RectTransform botao;
        [Tooltip("Metade de baixo espelhada para cima: 45 graus para baixo-esquerda vale como 45 graus para cima-esquerda. " +
                 "Desligue com uma textura de meia lua: o botao nao desce abaixo do centro.")]
        [SerializeField] bool espelharMetadeDeBaixo = true;
        [Tooltip("Raio do movimento do botao, em unidades da UI. 0 = automatico: metade do menor lado da imagem, menos metade do botao.")]
        [SerializeField, Min(0)] float raio;

        RectTransform area;
        Vector3 centro;
        int dedo = int.MinValue;

        JumpForceInput Input => player ? player.input : null;
        float Raio => raio > 0 ? raio
            : Mathf.Max(1, Mathf.Min(area.rect.width, area.rect.height) * 0.5f - (botao ? Mathf.Min(botao.rect.width, botao.rect.height) * 0.5f : 0));

        void Awake()
        {
            area = (RectTransform)transform;
            if (botao) centro = botao.localPosition;
            if (Input != null) Input.PadMirror = espelharMetadeDeBaixo;
        }

        void OnDisable()
        {
            // Sumir no meio da carga (vestiario, morte) cancela: o botao volta ao centro, na zona morta.
            if (dedo != int.MinValue)
            {
                Input?.SetAimPad(Vector2.zero, false);
                Input?.SetJumpSource(this, false);
            }
            dedo = int.MinValue;
            if (botao) botao.localPosition = centro;
        }

        public void OnPointerDown(PointerEventData dados)
        {
            if (dedo != int.MinValue) return;
            dedo = dados.pointerId;
            Input?.SetJumpSource(this, true);
            Mover(dados, true);
        }

        public void OnDrag(PointerEventData dados)
        {
            if (dados.pointerId == dedo) Mover(dados, true);
        }

        public void OnPointerUp(PointerEventData dados)
        {
            if (dados.pointerId != dedo) return;
            Mover(dados, false);
            Input?.SetJumpSource(this, false);
            dedo = int.MinValue;
        }

        void Mover(PointerEventData dados, bool segurando)
        {
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(area, dados.position, dados.pressEventCamera, out var local)) return;
            Input?.SetAimPad((local - (Vector2)centro) / Raio, segurando);
        }

        // O botao mostra a posicao do input, venha do toque, das setas ou do analogico.
        void LateUpdate()
        {
            var input = Input;
            if (input == null || !botao) return;
            input.PadMirror = espelharMetadeDeBaixo;
            botao.localPosition = centro + (Vector3)(input.AimPad * Raio);
        }
    }
}
