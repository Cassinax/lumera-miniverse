using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace Lumera.JumpForce
{
    [DefaultExecutionOrder(-150), DisallowMultipleComponent]
    public sealed class JumpForceInput : MonoBehaviour
    {
        [Tooltip("Abaixo disto o analogico do controle e ignorado no movimento.")]
        [Range(0, 0.9f)] public float stickDeadZone = 0.2f;
        [Header("Joystick do pulo")]
        [Tooltip("Fracao do raio, a partir do centro, que nao gera pulo. Soltar o pulo nela cancela o salto.")]
        [Range(0, 0.9f)] public float padDeadZone = 0.2f;
        [Tooltip("Velocidade do botao com setas e direcional, em raios por segundo.")]
        [Min(0.1f)] public float padArrowSpeed = 1.5f;
        public bool JumpPressed { get; private set; }
        public bool JumpReleased { get; private set; }
        public bool JumpHeld { get; private set; }
        public bool AnyPressed { get; private set; }
        public Vector2 Movement { get; private set; }
        public float Direction => Movement.x;
        // Botoes de direcao da tela: prioridade sobre teclado e analogico.
        public bool DirectionButtonsHeld => directionSources.Count > 0;

        // Joystick do pulo: posicao do botao, de -1 a 1 em cada eixo (comprimento 1 = borda). O comprimento
        // limita a carga do salto e o angulo da a direcao. Toque: o dedo move o botao. Setas, direcional e
        // analogico: so com o pulo fisico pressionado (assim nao disputam com o movimento do personagem).
        // Solto, volta ao centro um quadro depois de soltar o pulo (para o salto ler a posicao final).
        public Vector2 AimPad { get; private set; }
        // Metade de baixo espelhada para cima (fundo redondo). Desligado (meia lua): o botao nao desce do centro.
        public bool PadMirror { get; set; } = true;
        public float AimPadStrength => AimPad.magnitude;
        public bool AimPadInDeadZone => AimPad.magnitude < padDeadZone;
        // Direcao do salto: y sempre para cima (espelhado ou limitado).
        public Vector2 AimPadDirection => new Vector2(AimPad.x, Mathf.Abs(AimPad.y));

        readonly HashSet<object> jumpSources = new();
        readonly Dictionary<object, int> directionSources = new();
        bool previousHeld, uiJumpPulse;
        Vector2 padToque;
        bool padToqueHeld, padToqueSoltou, padAnalogico;
        bool gameplayEnabled = true, waitingForRelease;
        public void SetGameplayEnabled(bool value)
        {
            gameplayEnabled = value;
            waitingForRelease = value;
            Clear();
        }
        bool ControlsHeld()
        {
            if (Keyboard.current != null && Keyboard.current.anyKey.isPressed) return true;
            if (Mouse.current != null && Mouse.current.leftButton.isPressed) return true;
            if (Gamepad.current != null)
            {
                var pad = Gamepad.current;
                if (pad.buttonSouth.isPressed || pad.buttonWest.isPressed ||
                    pad.dpad.ReadValue().sqrMagnitude > 0.01f || pad.leftStick.ReadValue().sqrMagnitude > 0.04f) return true;
            }
            if (Touchscreen.current != null)
                foreach (var touch in Touchscreen.current.touches) if (touch.press.isPressed) return true;
            return false;
        }

        // Chamado pelo JumpForceBotaoDirecao. lado: -1 esquerda, 1 direita da tela. Os dois juntos se anulam.
        public void SetDirectionSource(object id, int lado, bool held)
        {
            if (held && (!gameplayEnabled || waitingForRelease)) return;
            if (held && lado != 0) directionSources[id] = lado > 0 ? 1 : -1;
            else directionSources.Remove(id);
        }
        int DirectionButtons()
        {
            int soma = 0;
            foreach (var lado in directionSources.Values) soma += lado;
            return System.Math.Sign(soma);
        }
        public void SetJumpSource(object id, bool held)
        {
            if (!gameplayEnabled || waitingForRelease) return;
            if (held) { if (jumpSources.Count == 0) uiJumpPulse = true; jumpSources.Add(id); }
            else jumpSources.Remove(id);
        }
        // Chamado pelo JumpForceJoystickPulo. valor: posicao do botao (-1 a 1). Ao soltar, guarda a posicao final.
        public void SetAimPad(Vector2 valor, bool held)
        {
            if (held && (!gameplayEnabled || waitingForRelease)) return;
            padToque = LimitarPad(valor);
            if (padToqueHeld && !held) padToqueSoltou = true;
            padToqueHeld = held;
        }
        Vector2 LimitarPad(Vector2 valor)
        {
            valor = Vector2.ClampMagnitude(valor, 1);
            if (!PadMirror) valor.y = Mathf.Max(0, valor.y);
            return valor;
        }
        void Update()
        {
            if (!gameplayEnabled) { Clear(); return; }
            if (waitingForRelease)
            {
                Clear();
                if (!ControlsHeld()) { waitingForRelease = false; }
                return;
            }
            bool held = jumpSources.Count > 0, pressed = uiJumpPulse, fisico = false;
            uiJumpPulse = false;
            AnyPressed = pressed;
            float digital = 0, stick = 0;
            Vector2 setas = Vector2.zero, analogico = Vector2.zero;
            var keyboard = Keyboard.current;
            if (keyboard != null)
            {
                fisico |= keyboard.spaceKey.isPressed;
                pressed |= keyboard.spaceKey.wasPressedThisFrame;
                AnyPressed |= keyboard.anyKey.wasPressedThisFrame;
                // Movement is horizontal only; up/down arrows only move the jump pad.
                digital = (keyboard.rightArrowKey.isPressed ? 1 : 0) - (keyboard.leftArrowKey.isPressed ? 1 : 0);
                setas = new Vector2(digital, (keyboard.upArrowKey.isPressed ? 1 : 0) - (keyboard.downArrowKey.isPressed ? 1 : 0));
            }
            var pad = Gamepad.current;
            if (pad != null)
            {
                fisico |= pad.buttonSouth.isPressed || pad.buttonWest.isPressed;
                pressed |= pad.buttonSouth.wasPressedThisFrame || pad.buttonWest.wasPressedThisFrame;
                var dpad = pad.dpad.ReadValue();
                if (Mathf.Abs(dpad.x) > 0.1f) digital = Mathf.Sign(dpad.x);
                if (dpad.sqrMagnitude > 0.01f) setas = new Vector2(Mathf.Round(dpad.x), Mathf.Round(dpad.y));
                analogico = pad.leftStick.ReadValue();
                if (Mathf.Abs(analogico.x) > stickDeadZone) stick = analogico.x;
                foreach (var control in pad.allControls)
                    if (control is ButtonControl button && button.wasPressedThisFrame) AnyPressed = true;
            }
            held |= fisico;
            if (Touchscreen.current != null)
                foreach (var touch in Touchscreen.current.touches)
                    if (touch.press.wasPressedThisFrame) AnyPressed = true;
            if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame) AnyPressed = true;
            // Botoes da tela valem como as setas e passam na frente delas.
            int botoes = DirectionButtons();
            if (botoes != 0) digital = botoes;
            // Prioridade: botoes da tela, setas/direcional, analogico.
            float move = digital != 0 ? digital : stick;
            Movement = new Vector2(Mathf.Clamp(move, -1, 1), 0);
            JumpPressed = !previousHeld && (held || pressed);
            JumpReleased = (previousHeld || pressed) && !held;
            JumpHeld = held;
            previousHeld = held;
            AtualizarPad(fisico, setas, analogico);
        }
        void AtualizarPad(bool fisico, Vector2 setas, Vector2 analogico)
        {
            if (padToqueHeld || padToqueSoltou)
            {
                AimPad = padToque;
                padToqueSoltou = false;
                padAnalogico = false;
            }
            else if (fisico)
            {
                // Setas avancam e voltam o botao; o analogico o posiciona direto (e volta ao centro com ele).
                if (setas != Vector2.zero)
                {
                    padAnalogico = false;
                    AimPad = LimitarPad(AimPad + setas.normalized * padArrowSpeed * Time.deltaTime);
                }
                else if (analogico.magnitude > stickDeadZone) padAnalogico = true;
                if (padAnalogico) AimPad = LimitarPad(analogico);
            }
            // Pulo solto: o salto le a posicao neste quadro; no seguinte o botao volta ao centro.
            else if (!JumpReleased)
            {
                AimPad = Vector2.zero;
                padAnalogico = false;
            }
        }
        void Clear()
        {
            JumpHeld = JumpPressed = JumpReleased = previousHeld = uiJumpPulse = AnyPressed = false;
            padToqueHeld = padToqueSoltou = padAnalogico = false;
            Movement = AimPad = padToque = Vector2.zero;
            jumpSources.Clear();
            directionSources.Clear();
        }
        void OnApplicationFocus(bool focus) { if (!focus) Clear(); }
        void OnApplicationPause(bool paused) { if (paused) Clear(); }
        void OnDisable() => Clear();
    }
}
