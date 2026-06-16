using UnityEngine;
using UnityEditor;
using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
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
        /// ブラウザでffmpegのダウンロードページを開く（手動DL用）
        /// </summary>
        public static void OpenDownloadPage()
        {
            string url;
            switch (Application.platform)
            {
                case RuntimePlatform.WindowsEditor:
                    url = "https://www.gyan.dev/ffmpeg/builds/";
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

        // ─── 自動ダウンロード＆インストール ───

        private static WebClient _activeClient;

        /// <summary>
        /// プラットフォーム別のFFmpegダウンロードURL
        /// </summary>
        private static string GetDownloadUrl()
        {
            switch (Application.platform)
            {
                case RuntimePlatform.WindowsEditor:
                    return "https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip";
                case RuntimePlatform.OSXEditor:
                    return "https://evermeet.cx/ffmpeg/getrelease/zip";
                default:
                    return null;
            }
        }

        /// <summary>
        /// FFmpegを自動ダウンロード＆インストールする。
        /// Library/MorulabTools/ffmpeg/ に自動配置し、パスを設定する。
        /// Windows・macOS対応。Linuxは手動DLへフォールバック。
        /// </summary>
        public static void DownloadAndInstall()
        {
            if (_activeClient != null)
            {
                Debug.LogWarning("[FFmpegManager] Download already in progress.");
                return;
            }

            string url = GetDownloadUrl();
            if (url == null)
            {
                EditorUtility.DisplayDialog(
                    "Unsupported Platform",
                    "Automatic FFmpeg download is supported on Windows and macOS only.\n\n" +
                    "Please download FFmpeg manually and set the path.",
                    "Open Download Page", "Cancel");
                // OpenDownloadPageは呼び出し側で判断
                return;
            }

            string installDir = DefaultInstallDir;
            Directory.CreateDirectory(installDir);

            string zipPath = Path.Combine(installDir, "__ffmpeg_dl.tmp");
            if (File.Exists(zipPath))
                File.Delete(zipPath);

            _activeClient = new WebClient();

            _activeClient.DownloadProgressChanged += (sender, e) =>
            {
                bool cancel = EditorUtility.DisplayCancelableProgressBar(
                    "Downloading FFmpeg",
                    $"{e.ProgressPercentage}% ({e.BytesReceived / 1024 / 1024}MB / {e.TotalBytesToReceive / 1024 / 1024}MB)",
                    e.ProgressPercentage / 100f);

                if (cancel)
                    _activeClient.CancelAsync();
            };

            _activeClient.DownloadFileCompleted += (sender, e) =>
            {
                EditorUtility.ClearProgressBar();

                if (e.Cancelled)
                {
                    Debug.Log("[FFmpegManager] Download cancelled.");
                    CleanupTemp(zipPath);
                    FinishDownload();
                    return;
                }

                if (e.Error != null)
                {
                    Debug.LogError($"[FFmpegManager] Download failed: {e.Error.Message}");
                    EditorUtility.DisplayDialog("Download Failed",
                        $"Failed to download FFmpeg:\n{e.Error.Message}", "OK");
                    CleanupTemp(zipPath);
                    FinishDownload();
                    return;
                }

                // ── Extract ──
                try
                {
                    EditorUtility.DisplayProgressBar("Installing FFmpeg", "Extracting...", 0.9f);

                    // 古いffmpegファイルをクリーンアップ
                    foreach (var f in Directory.GetFiles(installDir, "*", SearchOption.AllDirectories))
                    {
                        if (!f.EndsWith("__ffmpeg_dl.tmp"))
                        {
                            try { File.Delete(f); } catch { }
                        }
                    }

                    // ZIP解凍
                    ZipFile.ExtractToDirectory(zipPath, installDir, overwriteFiles: true);
                    File.Delete(zipPath);

                    EditorUtility.ClearProgressBar();

                    // ffmpeg実行ファイルを検索
                    string ffmpegExe = FindFFmpegExecutable(installDir);

                    if (string.IsNullOrEmpty(ffmpegExe))
                    {
                        EditorUtility.DisplayDialog("Installation Failed",
                            "FFmpeg was downloaded but the executable could not be found.\n" +
                            $"Check: {installDir}", "OK");
                    }
                    else
                    {
                        // Mac/Linux: 実行権限を付与
                        if (Application.platform == RuntimePlatform.OSXEditor)
                        {
                            try
                            {
                                var chmod = new ProcessStartInfo
                                {
                                    FileName = "chmod",
                                    Arguments = $"+x \"{ffmpegExe}\"",
                                    UseShellExecute = false,
                                    CreateNoWindow = true
                                };
                                Process.Start(chmod)?.WaitForExit();
                            }
                            catch { }
                        }

                        if (SetFFmpegPath(ffmpegExe))
                        {
                            EditorUtility.DisplayDialog("Installation Complete! 🎉",
                                $"FFmpeg has been installed to:\n{ffmpegExe}\n\nYou're all set!", "OK");
                            Debug.Log($"[FFmpegManager] FFmpeg installed: {ffmpegExe}");
                        }
                        else
                        {
                            EditorUtility.DisplayDialog("Installation Failed",
                                "FFmpeg was extracted but failed verification.\n" +
                                $"Path: {ffmpegExe}", "OK");
                        }
                    }
                }
                catch (Exception ex)
                {
                    EditorUtility.ClearProgressBar();
                    Debug.LogError($"[FFmpegManager] Extraction failed: {ex.Message}");
                    EditorUtility.DisplayDialog("Installation Failed",
                        $"Failed to extract FFmpeg:\n{ex.Message}", "OK");
                }

                FinishDownload();
            };

            Debug.Log($"[FFmpegManager] Downloading FFmpeg from: {url}");
            _activeClient.DownloadFileAsync(new Uri(url), zipPath);
        }

        private static void CleanupTemp(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }

        private static void FinishDownload()
        {
            _activeClient?.Dispose();
            _activeClient = null;
            EditorUtility.ClearProgressBar();
        }

        /// <summary>
        /// インストールディレクトリからffmpeg実行ファイルを再帰的に探す
        /// </summary>
        private static string FindFFmpegExecutable(string dir)
        {
            if (!Directory.Exists(dir))
                return null;

            string exeName = Application.platform == RuntimePlatform.WindowsEditor
                ? "ffmpeg.exe"
                : "ffmpeg";

            // 完全一致
            var exact = Directory.GetFiles(dir, exeName, SearchOption.AllDirectories);
            if (exact.Length > 0)
                return exact[0];

            // ワイルドカード
            var wild = Directory.GetFiles(dir, "ffmpeg*", SearchOption.AllDirectories);
            foreach (var f in wild)
            {
                var name = Path.GetFileName(f);
                if (name == "ffmpeg" || name == "ffmpeg.exe")
                    return f;
            }

            return null;
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
            int choice = EditorUtility.DisplayDialogComplex(
                "FFmpeg Required",
                "Some Morulab tools require FFmpeg for video export.\n\n" +
                "FFmpeg was not found on your system.\n\n" +
                "How would you like to proceed?",
                "Auto Install",   // 0
                "Later",          // 1
                "Manual Download"); // 2

            switch (choice)
            {
                case 0: // Auto Install
                    DownloadAndInstall();
                    break;
                case 2: // Manual Download
                    OpenDownloadPage();
                    if (EditorUtility.DisplayDialog(
                        "Locate FFmpeg",
                        "After downloading and extracting FFmpeg,\nclick 'Browse' to locate it.",
                        "Browse...",
                        "Cancel"))
                    {
                        BrowseAndSet();
                    }
                    break;
                // case 1: Later → 何もしない
            }
        }
    }
}
