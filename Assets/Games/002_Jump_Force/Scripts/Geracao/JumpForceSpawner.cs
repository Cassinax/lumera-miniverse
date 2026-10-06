using System;
using System.Collections.Generic;
using UnityEngine;

namespace Lumera.JumpForce
{
    [DefaultExecutionOrder(-350), DisallowMultipleComponent]
    public sealed class JumpForceSpawner : MonoBehaviour
    {
        [Header("Referencias")]
        public JumpForcePlayer player;
        public JumpForceCamera followCamera;
        public JumpForceBiomeController biomeController;
        public JumpForcePlatform startingGround;
        public GameObject platformPrefab;
        [Tooltip("Ordem: Padrao, Gelo, Invisivel, Nuvem, Instavel.")]
        public GameObject[] platformPrefabs = Array.Empty<GameObject>();
        [Header("Interativos independentes")]
        public JumpForceInteractive trampolinePrefab;
        public JumpForceInteractive fanPrefab;
        public GameObject coinPrefab;
        public GameObject pillarPrefab;
        public Collider[] invisibleWalls = Array.Empty<Collider>();
        [Header("Mapa da trilha")]
        public JumpForceTrailSettings settings = new();
        [Min(5)] public int levelsBelow = 5;
        [Min(5)] public int levelsAhead = 5;
        [Min(0.1f)] public float anticipationSeconds = 2;
        [Header("Pool")]
        [Min(10)] public int initialPerType = 10;
        [Tooltip("Objetos fisicos acompanham a camera e a velocidade; o restante existe apenas no mapa.")]
        [Min(0.1f)] public float activationLeadSeconds = 0.5f;
        [Header("Visibilidade")]
        [Range(0, 0.5f)] public float viewportMargin = 0.05f;
        [Header("Estado atual (leitura)")]
        [SerializeField] int currentLevel;
        [SerializeField] int createdPlatforms;
        [SerializeField] int createdPillars;
        [SerializeField] int runSeed;
        public JumpForceTrailMap Map { get; private set; }
        public IReadOnlyDictionary<int, JumpForceSpawnedElement> ActiveElements => active;
        public int CurrentLevel => currentLevel;
        public int CreatedPlatforms => createdPlatforms;
        public int CreatedPillars => createdPillars;

        readonly List<JumpForceSpawnedElement> platforms = new();
        readonly List<JumpForceSpawnedElement> pillars = new();
        readonly Dictionary<int, JumpForceSpawnedElement> active = new();
        readonly HashSet<int> retired = new();
        readonly List<int> remove = new();
        readonly List<JumpForceInteractive> trampolines = new();
        readonly List<JumpForceInteractive> fans = new();
        JumpForceElementShape[] variantShapes;
        JumpForceElementShape platformShape, pillarShape, groundShape;
        float originY, z;
        Camera view;
        bool initialized;
        int warnedAtLevel = -1, forgottenBelow;

        void Start()
        {
            if (!player || !followCamera || !startingGround || !platformPrefab || !pillarPrefab || !trampolinePrefab || !fanPrefab)
            {
                Debug.LogError("Spawner: preencha jogador, camera, chao, catalogo e interativos.", this);
                enabled = false;
                return;
            }
            view = followCamera.Visao;
            z = player.transform.position.z;
            var groundBounds = JumpForceSpawnedElement.ColliderBounds(startingGround.GetComponent<BoxCollider>());
            originY = groundBounds.max.y;
            groundBounds.center -= new Vector3(0, originY, z);
            groundShape = new JumpForceElementShape { bodyFromTop = groundBounds };
            if (platformPrefabs == null || platformPrefabs.Length == 0) platformPrefabs = new[] { platformPrefab };
            variantShapes = new JumpForceElementShape[platformPrefabs.Length];
            for (int i = 0; i < Mathf.Max(10, initialPerType); i++)
            {
                CreateInteractive(JumpForceSpecial.Trampoline);
                CreateInteractive(JumpForceSpecial.Fan);
            }
            for (int variant = 0; variant < platformPrefabs.Length; variant++)
                for (int i = 0; i < Mathf.Max(10, initialPerType); i++)
                {
                    var item = Create(JumpForceElementKind.Platform, variant);
                    variantShapes[variant] = item.Shape;
                }
            for (int i = 0; i < Mathf.Max(10, initialPerType); i++) Create(JumpForceElementKind.Pillar);
            platformShape = variantShapes[0];
            pillarShape = pillars[0].Shape;
            initialized = true;
            RestartTrail();
        }

