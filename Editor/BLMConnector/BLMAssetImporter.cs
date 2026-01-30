using System.IO;
using UnityEditor;
using UnityEngine;

namespace Moruton.BLMConnector
{
    public static class BLMAssetImporter
    {
        private const string DefaultImportFolder = "Assets/BLM_Imports";

        public static void ImportAsset(BoothAsset asset, string productName)
        {
            if (asset.assetType == AssetType.UnityPackage)
            {
                AssetDatabase.ImportPackage(asset.fullPath, true);
            }
            else
            {
                // 商品ごとにフォルダを作成 (オプションA)
                string sanitizedProductName = SanitizeFolderName(productName);
                string destinationFolder = Path.Combine(DefaultImportFolder, sanitizedProductName);
                
                // フォルダが存在しない場合は作成
                if (!Directory.Exists(destinationFolder))
                {
                    Directory.CreateDirectory(destinationFolder);
                }

                // Assets配下にコピー
                string destPath = Path.Combine(destinationFolder, asset.fileName);
                File.Copy(asset.fullPath, destPath, true);
                AssetDatabase.Refresh();

                // インポート後の設定（テクスチャの場合）
                if (asset.assetType == AssetType.Texture)
                {
                    ConfigureTextureImportSettings(destPath);
                }

                Debug.Log($"[BLM] Imported {asset.fileName} to {destPath}");
            }
        }

        private static void ConfigureTextureImportSettings(string assetPath)
        {
            // Assetsフォルダからの相対パスに変換
            string relativePath = assetPath.Replace("\\", "/");
            if (relativePath.StartsWith(Application.dataPath))
            {
                relativePath = "Assets" + relativePath.Substring(Application.dataPath.Length);
            }

            TextureImporter importer = AssetImporter.GetAtPath(relativePath) as TextureImporter;
            if (importer != null)
            {
                importer.textureType = TextureImporterType.Default;
                importer.SaveAndReimport();
            }
        }

        private static string SanitizeFolderName(string name)
        {
            // ファイル名に使用できない文字を除去
            char[] invalidChars = Path.GetInvalidFileNameChars();
            foreach (char c in invalidChars)
            {
                name = name.Replace(c, '_');
            }
            // 長すぎる名前を短縮
            if (name.Length > 50)
            {
                name = name.Substring(0, 50);
            }
            return name;
        }
    }
}
