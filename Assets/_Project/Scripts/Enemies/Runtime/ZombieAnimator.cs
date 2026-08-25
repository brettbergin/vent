using UnityEngine;
using Vent.Core.Utility;

namespace Vent.Enemies.Runtime
{
    /// <summary>
    /// The jointed greybox rig the prefab factory builds; the animator drives these pivots.
    /// Every field is a pivot (rotation applied about it), never a mesh.
    /// </summary>
    [System.Serializable]
    public sealed class ZombieRig
    {
        public Transform Hips;
        public Transform Spine;
        public Transform Head;
        public Transform Jaw;
        public Transform LeftShoulder, LeftElbow;
        public Transform RightShoulder, RightElbow;
        public Transform LeftHip, LeftKnee;
        public Transform RightHip, RightKnee;
    }

    /// <summary>
    /// Procedural animation for the zombie rig: a shuffling walk with bent knees and dragging
    /// feet, hunched spine, lolling head, arms held out and reaching, an attack lunge, a hit
    /// flinch, a full-body stagger, and a death topple in the direction of the killing shot.
    /// Per-spawn variation (height, tint, stride, arm pose) keeps a crowd from looking cloned.
    /// Rendering feedback (hit flash, tint) uses a <see cref="MaterialPropertyBlock"/> so pooled
    /// zombies never instantiate materials.
    /// </summary>
    public sealed class ZombieAnimator : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        [SerializeField] private ZombieRig rig = new();
        [SerializeField, Tooltip("Renderers that take the skin tint and hit flash (skin only; clothes keep their colour).")]
        private Renderer[] skinRenderers;

        [Header("Walk")]
        [SerializeField, Min(0f)] private float strideFrequency = 1.6f;
        [SerializeField, Min(0f)] private float thighSwingDegrees = 28f;
        [SerializeField, Min(0f)] private float kneeBendDegrees = 40f;
        [SerializeField, Min(0f)] private float hipBob = 0.035f;
        [SerializeField, Min(0f)] private float hipSway = 6f;
        [SerializeField, Min(0f)] private float hunchDegrees = 22f;

        [Header("Arms")]
        [SerializeField, Min(0f)] private float reachDegrees = 75f;
        [SerializeField, Min(0f)] private float armSwingDegrees = 12f;
        [SerializeField, Min(0f)] private float elbowBendDegrees = 25f;

        [Header("Variation")]
        [SerializeField] private Vector2 heightScaleRange = new(0.92f, 1.1f);
        [SerializeField] private Color skinTintA = new(0.42f, 0.55f, 0.36f);
        [SerializeField] private Color skinTintB = new(0.55f, 0.52f, 0.42f);
        [SerializeField] private Color flashColor = new(1f, 0.35f, 0.25f);

        private float locomotion;
        private float phase;
        private float attackT = 1f;
        private float attackDuration = 0.4f;
        private float staggerT = 1f;
        private float staggerDuration = 0.45f;
        private float deathT = 1f;
        private float deathDuration = 2f;
        private float toppleSign = 1f;
        private float toppleYaw;
        private float flash;
        private Vector3 flinchAxis = Vector3.right;
        private float flinch;
        private float jawOpen;
        private MaterialPropertyBlock block;
        private Color baseColor = Color.white;

        // Per-spawn variation
        private float strideScale = 1f;
        private float reachOffset;
        private float armAsymmetry;
        private float headTilt;
        private float lollPhase;

        public void Configure(ZombieRig parts, Renderer[] skin)
        {
            rig = parts;
            skinRenderers = skin;
        }

        private void Awake() => block = new MaterialPropertyBlock();

        /// <summary>Fresh spawn: neutral pose plus a new random body.</summary>
        public void ResetPose()
        {
            locomotion = 0f;
            attackT = 1f;
            staggerT = 1f;
            deathT = 1f;
            flash = 0f;
            flinch = 0f;
            jawOpen = 0f;
            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.identity;

            float height = Random.Range(heightScaleRange.x, heightScaleRange.y);
            transform.localScale = new Vector3(Mathf.Lerp(0.96f, 1.06f, Random.value), height, 1f);
            strideScale = Random.Range(0.85f, 1.2f);
            reachOffset = Random.Range(-15f, 15f);
            armAsymmetry = Random.Range(-20f, 20f);
            headTilt = Random.Range(-14f, 14f);
            lollPhase = Random.value * 10f;
            baseColor = Color.Lerp(skinTintA, skinTintB, Random.value);
            ApplyFlash(0f);
        }

