using System;
using System.Collections.Generic;
using UnityEngine;

namespace Lumera.JumpForce
{
    [Serializable]
    public sealed class JumpForceTrailSettings
    {
        [Min(0.5f)] public float levelHeight = 4;
        [Range(0, 1)] public float fixedChance = 0.6f;
        [Range(0, 1)] public float secondPlatformChance = 0.35f;
        [Range(0, 1)] public float pillarChance = 0.2f;
        [Range(0, 1)] public float verticalPillarChance = 0.5f;
        [Range(0, 5)] public float platformAmplitude = 5;
        [Range(0, 2)] public float pillarAmplitude = 2;
        [Min(0)] public float separation = 0.1f;
        [Range(0, 1)] public float shortSpecialGapChance = 0.2f;
        [Tooltip("Semente zero sorteia uma nova trilha a cada tentativa.")]
        public int seed;
    }

    public enum JumpForceElementKind { Platform, Pillar }
    public enum JumpForceSpecial { None, Trampoline, Fan }
    public enum JumpForceMotionAxis { X, Y }

    // Value-only geometry, measured once from prefabs. No scene objects are stored in the map.
    public struct JumpForceElementShape
    {
        public Bounds bodyFromTop;
        public float topOffset;
        public float baseSpeed;
    }

    [Serializable]
    public sealed class JumpForceTrailNode
    {
        public int id, level, lane, previousId;
        public bool primary;
        public JumpForceElementKind kind;
        public JumpForceSpecial special;
        public JumpForceMotionAxis axis;
        public float topY, amplitude, baseSpeed, fanYaw;
        public int motionSeed;
        // Movement state belongs to the logical node, so recycling a visual does not change its plan.
        public float offset, currentSpeed, untilSpeedChange;
        public int direction = 1;
        public Vector3 TopPosition(float z) => new Vector3(lane * 3, topY, z);
    }

    public sealed class JumpForceTrailLevel
    {
        public readonly int index;
        public readonly List<JumpForceTrailNode> nodes = new(2);
        public JumpForceTrailNode Primary => nodes[0];
        public JumpForceTrailLevel(int value) => index = value;
    }

    // Planning and history are independent of pooling, GameObjects and the camera.
    public sealed class JumpForceTrailMap
    {
        readonly JumpForceTrailSettings settings;
        readonly System.Random random;
        readonly JumpForceElementShape platform, pillar, ground;
        readonly SortedDictionary<int, JumpForceTrailLevel> levels = new();
        readonly List<int> obsolete = new();
        readonly float originY;
        readonly int collisionLevels;
        int lastLevel, nextTrampoline, nextFan;
        float jumpHeight, airSpeed, gravity;
        public IReadOnlyDictionary<int, JumpForceTrailLevel> Levels => levels;
        public int LastLevel => lastLevel;

        public JumpForceTrailMap(JumpForceTrailSettings settings, int seed, float originY,
            JumpForceElementShape platform, JumpForceElementShape pillar, JumpForceElementShape ground)
        {
            this.settings = settings;
            this.platform = platform;
            this.pillar = pillar;
            this.ground = ground;
            this.originY = originY;
            float span = Mathf.Max(platform.bodyFromTop.size.y, pillar.bodyFromTop.size.y, ground.bodyFromTop.size.y);
            collisionLevels = 2 + Mathf.CeilToInt((2 * span + 4) / Mathf.Max(0.5f, settings.levelHeight));
            random = new System.Random(seed);
            var start = new JumpForceTrailLevel(0);
            start.nodes.Add(new JumpForceTrailNode { id = 0, level = 0, primary = true, topY = originY, previousId = -1 });
            levels.Add(0, start);
            nextTrampoline = Gap();
            nextFan = Gap();
        }

        public void SetCapabilities(float height, float lateralSpeed, float acceleration)
        {
            jumpHeight = Mathf.Max(0.1f, height);
            airSpeed = Mathf.Max(0, lateralSpeed);
            gravity = Mathf.Max(0.1f, acceleration);
        }

