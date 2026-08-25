using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace Vent.Editor
{
    /// <summary>
    /// Renders a prefab to a PNG from a fixed three-quarter view, so a generated model can be
    /// eyeballed without opening the editor. Also runs headless: <c>-executeMethod Vent.Editor.PrefabSnapshot.SnapshotZombie</c>
    /// (needs a GPU, so no <c>-nographics</c>). Output: <c>Logs/snapshot-&lt;name&gt;.png</c>.
    /// </summary>
    public static class PrefabSnapshot
    {
        [MenuItem("Vent/Snapshot Zombie")]
        public static void SnapshotZombie() => Snapshot($"{Paths.Prefabs}/Zombie.prefab", new Vector3(1.9f, 1.3f, 2.6f), new Vector3(0f, 1.0f, 0f));

        [MenuItem("Vent/Snapshot SMG")]
        public static void SnapshotSmg() => Snapshot($"{Paths.Prefabs}/VM_SMG.prefab", new Vector3(0.45f, 0.25f, -0.35f), new Vector3(0f, 0f, 0.05f), 0.9f);

        [MenuItem("Vent/Snapshot Pistol")]
        public static void SnapshotPistol() => Snapshot($"{Paths.Prefabs}/VM_Pistol.prefab", new Vector3(0.3f, 0.15f, -0.25f), new Vector3(0f, 0f, 0.02f), 0.6f);

        /// <summary>Photograph every furnished room of the generated Building scene from a high corner.</summary>
        /// <summary>What the player sees on frame one: from the spawn point, at eye height, facing the spawn yaw, plus a look to each side.</summary>
        [MenuItem("Vent/Snapshot Player View")]
        public static void SnapshotPlayerView()
        {
            Scene scene = EditorSceneManager.OpenScene(Paths.BuildingScene, OpenSceneMode.Single);
            GameObject spawn = GameObject.Find("PlayerSpawn");
            if (spawn == null)
            {
                Debug.LogError("[Vent] Building scene has no PlayerSpawn; regenerate first.");
                return;
            }

            var camGo = new GameObject("SnapshotCamera");
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 70f;
            cam.GetUniversalAdditionalCameraData().renderPostProcessing = true;
            cam.nearClipPlane = 0.1f;
            cam.clearFlags = CameraClearFlags.Skybox;
            Directory.CreateDirectory("Logs");
            const int w = 1200, h = 800;
            var rt = new RenderTexture(w, h, 24);
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            foreach ((string name, float yawOffset) in new[] { ("forward", 0f), ("left", -90f), ("right", 90f), ("back", 180f) })
            {
                cam.transform.position = spawn.transform.position + Vector3.up * 1.6f;
                cam.transform.rotation = spawn.transform.rotation * Quaternion.Euler(0f, yawOffset, 0f);
                cam.targetTexture = rt;
                cam.Render();
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                tex.Apply();
                RenderTexture.active = null;
                cam.targetTexture = null;
                File.WriteAllBytes(Path.Combine("Logs", $"snapshot-Player_{name}.png"), tex.EncodeToPNG());
            }

            Object.DestroyImmediate(tex);
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(camGo);
            Debug.Log("[Vent] Player view snapshots written to Logs/snapshot-Player_*.png");
        }

        [MenuItem("Vent/Snapshot Rooms")]
        public static void SnapshotRooms()
        {
            Scene scene = EditorSceneManager.OpenScene(Paths.BuildingScene, OpenSceneMode.Single);
            Transform props = null;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                Transform found = root.transform.Find("Props");
                if (found != null) props = found;
            }

            if (props == null)
            {
                Debug.LogError("[Vent] Building scene has no Props root; regenerate first.");
                return;
            }

            var camGo = new GameObject("SnapshotCamera");
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 70f;
            // Match the player camera: tonemapping/exposure from the scene's Volume, else rooms look darker than in play.
            var camData = cam.GetUniversalAdditionalCameraData();
            camData.renderPostProcessing = true;
            cam.nearClipPlane = 0.1f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            Directory.CreateDirectory("Logs");
            const int w = 1200, h = 800;
            var rt = new RenderTexture(w, h, 24);
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            foreach (Transform room in props)
            {
                // Room_{c}_{r}_{type}: the grid centre is the reliable middle of the room.
                string[] parts = room.name.Split('_');
                if (parts.Length < 3 || !int.TryParse(parts[1], out int c) || !int.TryParse(parts[2], out int r)) continue;
                var layout = new BuildingLayout();
                Vector3 cellCenter = BuildingGenerator.CellCenter(c, r, layout.Columns, layout.Rows, layout.CellSize);
                Vector3 look = new(cellCenter.x, 0.9f, cellCenter.z);
                cam.transform.position = look + new Vector3(-3.3f, 2.2f, -3.3f); // stays inside a 10 m room
                cam.transform.LookAt(look);
                cam.targetTexture = rt;
                cam.Render();
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                tex.Apply();
                RenderTexture.active = null;
                cam.targetTexture = null;
                File.WriteAllBytes(Path.Combine("Logs", $"snapshot-{room.name}.png"), tex.EncodeToPNG());
            }

            Object.DestroyImmediate(tex);
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(camGo);
            Debug.Log("[Vent] Room snapshots written to Logs/snapshot-Room_*.png");
            EditorSceneManager.OpenScene(Paths.BootScene, OpenSceneMode.Single);
        }

        public static void Snapshot(string prefabPath, Vector3 cameraOffset, Vector3 lookAt, float orthoSize = 1.3f)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null)
            {
                Debug.LogError($"[Vent] No prefab at {prefabPath}");
                return;
            }

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.35f, 0.36f, 0.4f);

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            foreach (Behaviour b in instance.GetComponentsInChildren<Behaviour>(true))
            {
                b.enabled = false; // pose only; no animators or agents
            }

            var key = new GameObject("Key").AddComponent<Light>();
            key.type = LightType.Directional;
            key.intensity = 1.6f;
            key.color = new Color(1f, 0.95f, 0.85f);
            key.transform.rotation = Quaternion.Euler(40f, -35f, 0f);
            var rim = new GameObject("Rim").AddComponent<Light>();
            rim.type = LightType.Directional;
            rim.intensity = 0.7f;
            rim.color = new Color(0.6f, 0.7f, 1f);
            rim.transform.rotation = Quaternion.Euler(20f, 150f, 0f);

            var camGo = new GameObject("SnapshotCamera");
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = orthoSize;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.12f, 0.12f, 0.14f);
            cam.cullingMask = ~0;
            cam.transform.position = lookAt + cameraOffset;
            cam.transform.LookAt(lookAt);

            const int w = 900, h = 1100;
            var rt = new RenderTexture(w, h, 24);
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            cam.targetTexture = null;

            Directory.CreateDirectory("Logs");
            string file = Path.Combine("Logs", $"snapshot-{prefab.name}.png");
            File.WriteAllBytes(file, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            Object.DestroyImmediate(rt);
            Debug.Log($"[Vent] Snapshot written: {file}");

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }
    }
}
