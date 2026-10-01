using UnityEngine;

namespace Lumera.JumpForce
{
    [DisallowMultipleComponent]
    public sealed class JumpForceAnimation : MonoBehaviour
    {
        public Animator animator;
        [Range(0.01f, 0.99f)] public float chargePauseFrame = 0.5f;
        [Min(0.01f)] public float chargeAnimationSeconds = 0.35f;
        [Min(0.01f)] public float releaseAnimationSeconds = 0.45f;
        [Min(0)] public float transitionSeconds = 0.08f;
        static readonly int Progress = Animator.StringToHash("ProgressoPulo");
        static readonly int Idle = Animator.StringToHash("Parado");
        static readonly int Wave = Animator.StringToHash("Acenar");
        static readonly int Jump = Animator.StringToHash("Pulando");
        float jumpProgress;
        bool inJump, charging;
        public float JumpProgress => jumpProgress;

        void Awake()
        {
            if (!animator) animator = GetComponentInChildren<Animator>();
            if (animator) animator.applyRootMotion = false;
        }
        public void BeginCharge()
        {
            if (!animator) return;
            inJump = charging = true;
            jumpProgress = 0;
            animator.SetFloat(Progress, 0);
            animator.CrossFadeInFixedTime(Jump, transitionSeconds);
        }
        public void Release()
        {
            if (!animator) return;
            if (!inJump) animator.CrossFadeInFixedTime(Jump, transitionSeconds);
            inJump = true;
            charging = false;
            jumpProgress = Mathf.Max(jumpProgress, chargePauseFrame);
            animator.SetFloat(Progress, jumpProgress);
        }
        public void Land()
        {
            if (!animator || !inJump) return;
            inJump = charging = false;
            animator.CrossFadeInFixedTime(Idle, transitionSeconds);
        }
        public void ResetIntro()
        {
            inJump = charging = false;
            jumpProgress = 0;
            if (animator) { animator.SetFloat(Progress, 0); animator.Play(Wave, 0, 0); }
        }
        void Update()
        {
            if (!animator || !inJump) return;
            float end = charging ? chargePauseFrame : 1;
            float speed = charging ? chargePauseFrame / Mathf.Max(0.01f, chargeAnimationSeconds)
                : (1 - chargePauseFrame) / Mathf.Max(0.01f, releaseAnimationSeconds);
            jumpProgress = Mathf.MoveTowards(jumpProgress, end, speed * Time.deltaTime);
            animator.SetFloat(Progress, jumpProgress);
        }
    }
}
