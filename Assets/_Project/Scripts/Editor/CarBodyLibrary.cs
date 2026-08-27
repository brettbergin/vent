using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Vent.Vehicles.Data;

namespace Vent.Editor
{
    /// <summary>
    /// Car bodies grown from profiles, the way the plants are grown from cards. A body is a loft:
    /// a list of stations along the length, each giving the half-width, the belt line (top of the
    /// doors) and the roof height there, swept into a closed hull whose cross-section carries a
    /// rocker, a side bulge, a window sill, tumblehome and a crowned roof. Wheel wells are cut into
    /// the sills by raising the skin's lower edge over each wheel, so the tyre sits in an arch.
    /// Where the roof drops to the belt the roof surface becomes a windscreen or rear window; the
    /// segments between the pillars are flagged as glass. Three submeshes come out: paint, glass and
    /// the dark underbody. Wheels are revolved profiles: a tyre with shoulders and a dished rim.
    /// Five body styles are five station lists; the numbers are here so a silhouette is reviewable.
    /// </summary>
    public static class CarBodyLibrary
    {
        public const int SubmeshPaint = 0, SubmeshGlass = 1, SubmeshUnderbody = 2;

        /// <summary>One slice of the hull. The flags describe the segment from this station toward the nose.</summary>
        public readonly struct Station
        {
            public readonly float Z;
            public readonly float HalfWidth;
            /// <summary>Top of the lower body (the door tops / the deck) here.</summary>
            public readonly float BeltY;
            /// <summary>Top of the greenhouse here; equal to the belt where there is no cabin (bonnet, boot, bed).</summary>
            public readonly float RoofY;
            /// <summary>The side of the greenhouse in the next segment is a window (else a pillar).</summary>
            public readonly bool SideGlass;
            /// <summary>The top surface in the next segment is glass (a windscreen or rear window).</summary>
            public readonly bool TopGlass;

            public Station(float z, float halfWidth, float beltY, float roofY, bool sideGlass = false, bool topGlass = false)
            {
                Z = z;
                HalfWidth = halfWidth;
                BeltY = beltY;
                RoofY = Mathf.Max(roofY, beltY);
                SideGlass = sideGlass;
                TopGlass = topGlass;
            }
        }

        /// <summary>Everything the prefab factory needs to dress one body style.</summary>
        public sealed class Spec
        {
            public string Name;
            public float Length, Width, Height;
            public float FloorY;
            public float Wheelbase, Track, WheelRadius, TyreWidth;
            public float ArchClearance = 0.07f;
            public Station[] Stations;
            /// <summary>Where the driver sits (x is negative: left-hand drive) and where the cabin collider spans.</summary>
            public Vector3 DriverSeat;
            public float CabinZ0, CabinZ1, CabinRoofY, BeltAtDriver;
            public float NoseY, TailY;
            public bool Antenna, RoofRails, BedRails;
            public float BedZ0, BedZ1;
            public int Spokes = 5;

            public float HalfLength => Length / 2f;
        }