        JumpForceSpawnedElement Create(JumpForceElementKind kind, int variant = 0)
        {
            var prefab = kind == JumpForceElementKind.Platform ? platformPrefabs[variant] : pillarPrefab;
            var instance = Instantiate(prefab, Vector3.zero, Quaternion.identity, transform);
            var item = instance.AddComponent<JumpForceSpawnedElement>();
            if (kind == JumpForceElementKind.Platform && coinPrefab)
            {
                var coinObject = Instantiate(coinPrefab, instance.transform);
                coinObject.transform.localPosition = new Vector3(0, 1.1f, 0);
                coinObject.transform.localRotation = Quaternion.identity;
                coinObject.transform.localScale = Vector3.one;
            }
            item.Initialize(kind, variant);
            if (kind == JumpForceElementKind.Platform) item.ConfigureFanBounds(fans[0].CorpoLocal);
            instance.SetActive(false);
            if (kind == JumpForceElementKind.Platform) createdPlatforms++; else createdPillars++;
            (kind == JumpForceElementKind.Platform ? platforms : pillars).Add(item);
            return item;
        }

        JumpForceInteractive CreateInteractive(JumpForceSpecial type)
        {
            var instance = Instantiate(type == JumpForceSpecial.Fan ? fanPrefab : trampolinePrefab, transform);
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale = Vector3.one;
            instance.Preparar();
            instance.gameObject.SetActive(false);
            (type == JumpForceSpecial.Fan ? fans : trampolines).Add(instance);
            return instance;
        }
        JumpForceInteractive AcquireInteractive(JumpForceSpecial type)
        {
            if (type == JumpForceSpecial.None) return null;
            var pool = type == JumpForceSpecial.Fan ? fans : trampolines;
            foreach (var item in pool) if (item && !item.Reservado && !item.gameObject.activeSelf) return item;
            return CreateInteractive(type);
        }

        public void RestartTrail()
        {
            if (!initialized) return;
            enabled = true;
            warnedAtLevel = -1;
            forgottenBelow = 0;
            currentLevel = 0;
            if (biomeController) biomeController.Resetar();
            startingGround.gameObject.SetActive(true);
            foreach (var item in active.Values) Return(item);
            active.Clear();
            retired.Clear();
            runSeed = settings.seed != 0 ? settings.seed : UnityEngine.Random.Range(1, int.MaxValue);
            Map = new JumpForceTrailMap(settings, runSeed, originY, platformShape, pillarShape, groundShape, variantShapes);
            if (JumpForceSpawnedElement.TryGetWallLimits(invisibleWalls, out float left, out float right))
                Map.SetHorizontalBounds(left, right);
            RefreshWindow();
        }

        void UpdateCapabilities()
        {
            // The camera kills a fall deeper than this: planned jumps must never need one.
            float survivableFall = followCamera.SurvivableFall() - settings.safeFallMargin;
            Map.SetCapabilities(player.maximumJumpHeight, player.airSpeed,
                Mathf.Abs(Physics.gravity.y) * player.gravityMultiplier, survivableFall, player.anguloMaximo, player.margemPuloVertical);
        }

