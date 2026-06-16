using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

        [System.Serializable]
        private class PackageInfo
        {
            public string version;
        }
    }
}
