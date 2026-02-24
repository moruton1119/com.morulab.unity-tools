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
            return Path.Combine(appData, BLMConstants.BLMAppDataFolder, "data.db");
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
                                    thumbnailUrl = reader["thumbnail_url"] != DBNull.Value ? reader["thumbnail_url"].ToString() : "",
                                    sourceType = "BLM"
                                };
                                
                                // BLMのフォルダ構造: {libraryRoot}\b{id}
                                string productPath = Path.Combine(currentLibraryRoot, $"b{p.id}");

                                if (!Directory.Exists(productPath))
                                {
                                    string foundPath = FindFuzzyPath(currentLibraryRoot, p.id, p.name, p.shopSubdomain);
                                    if (!string.IsNullOrEmpty(foundPath)) productPath = foundPath;
                                }

                                p.rootFolderPath = productPath;

                                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                                string cacheDir = Path.Combine(appData, BLMConstants.BLMAppDataFolder, BLMConstants.BLMCacheThumbnailsFolder);
                                string png = Path.Combine(cacheDir, $"{p.id}.png");
                                string jpg = Path.Combine(cacheDir, $"{p.id}.jpg");
                                if (File.Exists(png)) p.thumbnailPath = png;
                                else if (File.Exists(jpg)) p.thumbnailPath = jpg;

                                // アセットを読み込む
                                p.assets = FindProductAssets(p.id, productPath);

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
                    // preferences テーブルは key-value 形式ではなく、カラムとして item_directory_path を持つ
                    cmd.CommandText = "SELECT item_directory_path FROM preferences LIMIT 1";
                    var result = cmd.ExecuteScalar();
                    
                    if (result != null)
                    {
                        string path = null;
                        
                        // BLOB型の場合はbyte[]としてデコード
                        if (result is byte[] bytes)
                        {
                            Debug.Log($"[BLM Debug] BLOB byte length: {bytes.Length}");
                            Debug.Log($"[BLM Debug] BLOB hex: {BitConverter.ToString(bytes)}");
                            
                            // UTF-16 LE (Little Endian) でデコード - BLMはこの形式を使用
                            path = System.Text.Encoding.Unicode.GetString(bytes);
                            Debug.Log($"[BLM Debug] UTF-16 decoded: {path}");
                            
                            // もしUTF-16で正しく取得できない場合、UTF-8を試す
                            if (string.IsNullOrEmpty(path) || path.Length < 3)
                            {
                                path = System.Text.Encoding.UTF8.GetString(bytes);
                                Debug.Log($"[BLM Debug] UTF-8 decoded: {path}");
                            }
                        }
                        else
                        {
                            path = result.ToString();
                            Debug.Log($"[BLM Debug] Found item_directory_path (TEXT): {path}");
                        }
                        
                        return path;
                    }
                    else
                    {
                        Debug.LogWarning("[BLM Debug] item_directory_path column is NULL or empty.");
                    }
                    
                    return null;
                }
            }
            catch (Exception ex)
            { 
                Debug.LogError($"[BLM Debug] Error reading preferences: {ex.Message}");
                return null; 
            }
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

        public static List<BoothAsset> FindProductAssets(string productId, string rootPath)
        {
            var assets = new List<BoothAsset>();
            if (!string.IsNullOrEmpty(rootPath) && Directory.Exists(rootPath))
            {
                foreach (var pattern in AssetTypeUtils.GetSearchPatterns())
                {
                    var files = Directory.GetFiles(rootPath, pattern, SearchOption.AllDirectories);
                    foreach (var file in files)
                    {
                        assets.Add(new BoothAsset
                        {
                            fileName = Path.GetFileName(file),
                            fullPath = file,
                            assetType = AssetTypeUtils.GetAssetType(Path.GetExtension(file))
                        });
                    }
                }
            }
            return assets;
        }

        public static List<BoothList> LoadLists(string dbPath)
        {
            var lists = new List<BoothList>();
            if (!File.Exists(dbPath))
            {
                Debug.LogError($"[BLM Standalone] Database file not found at: {dbPath}");
                return lists;
            }

#if BLM_LOCAL_CONNECTOR_HAS_SQLITE
            try
            {
                string connectionString = $"URI=file:{dbPath};ReadOnly=True";
                using (var connection = new SqliteConnection(connectionString))
                {
                    connection.Open();
                    using (var cmd = connection.CreateCommand())
                    {
                        cmd.CommandText = "SELECT id, title, description, created_at, updated_at FROM lists ORDER BY title";
                        using (var reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                lists.Add(new BoothList
                                {
                                    id = Convert.ToInt32(reader["id"]),
                                    title = reader["title"].ToString(),
                                    description = reader["description"] != DBNull.Value ? reader["description"].ToString() : "",
                                    createdAt = reader["created_at"].ToString(),
                                    updatedAt = reader["updated_at"].ToString()
                                });
                            }
                        }
                    }
                }
                Debug.Log($"[BLM Standalone] Loaded {lists.Count} lists.");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[BLM Standalone] Error loading lists: {ex.Message}");
            }
#endif
            return lists;
        }

        public static HashSet<int> LoadListItemBoothIds(string dbPath, int listId)
        {
            var boothIds = new HashSet<int>();
            if (!File.Exists(dbPath)) return boothIds;

#if BLM_LOCAL_CONNECTOR_HAS_SQLITE
            try
            {
                string connectionString = $"URI=file:{dbPath};ReadOnly=True";
                using (var connection = new SqliteConnection(connectionString))
                {
                    connection.Open();
                    using (var cmd = connection.CreateCommand())
                    {
                        cmd.CommandText = @"
                            SELECT ri.booth_item_id 
                            FROM list_items li
                            INNER JOIN registered_items ri ON li.item_id = ri.id
                            WHERE li.list_id = @listId";
                        
                        var param = cmd.CreateParameter();
                        param.ParameterName = "@listId";
                        param.Value = listId;
                        cmd.Parameters.Add(param);

                        using (var reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                boothIds.Add(Convert.ToInt32(reader["booth_item_id"]));
                            }
                        }
                    }
                }
                Debug.Log($"[BLM Standalone] Loaded {boothIds.Count} items for list {listId}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[BLM Standalone] Error loading list items: {ex.Message}");
            }
#endif
            return boothIds;
        }
    }
}
