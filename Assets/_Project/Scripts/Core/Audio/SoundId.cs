namespace Vent.Core.Audio
{
    /// <summary>
    /// Every sound effect in the game. Clips are synthesised at runtime by
    /// <see cref="ProceduralSoundBank"/>, so adding a sound means adding an id and a recipe.
    /// </summary>
    public enum SoundId
    {
        None = 0,
        PistolShot,
        SmgShot,
        DryFire,
        ReloadStart,
        ReloadEnd,
        WeaponDraw,
        HitMarker,
        HeadshotMarker,
        ImpactConcrete,
        ImpactFlesh,
        ZombieGrowl,
        ZombieAttack,
        ZombieHurt,
        ZombieDeath,
        VentRattle,
        PlayerHurt,
        PlayerDeath,
        LevelUp,
        WeaponLevelUp,
        Footstep,
        UiClick,
        UiConfirm,
        ReloadMagIn,
        ReloadRack,
        SlideLock,
        PerkDrop,
        PerkPickup,
        PerkNuke,
    }
}
