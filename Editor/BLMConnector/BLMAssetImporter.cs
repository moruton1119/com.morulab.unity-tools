using System.IO;
using UnityEditor;
using UnityEngine;

namespace Moruton.BLMConnector
{
    public static class BLMAssetImporter
    {
        public static void ImportAsset(BoothAsset asset, string productName)
        {
            if (asset.assetType == AssetType.UnityPackage)
            {
                AssetDatabase.ImportPackage(asset.fullPath, true);
            }
            else
            {
                string projectPath = Directory.GetParent(Application.dataPath)?.FullName;
                if (string.IsNullOrEmpty(projectPath))
                {
                    Debug.LogError("[BLM] Failed to get project path");
                    return;
                }

                string sanitizedProductName = SanitizeFolderName(productName);
                string absoluteDestFolder = Path.Combine(projectPath, BLMConstants.DefaultImportFolder, sanitizedProductName);
                
                if (!Directory.Exists(absoluteDestFolder))
                {
                    Directory.CreateDirectory(absoluteDestFolder);
                }

                string absoluteDestPath = Path.Combine(absoluteDestFolder, asset.fileName);
                File.Copy(asset.fullPath, absoluteDestPath, true);
                AssetDatabase.Refresh();

                string relativeAssetPath = $"{BLMConstants.DefaultImportFolder}/{sanitizedProductName}/{asset.fileName}";
                
                if (asset.assetType == AssetType.Texture)
                {
                    ConfigureTextureImportSettings(relativeAssetPath);
                }

                Debug.Log($"[BLM] Imported {asset.fileName} to {relativeAssetPath}");
            }
        }

        private static void ConfigureTextureImportSettings(string relativeAssetPath)
        {
            TextureImporter importer = AssetImporter.GetAtPath(relativeAssetPath) as TextureImporter;
            if (importer != null)
            {
                importer.textureType = TextureImporterType.Default;
                importer.SaveAndReimport();
            }
        }

        private static string SanitizeFolderName(string name)
        {
            char[] invalidChars = Path.GetInvalidFileNameChars();
            foreach (char c in invalidChars)
            {
                name = name.Replace(c, '_');
            }
            if (name.Length > BLMConstants.MaxFolderNameLength)
            {
                name = name.Substring(0, BLMConstants.MaxFolderNameLength);
            }
            return name;
        }
    }
}
