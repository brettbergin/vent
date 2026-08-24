using UnityEngine;
using Vent.Core.Audio;
using Vent.Core.Events;
using Vent.Core.Pooling;
using Vent.Core.Services;

namespace Vent.Weapons.Runtime
{
    /// <summary>
    /// Everything a <see cref="Weapon"/> needs from its surroundings, bundled so the weapon's
    /// constructor-equivalent has one parameter. Scene services (pools, audio) are resolved
    /// lazily through <see cref="GameServices"/> because Awake order between the player and
    /// scene infrastructure is not guaranteed.
    /// </summary>
    public sealed class WeaponContext
    {
        public IWeaponHolder Holder;
        public IRecoilReceiver Recoil;
        public Transform ViewModelSocket;
        public int ViewModelLayer = -1;
        public WeaponHudEventChannel HudChannel;
        public WeaponLevelUpEventChannel LevelUpChannel;
        /// <summary>Raised on every damaging hit; payload = headshot. Drives the HUD hit marker.</summary>
        public BoolEventChannel HitChannel;

        private PoolRegistry pools;
        private SfxPlayer sfx;

        public PoolRegistry Pools
        {
            get
            {
                if (pools == null)
                {
                    GameServices.TryGet(out pools);
                }

                return pools;
            }
        }

        public SfxPlayer Sfx
        {
            get
            {
                if (sfx == null)
                {
                    GameServices.TryGet(out sfx);
                }

                return sfx;
            }
        }
    }
}