        public void SetLocomotion(float speed01) => locomotion = Mathf.Clamp01(speed01);

        public void PlayAttack(float windupSeconds)
        {
            attackDuration = Mathf.Max(0.1f, windupSeconds + 0.25f);
            attackT = 0f;
        }

        public void PlayStagger(float seconds)
        {
            staggerDuration = Mathf.Max(0.1f, seconds);
            staggerT = 0f;
            flash = 1f;
        }

        /// <summary>Topple in the direction the killing shot was travelling, then sink and release.</summary>
        public void PlayDeath(float corpseSeconds, Vector3 hitDirection)
        {
            deathDuration = Mathf.Max(0.1f, corpseSeconds);
            deathT = 0f;
            hitDirection.y = 0f;
            // Shot from the front pushes the body backwards (falls onto its back); from behind, forwards.
            toppleSign = Vector3.Dot(hitDirection, transform.forward) >= 0f ? 1f : -1f;
            toppleYaw = Random.Range(-25f, 25f);
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

            if (deathT < 1f)
            {
                TickDeath(dt);
                return;
            }

            transform.localRotation = Quaternion.identity;

            // Gait
            float speedMul = Mathf.Lerp(0.35f, 1f, locomotion);
            phase += dt * strideFrequency * strideScale * Mathf.PI * 2f * speedMul;
            float gait = Mathf.Sin(phase);           // -1..1 left/right leg
            float gaitAbs = Mathf.Abs(Mathf.Cos(phase));

            float lunge = 0f;
            if (attackT < 1f)
            {
                attackT = Mathf.Min(1f, attackT + dt / attackDuration);
                lunge = Mathf.Sin(attackT * Mathf.PI);
            }

            float stagger = 0f;
            if (staggerT < 1f)
            {
                staggerT = Mathf.Min(1f, staggerT + dt / staggerDuration);
                stagger = Mathf.Sin(staggerT * Mathf.PI) * (1f - 0.5f * staggerT);
            }

            flinch = MathUtil.Damp(flinch, 0f, 10f, dt);
            flash = MathUtil.Damp(flash, 0f, 14f, dt);
            jawOpen = MathUtil.Damp(jawOpen, lunge > 0.2f || stagger > 0.3f ? 1f : 0.15f + 0.1f * Mathf.Sin(Time.time * 3f + lollPhase), 8f, dt);

            float loll = Mathf.Sin(Time.time * 0.9f + lollPhase);

            // Hips: bob and sway with the stride; a stagger throws them back.
            if (rig.Hips != null)
            {
                rig.Hips.localPosition = new Vector3(0f, 0.95f + gaitAbs * hipBob * locomotion - 0.08f * stagger, -0.12f * stagger + 0.2f * lunge);
                rig.Hips.localRotation = Quaternion.Euler(0f, gait * hipSway * locomotion, -gait * 3f * locomotion);
            }

            // Spine: hunched, leaning into the walk, lunging on attack, whipped back by a stagger, flinching sideways.
            if (rig.Spine != null)
            {
                Quaternion flinchRot = Quaternion.AngleAxis(-18f * flinch, flinchAxis);
                rig.Spine.localRotation = flinchRot * Quaternion.Euler(
                    hunchDegrees * (0.6f + 0.4f * locomotion) + 22f * lunge - 40f * stagger,
                    -gait * 4f * locomotion + 6f * loll,
                    3f * loll);
            }

            if (rig.Head != null)
            {
                // Looks up to compensate for the hunch, lolls side to side, tilts with the variation, snaps on lunge.
                rig.Head.localRotation = Quaternion.Euler(-hunchDegrees * 0.7f - 12f * lunge + 10f * flinch + 25f * stagger,
                    Mathf.Sin(phase * 0.5f) * 10f * locomotion + 8f * loll, headTilt + 10f * loll);
            }

            if (rig.Jaw != null)
            {
                rig.Jaw.localRotation = Quaternion.Euler(28f * jawOpen, 0f, 0f);
            }

            // Arms: held out and reaching; a slow independent drift, a small swing, a thrust on attack, flung up on stagger.
            float reachL = reachDegrees + reachOffset + armAsymmetry;
            float reachR = reachDegrees + reachOffset - armAsymmetry;
            float swing = Mathf.Sin(phase) * armSwingDegrees * locomotion;
            float drift = Mathf.Sin(Time.time * 1.3f + lollPhase) * 6f;
            Arm(rig.LeftShoulder, rig.LeftElbow, -reachL - swing - 30f * lunge + 35f * stagger, drift, 12f, lunge);
            Arm(rig.RightShoulder, rig.RightElbow, -reachR + swing - 30f * lunge + 35f * stagger, -drift, -12f, lunge);

            // Legs: thigh swings, knee bends on the swing-through, and the body never quite straightens.
            Leg(rig.LeftHip, rig.LeftKnee, gait, locomotion);
            Leg(rig.RightHip, rig.RightKnee, -gait, locomotion);

            ApplyFlash(flash);
        }

