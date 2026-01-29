using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

#if BLM_LOCAL_CONNECTOR_HAS_SQLITE
using Mono.Data.Sqlite;
using System.Data;
#endif

namespace Moruton.BLMConnector
{
    public class BLMDatabaseViewer : EditorWindow
    {
        private string _dbPath;
        private string _query = "SELECT * FROM preferences";
        private string _output = "";
        private Vector2 _scrollPos;

        [MenuItem("Window/BLM Connector/Database Viewer")]
        public static void ShowWindow()
        {
            GetWindow<BLMDatabaseViewer>("BLM Database Viewer");
        }

        private void OnEnable()
        {
            // デフォルトのDBパスを設定
            _dbPath = BLMDatabaseService.GetDefaultDbPath();
            
            // ユーザー指定があれば優先（環境によっては調整が必要）
            // string customPath = EditorPrefs.GetString("BLM_CustomDBPath", "");
            // if(!string.IsNullOrEmpty(customPath)) _dbPath = customPath;
        }

        private void OnGUI()
        {
            GUILayout.Label("BLM Database Viewer", EditorStyles.boldLabel);

            GUILayout.Space(10);
            GUILayout.Label("Database Path:", EditorStyles.label);
            EditorGUILayout.BeginHorizontal();
            _dbPath = EditorGUILayout.TextField(_dbPath);
            if (GUILayout.Button("Browse", GUILayout.Width(60)))
            {
                string selected = EditorUtility.OpenFilePanel("Select SQLite Database", Path.GetDirectoryName(_dbPath), "db");
                if (!string.IsNullOrEmpty(selected)) _dbPath = selected;
            }
            EditorGUILayout.EndHorizontal();

#if BLM_LOCAL_CONNECTOR_HAS_SQLITE
            GUILayout.Space(10);
            GUILayout.Label("SQL Query:", EditorStyles.label);
            _query = EditorGUILayout.TextArea(_query, GUILayout.Height(50));

            if (GUILayout.Button("Execute Query", GUILayout.Height(30)))
            {
                ExecuteQuery();
            }

            GUILayout.Space(10);
            GUILayout.Label("Output:", EditorStyles.label);
            
            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);
            EditorGUILayout.TextArea(_output, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();
#else
            GUILayout.Space(20);
            EditorGUILayout.HelpBox("SQLite support (Mono.Data.Sqlite) is not enabled or missing.\nPlease ensure BLM_LOCAL_CONNECTOR_HAS_SQLITE is defined and the DLL is present.", MessageType.Error);
#endif
        }

#if BLM_LOCAL_CONNECTOR_HAS_SQLITE
        private void ExecuteQuery()
        {
            if (!File.Exists(_dbPath))
            {
                _output = $"Error: Database file not found at\n{_dbPath}";
                return;
            }

            try
            {
                string connectionString = $"URI=file:{_dbPath};ReadOnly=True";
                using (var connection = new SqliteConnection(connectionString))
                {
                    connection.Open();
                    using (var cmd = connection.CreateCommand())
                    {
                        cmd.CommandText = _query;
                        using (var reader = cmd.ExecuteReader())
                        {
                            var sb = new System.Text.StringBuilder();
                            
                            // カラム名
                            for (int i = 0; i < reader.FieldCount; i++)
                            {
                                sb.Append(reader.GetName(i)).Append(" | ");
                            }
                            sb.AppendLine();
                            sb.AppendLine(new string('-', 50));

                            // データ
                            int rowCount = 0;
                            while (reader.Read())
                            {
                                rowCount++;
                                for (int i = 0; i < reader.FieldCount; i++)
                                {
                                    sb.Append(reader[i].ToString()).Append(" | ");
                                }
                                sb.AppendLine();
                            }
                            
                            if (rowCount == 0) sb.AppendLine("(No rows returned)");
                            
                            _output = sb.ToString();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _output = $"SQL Error: {ex.Message}";
            }
        }
#endif
    }
}
