using UnityEngine;
using UnityEngine.Events;

namespace Lumera.JumpForce
{
    [DefaultExecutionOrder(-100), DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody), typeof(CapsuleCollider), typeof(JumpForceInput))]
    public sealed class JumpForcePlayer : MonoBehaviour
    {
        public enum CategoriaPulo
        {
            Indisponivel,
            Ajustavel,
            LateralPilar,
            VentiladorGiratorio
        }

        [Header("Referencias")]
        public JumpForceInput input;
        public JumpForceScore score;
        public JumpForceAnimation animationDriver;
        public Transform visual;

        [Header("Deslizar e pular na lateral do pilar")]
        [Min(0)] public float wallAttraction = 3f;
        [Min(0)] public float wallSlideDrag = 5f;
        [Min(0.1f)] public float wallSlideSpeed = 2f;
        [Tooltip("Tempo minimo sem colisao com o pilar de origem ao saltar pela lateral. A colisao volta ao sair dele, sem reativar dentro da malha.")]
        [Min(0)] public float wallJumpCollisionGrace = 0.15f;
        public bool TouchingPillar => wallContact;
        public Collider PillarContact => wallContact;
        public bool CanWallJump => wallContact && wallJumpReady && takeoffGrace <= 0f;

        [Header("Pulo - alturas em metros")]
        [Min(0.1f)] public float minimumJumpHeight = 1.5f;
        [Min(0.1f)] public float maximumJumpHeight = 6.5f;
        [Min(0.01f)] public float fullChargeSeconds = 1f;
        public AnimationCurve chargeCurve = AnimationCurve.Linear(0, 0, 1, 1);
        [Min(0.1f)] public float gravityMultiplier = 1.8f;
        [Min(1f)] public float maximumFallSpeed = 28f;

        [Header("Pulos instantaneos por categoria")]
        [UnityEngine.Serialization.FormerlySerializedAs("proporcaoPuloInstantaneo")]
        [Tooltip("Fracao do impulso maximo ao pular pela lateral do pilar.")]
        [Range(0.1f, 1f)] public float proporcaoPuloPilar = 0.6f;
        [Tooltip("Fracao do impulso maximo ao pular da base do ventilador giratorio. No topo volta o pulo ajustavel.")]
        [Range(0.1f, 1f)] public float proporcaoPuloVentiladorGiratorio = 0.8f;

        [Header("Direcao do salto")]
        [Tooltip("Direcao_Pulo: aparece durante a carga no chao e aponta para onde o salto vai. Rotacao global, so no eixo Z.")]
        public Transform direcaoPulo;
        [Tooltip("Seta dentro de Direcao_Pulo: recebe a cor da camisa e cresce com a carga.")]
        public Renderer seta;
        [Tooltip("Limite da mira para cada lado, em graus, a partir do alto do personagem.")]
        [Range(0, 89)] public float anguloMaximo = 70f;
        [Tooltip("Graus para cada lado do topo tratados como salto vertical, sem impulso horizontal.")]
        [Range(0, 30)] public float margemPuloVertical = 5f;
        [Tooltip("Velocidade horizontal maxima em relacao ao apoio para considerar o jogador parado.")]
        [Min(0)] public float toleranciaParado = 0.15f;
        [Min(0)] public float tamanhoSetaMinimo = 0.6f;
        [Min(0)] public float tamanhoSetaMaximo = 1.2f;
        public float AnguloMira { get; private set; }

        [Header("Toque duplo para carga rapida")]
        [Min(0)] public float doubleTapWindow = 0.25f;
        [Min(0)] public float quickTapDuration = 0.12f;
        [Min(1)] public float doubleTapChargeMultiplier = 2f;
        public float ChargeSpeedMultiplier => chargeMultiplier;

        [Header("Movimento horizontal fisico")]
        public Camera movementCamera;
        [Min(0)] public float groundSpeed = 3.5f;
        [Min(0)] public float airSpeed = 5f;
        [Tooltip("Aceleracao do andar no chao, ate a velocidade relativa desejada. Parar e escorregar sao do atrito " +
                 "(Physics Material do jogador e do chao), nao do codigo.")]
        [Min(0)] public float groundAcceleration = 28f;
        [Tooltip("Ao pousar, a velocidade horizontal em relacao ao apoio fica limitada a de andar (Ground Speed), " +
                 "para um salto longo nao escorregar para fora da plataforma. Impulso unico no pouso, nao atrito.")]
        public bool pousoFirme = true;
        [Tooltip("Segundos aproximados para inverter totalmente o movimento controlavel no ar.")]
        [Min(0.01f)] public float tempoViradaNoAr = 0.25f;
        [Min(0)] public float visualTurnSpeed = 720f;
        [Min(0)] public float velocidadeMinimaParaVirar = 0.3f;

        [Header("Plano 2D - so X e Y")]
        public bool lockZ = true;

        [Header("Ajuda de plataforma - somente no ar")]
        public bool assistanceEnabled = true;
        [Min(0)] public float assistanceRange = 2.5f;
        [Tooltip("Aceleracao horizontal para afastar o personagem quando ele esta abaixo da plataforma.")]
        [Min(0)] public float repulsionAcceleration = 8f;
        [Tooltip("Aceleracao horizontal para aproximar o personagem do centro quando ele esta acima da plataforma.")]
        [Min(0)] public float attractionAcceleration = 6f;
        [Range(0, 1)] public float assistanceWhileSteering = 0.15f;
        public float MoveAmount { get; private set; }

        [Header("Escolha de plataforma")]
        [Min(0.1f)] public float searchRadius = 8f;
        [Min(0)] public float verticalDistanceWeight = 0.5f;
        [Min(0)] public float facingPreference = 1.4f;
        [Min(0)] public float targetHysteresis = 1.2f;

        [Header("Contato")]
        [Min(0.001f)] public float contactTolerance = 0.06f;
        [Tooltip("Mudanca minima de velocidade do suporte para sincronizar uma frenagem brusca.")]

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
        public bool GameplayEnabled { get; private set; } = true;

        readonly System.Collections.Generic.HashSet<JumpForcePlatform> landingSurfaces = new();

        public bool ParadoNoChao => Grounded && body &&
            Mathf.Abs(body.linearVelocity.x - (Support ? Support.Velocity.x : 0f)) <= toleranciaParado;
        public bool PodePular => GameplayEnabled && !Dead && takeoffGrace <= 0f &&
            !pendingJumpHeight.HasValue && (ParadoNoChao || CanWallJump);
        public CategoriaPulo CategoriaPuloAtual
        {
            get
            {
                if (!PodePular) return CategoriaPulo.Indisponivel;
                if (ParadoNoChao)
                    return Support && Support.instantJump
                        ? CategoriaPulo.VentiladorGiratorio : CategoriaPulo.Ajustavel;
                return !Grounded && CanWallJump
                    ? CategoriaPulo.LateralPilar : CategoriaPulo.Indisponivel;
            }
        }
        public bool JoystickPuloDisponivel => CategoriaPuloAtual == CategoriaPulo.Ajustavel;
        public bool PuloSimplesDisponivel => CategoriaPuloAtual == CategoriaPulo.LateralPilar ||
            CategoriaPuloAtual == CategoriaPulo.VentiladorGiratorio;

        Rigidbody body;
        CapsuleCollider capsule;
        float feetOffset;
        float chargeTime;
        float takeoffGrace;
        float? pendingJumpHeight;
        float? deferredTapHeight;
        float pendingJumpAngle;
        float deferredTapAngle;
        float pressStartedAt;
        float secondTapDeadline;
        float chargeMultiplier = 1f;
        float giroVisual;
        float previousVerticalSpeed;

        readonly System.Collections.Generic.List<Collider> ignoredPillarColliders = new();
        readonly System.Collections.Generic.List<bool> previousPillarIgnores = new();
        float pillarCollisionGrace;
        Collider wallContact;
        bool wallJumpReady;
        float wallNormalX;
        readonly ContactPoint[] physicsContacts = new ContactPoint[24];
        int physicsContactCount;

        JumpForcePlatform launchPlatform;
        JumpForcePlatform supportVelocitySource;
        Vector3 previousSupportVelocity;
        Vector3 facing = Vector3.forward;

        Quaternion initialRotation;
        Vector3 escalaSetaOriginal = Vector3.one;
        MaterialPropertyBlock blocoSeta;
        static readonly int CorBase = Shader.PropertyToID("_BaseColor");
        static readonly int CorLegada = Shader.PropertyToID("_Color");
        CollisionDetectionMode gameplayCollisionMode;
        bool initialized;

        const float TakeoffGrace = 0.12f;

        float Gravity => Mathf.Max(0.1f, -Physics.gravity.y * gravityMultiplier);

        public void SetGameplayEnabled(bool value)
        {
            if (GameplayEnabled == value)
                return;

            GameplayEnabled = value;

            if (!value)
            {
                RestorePillarCollision();
                CancelCharge();
                MoveAmount = 0f;
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

            if (!input)
                input = GetComponent<JumpForceInput>();

            if (!animationDriver)
                animationDriver = GetComponent<JumpForceAnimation>();

            feetOffset =
                capsule.center.y * transform.lossyScale.y -
                capsule.height * transform.lossyScale.y * 0.5f;

            body.constraints =
                RigidbodyConstraints.FreezeRotation |
                (lockZ ? RigidbodyConstraints.FreezePositionZ : RigidbodyConstraints.None);

            body.useGravity = true;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

            initialRotation = visual ? visual.localRotation : Quaternion.identity;
            facing = visual ? visual.forward : Vector3.forward;

            if (seta)
                escalaSetaOriginal = seta.transform.localScale;

            previousVerticalSpeed = body.linearVelocity.y;
            MostrarMira(false);
            initialized = true;
        }

        void Update()
        {
            if (Dead || !GameplayEnabled || !input)
                return;

            if (input.JumpPressed)
                BeginCharge();

            if (Charging)
            {
                float teto = input.AimPadInDeadZone
                    ? 0f
                    : Mathf.Clamp01(input.AimPadStrength);

                chargeTime = Mathf.Min(
                    chargeTime + Time.deltaTime * chargeMultiplier,
                    teto * fullChargeSeconds
                );

                AtualizarMira();
                MostrarMira(!input.AimPadInDeadZone);
            }

            if (input.JumpReleased)
                ReleaseJump();

            if (deferredTapHeight.HasValue && Time.time >= secondTapDeadline)
            {
                if (ParadoNoChao)
                {
                    pendingJumpHeight = deferredTapHeight;
                    pendingJumpAngle = deferredTapAngle;
                }

                deferredTapHeight = null;
            }
        }

        void AtualizarMira()
        {
            if (!Grounded || !input || input.AimPadInDeadZone)
                return;

            float limite = Mathf.Clamp(anguloMaximo, 0f, 89f);
            Vector2 direcao = input.AimPadDirection;

            AnguloMira = AjustarAnguloPulo(Mathf.Clamp(
                Mathf.Atan2(direcao.x, direcao.y) * Mathf.Rad2Deg,
                -limite,
                limite
            ));
        }

        public float AjustarAnguloPulo(float angulo)
        {
            angulo = Mathf.Clamp(angulo, -anguloMaximo, anguloMaximo);
            return Mathf.Abs(angulo) <= margemPuloVertical ? 0f : angulo;
        }

        void LateUpdate()
        {
            if (!direcaoPulo || !direcaoPulo.gameObject.activeSelf)
                return;

            direcaoPulo.rotation =
                Quaternion.Euler(0f, 0f, -AnguloMira * DireitaDaTela().x);

            if (seta)
            {
                seta.transform.localScale =
                    escalaSetaOriginal *
                    Mathf.Lerp(tamanhoSetaMinimo, tamanhoSetaMaximo, Charge01);
            }
        }

        void MostrarMira(bool visivel)
        {
            if (direcaoPulo && direcaoPulo.gameObject.activeSelf != visivel)
                direcaoPulo.gameObject.SetActive(visivel);
        }

        public void DefinirCorSeta(Color cor)
        {
            if (!seta)
                return;

            blocoSeta ??= new MaterialPropertyBlock();
            seta.GetPropertyBlock(blocoSeta);
            blocoSeta.SetColor(CorBase, cor);
            blocoSeta.SetColor(CorLegada, cor);
            seta.SetPropertyBlock(blocoSeta);
        }

        Vector3 DireitaDaTela()
        {
            Camera camera = movementCamera ? movementCamera : Camera.main;
            return camera && camera.transform.right.x < 0f
                ? Vector3.left
                : Vector3.right;
        }

        public void BeginCharge()
        {
            if (
                !GameplayEnabled ||
                !PodePular ||
                Dead ||
                Charging ||
                pendingJumpHeight.HasValue
            )
                return;

            if (PuloSimplesDisponivel)
            {
                PularInstantaneamente();
                return;
            }

            bool secondTap =
                deferredTapHeight.HasValue &&
                Time.time <= secondTapDeadline;

            deferredTapHeight = null;
            chargeMultiplier = secondTap
                ? Mathf.Max(1f, doubleTapChargeMultiplier)
                : 1f;

            Charging = true;
            chargeTime = 0f;
            pressStartedAt = Time.time;
            AnguloMira = 0f;
            MostrarMira(false);
            animationDriver?.BeginCharge();
        }

        public void PularInstantaneamente()
        {
            if (!GameplayEnabled || Dead || !PuloSimplesDisponivel ||
                Charging || pendingJumpHeight.HasValue)
                return;

            deferredTapHeight = null;
            float proporcao = Mathf.Clamp(
                CategoriaPuloAtual == CategoriaPulo.VentiladorGiratorio
                    ? proporcaoPuloVentiladorGiratorio : proporcaoPuloPilar, 0.1f, 1f);
            // v = sqrt(2gh): escalar a altura por p^2 aplica p do impulso.
            pendingJumpHeight = Mathf.Max(minimumJumpHeight, maximumJumpHeight) *
                proporcao * proporcao;
            pendingJumpAngle = 0f;
            AnguloMira = 0f;
            MostrarMira(false);
        }

        public void ReleaseJump()
        {
            if (!Charging)
                return;

            Charging = false;
            MostrarMira(false);

            if (!PodePular || Dead)
            {
                animationDriver?.Land();
                return;
            }

            if (input && input.AimPadInDeadZone)
            {
                chargeTime = 0f;
                animationDriver?.Land();
                return;
            }

            float factor = Mathf.Clamp01(chargeCurve.Evaluate(Charge01));
            float height = Mathf.Lerp(
                minimumJumpHeight,
                Mathf.Max(minimumJumpHeight, maximumJumpHeight),
                factor
            );

            if (
                Grounded &&
                chargeMultiplier == 1f &&
                doubleTapWindow > 0f &&
                Time.time - pressStartedAt <= quickTapDuration &&
                Time.time < pressStartedAt + doubleTapWindow
            )
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
            pendingJumpHeight = null;
            deferredTapHeight = null;
            chargeMultiplier = 1f;
            chargeTime = 0f;
            MostrarMira(false);
            animationDriver?.Land();
        }

        // Impulso fisico no Rigidbody (ForceMode.VelocityChange). O valor e a velocidade de saida em m/s ao longo
        // da direcao dele: a parte da velocidade atual nessa direcao e substituida (uma queda nao enfraquece o
        // trampolim; quem ja vai mais rapido no mesmo sentido nao e freado). O resto da velocidade continua.
        public void Launch(Vector2 impulso)
        {
            if (Dead || !GameplayEnabled || impulso == Vector2.zero)
                return;

            if (Charging || deferredTapHeight.HasValue)
                CancelCharge();

            pendingJumpHeight = null;
            pendingJumpAngle = 0f;
            launchPlatform = null;

            body.AddForce(
                new Vector3(impulso.x, impulso.y, 0f),
                ForceMode.VelocityChange
            );

            // Se o impulso possuir componente vertical, deixa de estar apoiado.
            if (Mathf.Abs(impulso.y) > 0.01f)
            {
                Grounded = false;
                Support = null;
                ResetSupportVelocityTracking();

                if (impulso.y > 0f)
                    takeoffGrace = TakeoffGrace;

                animationDriver?.Release();
            }
        }

        void OnCollisionEnter(Collision collision)
        {
            RememberContacts(collision);
            if (Dead || !GameplayEnabled)
                return;

            for (int i = 0; i < collision.contactCount; i++)
            {
                ContactPoint contact = collision.GetContact(i);
                Collider other = contact.otherCollider;

                if (!other)
                    continue;

                if (other.TryGetComponent(out JumpForceTrampolim trampolim))
                {
                    trampolim.Impulsionar(this);
                    return;
                }

                if (other.TryGetComponent(out JumpForceVentilador ventilador))
                {
                    ventilador.Empurrar(this);
                    return;
                }
            }
        }

        void OnCollisionStay(Collision collision) => RememberContacts(collision);
        void OnCollisionExit(Collision collision) => ForgetContacts(collision);

        void ForgetContacts(Collision collision)
        {
            // One collision pair may contain several colliders on a compound Rigidbody.
            for (int i = physicsContactCount - 1; i >= 0; i--)
            {
                GetContact(i, out Collider other, out _);
                if (!other || other == collision.collider ||
                    (collision.rigidbody && other.attachedRigidbody == collision.rigidbody))
                    physicsContacts[i] = physicsContacts[--physicsContactCount];
            }
        }

        void RememberContacts(Collision collision)
        {
            ForgetContacts(collision);
            for (int i = 0; i < collision.contactCount && physicsContactCount < physicsContacts.Length; i++)
                physicsContacts[physicsContactCount++] = collision.GetContact(i);
        }

        void OnTriggerEnter(Collider other)
        {
            if (Dead || !GameplayEnabled)
                return;

            if (other.TryGetComponent(out JumpForceCoin coin))
            {
                coin.Collect(this);
                return;
            }

            if (other.TryGetComponent(out JumpForceTrampolim trampolim))
                trampolim.Impulsionar(this);
            else if (other.TryGetComponent(out JumpForceVentilador ventilador))
                ventilador.Empurrar(this);
        }

        void OnApplicationFocus(bool focus)
        {
            if (!focus)
                CancelCharge();
        }

        void OnApplicationPause(bool paused)
        {
            if (paused)
                CancelCharge();
        }

        void FixedUpdate()
        {
            if (Dead || !GameplayEnabled)
                return;

#if UNITY_EDITOR
            if (FollowEditorDrag())
                return;
#endif

            float dt = Time.fixedDeltaTime;
            UpdatePillarCollisionRelease(dt);
            takeoffGrace = Mathf.Max(0f, takeoffGrace - dt);

            FindSupport();
            SyncSupportBrake();
            ProbePillar();

            if (
                CanWallJump &&
                input &&
                input.JumpHeld &&
                !pendingJumpHeight.HasValue
            )
            {
                BeginCharge();
            }

            if (pendingJumpHeight.HasValue)
            {
                if (ParadoNoChao || CanWallJump)
                {
                    bool wallJump = !Grounded;
                    launchPlatform = Support;

                    float speed = Mathf.Sqrt(
                        2f * Gravity * pendingJumpHeight.Value
                    );

                    float angle =
                        (Grounded ? AjustarAnguloPulo(pendingJumpAngle) : 0f) * Mathf.Deg2Rad;

                    float verticalImpulse = speed * Mathf.Cos(angle);
                    float horizontalImpulse;

                    if (wallJump)
                    {
                        ReleasePillarCollision();
                        wallJumpReady = false;
                        horizontalImpulse = 0f;
                    }
                    else
                    {
                        horizontalImpulse =
                            speed *
                            Mathf.Sin(angle) *
                            DireitaDaTela().x;
                    }

                    // A subida sai cheia em relacao ao apoio (plataforma ou pilar que sobe ou desce), tambem
                    // no salto de parede, em que o jogador estava deslizando para baixo.
                    float apoioY = Grounded && Support ? Support.Velocity.y : 0f;
                    verticalImpulse += apoioY - body.linearVelocity.y;

                    body.AddForce(
                        new Vector3(horizontalImpulse, verticalImpulse, 0f),
                        ForceMode.VelocityChange
                    );

                    Grounded = false;
                    Support = null;
                    ResetSupportVelocityTracking();
                    takeoffGrace = TakeoffGrace;

                    animationDriver?.Release();
                    onJump.Invoke();
                    JumpForceEventos.AvisarPulo();
                }

                pendingJumpHeight = null;
                pendingJumpAngle = 0f;
            }

            if (!Grounded)
            {
                if (Charging && !CanWallJump)
                {
                    CancelCharge();
                    animationDriver?.Release();
                }

                SelectTarget();
            }
            else
            {
                Target = null;
            }

            MoveHorizontally(dt);

            // A gravidade base vem do Rigidbody; o multiplicador excedente vale sempre, tambem no chao, para o
            // peso (e o atrito do Physics Material) ser o mesmo que o salto usa.
            if (Mathf.Abs(gravityMultiplier - 1f) > 0.0001f)
            {
                body.AddForce(
                    Physics.gravity * (gravityMultiplier - 1f),
                    ForceMode.Acceleration
                );
            }

            if (!Grounded)
                ApplyWallSlide();

            if (body.linearVelocity.y < -maximumFallSpeed)
            {
                Vector3 velocity = body.linearVelocity;
                velocity.y = -maximumFallSpeed;
                body.linearVelocity = velocity;
            }

            UpdateCollisions();
            previousVerticalSpeed = body.linearVelocity.y;
        }

#if UNITY_EDITOR
        bool editorDragging;

        bool FollowEditorDrag()
        {
            bool dragging =
                GUIUtility.hotControl != 0 &&
                UnityEditor.EditorWindow.focusedWindow is UnityEditor.SceneView &&
                UnityEditor.Selection.Contains(gameObject);

            if (dragging != editorDragging)
            {
                editorDragging = dragging;
                body.interpolation = dragging
                    ? RigidbodyInterpolation.None
                    : RigidbodyInterpolation.Interpolate;

                if (!dragging)
                {
                    Grounded = false;
                    Support = null;
                    ResetSupportVelocityTracking();
                }
            }

            if (!dragging)
                return false;

            body.position = transform.position;
            body.rotation = transform.rotation;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            previousVerticalSpeed = 0f;
            return true;
        }
#endif

        void FindSupport()
        {
            JumpForcePlatform found = null;
            float bestTop = float.NegativeInfinity;

            if (takeoffGrace <= 0f)
            {
                for (int i = 0; i < physicsContactCount; i++)
                {
                    GetContact(i, out Collider other, out Vector3 normal);
                    if (!other || normal.y < 0.7f) continue;
                    var platform = other.GetComponent<JumpForcePlatform>();
                    if (!platform)
                    {
                        var pillar = other.GetComponentInParent<JumpForcePilarArco>();
                        if (pillar) platform = pillar.TopSurface;
                    }
                    if (!platform || !platform.Surface || !platform.Surface.enabled ||
                        platform.Surface.isTrigger || body.linearVelocity.y - platform.Velocity.y > 0.5f)
                        continue;
                    float top = physicsContacts[i].point.y;
                    if (top > bestTop) { found = platform; bestTop = top; }
                }
            }

            bool wasGrounded = Grounded;
            Grounded = found != null;
            Support = found;

            if (found)
            {
                Target = null;
                launchPlatform = null;

                if (!wasGrounded)
                {
                    if (pousoFirme && !found.escorregadia)
                    {
                        float relativo = body.linearVelocity.x - found.Velocity.x;
                        float limitado = Mathf.Clamp(relativo, -groundSpeed, groundSpeed);
                        if (!Mathf.Approximately(relativo, limitado))
                            body.AddForce(Vector3.right * (limitado - relativo), ForceMode.VelocityChange);
                    }

                    animationDriver?.Land();
                    JumpForceEventos.AvisarPouso(Mathf.Max(0f, -previousVerticalSpeed));
                }

                if (!found.startingGround && !ReachedPlatform)
                {
                    ReachedPlatform = true;
                    onFirstPlatform.Invoke();
                }
            }
            else
            {
                if (wasGrounded)
                    animationDriver?.Release();

                ResetSupportVelocityTracking();
            }
        }

        void SyncSupportBrake()
        {
            if (!Grounded || !Support || !Support.Body || Support.escorregadia)
            {
                ResetSupportVelocityTracking();
                return;
            }

            Vector3 currentVelocity = Support.Velocity;

            if (supportVelocitySource != Support)
            {
                supportVelocitySource = Support;
                previousSupportVelocity = currentVelocity;
                return;
            }

            Vector3 delta =
                currentVelocity - previousSupportVelocity;

            bool braking =
                Vector3.Dot(previousSupportVelocity, delta) < 0f ||
                Vector3.Dot(previousSupportVelocity, currentVelocity) < 0f;

            if (braking && delta.sqrMagnitude > 0.000001f)
            {
                body.AddForce(
                    delta,
                    ForceMode.VelocityChange
                );
            }

            previousSupportVelocity = currentVelocity;
        }

        void ResetSupportVelocityTracking()
        {
            supportVelocitySource = null;
            previousSupportVelocity = Vector3.zero;
        }

        void SelectTarget()
        {
            JumpForcePlatform best = null;
            float bestScore = float.PositiveInfinity;

            float apex =
                FeetY +
                Mathf.Pow(Mathf.Max(0f, body.linearVelocity.y), 2f) /
                (2f * Gravity);

            foreach (JumpForcePlatform platform in JumpForcePlatform.Active)
            {
                if (
                    !platform ||
                    platform.startingGround ||
                    !platform.Surface ||
                    !platform.Surface.enabled ||
                    platform.Surface.isTrigger ||
                    ignoredPillarColliders.Contains(platform.Surface)
                )
                    continue;

                if (platform == launchPlatform && body.linearVelocity.y > 0f)
                    continue;

                if (platform.Top > apex + contactTolerance)
                    continue;

                Vector3 delta = platform.Center - body.position;

                if (lockZ)
                    delta.z = 0f;

                float horizontal = new Vector2(delta.x, delta.z).magnitude;

                if (
                    horizontal > searchRadius ||
                    Mathf.Abs(platform.Top - FeetY) > searchRadius
                )
                    continue;

                delta.y = 0f;

                float alignment =
                    delta.sqrMagnitude > 0.01f
                        ? Vector3.Dot(facing, delta.normalized)
                        : 0f;

                float scoreValue =
                    horizontal +
                    Mathf.Abs(platform.Top - FeetY) * verticalDistanceWeight -
                    alignment * facingPreference;

                if (platform == Target)
                    scoreValue -= targetHysteresis;

                if (scoreValue < bestScore)
                {
                    bestScore = scoreValue;
                    best = platform;
                }
            }

            Target = best;
        }

        void MoveHorizontally(float dt)
        {
            bool holdOnGround =
                Grounded &&
                (Charging || (input && input.JumpHeld));

            float command =
                !holdOnGround && input
                    ? Mathf.Clamp(input.Movement.x, -1f, 1f)
                    : 0f;

            MoveAmount = Mathf.Abs(command);

            float screenDirection = DireitaDaTela().x;
            float supportSpeedX =
                Grounded && Support
                    ? Support.Velocity.x
                    : 0f;

            float relativeSpeedX =
                body.linearVelocity.x - supportSpeedX;

            if (Grounded)
            {
                // So o comando gera forca. Sem comando, ou ja mais rapido no mesmo sentido, quem freia e o atrito.
                float desiredRelativeSpeed =
                    command * groundSpeed * screenDirection;

                float acceleration = groundAcceleration;

                float speedError =
                    desiredRelativeSpeed - relativeSpeedX;

                bool alreadyFaster =
                    relativeSpeedX * desiredRelativeSpeed > 0f &&
                    Mathf.Abs(relativeSpeedX) >= Mathf.Abs(desiredRelativeSpeed);

                if (Mathf.Abs(command) > 0.01f && !alreadyFaster && Mathf.Abs(speedError) > 0.001f && acceleration > 0f)
                {
                    float requestedAcceleration =
                        Mathf.Clamp(
                            speedError / Mathf.Max(dt, 0.0001f),
                            -acceleration,
                            acceleration
                        );

                    body.AddForce(
                        Vector3.right * requestedAcceleration,
                        ForceMode.Acceleration
                    );
                }
            }
            else if (Mathf.Abs(command) > 0.01f)
            {
                float desiredDirection =
                    Mathf.Sign(command * screenDirection);

                float acceleration =
                    2f * Mathf.Max(0.01f, airSpeed) /
                    Mathf.Max(0.01f, tempoViradaNoAr);

                // Acima de airSpeed, o controle nao apaga impulso externo no mesmo sentido.
                // Para inverter, a aceleracao continua agindo naturalmente contra a inercia.
                bool alreadyFasterSameDirection =
                    Mathf.Sign(relativeSpeedX) == desiredDirection &&
                    Mathf.Abs(relativeSpeedX) >= airSpeed;

                // Encostado no pilar e empurrando para dentro dele: a forca so empurraria o pilar.
                bool pushingIntoPillar =
                    wallContact && desiredDirection * wallNormalX < 0f;

                if (!alreadyFasterSameDirection && !pushingIntoPillar)
                {
                    body.AddForce(
                        Vector3.right * desiredDirection * acceleration * Mathf.Abs(command),
                        ForceMode.Acceleration
                    );
                }
            }

            ApplyPlatformAssistance(command);

            float visualVelocity = Grounded
                ? body.linearVelocity.x - supportSpeedX
                : body.linearVelocity.x;

            OlharParaOndeVai(visualVelocity, dt);
        }

        void ApplyPlatformAssistance(float command)
        {
            if (
                Grounded ||
                wallContact ||
                !assistanceEnabled ||
                !Target
            )
                return;

            Vector3 radial = body.position - Target.Center;
            radial.y = 0f;

            if (lockZ)
                radial.z = 0f;

            float radius = radial.magnitude;

            if (radius > assistanceRange || radius <= 0.0001f)
                return;

            Vector3 outward = radial / radius;
            Vector3 acceleration;

            if (FeetY < Target.Top)
            {
                // Abaixo da plataforma: afasta para ajudar a contornar a lateral.
                acceleration = outward * repulsionAcceleration;
            }
            else
            {
                // Acima da plataforma: aproxima do centro para ajudar o pouso.
                acceleration = -outward * attractionAcceleration;
            }

            if (Mathf.Abs(command) > 0.01f)
            {
                acceleration *= assistanceWhileSteering;

                Vector3 commandedDirection =
                    Vector3.right * Mathf.Sign(command * DireitaDaTela().x);

                float opposing = Vector3.Dot(acceleration, commandedDirection);

                if (opposing < 0f)
                    acceleration -= commandedDirection * opposing;
            }

            body.AddForce(acceleration, ForceMode.Acceleration);
        }

        void OlharParaOndeVai(float velocidadeX, float dt)
        {
            if (Mathf.Abs(velocidadeX) < velocidadeMinimaParaVirar)
                return;

            facing = Vector3.right * Mathf.Sign(velocidadeX);

            giroVisual = Mathf.MoveTowards(
                giroVisual,
                90f * Mathf.Sign(velocidadeX),
                visualTurnSpeed * dt
            );

            if (visual)
            {
                visual.localRotation =
                    initialRotation * Quaternion.Euler(0f, -giroVisual, 0f);
            }
        }

        void GetContact(int index, out Collider other, out Vector3 normal)
        {
            ContactPoint contact = physicsContacts[index];
            other = contact.otherCollider;
            normal = contact.normal;
            if (other == capsule)
            {
                other = contact.thisCollider;
                normal = -normal;
            }
            if (other && (!other.enabled || !other.gameObject.activeInHierarchy || ignoredPillarColliders.Contains(other))) other = null;
        }

        void ProbePillar()
        {
            Collider found = null;
            float normalX = 0f;
            if (!Grounded)
            {
                for (int i = 0; i < physicsContactCount; i++)
                {
                    GetContact(i, out Collider other, out Vector3 normal);
                    if (!other || Mathf.Abs(normal.x) < 0.7f ||
                        !other.GetComponentInParent<JumpForcePilarArco>())
                        continue;
                    found = other;
                    normalX = Mathf.Sign(normal.x);
                    break;
                }
            }

            if (found != wallContact)
                wallJumpReady = found != null;
            wallContact = found;
            wallNormalX = normalX;
        }

        void ReleasePillarCollision()
        {
            var pillar = wallContact ? wallContact.GetComponentInParent<JumpForcePilarArco>() : null;
            if (!pillar) return;
            RestorePillarCollision();
            pillar.GetComponentsInChildren(false, ignoredPillarColliders);
            for (int i = ignoredPillarColliders.Count - 1; i >= 0; i--)
                if (!ignoredPillarColliders[i].enabled || ignoredPillarColliders[i].isTrigger)
                    ignoredPillarColliders.RemoveAt(i);
            foreach (var collider in ignoredPillarColliders)
            {
                previousPillarIgnores.Add(Physics.GetIgnoreCollision(capsule, collider));
                Physics.IgnoreCollision(capsule, collider, true);
            }
            pillarCollisionGrace = wallJumpCollisionGrace;
            physicsContactCount = 0;
            wallContact = null;
            wallNormalX = 0f;
        }

        void UpdatePillarCollisionRelease(float dt)
        {
            if (ignoredPillarColliders.Count == 0) return;
            pillarCollisionGrace -= dt;
            if (pillarCollisionGrace > 0f) return;
            bool any = false, penetrating = false;
            Bounds bounds = default;
            foreach (var collider in ignoredPillarColliders)
            {
                if (!collider || !collider.enabled || !collider.gameObject.activeInHierarchy) continue;
                if (!any) bounds = collider.bounds; else bounds.Encapsulate(collider.bounds);
                any = true;
                penetrating |= Physics.ComputePenetration(capsule, body.position, body.rotation,
                    collider, collider.transform.position, collider.transform.rotation, out _, out _);
            }
            // Stay released during ascent alongside/inside the arch. Re-enable only in free space.
            bool clear = !any || !bounds.Intersects(capsule.bounds) || FeetY > bounds.max.y + contactTolerance;
            if (!penetrating && (clear || body.linearVelocity.y <= 0f))
                RestorePillarCollision();
        }

        void RestorePillarCollision()
        {
            if (capsule)
                for (int i = 0; i < ignoredPillarColliders.Count; i++)
                    if (ignoredPillarColliders[i])
                        Physics.IgnoreCollision(capsule, ignoredPillarColliders[i], previousPillarIgnores[i]);
            ignoredPillarColliders.Clear();
            previousPillarIgnores.Clear();
        }

        void ApplyWallSlide()
        {
            if (!wallContact || takeoffGrace > 0f)
                return;

            float command = input ? input.Movement.x : 0f;

            if (command * wallNormalX > 0.1f)
                return;

            body.AddForce(
                Vector3.left * wallNormalX * wallAttraction,
                ForceMode.Acceleration
            );

            if (body.linearVelocity.y < 0f)
            {
                body.AddForce(
                    Vector3.up * (-body.linearVelocity.y * wallSlideDrag),
                    ForceMode.Acceleration
                );

                Vector3 velocity = body.linearVelocity;
                velocity.y = Mathf.Max(velocity.y, -wallSlideSpeed);
                body.linearVelocity = velocity;
            }
        }

        void UpdateCollisions()
        {
            foreach (JumpForcePlatform platform in JumpForcePlatform.Active)
            {
                if (!platform || !platform.Surface || !capsule)
                    continue;

                if (ignoredPillarColliders.Contains(platform.Surface))
                {
                    Physics.IgnoreCollision(capsule, platform.Surface, true);
                    continue;
                }

                float relativeVerticalSpeed =
                    body.linearVelocity.y - platform.Velocity.y;

                bool leavingSupport = takeoffGrace > 0f && platform == launchPlatform;
                bool solid = platform.startingGround || !platform.oneWay || platform == Support;
                if (!solid)
                {
                    // Once approaching from above, keep the physical contact through the rounded
                    // capsule's edge contact. Feet below Top alone does not mean we are underneath.
                    if (!platform.Surface.enabled || platform.Surface.isTrigger ||
                        leavingSupport || relativeVerticalSpeed > 0.01f ||
                        capsule.bounds.max.y < platform.Surface.bounds.min.y - contactTolerance)
                        landingSurfaces.Remove(platform);
                    else if (FeetY >= platform.Top - contactTolerance)
                        landingSurfaces.Add(platform);

                    solid = landingSurfaces.Contains(platform);
                }
                else
                    landingSurfaces.Add(platform);

                Physics.IgnoreCollision(capsule, platform.Surface, !solid);
            }
        }

        public void Die()
        {
            if (Dead || !GameplayEnabled)
                return;

            CancelCharge();
            score?.ObserveHeight(body.position.y);

            RestorePillarCollision();
            landingSurfaces.Clear();
            physicsContactCount = 0;
            Dead = true;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.isKinematic = true;

            onDeath.Invoke();
            JumpForceEventos.AvisarMorte();

            body.position = Vector3.zero;
            transform.position = Vector3.zero;

            Grounded = false;
            Target = null;
            Support = null;
            launchPlatform = null;
            wallContact = null;
            wallJumpReady = false;
            ResetSupportVelocityTracking();
        }

        public void Restart()
        {
            RestorePillarCollision();
            landingSurfaces.Clear();
            physicsContactCount = 0;
            FindAnyObjectByType<JumpForcePlatformVisibility>()?.RestoreAll();

            body.isKinematic = false;
            Dead = false;
            Grounded = false;
            ReachedPlatform = false;
            Target = null;
            Support = null;
            launchPlatform = null;

            CancelCharge();

            body.position = Vector3.zero;
            transform.position = Vector3.zero;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;

            wallContact = null;
            wallJumpReady = false;
            takeoffGrace = 0f;
            giroVisual = 0f;
            AnguloMira = 0f;
            previousVerticalSpeed = 0f;
            facing = Vector3.forward;
            ResetSupportVelocityTracking();

            if (visual)
                visual.localRotation = initialRotation;

            animationDriver?.ResetIntro();
            UpdateCollisions();
        }

        void OnDisable()
        {
            RestorePillarCollision();
            landingSurfaces.Clear();
            physicsContactCount = 0;
            if (!initialized)
                return;

            CancelCharge();
            ResetSupportVelocityTracking();

            foreach (JumpForcePlatform platform in JumpForcePlatform.Active)
            {
                if (platform && platform.Surface && capsule)
                    Physics.IgnoreCollision(capsule, platform.Surface, false);
            }
        }
    }
}
