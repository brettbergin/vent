using UnityEngine;
using Vent.Core.Events;
using Vent.Core.Items;
using Vent.Core.Services;

namespace Vent.Player.Camera
{
    /// <summary>
    /// The rear-view mirror: a second, cheap camera on the back of the player's head that renders
    /// into a small texture the HUD shows at the top of the screen. It only exists once the player
    /// has found the mirror in the office, and a new run takes it away again. Rides the main camera,
    /// so in a car it looks back over the chase rig, which is what a driver wants from it.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RearViewMirror : MonoBehaviour, IRearViewSource
    {
        [SerializeField] private OfficeItemEventChannel itemCollected;
        [SerializeField] private LevelEventChannel levelChanged;
        [SerializeField] private UnityEngine.Camera rearCamera;
        [SerializeField, Min(64)] private int width = 320;
        [SerializeField, Min(32)] private int height = 120;

        private RenderTexture view;
        private bool active;

        public RenderTexture View => view;
        public bool IsActive => active;

        /// <summary>Editor-time wiring used by the prefab factory.</summary>
        public void Configure(OfficeItemEventChannel items, LevelEventChannel level, UnityEngine.Camera camera)
        {
            itemCollected = items;
            levelChanged = level;
            rearCamera = camera;
        }

        private void Awake()
        {
            if (rearCamera == null)
            {
                rearCamera = GetComponent<UnityEngine.Camera>();
            }

            if (rearCamera != null)
            {
                rearCamera.enabled = false;
            }
        }

        /// <summary>
        /// The texture is made the first time the mirror is switched on, not at spawn: a headless
        /// editor (the test runs) has no graphics device, and RenderTexture.Create logs an error
        /// there. Without a device there is no view; the HUD simply keeps the frame hidden.
        /// </summary>
        private void EnsureView()
        {
            if (view != null || SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
            {
                return;
            }

            view = new RenderTexture(width, height, 16) { name = "RT_RearView" };
            if (rearCamera != null)
            {
                rearCamera.targetTexture = view;
            }
        }

        private void OnEnable()
        {
            GameServices.Register<IRearViewSource>(this);
            itemCollected?.Subscribe(OnItem);
            levelChanged?.Subscribe(OnLevel);
        }

        private void OnDisable()
        {
            itemCollected?.Unsubscribe(OnItem);
            levelChanged?.Unsubscribe(OnLevel);
            GameServices.Unregister<IRearViewSource>(this);
        }

        private void OnDestroy()
        {
            if (view != null)
            {
                view.Release();
                Destroy(view);
            }
        }

        private void OnItem(OfficeItemInfo info)
        {
            if (info.Kind == OfficeItem.RearViewMirror)
            {
                SetActive(true);
            }
        }

        /// <summary>A new run starts at level 1, and nobody starts a run holding a mirror.</summary>
        private void OnLevel(LevelInfo info)
        {
            if (info.Level <= 1)
            {
                SetActive(false);
            }
        }

        private void SetActive(bool value)
        {
            active = value;
            if (value)
            {
                EnsureView();
            }

            if (rearCamera != null)
            {
                rearCamera.enabled = value && view != null;
            }
        }
    }
}
