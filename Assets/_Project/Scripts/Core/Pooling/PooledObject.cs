using System;
using UnityEngine;

namespace Vent.Core.Pooling
{
    /// <summary>
    /// Attached automatically to every instance spawned by a <see cref="PrefabPool"/>.
    /// Holds the back-reference to its pool so callers can release the instance without
    /// knowing which pool it came from (<c>GetComponent&lt;PooledObject&gt;().Release()</c>).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PooledObject : MonoBehaviour
    {
        /// <summary>Raised right before the instance is returned to the pool, so components can reset state.</summary>
        public event Action Released;

        internal PrefabPool Owner { get; set; }

        /// <summary>True while the instance is checked out of the pool.</summary>
        public bool IsActiveInPool { get; internal set; }

        /// <summary>Return this instance to its pool. Safe to call more than once.</summary>
        public void Release()
        {
            if (!IsActiveInPool || Owner == null)
            {
                return;
            }

            Released?.Invoke();
            Owner.Release(this);
        }
    }
}
