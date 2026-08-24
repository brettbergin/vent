using UnityEngine;
using Vent.Core.Utility;

namespace Vent.Enemies.Runtime
{
    /// <summary>
    /// Procedural animation for the greybox zombie body (a stack of primitives). Drives limb
    /// swing from locomotion speed, a lunge on attack, a hit flinch, and a sink-into-the-floor
    /// death. Rendering feedback (hit flash) uses a <see cref="MaterialPropertyBlock"/> so
    /// pooled zombies never instantiate materials.
    /// </summary>
    public sealed class ZombieAnimator : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        [Header("Parts")]
        [SerializeField] private Transform body;
        [SerializeField] private Transform head;
        [SerializeField] private Transform leftArm;
        [SerializeField] private Transform rightArm;
        [SerializeField] private Renderer[] renderers;

        [Header("Tuning")]
        [SerializeField, Min(0f)] private float strideFrequency = 2.2f;
        [SerializeField, Min(0f)] private float armSwingDegrees = 35f;
        [SerializeField, Min(0f)] private float bodyLeanDegrees = 12f;
        [SerializeField, Min(0f)] private float bobAmount = 0.05f;
        [SerializeField] private Color flashColor = new(1f, 0.35f, 0.25f);

        private float locomotion;
        private float phase;
        private float attackT = 1f;
        private float attackDuration = 0.4f;
        private float deathT = 1f;
        private float deathDuration = 2f;
        private float flash;
        private Vector3 flinchAxis;
        private float flinch;
        private Vector3 bodyRest;
        private MaterialPropertyBlock block;
        private Color baseColor = Color.white;

        public void Configure(Transform bodyT, Transform headT, Transform leftArmT, Transform rightArmT, Renderer[] rends)
        {
            body = bodyT;
            head = headT;
            leftArm = leftArmT;
            rightArm = rightArmT;
            renderers = rends;
        }

        private void Awake()
        {
            block = new MaterialPropertyBlock();
            if (body != null)
            {
                bodyRest = body.localPosition;
            }

            if (renderers != null && renderers.Length > 0 && renderers[0] != null && renderers[0].sharedMaterial != null
                && renderers[0].sharedMaterial.HasProperty(BaseColorId))
            {
                baseColor = renderers[0].sharedMaterial.GetColor(BaseColorId);
            }
        }

        public void ResetPose()
        {
            locomotion = 0f;
            attackT = 1f;
            deathT = 1f;
            flash = 0f;
            flinch = 0f;
            transform.localPosition = Vector3.zero;
            transform.localScale = Vector3.one;
            ApplyFlash(0f);
        }

        public void SetLocomotion(float speed01) => locomotion = Mathf.Clamp01(speed01);

        public void PlayAttack(float windupSeconds)
        {
            attackDuration = Mathf.Max(0.1f, windupSeconds + 0.25f);
            attackT = 0f;
        }

        public void PlayDeath(float corpseSeconds)
        {
            deathDuration = Mathf.Max(0.1f, corpseSeconds);
            deathT = 0f;
        }

        public void Flinch(Vector3 hitDirection, float strength)
        {
            flash = 1f;
            flinch = Mathf.Max(flinch, strength);
            flinchAxis = Vector3.Cross(Vector3.up, hitDirection).normalized;
        }

        private void Update()
        {
            float dt = Time.deltaTime;

            // Death: fold forward and sink through the floor, then the zombie is released.
            if (deathT < 1f)
            {
                deathT = Mathf.Min(1f, deathT + dt / deathDuration);
                float fold = Mathf.SmoothStep(0f, 1f, Mathf.Min(1f, deathT * 3f));
                float sink = Mathf.Max(0f, deathT - 0.4f) / 0.6f;
                transform.localRotation = Quaternion.Euler(80f * fold, 0f, 0f);
                transform.localPosition = new Vector3(0f, -1.6f * sink * sink, 0f);
                ApplyFlash(0f);
                return;
            }

            transform.localRotation = Quaternion.identity;

            // Locomotion cycle
            phase += dt * strideFrequency * Mathf.PI * 2f * Mathf.Lerp(0.3f, 1f, locomotion);
            float swing = Mathf.Sin(phase) * armSwingDegrees * locomotion;

            // Attack lunge: arms raise together and the body lunges forward.
            float lunge = 0f;
            if (attackT < 1f)
            {
                attackT = Mathf.Min(1f, attackT + dt / attackDuration);
                lunge = Mathf.Sin(attackT * Mathf.PI);
            }

            // Flinch decays quickly.
            flinch = MathUtil.Damp(flinch, 0f, 10f, dt);
            flash = MathUtil.Damp(flash, 0f, 14f, dt);

            if (body != null)
            {
                Quaternion lean = Quaternion.Euler(bodyLeanDegrees * (0.5f + 0.5f * locomotion) + 25f * lunge, 0f, 0f);
                Quaternion flinchRot = Quaternion.AngleAxis(-18f * flinch, flinchAxis);
                body.localRotation = flinchRot * lean;
                body.localPosition = bodyRest + Vector3.up * (Mathf.Abs(Mathf.Sin(phase)) * bobAmount * locomotion) + Vector3.forward * (0.25f * lunge);
            }

            if (leftArm != null)
            {
                leftArm.localRotation = Quaternion.Euler(-70f - swing - 50f * lunge, 0f, 10f);
            }

            if (rightArm != null)
            {
                rightArm.localRotation = Quaternion.Euler(-70f + swing - 50f * lunge, 0f, -10f);
            }

            if (head != null)
            {
                head.localRotation = Quaternion.Euler(-10f * lunge + 8f * flinch, Mathf.Sin(phase * 0.5f) * 8f * locomotion, 0f);
            }

            ApplyFlash(flash);
        }

        private void ApplyFlash(float amount)
        {
            if (renderers == null || block == null)
            {
                return;
            }

            Color c = Color.Lerp(baseColor, flashColor, amount);
            block.SetColor(BaseColorId, c);
            foreach (Renderer r in renderers)
            {
                if (r != null)
                {
                    r.SetPropertyBlock(block);
                }
            }
        }
    }
}
