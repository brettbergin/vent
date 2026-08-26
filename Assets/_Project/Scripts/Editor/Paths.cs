namespace Vent.Editor
{
    /// <summary>Every generated asset path in one place. Stable paths keep GUIDs stable across regenerations.</summary>
    public static class Paths
    {
        public const string Root = "Assets/_Project";
        public const string Data = Root + "/Data";
        public const string Events = Data + "/Events";
        public const string Materials = Root + "/Materials";
        public const string Prefabs = Root + "/Prefabs";
        public const string Textures = Root + "/Textures";
        public const string Meshes = Root + "/Meshes";
        public const string Scenes = Root + "/Scenes";
        public const string UI = Root + "/UI";
        public const string InputActions = Root + "/Scripts/Player/Input/PlayerInputActions.inputactions";

        public const string BootScene = Scenes + "/Boot.unity";
        public const string MainMenuScene = Scenes + "/MainMenu.unity";
        public const string BuildingScene = Scenes + "/Building.unity";
        public const string BuildingNavMesh = Scenes + "/Building_NavMesh.asset";
        public const string LightingSettings = Scenes + "/VentLighting.lighting";
        public const string PostProcessProfile = Scenes + "/VentPostFx.asset";
        public const string OutdoorPostProcessProfile = Scenes + "/VentPostFxOutdoor.asset";

        public const string Theme = UI + "/VentTheme.tss";
        public const string PanelSettings = UI + "/VentPanelSettings.asset";

        public static readonly string[] Folders =
        {
            Data, Events, Materials, Prefabs, Scenes, UI, Textures, Meshes,
        };
    }
}
