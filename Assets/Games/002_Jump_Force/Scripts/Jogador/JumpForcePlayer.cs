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
        [Header("Movimento livre")]
        public Camera movementCamera;
        [Min(0)] public float groundSpeed = 3.5f;
        [Min(0)] public float airSpeed = 5;
        [Min(0)] public float visualTurnSpeed = 720;
        [Header("Plano 2D - so X e Y")]
        [Tooltip("Trava o personagem no Z em que ele comeca a cena.")]
        public bool lockZ = true;
        [Tooltip("Quao rapido o empurrao horizontal de impulsos externos (trampolim, ventilador) se dissipa no ar.")]
        [Min(0)] public float externalHorizontalDrag = 2.5f;
        [Tooltip("O mesmo, com o personagem no chao (atrito).")]
        [Min(0)] public float externalGroundDrag = 8;
        [Header("Ajuda de plataforma - somente no ar")]
        public bool assistanceEnabled = true;
        [Min(0)] public float assistanceRange = 2.5f;
        [Min(0)] public float repulsionSpeed = 1;
        [Min(0)] public float attractionSpeed = 1;
        [Range(0, 1)] public float assistanceWhileSteering = 0.15f;
        public float MoveAmount { get; private set; }
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
        float feetOffset, playerRadius, chargeTime, previousFeet, takeoffGrace, planeZ, externalSpeedX;
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
            // Combine with the lock instead of overwriting what the Inspector set.
            planeZ = body.position.z;
            body.constraints = RigidbodyConstraints.FreezeRotation | (lockZ ? RigidbodyConstraints.FreezePositionZ : RigidbodyConstraints.None);
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
        // Velocity in m/s, independent of the Rigidbody mass. Used by the trampoline, the fan and other launchers.
        public void Launch(Vector2 velocity)
        {
            if (Dead) return;
            externalSpeedX = velocity.x;
            if (Grounded)
            {
                // A push too weak to clear the floor during the takeoff grace would sink through it: slide instead.
                if (velocity.y <= Gravity * TakeoffGrace) return;
                Grounded = false;
                Support = null;
            }
            if (Charging) CancelCharge();
            pendingJumpHeight = null;
            launchPlatform = null;
            body.linearVelocity = Vector3.up * velocity.y;
            if (velocity.y > 0)
            {
                takeoffGrace = TakeoffGrace;
                animationDriver?.Release();
            }
        }
        // A child collider's collision messages go to its Rigidbody's GameObject (a trampoline inside a
        // platform with a kinematic Rigidbody never hears them), but the player always gets its own.
        void OnCollisionEnter(Collision collision)
        {
            if (Dead) return;
            for (int i = 0; i < collision.contactCount; i++)
            {
                var contact = collision.GetContact(i);
                var other = contact.otherCollider;
                if (!other) continue;
                if (other.TryGetComponent(out JumpForceTrampolim trampolim) && trampolim.Impulsionar(this, other, contact.point)) return;
                if (other.TryGetComponent(out JumpForceVentilador ventilador)) { ventilador.Empurrar(this); return; }
            }
        }
        void OnTriggerEnter(Collider other)
        {
            if (Dead) return;
            if (other.TryGetComponent(out JumpForceTrampolim trampolim)) trampolim.Impulsionar(this, other, capsule);
            else if (other.TryGetComponent(out JumpForceVentilador ventilador)) ventilador.Empurrar(this);
        }
        void OnApplicationFocus(bool focus) { if (!focus) CancelCharge(); }
        void OnApplicationPause(bool paused) { if (paused) CancelCharge(); }

        void FixedUpdate()
        {
            if (Dead) return;
#if UNITY_EDITOR
            if (FollowEditorDrag()) return;
#endif
            float dt = Time.fixedDeltaTime;
            body.linearVelocity = Vector3.up * body.linearVelocity.y;
            takeoffGrace = Mathf.Max(0, takeoffGrace - dt);
            // Carry before contact tests. The support has already sampled its new transform.
            if (Grounded && Support && Support.isActiveAndEnabled)
                body.position = Support.CarryPoint(body.position);
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
                    takeoffGrace = TakeoffGrace;
                    animationDriver?.Release();
                    onJump.Invoke();
                }
                pendingJumpHeight = null;
            }
            if (!Grounded)
            {
                if (Charging) { CancelCharge(); animationDriver?.Release(); }
                SelectTarget();
                MoveHorizontally(dt);
                body.AddForce(Vector3.down * Gravity, ForceMode.Acceleration);
                if (body.linearVelocity.y < -maximumFallSpeed)
                    body.linearVelocity = Vector3.down * maximumFallSpeed;
            }
            else
            {
                MoveHorizontally(dt);
                FindSupport();
                if (Grounded) body.linearVelocity = Vector3.zero;
                else body.AddForce(Vector3.down * Gravity, ForceMode.Acceleration);
            }
            externalSpeedX *= Mathf.Exp(-(Grounded ? externalGroundDrag : externalHorizontalDrag) * dt);
            UpdateCollisions();
            LockToPlane();
            previousFeet = FeetY;
        }
        float Gravity => Mathf.Max(0.1f, -Physics.gravity.y * gravityMultiplier);
        const float TakeoffGrace = 0.12f;

        // The constraint only covers the solver; carry, snaps and direct position writes can still leak into Z.
        void LockToPlane()
        {
            if (!lockZ) return;
            var p = body.position;
            if (p.z != planeZ) { p.z = planeZ; body.position = p; }
            var v = body.linearVelocity;
            if (v.z != 0) { v.z = 0; body.linearVelocity = v; }
        }
