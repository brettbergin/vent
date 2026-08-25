using UnityEngine;
using Vent.Core.Audio;
using Vent.Core.Damage;
using Vent.Core.Events;
using Vent.Core.Pooling;
using Vent.Core.Utility;
using Vent.Weapons.Data;
using Vent.Weapons.Progression;
using Vent.Weapons.VFX;
using Vent.Weapons.View;

namespace Vent.Weapons.Runtime
{
    public enum WeaponState
    {
        /// <summary>Not the active weapon; GameObject is inactive.</summary>
        Holstered,
        /// <summary>Being raised; cannot fire yet.</summary>
        Drawing,
        /// <summary>Can fire or reload.</summary>
        Ready,
        Reloading,
    }

    /// <summary>
    /// Runtime instance of a gun. Created from a <see cref="WeaponDefinition"/> by
    /// <see cref="WeaponInventory"/>; owns ammo, level, bloom and the view-model.
    ///
    /// Firing is hitscan: a single <see cref="Physics.Raycast"/> from the holder's aim ray,
    /// perturbed by the current spread cone. Hits resolve through <see cref="Hitbox"/> so
    /// headshot multipliers apply without the weapon knowing anything about zombies.
    ///
    /// Handling details a real gun has: a tactical reload keeps the chambered round (+1), an empty
    /// reload is slower and ends with racking the action, recoil climbs under sustained fire and
    /// damage falls off with distance. The arithmetic lives in <see cref="Ballistics"/>.
    ///
    /// The state machine is a plain enum + switch: with four states and two timers, classes
    /// per state would obscure more than they clarify.
    /// </summary>
    public sealed class Weapon : MonoBehaviour
    {
        private const float MaxRaycastFallback = 200f;

        public WeaponDefinition Definition { get; private set; }
        public WeaponProgression Progression { get; private set; }
        public WeaponStats Stats { get; private set; }
        public WeaponState State { get; private set; } = WeaponState.Holstered;
        public int SlotIndex { get; private set; }
        public int Magazine { get; private set; }
        public int Reserve { get; private set; }

        /// <summary>Current cone half-angle in degrees, including movement and bloom.</summary>
        public float CurrentSpreadDegrees { get; private set; }

        /// <summary>Total shots fired this run (stats screen).</summary>
        public int ShotsFired { get; private set; }
        public int ShotsHit { get; private set; }

        private WeaponContext ctx;
        private WeaponViewModel viewModel;
        private Cooldown fireCooldown;
        private float stateTimer;
        private float stateDuration;
        private bool reloadFromEmpty;
        private int reloadPhase;
        private int consecutiveShots;
        private float lastShotTime = float.NegativeInfinity;
        private float bloom;
        private bool triggerHeld;
        private bool triggerPulled;
        private bool aiming;
        private bool active = true;
        private float lastPublishedSpread = -1f;
        private float nextSpreadPublish;

        // ------------------------------------------------------------------ construction

        /// <summary>Factory: builds the GameObject, the component and the view-model.</summary>
        public static Weapon Create(WeaponDefinition definition, int slotIndex, WeaponContext context)
        {
            var go = new GameObject($"Weapon_{definition.DisplayName}");
            go.transform.SetParent(context.ViewModelSocket, worldPositionStays: false);
            Weapon weapon = go.AddComponent<Weapon>();
            weapon.Initialize(definition, slotIndex, context);
            return weapon;
        }

        private void Initialize(WeaponDefinition definition, int slotIndex, WeaponContext context)
        {
            Definition = definition;
            SlotIndex = slotIndex;
            ctx = context;

            IWeaponLevelTable table = definition.LevelCurve != null
                ? definition.LevelCurve
                : (IWeaponLevelTable)FlatLevelTable.Instance;
            Progression = new WeaponProgression(table);
            Progression.LevelUp += OnLevelUp;

            RecomputeStats();
            Magazine = Stats.MagazineSize + 1; // one chambered
            Reserve = definition.StartingReserve;

            if (definition.ViewModelPrefab != null)
            {
                GameObject vm = Instantiate(definition.ViewModelPrefab, transform);
                vm.name = "ViewModel";
                viewModel = vm.GetComponent<WeaponViewModel>();
                if (viewModel == null)
                {
                    viewModel = vm.AddComponent<WeaponViewModel>();
                }

                if (ctx.ViewModelLayer >= 0)
                {
                    Layers.SetRecursively(vm, ctx.ViewModelLayer);
                }
            }

            State = WeaponState.Holstered;
            gameObject.SetActive(false);
        }

        private void OnDestroy()
        {
            if (Progression != null)
            {
                Progression.LevelUp -= OnLevelUp;
            }
        }

