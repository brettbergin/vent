using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Vent.Gameplay.World;

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

        /// <summary>Every car prefab from a three-quarter front and a three-quarter rear, on a neutral floor: Logs/snapshot-Car_*.png.</summary>
        [MenuItem("Vent/Snapshot Cars")]
        public static void SnapshotCars()
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.42f, 0.44f, 0.5f);
            var key = new GameObject("Key").AddComponent<Light>();
            key.type = LightType.Directional;
            key.intensity = 1.8f;
            key.color = new Color(1f, 0.95f, 0.85f);
            key.shadows = LightShadows.Soft;
            key.transform.rotation = Quaternion.Euler(45f, -40f, 0f);
            var rim = new GameObject("Rim").AddComponent<Light>();
            rim.type = LightType.Directional;
            rim.intensity = 0.6f;
            rim.color = new Color(0.6f, 0.7f, 1f);
            rim.transform.rotation = Quaternion.Euler(20f, 150f, 0f);
            GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.transform.localScale = new Vector3(4f, 1f, 4f);
            floor.GetComponent<Renderer>().sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>($"{Paths.Materials}/M_Asphalt.mat");

            var camGo = new GameObject("SnapshotCamera");
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 32f;
            cam.GetUniversalAdditionalCameraData().renderPostProcessing = false;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.55f, 0.6f, 0.68f);
            Directory.CreateDirectory("Logs");
            const int w = 1100, h = 700;
            var rt = new RenderTexture(w, h, 24);
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            foreach (Vent.Vehicles.Data.VehicleShape shape in System.Enum.GetValues(typeof(Vent.Vehicles.Data.VehicleShape)))
            {
                string name = CarBodyLibrary.For(shape).Name;
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{Paths.Prefabs}/Vehicle_{name}.prefab");
                if (prefab == null)
                {
                    Debug.LogError($"[Vent] No prefab for {name}; run Vent/Rebuild Assets and Prefabs.");
                    continue;
                }

                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                foreach (Behaviour behaviour in instance.GetComponentsInChildren<Behaviour>(true))
                {
                    behaviour.enabled = false;
                }

                foreach ((string view, Vector3 from) in new[] { ("front", new Vector3(5.5f, 2.4f, 7.0f)), ("rear", new Vector3(-6.0f, 2.2f, -6.5f)) })
                {
                    cam.transform.position = from;
                    cam.transform.LookAt(new Vector3(0f, 0.7f, 0f));
                    cam.targetTexture = rt;
                    cam.Render();
                    RenderTexture.active = rt;
                    tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                    tex.Apply();
                    RenderTexture.active = null;
                    cam.targetTexture = null;
                    File.WriteAllBytes(Path.Combine("Logs", $"snapshot-Car_{name}_{view}.png"), tex.EncodeToPNG());
                }

                Object.DestroyImmediate(instance);
            }

            Object.DestroyImmediate(tex);
            Object.DestroyImmediate(rt);
            Debug.Log("[Vent] Car snapshots written to Logs/snapshot-Car_*.png");
        }

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

        /// <summary>The street outside the front door, the hero car, an avenue, and the whole district from the air.</summary>
        [MenuItem("Vent/Snapshot District")]
        public static void SnapshotDistrict()
        {
            EditorSceneManager.OpenScene(Paths.BuildingScene, OpenSceneMode.Single);
            // The scene is saved with the indoor haze; outdoors the Atmosphere thins it at runtime. Do the same here (not saved).
            RenderSettings.fogDensity = 0.0032f;
            RenderSettings.fogColor = new Color(0.26f, 0.16f, 0.18f);
            RenderSettings.ambientSkyColor = new Color(0.50f, 0.40f, 0.46f);
            RenderSettings.ambientEquatorColor = new Color(0.34f, 0.24f, 0.26f);
            var camGo = new GameObject("SnapshotCamera");
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 70f;
            cam.GetUniversalAdditionalCameraData().renderPostProcessing = true;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 700f;
            cam.clearFlags = CameraClearFlags.Skybox;
            Directory.CreateDirectory("Logs");
            const int w = 1200, h = 800;
            var rt = new RenderTexture(w, h, 24);
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            (string name, Vector3 from, Vector3 at)[] shots =
            {
                ("door", new Vector3(31.5f, 1.6f, 0f), new Vector3(60f, 1.2f, 0f)),
                ("herocar", new Vector3(44f, 1.7f, 9f), new Vector3(36.5f, 0.6f, 0f)),
                ("avenue", new Vector3(62f, 1.6f, -80f), new Vector3(62f, 1.4f, 60f)),
                ("plaza", new Vector3(-20f, 2f, 40f), new Vector3(20f, 1f, 80f)),
                ("aerial", new Vector3(60f, 110f, -170f), new Vector3(0f, 0f, 0f)),
            };
            foreach ((string name, Vector3 from, Vector3 at) in shots)
            {
                cam.transform.position = from;
                cam.transform.LookAt(at);
                cam.targetTexture = rt;
                cam.Render();
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                tex.Apply();
                RenderTexture.active = null;
                cam.targetTexture = null;
                File.WriteAllBytes(Path.Combine("Logs", $"snapshot-District_{name}.png"), tex.EncodeToPNG());
            }

            Object.DestroyImmediate(tex);
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(camGo);
            Debug.Log("[Vent] District snapshots written to Logs/snapshot-District_*.png");
        }

        /// <summary>
        /// The plants: a big lobby plant up close, a desk with its plant, the park from its path, a
        /// street tree on the avenue, and the ivy on the office wall.
        /// </summary>
        [MenuItem("Vent/Snapshot Nature")]
        public static void SnapshotNature()
        {
            Scene scene = EditorSceneManager.OpenScene(Paths.BuildingScene, OpenSceneMode.Single);
            RenderSettings.fogDensity = 0.0032f;
            RenderSettings.fogColor = new Color(0.26f, 0.16f, 0.18f);
            RenderSettings.ambientSkyColor = new Color(0.50f, 0.40f, 0.46f);
            RenderSettings.ambientEquatorColor = new Color(0.34f, 0.24f, 0.26f);
            var camGo = new GameObject("SnapshotCamera");
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 60f;
            cam.GetUniversalAdditionalCameraData().renderPostProcessing = true;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 700f;
            cam.clearFlags = CameraClearFlags.Skybox;
            Directory.CreateDirectory("Logs");
            const int w = 1200, h = 800;
            var rt = new RenderTexture(w, h, 24);
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);

            var shots = new List<(string name, Vector3 from, Vector3 at)>();
            Transform park = null, plantLarge = null, potted = null, desk = null, ivy = null, streetTree = null, pothos = null;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                {
                    if (park == null && t.name.StartsWith("Park_")) park = t;
                    if (plantLarge == null && t.name == "PlantLarge") plantLarge = t;
                    if (potted == null && t.name == "PottedPlant") potted = t;
                    if (desk == null && t.name == "Desk" && t.Find("DeskPlant") != null) desk = t;
                    if (pothos == null && t.name == "Pothos") pothos = t;
                    if (ivy == null && t.name == "Ivy" && t.parent != null && t.parent.name == "Foliage" && t.parent.parent != null && t.parent.parent.name == "Building") ivy = t;
                    if (streetTree == null && t.name == "StreetTree") streetTree = t;
                }
            }

            void Frame(string name, Transform target, Vector3 offset, float lookHeight)
            {
                if (target == null)
                {
                    Debug.LogWarning($"[Vent] Snapshot Nature: no {name} in the scene.");
                    return;
                }

                Vector3 at = target.position + Vector3.up * lookHeight;
                shots.Add((name, at + target.TransformDirection(offset), at));
            }

            Frame("plant_large", plantLarge, new Vector3(0.9f, 0.9f, 2.2f), 0.9f);
            Frame("plant_potted", potted, new Vector3(-0.8f, 0.6f, 1.6f), 0.5f);
            Frame("desk", desk, new Vector3(-0.9f, 0.9f, 1.6f), 0.75f);
            Frame("pothos", pothos, new Vector3(0.3f, 0.2f, 1.4f), -0.1f);
            Frame("ivy", ivy, new Vector3(2.5f, 1.3f, 5f), 0.9f);
            Frame("street_tree", streetTree, new Vector3(6f, 1.4f, 9f), 3.5f);
            if (park != null)
            {
                Vector3 c = park.position;
                shots.Add(("park", c + park.TransformDirection(new Vector3(0f, 1.6f, -16f)), c + Vector3.up * 1.2f));
                shots.Add(("park_lawn", c + park.TransformDirection(new Vector3(7f, 1.4f, 7f)), c + park.TransformDirection(new Vector3(-6f, 0.6f, -6f))));
            }

            foreach ((string name, Vector3 from, Vector3 at) in shots)
            {
                cam.transform.position = from;
                cam.transform.LookAt(at);
                cam.targetTexture = rt;
                cam.Render();
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                tex.Apply();
                RenderTexture.active = null;
                cam.targetTexture = null;
                File.WriteAllBytes(Path.Combine("Logs", $"snapshot-Nature_{name}.png"), tex.EncodeToPNG());
            }

            Object.DestroyImmediate(tex);
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(camGo);
            Debug.Log($"[Vent] Nature snapshots written: {shots.Count} to Logs/snapshot-Nature_*.png");
        }

        /// <summary>
        /// Photographs the key hunt: the hint on the lobby whiteboard (the one thing no headless
        /// test can check — generated letters have to be legible and the right way round), a cable
        /// coil, a rack's patch panel, and a desk with its monitor lit and its drawer pulled out.
        /// </summary>
        [MenuItem("Vent/Snapshot Key Hunt")]
        public static void SnapshotKeyHunt()
        {
            EditorSceneManager.OpenScene(Paths.BuildingScene, OpenSceneMode.Single);
            var hunt = Object.FindFirstObjectByType<KeyHuntDirector>();
            if (hunt == null)
            {
                Debug.LogError("[Vent] Building scene has no KeyHuntDirector; regenerate first.");
                return;
            }

            var camGo = new GameObject("SnapshotCamera");
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 55f;
            cam.GetUniversalAdditionalCameraData().renderPostProcessing = true;
            cam.nearClipPlane = 0.03f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            Directory.CreateDirectory("Logs");
            const int w = 1200, h = 800;
            var rt = new RenderTexture(w, h, 24);
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);

            void Shoot(string name, Transform target, Vector3 lookAt, float back, float up)
            {
                if (target == null)
                {
                    Debug.LogWarning($"[Vent] Nothing to photograph for {name}");
                    return;
                }

                cam.transform.position = lookAt + target.forward * back + Vector3.up * up;
                cam.transform.LookAt(lookAt);
                cam.targetTexture = rt;
                cam.Render();
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                tex.Apply();
                RenderTexture.active = null;
                cam.targetTexture = null;
                File.WriteAllBytes(Path.Combine("Logs", $"snapshot-keyhunt-{name}.png"), tex.EncodeToPNG());
            }

            // Straight on and close, so the hint is read exactly as a player standing at it sees it.
            Transform note = hunt.Note != null ? hunt.Note.transform : null;
            if (note != null)
            {
                Shoot("hint", note, note.position + note.up * 1.5f, 1.9f, 0f);
            }

            // Show the roll: the panel this run chose, the first coil, and the key desk lit and open.
            hunt.SeedOverride = 4242;
            hunt.BeginRun();
            if (hunt.ActivePanel != null)
            {
                Transform panel = hunt.ActivePanel.transform;
                Shoot("panel", panel, panel.position, 1.0f, 0.15f);
            }

            if (hunt.ActiveCables.Count > 0)
            {
                Transform coil = hunt.ActiveCables[0].transform;
                Shoot("cable", coil, coil.position, 0.9f, 0.5f);
            }

            if (hunt.KeyDrawer != null)
            {
                // Light the screen and pull the drawer out by hand: BeginRun leaves both off, and
                // nothing is running an Update loop in the editor to animate them.
                hunt.KeyDrawer.SetScreenLit(true);
                hunt.KeyDrawer.transform.localPosition += Vector3.forward * 0.3f;
                // A desk's monitor faces its chair, which sits on the desk's -Z side, so the
                // camera has to stand behind the chair; from +Z you photograph the monitor's back
                // and the screen reads black whether it is lit or not.
                Transform desk = hunt.KeyDrawer.transform.parent.parent;
                Shoot("keydesk", desk, desk.position + Vector3.up * 0.9f, -2.4f, 0.9f);

                // And the drawer itself, close, with the key shown as the player sees it the moment it opens.
                Transform leaf = hunt.KeyDrawer.transform;
                Transform key = leaf.Find("Key");
                if (key != null)
                {
                    key.gameObject.SetActive(true);
                }

                // From a standing player's eye: steep enough to see over the front into the drawer.
                Vector3 inside = leaf.position + leaf.forward * -0.15f;
                Shoot("drawer", leaf, inside, 0.55f, 1.25f);
            }

            Object.DestroyImmediate(tex);
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(camGo);
            Debug.Log("[Vent] Key hunt snapshots written to Logs/snapshot-keyhunt-*.png");
            EditorSceneManager.OpenScene(Paths.BootScene, OpenSceneMode.Single);
        }

        [MenuItem("Vent/Snapshot Rooms")]
        public static void SnapshotRooms()
        {
            Scene scene = EditorSceneManager.OpenScene(Paths.BuildingScene, OpenSceneMode.Single);
            Transform props = null;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                // The district has a Props root too; the rooms are the building's.
                Transform found = root.transform.Find("Props");
                if (found != null && (props == null || root.name == "Building")) props = found;
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