        public static Spec For(VehicleShape shape)
        {
            switch (shape)
            {
                case VehicleShape.Hatchback:
                    return new Spec
                    {
                        Name = "Hatchback", Length = 4.0f, Width = 1.76f, Height = 1.48f, FloorY = 0.30f,
                        Wheelbase = 2.55f, Track = 1.55f, WheelRadius = 0.32f, TyreWidth = 0.20f,
                        Stations = new[]
                        {
                            new Station(-2.00f, 0.78f, 0.86f, 0.86f),
                            new Station(-1.88f, 0.84f, 0.92f, 0.92f, topGlass: true),   // tailgate glass, steep
                            new Station(-1.62f, 0.87f, 0.92f, 1.40f),
                            new Station(-1.45f, 0.88f, 0.92f, 1.46f, sideGlass: true),  // rear quarter glass
                            new Station(-0.75f, 0.88f, 0.92f, 1.48f),                   // B pillar
                            new Station(-0.62f, 0.88f, 0.92f, 1.48f, sideGlass: true),  // front door glass
                            new Station(0.30f, 0.88f, 0.92f, 1.46f, topGlass: true),    // windscreen
                            new Station(0.95f, 0.87f, 0.94f, 0.94f),
                            new Station(1.65f, 0.84f, 0.86f, 0.86f),
                            new Station(2.00f, 0.78f, 0.74f, 0.74f),
                        },
                        DriverSeat = new Vector3(-0.38f, 0.44f, -0.15f), CabinZ0 = -1.7f, CabinZ1 = 0.4f, CabinRoofY = 1.48f, BeltAtDriver = 0.92f,
                        NoseY = 0.74f, TailY = 0.86f, Antenna = true,
                    };
                case VehicleShape.Suv:
                    return new Spec
                    {
                        Name = "SUV", Length = 4.7f, Width = 1.92f, Height = 1.76f, FloorY = 0.42f,
                        Wheelbase = 2.9f, Track = 1.68f, WheelRadius = 0.38f, TyreWidth = 0.24f,
                        Stations = new[]
                        {
                            new Station(-2.35f, 0.86f, 1.02f, 1.02f),
                            new Station(-2.22f, 0.92f, 1.08f, 1.08f, topGlass: true),   // tailgate
                            new Station(-1.95f, 0.95f, 1.08f, 1.66f),
                            new Station(-1.80f, 0.96f, 1.08f, 1.72f, sideGlass: true),  // rear quarter
                            new Station(-0.90f, 0.96f, 1.08f, 1.75f),                   // C pillar
                            new Station(-0.78f, 0.96f, 1.08f, 1.76f, sideGlass: true),  // rear door
                            new Station(-0.02f, 0.96f, 1.08f, 1.76f),                   // B pillar
                            new Station(0.10f, 0.96f, 1.08f, 1.76f, sideGlass: true),   // front door
                            new Station(0.80f, 0.95f, 1.08f, 1.72f, topGlass: true),    // windscreen
                            new Station(1.40f, 0.94f, 1.10f, 1.10f),
                            new Station(2.10f, 0.90f, 1.02f, 1.02f),
                            new Station(2.35f, 0.84f, 0.88f, 0.88f),
                        },
                        DriverSeat = new Vector3(-0.40f, 0.56f, -0.30f), CabinZ0 = -2.1f, CabinZ1 = 0.9f, CabinRoofY = 1.76f, BeltAtDriver = 1.08f,
                        NoseY = 0.88f, TailY = 1.02f, RoofRails = true, Spokes = 6,
                    };
                case VehicleShape.Pickup:
                    return new Spec
                    {
                        Name = "Pickup", Length = 5.3f, Width = 1.92f, Height = 1.80f, FloorY = 0.44f,
                        Wheelbase = 3.3f, Track = 1.70f, WheelRadius = 0.38f, TyreWidth = 0.25f,
                        Stations = new[]
                        {
                            new Station(-2.65f, 0.86f, 1.02f, 1.02f),
                            new Station(-2.50f, 0.94f, 1.06f, 1.06f),                   // bed (covered)
                            new Station(-0.55f, 0.96f, 1.06f, 1.06f),
                            new Station(-0.47f, 0.96f, 1.06f, 1.78f),                   // cab back wall
                            new Station(-0.35f, 0.96f, 1.08f, 1.80f, sideGlass: true),  // cab window
                            new Station(0.45f, 0.96f, 1.08f, 1.80f),                    // A pillar base
                            new Station(0.55f, 0.96f, 1.08f, 1.78f, topGlass: true),    // windscreen
                            new Station(1.20f, 0.95f, 1.12f, 1.12f),
                            new Station(2.30f, 0.92f, 1.06f, 1.06f),
                            new Station(2.65f, 0.86f, 0.92f, 0.92f),
                        },
                        DriverSeat = new Vector3(-0.42f, 0.58f, 0.05f), CabinZ0 = -0.5f, CabinZ1 = 0.7f, CabinRoofY = 1.80f, BeltAtDriver = 1.08f,
                        NoseY = 0.92f, TailY = 1.02f, BedRails = true, BedZ0 = -2.5f, BedZ1 = -0.55f, Spokes = 6,
                    };
                case VehicleShape.Van:
                    return new Spec
                    {
                        Name = "Van", Length = 5.0f, Width = 1.95f, Height = 2.0f, FloorY = 0.36f,
                        Wheelbase = 3.4f, Track = 1.70f, WheelRadius = 0.36f, TyreWidth = 0.22f,
                        Stations = new[]
                        {
                            new Station(-2.50f, 0.90f, 1.05f, 1.92f),                   // rear doors (flat)
                            new Station(-2.40f, 0.96f, 1.05f, 1.98f),                   // cargo panel
                            new Station(0.05f, 0.97f, 1.05f, 2.00f),                    // B pillar
                            new Station(0.15f, 0.97f, 1.05f, 2.00f, sideGlass: true),   // front door glass
                            new Station(1.10f, 0.96f, 1.05f, 1.98f, topGlass: true),    // windscreen, steep
                            new Station(1.75f, 0.95f, 1.10f, 1.10f),
                            new Station(2.30f, 0.92f, 1.02f, 1.02f),
                            new Station(2.50f, 0.86f, 0.88f, 0.88f),
                        },
                        DriverSeat = new Vector3(-0.42f, 0.55f, 0.55f), CabinZ0 = -2.4f, CabinZ1 = 1.2f, CabinRoofY = 2.0f, BeltAtDriver = 1.05f,
                        NoseY = 0.88f, TailY = 1.05f, Spokes = 6,
                    };
                default:
                    return new Spec
                    {
                        Name = "Sedan", Length = 4.5f, Width = 1.8f, Height = 1.42f, FloorY = 0.30f,
                        Wheelbase = 2.8f, Track = 1.6f, WheelRadius = 0.34f, TyreWidth = 0.21f,
                        Stations = new[]
                        {
                            new Station(-2.25f, 0.80f, 0.84f, 0.84f),
                            new Station(-2.05f, 0.86f, 0.88f, 0.88f),
                            new Station(-1.55f, 0.89f, 0.90f, 0.90f, topGlass: true),   // rear window
                            new Station(-1.00f, 0.90f, 0.90f, 1.36f),                   // C pillar
                            new Station(-0.85f, 0.90f, 0.90f, 1.40f, sideGlass: true),  // rear door glass
                            new Station(-0.05f, 0.905f, 0.905f, 1.42f),                 // B pillar
                            new Station(0.08f, 0.905f, 0.905f, 1.42f, sideGlass: true), // front door glass
                            new Station(0.75f, 0.90f, 0.90f, 1.40f, topGlass: true),    // windscreen
                            new Station(1.35f, 0.89f, 0.92f, 0.92f),
                            new Station(1.90f, 0.87f, 0.86f, 0.86f),
                            new Station(2.15f, 0.83f, 0.80f, 0.80f),
                            new Station(2.25f, 0.78f, 0.72f, 0.72f),
                        },
                        DriverSeat = new Vector3(-0.40f, 0.44f, -0.20f), CabinZ0 = -1.55f, CabinZ1 = 0.75f, CabinRoofY = 1.42f, BeltAtDriver = 0.905f,
                        NoseY = 0.72f, TailY = 0.84f, Antenna = true,
                    };
            }
        }

