using System;
using System.Collections.Generic;
using UnityEngine;

namespace Lumera.JumpForce
{
    // One difficulty band. Numbers are balance, not rules: route safety always overrides them.
    [Serializable]
    public sealed class JumpForceDifficultyTier
    {
        [Tooltip("Intencao desta faixa. Guia para ajustar os numeros.")]
        public string intencao = "";
        [Range(0, 1)] public float fixedChance = 0.6f;
        [Range(0, 1)] public float secondPlatformChance = 0.35f;
        [Range(0, 1)] public float pillarChance = 0.2f;
        [Range(0, 1)] public float verticalPillarChance = 0.5f;
        [Tooltip("Limite para CADA lado. O planejamento reduz quando o salto, a separacao ou as paredes exigem.")]
        [Range(0, 5)] public float platformAmplitude = 5;
        [Range(0, 2)] public float pillarAmplitude = 2;
        [Range(0, 1)] public float shortSpecialGapChance = 0.2f;
        [Tooltip("Multiplica a velocidade media do prefab (4 x amplitude / periodo).")]
        [Min(0.1f)] public float speedMultiplier = 1;
        [Tooltip("Variacao sorteada em m/s, para mais e para menos, em torno da velocidade media.")]
        [Min(0)] public float speedVariation = 1;
        [Tooltip("Fracao do alcance lateral teorico usada no planejamento. Menor deixa os saltos mais folgados.")]
        [Range(0.4f, 1)] public float reachMargin = 0.85f;
    }

    [Serializable]
    public sealed class JumpForceTrailSettings
    {
        [Min(0.5f)] public float levelHeight = 4;
        [Tooltip("Nivel que encerra a partida. O mapa nao gera acima dele.")]
        [Min(1)] public int finalLevel = 450;
        [Header("Trecho final: rota unica")]
        [Min(1)] public int finalChallengeStart = 400;
        public JumpForceDifficultyTier finalChallengeTier = new()
        {
            intencao = "Final: sequencias precisas sem caminhos alternativos.",
            fixedChance = 0.2f, secondPlatformChance = 0, pillarChance = 0.35f, verticalPillarChance = 0,
            platformAmplitude = 5, pillarAmplitude = 2, shortSpecialGapChance = 0.3f,
            speedMultiplier = 1.35f, speedVariation = 1.4f, reachMargin = 0.92f
        };
        public JumpForceRouteSection[] finalSections = JumpForceBiomeRules.FinalPadrao();
        public bool IsFinalChallenge(int level) => level >= Mathf.Max(1, finalChallengeStart);
        [Min(0)] public float separation = 0.1f;
        [Tooltip("Semente zero sorteia uma nova trilha a cada tentativa.")]
        public int seed;
        [Header("Dificuldade")]
        [Tooltip("A dificuldade sobe a cada este numero de niveis.")]
        [Min(1)] public int levelsPerTier = 30;
        [Tooltip("Faixa 0: niveis 0-29; faixa 1: 30-59; faixa 2: 60-89; e assim por diante. " +
            "A ultima faixa vale para sempre: quatro faixas = tres aumentos e depois estavel.")]
        public JumpForceDifficultyTier[] tiers = DefaultTiers();
        [Header("Tipos de plataforma: Padrao, Gelo, Invisivel, Nuvem, Instavel")]
        [Tooltip("Pesos relativos. Zero desativa um tipo; o catalogo de prefabs usa a mesma ordem.")]
        public float[] platformWeights = { 4, 1, 1, 1, 1 };
        [Header("Biomas e conjuntos")]
        [Tooltip("Ativado: conjuntos controlam as variantes. Desativado: usa Platform Weights.")]
        public bool useBiomes = true;
        [Tooltip("Tempo entre pousar e sair da nuvem obrigatoria usado pelo planejamento. Nao limita a habilidade: demorar continua fazendo a nuvem afundar.")]
        [Min(0)] public float requiredCloudDepartureSeconds = 2f;
        public JumpForceBiomeRules[] biomes = JumpForceBiomeRules.Padrao();
        [Header("Velocidade dos elementos")]
        [Tooltip("Velocidade minima de um elemento movel, em m/s. Evita plataformas quase paradas.")]
        [Min(0.05f)] public float minimumSpeed = 0.4f;
        [Tooltip("Velocidade maxima de um elemento, como fracao da velocidade do jogador no ar.")]
        [Range(0.1f, 1)] public float maximumSpeedFraction = 0.7f;
        [Header("Seguranca da rota")]
        [Tooltip("Folga, em metros, descontada da maior queda abaixo da ultima plataforma que a camera tolera antes de matar.")]
        [Min(0)] public float safeFallMargin = 0.3f;