#if UNITY_EDITOR
        bool editorDragging;
        // Play mode: interpolation and FixedUpdate rewrite the Transform every step, undoing Scene view gizmo drags.
        // While the root is being dragged in the Scene view, the body follows the Transform instead.
        bool FollowEditorDrag()
        {
            bool dragging = GUIUtility.hotControl != 0
                && UnityEditor.EditorWindow.focusedWindow is UnityEditor.SceneView
                && UnityEditor.Selection.Contains(gameObject);
            if (dragging != editorDragging)
            {
                editorDragging = dragging;
                body.interpolation = dragging ? RigidbodyInterpolation.None : RigidbodyInterpolation.Interpolate;
                if (!dragging) { Grounded = false; Support = null; }
            }
            if (!dragging) return false;
            var p = transform.position;
            if (lockZ) p.z = planeZ;
            body.position = p;
            body.rotation = transform.rotation;
            body.linearVelocity = Vector3.zero;
            externalSpeedX = 0;
            previousFeet = FeetY;
            return true;
        }
#endif

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
                if (lockZ) delta.z = 0;
                float horizontal = new Vector2(delta.x, delta.z).magnitude;
                if (horizontal > searchRadius || Mathf.Abs(platform.Top - FeetY) > searchRadius) continue;
                delta.y = 0;
                float alignment = delta.sqrMagnitude > 0.01f ? Vector3.Dot(facing, delta.normalized) : 0;
                float score = horizontal + Mathf.Abs(platform.Top - FeetY) * verticalDistanceWeight - alignment * facingPreference;
                if (platform == Target) score -= targetHysteresis;
                if (score < bestScore) { bestScore = score; best = platform; }
            }
            Target = best;
        }
        void MoveHorizontally(float dt)
        {
            // Only the horizontal part of the command counts: up/down never means "move forward".
            float command = input ? Mathf.Clamp(input.Movement.x, -1, 1) : 0;
            MoveAmount = Mathf.Abs(command);
            // Screen right mapped onto world X (flipped if the camera looks from the other side).
            Vector3 right = Vector3.right;
            var camera = movementCamera ? movementCamera : Camera.main;
            if (camera && camera.transform.right.x < 0) right = Vector3.left;
            Vector3 desired = right * command * (Grounded ? groundSpeed : airSpeed);
            Vector3 help = Vector3.zero;
            if (!Grounded && assistanceEnabled && Target)
            {
                var radial = body.position - Target.Center;
                radial.y = 0;
                if (lockZ) radial.z = 0;
                float radius = radial.magnitude;
                if (radius <= assistanceRange)
                {
                    var outward = radius > 0.001f ? radial / radius : -right;
                    if (FeetY < Target.Top)
                    {
                        float remaining = Mathf.Max(0, Target.SafeRadius(playerRadius) - radius);
                        help = outward * Mathf.Min(repulsionSpeed, remaining / dt);
                    }
                    else help = -outward * Mathf.Min(attractionSpeed, radius / dt);
                    if (MoveAmount > 0.01f)
                    {
                        help *= assistanceWhileSteering;
                        // Assistance can bend the trajectory, but never oppose the player's command.
                        var direction = desired.normalized;
                        help -= direction * Mathf.Min(0, Vector3.Dot(help, direction));
                    }
                }
            }
            if (MoveAmount > 0.01f)
            {
                facing = desired.normalized;
                if (visual) visual.rotation = Quaternion.RotateTowards(visual.rotation, Quaternion.LookRotation(facing), visualTurnSpeed * dt);
            }
            Vector3 posicaoAlvo = (desired + help + Vector3.right * externalSpeedX) * dt;
            if (lockZ) posicaoAlvo.z = 0f;
            posicaoAlvo = StopAtObstacles(posicaoAlvo);
            if (posicaoAlvo != Vector3.zero) body.position += posicaoAlvo;
        }
        // Horizontal movement teleports the body, so the solver alone would let it sink into solid obstacles.
        // Only obstacles stop it: platforms keep their pass-through-from-below behaviour.
        Vector3 StopAtObstacles(Vector3 step)
        {
            const float skin = 0.01f;
            float distance = step.magnitude;
            if (distance < 0.00001f) return step;
            var direction = step / distance;
            float allowed = distance;
            foreach (var hit in body.SweepTestAll(direction, distance + skin, QueryTriggerInteraction.Ignore))
            {
                // Initial overlaps (distance 0) are left to the solver, so the player can still walk away.
                if (hit.distance <= 0 || !hit.collider.GetComponentInParent<JumpForcePilarArco>()) continue;
                allowed = Mathf.Min(allowed, Mathf.Max(0, hit.distance - skin));
            }
            // Hitting a wall also ends a trampoline or fan slide in that direction.
            if (allowed < distance && externalSpeedX * direction.x > 0) externalSpeedX = 0;
            return direction * allowed;
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
            FindAnyObjectByType<JumpForcePlatformVisibility>()?.RestoreAll();
            body.isKinematic = false;
            Dead = Grounded = ReachedPlatform = false;
            Target = Support = launchPlatform = null;
            CancelCharge();
            body.position = initialPosition;
            body.linearVelocity = Vector3.zero;
            previousFeet = FeetY;
            takeoffGrace = externalSpeedX = 0;
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
