using System.Collections.Generic;
using UnityEngine;

namespace Lumera.JumpForce
{
    [DefaultExecutionOrder(-200), DisallowMultipleComponent, RequireComponent(typeof(BoxCollider))]
    public sealed class JumpForcePlatform : MonoBehaviour
    {
        public Rigidbody Body { get; private set; }
        public Vector3 Velocity => Body ? Body.linearVelocity : Vector3.zero;
        public static readonly List<JumpForcePlatform> Active = new();
        [Tooltip("O chao inicial e solido; nao atrai nem ativa a camera de morte.")]
        public bool startingGround;
        [Min(0)] public float extraOrbitClearance = 0.15f;
        public BoxCollider Surface { get; private set; }
        public float Top => Surface.bounds.max.y;
        public Vector3 Center => Surface.bounds.center;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetRegistry() => Active.Clear();

        void Awake()
        {
            Surface = GetComponent<BoxCollider>();
            Body = GetComponent<Rigidbody>();
        }
        void OnEnable()
        {
            Surface = GetComponent<BoxCollider>();
            Body = Surface.attachedRigidbody;

            if (!Active.Contains(this))
                Active.Add(this);
        }
        void OnDisable() => Active.Remove(this);
        public float SafeRadius(float playerRadius)
        {
            var e = Surface.bounds.extents;
            return new Vector2(e.x, e.z).magnitude + playerRadius + extraOrbitClearance;
        }
    }
}
