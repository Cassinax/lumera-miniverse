using System;
using UnityEngine;

namespace Lumera.JumpForce
{
    public sealed class JumpForceSpawnedElement : MonoBehaviour
    {
        [SerializeField] JumpForceTrailNode node;
        public JumpForceTrailNode Node => node;
        public JumpForceElementShape Shape { get; private set; }
        public JumpForceElementKind Kind { get; private set; }
        public int Variant { get; private set; }
        public float Offset => node.offset;
        Renderer[] renderers;
        bool[] rendererEnabled;
        Transform[] parts;
        Quaternion[] rotations;
        Vector3[] positions, scales;
        JumpForcePlatform[] surfaces;
        JumpForcePlatformAbility ability;
        JumpForceCoin coin;
        JumpForceInteractive interactive;
        Vector3 coinLocalPosition;
        Rigidbody body;
        public Bounds WorldBounds
        {
            get
            {
                var b = node != null && node.special == JumpForceSpecial.Fan
                    ? (node.fanSpinY != 0 ? Shape.rotatingFanBodyFromTop : Shape.fanBodyFromTop) : Shape.bodyFromTop;
                b.center += transform.position + Vector3.up * Shape.topOffset;
                return ability ? ability.IncluirPartes(b) : b;
            }
        }
        public void Initialize(JumpForceElementKind kind, int variant = 0)
        {
            Kind = kind; Variant = variant;
            body = GetComponent<Rigidbody>();
            ability = GetComponent<JumpForcePlatformAbility>();
            var motion = GetComponent<JumpForcePlatformMotion>();
            var pillar = GetComponent<JumpForcePilarArco>();
            float speed = motion ? 4f * motion.amplitude.magnitude / Mathf.Max(0.1f, motion.period)
                : pillar ? 4f * pillar.amplitude / Mathf.Max(0.1f, pillar.period) : 1;
            coin = GetComponentInChildren<JumpForceCoin>(true);
            if (coin) coinLocalPosition = coin.transform.localPosition;
            Bounds bounds = default;
            bool first = true;
            foreach (var collider in GetComponentsInChildren<Collider>(true))
            {
                if (collider.isTrigger || collider.GetComponentInParent<JumpForceCoin>() ||
                    (kind == JumpForceElementKind.Pillar && collider.GetComponent<JumpForcePlatform>())) continue;
                var next = ColliderBounds(collider);
                if (first) { bounds = next; first = false; } else bounds.Encapsulate(next);
            }
            if (first) throw new InvalidOperationException("Elemento sem collider solido: " + name);
            var surface = GetComponent<JumpForcePlatform>();
            float top = surface ? ColliderBounds(surface.GetComponent<BoxCollider>()).max.y : bounds.max.y;
            bounds.center -= new Vector3(transform.position.x, top, transform.position.z);
            Shape = new JumpForceElementShape { bodyFromTop = bounds, fanBodyFromTop = bounds,
                rotatingFanBodyFromTop = bounds, topOffset = top - transform.position.y, baseSpeed = Mathf.Max(0.1f, speed),
                maximumDescent = ability && ability.tipo == JumpForcePlatformType.Nuvem ? Mathf.Max(0, ability.descidaMaxima) : 0,
                cloudWaitSeconds = ability ? Mathf.Max(0, ability.esperaNuvem) : 0,
                cloudDescentSpeed = ability ? Mathf.Max(0, ability.velocidadeDescida) : 0 };
            renderers = GetComponentsInChildren<Renderer>(true);
            rendererEnabled = new bool[renderers.Length];
            for (int i = 0; i < renderers.Length; i++) rendererEnabled[i] = renderers[i].enabled;
            parts = GetComponentsInChildren<Transform>(true);
            rotations = new Quaternion[parts.Length]; positions = new Vector3[parts.Length]; scales = new Vector3[parts.Length];
            for (int i = 0; i < parts.Length; i++)
            {
                rotations[i] = parts[i].localRotation; positions[i] = parts[i].localPosition; scales[i] = parts[i].localScale;
            }
            surfaces = GetComponentsInChildren<JumpForcePlatform>(true);
        }
        public void ConfigureFanBounds(Bounds localFan)
        {
            var shape = Shape;
            // Prefabs do catalogo possuem escala raiz unitaria e origens de montagem comuns.
            localFan.center -= Vector3.up * shape.topOffset;
            var fan = localFan;
            float halfX = Mathf.Max(Mathf.Abs(fan.min.x), Mathf.Abs(fan.max.x));
            float halfZ = Mathf.Max(Mathf.Abs(fan.min.z), Mathf.Abs(fan.max.z));
            fan = new Bounds(new Vector3(0, fan.center.y, 0), new Vector3(halfX * 2, fan.size.y, halfZ * 2));
            float radius = Mathf.Sqrt(halfX * halfX + halfZ * halfZ);
            var rotating = new Bounds(new Vector3(0, fan.center.y, 0), new Vector3(radius * 2, fan.size.y, radius * 2));
            fan.Encapsulate(shape.bodyFromTop); rotating.Encapsulate(shape.bodyFromTop);
            shape.fanBodyFromTop = fan; shape.rotatingFanBodyFromTop = rotating;
            Shape = shape;
        }
        // These are corridor limits, independent of the finite wall height and physics sync timing.
        public static bool TryGetWallLimits(Collider[] walls, out float left, out float right)
        {
            left = float.NegativeInfinity;
            right = float.PositiveInfinity;
            float firstX = float.PositiveInfinity, lastX = float.NegativeInfinity;
            foreach (var wall in walls)
            {
                if (!wall || !wall.enabled || !wall.gameObject.activeInHierarchy) continue;
                var bounds = ColliderBounds(wall);
                if (bounds.center.x < firstX) { firstX = bounds.center.x; left = bounds.max.x; }
                if (bounds.center.x > lastX) { lastX = bounds.center.x; right = bounds.min.x; }
            }
            return firstX < lastX && left < right;
        }

