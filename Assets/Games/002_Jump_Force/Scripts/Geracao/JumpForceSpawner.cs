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
        public JumpForcePlatform startingGround;
        public GameObject platformPrefab;
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
        [Header("Movimento")]
        [Tooltip("Variacao positiva/negativa em m/s sobre a velocidade media do prefab (4 x amplitude / periodo).")]
        [Min(0)] public float speedVariation = 3;
        public Vector2 speedChangeInterval = new Vector2(1, 3);
        [Header("Visibilidade")]
        [Range(0, 0.5f)] public float viewportMargin = 0.05f;
        [Header("Estado atual (leitura)")]
        [SerializeField] int currentLevel;
        [SerializeField] int plannedThrough;
        [SerializeField] int activeElements;
        [SerializeField] int createdPlatforms;
        [SerializeField] int createdPillars;
        [SerializeField] int runSeed;
        public JumpForceTrailMap Map { get; private set; }
        public IReadOnlyDictionary<int, JumpForceSpawnedElement> ActiveElements => active;
        public int CreatedPlatforms => createdPlatforms;
        public int CreatedPillars => createdPillars;

        readonly List<JumpForceSpawnedElement> platforms = new();
        readonly List<JumpForceSpawnedElement> pillars = new();
        readonly Dictionary<int, JumpForceSpawnedElement> active = new();
        readonly HashSet<int> retired = new();
        readonly List<int> remove = new();
        JumpForceElementShape platformShape, pillarShape, groundShape;
        float originY, z;
        Camera view;
        bool initialized;

        void Start()
        {
            if (!player || !followCamera || !startingGround || !platformPrefab || !pillarPrefab)
            {
                Debug.LogError("Spawner: preencha jogador, camera, chao e os dois prefabs.", this);
                enabled = false;
                return;
            }
            view = followCamera.GetComponent<Camera>();
            z = player.transform.position.z;
            var groundBounds = JumpForceSpawnedElement.ColliderBounds(startingGround.GetComponent<BoxCollider>());
            originY = groundBounds.max.y;
            groundBounds.center -= new Vector3(0, originY, z);
            groundShape = new JumpForceElementShape { bodyFromTop = groundBounds };
            for (int i = 0; i < Mathf.Max(10, initialPerType); i++)
            {
                Create(JumpForceElementKind.Platform);
                Create(JumpForceElementKind.Pillar);
            }
            platformShape = platforms[0].Shape;
            pillarShape = pillars[0].Shape;
            initialized = true;
            RestartTrail();
        }

        JumpForceSpawnedElement Create(JumpForceElementKind kind)
        {
            var prefab = kind == JumpForceElementKind.Platform ? platformPrefab : pillarPrefab;
            var instance = Instantiate(prefab, Vector3.zero, Quaternion.identity, transform);
            var item = instance.AddComponent<JumpForceSpawnedElement>();
            item.Initialize(kind);
            instance.SetActive(false);
            if (kind == JumpForceElementKind.Platform) createdPlatforms++; else createdPillars++;
            (kind == JumpForceElementKind.Platform ? platforms : pillars).Add(item);
            return item;
        }

        public void RestartTrail()
        {
            if (!initialized) return;
            foreach (var item in active.Values) Return(item);
            active.Clear();
            retired.Clear();
            runSeed = settings.seed != 0 ? settings.seed : UnityEngine.Random.Range(1, int.MaxValue);
            Map = new JumpForceTrailMap(settings, runSeed, originY, platformShape, pillarShape, groundShape);
            float left = float.NegativeInfinity, right = float.PositiveInfinity;
            foreach (var wall in invisibleWalls)
            {
                if (!wall) continue;
                var bounds = JumpForceSpawnedElement.ColliderBounds(wall);
                if (bounds.center.x < 0) left = Mathf.Max(left, bounds.max.x);
                else right = Mathf.Min(right, bounds.min.x);
            }
            Map.SetHorizontalBounds(left, right);
            RefreshWindow();
        }

        void UpdateCapabilities()
        {
            Map.SetCapabilities(player.maximumJumpHeight, player.airSpeed,
                Mathf.Abs(Physics.gravity.y) * player.gravityMultiplier);
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
            int high = currentLevel + ahead;
            try { Map.EnsureThrough(high); }
            catch (InvalidOperationException ex)
            {
                Debug.LogError(ex.Message + " Semente: " + runSeed, this);
                enabled = false;
                return;
            }
            // Planning five levels ahead does not allocate five levels of GameObjects.
            float lead = rising * Mathf.Max(0.1f, activationLeadSeconds);
            float activeTop = Mathf.Max(player.FeetY + 2 * settings.levelHeight, cameraTop + settings.levelHeight) + lead;
            float activeBottom = Mathf.Min(player.FeetY - settings.levelHeight, cameraBottom - settings.levelHeight);
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
                    if (active.ContainsKey(node.id) || retired.Contains(node.id)) continue;
                    var item = Acquire(node.kind);
                    item.name = (node.kind == JumpForceElementKind.Platform ? "Plataforma" : "Pilar") + "_Nivel_" + node.level + "_X_" + node.lane * 3;
                    item.Assign(node, z, player.score);
                    active.Add(node.id, item);
                }
            }
            // Keep logical history for the run; only scene objects are recycled.
            remove.Clear();
            foreach (int id in retired) if (id / 2 < low) remove.Add(id);
            foreach (int id in remove) retired.Remove(id);
            plannedThrough = Map.LastLevel;
            activeElements = active.Count;
        }

        bool IsSupporting(JumpForceSpawnedElement item) =>
            (player.Support && player.Support.transform.IsChildOf(item.transform)) ||
            (player.PillarContact && player.PillarContact.transform.IsChildOf(item.transform));

        JumpForceSpawnedElement Acquire(JumpForceElementKind kind)
        {
            var pool = kind == JumpForceElementKind.Platform ? platforms : pillars;
            foreach (var item in pool)
                if (item && !item.gameObject.activeSelf && item.Node == null) return item;
            return Create(kind);
        }

        void Return(JumpForceSpawnedElement item) => item.Release();

        void FixedUpdate()
        {
            if (!initialized || player.Dead) return;
            RefreshWindow();
            if (!enabled || !player.GameplayEnabled) return;
            foreach (var item in active.Values)
            {
                Vector3 step = item.Step(Time.fixedDeltaTime, Mathf.Max(0, speedVariation), speedChangeInterval);
                if (step == Vector3.zero) continue;
                Bounds swept = item.WorldBounds;
                var destination = swept;
                destination.center += step;
                swept.Encapsulate(destination);
                swept.Expand(Mathf.Max(0.01f, settings.separation * 0.5f));
                bool blocked = false;
                foreach (var other in active.Values)
                    if (other != item && swept.Intersects(other.WorldBounds)) { blocked = true; break; }
                if (!blocked)
                    foreach (var wall in invisibleWalls)
                        if (wall && wall.enabled && wall.gameObject.activeInHierarchy && swept.Intersects(wall.bounds))
                        { blocked = true; break; }
                item.ApplyStep(step, blocked);
            }
        }

        void LateUpdate()
        {
            if (!initialized || !view) return;
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
                // During the initial camera grace, platforms below can still be reached by descending.
                if (below && followCamera.LockedUpward && !supportingPlayer)
                {
                    remove.Add(pair.Key);
                    retired.Add(pair.Key);
                }
                else pair.Value.SetVisible(!above && !below);
            }
            foreach (int id in remove) { Return(active[id]); active.Remove(id); }
            activeElements = active.Count;
        }

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
