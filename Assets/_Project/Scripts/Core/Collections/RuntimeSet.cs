using System;
using System.Collections.Generic;
using UnityEngine;

namespace Vent.Core.Collections
{
    /// <summary>
    /// A ScriptableObject that holds a live list of scene objects of type <typeparamref name="T"/>.
    ///
    /// Objects add themselves in OnEnable and remove themselves in OnDisable. Anything that
    /// needs "all active zombies" (the spawner, the HUD, the debug overlay) reads the set
    /// instead of calling <c>FindObjectsOfType</c>, which walks the whole scene.
    /// </summary>
    public abstract class RuntimeSet<T> : ScriptableObject where T : Component
    {
        private readonly List<T> items = new();

        /// <summary>Raised after an item is added.</summary>
        public event Action<T> Added;

        /// <summary>Raised after an item is removed.</summary>
        public event Action<T> Removed;

        /// <summary>Read-only view. Do not cache across frames; iterate the live list.</summary>
        public IReadOnlyList<T> Items => items;

        public int Count => items.Count;

        public void Add(T item)
        {
            if (item == null || items.Contains(item))
            {
                return;
            }

            items.Add(item);
            Added?.Invoke(item);
        }

        public void Remove(T item)
        {
            if (items.Remove(item))
            {
                Removed?.Invoke(item);
            }
        }

        private void OnDisable()
        {
            // Clear on domain reload / editor stop so stale references never leak between runs.
            items.Clear();
            Added = null;
            Removed = null;
        }
    }
}
