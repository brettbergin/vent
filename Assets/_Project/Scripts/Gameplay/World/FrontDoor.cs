using System;
using UnityEngine;
using UnityEngine.AI;
using Vent.Core.Audio;
using Vent.Core.Events;
using Vent.Core.Interaction;
using Vent.Core.Services;
using Vent.Core.Utility;
using Vent.Gameplay.Levels;

namespace Vent.Gameplay.World
{
    /// <summary>
    /// The lobby's double glass doors. Locked for the first three levels — the building is the
    /// tutorial — and unlocked by the level-4 announcement; the player then pushes them open and
    /// they stay open for the rest of the run. While closed the leaves are Environment colliders
    /// (bullets stop, the sealed-lobby test still holds) and a carving NavMesh obstacle, so zombies
    /// on either side cannot path through until the player lets them.
    ///
    /// Also the switch that turns the outdoors on: opening the door activates the exterior vents,
    /// so the spawner never spends a slot on a manhole the player cannot reach yet.
    /// </summary>
    public sealed class FrontDoor : MonoBehaviour, IInteractable
    {
        [Header("Events")]
        [SerializeField] private LevelEventChannel levelChanged;
        [SerializeField, Tooltip("Centre-screen banner when the door unlocks.")]
        private StringEventChannel announcement;

        [Header("Rule")]
        [SerializeField, Min(1), Tooltip("The level at which the door unlocks. Levels 1-3 are the warm-up inside the building.")]
        private int unlockLevel = 4;

        [Header("Leaves")]
        [SerializeField] private Transform hingeLeft;
        [SerializeField] private Transform hingeRight;
        [SerializeField, Range(30f, 120f), Tooltip("Degrees each leaf swings outward.")]
        private float openAngle = 100f;
        [SerializeField, Min(0.1f)] private float openSharpness = 6f;

        [Header("Blocking")]
        [SerializeField, Tooltip("Carves the doorway out of the NavMesh while the door is shut.")]
        private NavMeshObstacle obstacle;

        [Header("Feedback")]
        [SerializeField] private Renderer lockLamp;
        [SerializeField] private Color lockedColor = new(1f, 0.15f, 0.1f);
        [SerializeField] private Color unlockedColor = new(0.2f, 1f, 0.3f);
        [SerializeField, Min(0f), Tooltip("Seconds after unlocking before the banner, so it follows the LEVEL banner instead of fighting it.")]
        private float announceDelay = 2.6f;
        [SerializeField] private string lockedPrompt = "LOCKED  -  OPENS AT LEVEL {0}";
        [SerializeField] private string openPrompt = "OPEN DOOR";
        [SerializeField] private string announcementText = "FRONT DOOR UNLOCKED\nTHE STREET IS OPEN";

        [Header("Outside")]
        [SerializeField, Tooltip("Activated the first time the door opens: the outdoor spawn points.")]
        private GameObject exteriorVents;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        private FrontDoorState state;
        private float openT;
        private float announceAt = -1f;
        private Cooldown rattle;
        private MaterialPropertyBlock lampBlock;

        public bool IsUnlocked => state.IsUnlocked;
        public bool IsOpen => state.IsOpen;
        public int UnlockLevel => unlockLevel;

        /// <summary>0 closed .. 1 fully open, as animated.</summary>
        public float OpenAmount => openT;

        /// <summary>Raised once when the player opens the door.</summary>
        public event Action Opened;

        /// <summary>Editor-time wiring used by the building generator.</summary>
        public void Configure(LevelEventChannel level, StringEventChannel banner, Transform leftHinge, Transform rightHinge,
            NavMeshObstacle carve, Renderer lamp, GameObject outdoorVents)
        {
            levelChanged = level;
            announcement = banner;
            hingeLeft = leftHinge;
            hingeRight = rightHinge;
            obstacle = carve;
            lockLamp = lamp;
            exteriorVents = outdoorVents;
        }

        // ----- IInteractable -----
        public string Prompt => IsOpen ? string.Empty : IsUnlocked ? openPrompt : string.Format(lockedPrompt, unlockLevel);
        public bool IsAvailable => IsUnlocked && !IsOpen;

        public void Interact()
        {
            switch (state.TryOpen())
            {
                case DoorAction.Locked:
                    if (rattle.TryConsume(Time.time, 0.6f))
                    {
                        SfxPlayer.TryPlayAt(SoundId.DoorLocked, transform.position + Vector3.up, 0.8f);
                    }

                    break;
                case DoorAction.Opened:
                    Open();
                    break;
            }
        }

        private void Awake()
        {
            state = new FrontDoorState(unlockLevel);
            lampBlock = new MaterialPropertyBlock();
            ApplyClosed();
        }

        private void OnEnable()
        {
            levelChanged?.Subscribe(OnLevelChanged);
            // The channel only fires on change: a door enabled mid-run must ask where the run is.
            if (GameServices.TryGet(out LevelDirector director) && director.IsRunning && state.OnLevel(director.Level))
            {
                Unlock(silent: true);
            }
        }

        private void OnDisable() => levelChanged?.Unsubscribe(OnLevelChanged);

        private void Update()
        {
            float dt = Time.deltaTime;
            openT = MathUtil.Damp(openT, IsOpen ? 1f : 0f, openSharpness, dt);
            if (hingeLeft != null)
            {
                hingeLeft.localRotation = Quaternion.Euler(0f, -openAngle * openT, 0f);
            }

            if (hingeRight != null)
            {
                hingeRight.localRotation = Quaternion.Euler(0f, openAngle * openT, 0f);
            }

            if (announceAt >= 0f && Time.time >= announceAt)
            {
                announceAt = -1f;
                announcement?.Raise(announcementText);
            }
        }

        private void OnLevelChanged(LevelInfo info)
        {
            if (state.OnLevel(info.Level))
            {
                Unlock(silent: false);
            }
            else if (info.Level <= 1)
            {
                ApplyClosed();
            }
        }

        private void Unlock(bool silent)
        {
            SetLamp(unlockedColor);
            if (silent)
            {
                return;
            }

            SfxPlayer.TryPlayAt(SoundId.DoorUnlock, transform.position + Vector3.up, 1f);
            announceAt = Time.time + announceDelay;
        }

        private void Open()
        {
            if (obstacle != null)
            {
                obstacle.enabled = false;
            }

            SfxPlayer.TryPlayAt(SoundId.DoorOpen, transform.position + Vector3.up, 1f);
            if (exteriorVents != null && !exteriorVents.activeSelf)
            {
                exteriorVents.SetActive(true);
            }

            Opened?.Invoke();
        }

        /// <summary>Shut, locked, red lamp: the state at the start of a run.</summary>
        private void ApplyClosed()
        {
            openT = 0f;
            announceAt = -1f;
            if (obstacle != null)
            {
                obstacle.enabled = true;
            }

            SetLamp(lockedColor);
        }

        private void SetLamp(Color color)
        {
            if (lockLamp == null)
            {
                return;
            }

            lockLamp.GetPropertyBlock(lampBlock);
            lampBlock.SetColor(BaseColorId, color);
            lampBlock.SetColor(EmissionColorId, color * 2.5f);
            lockLamp.SetPropertyBlock(lampBlock);
        }
    }
}
