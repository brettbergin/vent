namespace Vent.Weapons.Progression
{
    /// <summary>Engine-free view of a level table, so progression logic can be tested without assets.</summary>
    public interface IWeaponLevelTable
    {
        int MaxLevel { get; }

        /// <summary>Experience required to go from <paramref name="level"/> to <c>level + 1</c>.</summary>
        int ExperienceToNext(int level);
    }
}
