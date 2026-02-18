using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Moruton.BLMConnector
{
    [InitializeOnLoad]
    public static class BLMEnvironmentManager
    {
        private const string DEFINE_SYMBOL = "BLM_LOCAL_CONNECTOR_HAS_SQLITE";
        private const string DLL_SQLITE_NATIVE = "sqlite3.dll";
        private const string DLL_SQLITE_MANAGED = "Mono.Data.Sqlite.dll";
        
        // Path in our package
        private static string MyPluginPath => "Packages/com.morulab.unity-tools/Editor/BLMConnector/Plugins";

        static BLMEnvironmentManager()
        {
            EditorApplication.delayCall += Initialize;
        }

        private static void Initialize()
        {
            var nativeResult = FindDlls(DLL_SQLITE_NATIVE);
            var managedResult = FindDlls(DLL_SQLITE_MANAGED);

            bool hasConflict = nativeResult.hasConflict || managedResult.hasConflict;
            
            if (hasConflict)
            {
                bool userAgreed = EditorUtility.DisplayDialog(
                    "BLM Connector Standalone - Dependency Conflict",
                    "Duplicate SQLite libraries were detected in your project.\n\n" +
                    "To prevent errors, BLM Connector Standalone needs to remove its internal copies and use the existing ones.\n\n" +
                    "Proceed with cleanup?",
                    "Fix Conflict (Delete Standalone Copies)", "Cancel");

                if (userAgreed)
                {
                    ResolveConflict(nativeResult);
                    ResolveConflict(managedResult);
                    AssetDatabase.Refresh();
                    return;
                }
                else
                {
                    Debug.LogWarning("[BLM Standalone] Dependency conflict not resolved. Tool disabled.");
                    SetDefineSymbol(false);
                    return;
                }
            }

            bool nativeExists = nativeResult.paths.Count > 0;
            bool managedExists = managedResult.paths.Count > 0;

            if (nativeExists && managedExists)
            {
                SetDefineSymbol(true);
            }
            else
            {
                SetDefineSymbol(false);
            }
        }

        private struct DllSearchResult
        {
            public System.Collections.Generic.List<string> paths;
            public bool hasConflict; 
            public bool hasMyCopy;
        }

        private static DllSearchResult FindDlls(string filename)
        {
            var paths = new System.Collections.Generic.List<string>();
            string nameNoExt = Path.GetFileNameWithoutExtension(filename);
            string[] guids = AssetDatabase.FindAssets(nameNoExt);
            
            bool hasMyCopy = false;
            bool hasOtherCopy = false;

            foreach (var guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.EndsWith(filename, StringComparison.OrdinalIgnoreCase)) continue;
                
                paths.Add(path);
                
                if (path.StartsWith(MyPluginPath)) hasMyCopy = true;
                else hasOtherCopy = true;
            }

            return new DllSearchResult 
            { 
                paths = paths, 
                hasMyCopy = hasMyCopy, 
                hasConflict = (hasMyCopy && hasOtherCopy) 
            };
        }

        private static void ResolveConflict(DllSearchResult result)
        {
            if (!result.hasConflict) return;

            foreach (var path in result.paths)
            {
                if (path.StartsWith(MyPluginPath))
                {
                    AssetDatabase.DeleteAsset(path);
                    Debug.Log($"[BLM Standalone] Removed redundant dependency: {path}");
                }
            }
        }

        private static void SetDefineSymbol(bool enable)
        {
            string definesString = PlayerSettings.GetScriptingDefineSymbolsForGroup(EditorUserBuildSettings.selectedBuildTargetGroup);
            var defines = new System.Collections.Generic.List<string>(definesString.Split(';'));
            
            bool changed = false;
            if (enable)
            {
                if (!defines.Contains(DEFINE_SYMBOL))
                {
                    defines.Add(DEFINE_SYMBOL);
                    changed = true;
                    Debug.Log($"[BLM Standalone] Dependencies validated. Enabled {DEFINE_SYMBOL}.");
                }
            }
            else
            {
                if (defines.Contains(DEFINE_SYMBOL))
                {
                    defines.Remove(DEFINE_SYMBOL);
                    changed = true;
                    Debug.Log($"[BLM Standalone] Dependencies invalid. Disabled {DEFINE_SYMBOL}.");
                }
            }

            if (changed)
            {
                PlayerSettings.SetScriptingDefineSymbolsForGroup(EditorUserBuildSettings.selectedBuildTargetGroup, string.Join(";", defines));
            }
        }
    }
}
