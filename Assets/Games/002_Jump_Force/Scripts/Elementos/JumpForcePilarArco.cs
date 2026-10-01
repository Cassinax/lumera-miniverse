using UnityEngine;

namespace Lumera.JumpForce
{
    // Obstáculo sólido que vai e volta em um único eixo do mundo (X ou Y). Não é atravessável:
    // o Rigidbody kinematic empurra o personagem, e o JumpForcePlayer não anda para dentro dele.
    [DefaultExecutionOrder(-300), DisallowMultipleComponent, RequireComponent(typeof(Rigidbody))]
    public sealed class JumpForcePilarArco : MonoBehaviour
    {
        public enum Eixo { X, Y }

        public Eixo eixo = Eixo.X;
        [Tooltip("Distância em metros que o pilar percorre para cada lado. Zero mantém o pilar parado.")]
        [Min(0)] public float amplitude = 2;
        [Tooltip("Segundos para ir e voltar.")]
        [Min(0.1f)] public float period = 4;
        [Range(0, 1)] public float phase;
        [Tooltip("Sorteia, ao iniciar, se o pilar vai primeiro para o lado positivo ou negativo do eixo.")]
        public bool randomStartDirection = true;

        Rigidbody body;
        Vector3 localOrigin;
        float elapsed, direction = 1;

        Vector3 Axis => eixo == Eixo.X ? Vector3.right : Vector3.up;
        // Follows the parent (e.g. a moving platform) while oscillating along the world axis.
        Vector3 Origin => transform.parent ? transform.parent.TransformPoint(localOrigin) : localOrigin;

        void Awake()
        {
            // RequireComponent does not add the Rigidbody to components that already existed in the scene.
            if (!TryGetComponent(out body)) body = gameObject.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            localOrigin = transform.localPosition;
            if (!transform.parent) localOrigin = transform.position;
            if (randomStartDirection) direction = Random.value < 0.5f ? -1 : 1;
        }

        void FixedUpdate()
        {
            elapsed += Time.fixedDeltaTime;
            // Same curve as the platforms: starts at the placed position, flipping the sign changes only the first direction.
            float offset = direction * (Mathf.Sin((elapsed / Mathf.Max(0.1f, period) + phase) * 2 * Mathf.PI)
                         - Mathf.Sin(phase * 2 * Mathf.PI));
            // MovePosition on a kinematic body pushes the character instead of teleporting through it.
            body.MovePosition(Origin + Axis * (amplitude * offset));
        }

#if UNITY_EDITOR
        // Path and both extremes of the pillar, drawn from where it is placed (or started, in Play).
        void OnDrawGizmosSelected()
        {
            Vector3 origin = Application.isPlaying && body ? Origin : transform.position;
            float shift = Mathf.Sin(phase * 2 * Mathf.PI);
            Vector3 a = origin + Axis * (amplitude * (-1 - shift));
            Vector3 b = origin + Axis * (amplitude * (1 - shift));
            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(a, b);
            var colliders = GetComponentsInChildren<Collider>();
            if (colliders.Length == 0) return;
            Bounds bounds = colliders[0].bounds;
            foreach (var c in colliders) bounds.Encapsulate(c.bounds);
            Vector3 fromPivot = bounds.center - transform.position;
            Gizmos.color = new Color(1f, 0.85f, 0f, 0.5f);
            Gizmos.DrawWireCube(a + fromPivot, bounds.size);
            Gizmos.DrawWireCube(b + fromPivot, bounds.size);
        }
#endif
    }
}
