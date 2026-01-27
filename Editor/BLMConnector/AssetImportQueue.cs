using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Moruton.BLMConnector
{
    [InitializeOnLoad]
    public static class AssetImportQueue
    {
        private const string PREF_QUEUE = "BLM_Standalone_ImportQueue_List";
        private const string PREF_IS_IMPORTING = "BLM_Standalone_ImportQueue_IsImporting";
        private const string PREF_INTERACTIVE = "BLM_Standalone_ImportQueue_Interactive";

        [Serializable]
        private class QueueItem
        {
            public string path;
            public string productId;
        }

        private static Queue<QueueItem> importQueue = new Queue<QueueItem>();
        private static bool isImporting = false;
        private static QueueItem currentItem = null;

        static AssetImportQueue()
        {
            LoadState();
            if (isImporting && importQueue.Count > 0)
            {
                EditorApplication.delayCall += () =>
                {
                    Debug.Log("[BLM Standalone] Resuming import queue after domain reload...");
                    isImporting = false; 
                    CheckQueue();
                };
            }
            else if (isImporting)
            {
                isImporting = false;
                SaveState();
            }
        }

        public static bool InteractiveMode
        {
            get => EditorPrefs.GetBool(PREF_INTERACTIVE, true);
            set => EditorPrefs.SetBool(PREF_INTERACTIVE, value);
        }

        public static void Enqueue(string packagePath, string productId)
        {
            if (string.IsNullOrEmpty(packagePath)) return;
            if (Contains(packagePath)) return;
            importQueue.Enqueue(new QueueItem { path = packagePath, productId = productId });
            SaveState();
        }

        public static void EnqueueMultiple(IEnumerable<string> packagePaths, string productId)
        {
            foreach (var path in packagePaths)
            {
                if (!string.IsNullOrEmpty(path) && !Contains(path))
                    importQueue.Enqueue(new QueueItem { path = path, productId = productId });
            }
            SaveState();
        }

        private static bool Contains(string path)
        {
            foreach (var item in importQueue) if (item.path == path) return true;
            return false;
        }

        public static void StartImport()
        {
            if (isImporting)
            {
                Debug.LogWarning("[BLM Standalone] Import system thinks it is already running.");
                return;
            }

            if (importQueue.Count == 0) return;
            CheckQueue();
        }

        public static void ClearQueue()
        {
            importQueue.Clear();
            isImporting = false;
            SaveState();
        }

        private static void ProcessNext()
        {
            if (importQueue.Count == 0)
            {
                isImporting = false;
                SaveState();
                return;
            }

            isImporting = true;
            SaveState(); 

            currentItem = importQueue.Dequeue();
            SaveState(); 

            if (!System.IO.File.Exists(currentItem.path))
            {
                isImporting = false;
                CheckQueue();
                return;
            }

            CleanupEvents();

            AssetDatabase.importPackageCompleted += OnImportCompleted;
            AssetDatabase.importPackageCancelled += OnImportCancelled;
            AssetDatabase.importPackageFailed += OnImportFailed;

            try
            {
                AssetDatabase.ImportPackage(currentItem.path, interactive: InteractiveMode);
            }
            catch (Exception ex)
            {
                OnImportFailed(currentItem.path, ex.Message);
            }
        }

        private static void CheckQueue()
        {
            if (isImporting) return;
            ProcessNext();
        }

        public static event Action OnImportFinishedAction;

        private static void OnImportCompleted(string packageName)
        {
            if (currentItem != null && !string.IsNullOrEmpty(currentItem.productId))
            {
                BLMHistory.MarkAsInstalled(currentItem.productId);
            }

            OnImportFinishedAction?.Invoke();

            CleanupEvents();
            isImporting = false;
            SaveState();
            EditorApplication.delayCall += CheckQueue;
        }

        private static void OnImportCancelled(string packageName)
        {
            CleanupEvents();
            isImporting = false;
            SaveState();
            EditorApplication.delayCall += CheckQueue;
        }

        private static void OnImportFailed(string packageName, string errorMessage)
        {
            CleanupEvents();
            isImporting = false;
            SaveState();
            EditorApplication.delayCall += CheckQueue;
        }

        private static void CleanupEvents()
        {
            AssetDatabase.importPackageCompleted -= OnImportCompleted;
            AssetDatabase.importPackageCancelled -= OnImportCancelled;
            AssetDatabase.importPackageFailed -= OnImportFailed;
        }

        private static void SaveState()
        {
            var lines = new List<string>();
            foreach (var item in importQueue) lines.Add($"{item.path}|{item.productId}");
            EditorPrefs.SetString(PREF_QUEUE, string.Join("\n", lines));
            EditorPrefs.SetBool(PREF_IS_IMPORTING, isImporting);
        }

        private static void LoadState()
        {
            string q = EditorPrefs.GetString(PREF_QUEUE, "");
            importQueue.Clear();
            if (!string.IsNullOrEmpty(q))
            {
                foreach (var line in q.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var parts = line.Split('|');
                    if (parts.Length >= 2) importQueue.Enqueue(new QueueItem { path = parts[0], productId = parts[1] });
                }
            }
            isImporting = EditorPrefs.GetBool(PREF_IS_IMPORTING, false);
        }

        public static int RemainingCount => importQueue.Count;
        public static bool IsImporting => isImporting;

        public static string[] GetQueueItems()
        {
            var list = new List<string>();
            foreach (var item in importQueue) list.Add(item.path);
            return list.ToArray();
        }
        public static string CurrentProductId => currentItem?.productId;
    }
}
