using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
namespace Lumera.JumpForce
{
    public sealed class JumpForceTouchControl : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IDragHandler
    {
        public JumpForceInput input;
        [Range(0, 1)] public float idleAlpha = 0;
        [Range(0, 1)] public float pressedAlpha = 0.3f;
        void Awake() { if (!GetComponent<CanvasGroup>()) gameObject.AddComponent<CanvasGroup>(); }
        void Update() { GetComponent<CanvasGroup>().alpha = pointers.Count > 0 ? pressedAlpha : idleAlpha; }
        public bool jump;
        public bool joystick;
        public Vector2 direction;
        readonly HashSet<int> pointers = new();
        object Source => this;
        public void OnPointerDown(PointerEventData data)
        {
            pointers.Add(data.pointerId);
            Apply(data);
        }
        public void OnDrag(PointerEventData data) { if (pointers.Contains(data.pointerId) && joystick) Apply(data); }
        void Apply(PointerEventData data)
        {
            if (!input) return;
            if (jump) input.SetJumpSource(Source, true);
            else
            {
                var value = direction;
                if (joystick && RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)transform, data.position, data.pressEventCamera, out var point))
                {
                    var rect = ((RectTransform)transform).rect;
                    value = Vector2.ClampMagnitude(new Vector2(point.x / (rect.width * 0.4f), point.y / (rect.height * 0.4f)), 1);
                }
                input.SetMoveSource(Source, value);
            }
        }
        public void OnPointerUp(PointerEventData data)
        {
            pointers.Remove(data.pointerId);
            if (pointers.Count == 0) Clear();
        }
        void Clear()
        {
            if (input) { input.SetMoveSource(Source, Vector2.zero); input.SetJumpSource(Source, false); }
        }
        void OnDisable() { pointers.Clear(); Clear(); }
    }
}
