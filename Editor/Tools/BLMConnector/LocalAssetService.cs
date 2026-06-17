using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEditor;

namespace Moruton.BLMConnector
{
    public static class LocalAssetService
    {
        public static List<BoothProduct> LoadLocalAssets(string libraryRoot)
        {
            var products = new List<BoothProduct>();

            if (string.IsNullOrEmpty(libraryRoot))
            {
                Debug.LogWarning("[BLM Standalone] Library root is null or empty. Cannot load local assets.");
                return products;
            }

            string localAssetsPath = Path.Combine(libraryRoot, BLMConstants.LocalAssetsFolderName);

            if (!Directory.Exists(localAssetsPath))
            {
                Debug.Log($"[BLM Standalone] LocalAssets folder not found at: {localAssetsPath}");
                return products;
            }

            try
            {
                var folders = Directory.GetDirectories(localAssetsPath);
                // Debug.Log suppressed for performance

                foreach (var folder in folders)
                {
                    var product = new BoothProduct
                    {
                        id = BLMConstants.LocalIdPrefix + Path.GetFileName(folder),
                        name = Path.GetFileName(folder),
                        shopName = BLMConstants.LocalShopName,
                        rootFolderPath = folder,
                        sourceType = "Local",
                        packages = new List<BoothPackage>(),
                        assets = new List<BoothAsset>()
                    };

                    var allFiles = Directory.GetFiles(folder, "*.*", SearchOption.AllDirectories);

                    // 1回の走査でサムネイル + アセット両方取得
                    foreach (var file in allFiles)
                    {
                        string ext = Path.GetExtension(file).ToLower();
                        string fileName = Path.GetFileName(file);

                        if (ext == ".blend") continue;

                        // サムネイルは最初の画像ファイル
                        if (product.thumbnailPath == null && AssetTypeUtils.IsImageFile(ext))
                        {
                            product.thumbnailPath = file;
                        }

                        if (ext == ".unitypackage")
                        {
                            product.packages.Add(new BoothPackage
                            {
                                fileName = fileName,
                                fullPath = file
                            });

                            product.assets.Add(new BoothAsset
                            {
                                fileName = fileName,
                                fullPath = file,
                                assetType = AssetType.UnityPackage
                            });
                        }
                        else if (AssetTypeUtils.IsImageFile(ext))
                        {
                            if (file == product.thumbnailPath) continue;

                            product.assets.Add(new BoothAsset
                            {
                                fileName = fileName,
                                fullPath = file,
                                assetType = AssetType.Texture
                            });
                        }
                    }

                    if (product.packages.Count > 0)
                    {
                        products.Add(product);
                        // Debug.Log suppressed for performance
                    }
                    else
                    {
                        // Debug.Log suppressed for performance
                    }
                }

                // Debug.Log suppressed for performance
            }
            catch (Exception ex)
            {
                Debug.LogError($"[BLM Standalone] Error loading local assets: {ex.Message}");
            }

            return products;
        }
    }
}