        void RefreshWindow()
        {
            UpdateCapabilities();
            currentLevel = Mathf.Max(0, Mathf.FloorToInt((player.FeetY - originY) / Mathf.Max(0.5f, settings.levelHeight)));
            float gravity = Mathf.Max(0.1f, Mathf.Abs(Physics.gravity.y) * player.gravityMultiplier);
            float rising = Mathf.Max(0, player.Body.linearVelocity.y);
            float projectedRise = Mathf.Max(player.maximumJumpHeight, rising * rising / (2 * gravity));
            float aheadHeight = projectedRise + rising * Mathf.Max(0.1f, anticipationSeconds);
            float cameraBottom = player.FeetY - 2 * settings.levelHeight;
            float cameraTop = player.FeetY + 2 * settings.levelHeight;
            // Also cover what the camera can show above the player, not only the fixed five-level window.
            if (view)
            {
                float depth = view.WorldToViewportPoint(new Vector3(0, player.FeetY, z)).z;
                if (depth > 0)
                {
                    cameraTop = view.ViewportToWorldPoint(new Vector3(0.5f, 1.1f, depth)).y;
                    cameraBottom = view.ViewportToWorldPoint(new Vector3(0.5f, -0.1f, depth)).y;
                    aheadHeight = Mathf.Max(aheadHeight, cameraTop - player.FeetY);
                }
            }
            int ahead = Mathf.Max(5, levelsAhead, Mathf.CeilToInt(aheadHeight / Mathf.Max(0.5f, settings.levelHeight)));
            int low = Mathf.Max(1, currentLevel - Mathf.Max(5, levelsBelow));
            int high = Mathf.Min(currentLevel + ahead, Mathf.Max(1, settings.finalLevel));
            if (!Map.EnsureThrough(high))
            {
                if (warnedAtLevel != Map.LastLevel)
                {
                    warnedAtLevel = Map.LastLevel;
                    Debug.LogWarning("Jump Force: mantendo a rota existente e tentando outra continuacao apos o nivel " +
                        Map.LastLevel + ". Semente: " + runSeed, this);
                }
                // Do not recycle the route or disable the component when a planning attempt fails.
                return;
            }
            warnedAtLevel = -1;
            // Planning five levels ahead does not allocate five levels of GameObjects.
            float lead = rising * Mathf.Max(0.1f, activationLeadSeconds);
            float activeTop = Mathf.Max(player.FeetY + 2 * settings.levelHeight, cameraTop + settings.levelHeight) + lead;
            float activeBottom = Mathf.Min(player.FeetY - settings.levelHeight, cameraBottom - settings.levelHeight);
            // While the player is high in a jump, keep what lies around the support it can fall back to.
            if (followCamera.LockedUpward) activeBottom = Mathf.Min(activeBottom, followCamera.FloorFeet - settings.levelHeight);
            int activeHigh = Mathf.Min(high, Mathf.CeilToInt((activeTop - originY) / settings.levelHeight));
            int activeLow = Mathf.Max(low, Mathf.FloorToInt((activeBottom - originY) / settings.levelHeight));
            remove.Clear();
            foreach (var pair in active)
            {
                var item = pair.Value;
                bool supportingPlayer = IsSupporting(item);
                if (!item.gameObject.activeSelf || ((item.Node.level < activeLow || item.Node.level > activeHigh) && !supportingPlayer)) remove.Add(pair.Key);
            }
            foreach (int id in remove) { Return(active[id]); active.Remove(id); }
            for (int index = activeLow; index <= activeHigh; index++)
            {
                if (!Map.Levels.TryGetValue(index, out var level)) continue;
                foreach (var node in level.nodes)
                {
                    if (!active.ContainsKey(node.id) && node.ReassembleIfDue(Time.time)) retired.Remove(node.id);
                    if (active.ContainsKey(node.id) || retired.Contains(node.id) || node.destroyed) continue;
                    Map.ProtectThrough(node.level);
                    var item = Acquire(node.kind, node.platformVariant);
                    item.name = (node.kind == JumpForceElementKind.Platform ? "Plataforma" : "Pilar") + "_Nivel_" + node.level + "_X_" + node.lane * 3;
                    item.Assign(node, z, player.score, player, followCamera, AcquireInteractive(node.special), transform);
                    active.Add(node.id, item);
                }
            }
            // Keep logical history for the run; only scene objects are recycled.
            remove.Clear();
            foreach (int id in retired) if (id / 2 < low) remove.Add(id);
            foreach (int id in remove) retired.Remove(id);
            // Endless route: logical history below the window is no longer needed by planning or recycling.
            if (low - 1 > forgottenBelow)
            {
                forgottenBelow = low - 1;
                Map.ForgetBelow(forgottenBelow);
            }
        }

