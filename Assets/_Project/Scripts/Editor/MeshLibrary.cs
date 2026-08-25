using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Vent.Editor
{
    /// <summary>
    /// Cube meshes whose UVs are in metres, so a texture authored at "one repeat = N metres" tiles at
    /// the same density on a 15 m floor slab and a 0.3 m wall pier. Unity's primitive cube maps 0..1
    /// across every face, which stretches one repeat over a whole wall. One mesh asset per distinct
    /// size, cached, with lightmap UVs generated so the blocks can be baked.
    /// </summary>
    public static class MeshLibrary
    {
        private static readonly Dictionary<string, Mesh> Cache = new();

        public static Mesh WorldCube(Vector3 size)
        {
            string key = $"Cube_{size.x:0.###}x{size.y:0.###}x{size.z:0.###}";
            if (Cache.TryGetValue(key, out Mesh cached) && cached != null)
            {
                return cached;
            }

            string path = $"{Paths.Meshes}/{key}.asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (mesh == null)
            {
                mesh = Build(size);
                mesh.name = key;
                ProjectBootstrap.EnsureFolder(Paths.Meshes);
                AssetDatabase.CreateAsset(mesh, path);
            }

            Cache[key] = mesh;
            return mesh;
        }

        /// <summary>Unit cube (scaled by the transform to <paramref name="size"/>) with per-face UVs in metres.</summary>
        private static Mesh Build(Vector3 size)
        {
            var verts = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();

            void Face(Vector3 normal, Vector3 right, Vector3 up, float width, float height)
            {
                int i = verts.Count;
                Vector3 c = normal * 0.5f;
                verts.Add(c - right * 0.5f - up * 0.5f);
                verts.Add(c + right * 0.5f - up * 0.5f);
                verts.Add(c + right * 0.5f + up * 0.5f);
                verts.Add(c - right * 0.5f + up * 0.5f);
                for (int k = 0; k < 4; k++) normals.Add(normal);
                uvs.Add(new Vector2(0f, 0f));
                uvs.Add(new Vector2(width, 0f));
                uvs.Add(new Vector2(width, height));
                uvs.Add(new Vector2(0f, height));
                tris.AddRange(new[] { i, i + 2, i + 1, i, i + 3, i + 2 });
            }

            Face(Vector3.up, Vector3.right, Vector3.forward, size.x, size.z);
            Face(Vector3.down, Vector3.right, Vector3.back, size.x, size.z);
            Face(Vector3.forward, Vector3.left, Vector3.up, size.x, size.y);
            Face(Vector3.back, Vector3.right, Vector3.up, size.x, size.y);
            Face(Vector3.right, Vector3.forward, Vector3.up, size.z, size.y);
            Face(Vector3.left, Vector3.back, Vector3.up, size.z, size.y);

            var mesh = new Mesh();
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            Unwrapping.GenerateSecondaryUVSet(mesh);
            return mesh;
        }
    }
}
