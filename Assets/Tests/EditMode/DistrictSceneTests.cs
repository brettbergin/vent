using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using Vent.Core.Utility;
using Vent.Enemies.Spawning;
using Vent.Gameplay.World;

namespace Vent.Tests.EditMode
{
    /// <summary>
    /// Guards the generated district: its surfaces must be walkable Environment at the right
    /// heights (the NavMesh and the player's containment depend on it), its names must not collide
    /// with the office's, it must expose parking spots for the cars, and nothing may hang over a
    /// road low enough to catch the chase camera.
    /// </summary>
    public sealed class DistrictSceneTests
    {
        private static Scene Open() => EditorSceneManager.OpenScene(Vent.Editor.Paths.BuildingScene, OpenSceneMode.Single);
        private static void Close() => EditorSceneManager.OpenScene($"{Vent.Editor.Paths.Scenes}/{SceneNames.Boot}.unity", OpenSceneMode.Single);

        private static GameObject District(Scene scene)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.name == "District")
                {
                    return root;
                }
            }

            Assert.Fail("Building scene has no District root; run Vent/Rebuild Everything.");
            return null;
        }

        [Test]
        public void DistrictSurfacesAreEnvironmentCollidersAtStreetHeights()
        {
            Scene scene = Open();
            try
            {
                GameObject district = District(scene);
                Transform vents = district.transform.Find("ExteriorVents");
                int environment = LayerMask.NameToLayer(Layers.Environment);
                int roads = 0, sidewalks = 0;
                foreach (Collider c in district.GetComponentsInChildren<Collider>(true))
                {
                    if (vents != null && c.transform.IsChildOf(vents))
                    {
                        continue;
                    }

                    Assert.AreEqual(environment, c.gameObject.layer, $"{c.name} must be Environment so it bakes into the NavMesh and stops bullets");
                }

                foreach (Renderer r in district.GetComponentsInChildren<Renderer>(true))
                {
                    if (r.name.StartsWith("Road_"))
                    {
                        roads++;
                        Assert.AreEqual(-0.15f, r.bounds.max.y, 0.02f, $"{r.name} surface sits one kerb below the sidewalks");
                    }
                    else if (r.name.StartsWith("Sidewalk_"))
                    {
                        sidewalks++;
                        Assert.AreEqual(0f, r.bounds.max.y, 0.02f, $"{r.name} is flush with the lobby floor");
                    }
                }

                Assert.GreaterOrEqual(roads, 20, "avenues, streets and a ring road");
                Assert.GreaterOrEqual(sidewalks, 100, "a sidewalk ring around every block");
            }
            finally
            {
                Close();
            }
        }

        [Test]
        public void DistrictUsesNoReservedNames()
        {
            Scene scene = Open();
            try
            {
                foreach (Transform t in District(scene).GetComponentsInChildren<Transform>(true))
                {
                    string n = t.name;
                    Assert.IsFalse(n == "Floor" || n.StartsWith("Glass") || n.StartsWith("Light_") || n.StartsWith("WindowLight_") || n.StartsWith("Building"),
                        $"{n} collides with a name the office tests look for");
                }
            }
            finally
            {
                Close();
            }
        }

        [Test]
        public void DistrictExposesParkingSpotsAndIsFenced()
        {
            Scene scene = Open();
            try
            {
                GameObject district = District(scene);
                Transform spots = district.transform.Find("ParkingSpots");
                Assert.IsNotNull(spots, "ParkingSpots group");
                Assert.GreaterOrEqual(spots.childCount, 18, "enough bays for a fleet");
                foreach (Transform spot in spots)
                {
                    Assert.LessOrEqual(Mathf.Abs(spot.position.y), 0.2f, $"{spot.name} sits on the ground");
                    // Parked cars carve their bays (a whole row of them, side by side) out of the NavMesh, so
                    // the nearest walkable ground is the aisle in front: a car length away at most.
                    Assert.IsTrue(NavMesh.SamplePosition(spot.position, out _, 4f, NavMesh.AllAreas), $"{spot.name} must have NavMesh beside it (the player walks up to the car)");
                }

                var barrier = new Bounds();
                bool any = false;
                foreach (Collider c in district.GetComponentsInChildren<Collider>(true))
                {
                    if (!c.name.StartsWith("Barrier_"))
                    {
                        continue;
                    }

                    if (!any) { barrier = c.bounds; any = true; } else barrier.Encapsulate(c.bounds);
                }

                Assert.IsTrue(any, "a perimeter barrier exists");
                Assert.GreaterOrEqual(barrier.max.x, 190f);
                Assert.LessOrEqual(barrier.min.x, -190f);
                Assert.GreaterOrEqual(barrier.max.z, 168f);
                Assert.LessOrEqual(barrier.min.z, -168f);
            }
            finally
            {
                Close();
            }
        }

        [Test]
        public void SkylineStaysBeyondTheDistrict()
        {
            Scene scene = Open();
            try
            {
                var districtBounds = new Bounds(Vector3.zero, new Vector3(386f + 20f, 400f, 342f + 20f));
                int blocks = 0;
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
                    {
                        if (r.name.StartsWith("Building") && r.transform.parent != null && r.transform.parent.name == "Exterior")
                        {
                            blocks++;
                            Assert.IsFalse(districtBounds.Intersects(r.bounds), $"{r.name} stands inside the district");
                        }
                    }
                }

                Assert.GreaterOrEqual(blocks, 10, "a skyline beyond the barrier");
            }
            finally
            {
                Close();
            }
        }

        [Test]
        public void NothingOverhangsTheRoadsBelowFourMetres()
        {
            Scene scene = Open();
            try
            {
                GameObject district = District(scene);
                var roads = new List<Bounds>();
                var others = new List<Renderer>();
                foreach (Renderer r in district.GetComponentsInChildren<Renderer>(true))
                {
                    if (r.name.StartsWith("Road_"))
                    {
                        roads.Add(r.bounds);
                    }
                    else if (r.bounds.min.y > 0.2f)
                    {
                        others.Add(r);
                    }
                }

                foreach (Renderer r in others)
                {
                    Bounds b = r.bounds;
                    foreach (Bounds road in roads)
                    {
                        bool overlapsXZ = b.min.x < road.max.x && b.max.x > road.min.x && b.min.z < road.max.z && b.max.z > road.min.z;
                        if (overlapsXZ)
                        {
                            Assert.GreaterOrEqual(b.min.y, 4f, $"{r.name} hangs over a road below the chase camera's headroom");
                        }
                    }
                }
            }
            finally
            {
                Close();
            }
        }

        [Test]
        public void FrontDoorSitsInTheLobbyWallFacingTheStreet()
        {
            Scene scene = Open();
            try
            {
                FrontDoor door = null;
                Transform spawn = null;
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    door ??= root.GetComponentInChildren<FrontDoor>(true);
                    if (root.name == "PlayerSpawn")
                    {
                        spawn = root.transform;
                    }
                }

                Assert.IsNotNull(door, "the building has a FrontDoor");
                Assert.IsNotNull(spawn, "the scene has a PlayerSpawn");
                Assert.AreEqual(30f, door.transform.position.x, 0.1f, "in the +X outer wall");
                Assert.AreEqual(0f, door.transform.position.z, 0.1f, "middle of the lobby wall");
                Assert.Greater(Vector3.Dot(door.transform.forward, Vector3.right), 0.99f, "opens outward onto the street");
                Vector3 toDoor = (door.transform.position - spawn.position).normalized;
                Assert.Greater(Vector3.Dot(spawn.forward, toDoor), 0.95f, "the player spawns facing the door");
                var obstacle = door.GetComponent<NavMeshObstacle>();
                Assert.IsNotNull(obstacle, "a shut door carves the doorway");
                Assert.IsTrue(obstacle.carving);
                int environment = LayerMask.NameToLayer(Layers.Environment);
                var hinges = door.GetComponentsInChildren<BoxCollider>(true);
                Assert.AreEqual(2, hinges.Length, "one collider per leaf");
                foreach (BoxCollider hinge in hinges)
                {
                    Assert.AreEqual(environment, hinge.gameObject.layer, "leaves block bullets and rays");
                }
            }
            finally
            {
                Close();
            }
        }

        [Test]
        public void ExteriorVentsStartInactiveAndSitOnTheGround()
        {
            Scene scene = Open();
            try
            {
                Transform vents = District(scene).transform.Find("ExteriorVents");
                Assert.IsNotNull(vents, "ExteriorVents group");
                Assert.IsFalse(vents.gameObject.activeSelf, "outdoor spawns wait for the front door");
                var all = vents.GetComponentsInChildren<AirVent>(true);
                Assert.GreaterOrEqual(all.Length, 28, "a manhole per block plus the office's");
                foreach (AirVent vent in all)
                {
                    Assert.Less(Mathf.Abs(vent.FloorPosition.y), 0.5f, $"{vent.name} lands at street level");
                    Assert.Less(vent.GratePosition.y, vent.FloorPosition.y - 1f, $"{vent.name} spawns underground");
                }
            }
            finally
            {
                Close();
            }
        }
    }
}