        bool IsSupporting(JumpForceSpawnedElement item) =>
            (player.Support && (player.Support.transform.IsChildOf(item.transform) ||
                (player.Support.proprietario && player.Support.proprietario.gameObject == item.gameObject))) ||
            (player.PillarContact && player.PillarContact.transform.IsChildOf(item.transform));

        JumpForceSpawnedElement Acquire(JumpForceElementKind kind, int variant)
        {
            var pool = kind == JumpForceElementKind.Platform ? platforms : pillars;
            foreach (var item in pool)
                if (item && !item.gameObject.activeSelf && item.Node == null && (kind == JumpForceElementKind.Pillar || item.Variant == variant)) return item;
            return Create(kind, variant);
        }

        void Return(JumpForceSpawnedElement item) => item.Release();

        void FixedUpdate()
        {
            if (!initialized || player.Dead)
                return;

            if (!player.GameplayEnabled)
                return;

            RefreshWindow();
        }

        void LateUpdate()
        {
            if (!initialized || !view || player.Victory) return;
            if (followCamera.InWardrobe)
            {
                foreach (var item in active.Values) item.SetVisible(false);
                return;
            }
            remove.Clear();
            foreach (var pair in active)
            {
                var bounds = pair.Value.WorldBounds;
                float minY = float.PositiveInfinity, maxY = float.NegativeInfinity;
                bool inFront = true;
                for (int i = 0; i < 8; i++)
                {
                    var sign = new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1);
                    var screen = view.WorldToViewportPoint(bounds.center + Vector3.Scale(bounds.extents, sign));
                    inFront &= screen.z > view.nearClipPlane;
                    minY = Mathf.Min(minY, screen.y);
                    maxY = Mathf.Max(maxY, screen.y);
                }
                bool below = inFront && maxY < -viewportMargin;
                bool above = inFront && minY > 1 + viewportMargin;
                bool supportingPlayer = IsSupporting(pair.Value);
                // The camera comes back down to the last support after a jump, so only what it can never
                // show again is recycled. Before the first platform nothing is (OutOfReachBelow is false).
                if (!supportingPlayer && followCamera.OutOfReachBelow(bounds, viewportMargin))
                {
                    remove.Add(pair.Key);
                    retired.Add(pair.Key);
                }
                else pair.Value.SetVisible(!above && !below);
            }
            foreach (int id in remove) { Return(active[id]); active.Remove(id); }
            // The starting ground is a scene object, not pooled: switched off once it can never be seen again.
            if (startingGround.gameObject.activeSelf && !IsSupportingGround() &&
                followCamera.OutOfReachBelow(JumpForceSpawnedElement.ColliderBounds(startingGround.Surface), viewportMargin))
                startingGround.gameObject.SetActive(false);
        }

        bool IsSupportingGround() => player.Support == startingGround;

#if UNITY_EDITOR
        void OnDrawGizmosSelected()
        {
            if (Map == null) return;
            foreach (var level in Map.Levels.Values)
                foreach (var node in level.nodes)
                {
                    Gizmos.color = node.primary ? Color.green : Color.cyan;
                    Gizmos.DrawWireSphere(node.TopPosition(z), 0.2f);
                    var envelope = Map.Envelope(node);
                    envelope.center += Vector3.forward * z;
                    Gizmos.DrawWireCube(envelope.center, envelope.size);
                    if (node.previousId >= 0 && Map.Levels.TryGetValue(node.previousId / 2, out var previous))
                        Gizmos.DrawLine(previous.Primary.TopPosition(z), node.TopPosition(z));
                }
        }
#endif
    }
}
