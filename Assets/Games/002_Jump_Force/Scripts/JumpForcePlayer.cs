using UnityEngine;
using UnityEngine.Events;

namespace Lumera.JumpForce
{
    [DefaultExecutionOrder(-100), DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody), typeof(CapsuleCollider), typeof(JumpForceInput))]
    public sealed class JumpForcePlayer : MonoBehaviour
    {
        [Header("Referencias")]
        public JumpForceInput input;
        public JumpForceAnimation animationDriver;
        public Transform visual;
        [Header("Pulo - alturas em metros")]
        [Min(0.1f)] public float minimumJumpHeight = 1.5f;
        [Min(0.1f)] public float maximumJumpHeight = 6.5f;
        [Min(0.01f)] public float fullChargeSeconds = 1;
        public AnimationCurve chargeCurve = AnimationCurve.Linear(0, 0, 1, 1);
        [Min(0.1f)] public float gravityMultiplier = 1.8f;
        [Min(1)] public float maximumFallSpeed = 28;
        [Header("Orbita")]
        [Min(0)] public float orbitDegreesPerSecond = 150;
        [Min(0)] public float repulsionSpeed = 6;
        [Min(0)] public float attractionSpeed = 7;
        [Min(0)] public float steeringRadius = 0.3f;
        [Min(0)] public float visualTurnSpeed = 720;
        [Tooltip("Carrega X/Z com a plataforma alvo mesmo no ar. Y continua balistico.")]
        public bool followTargetInAir = true;
        [Header("Escolha de plataforma")]
        [Min(0.1f)] public float searchRadius = 8;
        [Min(0)] public float verticalDistanceWeight = 0.5f;
        [Min(0)] public float facingPreference = 1.4f;
        [Min(0)] public float targetHysteresis = 1.2f;
        [Header("Contato")]
        [Min(0.001f)] public float contactTolerance = 0.06f;
        [Min(0)] public float groundProbeDistance = 0.08f;
        [Header("Eventos")]
        public UnityEvent onJump = new();
        public UnityEvent onFirstPlatform = new();
        public UnityEvent onDeath = new();
        public bool Grounded { get; private set; }
        public bool Charging { get; private set; }
        public bool Dead { get; private set; }
        public bool ReachedPlatform { get; private set; }
        public float Charge01 => Mathf.Clamp01(chargeTime / Mathf.Max(0.01f, fullChargeSeconds));
        public JumpForcePlatform Target { get; private set; }
        public JumpForcePlatform Support { get; private set; }
        public float FeetY => body.position.y + feetOffset;
        public Rigidbody Body => body;
        Rigidbody body;
        CapsuleCollider capsule;
        float feetOffset, playerRadius, chargeTime, previousFeet, takeoffGrace;
        float? pendingJumpHeight;
        JumpForcePlatform launchPlatform;
        Vector3 facing = Vector3.forward;
        Vector3 initialPosition;
        Quaternion initialRotation;
        bool initialized;

        void Awake()
        {
            body = GetComponent<Rigidbody>();
            capsule = GetComponent<CapsuleCollider>();
            if (!input) input = GetComponent<JumpForceInput>();
            if (!animationDriver) animationDriver = GetComponent<JumpForceAnimation>();
            feetOffset = capsule.center.y * transform.lossyScale.y - capsule.height * transform.lossyScale.y * 0.5f;
            playerRadius = capsule.radius * Mathf.Max(transform.lossyScale.x, transform.lossyScale.z);
            body.constraints = RigidbodyConstraints.FreezeRotation;
            body.useGravity = false;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            initialPosition = body.position;
            initialRotation = visual ? visual.localRotation : Quaternion.identity;
            previousFeet = FeetY;
            facing = visual ? visual.forward : Vector3.forward;
            initialized = true;
        }
        void Update()
        {
            if (Dead || !input) return;
            if (input.JumpPressed) BeginCharge();
            if (Charging) chargeTime += Time.deltaTime;
            if (input.JumpReleased) ReleaseJump();
        }
        // Public commands also serve future UI buttons, upgrades and deterministic validation.
        public void BeginCharge()
        {
            if (!Grounded || Dead || Charging || pendingJumpHeight.HasValue) return;
            Charging = true;
            chargeTime = 0;
            animationDriver?.BeginCharge();
        }
        public void ReleaseJump()
        {
            if (!Charging) return;
            Charging = false;
            if (!Grounded || Dead) { animationDriver?.Land(); return; }
            float factor = Mathf.Clamp01(chargeCurve.Evaluate(Charge01));
            pendingJumpHeight = Mathf.Lerp(minimumJumpHeight, Mathf.Max(minimumJumpHeight, maximumJumpHeight), factor);
        }
        public void CancelCharge()
        {
            Charging = false;
            pendingJumpHeight = null;
            chargeTime = 0;
            animationDriver?.Land();
        }
        void OnApplicationFocus(bool focus) { if (!focus) CancelCharge(); }
        void OnApplicationPause(bool paused) { if (paused) CancelCharge(); }

        void FixedUpdate()
        {
            if (Dead) return;
            float dt = Time.fixedDeltaTime;
            body.linearVelocity = Vector3.up * body.linearVelocity.y;
            takeoffGrace = Mathf.Max(0, takeoffGrace - dt);
            // Carry before contact tests. The support has already sampled its new transform.
            if (Grounded && Support && Support.isActiveAndEnabled)
                body.position = Support.CarryPoint(body.position);
            else if (followTargetInAir && Target && Target.isActiveAndEnabled)
            {
                var carried = Target.CarryPoint(body.position);
                body.position = new Vector3(carried.x, body.position.y, carried.z);
            }
            Physics.SyncTransforms();
            FindSupport();
            if (pendingJumpHeight.HasValue)
            {
                if (Grounded)
                {
                    launchPlatform = Support;
                    float speed = Mathf.Sqrt(2 * Gravity * pendingJumpHeight.Value);
                    body.linearVelocity = Vector3.up * speed;
                    Grounded = false;
                    Support = null;
                    takeoffGrace = 0.12f;
                    animationDriver?.Release();
                    onJump.Invoke();
                }
                pendingJumpHeight = null;
            }
            if (!Grounded)
            {
                if (Charging) { CancelCharge(); animationDriver?.Release(); }
                SelectTarget();
                Orbit(dt);
                body.AddForce(Vector3.down * Gravity, ForceMode.Acceleration);
                if (body.linearVelocity.y < -maximumFallSpeed)
                    body.linearVelocity = Vector3.down * maximumFallSpeed;
            }
            else body.linearVelocity = Vector3.zero;
            UpdateCollisions();
            previousFeet = FeetY;
        }
        float Gravity => Mathf.Max(0.1f, -Physics.gravity.y * gravityMultiplier);

        void FindSupport()
        {
            JumpForcePlatform found = null;
            if (takeoffGrace <= 0 && body.linearVelocity.y <= 0.5f)
            {
                foreach (var platform in JumpForcePlatform.Active)
                {
                    if (!platform.Surface.enabled || platform.Surface.isTrigger) continue;
                    float top = platform.Top;
                    var local = platform.transform.InverseTransformPoint(body.position);
                    var box = platform.Surface;
                    var scale = platform.transform.lossyScale;
                    bool inside = Mathf.Abs(local.x - box.center.x) <= box.size.x * 0.5f + playerRadius * 0.4f / Mathf.Abs(scale.x)
                        && Mathf.Abs(local.z - box.center.z) <= box.size.z * 0.5f + playerRadius * 0.4f / Mathf.Abs(scale.z);
                    bool touching = FeetY >= top - contactTolerance && FeetY <= top + groundProbeDistance;
                    bool crossed = previousFeet >= top - platform.Delta.y - contactTolerance && FeetY <= top && !Grounded;
                    if (inside && (touching || crossed) && (found == null || top > found.Top)) found = platform;
                }
            }
            bool wasGrounded = Grounded;
            Grounded = found != null;
            Support = found;
            if (found)
            {
                var p = body.position;
                p.y = found.Top - feetOffset + 0.005f;
                body.position = p;
                body.linearVelocity = Vector3.zero;
                Target = null;
                launchPlatform = null;
                if (!wasGrounded) animationDriver?.Land();
                if (!found.startingGround && !ReachedPlatform)
                {
                    ReachedPlatform = true;
                    onFirstPlatform.Invoke();
                }
            }
            else if (wasGrounded) animationDriver?.Release();
        }
        void SelectTarget()
        {
            var best = (JumpForcePlatform)null;
            float bestScore = float.PositiveInfinity;
            float apex = FeetY + Mathf.Pow(Mathf.Max(0, body.linearVelocity.y), 2) / (2 * Gravity);
            foreach (var platform in JumpForcePlatform.Active)
            {
                if (platform.startingGround || !platform.Surface.enabled || platform.Surface.isTrigger) continue;
                if (platform == launchPlatform && body.linearVelocity.y > 0) continue;
                if (platform.Top > apex + contactTolerance) continue;
                var delta = platform.Center - body.position;
                float horizontal = new Vector2(delta.x, delta.z).magnitude;
                if (horizontal > searchRadius || Mathf.Abs(platform.Top - FeetY) > searchRadius) continue;
                delta.y = 0;
                float alignment = delta.sqrMagnitude > 0.01f ? Vector3.Dot(facing, delta.normalized) : 0;
                float score = horizontal + Mathf.Abs(platform.Top - FeetY) * verticalDistanceWeight - alignment * facingPreference;
                if (platform == Target) score -= targetHysteresis;
                if (score < bestScore) { bestScore = score; best = platform; }
            }
            Target = best ? best : launchPlatform && launchPlatform.isActiveAndEnabled ? launchPlatform : null;
        }
        void Orbit(float dt)
        {
            if (!Target) return;
            var p = body.position;
            var center = Target.Center;
            var radial = new Vector3(p.x - center.x, 0, p.z - center.z);
            float radius = radial.magnitude;
            if (radius < 0.001f) radial = Vector3.back;
            else radial /= radius;
            float steer = input ? input.Direction : 0;
            radial = Quaternion.AngleAxis(-steer * orbitDegreesPerSecond * dt, Vector3.up) * radial;
            if (FeetY < Target.Top)
            {
                // Only push up to the safe ring; never keep accelerating outward.
                float safe = Target.SafeRadius(playerRadius);
                if (radius < safe) radius = Mathf.MoveTowards(radius, safe, repulsionSpeed * dt);
            }
            else
            {
                float insideRadius = Mathf.Min(steeringRadius,
                    Mathf.Min(Target.Surface.bounds.extents.x, Target.Surface.bounds.extents.z) * 0.5f);
                radius = Mathf.MoveTowards(radius, Mathf.Abs(steer) > 0.1f ? insideRadius : 0, attractionSpeed * dt);
            }
            var next = new Vector3(center.x + radial.x * radius, p.y, center.z + radial.z * radius);
            var motion = next - p;
            if (Mathf.Abs(steer) > 0.1f)
                facing = Vector3.Cross(Vector3.up, radial) * -Mathf.Sign(steer);
            else if (motion.sqrMagnitude > 0.0001f) facing = motion.normalized;
            if (visual && facing.sqrMagnitude > 0.01f)
                visual.rotation = Quaternion.RotateTowards(visual.rotation, Quaternion.LookRotation(facing), visualTurnSpeed * dt);
            body.position = next;
        }
        void UpdateCollisions()
        {
            foreach (var platform in JumpForcePlatform.Active)
            {
                bool ignore = !platform.startingGround && platform != Support
                    && (body.linearVelocity.y > 0.01f || FeetY < platform.Top - contactTolerance);
                Physics.IgnoreCollision(capsule, platform.Surface, ignore);
            }
        }
        public void Die()
        {
            if (Dead) return;
            CancelCharge();
            Dead = true;
            body.linearVelocity = Vector3.zero;
            body.isKinematic = true;
            onDeath.Invoke();
        }
        public void Restart()
        {
            body.isKinematic = false;
            Dead = Grounded = ReachedPlatform = false;
            Target = Support = launchPlatform = null;
            CancelCharge();
            body.position = initialPosition;
            body.linearVelocity = Vector3.zero;
            previousFeet = FeetY;
            takeoffGrace = 0;
            facing = Vector3.forward;
            if (visual) visual.localRotation = initialRotation;
            animationDriver?.ResetIntro();
            UpdateCollisions();
        }
        void OnDisable()
        {
            if (!initialized) return;
            CancelCharge();
            foreach (var platform in JumpForcePlatform.Active)
                if (platform && platform.Surface && capsule) Physics.IgnoreCollision(capsule, platform.Surface, false);
        }
    }
}