        // ------------------------------------------------------------------ commands

        public void Draw()
        {
            gameObject.SetActive(true);
            State = WeaponState.Drawing;
            stateTimer = Definition.DrawSeconds;
            triggerPulled = false;
            viewModel?.PlayDraw(Definition.DrawSeconds);
            ctx.Sfx?.Play2D(SoundId.WeaponDraw, 0.5f);
            PublishHud();
        }

        public void Holster()
        {
            State = WeaponState.Holstered;
            triggerHeld = false;
            triggerPulled = false;

            // A muzzle flash may still be parented under the muzzle; return it before we go inactive,
            // otherwise it would sit checked-out of its pool until this weapon is drawn again.
            foreach (PooledObject effect in GetComponentsInChildren<PooledObject>(includeInactive: true))
            {
                effect.Release();
            }

            gameObject.SetActive(false);
        }

        public void PullTrigger()
        {
            triggerHeld = true;
            triggerPulled = true;
        }

        public void ReleaseTrigger() => triggerHeld = false;

        public void SetAiming(bool value)
        {
            aiming = value;
            viewModel?.SetAiming(value);
        }

        /// <summary>Suppress firing (menus, death) without holstering.</summary>
        public void SetActiveControl(bool value)
        {
            active = value;
            if (!value)
            {
                triggerHeld = false;
                triggerPulled = false;
            }
        }

        /// <summary>Rounds the gun holds when topped up: the magazine plus one chambered.</summary>
        public int Capacity => Stats.MagazineSize + 1;

        public bool TryReload()
        {
            if (State != WeaponState.Ready || Reserve <= 0 || Magazine >= Capacity)
            {
                return false;
            }

            reloadFromEmpty = Magazine == 0;
            reloadPhase = 0;
            stateDuration = reloadFromEmpty ? Stats.EmptyReloadSeconds : Stats.ReloadSeconds;
            State = WeaponState.Reloading;
            stateTimer = stateDuration;
            viewModel?.PlayReload(stateDuration, reloadFromEmpty);
            ctx.Sfx?.Play2D(SoundId.ReloadStart, 0.7f); // magazine out
            PublishHud();
            return true;
        }

        /// <summary>Top up magazine and reserve to their maximums (level transitions).</summary>
        public void RefillAmmo()
        {
            Magazine = Capacity;
            Reserve = Definition.MaxReserve;
            if (State == WeaponState.Reloading)
            {
                State = WeaponState.Ready;
            }

            viewModel?.SetChambered(true);
            PublishHud();
        }

        public void GrantExperience(int amount)
        {
            Progression.AddExperience(amount);
            PublishHud();
        }

        public void ResetForNewRun()
        {
            Progression.Reset();
            RecomputeStats();
            Magazine = Capacity;
            Reserve = Definition.StartingReserve;
            bloom = 0f;
            consecutiveShots = 0;
            viewModel?.SetChambered(true);
            ShotsFired = 0;
            ShotsHit = 0;
            fireCooldown.Reset();
            if (State != WeaponState.Holstered)
            {
                State = WeaponState.Ready;
            }

            PublishHud();
        }

        // ------------------------------------------------------------------ update loop

        private void Update()
        {
            float dt = Time.deltaTime;
            float now = Time.time;

            bloom = MathUtil.Damp(bloom, 0f, Definition.SpreadRecovery, dt);
            float movement = ctx.Holder?.MovementFactor ?? 0f;
            float spread = (Definition.BaseSpread + Definition.MovementSpread * movement + bloom) * Stats.SpreadScale;
            CurrentSpreadDegrees = aiming ? spread * Definition.AimSpreadScale : spread;

            if (viewModel != null && ctx.Holder != null)
            {
                viewModel.SetMotion(movement, ctx.Holder.IsGrounded, ctx.Holder.LookDelta);
            }

            // The crosshair blooms with movement and recovers between shots; push those changes
            // at ~20 Hz rather than every frame.
            if (now >= nextSpreadPublish && Mathf.Abs(CurrentSpreadDegrees - lastPublishedSpread) > 0.05f)
            {
                nextSpreadPublish = now + 0.05f;
                PublishHud();
            }

            switch (State)
            {
                case WeaponState.Drawing:
                    stateTimer -= dt;
                    if (stateTimer <= 0f)
                    {
                        State = WeaponState.Ready;
                        PublishHud();
                    }

                    break;

                case WeaponState.Reloading:
                    stateTimer -= dt;
                    TickReloadPhases(1f - stateTimer / Mathf.Max(0.01f, stateDuration));
                    if (stateTimer <= 0f)
                    {
                        CompleteReload();
                    }

                    break;

                case WeaponState.Ready:
                    TickReady(now);
                    break;
            }

            triggerPulled = false;
        }

