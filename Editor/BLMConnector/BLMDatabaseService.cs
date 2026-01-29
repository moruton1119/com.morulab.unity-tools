using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;

#if BLM_LOCAL_CONNECTOR_HAS_SQLITE
using Mono.Data.Sqlite;
using System.Data;
#endif

namespace Moruton.BLMConnector
{
    public static class BLMDatabaseService
    {
        public static string GetDefaultDbPath()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appData, "pm.booth.library-manager", "data.db");
        }

        public static string LibraryRoot { get; private set; }

        public static List<BoothProduct> LoadProducts(string dbPath)
        {
            var products = new List<BoothProduct>();
            if (!File.Exists(dbPath))
            {
                Debug.LogError($"[BLM Standalone] Database file not found at: {dbPath}");
                return products;
            }

#if BLM_LOCAL_CONNECTOR_HAS_SQLITE
            try
            {
                string connectionString = $"URI=file:{dbPath};ReadOnly=True";
                string currentLibraryRoot = "";

                using (var connection = new SqliteConnection(connectionString))
                {
                    connection.Open();

                    // 1. Get library root path from preferences
                    currentLibraryRoot = LoadLibraryRoot(connection);
                    LibraryRoot = currentLibraryRoot; 

                    if (string.IsNullOrEmpty(currentLibraryRoot))
                    {
                        // Fallback logic if needed
                    }

                    if (string.IsNullOrEmpty(currentLibraryRoot))
                    {
                        Debug.LogWarning("[BLM Standalone] Could not find library root in database preferences.");
                    }

                    Debug.Log($"[BLM Standalone] Library Root: {currentLibraryRoot}");

                    // 2. Get Products
                    using (var cmd = connection.CreateCommand())
                    {
                        cmd.CommandText = @"
                            SELECT 
                                i.id, 
                                i.name, 
                                s.name as shop_name,
                                i.shop_subdomain,
                                i.thumbnail_url
                            FROM booth_items i
                            LEFT JOIN shops s ON i.shop_subdomain = s.subdomain";

                        using (var reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                var p = new BoothProduct
                                {
                                    id = reader["id"].ToString(),
                                    name = reader["name"].ToString(),
                                    shopName = reader["shop_name"] != DBNull.Value ? reader["shop_name"].ToString() : "Unknown",
                                    shopSubdomain = reader["shop_subdomain"] != DBNull.Value ? reader["shop_subdomain"].ToString() : "",
                                    thumbnailUrl = reader["thumbnail_url"] != DBNull.Value ? reader["thumbnail_url"].ToString() : ""
                                };
                                
                                string productPath = Path.Combine(currentLibraryRoot, p.shopSubdomain, p.id);

                                if (!Directory.Exists(productPath))
                                {
                                    string foundPath = FindFuzzyPath(currentLibraryRoot, p.id, p.name, p.shopSubdomain);
                                    if (!string.IsNullOrEmpty(foundPath)) productPath = foundPath;
                                }

                                p.rootFolderPath = productPath;

                                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                                string cacheDir = Path.Combine(appData, "pm.booth.library-manager", "cache", "thumbnails");
                                string png = Path.Combine(cacheDir, $"{p.id}.png");
                                string jpg = Path.Combine(cacheDir, $"{p.id}.jpg");
                                if (File.Exists(png)) p.thumbnailPath = png;
                                else if (File.Exists(jpg)) p.thumbnailPath = jpg;

                                products.Add(p);
                            }
                        }
                    }
                    Debug.Log($"[BLM Standalone] Successfully loaded {products.Count} products.");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[BLM Standalone] Database Error: {ex.Message}");
            }
#endif
            return products;
        }

#if BLM_LOCAL_CONNECTOR_HAS_SQLITE
        public static string LoadLibraryRoot(SqliteConnection connection)
        {
            try
            {
                using (var cmd = connection.CreateCommand())
                {
                    cmd.CommandText = "SELECT value FROM preferences WHERE key = 'library_root'";
                    var result = cmd.ExecuteScalar();
                    if (result != null) return result.ToString();

                    cmd.CommandText = "SELECT value FROM preferences WHERE key = 'item_directory_path'";
                    result = cmd.ExecuteScalar();
                    return result?.ToString();
                }
            }
            catch { return null; }
        }
#endif

        public static string FindFuzzyPath(string libraryRoot, string productId, string productName, string shopSubdomain)
        {
            if (string.IsNullOrEmpty(libraryRoot) || !Directory.Exists(libraryRoot)) return null;

            string bStylePath = Path.Combine(libraryRoot, $"b{productId}");
            if (Directory.Exists(bStylePath)) return bStylePath;

            try
            {
                string parent = Directory.GetParent(libraryRoot)?.FullName;
                if (!string.IsNullOrEmpty(parent) && Directory.Exists(parent))
                {
                    string parentBPath = Path.Combine(parent, $"b{productId}");
                    if (Directory.Exists(parentBPath)) return parentBPath;

                    string parentIdPath = Path.Combine(parent, productId);
                    if (Directory.Exists(parentIdPath)) return parentIdPath;

                    string parentStandard = Path.Combine(parent, shopSubdomain, productId);
                    if (Directory.Exists(parentStandard)) return parentStandard;
                }
            }
            catch { }

            var split = productName.Split(new[] { ' ', '/', '(', ')', '[', ']', '　', '／', '.', '-', '_' }, StringSplitOptions.RemoveEmptyEntries);
            var keywords = new List<string>();
            foreach (var s in split) if (s.Length >= 2) keywords.Add(s);

            try
            {
                var dirs = Directory.GetDirectories(libraryRoot);
                foreach (var dir in dirs)
                {
                    if (IsMatch(dir, productId, keywords, shopSubdomain)) return dir;
                    try
                    {
                        var nested = Directory.GetDirectories(dir);
                        foreach (var n in nested) if (IsMatch(n, productId, keywords, shopSubdomain)) return n;
                    }
                    catch { }
                }
            }
            catch { }

            return null;
        }

        private static bool IsMatch(string path, string id, List<string> keywords, string shop)
        {
            string name = Path.GetFileName(path);
            if (name.Contains(id)) return true; 
            if (!string.IsNullOrEmpty(shop) && name.IndexOf(shop, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            foreach (var kw in keywords) if (name.IndexOf(kw, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        public static List<BoothPackage> FindProductPackages(string productId, string rootPath)
        {
            var packages = new List<BoothPackage>();
            if (!string.IsNullOrEmpty(rootPath) && Directory.Exists(rootPath))
            {
                var files = Directory.GetFiles(rootPath, "*.unitypackage", SearchOption.AllDirectories);
                foreach (var file in files)
                {
                    packages.Add(new BoothPackage { fileName = Path.GetFileName(file), fullPath = file });
                }
            }
            return packages;
        }
    }
}
