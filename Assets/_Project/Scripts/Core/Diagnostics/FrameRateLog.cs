using UnityEngine;
using UnityEngine.SceneManagement;

namespace Vent.Core.Diagnostics
{
    /// <summary>
    /// Writes the frame time to the player log every few seconds — mean and the worst frame in the
    /// window, with the scene and the resolution — so a "it runs badly" report can be read as
    /// numbers from <c>~/Library/Logs/Vent Studio/Vent/Player.log</c> without a profiler attached.
    /// Player builds only; the editor has its own tools.
    /// </summary>
    public sealed class FrameRateLog : MonoBehaviour
    {
        private const float WindowSeconds = 5f;

        private float windowStart;
        private int frames;
        private float worst;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (Application.isEditor)
            {
                return;
            }

            var go = new GameObject("FrameRateLog") { hideFlags = HideFlags.HideAndDontSave };
            DontDestroyOnLoad(go);
            go.AddComponent<FrameRateLog>();
        }

        private void OnEnable()
        {
            windowStart = Time.realtimeSinceStartup;
            frames = 0;
            worst = 0f;
        }

        private void Update()
        {
            frames++;
            worst = Mathf.Max(worst, Time.unscaledDeltaTime);
            float elapsed = Time.realtimeSinceStartup - windowStart;
            if (elapsed < WindowSeconds)
            {
                return;
            }

            float mean = elapsed / frames * 1000f;
            Debug.Log($"[FrameRateLog] {SceneManager.GetActiveScene().name} {Screen.width}x{Screen.height} {(Screen.fullScreen ? "fullscreen" : "windowed")}: {frames / elapsed:0.0} fps, mean {mean:0.0} ms, worst {worst * 1000f:0.0} ms, vsync={QualitySettings.vSyncCount}");
            windowStart = Time.realtimeSinceStartup;
            frames = 0;
            worst = 0f;
        }
    }
}
