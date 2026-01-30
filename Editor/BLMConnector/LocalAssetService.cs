using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEditor;

namespace Moruton.BLMConnector
{
    public static class LocalAssetService
    {
        /// <summary>
        /// Load local development assets from {libraryRoot}/LocalAssets/ folder
        /// </summary>
        public static List<BoothProduct> LoadLocalAssets(string libraryRoot)
        {
            var products = new List<BoothProduct>();
            
            if (string.IsNullOrEmpty(libraryRoot))
            {
                Debug.LogWarning("[BLM Standalone] Library root is null or empty. Cannot load local assets.");
                return products;
            }

            string localAssetsPath = Path.Combine(libraryRoot, "LocalAssets");
            
            if (!Directory.Exists(localAssetsPath))
            {
                Debug.Log($"[BLM Standalone] LocalAssets folder not found at: {localAssetsPath}");
                return products;
            }

            try
            {
                var folders = Directory.GetDirectories(localAssetsPath);
                Debug.Log($"[BLM Standalone] Scanning {folders.Length} folders in LocalAssets...");

                foreach (var folder in folders)
                {
                    var product = new BoothProduct
                    {
                        id = "local_" + Path.GetFileName(folder),
                        name = Path.GetFileName(folder),
                        shopName = "Local",
                        rootFolderPath = folder,
                        sourceType = "Local"
                    };

                    // Find ANY image file in root as thumbnail
                    string[] imagePatterns = { "*.png", "*.jpg", "*.jpeg", "*.tga", "*.psd" };
                    foreach (var pattern in imagePatterns)
                    {
                        var images = Directory.GetFiles(folder, pattern, SearchOption.TopDirectoryOnly);
                        if (images.Length > 0)
                        {
                            product.thumbnailPath = images[0]; // Use first found
                            Debug.Log($"[BLM Standalone] Found thumbnail for {product.name}: {Path.GetFileName(images[0])}");
                            break;
                        }
                    }

                    // Find all .unitypackage files in root
                    var packages = Directory.GetFiles(folder, "*.unitypackage", SearchOption.TopDirectoryOnly);
                    foreach (var pkg in packages)
                    {
                        product.packages.Add(new BoothPackage
                        {
                            fileName = Path.GetFileName(pkg),
                            fullPath = pkg
                        });
                    }

                    // Only add if at least one .unitypackage exists
                    if (product.packages.Count > 0)
                    {
                        products.Add(product);
                        Debug.Log($"[BLM Standalone] Loaded local asset: {product.name} ({product.packages.Count} packages)");
                    }
                    else
                    {
                        Debug.LogWarning($"[BLM Standalone] Skipping folder (no .unitypackage found): {product.name}");
                    }
                }

                Debug.Log($"[BLM Standalone] Successfully loaded {products.Count} local assets.");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[BLM Standalone] Error loading local assets: {ex.Message}");
            }

            return products;
        }
    }
}