        public static Bounds ColliderBounds(Collider collider)
        {
            Bounds local;
            if (collider is BoxCollider box) local = new Bounds(box.center, box.size);
            else if (collider is SphereCollider sphere) local = new Bounds(sphere.center, Vector3.one * sphere.radius * 2);
            else if (collider is MeshCollider mesh && mesh.sharedMesh) local = mesh.sharedMesh.bounds;
            else return collider.bounds;
            Bounds result = new Bounds(collider.transform.TransformPoint(local.center), Vector3.zero);
            for (int i = 0; i < 8; i++)
            {
                var sign = new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1);
                result.Encapsulate(collider.transform.TransformPoint(local.center + Vector3.Scale(local.extents, sign)));
            }
            return result;
        }


        public void Assign(JumpForceTrailNode record, float z, JumpForceScore score,
            JumpForcePlayer player = null, JumpForceCamera camera = null, JumpForceInteractive accessory = null, Transform pool = null)
        {
            node = record;
            for (int i = 0; i < parts.Length; i++)
            {
                if (!parts[i] || parts[i] == transform) continue;
                parts[i].localPosition = positions[i];
                parts[i].localRotation = rotations[i];
                parts[i].localScale = scales[i];
            }
            transform.rotation = Quaternion.identity;
            Vector3 centro = record.TopPosition(z) - Vector3.up * Shape.topOffset;
            transform.position = centro;
            if (body) body.position = centro;
            if (ability) ability.Reiniciar(record, player, camera);
            foreach (var surface in surfaces) if (surface) surface.instantJump = record.special == JumpForceSpecial.Fan && record.fanSpinY != 0;
            interactive = accessory;
            if (interactive) interactive.Montar(transform, record, pool);
            if (coin)
            {
                coin.transform.localPosition = coinLocalPosition;
                var pos = coin.transform.position; pos.z = z;
                if (record.special == JumpForceSpecial.Fan) pos.y = Mathf.Max(pos.y, WorldBounds.max.y + 0.5f);
                coin.transform.position = pos;
                coin.Configure(record, score);
            }
            SetVisible(true);
            gameObject.SetActive(true);
            ConfigurarMovimento(centro);
        }
        void ConfigurarMovimento(Vector3 centro)
        {
            Vector3 inicio = centro;
            centro += Vector3.right * node.motionCenterOffsetX;
            Vector3 eixo = node.axis == JumpForceMotionAxis.Y ? Vector3.up : Vector3.right;
            var motion = GetComponent<JumpForcePlatformMotion>();
            if (motion) motion.Configurar(centro, eixo, node.amplitude, node.baseSpeed, node.direction);
            var pillar = GetComponent<JumpForcePilarArco>();
            if (pillar) pillar.Configurar(centro, node.axis == JumpForceMotionAxis.Y ? JumpForcePilarArco.Eixo.Y : JumpForcePilarArco.Eixo.X,
                node.amplitude, node.baseSpeed, node.direction);
            if (node.motionCenterOffsetX != 0 && body) { body.position = inicio; transform.position = inicio; }
        }
        public void SetVisible(bool value)
        {
            for (int i = 0; i < renderers.Length; i++) if (renderers[i]) renderers[i].enabled = value && rendererEnabled[i];
            if (interactive) interactive.SetVisible(value);
        }
        public void DesativarConteudo()
        {
            if (interactive) interactive.gameObject.SetActive(false);
            if (coin) coin.gameObject.SetActive(false);
        }
        // Ponto unico de retorno: encerra habilidades, recolhe pecas e devolve o interativo ao pool.
        public void Release()
        {
            if (interactive) { interactive.Liberar(); interactive = null; }
            gameObject.SetActive(false);
            if (ability) ability.ResetarParaPool();
            if (body && !body.isKinematic) { body.linearVelocity = Vector3.zero; body.angularVelocity = Vector3.zero; }
            foreach (var surface in surfaces) if (surface) surface.instantJump = false;
            SetVisible(true);
            node = null;
        }
    }
}
