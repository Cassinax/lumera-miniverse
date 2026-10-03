using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace Lumera.JumpForce
{
    [DefaultExecutionOrder(-150), DisallowMultipleComponent]
    public sealed class JumpForceInput : MonoBehaviour
    {
        public bool tiltEnabled = true;
        [Range(0, 0.9f)] public float tiltDeadZone = 0.12f;
        [Min(0.1f)] public float tiltSensitivity = 2.5f;
        [Min(0)] public float tiltSmoothing = 8;
        public bool invertTilt;
        [Tooltip("Abaixo disto o analogico do controle e ignorado (movimento e mira).")]
        [Range(0, 0.9f)] public float stickDeadZone = 0.2f;
        public bool JumpPressed { get; private set; }
        public bool JumpReleased { get; private set; }
        public bool JumpHeld { get; private set; }
        public bool AnyPressed { get; private set; }
        public Vector2 Movement { get; private set; }
        public float Direction => Movement.x;
        // Botoes de direcao da tela (Button_Esquerda, Button_Direita): enquanto algum esta pressionado,
        // a inclinacao do aparelho e ignorada.
        public bool DirectionButtonsHeld => directionSources.Count > 0;
        // Mira do salto. Absoluta (-1 a 1, lado todo = angulo maximo): inclinacao. Por angulo: analogico.
        // Giro (-1, 0 ou 1): botoes da tela, setas e direcional giram a mira, que fica onde parar. Sem fonte: mantem.
        public bool AimAbsolute { get; private set; }
        public float Aim { get; private set; }
        // Analogico fisico: a mira e o angulo para onde ele aponta (atan2 de x e y), em graus.
        // 0 = para cima, positivo = para a direita da tela. Vale so quando AimByAngle e verdadeiro.
        public bool AimByAngle { get; private set; }
        public float AimAngle { get; private set; }
        readonly HashSet<object> jumpSources = new();
        readonly Dictionary<object, int> directionSources = new();
        bool previousHeld, sensorEnabledByUs, uiJumpPulse;
        float tilt, tiltNeutral;
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
        void OnEnable()
        {
            if (tiltEnabled && Accelerometer.current != null && !Accelerometer.current.enabled)
            {
                InputSystem.EnableDevice(Accelerometer.current);
                sensorEnabledByUs = true;
            }
        }
        public void CalibrateTilt() => tiltNeutral = Accelerometer.current?.acceleration.ReadValue().x ?? 0;
        void Update()
        {
            if (!gameplayEnabled) { Clear(); return; }
            if (waitingForRelease)
            {
                Clear();
                if (!ControlsHeld()) { waitingForRelease = false; CalibrateTilt(); }
                return;
            }
            bool held = jumpSources.Count > 0, pressed = uiJumpPulse;
            uiJumpPulse = false;
            AnyPressed = pressed;
            float digital = 0, stick = 0;
            bool stickMirando = false;
            float stickAngulo = 0;
            var keyboard = Keyboard.current;
            if (keyboard != null)
            {
                held |= keyboard.spaceKey.isPressed;
                pressed |= keyboard.spaceKey.wasPressedThisFrame;
                AnyPressed |= keyboard.anyKey.wasPressedThisFrame;
                // Horizontal only: up/down arrows are ignored on purpose (the game lives on the X/Y plane).
                digital = (keyboard.rightArrowKey.isPressed ? 1 : 0) - (keyboard.leftArrowKey.isPressed ? 1 : 0);
            }
            var pad = Gamepad.current;
            if (pad != null)
            {
                held |= pad.buttonSouth.isPressed || pad.buttonWest.isPressed;
                pressed |= pad.buttonSouth.wasPressedThisFrame || pad.buttonWest.wasPressedThisFrame;
                float dpadX = pad.dpad.ReadValue().x;
                if (Mathf.Abs(dpadX) > 0.1f) digital = Mathf.Sign(dpadX);
                var analogico = pad.leftStick.ReadValue();
                if (Mathf.Abs(analogico.x) > stickDeadZone) stick = analogico.x;
                // Mira: inclinacao do analogico pelo comprimento (Pitagoras) e direcao pelo angulo (atan2).
                // Apontar para cima volta a mira a 0; a metade de baixo vale como o lado para onde pende.
                if (analogico.magnitude > stickDeadZone)
                {
                    stickAngulo = Mathf.Atan2(analogico.x, analogico.y) * Mathf.Rad2Deg;
                    if (Mathf.Abs(stickAngulo) > 90) stickAngulo = Mathf.Sign(analogico.x) * 90;
                    // Quase reto para baixo nao tem lado: mantem a mira.
                    stickMirando = analogico.y >= 0 || Mathf.Abs(analogico.x) > Mathf.Abs(analogico.y) * 0.25f;
                }
                foreach (var control in pad.allControls)
                    if (control is ButtonControl button && button.wasPressedThisFrame) AnyPressed = true;
            }
            if (Touchscreen.current != null)
                foreach (var touch in Touchscreen.current.touches)
                    if (touch.press.wasPressedThisFrame) AnyPressed = true;
            if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame) AnyPressed = true;
            // Botoes da tela valem como as setas e passam na frente delas.
            int botoes = DirectionButtons();
            if (botoes != 0) digital = botoes;
            // Inclinar o aparelho so vale com os botoes de direcao soltos.
            float raw = tiltEnabled && !DirectionButtonsHeld && Accelerometer.current != null
                ? (Accelerometer.current.acceleration.ReadValue().x - tiltNeutral) * (invertTilt ? -1 : 1) : 0;
            raw = Mathf.Abs(raw) < tiltDeadZone ? 0 : Mathf.Sign(raw) * (Mathf.Abs(raw) - tiltDeadZone) * tiltSensitivity;
            tilt = DirectionButtonsHeld ? 0 : Mathf.Lerp(tilt, Mathf.Clamp(raw, -1, 1), 1 - Mathf.Exp(-tiltSmoothing * Time.unscaledDeltaTime));
            // Prioridade: botoes da tela, setas/direcional, analogico, inclinacao.
            float move = digital != 0 ? digital : stick != 0 ? stick : tilt;
            Movement = new Vector2(Mathf.Clamp(move, -1, 1), 0);
            AimByAngle = false;
            if (digital != 0) { AimAbsolute = false; Aim = digital; }
            else if (stickMirando) { AimAbsolute = AimByAngle = true; AimAngle = stickAngulo; Aim = stickAngulo / 90; }
            else if (Mathf.Abs(tilt) > 0.01f) { AimAbsolute = true; Aim = tilt; }
            else { AimAbsolute = false; Aim = 0; }
            JumpPressed = !previousHeld && (held || pressed);
            JumpReleased = (previousHeld || pressed) && !held;
            JumpHeld = held;
            previousHeld = held;
        }
        void Clear()
        {
            JumpHeld = JumpPressed = JumpReleased = previousHeld = uiJumpPulse = AnyPressed = AimAbsolute = AimByAngle = false;
            Movement = Vector2.zero;
            tilt = Aim = AimAngle = 0;
            jumpSources.Clear();
            directionSources.Clear();
        }
        void OnApplicationFocus(bool focus) { if (!focus) Clear(); }
        void OnApplicationPause(bool paused) { if (paused) Clear(); }
        void OnDisable()
        {
            Clear();
            if (sensorEnabledByUs && Accelerometer.current != null) InputSystem.DisableDevice(Accelerometer.current);
            sensorEnabledByUs = false;
        }
    }
}
