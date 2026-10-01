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
        [Tooltip("Sorteia, ao iniciar, se a plataforma vai primeiro no sentido da amplitude ou no oposto.")]
        public bool randomStartDirection = true;
        Vector3 origin;
        Quaternion initialRotation;
        float elapsed, direction = 1;


        void Awake()
        {
            origin = transform.position;
            initialRotation = transform.rotation;
            if (randomStartDirection) direction = Random.value < 0.5f ? -1 : 1;
        }
        void FixedUpdate()
        {
            elapsed += Time.fixedDeltaTime;
            // Flipping the sign keeps the start at the origin; only the first direction changes.
            float offset = direction * (Mathf.Sin((elapsed / Mathf.Max(0.1f, period) + phase) * 2 * Mathf.PI)
                         - Mathf.Sin(phase * 2 * Mathf.PI));
            var position = origin + amplitude * offset;
            // Platforms only translate; the rotation stays as placed in the scene.
            // Set before the platform samples its displacement and the character follows it.
            transform.SetPositionAndRotation(position, initialRotation);

        }
    }
}
