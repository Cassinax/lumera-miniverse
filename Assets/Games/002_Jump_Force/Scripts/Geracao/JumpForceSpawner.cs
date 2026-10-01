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
        [Header("Mapa da trilha")]
        public JumpForceTrailSettings settings = new();
        [Min(5)] public int levelsBelow = 5;
        [Min(5)] public int levelsAhead = 5;
        [Min(0.1f)] public float anticipationSeconds = 2;
        [Header("Pool")]
        [Min(10)] public int initialPerType = 10;
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

        readonly Stack<JumpForceSpawnedElement> platforms = new();
        readonly Stack<JumpForceSpawnedElement> pillars = new();
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
                platforms.Push(Create(JumpForceElementKind.Platform));
                pillars.Push(Create(JumpForceElementKind.Pillar));
            }
            platformShape = platforms.Peek().Shape;
            pillarShape = pillars.Peek().Shape;
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
            // Also cover what the camera can show above the player, not only the fixed five-level window.
            if (view)
            {
                float depth = view.WorldToViewportPoint(new Vector3(0, player.FeetY, z)).z;
                if (depth > 0) aheadHeight = Mathf.Max(aheadHeight, view.ViewportToWorldPoint(new Vector3(0.5f, 1.1f, depth)).y - player.FeetY);
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
            remove.Clear();
            foreach (var pair in active)
            {
                var item = pair.Value;
                bool supportingPlayer = player.Support && player.Support.transform.IsChildOf(item.transform);
                if ((item.Node.level < low || item.Node.level > high) && !supportingPlayer) remove.Add(pair.Key);
            }
            foreach (int id in remove) { Return(active[id]); active.Remove(id); }
            for (int index = low; index <= high; index++)
            {
                if (!Map.Levels.TryGetValue(index, out var level)) continue;
                foreach (var node in level.nodes)
                {
                    if (active.ContainsKey(node.id) || retired.Contains(node.id)) continue;
                    var pool = node.kind == JumpForceElementKind.Platform ? platforms : pillars;
                    var item = pool.Count > 0 ? pool.Pop() : Create(node.kind);
                    item.name = (node.kind == JumpForceElementKind.Platform ? "Plataforma" : "Pilar") + "_Nivel_" + node.level + "_X_" + node.lane * 3;
                    item.Assign(node, z);
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

        void Return(JumpForceSpawnedElement item)
        {
            item.Release();
            (item.Kind == JumpForceElementKind.Platform ? platforms : pillars).Push(item);
        }

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
                bool supportingPlayer = player.Support && player.Support.transform.IsChildOf(pair.Value.transform);
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
