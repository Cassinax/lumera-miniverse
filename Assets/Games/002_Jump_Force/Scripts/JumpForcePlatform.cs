using System.Collections.Generic;
using UnityEngine;

namespace Lumera.JumpForce
{
    [DefaultExecutionOrder(-200), DisallowMultipleComponent, RequireComponent(typeof(BoxCollider))]
    public sealed class JumpForcePlatform : MonoBehaviour
    {
        public static readonly List<JumpForcePlatform> Active = new();
        [Tooltip("O chao inicial e solido; nao atrai nem ativa a camera de morte.")]
        public bool startingGround;
        [Min(0)] public float extraOrbitClearance = 0.15f;
        public BoxCollider Surface { get; private set; }
        public Vector3 Delta { get; private set; }
        public float Top => Surface.bounds.max.y;
        public Vector3 Center => Surface.bounds.center;
        Matrix4x4 previousMatrix;
        Matrix4x4 deltaMatrix;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetRegistry() => Active.Clear();

        void Awake() => Surface = GetComponent<BoxCollider>();
        void OnEnable()
        {
            Surface = GetComponent<BoxCollider>();
            previousMatrix = transform.localToWorldMatrix;
            deltaMatrix = Matrix4x4.identity;
            Delta = Vector3.zero;
            if (!Active.Contains(this)) Active.Add(this);
        }
        void OnDisable() => Active.Remove(this);
        void FixedUpdate()
        {
            var current = transform.localToWorldMatrix;
            deltaMatrix = current * previousMatrix.inverse;
            Delta = current.MultiplyPoint3x4(Vector3.zero) - previousMatrix.MultiplyPoint3x4(Vector3.zero);
            previousMatrix = current;
        }
        public Vector3 CarryPoint(Vector3 worldPoint) => deltaMatrix.MultiplyPoint3x4(worldPoint);
        public float SafeRadius(float playerRadius)
        {
            var e = Surface.bounds.extents;
            return new Vector2(e.x, e.z).magnitude + playerRadius + extraOrbitClearance;
        }
    }
}