        private void TickReady(float now)
        {
            if (!active)
            {
                return;
            }

            bool wantsFire = Definition.FireMode == FireMode.Automatic ? triggerHeld : triggerPulled;
            if (!wantsFire)
            {
                return;
            }

            if (Magazine <= 0)
            {
                // Empty: auto-reload if we can, otherwise a dry click (once per pull).
                if (!TryReload() && triggerPulled)
                {
                    ctx.Sfx?.Play2D(SoundId.DryFire, 0.6f);
                }

                return;
            }

            if (fireCooldown.TryConsume(now, Stats.SecondsBetweenShots))
            {
                Fire();
            }
        }

        /// <summary>Sounds keyed to the reload animation: mag in near the middle, rack near the end if it was empty.</summary>
        private void TickReloadPhases(float progress)
        {
            if (reloadPhase == 0 && progress >= WeaponViewModel.ReloadMagInAt)
            {
                reloadPhase = 1;
                ctx.Sfx?.Play2D(SoundId.ReloadMagIn, 0.7f);
            }

            if (reloadPhase == 1 && reloadFromEmpty && progress >= WeaponViewModel.ReloadRackAt)
            {
                reloadPhase = 2;
                ctx.Sfx?.Play2D(SoundId.ReloadRack, 0.8f);
            }
        }

        private void CompleteReload()
        {
            int target = Ballistics.RoundsAfterReload(Stats.MagazineSize, hadRoundChambered: !reloadFromEmpty);
            int needed = Mathf.Max(0, target - Magazine);
            int taken = Mathf.Min(needed, Reserve);
            Magazine += taken;
            Reserve -= taken;
            State = WeaponState.Ready;
            viewModel?.SetChambered(Magazine > 0);
            ctx.Sfx?.Play2D(SoundId.ReloadEnd, 0.6f);
            PublishHud();
        }

        // ------------------------------------------------------------------ firing

        private void Fire()
        {
            float now = Time.time;
            Magazine--;
            ShotsFired++;
            bloom = Mathf.Min(bloom + Definition.SpreadPerShot, Definition.MaxBloom);

            // Sustained fire climbs: each consecutive shot kicks harder until the ramp tops out.
            consecutiveShots = now - lastShotTime <= Stats.SecondsBetweenShots + Definition.RecoilRampReset ? consecutiveShots + 1 : 1;
            lastShotTime = now;
            float ramp = Ballistics.RecoilRamp(consecutiveShots, Definition.RecoilRampShots, Definition.RecoilRampMultiplier);

            // Recoil: the view kicks up and slightly sideways; aiming steadies it.
            float recoilScale = (aiming ? Definition.AimRecoilScale : 1f) * ramp;
            var kick = new Vector2(
                Random.Range(Definition.VerticalKickRange.x, Definition.VerticalKickRange.y),
                Random.Range(Definition.HorizontalKickRange.x, Definition.HorizontalKickRange.y)) * recoilScale;
            ctx.Recoil?.AddRecoil(kick);
            viewModel?.OnShot(ramp, Magazine == 0);

            // Hitscan.
            Ray aim = ctx.Holder?.AimRay ?? new Ray(transform.position, transform.forward);
            Vector3 direction = MathUtil.RandomInCone(aim.direction, CurrentSpreadDegrees * Mathf.Deg2Rad);
            float range = Definition.Range > 0f ? Definition.Range : MaxRaycastFallback;
            Vector3 endPoint = aim.origin + direction * range;

            bool hitSomething = false;
            if (Physics.Raycast(aim.origin, direction, out RaycastHit hit, range, Layers.ShootableMask, QueryTriggerInteraction.Ignore))
            {
                endPoint = hit.point;
                hitSomething = ApplyHit(hit, direction);
            }

            SpawnMuzzleFlash();
            SpawnTracer(endPoint);
            SpawnShellCasing();
            ctx.Sfx?.Play2D(Definition.FireSound, Definition.FireVolume);
            ctx.NoiseChannel?.Raise(new NoiseInfo(aim.origin));
            if (Magazine == 0)
            {
                ctx.Sfx?.Play2D(SoundId.SlideLock, 0.5f);
            }

            if (hitSomething)
            {
                ShotsHit++;
            }

            PublishHud();
        }

