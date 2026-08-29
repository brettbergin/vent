using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using UnityEngine;
using UnityEngine.Networking;

namespace Vent.Core.Updates
{
    /// <summary>
    /// Streams a release zip to disk and checks it against the hash in the manifest.
    ///
    /// The archive is 40–80 MB, so it goes to a file rather than through a memory buffer, and the
    /// SHA-256 is verified before anything is allowed to act on it — that check, not the URL rules,
    /// is what makes installing a downloaded file safe.
    /// </summary>
    public static class UpdateDownloader
    {
        public static async Awaitable<bool> DownloadAsync(
            string url, string destination, string expectedSha256, Action<float> onProgress, CancellationToken ct)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(destination));
                if (File.Exists(destination))
                {
                    File.Delete(destination);
                }

                using (UnityWebRequest request = UnityWebRequest.Get(url))
                {
                    var handler = new DownloadHandlerFile(destination) { removeFileOnAbort = true };
                    request.downloadHandler = handler;

                    UnityWebRequestAsyncOperation op = request.SendWebRequest();
                    while (!op.isDone)
                    {
                        onProgress?.Invoke(request.downloadProgress);
                        await Awaitable.NextFrameAsync(ct);
                    }

                    if (request.result != UnityWebRequest.Result.Success)
                    {
                        Debug.LogWarning($"[Updater] download failed: {request.error}");
                        return false;
                    }
                }

                onProgress?.Invoke(1f);

                string actual = Sha256(destination);
                if (!string.Equals(actual, expectedSha256, StringComparison.OrdinalIgnoreCase))
                {
                    Debug.LogWarning($"[Updater] checksum mismatch: expected {expectedSha256}, got {actual}");
                    TryDelete(destination);
                    return false;
                }

                Debug.Log($"[Updater] downloaded and verified {destination}");
                return true;
            }
            catch (OperationCanceledException)
            {
                TryDelete(destination);
                throw;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Updater] download failed: {e.Message}");
                TryDelete(destination);
                return false;
            }
        }

        public static string Sha256(string path)
        {
            using var sha = SHA256.Create();
            using FileStream stream = File.OpenRead(path);
            byte[] hash = sha.ComputeHash(stream);

            var sb = new System.Text.StringBuilder(hash.Length * 2);
            foreach (byte b in hash)
            {
                sb.Append(b.ToString("x2"));
            }

            return sb.ToString();
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception)
            {
                // Nothing useful to do; the next attempt overwrites it.
            }
        }
    }
}
