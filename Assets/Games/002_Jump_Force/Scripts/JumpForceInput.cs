using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace Lumera.JumpForce
{
    [DefaultExecutionOrder(-150), DisallowMultipleComponent]
    public sealed class JumpForceInput : MonoBehaviour
    {
        [Header("Controles")]
        public bool tiltEnabled = true;
        [Range(0, 0.9f)] public float tiltDeadZone = 0.12f;
        [Min(0.1f)] public float tiltSensitivity = 2.5f;
        [Min(0)] public float tiltSmoothing = 8;
        public bool invertTilt;
        [Tooltip("Permite testar toque usando o mouse no Editor.")]
        public bool mouseAsTouch = true;
        public bool JumpPressed { get; private set; }
        public bool JumpReleased { get; private set; }
        public bool JumpHeld { get; private set; }
        public float Direction { get; private set; }
        readonly HashSet<int> acceptedTouches = new();
        readonly List<RaycastResult> uiHits = new();
        bool mouseAccepted, previousHeld, sensorEnabledByUs;
        float tilt, tiltNeutral;

        void OnEnable()
        {
            if (tiltEnabled && Accelerometer.current != null && !Accelerometer.current.enabled)
            {
                InputSystem.EnableDevice(Accelerometer.current);
                sensorEnabledByUs = true;
            }
        }
        public void CalibrateTilt() => tiltNeutral = Accelerometer.current?.acceleration.ReadValue().x ?? 0;
        bool OverUI(Vector2 position)
        {
            if (!EventSystem.current) return false;
            uiHits.Clear();
            EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = position }, uiHits);
            return uiHits.Count > 0;
        }
        void Update()
        {
            bool held = false, pressed = false;
            float directional = 0, touchDirection = 0;
            var keyboard = Keyboard.current;
            if (keyboard != null)
            {
                held |= keyboard.spaceKey.isPressed;
                pressed |= keyboard.spaceKey.wasPressedThisFrame;
                directional = (keyboard.rightArrowKey.isPressed ? 1 : 0) - (keyboard.leftArrowKey.isPressed ? 1 : 0);
            }
            var pad = Gamepad.current;
            if (pad != null)
            {
                // Cross on PlayStation / A on Xbox, plus the literal Xbox X.
                held |= pad.buttonSouth.isPressed || pad.buttonWest.isPressed;
                pressed |= pad.buttonSouth.wasPressedThisFrame || pad.buttonWest.wasPressedThisFrame;
                float axis = Mathf.Abs(pad.dpad.x.ReadValue()) > 0.1f ? pad.dpad.x.ReadValue() : pad.leftStick.x.ReadValue();
                if (Mathf.Abs(axis) > Mathf.Abs(directional)) directional = axis;
            }
            if (Touchscreen.current != null)
            {
                foreach (var touch in Touchscreen.current.touches)
                {
                    int id = touch.touchId.ReadValue();
                    var pos = touch.position.ReadValue();
                    if (touch.press.wasPressedThisFrame) { acceptedTouches.Remove(id); if (!OverUI(pos)) { acceptedTouches.Add(id); pressed = true; } }
                    if (touch.press.isPressed && acceptedTouches.Contains(id))
                    {
                        held = true;
                        touchDirection += pos.x < Screen.width * 0.5f ? -1 : 1;
                    }
                    if (!touch.press.isPressed) acceptedTouches.Remove(id);
                }
            }
            if (mouseAsTouch && Mouse.current != null)
            {
                var mouse = Mouse.current;
                if (mouse.leftButton.wasPressedThisFrame)
                {
                    mouseAccepted = !OverUI(mouse.position.ReadValue());
                    pressed |= mouseAccepted;
                }
                if (mouse.leftButton.isPressed && mouseAccepted)
                {
                    held = true;
                    touchDirection += mouse.position.ReadValue().x < Screen.width * 0.5f ? -1 : 1;
                }
                if (!mouse.leftButton.isPressed) mouseAccepted = false;
            }
            float rawTilt = tiltEnabled && Accelerometer.current != null
                ? (Accelerometer.current.acceleration.ReadValue().x - tiltNeutral) * (invertTilt ? -1 : 1) : 0;
            rawTilt = Mathf.Abs(rawTilt) < tiltDeadZone ? 0 : Mathf.Sign(rawTilt) * (Mathf.Abs(rawTilt) - tiltDeadZone) * tiltSensitivity;
            tilt = Mathf.Lerp(tilt, Mathf.Clamp(rawTilt, -1, 1), 1 - Mathf.Exp(-tiltSmoothing * Time.unscaledDeltaTime));
            Direction = Mathf.Abs(touchDirection) > 0 ? Mathf.Clamp(touchDirection, -1, 1)
                : Mathf.Abs(directional) > 0.1f ? directional : tilt;
            JumpPressed = !previousHeld && (held || pressed);
            JumpReleased = (previousHeld || pressed) && !held;
            JumpHeld = held;
            previousHeld = held;
        }
        void Clear()
        {
            JumpHeld = JumpPressed = JumpReleased = previousHeld = mouseAccepted = false;
            Direction = tilt = 0;
            acceptedTouches.Clear();
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
