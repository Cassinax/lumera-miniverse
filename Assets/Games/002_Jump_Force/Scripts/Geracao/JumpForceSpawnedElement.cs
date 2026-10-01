using System;
using UnityEngine;

namespace Lumera.JumpForce
{
    // The pooled scene representation; generation records themselves hold no Unity objects.
    public sealed class JumpForceSpawnedElement : MonoBehaviour
    {
        [SerializeField] JumpForceTrailNode node;
        public JumpForceTrailNode Node => node;
        public JumpForceElementShape Shape { get; private set; }
        public Bounds WorldBounds
        {
            get { var bounds = node != null && node.special == JumpForceSpecial.Fan ? Shape.fanBodyFromTop : Shape.bodyFromTop; bounds.center += transform.position + Vector3.up * Shape.topOffset; return bounds; }
        }
        public JumpForceElementKind Kind { get; private set; }
        Renderer[] renderers;
        bool[] rendererEnabled;
        Transform trampoline, fan;
        JumpForceCoin coin;
        Vector3 coinLocalPosition;
        Quaternion fanRotation;
        System.Random motionRandom;
        Vector3 origin;
        bool visible = true;
        Transform[] parts;
        Quaternion[] rotations;
        public float Offset => node.offset;

        public void Initialize(JumpForceElementKind kind)
        {
            Kind = kind;
            var motion = GetComponent<JumpForcePlatformMotion>();
            var pillar = GetComponent<JumpForcePilarArco>();
            float speed = motion ? 4 * motion.amplitude.magnitude / Mathf.Max(0.1f, motion.period) :
                pillar ? 4 * pillar.amplitude / Mathf.Max(0.1f, pillar.period) : 1;
            if (motion) motion.enabled = false;
            if (pillar) pillar.enabled = false; // Retains its standable top and obstacle identity.
            var trampComponent = GetComponentInChildren<JumpForceTrampolim>(true);
            var fanComponent = GetComponentInChildren<JumpForceVentilador>(true);
            trampoline = AccessoryRoot(trampComponent ? trampComponent.transform : null);
            fan = AccessoryRoot(fanComponent ? fanComponent.transform : null);
            if (fan) fanRotation = fan.localRotation;
            coin = GetComponentInChildren<JumpForceCoin>(true);
            if (coin) coinLocalPosition = coin.transform.localPosition;
            var surface = GetComponent<JumpForcePlatform>();
            Bounds body = default, fanBody = default;
            bool first = true, firstFan = true;
            foreach (var collider in GetComponentsInChildren<Collider>(true))
            {
                if (collider.isTrigger || collider.GetComponentInParent<JumpForceCoin>() ||
                    (kind == JumpForceElementKind.Pillar && collider.GetComponent<JumpForcePlatform>())) continue;
                Bounds bounds = ColliderBounds(collider);
                if (fan && collider.transform.IsChildOf(fan))
                {
                    if (firstFan) { fanBody = bounds; firstFan = false; } else fanBody.Encapsulate(bounds);
                    continue;
                }
                if (trampoline && collider.transform.IsChildOf(trampoline)) continue;
                if (first) { body = bounds; first = false; } else body.Encapsulate(bounds);
            }
            if (first) throw new InvalidOperationException("Elemento sem collider solido: " + name);
            float top = surface ? ColliderBounds(surface.GetComponent<BoxCollider>()).max.y : body.max.y;
            if (firstFan) fanBody = body; else fanBody.Encapsulate(body);
            var reference = new Vector3(transform.position.x, top, transform.position.z);
            body.center -= reference;
            fanBody.center -= reference;
            // Reserve both fan orientations without inflating ordinary platform geometry.
            float halfX = Mathf.Max(Mathf.Abs(fanBody.min.x), Mathf.Abs(fanBody.max.x));
            float halfZ = Mathf.Max(Mathf.Abs(fanBody.min.z), Mathf.Abs(fanBody.max.z));
            fanBody = new Bounds(new Vector3(0, fanBody.center.y, 0), new Vector3(2 * halfX, fanBody.size.y, 2 * halfZ));
            Shape = new JumpForceElementShape { bodyFromTop = body, fanBodyFromTop = fanBody,
                topOffset = top - transform.position.y, baseSpeed = Mathf.Max(0.1f, speed) };
            renderers = GetComponentsInChildren<Renderer>(true);
            rendererEnabled = new bool[renderers.Length];
            for (int i = 0; i < renderers.Length; i++) rendererEnabled[i] = renderers[i].enabled;
            parts = GetComponentsInChildren<Transform>(true);
            rotations = new Quaternion[parts.Length];
            for (int i = 0; i < parts.Length; i++) rotations[i] = parts[i].localRotation;
        }

