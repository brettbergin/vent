using UnityEngine;
using UnityEngine.UIElements;
using Vent.Core;
using Vent.Core.Audio;
using Vent.Core.Events;

namespace Vent.UI.Screens
{
    /// <summary>
    /// Base for every UI Toolkit screen. A screen is a <see cref="UIDocument"/> plus the game
    /// states in which it is visible. Visibility is driven purely by
    /// <see cref="GameStateEventChannel"/>; screens never ask the game manager anything.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public abstract class UIScreen : MonoBehaviour
    {
        [SerializeField] private GameStateEventChannel stateChanged;
        [SerializeField] private GameState[] visibleIn = System.Array.Empty<GameState>();

        private UIDocument document;
        private bool bound;

        protected VisualElement Root => document != null ? document.rootVisualElement : null;
        protected GameState CurrentState { get; private set; } = GameState.Boot;
        public bool IsVisible { get; private set; }

        public void ConfigureVisibility(GameStateEventChannel channel, params GameState[] states)
        {
            stateChanged = channel;
            visibleIn = states;
        }

        protected virtual void Awake() => document = GetComponent<UIDocument>();

        protected virtual void OnEnable()
        {
            stateChanged?.Subscribe(OnStateChanged);
            EnsureBound();
            ApplyVisibility(IsVisibleIn(CurrentState));
        }

        protected virtual void OnDisable()
        {
            stateChanged?.Unsubscribe(OnStateChanged);
            if (bound)
            {
                Unbind();
                bound = false;
            }
        }

        /// <summary>Query elements and hook callbacks. Called once the visual tree exists.</summary>
        protected abstract void Bind(VisualElement root);

        /// <summary>Undo <see cref="Bind"/>.</summary>
        protected virtual void Unbind() { }

        /// <summary>Called whenever the screen becomes visible.</summary>
        protected virtual void OnShown() { }

        protected virtual void OnHidden() { }

        protected void EnsureBound()
        {
            if (bound || Root == null)
            {
                return;
            }

            bound = true;
            Bind(Root);
        }

        private bool IsVisibleIn(GameState state)
        {
            foreach (GameState s in visibleIn)
            {
                if (s == state)
                {
                    return true;
                }
            }

            return false;
        }

        private void OnStateChanged(GameState state)
        {
            CurrentState = state;
            EnsureBound();
            ApplyVisibility(IsVisibleIn(state));
        }

        private void ApplyVisibility(bool visible)
        {
            if (Root == null)
            {
                return;
            }

            Root.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            if (visible == IsVisible)
            {
                return;
            }

            IsVisible = visible;
            if (visible)
            {
                OnShown();
            }
            else
            {
                OnHidden();
            }
        }

        // ---------------------------------------------------------------- helpers for subclasses

        protected static void Click(Button button, System.Action action)
        {
            if (button == null)
            {
                return;
            }

            button.clicked += () =>
            {
                SfxPlayer.TryPlay2D(SoundId.UiClick, 0.6f);
                action?.Invoke();
            };
        }

        protected static void SetHidden(VisualElement element, bool hidden)
        {
            element?.EnableInClassList("hidden", hidden);
        }

        protected static void FocusFirst(VisualElement root)
        {
            root?.Q<Button>()?.Focus();
        }
    }
}
