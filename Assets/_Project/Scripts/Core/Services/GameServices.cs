using System;
using System.Collections.Generic;
using UnityEngine;

namespace Vent.Core.Services
{
    /// <summary>
    /// A deliberately tiny service locator for scene-scoped singletons (the game manager, the
    /// level director, the player). It replaces static <c>Instance</c> properties and
    /// <c>FindObjectOfType</c> calls with one explicit registration point.
    ///
    /// Rules of use, in order of preference:
    ///   1. Serialized references (drag in the inspector / wire in the prefab factory).
    ///   2. ScriptableObject channels / runtime sets (see <c>Vent.Core.Events</c>).
    ///   3. <see cref="GameServices"/> — only for true singletons that many systems need.
    ///
    /// The registry is cleared on domain reload and can be reset by tests.
    /// </summary>
    public static class GameServices
    {
        private static readonly Dictionary<Type, object> Services = new();

        /// <summary>Register a service. Replaces any existing registration of the same type.</summary>
        public static void Register<T>(T service) where T : class
        {
            if (service == null)
            {
                throw new ArgumentNullException(nameof(service));
            }

            Services[typeof(T)] = service;
        }

        /// <summary>Remove a service, but only if the given instance is the one registered (guards against stale unregisters).</summary>
        public static void Unregister<T>(T service) where T : class
        {
            if (Services.TryGetValue(typeof(T), out object existing) && ReferenceEquals(existing, service))
            {
                Services.Remove(typeof(T));
            }
        }

        /// <summary>Resolve a service; throws if missing so misconfiguration fails loudly.</summary>
        public static T Get<T>() where T : class
        {
            if (Services.TryGetValue(typeof(T), out object service))
            {
                return (T)service;
            }

            throw new InvalidOperationException($"Service {typeof(T).Name} is not registered.");
        }

        /// <summary>Resolve a service if present.</summary>
        public static bool TryGet<T>(out T service) where T : class
        {
            if (Services.TryGetValue(typeof(T), out object found))
            {
                service = (T)found;
                return true;
            }

            service = null;
            return false;
        }

        public static bool Has<T>() where T : class => Services.ContainsKey(typeof(T));

        /// <summary>Drop everything. Called on subsystem registration so "Enter Play Mode without domain reload" stays safe.</summary>
        public static void Clear() => Services.Clear();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnPlay() => Clear();
    }
}
