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
        // Joystick virtual: enquanto pressionado, a inclinacao do aparelho e ignorada.
        public bool JoystickHeld { get; private set; }
        // Mira do salto. Absoluta (-1 a 1, lado todo = angulo maximo): joystick, analogico e inclinacao.
        // Giro (-1, 0 ou 1): setas e direcional giram a mira, que fica onde parar. Sem fonte: giro 0 (mantem).
        public bool AimAbsolute { get; private set; }
        public float Aim { get; private set; }
        readonly HashSet<object> jumpSources = new();
        bool previousHeld, sensorEnabledByUs, uiJumpPulse;
        float tilt, tiltNeutral;
        Vector2 joystick;
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

        // Chamado pelo JumpForceJoystick. valor: deslocamento do controle, de -1 a 1 em cada eixo.
        public void SetJoystick(Vector2 value, bool held)
        {
            if (!gameplayEnabled || waitingForRelease) { joystick = Vector2.zero; JoystickHeld = false; return; }
            joystick = held ? Vector2.ClampMagnitude(value, 1) : Vector2.zero;
            JoystickHeld = held;
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
                float x = pad.leftStick.ReadValue().x;
                if (Mathf.Abs(x) > stickDeadZone) stick = x;
                foreach (var control in pad.allControls)
                    if (control is ButtonControl button && button.wasPressedThisFrame) AnyPressed = true;
            }
            if (Touchscreen.current != null)
                foreach (var touch in Touchscreen.current.touches)
                    if (touch.press.wasPressedThisFrame) AnyPressed = true;
            if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame) AnyPressed = true;
            // Inclinar o aparelho so vale com o joystick solto.
            float raw = tiltEnabled && !JoystickHeld && Accelerometer.current != null
                ? (Accelerometer.current.acceleration.ReadValue().x - tiltNeutral) * (invertTilt ? -1 : 1) : 0;
            raw = Mathf.Abs(raw) < tiltDeadZone ? 0 : Mathf.Sign(raw) * (Mathf.Abs(raw) - tiltDeadZone) * tiltSensitivity;
            tilt = JoystickHeld ? 0 : Mathf.Lerp(tilt, Mathf.Clamp(raw, -1, 1), 1 - Mathf.Exp(-tiltSmoothing * Time.unscaledDeltaTime));
            // Prioridade: joystick, setas/direcional, analogico, inclinacao.
            float move = JoystickHeld ? joystick.x : digital != 0 ? digital : stick != 0 ? stick : tilt;
            Movement = new Vector2(Mathf.Clamp(move, -1, 1), 0);
            if (JoystickHeld) { AimAbsolute = true; Aim = Mathf.Clamp(joystick.x, -1, 1); }
            else if (digital != 0) { AimAbsolute = false; Aim = digital; }
            else if (stick != 0) { AimAbsolute = true; Aim = stick; }
            else if (Mathf.Abs(tilt) > 0.01f) { AimAbsolute = true; Aim = tilt; }
            else { AimAbsolute = false; Aim = 0; }
            JumpPressed = !previousHeld && (held || pressed);
            JumpReleased = (previousHeld || pressed) && !held;
            JumpHeld = held;
            previousHeld = held;
        }
        void Clear()
        {
            JumpHeld = JumpPressed = JumpReleased = previousHeld = uiJumpPulse = AnyPressed = JoystickHeld = AimAbsolute = false;
            Movement = joystick = Vector2.zero;
            tilt = Aim = 0;
            jumpSources.Clear();
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
