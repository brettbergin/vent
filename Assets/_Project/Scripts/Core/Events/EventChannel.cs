using System;
using UnityEngine;

namespace Vent.Core.Events
{
    /// <summary>
    /// Base class for ScriptableObject-backed event channels.
    ///
    /// Why ScriptableObjects? An asset can be referenced by any prefab or scene object
    /// without either side knowing about the other. The HUD can listen for "zombie killed"
    /// without a reference to the spawner, and a unit test can raise the same event without
    /// a scene at all. This is the "event channel" pattern popularised by Unity's
    /// Open Projects (Chop Chop) and used widely in production.
    ///
    /// Listeners are plain C# delegates (not UnityEvents) so subscriptions are cheap,
    /// allocation-free to invoke, and visible to the compiler.
    /// </summary>
    public abstract class EventChannelBase : ScriptableObject
    {
        [SerializeField, TextArea]
        [Tooltip("Editor-only documentation: what this event means and who raises it.")]
        private string description;

        /// <summary>Human-readable description shown in the inspector; never read at runtime.</summary>
        public string Description => description;
    }

    /// <summary>
    /// Generic typed event channel. Concrete non-generic subclasses exist only because
    /// Unity cannot serialise (or create assets from) open generic types.
    /// </summary>
    /// <typeparam name="T">Payload type carried by the event.</typeparam>
    public abstract class EventChannel<T> : EventChannelBase
    {
        private Action<T> listeners;

        /// <summary>Subscribe. Always pair with <see cref="Unsubscribe"/> in OnDisable.</summary>
        public void Subscribe(Action<T> listener) => listeners += listener;

        /// <summary>Unsubscribe. Safe to call for a listener that was never added.</summary>
        public void Unsubscribe(Action<T> listener) => listeners -= listener;

        /// <summary>
        /// Raise the event synchronously on the calling thread. Exceptions in a listener are
        /// logged and swallowed so that one broken listener cannot starve the others.
        /// </summary>
        public void Raise(T payload)
        {
            Action<T> snapshot = listeners;
            if (snapshot == null)
            {
                return;
            }

            foreach (Delegate d in snapshot.GetInvocationList())
            {
                try
                {
                    ((Action<T>)d).Invoke(payload);
                }
                catch (Exception e)
                {
                    Debug.LogException(e, this);
                }
            }
        }

        /// <summary>Number of live subscribers. Exposed for tests and the debug overlay.</summary>
        public int ListenerCount => listeners?.GetInvocationList().Length ?? 0;

        private void OnDisable()
        {
            // ScriptableObjects survive scene loads; listeners generally do not.
            // Domain reload also lands here, which conveniently clears stale delegates.
            listeners = null;
        }
    }

    /// <summary>Event with no payload.</summary>
    [CreateAssetMenu(menuName = "Vent/Events/Void Event", fileName = "Evt_Void")]
    public sealed class VoidEventChannel : EventChannelBase
    {
        private Action listeners;

        public void Subscribe(Action listener) => listeners += listener;
        public void Unsubscribe(Action listener) => listeners -= listener;

        public void Raise()
        {
            Action snapshot = listeners;
            if (snapshot == null)
            {
                return;
            }

            foreach (Delegate d in snapshot.GetInvocationList())
            {
                try
                {
                    ((Action)d).Invoke();
                }
                catch (Exception e)
                {
                    Debug.LogException(e, this);
                }
            }
        }

        public int ListenerCount => listeners?.GetInvocationList().Length ?? 0;

        private void OnDisable() => listeners = null;
    }

    [CreateAssetMenu(menuName = "Vent/Events/Int Event", fileName = "Evt_Int")]
    public sealed class IntEventChannel : EventChannel<int> { }

    [CreateAssetMenu(menuName = "Vent/Events/Float Event", fileName = "Evt_Float")]
    public sealed class FloatEventChannel : EventChannel<float> { }

    [CreateAssetMenu(menuName = "Vent/Events/Bool Event", fileName = "Evt_Bool")]
    public sealed class BoolEventChannel : EventChannel<bool> { }

    [CreateAssetMenu(menuName = "Vent/Events/String Event", fileName = "Evt_String")]
    public sealed class StringEventChannel : EventChannel<string> { }
}