        int Gap() => random.NextDouble() < settings.shortSpecialGapChance ? random.Next(2, 5) : random.Next(5, 9);
        bool Chance(float chance) => random.NextDouble() < chance;
        float Range(float low, float high) => Mathf.Lerp(low, high, (float)random.NextDouble());
        JumpForceElementShape Shape(JumpForceTrailNode node) => node.level == 0 ? ground : node.kind == JumpForceElementKind.Pillar ? pillar : platform;

        public Bounds Envelope(JumpForceTrailNode node)
        {
            var shape = Shape(node);
            var bounds = shape.bodyFromTop;
            bounds.center += node.TopPosition(0);
            bounds.Expand(node.axis == JumpForceMotionAxis.X ? new Vector3(2 * node.amplitude, 0, 0) : new Vector3(0, 2 * node.amplitude, 0));
            bounds.Expand(settings.separation);
            return bounds;
        }

        bool Reachable(JumpForceTrailNode from, JumpForceTrailNode to)
        {
            float dy = to.topY - from.topY +
                (from.axis == JumpForceMotionAxis.Y ? from.amplitude : 0) +
                (to.axis == JumpForceMotionAxis.Y ? to.amplitude : 0);
            if (dy > jumpHeight - 0.1f) return false;
            float launch = Mathf.Sqrt(2 * gravity * jumpHeight);
            float time = (launch + Mathf.Sqrt(Mathf.Max(0, launch * launch - 2 * gravity * dy))) / gravity;
            float distance = Mathf.Abs(to.lane - from.lane) * 3 +
                (from.axis == JumpForceMotionAxis.X ? from.amplitude : 0) +
                (to.axis == JumpForceMotionAxis.X ? to.amplitude : 0);
            // A conservative route: center-to-center, with a steering margin, at every motion phase.
            return distance <= airSpeed * time * 0.85f;
        }

        bool Free(JumpForceTrailNode candidate)
        {
            Bounds bounds = Envelope(candidate);
            for (int i = Mathf.Max(0, candidate.level - collisionLevels); i <= candidate.level + collisionLevels; i++)
                if (levels.TryGetValue(i, out var level))
                    foreach (var node in level.nodes)
                        if (bounds.Intersects(Envelope(node))) return false;
            return true;
        }

        bool HasSafeExit(JumpForceTrailNode node)
        {
            bool vertical = node.kind == JumpForceElementKind.Pillar && node.axis == JumpForceMotionAxis.Y && node.amplitude > 0;
            bool bothNext = nextTrampoline == node.level + 1 && nextFan == node.level + 1;
            if (!vertical && !bothNext) return true;
            int exits = 0;
            foreach (int lane in new[] { -1, 0, 1 })
            {
                if ((vertical && lane == 0) || Mathf.Abs(lane - node.lane) > 1) continue;
                var next = NewNode(node.level + 1, lane, JumpForceElementKind.Platform, true, node.id);
                next.amplitude = 0;
                if (Reachable(node, next) && Free(next) && !Envelope(node).Intersects(Envelope(next))) exits++;
            }
            // Reserve both sides if both independent specials are due on the next level.
            return exits >= (bothNext ? 2 : 1);
        }

        JumpForceTrailNode NewNode(int level, int lane, JumpForceElementKind kind, bool primary, int previousId)
        {
            return new JumpForceTrailNode
            {
                id = level * 2 + (primary ? 0 : 1), level = level, lane = lane, primary = primary,
                previousId = previousId, topY = originY + level * settings.levelHeight,
                kind = kind, axis = JumpForceMotionAxis.X,
                baseSpeed = kind == JumpForceElementKind.Pillar ? pillar.baseSpeed : platform.baseSpeed,
                motionSeed = random.Next(), direction = Chance(0.5f) ? -1 : 1,
                fanYaw = lane == -1 ? 180 : lane == 1 ? 0 : Chance(0.5f) ? 180 : 0
            };
        }

        bool Fit(JumpForceTrailNode node, JumpForceTrailNode previous)
        {
            while (true)
            {
                if (Reachable(previous, node) && Free(node) && HasSafeExit(node)) return true;
                if (node.amplitude <= 0) return false;
                node.amplitude = Mathf.Max(0, node.amplitude - 0.25f);
            }
        }

        int[] ShuffledLanes()
        {
            var lanes = new[] { -1, 0, 1 };
            for (int i = 2; i > 0; i--) { int j = random.Next(i + 1); (lanes[i], lanes[j]) = (lanes[j], lanes[i]); }
            return lanes;
        }

