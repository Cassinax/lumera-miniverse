using System.Collections.Generic;
using UnityEngine;

namespace Lumera.JumpForce
{
    // Celebracao somente com corpos ja existentes. Guarda o estado antes de liberar a fisica.
    [DisallowMultipleComponent]
    public sealed class JumpForceVictoryFloat : MonoBehaviour
    {
        [Tooltip("Rotacao suave na vitoria, em graus por segundo. Zero apenas libera as travas.")]
        [Min(0)] public float rotationSpeed = 12f;
        struct BodyState
        {
            public Rigidbody body;
            public RigidbodyConstraints constraints;
            public CollisionDetectionMode collisionMode;
            public bool kinematic, gravity, collisions;
            public Vector3 position;
            public Quaternion rotation;
        }
        struct JointState
        {
            public ConfigurableJoint joint;
            public ConfigurableJointMotion x, y, z, ax, ay, az;
        }
        readonly List<BodyState> bodies = new();
        readonly List<JointState> joints = new();
        readonly List<Behaviour> motors = new();
        readonly HashSet<Rigidbody> seen = new();

        public void Begin(JumpForcePlayer player, JumpForceSpawner spawner)
        {
            Restore();
            if (!player) return;
            AddBody(player.Body);
            var view = spawner && spawner.followCamera ? spawner.followCamera.Visao : null;
            if (!spawner || !view) return;
            var planes = GeometryUtility.CalculateFrustumPlanes(view);
            foreach (var element in spawner.ActiveElements.Values)
            {
                if (!element || !element.gameObject.activeInHierarchy) continue;
                bool visible = false;
                foreach (var renderer in element.GetComponentsInChildren<Renderer>())
                    visible |= renderer.enabled && !renderer.forceRenderingOff &&
                        GeometryUtility.TestPlanesAABB(planes, renderer.bounds);
                if (!visible) continue;
                foreach (var motor in element.GetComponentsInChildren<MonoBehaviour>())
                    if (motor.enabled && (motor is JumpForcePlatformMotion ||
                        motor is JumpForcePilarArco || motor is JumpForceGirador))
                    {
                        motors.Add(motor);
                        motor.enabled = false;
                    }
                foreach (var body in element.GetComponentsInChildren<Rigidbody>()) AddBody(body);
                var ability = element.GetComponent<JumpForcePlatformAbility>();
                if (ability)
                    foreach (var part in ability.partes)
                        if (part && part.gameObject.activeInHierarchy) AddBody(part);
            }
        }

        void AddBody(Rigidbody body)
        {
            if (!body || !seen.Add(body)) return;
            bodies.Add(new BodyState { body = body, constraints = body.constraints,
                collisionMode = body.collisionDetectionMode, kinematic = body.isKinematic,
                gravity = body.useGravity, collisions = body.detectCollisions,
                position = body.position, rotation = body.rotation });
            foreach (var joint in body.GetComponents<ConfigurableJoint>())
            {
                joints.Add(new JointState { joint = joint, x = joint.xMotion, y = joint.yMotion,
                    z = joint.zMotion, ax = joint.angularXMotion, ay = joint.angularYMotion, az = joint.angularZMotion });
                joint.xMotion = joint.yMotion = joint.zMotion = ConfigurableJointMotion.Free;
                joint.angularXMotion = joint.angularYMotion = joint.angularZMotion = ConfigurableJointMotion.Free;
            }
            body.collisionDetectionMode = CollisionDetectionMode.Discrete;
            body.detectCollisions = false;
            body.useGravity = false;
            body.constraints = RigidbodyConstraints.None;
            body.isKinematic = false;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Random.onUnitSphere * Mathf.Max(0, rotationSpeed) * Mathf.Deg2Rad;
        }

        public void Restore()
        {
            foreach (var state in bodies)
            {
                var body = state.body;
                if (!body) continue;
                if (!body.isKinematic)
                {
                    body.linearVelocity = Vector3.zero;
                    body.angularVelocity = Vector3.zero;
                }
                body.position = state.position;
                body.rotation = state.rotation;
                body.constraints = state.constraints;
                body.useGravity = state.gravity;
                body.isKinematic = state.kinematic;
                body.detectCollisions = state.collisions;
                body.collisionDetectionMode = state.collisionMode;
            }
            foreach (var state in joints)
            {
                if (!state.joint) continue;
                state.joint.xMotion = state.x; state.joint.yMotion = state.y; state.joint.zMotion = state.z;
                state.joint.angularXMotion = state.ax; state.joint.angularYMotion = state.ay; state.joint.angularZMotion = state.az;
            }
            foreach (var motor in motors) if (motor) motor.enabled = true;
            bodies.Clear(); joints.Clear(); motors.Clear(); seen.Clear();
        }
        void OnDisable() => Restore();
    }
}