        /// <summary>Resolve a raycast hit: damage a hitbox if present, otherwise just spawn an impact.</summary>
        private bool ApplyHit(in RaycastHit hit, Vector3 direction)
        {
            if (Hitbox.TryResolve(hit.collider, out Hitbox hitbox, out IDamageable damageable) && damageable.IsAlive)
            {
                float damage = Stats.Damage * Ballistics.DamageScale(hit.distance, Definition.FalloffStart, Definition.FalloffEnd, Definition.MinDamageScale);
                var info = new DamageInfo(damage, DamageKind.Bullet, this, hit.point, hit.normal, direction);
                DamageResult result = hitbox != null
                    ? hitbox.Hit(info.WithAmount(damage, false))
                    : damageable.ApplyDamage(info);

                if (!result.Ignored)
                {
                    bool headshot = hitbox != null && hitbox.IsHead;
                    ctx.HitChannel?.Raise(headshot);
                    SpawnImpact(Definition.BloodImpactPrefab, hit.point, hit.normal);
                    ctx.Sfx?.Play2D(headshot ? SoundId.HeadshotMarker : SoundId.HitMarker, 0.5f);
                    return true;
                }
            }

            SpawnImpact(Definition.ImpactPrefab, hit.point, hit.normal);
            SfxPlayer.TryPlayAt(SoundId.ImpactConcrete, hit.point, 0.4f);
            return false;
        }

        private void SpawnMuzzleFlash()
        {
            if (Definition.MuzzleFlashPrefab == null || ctx.Pools == null)
            {
                return;
            }

            Transform muzzle = viewModel != null ? viewModel.Muzzle : transform;
            var flash = ctx.Pools.Spawn<MuzzleFlash>(Definition.MuzzleFlashPrefab, muzzle.position, muzzle.rotation);
            flash?.Play(muzzle, Definition.MuzzleFlashScale);
        }

        private void SpawnShellCasing()
        {
            if (Definition.ShellCasingPrefab == null || ctx.Pools == null || viewModel == null)
            {
                return;
            }

            Transform port = viewModel.EjectionPort;
            var shell = ctx.Pools.Spawn<ShellCasing>(Definition.ShellCasingPrefab, port.position, port.rotation);
            shell?.Eject(port.right, port.up);
        }

        private void SpawnTracer(Vector3 endPoint)
        {
            if (Definition.TracerPrefab == null || ctx.Pools == null)
            {
                return;
            }

            Transform muzzle = viewModel != null ? viewModel.Muzzle : transform;
            var tracer = ctx.Pools.Spawn<Tracer>(Definition.TracerPrefab, muzzle.position, Quaternion.identity);
            tracer?.Show(muzzle.position, endPoint);
        }

        private void SpawnImpact(GameObject prefab, Vector3 point, Vector3 normal)
        {
            if (prefab == null || ctx.Pools == null)
            {
                return;
            }

            ctx.Pools.Spawn(prefab, point + normal * 0.01f, Quaternion.LookRotation(normal));
        }

        // ------------------------------------------------------------------ progression

        private void OnLevelUp(int newLevel)
        {
            int previousMagazine = Stats.MagazineSize;
            RecomputeStats();

            // A level-up tops the magazine up: immediate, tangible reward.
            Magazine = Capacity;
            viewModel?.SetChambered(true);
            Reserve = Mathf.Min(Definition.MaxReserve, Reserve + (Stats.MagazineSize - previousMagazine));

            ctx.LevelUpChannel?.Raise(new WeaponLevelUpInfo(Definition.DisplayName, newLevel));
            ctx.Sfx?.Play2D(SoundId.WeaponLevelUp, 0.7f);
        }

        private void RecomputeStats()
        {
            WeaponLevelModifiers mods = Definition.LevelCurve != null
                ? Definition.LevelCurve.Evaluate(Progression.Level)
                : WeaponLevelModifiers.Identity;
            Stats = new WeaponStats(Definition, mods);
        }

        /// <summary>Push a HUD snapshot; cheap enough to call on every state change.</summary>
        public void PublishHud()
        {
            if (State == WeaponState.Holstered)
            {
                return; // only the weapon in hand owns the HUD
            }

            lastPublishedSpread = CurrentSpreadDegrees;
            ctx.HudChannel?.Raise(new WeaponHudInfo(
                Definition.DisplayName,
                SlotIndex,
                Magazine,
                Reserve,
                Progression.Level,
                Progression.Progress01,
                State == WeaponState.Reloading,
                CurrentSpreadDegrees));
        }

        /// <summary>Fallback table for definitions without a curve: level 1 forever.</summary>
        private sealed class FlatLevelTable : IWeaponLevelTable
        {
            public static readonly FlatLevelTable Instance = new();
            public int MaxLevel => 1;
            public int ExperienceToNext(int level) => int.MaxValue;
        }
    }
}
