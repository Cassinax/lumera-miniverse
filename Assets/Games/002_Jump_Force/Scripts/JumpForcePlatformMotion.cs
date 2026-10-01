using UnityEngine;

namespace Lumera.JumpForce
{
    [DefaultExecutionOrder(-300), DisallowMultipleComponent, RequireComponent(typeof(JumpForcePlatform))]
    public sealed class JumpForcePlatformMotion : MonoBehaviour
    {
        [Tooltip("Amplitude em metros no espaco global. Zero mantem a plataforma parada.")]
        public Vector3 amplitude = Vector3.zero;
        [Min(0.1f)] public float period = 4;
        [Range(0, 1)] public float phase;
        [Tooltip("Rotacao horizontal em graus por segundo. Nao inclinar o piso.")]
        public float yawDegreesPerSecond;
        Vector3 origin;
        Quaternion initialRotation;
        float elapsed;


        void Awake()
        {
            origin = transform.position;
            initialRotation = transform.rotation;

        }
        void FixedUpdate()
        {
            elapsed += Time.fixedDeltaTime;
            float offset = Mathf.Sin((elapsed / Mathf.Max(0.1f, period) + phase) * 2 * Mathf.PI)
                         - Mathf.Sin(phase * 2 * Mathf.PI);
            var position = origin + amplitude * offset;
            var rotation = initialRotation * Quaternion.Euler(0, elapsed * yawDegreesPerSecond, 0);
            // Set before the platform samples its displacement and the character follows it.
            transform.SetPositionAndRotation(position, rotation);

        }
    }
}
