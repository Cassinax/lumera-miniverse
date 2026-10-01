using UnityEngine;

namespace Lumera.JumpForce
{
    [DisallowMultipleComponent]
    public sealed class JumpForceAnimation : MonoBehaviour
    {
        public Animator animator;
        public JumpForcePlayer player;
        public Transform poseTransform;
        [Range(0.01f, 0.99f)] public float chargePauseFrame = 0.5f;
        [Min(0.01f)] public float chargeAnimationSeconds = 0.35f;
        [Min(0.01f)] public float releaseAnimationSeconds = 0.18f;
        [Min(0)] public float transitionSeconds = 0.1f;
        [Tooltip("Rotacao visual para orientar Flying para cima.")]
        public Vector3 flyingEuler = new Vector3(-75, 0, 0);
        [Min(0)] public float poseRotationSpeed = 540;
        [Min(0.01f)] public float runningPlaybackSpeed = 1;
        public string CurrentState { get; private set; } = "Acenar";
        public float JumpProgress => jumpProgress;
        static readonly int Progress = Animator.StringToHash("ProgressoPulo");
        float jumpProgress, launchTime;
        bool charging, launching, intro = true;
        Quaternion initialPose;
        Vector3 initialPosition;
        void Awake()
        {
            if (!animator) animator = GetComponentInChildren<Animator>();
            if (!player) player = GetComponent<JumpForcePlayer>();
            if (!poseTransform && animator) poseTransform = animator.transform;
            if (poseTransform) { initialPose = poseTransform.localRotation; initialPosition = poseTransform.localPosition; }
            if (animator) animator.applyRootMotion = false;
        }
        void State(string state)
        {
            if (!animator || CurrentState == state) return;
            CurrentState = state;
            animator.CrossFadeInFixedTime(Animator.StringToHash(state), transitionSeconds);
        }
        public void BeginCharge()
        {
            intro = false; charging = true; launching = false; jumpProgress = 0;
            if (animator) animator.SetFloat(Progress, 0);
            State("Pulando");
        }
        public void Release()
        {
            intro = charging = false;
            launching = true;
            launchTime = 0;
            jumpProgress = Mathf.Max(jumpProgress, chargePauseFrame);
            State("Pulando");
        }
        public void Land()
        {
            charging = launching = false;
            if (!intro) State("Parado");
        }
        public void ResetIntro()
        {
            intro = true; charging = launching = false; jumpProgress = 0;
            CurrentState = "Acenar";
            if (poseTransform) { poseTransform.localRotation = initialPose; poseTransform.localPosition = initialPosition; }
            if (animator) { animator.speed = 1; animator.SetFloat(Progress, 0); animator.Play("Acenar", 0, 0); }
        }
        void Update()
        {
            if (!animator || !player || player.Dead) return;
            animator.speed = 1;
            if (charging)
            {
                jumpProgress = Mathf.MoveTowards(jumpProgress, chargePauseFrame, chargePauseFrame / Mathf.Max(0.01f, chargeAnimationSeconds) * Time.deltaTime);
                animator.SetFloat(Progress, jumpProgress);
            }
            else if (!player.Grounded)
            {
                intro = false;
                launchTime += Time.deltaTime;
                if (launching && player.Body.linearVelocity.y > 0 && launchTime < releaseAnimationSeconds)
                {
                    jumpProgress = Mathf.Lerp(chargePauseFrame, 1, launchTime / Mathf.Max(0.01f, releaseAnimationSeconds));
                    animator.SetFloat(Progress, jumpProgress);
                }
                else
                {
                    launching = false;
                    State(player.Body.linearVelocity.y > 0.05f ? "Flying" : "Floating");
                }
            }
            else if (player.MoveAmount > 0.01f)
            {
                intro = false;
                State("Running");
                animator.speed = Mathf.Max(0.1f, player.MoveAmount * runningPlaybackSpeed);
            }
            else if (!intro) State("Parado");
            else if (animator.GetCurrentAnimatorStateInfo(0).IsName("Parado")) { intro = false; CurrentState = "Parado"; }
        }
        void LateUpdate()
        {
            if (!poseTransform) return;
            Quaternion desired = initialPose * (CurrentState == "Flying" ? Quaternion.Euler(flyingEuler) : Quaternion.identity);
            poseTransform.localRotation = Quaternion.RotateTowards(poseTransform.localRotation, desired, poseRotationSpeed * Time.deltaTime);
            // Rotate around the hips, not around the feet, to keep the flight pose over the capsule.
            Vector3 pivot = new Vector3(0, 0.65f, 0);
            poseTransform.localPosition = initialPosition + initialPose * pivot - poseTransform.localRotation * pivot;
        }
    }
}