        private void Arm(Transform shoulder, Transform elbow, float pitch, float yaw, float roll, float lunge)
        {
            if (shoulder != null)
            {
                shoulder.localRotation = Quaternion.Euler(pitch, yaw, roll);
            }

            if (elbow != null)
            {
                // Elbows straighten as the arms thrust forward on the lunge.
                elbow.localRotation = Quaternion.Euler(-elbowBendDegrees * (1f - 0.8f * lunge), 0f, 0f);
            }
        }

        private void Leg(Transform hip, Transform knee, float gait, float amount)
        {
            if (hip != null)
            {
                hip.localRotation = Quaternion.Euler(gait * thighSwingDegrees * amount, 0f, 0f);
            }

            if (knee != null)
            {
                // Knee bends most when the leg is behind and swinging through; always a little bent.
                float bend = Mathf.Max(0f, -gait) * kneeBendDegrees * amount + 8f;
                knee.localRotation = Quaternion.Euler(bend, 0f, 0f);
            }
        }

        private void TickDeath(float dt)
        {
            deathT = Mathf.Min(1f, deathT + dt / deathDuration);
            // Topple: accelerating fall about the feet to 88°, a small bounce, then sink through the floor.
            float fall = Mathf.SmoothStep(0f, 1f, Mathf.Min(1f, deathT * 2.4f));
            float bounce = Mathf.Sin(Mathf.Clamp01((deathT - 0.42f) / 0.25f) * Mathf.PI) * 4f;
            float sink = Mathf.Max(0f, deathT - 0.55f) / 0.45f;
            transform.localRotation = Quaternion.Euler(toppleSign * (88f * fall - bounce), toppleYaw * fall, 0f);
            transform.localPosition = new Vector3(0f, -1.4f * sink * sink, 0f);

            // Limbs go slack.
            float slack = fall;
            if (rig.Spine != null) rig.Spine.localRotation = Quaternion.Euler(hunchDegrees * (1f - slack), 0f, 0f);
            if (rig.Head != null) rig.Head.localRotation = Quaternion.Euler(-hunchDegrees * 0.7f * (1f - slack) + 20f * slack, 0f, headTilt);
            Arm(rig.LeftShoulder, rig.LeftElbow, Mathf.Lerp(-reachDegrees, -10f, slack), 0f, 20f * slack, 0f);
            Arm(rig.RightShoulder, rig.RightElbow, Mathf.Lerp(-reachDegrees, -10f, slack), 0f, -20f * slack, 0f);
            Leg(rig.LeftHip, rig.LeftKnee, 0f, 0f);
            Leg(rig.RightHip, rig.RightKnee, 0f, 0f);
            ApplyFlash(0f);
        }

        private void ApplyFlash(float amount)
        {
            if (skinRenderers == null || block == null)
            {
                return;
            }

            block.SetColor(BaseColorId, Color.Lerp(baseColor, flashColor, amount));
            foreach (Renderer r in skinRenderers)
            {
                if (r != null)
                {
                    r.SetPropertyBlock(block);
                }
            }
        }
    }
}
