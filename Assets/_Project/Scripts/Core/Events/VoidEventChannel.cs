using System;
using UnityEngine;

namespace Vent.Core.Events
{
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
}