        public void EnsureThrough(int endLevel)
        {
            while (lastLevel < endLevel) GenerateNext();
        }

        void GenerateNext()
        {
            int index = lastLevel + 1;
            var previous = levels[lastLevel].Primary;
            bool trampolineDue = index == nextTrampoline, fanDue = index == nextFan;
            bool both = trampolineDue && fanDue;
            bool restricted = previous.kind == JumpForceElementKind.Pillar && previous.axis == JumpForceMotionAxis.Y && previous.amplitude > 0;
            bool fixedRoll = Chance(settings.fixedChance);
            bool pillarRoll = !restricted && !trampolineDue && !fanDue && Chance(settings.pillarChance);
            JumpForceTrailNode primary = null;
            var lanes = ShuffledLanes();
            // Try the selected type first; a normal, fixed platform is the safe fallback.
            for (int attempt = 0; attempt < 2 && primary == null; attempt++)
                foreach (int lane in lanes)
                {
                    if ((restricted && lane == 0) || Mathf.Abs(lane - previous.lane) > 1) continue;
                    var kind = attempt == 0 && pillarRoll ? JumpForceElementKind.Pillar : JumpForceElementKind.Platform;
                    var node = NewNode(index, lane, kind, true, previous.id);
                    if (!fixedRoll && !restricted && !both && attempt == 0)
                    {
                        node.amplitude = Range(0, kind == JumpForceElementKind.Pillar ?
                            Mathf.Clamp(settings.pillarAmplitude, 0, 2) : Mathf.Clamp(settings.platformAmplitude, 0, 5));
                        if (kind == JumpForceElementKind.Pillar && lane == 0 && Chance(settings.verticalPillarChance))
                            node.axis = JumpForceMotionAxis.Y;
                    }
                    if (!Fit(node, previous)) continue;
                    if (both)
                    {
                        bool spaceForSecond = false;
                        foreach (int otherLane in new[] { -1, 0, 1 })
                        {
                            if (otherLane == lane || (restricted && otherLane == 0)) continue;
                            var other = NewNode(index, otherLane, JumpForceElementKind.Platform, false, previous.id);
                            if (Reachable(previous, other) && Free(other) && !Envelope(node).Intersects(Envelope(other)))
                                spaceForSecond = true;
                        }
                        if (!spaceForSecond) continue;
                    }
                    primary = node;
                    break;
                }
            if (primary == null)
                throw new InvalidOperationException("Jump Force: sem rota segura no nivel " + index + ". Confira dimensoes dos prefabs e alcance do jogador.");
            var level = new JumpForceTrailLevel(index);
            level.nodes.Add(primary);
            levels.Add(index, level);
            if (trampolineDue) primary.special = JumpForceSpecial.Trampoline;
            else if (fanDue) primary.special = JumpForceSpecial.Fan;

            if (both || Chance(settings.secondPlatformChance))
                foreach (int lane in ShuffledLanes())
                {
                    if (lane == primary.lane || (restricted && lane == 0)) continue;
                    // A vertical pillar reserves the central shaft through the next level.
                    var node = NewNode(index, lane, JumpForceElementKind.Platform, false, previous.id);
                    if (!restricted && !both && !Chance(settings.fixedChance))
                        node.amplitude = Range(0, Mathf.Clamp(settings.platformAmplitude, 0, 5));
                    if (!Fit(node, previous)) continue;
                    if (both) node.special = JumpForceSpecial.Fan;
                    level.nodes.Add(node);
                    break;
                }
            if (both && level.nodes.Count != 2)
            {
                levels.Remove(index);
                throw new InvalidOperationException("Jump Force: os dois especiais precisam de duas plataformas no nivel " + index + ".");
            }
            if (trampolineDue) nextTrampoline = index + Gap();
            if (fanDue) nextFan = index + Gap();
            lastLevel = index;
        }

        public void ForgetBelow(int firstLevel)
        {
            obsolete.Clear();
            foreach (int index in levels.Keys) if (index < firstLevel && index < lastLevel) obsolete.Add(index);
            foreach (int index in obsolete) levels.Remove(index);
        }
    }
}
