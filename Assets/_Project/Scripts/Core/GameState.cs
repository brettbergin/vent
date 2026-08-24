namespace Vent.Core
{
    /// <summary>Top-level application state. Owned by the game manager; broadcast to UI and systems.</summary>
    public enum GameState
    {
        Boot,
        MainMenu,
        Playing,
        Paused,
        GameOver,
    }
}
