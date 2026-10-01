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
        public Bounds bodyFromTop, fanBodyFromTop;
        public float topOffset;
        public float baseSpeed;
    }

    [Serializable]
    public sealed class JumpForceTrailNode
    {
        public int id, level, lane, previousId;
        public bool primary, hasCoin, coinCollected;
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
        int lastLevel, nextTrampoline, nextFan, nextCoin;
        float leftBoundary = float.NegativeInfinity, rightBoundary = float.PositiveInfinity;
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
            nextFan = GapExcept(nextTrampoline);
            nextCoin = random.Next(2, 6);
        }

        public void SetCapabilities(float height, float lateralSpeed, float acceleration)
        {
            jumpHeight = Mathf.Max(0.1f, height);
            airSpeed = Mathf.Max(0, lateralSpeed);
            gravity = Mathf.Max(0.1f, acceleration);
        }

        int Gap() => random.NextDouble() < settings.shortSpecialGapChance ? random.Next(2, 5) : random.Next(5, 9);
        int GapExcept(int forbidden)
        {
            bool shortGap = Chance(settings.shortSpecialGapChance);
            int low = shortGap ? 2 : 5, high = shortGap ? 5 : 9;
            int value;
            do { value = random.Next(low, high); } while (value == forbidden);
            return value;
        }

        public void SetHorizontalBounds(float left, float right)
        {
            leftBoundary = left;
            rightBoundary = right;
        }
        bool Chance(float chance) => random.NextDouble() < chance;
        float Range(float low, float high) => Mathf.Lerp(low, high, (float)random.NextDouble());
        JumpForceElementShape Shape(JumpForceTrailNode node) => node.level == 0 ? ground : node.kind == JumpForceElementKind.Pillar ? pillar : platform;

        public Bounds Envelope(JumpForceTrailNode node)
        {
            var shape = Shape(node);
            var bounds = node.special == JumpForceSpecial.Fan ? shape.fanBodyFromTop : shape.bodyFromTop;
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
            if (candidate.level > 0 && (bounds.min.x < leftBoundary || bounds.max.x > rightBoundary)) return false;
            for (int i = Mathf.Max(0, candidate.level - collisionLevels); i <= candidate.level + collisionLevels; i++)
                if (levels.TryGetValue(i, out var level))
                    foreach (var node in level.nodes)
                        if (node.id != candidate.id && bounds.Intersects(Envelope(node))) return false;
            return true;
        }

        bool HasSafeExit(JumpForceTrailNode node)
        {
            bool vertical = node.kind == JumpForceElementKind.Pillar && node.axis == JumpForceMotionAxis.Y && node.amplitude > 0;
            if (!vertical) return true;
            foreach (int lane in new[] { -1, 1 })
            {
                var next = NewNode(node.level + 1, lane, JumpForceElementKind.Platform, true, node.id);
                if (Reachable(node, next) && Free(next) && !Envelope(node).Intersects(Envelope(next))) return true;
            }
            return false;
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

        bool VerticalPillar(JumpForceTrailNode node) =>
            node.kind == JumpForceElementKind.Pillar && node.axis == JumpForceMotionAxis.Y && node.amplitude > 0;

        bool TryMoveX(JumpForceTrailNode node, JumpForceTrailNode previous)
        {
            float oldAmplitude = node.amplitude, oldOffset = node.offset;
            var oldAxis = node.axis;
            node.axis = JumpForceMotionAxis.X;
            // Small but real movement is sufficient to open a passage.
            float maximum = node.kind == JumpForceElementKind.Pillar ? settings.pillarAmplitude : settings.platformAmplitude;
            node.amplitude = Mathf.Min(1, maximum);
            node.offset = 0;
            if (node.amplitude >= 0.25f && Fit(node, previous) && node.amplitude >= 0.25f) return true;
            node.amplitude = oldAmplitude;
            node.offset = oldOffset;
            node.axis = oldAxis;
            return false;
        }

        bool AddAlternative(JumpForceTrailLevel level, JumpForceTrailNode previous,
            bool restricted, bool oppositePillar)
        {
            if (level.nodes.Count == 2) return true;
            var primary = level.Primary;
            foreach (int lane in ShuffledLanes())
            {
                if (lane == primary.lane || (restricted && lane == 0) ||
                    (oppositePillar && lane != -primary.lane)) continue;
                var node = NewNode(level.index, lane, oppositePillar ? JumpForceElementKind.Pillar :
                    JumpForceElementKind.Platform, false, previous.id);
                if (!oppositePillar && !restricted && !Chance(settings.fixedChance))
                    node.amplitude = Range(0, settings.platformAmplitude);
                // An escape beside the fan can also be reached from the fan's own support.
                if (!Fit(node, previous))
                {
                    if (!Fit(node, primary)) continue;
                    node.previousId = primary.id;
                }
                level.nodes.Add(node);
                return true;
            }
            return false;
        }

        bool ResolveDeadEnds(JumpForceTrailLevel level, JumpForceTrailLevel lower, bool restricted)
        {
            var primary = level.Primary;
            var previous = lower.Primary;
            bool fixedFan = primary.special == JumpForceSpecial.Fan && primary.amplitude == 0;
            if (fixedFan && primary.lane != 0 && lower.nodes.Count == 1)
            {
                // Exact requested escape: stationary pillar in the opposite corner.
                level.nodes.RemoveAll(n => !n.primary);
                if (!AddAlternative(level, previous, restricted, true)) return false;
            }

            if (level.nodes.Count == 1 &&
                (primary.special == JumpForceSpecial.Fan || primary.kind == JumpForceElementKind.Pillar))
                foreach (var below in lower.nodes)
                    if (below.special == JumpForceSpecial.Trampoline && below.lane == primary.lane)
                    {
                        if ((!restricted && TryMoveX(primary, previous)) ||
                            AddAlternative(level, previous, restricted, false)) break;
                        return false;
                    }

            if (level.nodes.Count == 1 && primary.special == JumpForceSpecial.Fan &&
                primary.lane == 0 && primary.amplitude == 0)
            {
                foreach (var below in lower.nodes)
                {
                    if (below.level == 0 || below.kind != JumpForceElementKind.Platform) continue;
                    if (below.axis == JumpForceMotionAxis.X && below.amplitude > 0) return true;
                    var before = levels[below.level - 1].Primary;
                    if (VerticalPillar(before)) continue;
                    if (TryMoveX(below, before))
                    {
                        if (Reachable(below, primary)) return true;
                        below.amplitude = 0;
                    }
                }
                return AddAlternative(level, previous, restricted, false) ||
                    (!restricted && TryMoveX(primary, previous));
            }
            return true;
        }

        void GenerateNext()
        {
            int index = lastLevel + 1;
            var lower = levels[lastLevel];
            var previous = lower.Primary;
            bool trampolineDue = index == nextTrampoline, fanDue = index == nextFan;
            bool coinDue = index >= nextCoin;
            bool restricted = VerticalPillar(previous);
            bool fixedRoll = Chance(settings.fixedChance);
            bool pillarRoll = !restricted && !trampolineDue && !fanDue && !coinDue && Chance(settings.pillarChance);
            JumpForceTrailLevel accepted = null;
            var lanes = ShuffledLanes();
            for (int attempt = 0; attempt < 2 && accepted == null; attempt++)
                foreach (int lane in lanes)
                {
                    if ((restricted && lane == 0) || Mathf.Abs(lane - previous.lane) > 1) continue;
                    var kind = attempt == 0 && pillarRoll ? JumpForceElementKind.Pillar : JumpForceElementKind.Platform;
                    var node = NewNode(index, lane, kind, true, previous.id);
                    node.special = trampolineDue ? JumpForceSpecial.Trampoline :
                        fanDue ? JumpForceSpecial.Fan : JumpForceSpecial.None;
                    if (!fixedRoll && !restricted && attempt == 0)
                    {
                        node.amplitude = Range(0, kind == JumpForceElementKind.Pillar ?
                            Mathf.Clamp(settings.pillarAmplitude, 0, 2) : Mathf.Clamp(settings.platformAmplitude, 0, 5));
                        if (kind == JumpForceElementKind.Pillar && lane == 0 && Chance(settings.verticalPillarChance))
                            node.axis = JumpForceMotionAxis.Y;
                    }
                    if (!Fit(node, previous)) continue;
                    var candidate = new JumpForceTrailLevel(index);
                    candidate.nodes.Add(node);
                    levels.Add(index, candidate);
                    if (Chance(settings.secondPlatformChance)) AddAlternative(candidate, previous, restricted, false);
                    if (ResolveDeadEnds(candidate, lower, restricted)) { accepted = candidate; break; }
                    levels.Remove(index);
                }
            if (accepted == null)
                throw new InvalidOperationException("Jump Force: sem rota segura no nivel " + index +
                    ". Confira dimensoes dos prefabs, paredes e alcance do jogador.");
            if (coinDue)
            {
                foreach (var node in accepted.nodes)
                    if (node.kind == JumpForceElementKind.Platform)
                    {
                        node.hasCoin = true;
                        nextCoin = index + random.Next(2, 6);
                        break;
                    }
            }
            // Independent schedules, but a date reserved for one cannot be used by the other.
            if (trampolineDue) nextTrampoline = index + GapExcept(nextFan - index);
            if (fanDue) nextFan = index + GapExcept(nextTrampoline - index);
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
