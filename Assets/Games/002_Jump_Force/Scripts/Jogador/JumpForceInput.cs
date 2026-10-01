using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
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
        public bool mouseAsTouch = true;
        public bool JumpPressed { get; private set; }
        public bool JumpReleased { get; private set; }
        public bool JumpHeld { get; private set; }
        public bool AnyPressed { get; private set; }
        public Vector2 Movement { get; private set; }
        public float Direction => Movement.x;
        readonly HashSet<int> acceptedTouches = new();
        readonly HashSet<object> jumpSources = new();
        readonly Dictionary<object, Vector2> moveSources = new();
        readonly List<RaycastResult> uiHits = new();
        bool mouseAccepted, previousHeld, sensorEnabledByUs, uiJumpPulse;
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

        public void SetMoveSource(object id, Vector2 value)
        {
            if (!gameplayEnabled || waitingForRelease) return;
            if (value == Vector2.zero) moveSources.Remove(id);
            else moveSources[id] = value;
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
        bool OverUI(Vector2 position)
        {
            if (!EventSystem.current) return false;
            uiHits.Clear();
            EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = position }, uiHits);
            return uiHits.Count > 0;
        }
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
            Vector2 direction = Vector2.zero, virtualMove = Vector2.zero;
            foreach (var value in moveSources.Values) virtualMove.x += value.x;
            float touchDirection = 0;
            var keyboard = Keyboard.current;
            if (keyboard != null)
            {
                held |= keyboard.spaceKey.isPressed;
                pressed |= keyboard.spaceKey.wasPressedThisFrame;
                AnyPressed |= keyboard.anyKey.wasPressedThisFrame;
                // Horizontal only: up/down arrows are ignored on purpose (the game lives on the X/Y plane).
                direction = new Vector2(
                    (keyboard.rightArrowKey.isPressed ? 1 : 0) - (keyboard.leftArrowKey.isPressed ? 1 : 0), 0);
            }
            var pad = Gamepad.current;
            if (pad != null)
            {
                held |= pad.buttonSouth.isPressed || pad.buttonWest.isPressed;
                pressed |= pad.buttonSouth.wasPressedThisFrame || pad.buttonWest.wasPressedThisFrame;
                float dpadX = pad.dpad.ReadValue().x;
                var axis = new Vector2(Mathf.Abs(dpadX) > 0.1f ? dpadX : pad.leftStick.ReadValue().x, 0);
                if (axis.sqrMagnitude > direction.sqrMagnitude) direction = axis;
                foreach (var control in pad.allControls)
                    if (control is ButtonControl button && button.wasPressedThisFrame) AnyPressed = true;
            }
            if (Touchscreen.current != null)
            {
                foreach (var touch in Touchscreen.current.touches)
                {
                    int id = touch.touchId.ReadValue();
                    var pos = touch.position.ReadValue();
                    if (touch.press.wasPressedThisFrame)
                    {
                        AnyPressed = true;
                        acceptedTouches.Remove(id);
                        if (!OverUI(pos)) acceptedTouches.Add(id);
                    }
                    if (touch.press.isPressed && acceptedTouches.Contains(id))
                        touchDirection += pos.x < Screen.width * 0.5f ? -1 : 1;
                    if (!touch.press.isPressed) acceptedTouches.Remove(id);
                }
            }
            if (mouseAsTouch && Mouse.current != null)
            {
                var mouse = Mouse.current;
                if (mouse.leftButton.wasPressedThisFrame) { AnyPressed = true; mouseAccepted = !OverUI(mouse.position.ReadValue()); }
                if (mouse.leftButton.isPressed && mouseAccepted)
                    touchDirection += mouse.position.ReadValue().x < Screen.width * 0.5f ? -1 : 1;
                if (!mouse.leftButton.isPressed) mouseAccepted = false;
            }
            float raw = tiltEnabled && Accelerometer.current != null
                ? (Accelerometer.current.acceleration.ReadValue().x - tiltNeutral) * (invertTilt ? -1 : 1) : 0;
            raw = Mathf.Abs(raw) < tiltDeadZone ? 0 : Mathf.Sign(raw) * (Mathf.Abs(raw) - tiltDeadZone) * tiltSensitivity;
            tilt = Mathf.Lerp(tilt, Mathf.Clamp(raw, -1, 1), 1 - Mathf.Exp(-tiltSmoothing * Time.unscaledDeltaTime));
            Movement = Vector2.ClampMagnitude(virtualMove.sqrMagnitude > 0 ? virtualMove :
                touchDirection != 0 ? new Vector2(Mathf.Clamp(touchDirection, -1, 1), 0) :
                direction.sqrMagnitude > 0.01f ? direction : new Vector2(tilt, 0), 1);
            JumpPressed = !previousHeld && (held || pressed);
            JumpReleased = (previousHeld || pressed) && !held;
            JumpHeld = held;
            previousHeld = held;
        }
        void Clear()
        {
            JumpHeld = JumpPressed = JumpReleased = previousHeld = mouseAccepted = uiJumpPulse = AnyPressed = false;
            Movement = Vector2.zero;
            tilt = 0;
            acceptedTouches.Clear(); jumpSources.Clear(); moveSources.Clear();
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
