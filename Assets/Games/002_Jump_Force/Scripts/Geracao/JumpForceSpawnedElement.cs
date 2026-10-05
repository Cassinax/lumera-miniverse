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
        bool visible = true;
        Transform[] parts;
        Quaternion[] rotations;
        public float Offset => node.offset;

        public void Initialize(JumpForceElementKind kind)
        {
            Kind = kind;

            var motion = GetComponent<JumpForcePlatformMotion>();
            var pillar = GetComponent<JumpForcePilarArco>();

            // A velocidade-base continua sendo registrada como dado do elemento,
            // mas o SpawnedElement não controla nem desativa mais o movimento físico.
            float speed =
                motion
                    ? 4f * motion.amplitude.magnitude / Mathf.Max(0.1f, motion.period)
                    : pillar
                        ? 4f * pillar.amplitude / Mathf.Max(0.1f, pillar.period)
                        : 1f;

            var trampComponent =
                GetComponentInChildren<JumpForceTrampolim>(true);

            var fanComponent =
                GetComponentInChildren<JumpForceVentilador>(true);

            trampoline =
                AccessoryRoot(trampComponent ? trampComponent.transform : null);

            fan =
                AccessoryRoot(fanComponent ? fanComponent.transform : null);

            if (fan)
                fanRotation = fan.localRotation;

            coin =
                GetComponentInChildren<JumpForceCoin>(true);

            if (coin)
                coinLocalPosition = coin.transform.localPosition;

            var surface =
                GetComponent<JumpForcePlatform>();

            Bounds body = default;
            Bounds fanBody = default;

            bool first = true;
            bool firstFan = true;

            foreach (var collider in GetComponentsInChildren<Collider>(true))
            {
                if (
                    collider.isTrigger ||
                    collider.GetComponentInParent<JumpForceCoin>() ||
                    (
                        kind == JumpForceElementKind.Pillar &&
                        collider.GetComponent<JumpForcePlatform>()
                    )
                )
                    continue;

                Bounds bounds = ColliderBounds(collider);

                if (fan && collider.transform.IsChildOf(fan))
                {
                    if (firstFan)
                    {
                        fanBody = bounds;
                        firstFan = false;
                    }
                    else
                    {
                        fanBody.Encapsulate(bounds);
                    }

                    continue;
                }

                if (trampoline && collider.transform.IsChildOf(trampoline))
                    continue;

                if (first)
                {
                    body = bounds;
                    first = false;
                }
                else
                {
                    body.Encapsulate(bounds);
                }
            }

            if (first)
                throw new InvalidOperationException(
                    "Elemento sem collider solido: " + name
                );

            float top =
                surface
                    ? ColliderBounds(surface.GetComponent<BoxCollider>()).max.y
                    : body.max.y;

            if (firstFan)
                fanBody = body;
            else
                fanBody.Encapsulate(body);

            Vector3 reference =
                new Vector3(
                    transform.position.x,
                    top,
                    transform.position.z
                );

            body.center -= reference;
            fanBody.center -= reference;

            float halfX =
                Mathf.Max(
                    Mathf.Abs(fanBody.min.x),
                    Mathf.Abs(fanBody.max.x)
                );

            float halfZ =
                Mathf.Max(
                    Mathf.Abs(fanBody.min.z),
                    Mathf.Abs(fanBody.max.z)
                );

            fanBody =
                new Bounds(
                    new Vector3(0, fanBody.center.y, 0),
                    new Vector3(
                        2f * halfX,
                        fanBody.size.y,
                        2f * halfZ
                    )
                );

            Shape =
                new JumpForceElementShape
                {
                    bodyFromTop = body,
                    fanBodyFromTop = fanBody,
                    topOffset = top - transform.position.y,
                    baseSpeed = Mathf.Max(0.1f, speed)
                };

            renderers =
                GetComponentsInChildren<Renderer>(true);

            rendererEnabled =
                new bool[renderers.Length];

            for (int i = 0; i < renderers.Length; i++)
                rendererEnabled[i] = renderers[i].enabled;

            parts =
                GetComponentsInChildren<Transform>(true);

            rotations =
                new Quaternion[parts.Length];

            for (int i = 0; i < parts.Length; i++)
                rotations[i] = parts[i].localRotation;
        }

        Transform AccessoryRoot(Transform part)
        {
            if (!part) return null;
            while (part.parent && part.parent != transform) part = part.parent;
            return part == transform ? null : part;
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

        public void Assign(JumpForceTrailNode record, float z, JumpForceScore score)
        {
            node = record;

            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i] != transform)
                    parts[i].localRotation = rotations[i];
            }

            Vector3 spawnPosition =
                record.TopPosition(z) - Vector3.up * Shape.topOffset;

            Rigidbody rb = GetComponent<Rigidbody>();

            if (rb)
            {
                rb.position = spawnPosition;
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
            else
            {
                transform.position = spawnPosition;
            }

            // Informa ao novo motor físico qual é a origem deste elemento
            // reutilizado pelo pool.
            var motion = GetComponent<JumpForcePlatformMotion>();

            if (motion)
                motion.SetOrigin(spawnPosition);

            if (trampoline)
                trampoline.gameObject.SetActive(
                    record.special == JumpForceSpecial.Trampoline
                );

            if (fan)
            {
                fan.gameObject.SetActive(
                    record.special == JumpForceSpecial.Fan
                );

                var euler = fanRotation.eulerAngles;

                fan.localRotation =
                    Quaternion.Euler(
                        euler.x,
                        record.fanYaw,
                        euler.z
                    );
            }

            if (coin)
            {
                coin.transform.localPosition = coinLocalPosition;

                Vector3 coinPosition = coin.transform.position;
                coinPosition.z = z;

                if (record.special == JumpForceSpecial.Fan)
                {
                    coinPosition.y =
                        Mathf.Max(
                            coinPosition.y,
                            WorldBounds.max.y + 0.5f
                        );
                }

                coin.transform.position = coinPosition;
                coin.Configure(record, score);
            }

            SetVisible(true);
            gameObject.SetActive(true);
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
