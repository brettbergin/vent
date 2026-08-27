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

        /// <summary>
        /// A flat rectangle facing +Z with the UVs written out explicitly: (0,0) at the bottom-left
        /// as seen from the front, U to the right, V up.
        ///
        /// For anything that has to display a picture the right way round — the whiteboard's hint.
        /// A thin cube would do the job geometrically, but Unity's primitive cube does not give its
        /// six faces a consistent UV handedness, so a texture with words on it comes out mirrored or
        /// upside down depending on which face you happen to be looking at, and the fix degenerates
        /// into guessing at rotations. Owning the four vertices removes the question.
        /// </summary>
        public static Mesh Card(float width, float height)
        {
            string key = $"Card_{width:0.###}x{height:0.###}";
            if (Cache.TryGetValue(key, out Mesh cached) && cached != null)
            {
                return cached;
            }

            string path = $"{Paths.Meshes}/{key}.asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (mesh == null)
            {
                // Seen from +Z looking back along -Z, Unity's left-handed axes put +X on the
                // viewer's LEFT, and a front face is the one whose vertices run clockwise from
                // there. Both facts are easy to get backwards, so the corners are named for where
                // they land on the viewer's screen rather than for their sign in X.
                float w = width / 2f, h = height / 2f;
                Vector3 bottomLeft = new(w, -h, 0f), topLeft = new(w, h, 0f);
                Vector3 topRight = new(-w, h, 0f), bottomRight = new(-w, -h, 0f);
                mesh = new Mesh
                {
                    name = key,
                    vertices = new[] { bottomLeft, topLeft, topRight, bottomRight },
                    uv = new[] { new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0f) },
                    normals = new[] { Vector3.forward, Vector3.forward, Vector3.forward, Vector3.forward },
                    triangles = new[] { 0, 1, 2, 0, 2, 3 },
                };
                mesh.RecalculateTangents();
                mesh.RecalculateBounds();
                ProjectBootstrap.EnsureFolder(Paths.Meshes);
                AssetDatabase.CreateAsset(mesh, path);
            }

            Cache[key] = mesh;
            return mesh;
        }

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