        public static JumpForceDifficultyTier[] DefaultTiers() => new[]
        {
            new JumpForceDifficultyTier { intencao = "Aprender: quase tudo parado, movimentos curtos e lentos, saltos laterais folgados.",
                fixedChance = 0.75f, secondPlatformChance = 0.5f, pillarChance = 0.08f, verticalPillarChance = 0,
                platformAmplitude = 1.5f, pillarAmplitude = 0.75f, shortSpecialGapChance = 0.15f,
                speedMultiplier = 0.6f, speedVariation = 0.3f, reachMargin = 0.7f },
            new JumpForceDifficultyTier { intencao = "Ritmo: mais movimento, pilares passam a aparecer com frequencia.",
                fixedChance = 0.6f, secondPlatformChance = 0.35f, pillarChance = 0.2f, verticalPillarChance = 0.4f,
                platformAmplitude = 3, pillarAmplitude = 1.5f, shortSpecialGapChance = 0.2f,
                speedMultiplier = 0.85f, speedVariation = 0.6f, reachMargin = 0.8f },
            new JumpForceDifficultyTier { intencao = "Desafio: menos apoio extra, percursos longos e mais rapidos.",
                fixedChance = 0.45f, secondPlatformChance = 0.25f, pillarChance = 0.28f, verticalPillarChance = 0.5f,
                platformAmplitude = 4, pillarAmplitude = 2, shortSpecialGapChance = 0.25f,
                speedMultiplier = 1, speedVariation = 0.9f, reachMargin = 0.85f },
            new JumpForceDifficultyTier { intencao = "Mestre: dificuldade maxima, estavel daqui em diante.",
                fixedChance = 0.35f, secondPlatformChance = 0.2f, pillarChance = 0.35f, verticalPillarChance = 0.6f,
                platformAmplitude = 5, pillarAmplitude = 2, shortSpecialGapChance = 0.3f,
                speedMultiplier = 1.15f, speedVariation = 1.2f, reachMargin = 0.85f },
        };

        public int TierIndex(int level) => IsFinalChallenge(level) ? (tiers == null ? 0 : tiers.Length) :
            tiers == null || tiers.Length == 0 ? 0 :
            Mathf.Clamp(Mathf.Max(0, level) / Mathf.Max(1, levelsPerTier), 0, tiers.Length - 1);
    }

    public enum JumpForceElementKind { Platform, Pillar }
    public enum JumpForceSpecial { None, Trampoline, Fan }
    public enum JumpForceMotionAxis { X, Y }

    // Value-only geometry, measured once from prefabs. No scene objects are stored in the map.
    public struct JumpForceElementShape
    {
        public Bounds bodyFromTop, fanBodyFromTop, rotatingFanBodyFromTop;
        public float topOffset;
        public float baseSpeed;
        public float maximumDescent, cloudWaitSeconds, cloudDescentSpeed;
    }

