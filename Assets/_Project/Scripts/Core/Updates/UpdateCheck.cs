using System;
using System.Threading;
using UnityEngine;
using UnityEngine.Networking;

namespace Vent.Core.Updates
{
    /// <summary>Fetches latest.json. Never throws: a failed check just means "no update".</summary>
    public static class UpdateCheck
    {
        private const int TimeoutSeconds = 15;

        public static async Awaitable<UpdateManifest> FetchAsync(string url, CancellationToken ct)
        {
            using UnityWebRequest request = UnityWebRequest.Get(url);
            request.timeout = TimeoutSeconds;

            try
            {
                UnityWebRequestAsyncOperation op = request.SendWebRequest();
                while (!op.isDone)
                {
                    await Awaitable.NextFrameAsync(ct);
                }

                if (request.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogWarning($"[Updater] check failed: {request.error}");
                    return null;
                }

                UpdateManifest manifest = UpdateManifest.Parse(request.downloadHandler.text);
                if (manifest == null)
                {
                    Debug.LogWarning("[Updater] manifest could not be parsed");
                }

                return manifest;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Updater] check failed: {e.Message}");
                return null;
            }
        }
    }
}