        // ------------------------------------------------------------------ body

        private const int RingPoints = 12;
        private static readonly (int start, int end, int submesh)[] Bands =
        {
            (0, 3, SubmeshUnderbody),   // floor, well wall, well ceiling
            (3, 6, SubmeshPaint),       // rocker, side bulge, belt
            (6, 8, -1),                 // greenhouse side: window or pillar (per segment)
            (8, 11, -2),                // roof or deck: paint, or glass where the roof slopes to the belt
        };

        /// <summary>The hull, as a mesh asset with three submeshes (paint, glass, underbody).</summary>
        public static Mesh Body(Spec spec)
        {
            List<Station> stations = Resolve(spec);
            var vertices = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new[] { new List<int>(), new List<int>(), new List<int>() };
            int n = stations.Count;
            float innerX = spec.Track / 2f - spec.TyreWidth / 2f - 0.05f;

            // One ring of points per station per band; the left side is the mirror with reversed winding.
            Vector2[][] rings = new Vector2[n][];
            for (int i = 0; i < n; i++)
            {
                rings[i] = Ring(spec, stations[i], ArchY(spec, stations[i].Z), innerX);
            }

            foreach ((int start, int end, int submesh) in Bands)
            {
                int count = end - start + 1;
                int baseRight = vertices.Count;
                for (int i = 0; i < n; i++)
                {
                    for (int j = start; j <= end; j++)
                    {
                        vertices.Add(new Vector3(rings[i][j].x, rings[i][j].y, stations[i].Z));
                        uvs.Add(new Vector2(stations[i].Z / spec.Length + 0.5f, rings[i][j].y / spec.Height));
                    }
                }

                int baseLeft = vertices.Count;
                for (int i = 0; i < n; i++)
                {
                    for (int j = start; j <= end; j++)
                    {
                        vertices.Add(new Vector3(-rings[i][j].x, rings[i][j].y, stations[i].Z));
                        uvs.Add(new Vector2(stations[i].Z / spec.Length + 0.5f, rings[i][j].y / spec.Height));
                    }
                }

                for (int i = 0; i < n - 1; i++)
                {
                    int target = submesh >= 0 ? submesh : submesh == -1 ? (stations[i].SideGlass ? SubmeshGlass : SubmeshPaint) : (stations[i].TopGlass ? SubmeshGlass : SubmeshPaint);
                    List<int> list = tris[target];
                    for (int j = 0; j < count - 1; j++)
                    {
                        int a = baseRight + i * count + j, b = a + 1, c = baseRight + (i + 1) * count + j + 1, d = c - 1;
                        list.Add(a); list.Add(b); list.Add(c);
                        list.Add(a); list.Add(c); list.Add(d);
                        int la = baseLeft + i * count + j, lb = la + 1, lc = baseLeft + (i + 1) * count + j + 1, ld = lc - 1;
                        list.Add(la); list.Add(lc); list.Add(lb);
                        list.Add(la); list.Add(ld); list.Add(lc);
                    }
                }
            }

            // End caps: the tail and the nose, flat, fanned from their centroid.
            Cap(vertices, uvs, tris[SubmeshPaint], rings[0], stations[0].Z, spec, Vector3.back);
            Cap(vertices, uvs, tris[SubmeshPaint], rings[n - 1], stations[n - 1].Z, spec, Vector3.forward);

            var mesh = new Mesh { name = $"Car_{spec.Name}_Body", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.subMeshCount = 3;
            for (int s = 0; s < 3; s++)
            {
                mesh.SetTriangles(tris[s], s);
            }

            mesh.RecalculateNormals();
            SealCentreSeam(mesh);
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            return Save(mesh);
        }

        /// <summary>Merge the wheel-arch stations into the spec's, interpolating the hull numbers and inheriting the flags.</summary>
        private static List<Station> Resolve(Spec spec)
        {
            var zs = new SortedSet<float>();
            foreach (Station s in spec.Stations)
            {
                zs.Add(s.Z);
            }

            float archR = spec.WheelRadius + spec.ArchClearance;
            foreach (float wheelZ in new[] { -spec.Wheelbase / 2f, spec.Wheelbase / 2f })
            {
                foreach (float t in new[] { -1f, -0.8f, -0.55f, -0.28f, 0f, 0.28f, 0.55f, 0.8f, 1f })
                {
                    float z = wheelZ + t * archR;
                    if (z > spec.Stations[0].Z + 0.02f && z < spec.Stations[^1].Z - 0.02f)
                    {
                        zs.Add(z);
                    }
                }
            }

            var result = new List<Station>();
            foreach (float z in zs)
            {
                int k = 0;
                while (k < spec.Stations.Length - 2 && spec.Stations[k + 1].Z <= z)
                {
                    k++;
                }

                Station a = spec.Stations[k], b = spec.Stations[k + 1];
                float t = Mathf.InverseLerp(a.Z, b.Z, z);
                result.Add(new Station(z, Mathf.Lerp(a.HalfWidth, b.HalfWidth, t), Mathf.Lerp(a.BeltY, b.BeltY, t), Mathf.Lerp(a.RoofY, b.RoofY, t), a.SideGlass, a.TopGlass));
            }

            return result;
        }

        /// <summary>Height of the skin's lower edge at z: the floor, lifted into an arch over each wheel.</summary>
        private static float ArchY(Spec spec, float z)
        {
            float archR = spec.WheelRadius + spec.ArchClearance;
            float best = spec.FloorY;
            foreach (float wheelZ in new[] { -spec.Wheelbase / 2f, spec.Wheelbase / 2f })
            {
                float dz = z - wheelZ;
                if (Mathf.Abs(dz) < archR)
                {
                    best = Mathf.Max(best, spec.WheelRadius + Mathf.Sqrt(archR * archR - dz * dz));
                }
            }

            return best;
        }

        /// <summary>The right half of the cross-section at one station, floor centre to roof centre: twelve points, always the same count so stations loft.</summary>
        private static Vector2[] Ring(Spec spec, Station s, float archY, float innerX)
        {
            float hw = s.HalfWidth;
            bool cabin = s.RoofY > s.BeltY + 0.02f;
            float cabinHw = hw - 0.05f;
            float tumble = cabin ? 0.16f : 0.08f;
            float roofHw = cabinHw - tumble;
            float roofY = s.RoofY;
            float crown = cabin ? 0.04f : 0.03f;
            float glassTop = cabin ? roofY - 0.06f : s.BeltY + 0.02f;
            return new[]
            {
                new Vector2(0f, spec.FloorY),                                   // 0 floor centre
                new Vector2(innerX, spec.FloorY),                               // 1 floor edge
                new Vector2(innerX, archY),                                     // 2 well wall top
                new Vector2(hw - 0.05f, archY),                                 // 3 well ceiling to the rocker
                new Vector2(hw - 0.015f, archY + 0.05f),                        // 4 rocker chamfer
                new Vector2(hw, Mathf.Lerp(archY, s.BeltY, 0.5f)),              // 5 side bulge
                new Vector2(hw - 0.02f, s.BeltY),                               // 6 belt line
                new Vector2(cabinHw, s.BeltY + 0.02f),                          // 7 window sill
                new Vector2(roofHw + 0.02f, glassTop),                          // 8 top of the glass
                new Vector2(roofHw - 0.03f, cabin ? roofY - 0.01f : glassTop + 0.01f), // 9 roof edge
                new Vector2(roofHw * 0.5f, roofY + crown * 0.7f),               // 10 roof shoulder
                new Vector2(0f, roofY + crown),                                 // 11 roof centre
            };
        }

        private static void Cap(List<Vector3> vertices, List<Vector2> uvs, List<int> tris, Vector2[] ring, float z, Spec spec, Vector3 outward)
        {
            // The full outline: right side floor→roof, then the left side back down. Skip the well points (the caps sit where there is no arch).
            var outline = new List<Vector2>();
            for (int j = 1; j < RingPoints; j++)
            {
                outline.Add(ring[j]);
            }

            for (int j = RingPoints - 2; j >= 1; j--)
            {
                outline.Add(new Vector2(-ring[j].x, ring[j].y));
            }

            Vector2 centroid = Vector2.zero;
            foreach (Vector2 p in outline)
            {
                centroid += p;
            }

            centroid /= outline.Count;
            int centre = vertices.Count;
            vertices.Add(new Vector3(centroid.x, centroid.y, z));
            uvs.Add(new Vector2(centroid.x / spec.Width + 0.5f, centroid.y / spec.Height));
            int first = vertices.Count;
            foreach (Vector2 p in outline)
            {
                vertices.Add(new Vector3(p.x, p.y, z));
                uvs.Add(new Vector2(p.x / spec.Width + 0.5f, p.y / spec.Height));
            }

            for (int k = 0; k < outline.Count; k++)
            {
                int a = first + k, b = first + (k + 1) % outline.Count;
                Vector3 normal = Vector3.Cross(vertices[a] - vertices[centre], vertices[b] - vertices[centre]);
                if (Vector3.Dot(normal, outward) >= 0f)
                {
                    tris.Add(centre); tris.Add(a); tris.Add(b);
                }
                else
                {
                    tris.Add(centre); tris.Add(b); tris.Add(a);
                }
            }
        }

        /// <summary>The two halves meet at x = 0 with mirrored normals; flatten them so the roof and floor show no seam.</summary>
        private static void SealCentreSeam(Mesh mesh)
        {
            Vector3[] v = mesh.vertices;
            Vector3[] nrm = mesh.normals;
            for (int i = 0; i < v.Length; i++)
            {
                if (Mathf.Abs(v[i].x) < 1e-4f)
                {
                    nrm[i].x = 0f;
                    nrm[i] = nrm[i].sqrMagnitude > 1e-6f ? nrm[i].normalized : Vector3.up;
                }
            }

            mesh.normals = nrm;
        }

        // ------------------------------------------------------------------ wheels

        /// <summary>A tyre: a profile with a flat tread and rounded shoulders revolved about the axle (local X).</summary>
        public static Mesh Tyre(float radius, float width)
        {
            float hw = width / 2f, r = radius;
            (float r, float x)[] profile =
            {
                (0.60f * r, -hw * 0.92f), (0.82f * r, -hw), (0.94f * r, -hw * 0.92f), (r, -hw * 0.62f),
                (r, hw * 0.62f), (0.94f * r, hw * 0.92f), (0.82f * r, hw), (0.60f * r, hw * 0.92f),
            };
            return Save(Revolve($"Car_Tyre_{radius:0.00}x{width:0.00}", profile, 28));
        }

        /// <summary>A rim: a dish from the hub out to the tyre bead, facing +X (the outside of the wheel on the right; the factory mirrors it on the left).</summary>
        public static Mesh Rim(float radius, float width)
        {
            float hw = width / 2f, r = radius;
            (float r, float x)[] profile =
            {
                (0f, hw * 0.55f), (0.16f * r, hw * 0.58f), (0.30f * r, hw * 0.40f), (0.52f * r, hw * 0.46f), (0.62f * r, hw * 0.62f), (0.62f * r, -hw * 0.6f), (0.40f * r, -hw * 0.6f),
            };
            return Save(Revolve($"Car_Rim_{radius:0.00}x{width:0.00}", profile, 28));
        }

        private static Mesh Revolve(string name, (float r, float x)[] profile, int segments)
        {
            var vertices = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            for (int p = 0; p < profile.Length; p++)
            {
                for (int s = 0; s <= segments; s++)
                {
                    float a = s / (float)segments * Mathf.PI * 2f;
                    vertices.Add(new Vector3(profile[p].x, profile[p].r * Mathf.Cos(a), profile[p].r * Mathf.Sin(a)));
                    uvs.Add(new Vector2(s / (float)segments, p / (float)(profile.Length - 1)));
                }
            }

            int ring = segments + 1;
            for (int p = 0; p < profile.Length - 1; p++)
            {
                for (int s = 0; s < segments; s++)
                {
                    int a = p * ring + s, b = a + 1, c = (p + 1) * ring + s + 1, d = c - 1;
                    AddFacingOut(tris, vertices, a, b, c);
                    AddFacingOut(tris, vertices, a, c, d);
                }
            }

            var mesh = new Mesh { name = name };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>Wind a triangle so it faces away from the origin; the revolved shapes are star-shaped about it.</summary>
        private static void AddFacingOut(List<int> tris, List<Vector3> v, int a, int b, int c)
        {
            Vector3 centre = (v[a] + v[b] + v[c]) / 3f;
            Vector3 normal = Vector3.Cross(v[b] - v[a], v[c] - v[a]);
            if (normal.sqrMagnitude < 1e-12f)
            {
                return;
            }

            if (Vector3.Dot(normal, centre) >= 0f)
            {
                tris.Add(a); tris.Add(b); tris.Add(c);
            }
            else
            {
                tris.Add(a); tris.Add(c); tris.Add(b);
            }
        }

        // ------------------------------------------------------------------ assets

        /// <summary>Write the mesh under Meshes/, reusing an existing asset so prefabs keep their reference across regens.</summary>
        private static Mesh Save(Mesh built)
        {
            string path = $"{Paths.Meshes}/{built.name}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing == null)
            {
                ProjectBootstrap.EnsureFolder(Paths.Meshes);
                AssetDatabase.CreateAsset(built, path);
                return built;
            }

            existing.Clear();
            existing.indexFormat = built.indexFormat;
            existing.vertices = built.vertices;
            existing.uv = built.uv;
            existing.subMeshCount = built.subMeshCount;
            for (int s = 0; s < built.subMeshCount; s++)
            {
                existing.SetTriangles(built.GetTriangles(s), s);
            }

            existing.normals = built.normals;
            existing.tangents = built.tangents;
            existing.RecalculateBounds();
            EditorUtility.SetDirty(existing);
            UnityEngine.Object.DestroyImmediate(built);
            return existing;
        }
    }
}