    [Serializable]
    public sealed class JumpForceTrailNode
    {
        public const float LaneSpacing = 3;
        public int id, level, lane, previousId, tier;
        public bool primary, hasCoin, coinCollected;
        public JumpForceElementKind kind;
        public int platformVariant;
        public int biomeIndex = -1, sectionIndex = -1, sectionStep;
        public bool routeRecovery, mandatoryCloud;
        // Estado logico sobrevive ao retorno do GameObject ao pool.
        public bool breakStarted, destroyed;
        public int brokenPieces;
        public float breakTimer, cloudOffset, cloudTimer, reassembleAt;
        public bool ReassembleIfDue(float now)
        {
            if (reassembleAt <= 0 || now < reassembleAt) return false;
            ResetBreakState();
            return true;
        }
        public void ResetBreakState()
        {
            breakStarted = destroyed = false;
            brokenPieces = 0;
            breakTimer = reassembleAt = 0;
        }
        public JumpForceSpecial special;
        public JumpForceMotionAxis axis;
        public float topY, amplitude, baseSpeed, fanYaw, fanSpinY, motionCenterOffsetX;
        // Speed limits chosen at planning time, from the tier and the player's air speed.
        public float speedVariation, minimumSpeed, maximumSpeed;
        public int motionSeed;
        // Movement state belongs to the logical node, so recycling a visual does not change its plan.
        public float offset, currentSpeed, targetSpeed, untilSpeedChange;
        public int direction = 1;
        public float MotionCenterX => lane * LaneSpacing + motionCenterOffsetX;
        public Vector3 TopPosition(float z) => new Vector3(lane * LaneSpacing, topY, z);
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
        readonly int seed;
        readonly JumpForceElementShape platform, pillar, ground;
        readonly JumpForceElementShape[] variants;
        readonly SortedDictionary<int, JumpForceTrailLevel> levels = new();
        readonly List<int> obsolete = new();
        readonly Dictionary<int, (int trampoline, int fan, int coin)> schedules = new();
        int protectedThrough;
        public int RecoveryCount { get; private set; }
        readonly float originY;
        readonly int collisionLevels;
        int lastLevel, nextTrampoline, nextFan, nextCoin;
        float leftBoundary = float.NegativeInfinity, rightBoundary = float.PositiveInfinity;
        float jumpHeight, airSpeed, gravity, maximumAimAngle, verticalAimMargin, safeFall = float.PositiveInfinity;
        static readonly JumpForceDifficultyTier fallbackTier = new();
        // Below this, a "moving" element barely moves: it is planned as fixed instead.
        const float MinimumAmplitude = 0.25f;
        public IReadOnlyDictionary<int, JumpForceTrailLevel> Levels => levels;
        public int LastLevel => lastLevel;
        public int TierIndex(int level) => settings.TierIndex(level);
        JumpForceDifficultyTier Tier(int level) => settings.IsFinalChallenge(level)
            ? settings.finalChallengeTier ?? fallbackTier :
            settings.tiers != null && settings.tiers.Length > 0 ? settings.tiers[settings.TierIndex(level)] ?? fallbackTier : fallbackTier;

        public JumpForceTrailMap(JumpForceTrailSettings settings, int seed, float originY,
            JumpForceElementShape platform, JumpForceElementShape pillar, JumpForceElementShape ground, JumpForceElementShape[] variants = null)
        {
            this.settings = settings;
            this.seed = seed;
            this.platform = platform;
            this.variants = variants;
            this.pillar = pillar;
            this.ground = ground;
            this.originY = originY;
            float span = Mathf.Max(platform.bodyFromTop.size.y, pillar.bodyFromTop.size.y, ground.bodyFromTop.size.y);
            if (variants != null) foreach (var shape in variants) span = Mathf.Max(span, shape.bodyFromTop.size.y + shape.maximumDescent);
            collisionLevels = 2 + Mathf.CeilToInt((2 * span + 4) / Mathf.Max(0.5f, settings.levelHeight));
            random = new System.Random(seed);
            var start = new JumpForceTrailLevel(0);
            var startPlan = Plan(0);
            start.nodes.Add(new JumpForceTrailNode { id = 0, level = 0, primary = true, topY = originY, previousId = -1,
                biomeIndex = startPlan.bioma, sectionIndex = startPlan.conjunto, sectionStep = startPlan.passo });
            levels.Add(0, start);
            nextTrampoline = GapExcept(0, 0);
            nextFan = GapExcept(nextTrampoline, 0);
            nextCoin = random.Next(2, 6);
            schedules[0] = (nextTrampoline, nextFan, nextCoin);
        }

        // survivableFall: how far below the support it left the player can drop before the camera kills it.
        public void SetCapabilities(float height, float lateralSpeed, float acceleration, float survivableFall, float aimAngle = 0f, float verticalMargin = 0f)
        {
            jumpHeight = Mathf.Max(0.1f, height);
            airSpeed = Mathf.Max(0, lateralSpeed);
            gravity = Mathf.Max(0.1f, acceleration);
            safeFall = Mathf.Max(0.5f, survivableFall);
            maximumAimAngle = Mathf.Clamp(aimAngle, 0f, 89f) * Mathf.Deg2Rad;
            verticalAimMargin = Mathf.Max(0f, verticalMargin) * Mathf.Deg2Rad;
        }

