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
        public JumpForceScore score;
        public Collider[] invisibleWalls = System.Array.Empty<Collider>();
        [Header("Deslizar e pular na lateral do pilar")]
        [Min(0)] public float wallAttraction = 3;
        [Min(0)] public float wallSlideDrag = 5;
        [Min(0.1f)] public float wallSlideSpeed = 2;
        [Min(0.01f)] public float wallProbeDistance = 0.1f;
        [Min(0)] public float wallJumpAwaySpeed = 2.5f;
        public bool TouchingPillar => wallContact;
        public Collider PillarContact => wallContact;
        public bool CanWallJump => wallContact && wallJumpReady && takeoffGrace <= 0;
        Collider wallContact;
        bool wallJumpReady;
        float wallNormalX;
        readonly RaycastHit[] contactHits = new RaycastHit[24];
        public JumpForceAnimation animationDriver;
        public Transform visual;
        [Header("Pulo - alturas em metros")]
        [Min(0.1f)] public float minimumJumpHeight = 1.5f;
        [Min(0.1f)] public float maximumJumpHeight = 6.5f;
        [Min(0.01f)] public float fullChargeSeconds = 1;
        public AnimationCurve chargeCurve = AnimationCurve.Linear(0, 0, 1, 1);
        [Min(0.1f)] public float gravityMultiplier = 1.8f;
        [Min(1)] public float maximumFallSpeed = 28;
        [Header("Direcao do salto")]
        [Tooltip("Direcao_Pulo: aparece durante a carga no chao e aponta para onde o salto vai. Rotacao global, so no eixo Z.")]
        public Transform direcaoPulo;
        [Tooltip("Seta dentro de Direcao_Pulo: recebe a cor da camisa e cresce com a carga.")]
        public Renderer seta;
        [Tooltip("Limite da mira para cada lado, em graus, a partir do alto do personagem.")]
        [Range(0, 89)] public float anguloMaximo = 70;
        [Tooltip("Graus por segundo ao girar a mira com setas ou direcional.")]
        [Min(0)] public float velocidadeGiroMira = 120;
        [Tooltip("Escala da seta sem carga (multiplica a escala original da Seta).")]
        [Min(0)] public float tamanhoSetaMinimo = 0.6f;
        [Tooltip("Escala da seta com a carga cheia (multiplica a escala original da Seta).")]
        [Min(0)] public float tamanhoSetaMaximo = 1.2f;
        // Graus: 0 = para cima, positivo = para a direita da tela.
        public float AnguloMira { get; private set; }
        [Header("Toque duplo para carga rapida")]
        [Tooltip("Janela a partir do primeiro pressionamento. Um toque breve aguarda esta janela antes de saltar.")]
        [Min(0)] public float doubleTapWindow = 0.25f;
        [Min(0)] public float quickTapDuration = 0.12f;
        [Min(1)] public float doubleTapChargeMultiplier = 2;
        public float ChargeSpeedMultiplier => chargeMultiplier;
        [Header("Movimento livre")]
        public Camera movementCamera;
        [Min(0)] public float groundSpeed = 3.5f;
        [Min(0)] public float airSpeed = 5;
        [Min(0)] public float visualTurnSpeed = 720;
        [Tooltip("No ar, a parte horizontal do salto mantem a velocidade, mas segue o lado comandado. " +
                 "Segundos para inverter totalmente o sentido com a direcao toda para o outro lado.")]
        [Min(0.01f)] public float tempoViradaNoAr = 0.25f;
        [Tooltip("Abaixo desta velocidade horizontal (m/s) o personagem continua olhando para onde estava.")]
        [Min(0)] public float velocidadeMinimaParaVirar = 0.3f;
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
        float? pendingJumpHeight, deferredTapHeight;
        // Angulo da mira guardado ao soltar o pulo. A parte horizontal do salto dura o voo todo com a mesma
        // velocidade (impulsoSaltoX), mas o sentido (jumpSpeedX) segue a direcao comandada no ar.
        float pendingJumpAngle, deferredTapAngle, jumpSpeedX, impulsoSaltoX;
        // Giro do visual em Y, em graus, a partir de olhar para a camera: +90 = olhando para +X, -90 = para -X.
        float giroVisual;
        Vector3 escalaSetaOriginal = Vector3.one;
        MaterialPropertyBlock blocoSeta;
        static readonly int CorBase = Shader.PropertyToID("_BaseColor"), CorLegada = Shader.PropertyToID("_Color");
        float pressStartedAt, secondTapDeadline, chargeMultiplier = 1;
        JumpForcePlatform launchPlatform;
        Vector3 facing = Vector3.forward;

        Quaternion initialRotation;
        bool initialized;
        public bool GameplayEnabled { get; private set; } = true;
        CollisionDetectionMode gameplayCollisionMode;
        public void SetGameplayEnabled(bool value)
        {
            if (GameplayEnabled == value) return;
            GameplayEnabled = value;
            if (!value)
            {
                CancelCharge();
                MoveAmount = 0;
                body.linearVelocity = Vector3.zero;
                gameplayCollisionMode = body.collisionDetectionMode;
                body.collisionDetectionMode = CollisionDetectionMode.Discrete;
                body.isKinematic = true;
                body.detectCollisions = false;
            }
            else
            {
                body.isKinematic = false;
                body.detectCollisions = true;
                body.collisionDetectionMode = gameplayCollisionMode;
            }
        }

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

            initialRotation = visual ? visual.localRotation : Quaternion.identity;
            previousFeet = FeetY;
            facing = visual ? visual.forward : Vector3.forward;
            if (seta) escalaSetaOriginal = seta.transform.localScale;
            MostrarMira(false);
            initialized = true;
        }
        void Update()
        {
            if (Dead || !GameplayEnabled || !input) return;
            if (input.JumpPressed) BeginCharge();
            if (Charging)
            {
                chargeTime += Time.deltaTime * chargeMultiplier;
                AtualizarMira(Time.deltaTime);
            }
            if (input.JumpReleased) ReleaseJump();
            if (deferredTapHeight.HasValue && Time.time >= secondTapDeadline)
            {
                if (Grounded) { pendingJumpHeight = deferredTapHeight; pendingJumpAngle = deferredTapAngle; }
                deferredTapHeight = null;
            }
        }

        // Segurando o pulo no chao, a direcao mira o salto. Absoluta (joystick, analogico, inclinacao) ou girando
        // (setas, direcional). Soltar a direcao mantem a mira.
        void AtualizarMira(float dt)
        {
            if (!Grounded || !input) return;
            float limite = Mathf.Clamp(anguloMaximo, 0, 89);
            float angulo = input.AimByAngle ? input.AimAngle
                : input.AimAbsolute ? input.Aim * limite : AnguloMira + input.Aim * velocidadeGiroMira * dt;
            AnguloMira = Mathf.Clamp(angulo, -limite, limite);
        }

        void LateUpdate()
        {
            if (!direcaoPulo || !direcaoPulo.gameObject.activeSelf) return;
            // Rotacao global: so Z muda, x e y ficam 0 mesmo com o personagem virado.
            direcaoPulo.rotation = Quaternion.Euler(0, 0, -AnguloMira * DireitaDaTela().x);
            if (seta) seta.transform.localScale = escalaSetaOriginal * Mathf.Lerp(tamanhoSetaMinimo, tamanhoSetaMaximo, Charge01);
        }

        void MostrarMira(bool visivel)
        {
            if (direcaoPulo && direcaoPulo.gameObject.activeSelf != visivel) direcaoPulo.gameObject.SetActive(visivel);
        }

        // Cor da camisa da paleta escolhida (vestiario).
        public void DefinirCorSeta(Color cor)
        {
            if (!seta) return;
            blocoSeta ??= new MaterialPropertyBlock();
            seta.GetPropertyBlock(blocoSeta);
            blocoSeta.SetColor(CorBase, cor);
            blocoSeta.SetColor(CorLegada, cor);
            seta.SetPropertyBlock(blocoSeta);
        }

        Vector3 DireitaDaTela()
        {
            // Screen right mapped onto world X (flipped if the camera looks from the other side).
            var camera = movementCamera ? movementCamera : Camera.main;
            return camera && camera.transform.right.x < 0 ? Vector3.left : Vector3.right;
        }
        // Public commands also serve future UI buttons, upgrades and deterministic validation.
        public void BeginCharge()
        {
            if (!GameplayEnabled || (!Grounded && !CanWallJump) || Dead || Charging || pendingJumpHeight.HasValue) return;
            if (!Grounded && CanWallJump)
            {
                deferredTapHeight = null;
                chargeTime = fullChargeSeconds;
                pendingJumpHeight = Mathf.Max(minimumJumpHeight, maximumJumpHeight);
                return; // Full wall jump on press; no hold/release delay.
            }
            bool secondTap = deferredTapHeight.HasValue && Time.time <= secondTapDeadline;
            deferredTapHeight = null;
            chargeMultiplier = secondTap ? Mathf.Max(1, doubleTapChargeMultiplier) : 1;
            Charging = true;
            chargeTime = 0;
            pressStartedAt = Time.time;
            AnguloMira = 0;
            MostrarMira(true);
            animationDriver?.BeginCharge();
        }
        public void ReleaseJump()
        {
            if (!Charging) return;
            Charging = false;
            MostrarMira(false);
            if ((!Grounded && !CanWallJump) || Dead) { animationDriver?.Land(); return; }
            float factor = Mathf.Clamp01(chargeCurve.Evaluate(Charge01));
            float height = Mathf.Lerp(minimumJumpHeight, Mathf.Max(minimumJumpHeight, maximumJumpHeight), factor);
            if (Grounded && chargeMultiplier == 1 && doubleTapWindow > 0 &&
                Time.time - pressStartedAt <= quickTapDuration && Time.time < pressStartedAt + doubleTapWindow)
            {
                deferredTapHeight = height;
                deferredTapAngle = AnguloMira;
                secondTapDeadline = pressStartedAt + doubleTapWindow;
            }
            else
            {
                pendingJumpHeight = height;
                pendingJumpAngle = AnguloMira;
            }
        }
        public void CancelCharge()
        {
            Charging = false;
            pendingJumpHeight = deferredTapHeight = null;
            chargeMultiplier = 1;
            chargeTime = 0;
            MostrarMira(false);
            animationDriver?.Land();
        }
        // Velocity in m/s, independent of the Rigidbody mass. Used by the trampoline, the fan and other launchers.
        public void Launch(Vector2 velocity)
        {
            if (Dead || !GameplayEnabled) return;
            externalSpeedX = velocity.x;
            jumpSpeedX = impulsoSaltoX = 0;
            if (Grounded)
            {
                // A push too weak to clear the floor during the takeoff grace would sink through it: slide instead.
                if (velocity.y <= Gravity * TakeoffGrace) return;
                Grounded = false;
                Support = null;
            }
            if (Charging || deferredTapHeight.HasValue) CancelCharge();
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
            if (Dead || !GameplayEnabled) return;
            for (int i = 0; i < collision.contactCount; i++)
            {
                var contact = collision.GetContact(i);
                var other = contact.otherCollider;
                if (!other) continue;
                if (other.TryGetComponent(out JumpForceTrampolim trampolim)) { trampolim.Impulsionar(this); return; }
                if (other.TryGetComponent(out JumpForceVentilador ventilador)) { ventilador.Empurrar(this); return; }
            }
        }
        void OnTriggerEnter(Collider other)
        {
            if (Dead || !GameplayEnabled) return;
            if (other.TryGetComponent(out JumpForceCoin coin)) { coin.Collect(this); return; }
            if (other.TryGetComponent(out JumpForceTrampolim trampolim)) trampolim.Impulsionar(this);
            else if (other.TryGetComponent(out JumpForceVentilador ventilador)) ventilador.Empurrar(this);
        }
        void OnApplicationFocus(bool focus) { if (!focus) CancelCharge(); }
        void OnApplicationPause(bool paused) { if (paused) CancelCharge(); }

        void FixedUpdate()
        {
            if (Dead || !GameplayEnabled) return;
#if UNITY_EDITOR
            if (FollowEditorDrag()) return;
#endif
            float dt = Time.fixedDeltaTime;
            body.linearVelocity = Vector3.up * body.linearVelocity.y;
            takeoffGrace = Mathf.Max(0, takeoffGrace - dt);
            // Carry before contact tests. The support has already sampled its new transform.
            if (Grounded && Support && Support.isActiveAndEnabled)
                body.position = ConstrainToWalls(Support.CarryPoint(body.position));
            Physics.SyncTransforms();
            FindSupport();
            ProbePillar();
            if (CanWallJump && input && input.JumpHeld && !pendingJumpHeight.HasValue) BeginCharge();
            if (pendingJumpHeight.HasValue)
            {
                if (Grounded || CanWallJump)
                {
                    if (!Grounded)
                    {
                        wallJumpReady = false;
                        externalSpeedX = wallNormalX * wallJumpAwaySpeed;
                    }
                    launchPlatform = Support;
                    float speed = Mathf.Sqrt(2 * Gravity * pendingJumpHeight.Value);
                    // A carga define a forca total; a mira divide entre altura e distancia. Salto de parede: vertical.
                    float angulo = (Grounded ? pendingJumpAngle : 0) * Mathf.Deg2Rad;
                    body.linearVelocity = Vector3.up * speed * Mathf.Cos(angulo);
                    jumpSpeedX = speed * Mathf.Sin(angulo) * DireitaDaTela().x;
                    impulsoSaltoX = Mathf.Abs(jumpSpeedX);
                    Grounded = false;
                    Support = null;
                    takeoffGrace = TakeoffGrace;
                    animationDriver?.Release();
                    onJump.Invoke();
                    JumpForceEventos.AvisarPulo();
                }
                pendingJumpHeight = null;
                pendingJumpAngle = 0;
            }
            if (!Grounded)
            {
                if (Charging && !CanWallJump) { CancelCharge(); animationDriver?.Release(); }
                SelectTarget();
                MoveHorizontally(dt);
                body.AddForce(Vector3.down * Gravity, ForceMode.Acceleration);
                ApplyWallSlide();
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
        Vector3 ConstrainToWalls(Vector3 position)
        {
            if (JumpForceSpawnedElement.TryGetWallLimits(invisibleWalls, out float left, out float right) &&
                right - left > 2 * playerRadius)
                position.x = Mathf.Clamp(position.x, left + playerRadius + 0.01f, right - playerRadius - 0.01f);
            return position;
        }
        void LockToPlane()
        {
            body.position = ConstrainToWalls(body.position);
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
            float impacto = Mathf.Max(0, -body.linearVelocity.y);
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
                jumpSpeedX = impulsoSaltoX = 0;
                if (!wasGrounded)
                {
                    animationDriver?.Land();
                    JumpForceEventos.AvisarPouso(impacto);
                }
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
            // Keep the held sources intact: airborne steering must remain available while jump is held.
            bool holdOnGround = Grounded && (Charging || (input && input.JumpHeld));
            float command = !holdOnGround && input ? Mathf.Clamp(input.Movement.x, -1, 1) : 0;
            MoveAmount = Mathf.Abs(command);
            Vector3 right = DireitaDaTela();
            Vector3 desired = right * command * (Grounded ? groundSpeed : airSpeed);
            Vector3 help = Vector3.zero;
            if (!Grounded && !wallContact && assistanceEnabled && Target)
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
            // No ar, a parte horizontal do salto mantem a velocidade, mas vira para o lado comandado.
            if (!Grounded && impulsoSaltoX > 0 && MoveAmount > 0.1f)
            {
                float alvo = Mathf.Sign(command) * right.x * impulsoSaltoX;
                jumpSpeedX = Mathf.MoveTowards(jumpSpeedX, alvo, 2 * impulsoSaltoX / tempoViradaNoAr * MoveAmount * dt);
            }
            OlharParaOndeVai(desired.x + externalSpeedX + jumpSpeedX, dt);
            Vector3 posicaoAlvo = (desired + help + Vector3.right * (externalSpeedX + jumpSpeedX)) * dt;
            if (lockZ) posicaoAlvo.z = 0f;
            posicaoAlvo = StopAtObstacles(posicaoAlvo);
            if (posicaoAlvo != Vector3.zero) body.position += posicaoAlvo;
        }
        // Gira so o visual (Direcao_Visual) em Y: a raiz e o corpo fisico nunca giram. A troca de lado passa
        // pela frente (olhando para a camera), sem mostrar as costas.
        void OlharParaOndeVai(float velocidadeX, float dt)
        {
            if (Mathf.Abs(velocidadeX) < velocidadeMinimaParaVirar) return;
            facing = Vector3.right * Mathf.Sign(velocidadeX);
            giroVisual = Mathf.MoveTowards(giroVisual, 90 * Mathf.Sign(velocidadeX), visualTurnSpeed * dt);
            if (visual) visual.localRotation = initialRotation * Quaternion.Euler(0, -giroVisual, 0);
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
            CapsulePoints(out var bottom, out var top);
            int count = Physics.CapsuleCastNonAlloc(bottom, top, playerRadius * 0.95f, direction,
                contactHits, distance + skin, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                var hit = contactHits[i];
                if (hit.collider == capsule || hit.distance <= 0 || hit.collider.GetComponent<JumpForcePlatform>()) continue;
                bool wall = System.Array.IndexOf(invisibleWalls, hit.collider) >= 0;
                if (!wall && !hit.collider.GetComponentInParent<JumpForcePilarArco>()) continue;
                allowed = Mathf.Min(allowed, Mathf.Max(0, hit.distance - skin));
            }
            // Hitting a wall also ends a trampoline or fan slide in that direction.
            if (allowed < distance && externalSpeedX * direction.x > 0) externalSpeedX = 0;
            if (allowed < distance && jumpSpeedX * direction.x > 0) jumpSpeedX = impulsoSaltoX = 0;
            return direction * allowed;
        }
        void CapsulePoints(out Vector3 bottom, out Vector3 top)
        {
            Vector3 center = body.position + Vector3.Scale(capsule.center, transform.lossyScale);
            float half = Mathf.Max(0, capsule.height * transform.lossyScale.y * 0.5f - playerRadius);
            bottom = center - Vector3.up * half;
            top = center + Vector3.up * half;
        }
        void ProbePillar()
        {
            Collider found = null;
            float normal = 0, best = float.PositiveInfinity;
            if (!Grounded)
            {
                CapsulePoints(out var bottom, out var top);
                Vector3 center = (bottom + top) * 0.5f;
                for (int side = -1; side <= 1; side += 2)
                {
                    int count = Physics.SphereCastNonAlloc(center, playerRadius * 0.9f, Vector3.right * side,
                        contactHits, wallProbeDistance + playerRadius * 0.1f, ~0, QueryTriggerInteraction.Ignore);
                    for (int i = 0; i < count; i++)
                    {
                        var hit = contactHits[i];
                        if (!hit.collider || hit.collider == capsule || hit.collider.GetComponent<JumpForcePlatform>() ||
                            !hit.collider.GetComponentInParent<JumpForcePilarArco>() || Mathf.Abs(hit.normal.x) < 0.7f ||
                            center.y >= hit.collider.bounds.max.y - 0.05f || hit.distance >= best) continue;
                        found = hit.collider;
                        normal = Mathf.Sign(hit.normal.x);
                        best = hit.distance;
                    }
                }
            }
            if (found != wallContact) wallJumpReady = found != null;
            wallContact = found;
            wallNormalX = normal;
        }
        void ApplyWallSlide()
        {
            if (!wallContact || takeoffGrace > 0) return;
            // Steering away releases the attraction; contact must actually break to rearm another jump.
            float command = input ? input.Movement.x : 0;
            if (command * wallNormalX > 0.1f) return;
            body.AddForce(Vector3.left * wallNormalX * wallAttraction, ForceMode.Acceleration);
            if (body.linearVelocity.y < 0)
            {
                body.AddForce(Vector3.up * (-body.linearVelocity.y * wallSlideDrag), ForceMode.Acceleration);
                var velocity = body.linearVelocity;
                velocity.y = Mathf.Max(velocity.y, -wallSlideSpeed);
                body.linearVelocity = velocity;
            }
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
            if (Dead || !GameplayEnabled) return;
            CancelCharge();
            score?.ObserveHeight(body.position.y);
            Dead = true;
            jumpSpeedX = impulsoSaltoX = 0;
            body.linearVelocity = Vector3.zero;
            body.isKinematic = true;
            onDeath.Invoke();
            JumpForceEventos.AvisarMorte();
            body.position = Vector3.zero;
            transform.position = Vector3.zero;
            Grounded = false;
            Target = Support = launchPlatform = null;
            wallContact = null;
            wallJumpReady = false;
        }
        public void Restart()
        {
            FindAnyObjectByType<JumpForcePlatformVisibility>()?.RestoreAll();
            body.isKinematic = false;
            Dead = Grounded = ReachedPlatform = false;
            Target = Support = launchPlatform = null;
            CancelCharge();
            body.position = Vector3.zero;
            transform.position = Vector3.zero;
            planeZ = 0;
            wallContact = null;
            wallJumpReady = false;
            body.linearVelocity = Vector3.zero;
            previousFeet = FeetY;
            takeoffGrace = externalSpeedX = jumpSpeedX = impulsoSaltoX = giroVisual = AnguloMira = 0;
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
