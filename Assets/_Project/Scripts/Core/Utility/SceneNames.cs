namespace Vent.Core.Utility
{
    /// <summary>Scene names in build order. The editor bootstrap and the game manager both read from here.</summary>
    public static class SceneNames
    {
        public const string Boot = "Boot";
        public const string MainMenu = "MainMenu";
        public const string Building = "Building";

        public static readonly string[] BuildOrder = { Boot, MainMenu, Building };
    }
}