        int GapExcept(int forbidden, int level)
        {
            bool shortGap = Chance(Tier(level).shortSpecialGapChance);
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
        JumpForceBiomePlan Plan(int level)
        {
            if (settings.IsFinalChallenge(level) && settings.finalSections != null && settings.finalSections.Length > 0)
            {
                int local = level - Mathf.Max(1, settings.finalChallengeStart);
                int block = local / 10;
                int section = (int)(unchecked((uint)seed + (uint)block * 2654435761u) % (uint)settings.finalSections.Length);
                var sequence = settings.finalSections[section]?.plataformas;
                var type = sequence != null && sequence.Length > 0 ? sequence[local % sequence.Length] : JumpForcePlatformType.Instavel;
                return new JumpForceBiomePlan(JumpForceBiomeRules.Indice(settings.biomes, level), section, local % 10,
                    type, false, false, type == JumpForcePlatformType.Nuvem);
            }
            return settings.useBiomes ? JumpForceBiomeRules.Planejar(settings.biomes, seed, level)
                : new JumpForceBiomePlan(-1, -1, 0, JumpForcePlatformType.Padrao, false, false);
        }
        int FinalLane(int level)
        {
            int step = (level - Mathf.Max(1, settings.finalChallengeStart)) % 4;
            return (step == 1 ? 1 : step == 3 ? -1 : 0) * ((seed & 1) == 0 ? 1 : -1);
        }
        bool Cloud(JumpForceTrailNode node) => node.kind == JumpForceElementKind.Platform &&
            node.platformVariant == (int)JumpForcePlatformType.Nuvem;
        bool Chance(float chance) => random.NextDouble() < chance;
        float Range(float low, float high) => Mathf.Lerp(low, high, (float)random.NextDouble());
        JumpForceElementShape Shape(JumpForceTrailNode node) => node.level == 0 ? ground : node.kind == JumpForceElementKind.Pillar ? pillar :
            variants != null && node.platformVariant >= 0 && node.platformVariant < variants.Length ? variants[node.platformVariant] : platform;

        float Descent(JumpForceTrailNode node) => Shape(node).maximumDescent;
        float DepartureDescent(JumpForceTrailNode node)
        {
            var shape = Shape(node);
            // Gargalos sao desafios de tempo: a rota exige sair antes de afundar.
            // Colisoes continuam considerando TODA a descida, sem limitar a habilidade.
            return node.mandatoryCloud ? Mathf.Min(shape.maximumDescent,
                Mathf.Max(0, settings.requiredCloudDepartureSeconds - shape.cloudWaitSeconds) * shape.cloudDescentSpeed)
                : shape.maximumDescent;
        }

        public Bounds Envelope(JumpForceTrailNode node)
        {
            var shape = Shape(node);
            var bounds = node.special == JumpForceSpecial.Fan ? (node.fanSpinY != 0 ? shape.rotatingFanBodyFromTop : shape.fanBodyFromTop) : shape.bodyFromTop;
            bounds.center += node.TopPosition(0) + Vector3.right * node.motionCenterOffsetX;
            // A nuvem ocupa todo o caminho da descida, nao somente a altura de nascimento.
            bounds.center -= Vector3.up * (Descent(node) * 0.5f);
            bounds.size += Vector3.up * Descent(node);
            bounds.Expand(node.axis == JumpForceMotionAxis.X ? new Vector3(2 * node.amplitude, 0, 0) : new Vector3(0, 2 * node.amplitude, 0));
            bounds.Expand(settings.separation);
            return bounds;
        }

        static float AmplitudeX(JumpForceTrailNode node) => node.axis == JumpForceMotionAxis.X ? node.amplitude : 0;
        static float AmplitudeY(JumpForceTrailNode node) => node.axis == JumpForceMotionAxis.Y ? node.amplitude : 0;

        bool Reachable(JumpForceTrailNode from, JumpForceTrailNode to)
        {
            float dy = to.topY - from.topY;
            float swing = AmplitudeY(from) + AmplitudeY(to);
            float highest = dy + swing + DepartureDescent(from);
            float lowest = dy - swing - (to.mandatoryCloud ? 0f : Descent(to));
            if (highest > jumpHeight - 0.1f) return false;
            // Opcionais incluem a pior altitude; obrigatorias exigem sair no tempo planejado.
            float reach = Mathf.Min(HorizontalReach(highest), HorizontalReach(lowest));
            if (reach <= 0) return false;
            float distance = Mathf.Abs(to.MotionCenterX - from.MotionCenterX) + AmplitudeX(from) + AmplitudeX(to);
            // Center-to-center, with a steering margin, at every motion phase.
            return distance <= reach * Tier(to.level).reachMargin;
        }

        // Air Speed is steering, not the horizontal impulse of an aimed jump.
        // Compare steering alone with the ballistic range; do not add the two ranges.
        float HorizontalReach(float dy)
        {
            float time = FlightTime(dy);
            if (time <= 0f) return 0f;
            float steeringReach = airSpeed * time;
            float speedSquared = 2f * gravity * jumpHeight;
            float speed = Mathf.Sqrt(speedSquared);
            // Angle from vertical that maximizes range at this destination height.
            float optimum = Mathf.Atan(Mathf.Sqrt(Mathf.Max(0f, speedSquared - 2f * gravity * dy)) / speed);
            float angle = Mathf.Min(maximumAimAngle, optimum);
            if (angle <= verticalAimMargin) return steeringReach;
            float verticalSpeed = speed * Mathf.Cos(angle);
            float discriminant = verticalSpeed * verticalSpeed - 2f * gravity * dy;
            if (discriminant < 0f) return steeringReach;
            float flight = (verticalSpeed + Mathf.Sqrt(discriminant)) / gravity;
            return Mathf.Max(steeringReach, speed * Mathf.Sin(angle) * flight);
        }

        // Air time of a full jump until the feet come back down to dy. The camera comes back down to the support
        // the player left, so the full jump is always survivable; only a target more than safeFall below it is not.
        float FlightTime(float dy)
        {
            if (dy > jumpHeight - 0.1f || dy < -safeFall) return 0;
            return Mathf.Sqrt(2 * jumpHeight / gravity) + Mathf.Sqrt(2 * (jumpHeight - dy) / gravity);
        }

        bool Free(JumpForceTrailNode candidate)
        {
            if (Descent(candidate) > safeFall) return false;
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
            if (node.level >= Mathf.Max(1, settings.finalLevel)) return true;
            bool vertical = node.kind == JumpForceElementKind.Pillar && node.axis == JumpForceMotionAxis.Y && node.amplitude > 0;
            for (int lane = -1; lane <= 1; lane++)
            {
                if (vertical && lane == 0 || settings.IsFinalChallenge(node.level + 1) && lane != FinalLane(node.level + 1)) continue;
                var next = NewNode(node.level + 1, lane, JumpForceElementKind.Platform, true, node.id);
                // Gargalos exigem a geometria real da nuvem; os demais aceitam ponte padrao.
                if (!next.mandatoryCloud && !settings.IsFinalChallenge(next.level)) next.platformVariant = 0;
                var plan = Plan(next.level);
                next.special = plan.PermiteInterativo
                    ? nextFan == next.level && node.special != JumpForceSpecial.Trampoline &&
                        !FanBlockedByTrampoline(next.level) ? JumpForceSpecial.Fan :
                        nextTrampoline == next.level ? JumpForceSpecial.Trampoline : JumpForceSpecial.None
                    : JumpForceSpecial.None;
                if (Reachable(node, next) && Free(next) && !Envelope(node).Intersects(Envelope(next))) return true;
            }
            return false;
        }

        int ChooseVariant()
        {
            if (variants == null || variants.Length <= 1) return 0;
            float sum = 0;
            for (int i = 0; i < variants.Length; i++)
                sum += settings.platformWeights != null && i < settings.platformWeights.Length ? Mathf.Max(0, settings.platformWeights[i]) : 0;
            if (sum <= 0) return 0;
            float choice = Range(0, sum);
            for (int i = 0; i < variants.Length; i++)
            {
                choice -= settings.platformWeights != null && i < settings.platformWeights.Length ? Mathf.Max(0, settings.platformWeights[i]) : 0;
                if (choice < 0) return i;
            }
            return 0;
        }

        JumpForceTrailNode NewNode(int level, int lane, JumpForceElementKind kind, bool primary, int previousId)
        {
            var tier = Tier(level);
            var plan = Plan(level);
            int variant = kind != JumpForceElementKind.Platform ? 0 :
                settings.useBiomes ? primary ? (int)plan.plataforma : 0 : ChooseVariant();
            if (variants == null || variant < 0 || variant >= variants.Length) variant = 0;
            float minimumSpeed = settings.minimumSpeed;
            // Never as fast as the player: a target that outruns the steering is not a fair jump.
            float maximumSpeed = Mathf.Max(minimumSpeed, airSpeed * settings.maximumSpeedFraction);
            float speed = (kind == JumpForceElementKind.Pillar ? pillar.baseSpeed : platform.baseSpeed) * tier.speedMultiplier;
            return new JumpForceTrailNode
            {
                id = level * 2 + (primary ? 0 : 1), level = level, lane = lane, primary = primary,
                previousId = previousId, topY = originY + level * settings.levelHeight,
                kind = kind, platformVariant = variant, axis = JumpForceMotionAxis.X, tier = settings.TierIndex(level),
                biomeIndex = plan.bioma, sectionIndex = plan.conjunto, sectionStep = plan.passo,
                mandatoryCloud = kind == JumpForceElementKind.Platform && variant == (int)JumpForcePlatformType.Nuvem,
                baseSpeed = Mathf.Clamp(speed, minimumSpeed, maximumSpeed), speedVariation = tier.speedVariation,
                minimumSpeed = minimumSpeed, maximumSpeed = maximumSpeed,
                motionSeed = random.Next(), direction = Chance(0.5f) ? -1 : 1,
                fanYaw = lane == -1 ? 180 : lane == 1 ? 0 : Chance(0.5f) ? 180 : 0,
                fanSpinY = lane == 0 ? Range(30, 60) * (Chance(0.5f) ? -1 : 1) : 0
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

        // Finds the largest amplitude that fits, then draws uniformly below it. Drawing first and shrinking
        // afterwards piled most elements up at the space limit, whatever the tier asked for.
        bool FitMoving(JumpForceTrailNode node, JumpForceTrailNode previous, float maximum)
        {
            node.amplitude = Mathf.Max(0, maximum);
            if (!Fit(node, previous)) return false;
            float largest = node.amplitude;
            node.amplitude = largest < MinimumAmplitude ? 0 : Range(MinimumAmplitude, largest);
            return Fit(node, previous);
        }

        int[] ShuffledLanes()
        {
            var lanes = new[] { -1, 0, 1 };
            for (int i = 2; i > 0; i--) { int j = random.Next(i + 1); (lanes[i], lanes[j]) = (lanes[j], lanes[i]); }
            return lanes;
        }

        // Published records are never rewritten, even after their scene objects return to the pool.
        public void ProtectThrough(int level) => protectedThrough = Mathf.Max(protectedThrough, level);

        public bool EnsureThrough(int endLevel)
        {
            endLevel = Mathf.Min(endLevel, Mathf.Max(1, settings.finalLevel));
            int repairs = 0;
            while (lastLevel < endLevel)
            {
                if (GenerateNext()) continue;
                if (lastLevel > protectedThrough && repairs++ < 12)
                {
                    int first = Mathf.Max(protectedThrough + 1, lastLevel - 1);
                    for (int i = first; i <= lastLevel; i++) { levels.Remove(i); schedules.Remove(i); }
                    lastLevel = first - 1;
                    var schedule = schedules[lastLevel];
                    nextTrampoline = schedule.trampoline;
                    nextFan = schedule.fan;
                    nextCoin = schedule.coin;
                    RecoveryCount++;
                    continue;
                }
                // An ordinary reachable bridge takes priority over an impossible special combination.
                if (!GenerateBridge()) return false;
                RecoveryCount++;
            }
            return true;
        }

        bool GenerateBridge()
        {
            int index = lastLevel + 1;
            var lower = levels[lastLevel];
            bool restricted = lower.nodes.Exists(VerticalPillar);
            foreach (var previous in lower.nodes)
                foreach (int lane in new[] { previous.lane, 0, -1, 1 })
                {
                    if (restricted && lane == 0 || settings.IsFinalChallenge(index) && lane != FinalLane(index)) continue;
                    var node = NewNode(index, lane, JumpForceElementKind.Platform, true, previous.id);
                    if (!node.mandatoryCloud && !settings.IsFinalChallenge(index)) node.platformVariant = 0;
                    node.routeRecovery = true;
                    if (!Reachable(previous, node) || !Free(node) || !HasSafeExit(node)) continue;
                    var level = new JumpForceTrailLevel(index);
                    level.nodes.Add(node);
                    levels.Add(index, level);
                    if (index >= nextCoin)
                    {
                        node.hasCoin = true;
                        nextCoin = index + random.Next(2, 6);
                    }
                    PostponeSpecials(index);
                    lastLevel = index;
                    schedules[index] = (nextTrampoline, nextFan, nextCoin);
                    return true;
                }
            return false; // Keep every accepted/published record; the caller can retry without disabling itself.
        }

        // Vale para todas as pistas e sobrevive ao pool: consulta apenas o mapa logico.
        bool FanBlockedByTrampoline(int level)
        {
            for (int below = Mathf.Max(1, level - 2); below < level; below++)
                if (levels.TryGetValue(below, out var lower))
                    foreach (var node in lower.nodes)
                        if (node.special == JumpForceSpecial.Trampoline) return true;
            return false;
        }

        // Specials scheduled at or before this level move to the next one; fan and trampoline stay apart.
        void PostponeSpecials(int index)
        {
            if (nextTrampoline <= index) nextTrampoline = index + 1;
            if (nextFan <= index) nextFan = index + 1;
            if (nextFan == nextTrampoline) nextFan++;
        }

        bool VerticalPillar(JumpForceTrailNode node) =>
            node.kind == JumpForceElementKind.Pillar && node.axis == JumpForceMotionAxis.Y && node.amplitude > 0;

        bool TryMoveX(JumpForceTrailNode node, JumpForceTrailNode previous)
        {
            float oldAmplitude = node.amplitude, oldOffset = node.offset;
            var oldAxis = node.axis;
            node.axis = JumpForceMotionAxis.X;
            // Small but real movement is sufficient to open a passage.
            var tier = Tier(node.level);
            float maximum = node.kind == JumpForceElementKind.Pillar ? tier.pillarAmplitude : tier.platformAmplitude;
            node.amplitude = Mathf.Min(1, maximum);
            node.offset = 0;
            if (node.amplitude >= MinimumAmplitude && Fit(node, previous) && node.amplitude >= MinimumAmplitude) return true;
            node.amplitude = oldAmplitude;
            node.offset = oldOffset;
            node.axis = oldAxis;
            return false;
        }

        bool AddAlternative(JumpForceTrailLevel level, JumpForceTrailNode previous,
            bool restricted, bool oppositePillar)
        {
            if (settings.IsFinalChallenge(level.index) || Cloud(level.Primary)) return false;
            if (level.nodes.Count == 2) return true;
            var primary = level.Primary;
            if (primary.mandatoryCloud) oppositePillar = true;
            foreach (int lane in ShuffledLanes())
            {
                if (lane == primary.lane || (restricted && lane == 0) ||
                    (oppositePillar && !primary.mandatoryCloud && lane != -primary.lane)) continue;
                var node = NewNode(level.index, lane, oppositePillar ? JumpForceElementKind.Pillar :
                    JumpForceElementKind.Platform, false, previous.id);
                var tier = Tier(level.index);
                if (primary.mandatoryCloud &&
                    levels.TryGetValue(level.index - 1, out var lowerMandatory) &&
                    lowerMandatory.nodes.Exists(below => Reachable(below, node))) continue;
                if (Cloud(primary))
                {
                    node.platformVariant = 0;
                    node.previousId = primary.id;
                    if (!Fit(node, primary)) continue;
                    level.nodes.Add(node);
                    return true;
                }
                bool moving = !oppositePillar && !restricted && !Plan(level.index).ApoioSeguro && !Chance(tier.fixedChance);
                // An escape beside the fan can also be reached from the fan's own support.
                if (!(moving ? FitMoving(node, previous, tier.platformAmplitude) : Fit(node, previous)))
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
            var previous = lower.nodes.Find(n => n.id == primary.previousId) ?? lower.Primary;
            if (primary.special == JumpForceSpecial.Fan && primary.lane != 0 && level.nodes.Count == 1)
            {
                // A lone corner fan must cover -3, 0 and +3, starting at its birth lane.
                float oldAmplitude = primary.amplitude;
                primary.axis = JumpForceMotionAxis.X;
                primary.motionCenterOffsetX = -primary.lane * JumpForceTrailNode.LaneSpacing;
                primary.amplitude = JumpForceTrailNode.LaneSpacing;
                primary.direction = -primary.lane;
                if (Reachable(previous, primary) && Free(primary) && HasSafeExit(primary)) return true;
                // Never silently shrink the required sweep. Offer a second support if it cannot fit.
                primary.motionCenterOffsetX = 0;
                primary.amplitude = oldAmplitude;
                return AddAlternative(level, previous, restricted, false);
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

            return true;
        }

        bool GenerateNext()
        {
            int index = lastLevel + 1;
            var lower = levels[lastLevel];
            var tier = Tier(index);
            bool restricted = lower.nodes.Exists(VerticalPillar);
            // After a vertical pillar the level only takes common fixed platforms on the sides: specials wait.
            var plan = Plan(index);
            if (restricted || !plan.PermiteInterativo) PostponeSpecials(index);
            // Um adiamento ou recuperacao nunca pode recolocar vento nos dois niveis de seguranca.
            if (nextFan <= index && FanBlockedByTrampoline(index))
            {
                nextFan = index + 1;
                if (nextFan == nextTrampoline) nextFan++;
            }
            bool trampolineDue = index == nextTrampoline, fanDue = index == nextFan;
            bool coinDue = index >= nextCoin;
            bool fixedRoll = Chance(tier.fixedChance);
            bool pillarRoll = !restricted && !plan.nuvemObrigatoria && plan.PermiteInterativo && !trampolineDue && !fanDue && !coinDue && Chance(tier.pillarChance);
            JumpForceTrailLevel accepted = null;
            var lanes = ShuffledLanes();
            for (int attempt = 0; attempt < 2 && accepted == null; attempt++)
                foreach (var previous in lower.nodes)
                foreach (int lane in lanes)
                {
                    if (accepted != null) break;
                    if ((restricted && lane == 0) || Mathf.Abs(lane - previous.lane) > 1 ||
                        settings.IsFinalChallenge(index) && lane != FinalLane(index)) continue;
                    var kind = attempt == 0 && pillarRoll ? JumpForceElementKind.Pillar : JumpForceElementKind.Platform;
                    var node = NewNode(index, lane, kind, true, previous.id);
                    if (!node.mandatoryCloud && !settings.IsFinalChallenge(index) && (restricted || attempt > 0))
                    {
                        node.routeRecovery = node.platformVariant != 0;
                        node.platformVariant = 0;
                    }
                    node.special = trampolineDue ? JumpForceSpecial.Trampoline :
                        fanDue ? JumpForceSpecial.Fan : JumpForceSpecial.None;
                    bool moving = !fixedRoll && !restricted && !plan.ApoioSeguro && !plan.nuvemObrigatoria && attempt == 0;
                    if (moving && kind == JumpForceElementKind.Pillar && lane == 0 && !settings.IsFinalChallenge(index + 1) &&
                        Plan(index + 1).plataforma != JumpForcePlatformType.Nuvem && Chance(tier.verticalPillarChance))
                        node.axis = JumpForceMotionAxis.Y;
                    if (!(moving ? FitMoving(node, previous, kind == JumpForceElementKind.Pillar ?
                        tier.pillarAmplitude : tier.platformAmplitude) : Fit(node, previous))) continue;
                    var candidate = new JumpForceTrailLevel(index);
                    candidate.nodes.Add(node);
                    levels.Add(index, candidate);
                    bool escapeReady = !Cloud(node) || node.mandatoryCloud || AddAlternative(candidate, previous, restricted, false);
                    if ((!Cloud(node) || node.mandatoryCloud) && Chance(tier.secondPlatformChance)) AddAlternative(candidate, previous, restricted, node.mandatoryCloud);
                    if (escapeReady && ResolveDeadEnds(candidate, lower, restricted)) { accepted = candidate; break; }
                    levels.Remove(index);
                }
            if (accepted == null) return false;
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
            if (trampolineDue)
            {
                nextFan = Mathf.Max(nextFan, index + 3);
                nextTrampoline = index + GapExcept(nextFan - index, index);
            }
            if (fanDue) nextFan = index + GapExcept(nextTrampoline - index, index);
            lastLevel = index;
            schedules[index] = (nextTrampoline, nextFan, nextCoin);
            return true;
        }

        // The route is endless: history far below the camera is dropped so memory stays flat on long runs.
        public void ForgetBelow(int firstLevel)
        {
            obsolete.Clear();
            foreach (int index in levels.Keys)
            {
                if (index >= firstLevel || index >= lastLevel) break; // Keys are sorted.
                obsolete.Add(index);
            }
            foreach (int index in obsolete) { levels.Remove(index); schedules.Remove(index); }
        }
    }
}
