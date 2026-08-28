using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Vent.Editor;

namespace Vent.Tests.EditMode
{
    /// <summary>The floor plan drawn for the map: rooms are painted, the outside is clear, a door is a gap in a wall, and the world extent maps room centres to the right texels.</summary>
    public sealed class BuildingMapTests
    {
        private const string Name = "TestBuildingMap";

        [TearDown]
        public void RemoveTheTestImage() => AssetDatabase.DeleteAsset($"{Paths.Textures}/T_{Name}.png");

        [Test]
        public void RoomsArePaintedTheOutsideIsClearAndDoorsAreGaps()
        {
            const int cols = 2, rows = 1;
            const float cell = 10f;
            var plan = new[] { BuildingGenerator.RoomType.Office, BuildingGenerator.RoomType.Lobby };
            var doors = new HashSet<(int, int)> { (0, 1) };
            Vector3 frontDoor = new(cell, 0f, 0f);
            Texture2D imported = TextureFactory.BuildingMap(cols, rows, cell, plan, doors, (1, 0), new List<Vector3>(), frontDoor, 2f, out Rect world, Name);
            Assert.IsNotNull(imported);
            // The imported asset is not CPU-readable; decode the PNG it was written from.
            var map = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            Assert.IsTrue(map.LoadImage(System.IO.File.ReadAllBytes($"{Paths.Textures}/T_{Name}.png")));

            Color At(Vector3 p)
            {
                float u = (p.x - world.xMin) / world.width, v = (p.z - world.yMin) / world.height;
                return map.GetPixel(Mathf.RoundToInt(u * map.width), Mathf.RoundToInt(v * map.height));
            }

            Vector3 office = BuildingGenerator.CellCenter(0, 0, cols, rows, cell), lobby = BuildingGenerator.CellCenter(1, 0, cols, rows, cell);
            Assert.Greater(At(office + new Vector3(2f, 0f, 2f)).a, 0.3f, "a room is painted");
            Assert.Greater(At(lobby + new Vector3(-2f, 0f, -2f)).a, 0.3f);
            Assert.AreEqual(0f, At(new Vector3(world.xMin + 0.5f, 0f, world.yMin + 0.5f)).a, 1e-3f, "outside the walls is clear");

            Vector3 sharedWall = (office + lobby) / 2f;
            Color doorTexel = At(sharedWall), wallTexel = At(sharedWall + new Vector3(0f, 0f, 4f));
            Assert.Greater(doorTexel.r, 0.8f, "the door is a light gap");
            Assert.Less(wallTexel.r, 0.2f, "the wall beside it is dark");
            Assert.Greater(At(frontDoor).r, 0.8f, "the front door is marked");
            Assert.Less(At(frontDoor).b, 0.5f, "in the accent colour");
        }
    }
}
