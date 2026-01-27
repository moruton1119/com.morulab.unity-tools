using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Moruton.BLMConnector
{
    public class BLMProductTagger : AssetPostprocessor
    {
        static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
        {
            if (!AssetImportQueue.IsImporting) return;
            string pid = AssetImportQueue.CurrentProductId;
            if (string.IsNullOrEmpty(pid)) return;

            var roots = new HashSet<string>();

            foreach (var path in importedAssets)
            {
                if (!path.StartsWith("Assets/")) continue;
                string root = GetTopLevelFolder(path);
                if (!string.IsNullOrEmpty(root))
                {
                    roots.Add(root);
                }
            }

            if (roots.Count == 0) return;

            string labelId = $"BLM_PID_{pid}";
            string labelTag = "BLM_Managed";

            foreach (var rootPath in roots)
            {
                UnityEngine.Object asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(rootPath);
                if (asset != null)
                {
                    var labels = new List<string>(AssetDatabase.GetLabels(asset));
                    bool muddy = false;
                    
                    if (!labels.Contains(labelTag)) { labels.Add(labelTag); muddy = true; }
                    if (!labels.Contains(labelId)) { labels.Add(labelId); muddy = true; }

                    if (muddy)
                    {
                        AssetDatabase.SetLabels(asset, labels.ToArray());
                    }
                }
            }
        }

        private static string GetTopLevelFolder(string path)
        {
            var parts = path.Split('/');
            if (parts.Length < 2) return null; 
            if (parts.Length == 2) return path; 
            return $"{parts[0]}/{parts[1]}";
        }
    }
}
