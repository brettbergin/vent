using System.Collections.Generic;
using UnityEngine;
using Vent.Core.Events;
using Vent.Core.Utility;
using Vent.Weapons.Data;

namespace Vent.Weapons.Runtime
{
    /// <summary>
    /// The player's two weapon slots. Builds runtime <see cref="Weapon"/>s from definitions,
    /// routes trigger/reload/switch commands to the active one, and credits kills back to the
    /// weapon that made them (by matching <see cref="KillInfo.Killer"/>).
    ///
    /// Exactly two slots by design: the game's whole progression loop is "level your primary
    /// and secondary", so the inventory is deliberately not a general container.
    /// </summary>
    public sealed class WeaponInventory : MonoBehaviour
    {
        public const int SlotCount = 2;

        [Header("Loadout")]
        [SerializeField] private WeaponDefinition primary;
        [SerializeField] private WeaponDefinition secondary;

        [Header("Scene wiring")]
        [SerializeField, Tooltip("Under the camera; weapons are parented here.")]
        private Transform viewModelSocket;

        [Header("Events")]
        [SerializeField] private WeaponHudEventChannel hudChannel;
        [SerializeField] private WeaponLevelUpEventChannel levelUpChannel;
        [SerializeField] private KillEventChannel killChannel;
        [SerializeField] private BoolEventChannel hitChannel;

        private readonly List<Weapon> weapons = new(SlotCount);
        private WeaponContext context;
        private int currentIndex = -1;
        private bool initialized;

        public Weapon Current => currentIndex >= 0 && currentIndex < weapons.Count ? weapons[currentIndex] : null;
        public IReadOnlyList<Weapon> Weapons => weapons;

        public WeaponDefinition Primary
        {
            get => primary;
            set => primary = value;
        }

        public WeaponDefinition Secondary
        {
            get => secondary;
            set => secondary = value;
        }

        public Transform ViewModelSocket
        {
            get => viewModelSocket;
            set => viewModelSocket = value;
        }

        public WeaponHudEventChannel HudChannel
        {
            get => hudChannel;
            set => hudChannel = value;
        }

        public WeaponLevelUpEventChannel LevelUpChannel
        {
            get => levelUpChannel;
            set => levelUpChannel = value;
        }

        public KillEventChannel KillChannel
        {
            get => killChannel;
            set => killChannel = value;
        }

        public BoolEventChannel HitChannel
        {
            get => hitChannel;
            set => hitChannel = value;
        }

        /// <summary>Build the weapons. Called once by the holder (the player) during its Awake.</summary>
        public void Initialize(IWeaponHolder holder, IRecoilReceiver recoil)
        {
            if (initialized)
            {
                return;
            }

            initialized = true;
            context = new WeaponContext
            {
                Holder = holder,
                Recoil = recoil,
                ViewModelSocket = viewModelSocket != null ? viewModelSocket : transform,
                ViewModelLayer = Layers.WeaponViewIndex,
                HudChannel = hudChannel,
                LevelUpChannel = levelUpChannel,
                HitChannel = hitChannel,
            };

            foreach (WeaponDefinition def in new[] { primary, secondary })
            {
                if (def == null)
                {
                    continue;
                }

                weapons.Add(Weapon.Create(def, weapons.Count, context));
            }

            if (weapons.Count > 0)
            {
                SelectSlot(0);
            }
        }

        private void OnEnable() => killChannel?.Subscribe(OnKill);
        private void OnDisable() => killChannel?.Unsubscribe(OnKill);

        // ------------------------------------------------------------------ commands

        public void PullTrigger() => Current?.PullTrigger();
        public void ReleaseTrigger() => Current?.ReleaseTrigger();
        public void Reload() => Current?.TryReload();

        public void SetAiming(bool aiming)
        {
            foreach (Weapon w in weapons)
            {
                w.SetAiming(aiming);
            }
        }

        public void SelectSlot(int index)
        {
            if (index < 0 || index >= weapons.Count || index == currentIndex)
            {
                return;
            }

            Current?.Holster();
            currentIndex = index;
            Current.Draw();
        }

        /// <summary>Move to the next/previous slot, wrapping.</summary>
        public void Cycle(int direction)
        {
            if (weapons.Count == 0)
            {
                return;
            }

            int next = (currentIndex + (direction >= 0 ? 1 : -1) + weapons.Count) % weapons.Count;
            SelectSlot(next);
        }

        /// <summary>Enable/disable firing on all weapons without holstering (menus, death).</summary>
        public void SetWeaponsActive(bool active)
        {
            foreach (Weapon w in weapons)
            {
                w.SetActiveControl(active);
            }
        }

        public void RefillAllAmmo()
        {
            foreach (Weapon w in weapons)
            {
                w.RefillAmmo();
            }
        }

        public void ResetForNewRun()
        {
            foreach (Weapon w in weapons)
            {
                w.ResetForNewRun();
            }

            if (weapons.Count > 0)
            {
                currentIndex = -1;
                SelectSlot(0);
            }
        }

        /// <summary>Re-publish the active weapon's HUD state (e.g. after the HUD scene loads).</summary>
        public void PublishHud() => Current?.PublishHud();

        // ------------------------------------------------------------------ kill credit

        private void OnKill(KillInfo info)
        {
            if (info.Killer == null)
            {
                return;
            }

            foreach (Weapon w in weapons)
            {
                if (ReferenceEquals(info.Killer, w))
                {
                    w.GrantExperience(info.Experience);
                    return;
                }
            }
        }
    }
}
