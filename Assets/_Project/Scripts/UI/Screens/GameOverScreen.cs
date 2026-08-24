using UnityEngine;
using UnityEngine.UIElements;
using Vent.Core.Events;

namespace Vent.UI.Screens
{
    public sealed class GameOverScreen : UIScreen
    {
        [Header("Requests out")]
        [SerializeField] private VoidEventChannel restartRequested;
        [SerializeField] private VoidEventChannel menuRequested;

        [Header("Data in")]
        [SerializeField] private RunSummaryEventChannel runEnded;

        private Label level, kills, headshots, time, record;
        private VisualElement root;

        public void Configure(VoidEventChannel restart, VoidEventChannel menu, RunSummaryEventChannel summary)
        {
            restartRequested = restart;
            menuRequested = menu;
            runEnded = summary;
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            runEnded?.Subscribe(OnRunEnded);
        }

        protected override void OnDisable()
        {
            runEnded?.Unsubscribe(OnRunEnded);
            base.OnDisable();
        }

        protected override void Bind(VisualElement r)
        {
            root = r;
            level = r.Q<Label>("stat-level");
            kills = r.Q<Label>("stat-kills");
            headshots = r.Q<Label>("stat-headshots");
            time = r.Q<Label>("stat-time");
            record = r.Q<Label>("record");

            Click(r.Q<Button>("restart"), () => restartRequested?.Raise());
            Click(r.Q<Button>("menu"), () => menuRequested?.Raise());
        }

        protected override void OnShown() => FocusFirst(root);

        private void OnRunEnded(RunSummary summary)
        {
            EnsureBound();
            if (level != null) level.text = summary.LevelReached.ToString();
            if (kills != null) kills.text = summary.TotalKills.ToString();
            if (headshots != null) headshots.text = summary.Headshots.ToString();
            if (time != null)
            {
                int total = Mathf.RoundToInt(summary.DurationSeconds);
                time.text = $"{total / 60}:{total % 60:00}";
            }

            SetHidden(record, !summary.NewRecord);
        }
    }
}
