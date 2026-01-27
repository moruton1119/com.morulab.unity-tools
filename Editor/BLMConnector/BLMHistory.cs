using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Moruton.BLMConnector
{
    public static class BLMHistory
    {
        private static HashSet<string> installedIds = new HashSet<string>();
        private static bool isLoaded = false;

        public static void Refresh()
        {
            installedIds.Clear();

            string[] guids = AssetDatabase.FindAssets("l:BLM_Managed");
            foreach (var guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
                if (asset == null) continue;

                var labels = AssetDatabase.GetLabels(asset);
                foreach (var label in labels)
                {
                    if (label.StartsWith("BLM_PID_"))
                    {
                        string pid = label.Substring("BLM_PID_".Length);
                        installedIds.Add(pid);
                    }
                }
            }

            isLoaded = true;
        }

        public static void Load()
        {
            if (!isLoaded) Refresh();
        }

        public static bool IsInstalled(BoothProduct product) => IsInstalled(product?.id);

        public static bool IsInstalled(string productId)
        {
            if (string.IsNullOrEmpty(productId)) return false;
            Load();
            return installedIds.Contains(productId);
        }

        public static void MarkAsInstalled(string productId)
        {
            Refresh();
        }

        public static void Unmark(string productId)
        {
            if (installedIds.Contains(productId))
            {
                installedIds.Remove(productId);
            }
        }
    }
}
