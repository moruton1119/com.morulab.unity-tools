using UnityEngine;
using UnityEditor;
using System.IO;
using System.Linq;
using Process = System.Diagnostics.Process;
using ProcessStartInfo = System.Diagnostics.ProcessStartInfo;

namespace MorulabTools.Core
{
    /// <summary>
    /// 汎用FFmpeg管理クラス。
    /// ffmpegを使用するツール（ThumbnailStudio等）はこのアセンブリを参照する。
    /// ランチャー起動時に FFmpegManager.HasFFmpegUsers() で判定し、
    /// ffmpegが必要なプロジェクトでのみチェック・DLを促す。
    /// </summary>
    public static class FFmpegManager
    {
        private const string PrefKey = "Morulab.FFmpegManager.ffmpegPath";
        private const string PrefKeyVerified = "Morulab.FFmpegManager.verified";

        /// <summary>
        /// デフォルトのffmpeg保存先（Library配下でgit/gitignore安全）
        /// </summary>
        public static string DefaultInstallDir => Path.Combine(Application.dataPath, "../Library/MorulabTools/ffmpeg");

        /// <summary>
        /// 現在のffmpegパス（未設定の場合は "ffmpeg" = PATH検索）
        /// </summary>
        public static string FFmpegPath
        {
            get => EditorPrefs.GetString(PrefKey, "ffmpeg");
            private set => EditorPrefs.SetString(PrefKey, value);
        }

        /// <summary>
        /// ffmpegが利用可能かどうか
        /// </summary>
        public static bool IsAvailable => CheckAvailable(FFmpegPath);

        /// <summary>
        /// このプロジェクトでFFmpegManagerを参照しているツールが存在するか判定する。
        /// FFmpegManagerアセンブリを参照しているアセンブリが1つでもあればtrue。
        /// </summary>
        public static bool HasFFmpegUsers()
        {
            var assemblies = System.AppDomain.CurrentDomain.GetAssemblies();
            foreach (var asm in assemblies)
            {
                try
                {
                    var refs = asm.GetReferencedAssemblies();
                    if (refs.Any(r => r.Name == "MorulabTools.FFmpegManager.Editor"))
                    {
                        // 自分自身は除外
                        if (asm.GetName().Name != "MorulabTools.FFmpegManager.Editor")
                            return true;
                    }
                }
                catch { }
            }
            return false;
        }

        /// <summary>
        /// ffmpegが利用可能かチェック。成功したらverifiedフラグを立てる。
        /// </summary>
        public static bool CheckAvailable(string path = null)
        {
            if (string.IsNullOrEmpty(path))
                path = FFmpegPath;

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = path,
                    Arguments = "-version",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                using (var proc = Process.Start(psi))
                {
                    proc.WaitForExit(5000);
                    bool ok = proc.ExitCode == 0;
                    if (ok)
                    {
                        EditorPrefs.SetBool(PrefKeyVerified, true);
                    }
                    return ok;
                }
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 指定パスにffmpegを設定する
        /// </summary>
        public static bool SetFFmpegPath(string path)
        {
            if (!CheckAvailable(path))
                return false;

            FFmpegPath = path;
            EditorPrefs.SetBool(PrefKeyVerified, true);
            Debug.Log($"[FFmpegManager] ffmpeg path set to: {path}");
            return true;
        }

        /// <summary>
        /// ブラウザでffmpegのダウンロードページを開く
        /// </summary>
        public static void OpenDownloadPage()
        {
            // Windows: gyan.dev（LGPL essentials build）
            // Mac: evermeet.cx
            // Linux: johnvansickle.com
            string url;
            switch (Application.platform)
            {
                case RuntimePlatform.WindowsEditor:
                    url = "https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip";
                    break;
                case RuntimePlatform.OSXEditor:
                    url = "https://evermeet.cx/ffmpeg/";
                    break;
                default:
                    url = "https://johnvansickle.com/ffmpeg/";
                    break;
            }
            Application.OpenURL(url);
        }

        /// <summary>
        /// ファイルブラウザでffmpegを選択して設定
        /// </summary>
        public static bool BrowseAndSet()
        {
            string startDir = EditorPrefs.GetString(PrefKey + "_lastDir", "");
            if (string.IsNullOrEmpty(startDir) || !Directory.Exists(startDir))
                startDir = System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);

            string filter = Application.platform == RuntimePlatform.WindowsEditor ? "exe" : "";
            string path = EditorUtility.OpenFilePanelWithFilters(
                "Select ffmpeg executable",
                startDir,
                string.IsNullOrEmpty(filter) ? new[] { "All files", "*" } : new[] { "ffmpeg", filter, "All files", "*" });

            if (string.IsNullOrEmpty(path))
                return false;

            if (SetFFmpegPath(path))
            {
                EditorPrefs.SetString(PrefKey + "_lastDir", Path.GetDirectoryName(path));
                return true;
            }
            else
            {
                EditorUtility.DisplayDialog(
                    "Invalid ffmpeg",
                    $"The selected file does not appear to be a valid ffmpeg executable.\nPath: {path}",
                    "OK");
                return false;
            }
        }

        /// <summary>
        /// ffmpegコマンドを実行する。各ツールから呼び出す共通メソッド。
        /// </summary>
        public static int Run(string arguments)
        {
            var psi = new ProcessStartInfo
            {
                FileName = string.IsNullOrEmpty(FFmpegPath) ? "ffmpeg" : FFmpegPath,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using (var process = Process.Start(psi))
            {
                process.ErrorDataReceived += (s, e) => { };
                process.OutputDataReceived += (s, e) => { };
                process.BeginErrorReadLine();
                process.BeginOutputReadLine();
                process.WaitForExit();
                return process.ExitCode;
            }
        }

        /// <summary>
        /// ランチャー等の起動スキャンで呼ぶ。
        /// FFmpegManagerを参照するツールがあり、かつffmpeg未検証の場合にDLを促す。
        /// 一度検証したら EditorPrefs でスキップ。
        /// </summary>
        public static void StartupCheck()
        {
            // FFmpegManagerを参照するツールがない場合はスキップ
            if (!HasFFmpegUsers())
                return;

            // 一度検証済みならスキップ
            if (EditorPrefs.GetBool(PrefKeyVerified, false))
            {
                // 念のため軽くチェック（パスが変わってる可能性）
                if (!IsAvailable)
                {
                    EditorPrefs.SetBool(PrefKeyVerified, false);
                }
                else
                {
                    return;
                }
            }

            // ffmpegが見つかれば終わり
            if (IsAvailable)
            {
                EditorPrefs.SetBool(PrefKeyVerified, true);
                return;
            }

            // 見つからない場合はDLを促す
            bool download = EditorUtility.DisplayDialog(
                "FFmpeg Required",
                "Some Morulab tools require FFmpeg for video export.\n\n" +
                "FFmpeg was not found on your system.\n\n" +
                "Would you like to download it now?",
                "Download FFmpeg",
                "Later");

            if (download)
            {
                OpenDownloadPage();

                if (EditorUtility.DisplayDialog(
                    "Locate FFmpeg",
                    "After downloading and extracting FFmpeg,\nclick 'Browse' to locate it.",
                    "Browse...",
                    "Cancel"))
                {
                    BrowseAndSet();
                }
            }
        }
    }
}
