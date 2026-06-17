using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace MorulabTools.Core
{
    /// <summary>
    /// VPMリポジトリの index.json から最新バージョンを取得し、
    /// アップデートの有無を判定する汎用チェッカー。
    /// </summary>
    public static class PackageUpdateChecker
    {
        /// <summary>
        /// VPMリポジトリのindex.jsonURLをパッケージ名で管理。
        /// 新しいパッケージを追加する場合はここに追記。
        /// </summary>
        private static readonly Dictionary<string, string> RepoUrls = new Dictionary<string, string>
        {
            { "com.morulab.unity-tools", "https://moruton1119.github.io/com.morulab.unity-tools/index.json" },
            { "com.moruton.gimmicks", "https://moruton1119.github.io/com.moruton.gimmicks/index.json" },
        };

        /// <summary>
        /// GitHub ReleaseのベースURL（自己更新用）
        /// </summary>
        private static readonly Dictionary<string, string> GitHubReleaseUrls = new Dictionary<string, string>
        {
            { "com.morulab.unity-tools", "https://github.com/moruton1119/com.morulab.unity-tools/releases/download" },
            { "com.moruton.gimmicks", "https://github.com/moruton1119/com.moruton.gimmicks/releases/download" },
        };

        /// <summary>
        /// パッケージごとのキャッシュ: (packageName -> (latestVersion, fetchTime))
        /// </summary>
        private static readonly Dictionary<string, (string version, DateTime fetchedAt)> _cache
            = new Dictionary<string, (string, DateTime)>();

        private const double CacheDurationMinutes = 30;

        /// <summary>
        /// 指定パッケージの最新バージョンを取得する（非同期）。
        /// index.json の versions から全バージョンを列挙し、SemVer で最大のものを返す。
        /// </summary>
        /// <param name="packageName">パッケージ名（例: com.morulab.unity-tools）</param>
        /// <param name="includePrerelease">ベータ版等のプレリリースを含めるか</param>
        public static async Task<string> GetLatestVersionAsync(string packageName, bool includePrerelease = false)
        {
            // キャッシュチェック
            if (_cache.TryGetValue(packageName, out var cached))
            {
                if ((DateTime.Now - cached.fetchedAt).TotalMinutes < CacheDurationMinutes)
                {
                    return cached.version;
                }
            }

            if (!RepoUrls.TryGetValue(packageName, out string url))
            {
                Debug.LogWarning($"[UpdateChecker] Unknown package: {packageName}");
                return null;
            }

            string json = await FetchJsonAsync(url);
            if (json == null) return null;

            // versions オブジェクト内の全バージョンキーを抽出
            var versions = ExtractAllVersions(json, packageName);
            if (versions.Count == 0)
            {
                Debug.LogWarning($"[UpdateChecker] No versions found for {packageName} in {url}");
                return null;
            }

            // SemVer で最大のものを選択
            string latest = SelectLatestVersion(versions, includePrerelease);

            _cache[packageName] = (latest, DateTime.Now);
            return latest;
        }

        /// <summary>
        /// 同期版（Editorメインスレッド用）。UnityWebRequestの完了を待つため async の結果を同期取得する。
        /// 初回呼び出し時はキャッシュがないため null を返す可能性がある（バックグラウンドで取得開始し、
        /// 次回呼び出し時にキャッシュから返す設計）。
        /// </summary>
        public static string GetLatestVersionCached(string packageName)
        {
            if (_cache.TryGetValue(packageName, out var cached))
            {
                if ((DateTime.Now - cached.fetchedAt).TotalMinutes < CacheDurationMinutes)
                {
                    return cached.version;
                }
            }
            return null;
        }

        /// <summary>
        /// バックグラウンドでフェッチを開始する（結果はキャッシュに格納される）。
        /// </summary>
        public static void PrefetchLatestVersion(string packageName)
        {
            if (_cache.ContainsKey(packageName) &&
                (DateTime.Now - _cache[packageName].fetchedAt).TotalMinutes < CacheDurationMinutes)
            {
                return; // まだキャッシュが有効
            }

            _ = GetLatestVersionAsync(packageName);
        }

        /// <summary>
        /// 現在インストールされているバージョンを取得する。
        /// </summary>
        public static string GetCurrentVersion(string packageName)
        {
            string path = Path.Combine(Directory.GetCurrentDirectory(), "Packages", packageName, "package.json");
            if (File.Exists(path))
            {
                try
                {
                    string json = File.ReadAllText(path);
                    var pkg = JsonUtility.FromJson<PackageInfo>(json);
                    return pkg.version;
                }
                catch { }
            }
            return "0.0.0";
        }

        /// <summary>
        /// アップデートが利用可能か判定する。
        /// </summary>
        /// <param name="packageName">パッケージ名</param>
        /// <param name="includePrerelease">ベータ版等を含めるか</param>
        public static bool IsUpdateAvailable(string packageName, bool includePrerelease = false)
        {
            string latest = GetLatestVersionCached(packageName);
            if (string.IsNullOrEmpty(latest)) return false;

            string current = GetCurrentVersion(packageName);

            if (!SemVer.TryParse(current, out var curVer)) return false;
            if (!SemVer.TryParse(latest, out var latVer)) return latest != current;

            // prerelease を含まない設定の時、latest が prerelease なら通知しない
            if (!includePrerelease && latVer.IsPreRelease) return false;

            return latVer > curVer;
        }

        #region Private Methods

        private static async Task<string> FetchJsonAsync(string url)
        {
            try
            {
                using (var request = UnityEngine.Networking.UnityWebRequest.Get(url))
                {
                    var operation = request.SendWebRequest();
                    while (!operation.isDone) await Task.Yield();

                    if (request.result == UnityEngine.Networking.UnityWebRequest.Result.Success)
                    {
                        return request.downloadHandler.text;
                    }
                    else
                    {
                        Debug.LogWarning($"[UpdateChecker] Failed to fetch {url}: {request.error}");
                        return null;
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[UpdateChecker] Exception fetching {url}: {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// index.json から指定パッケージの全バージョンキーを抽出する。
        /// JsonUtilityがDictionaryを扱えないため、簡易パーサーで抽出。
        /// </summary>
        private static List<string> ExtractAllVersions(string json, string packageName)
        {
            var versions = new List<string>();

            // "packageName" を探す
            string searchPattern = $"\"{packageName}\"";
            int pkgIndex = json.IndexOf(searchPattern, StringComparison.OrdinalIgnoreCase);
            if (pkgIndex == -1) return versions;

            // "versions" を探す
            int versionsIndex = json.IndexOf("\"versions\"", pkgIndex);
            if (versionsIndex == -1) return versions;

            // versions の { から対応する } までをスキャン
            int braceStart = json.IndexOf('{', versionsIndex);
            if (braceStart == -1) return versions;

            int depth = 0;
            int braceEnd = -1;
            for (int i = braceStart; i < json.Length; i++)
            {
                if (json[i] == '{') depth++;
                else if (json[i] == '}')
                {
                    depth--;
                    if (depth == 0)
                    {
                        braceEnd = i;
                        break;
                    }
                }
            }

            if (braceEnd == -1) return versions;

            string versionsBlock = json.Substring(braceStart + 1, braceEnd - braceStart - 1);

            // "X.Y.Z" のようなキーをすべて抽出
            int pos = 0;
            while (pos < versionsBlock.Length)
            {
                int start = versionsBlock.IndexOf('"', pos);
                if (start == -1) break;
                int end = versionsBlock.IndexOf('"', start + 1);
                if (end == -1) break;

                string key = versionsBlock.Substring(start + 1, end - start - 1);

                // キーの直後に : がある場合のみバージョンキーと判定
                int colonIdx = versionsBlock.IndexOf(':', end);
                // 次に { があるか確認（バージョンオブジェクト）
                if (colonIdx != -1 && colonIdx < versionsBlock.Length)
                {
                    // : の後に { を探す（バージョンエントリ）
                    int nextBrace = versionsBlock.IndexOf('{', colonIdx);
                    int nextQuote = versionsBlock.IndexOf('"', colonIdx);
                    // { が " より前にあればバージョンキー確定
                    if (nextBrace != -1 && (nextQuote == -1 || nextBrace < nextQuote))
                    {
                        if (SemVer.TryParse(key, out _))
                        {
                            versions.Add(key);
                        }
                    }
                }

                pos = end + 1;
            }

            return versions;
        }

        /// <summary>
        /// SemVerで比較し、最新バージョンを選択する。
        /// </summary>
        private static string SelectLatestVersion(List<string> versions, bool includePrerelease)
        {
            SemVer best = default;
            string bestStr = null;
            bool initialized = false;

            foreach (var v in versions)
            {
                if (!SemVer.TryParse(v, out var sv)) continue;

                if (!includePrerelease && sv.IsPreRelease) continue;

                if (!initialized || sv > best)
                {
                    best = sv;
                    bestStr = v;
                    initialized = true;
                }
            }

            return bestStr ?? (versions.Count > 0 ? versions[0] : null);
        }

        #endregion

        // ─── 自己更新（Auto Update）───

        private static bool _isUpdating;
        private static string _updateStatus;

        /// <summary>
        /// 更新中かどうか
        /// </summary>
        public static bool IsUpdating => _isUpdating;

        /// <summary>
        /// 現在の更新ステータスメッセージ
        /// </summary>
        public static string UpdateStatus => _updateStatus;

        /// <summary>
        /// パッケージを自動ダウンロード＆インストールする。
        /// VCCを使わずにPackages/内のファイルを直接差し替える。
        /// </summary>
        /// <param name="packageName">パッケージ名</param>
        /// <param name="targetVersion">インストール対象バージョン</param>
        public static async Task<bool> DownloadAndInstallUpdateAsync(string packageName, string targetVersion)
        {
            if (_isUpdating) return false;
            if (!GitHubReleaseUrls.TryGetValue(packageName, out string releaseBaseUrl))
            {
                Debug.LogError($"[UpdateChecker] Unknown package for auto-update: {packageName}");
                return false;
            }

            _isUpdating = true;
            _updateStatus = $"v{targetVersion} \u3092\u30c0\u30a6\u30f3\u30ed\u30fc\u30c9\u4e2d...";

            try
            {
                string zipUrl = $"{releaseBaseUrl}/v{targetVersion}/{packageName}-{targetVersion}.zip";
                string tempPath = Path.Combine(Path.GetTempPath(), "MorulabTools_Update");
                string zipPath = Path.Combine(tempPath, "update.zip");
                string extractPath = Path.Combine(tempPath, "extracted");

                // temp\u30c7\u30a3\u30ec\u30af\u30c8\u30ea\u3092\u30af\u30ea\u30a2
                if (Directory.Exists(tempPath))
                    Directory.Delete(tempPath, true);
                Directory.CreateDirectory(tempPath);
                Directory.CreateDirectory(extractPath);

                // DL
                using (var httpClient = new HttpClient())
                {
                    httpClient.Timeout = TimeSpan.FromMinutes(5);
                    var response = await httpClient.GetAsync(zipUrl);
                    if (!response.IsSuccessStatusCode)
                    {
                        // v\u306a\u3057\u3082\u8a66\u884c
                        string fallbackUrl = $"{releaseBaseUrl}/{targetVersion}/{packageName}-{targetVersion}.zip";
                        response = await httpClient.GetAsync(fallbackUrl);
                        if (!response.IsSuccessStatusCode)
                            throw new Exception($"Download failed: {response.StatusCode}");
                    }
                    var bytes = await response.Content.ReadAsByteArrayAsync();
                    File.WriteAllBytes(zipPath, bytes);
                }

                _updateStatus = "\u30c0\u30a6\u30f3\u30ed\u30fc\u30c9\u5b8c\u4e86\u3002\u5c55\u958b\u4e2d...";

                // \u89e3\u51cd
                await Task.Run(() => ZipFile.ExtractToDirectory(zipPath, extractPath));

                _updateStatus = "\u30d1\u30c3\u30b1\u30fc\u30b8\u3092\u66f4\u65b0\u4e2d...";

                // 差分更新
                string absolutePackagePath = Path.GetFullPath($"Packages/{packageName}");
                string sourceContentPath = extractPath;

                // ZIP\u5185\u306b\u30d1\u30c3\u30b1\u30fc\u30b8\u540d\u30c7\u30a3\u30ec\u30af\u30c8\u30ea\u304c\u3042\u308b\u5834\u5408\u306e\u5bfe\u5fdc
                if (Directory.Exists(Path.Combine(extractPath, packageName)))
                    sourceContentPath = Path.Combine(extractPath, packageName);

                // \u53e4\u3044\u30d5\u30a1\u30a4\u30eb\u306e\u524a\u9664\uff08\u4e0d\u8981\u306a\u30d5\u30a1\u30a4\u30eb\u3092\u6b8b\u3055\u306a\u3044\uff09
                if (Directory.Exists(absolutePackagePath))
                {
                    foreach (string file in Directory.GetFiles(absolutePackagePath, "*", SearchOption.AllDirectories))
                    {
                        string relativePath = file.Substring(absolutePackagePath.Length)
                            .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                        string destFile = Path.Combine(sourceContentPath, relativePath);

                        if (!File.Exists(destFile))
                        {
                            string metaFile = file + ".meta";
                            if (File.Exists(metaFile))
                                File.Delete(metaFile);
                            File.Delete(file);
                        }
                    }
                }

                // \u65b0\u30d5\u30a1\u30a4\u30eb\u306e\u30b3\u30d4\u30fc
                foreach (string file in Directory.GetFiles(sourceContentPath, "*", SearchOption.AllDirectories))
                {
                    string relativePath = file.Substring(sourceContentPath.Length)
                        .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    string destFile = Path.Combine(absolutePackagePath, relativePath);
                    string destDir = Path.GetDirectoryName(destFile);

                    if (!Directory.Exists(destDir))
                        Directory.CreateDirectory(destDir);

                    File.Copy(file, destFile, true);
                }

                // vpm-manifest.json \u66f4\u65b0
                _updateStatus = "vpm-manifest.json \u3092\u66f4\u65b0\u4e2d...";
                UpdateVpmManifest(packageName, targetVersion);

                // \u4e00\u6642\u30d5\u30a1\u30a4\u30eb\u524a\u9664
                try { Directory.Delete(tempPath, true); } catch { }

                AssetDatabase.Refresh();

                _updateStatus = $"\u2705 v{targetVersion} \u306b\u66f4\u65b0\u5b8c\u4e86\uff01";
                Debug.Log($"[UpdateChecker] Successfully updated {packageName} to v{targetVersion}");

                // \u30ad\u30e3\u30c3\u30b7\u30e5\u66f4\u65b0
                _cache[packageName] = (targetVersion, DateTime.Now);

                return true;
            }
            catch (Exception e)
            {
                _updateStatus = $"\u66f4\u65b0\u306b\u5931\u6557\u3057\u307e\u3057\u305f: {e.Message}";
                Debug.LogError($"[UpdateChecker] Update failed: {e}");
                return false;
            }
            finally
            {
                _isUpdating = false;
            }
        }

        /// <summary>
        /// vpm-manifest.json \u306e\u30d0\u30fc\u30b8\u30e7\u30f3\u3092\u66f4\u65b0\u3059\u308b
        /// </summary>
        private static void UpdateVpmManifest(string packageName, string newVersion)
        {
            string vpmManifestPath = Path.Combine(Directory.GetCurrentDirectory(), "Packages", "vpm-manifest.json");
            if (!File.Exists(vpmManifestPath))
            {
                Debug.LogWarning("[UpdateChecker] vpm-manifest.json not found");
                return;
            }

            string json = File.ReadAllText(vpmManifestPath);
            json = UpdateJsonVersion(json, "dependencies", packageName, newVersion);
            json = UpdateJsonVersion(json, "locked", packageName, newVersion);
            File.WriteAllText(vpmManifestPath, json);
        }

        private static string UpdateJsonVersion(string json, string section, string packageName, string newVersion)
        {
            string searchPattern = $"\"{packageName}\"";
            int packageIndex = json.IndexOf(searchPattern);

            while (packageIndex != -1)
            {
                int sectionStart = json.LastIndexOf($"\"{section}\"", packageIndex);
                if (sectionStart == -1)
                {
                    packageIndex = json.IndexOf(searchPattern, packageIndex + 1);
                    continue;
                }

                int versionStart = json.IndexOf("\"version\"", packageIndex);
                if (versionStart == -1)
                {
                    packageIndex = json.IndexOf(searchPattern, packageIndex + 1);
                    continue;
                }

                int valueStart = json.IndexOf('"', versionStart + 10) + 1;
                int valueEnd = json.IndexOf('"', valueStart);

                if (valueStart > 0 && valueEnd > valueStart)
                {
                    json = json.Substring(0, valueStart) + newVersion + json.Substring(valueEnd);
                    break;
                }

                packageIndex = json.IndexOf(searchPattern, packageIndex + 1);
            }

            return json;
        }

        [System.Serializable]
        private class PackageInfo
        {
            public string version;
        }
    }
}