        Transform AccessoryRoot(Transform part)
        {
            if (!part) return null;
            while (part.parent && part.parent != transform) part = part.parent;
            return part == transform ? null : part;
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

        public void Assign(JumpForceTrailNode record, float z, JumpForceScore score)
        {
            node = record;
            motionRandom = new System.Random(record.motionSeed);
            for (int i = 0; i < parts.Length; i++) if (parts[i] != transform) parts[i].localRotation = rotations[i];
            origin = record.TopPosition(z) - Vector3.up * Shape.topOffset;
            transform.position = origin + Axis * record.offset;
            if (trampoline) trampoline.gameObject.SetActive(record.special == JumpForceSpecial.Trampoline);
            if (fan)
            {
                fan.gameObject.SetActive(record.special == JumpForceSpecial.Fan);
                var euler = fanRotation.eulerAngles;
                fan.localRotation = Quaternion.Euler(euler.x, record.fanYaw, euler.z);
            }
            if (coin)
            {
                // Coins share the player's gameplay plane, including when the fan turns.
                coin.transform.localPosition = coinLocalPosition;
                var coinPosition = coin.transform.position;
                coinPosition.z = z;
                if (record.special == JumpForceSpecial.Fan)
                    coinPosition.y = Mathf.Max(coinPosition.y, WorldBounds.max.y + 0.5f);
                coin.transform.position = coinPosition;
                coin.Configure(record, score);
            }
            SetVisible(true);
            gameObject.SetActive(true); // OnEnable resets platform displacement before the player samples it.
        }

        Vector3 Axis => node.axis == JumpForceMotionAxis.Y ? Vector3.up : Vector3.right;
        public Vector3 Step(float dt, float speedVariation, Vector2 changeInterval)
        {
            if (node.amplitude <= 0) return Vector3.zero;
            node.untilSpeedChange -= dt;
            if (node.untilSpeedChange <= 0)
            {
                node.currentSpeed = Mathf.Max(0.1f, node.baseSpeed + ((float)motionRandom.NextDouble() * 2 - 1) * speedVariation);
                node.untilSpeedChange = Mathf.Lerp(Mathf.Max(0.1f, changeInterval.x), Mathf.Max(changeInterval.x, changeInterval.y), (float)motionRandom.NextDouble());
            }
            float next = Mathf.MoveTowards(node.offset, node.direction * node.amplitude, node.currentSpeed * dt);
            return Axis * (next - node.offset);
        }

        public void ApplyStep(Vector3 step, bool blocked)
        {
            if (blocked) { node.direction = -node.direction; return; }
            node.offset += Vector3.Dot(step, Axis);
            transform.position = origin + Axis * node.offset;
            if (Mathf.Abs(node.offset - node.direction * node.amplitude) < 0.0001f) node.direction = -node.direction;
        }

        public void SetVisible(bool value)
        {
            if (visible == value) return;
            visible = value;
            for (int i = 0; i < renderers.Length; i++) if (renderers[i]) renderers[i].enabled = value && rendererEnabled[i];
        }
        public void Release()
        {
            gameObject.SetActive(false);
            SetVisible(true);
            node = null;
        }
    }
}
