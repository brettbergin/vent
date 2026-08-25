using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Vent.Tests.EditMode
{
    /// <summary>
    /// Unity can only deserialise a MonoBehaviour or ScriptableObject whose class name matches its
    /// file name. Violations only show up at load time as "referenced script is missing", so this
    /// test catches them at edit time for every concrete type in the project's assemblies.
    /// </summary>
    public sealed class ScriptFileNamingTests
    {
        [Test]
        public void EveryUnityObjectSubclassLivesInAFileNamedAfterIt()
        {
            HashSet<Type> backedByScript = AssetDatabase.FindAssets("t:MonoScript", new[] { "Assets" })
                .Select(guid => AssetDatabase.LoadAssetAtPath<MonoScript>(AssetDatabase.GUIDToAssetPath(guid)))
                .Where(script => script != null)
                .Select(script => script.GetClass())
                .Where(type => type != null)
                .ToHashSet();

            var offenders = new List<string>();
            foreach (System.Reflection.Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (!assembly.GetName().Name.StartsWith("Vent.", StringComparison.Ordinal))
                {
                    continue;
                }

                foreach (Type type in assembly.GetTypes())
                {
                    bool serialisable = !type.IsAbstract && !type.IsGenericTypeDefinition
                                        && (typeof(MonoBehaviour).IsAssignableFrom(type) || typeof(ScriptableObject).IsAssignableFrom(type));
                    if (serialisable && !backedByScript.Contains(type))
                    {
                        offenders.Add(type.FullName);
                    }
                }
            }

            Assert.IsEmpty(offenders, "These types must each live in a .cs file named after the class:\n" + string.Join("\n", offenders));
        }
    }
}
