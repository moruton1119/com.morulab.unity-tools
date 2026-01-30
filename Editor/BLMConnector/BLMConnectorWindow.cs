using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using System.IO;
using UnityEngine.Networking;
// using MorulabTools.Launcher;

namespace Moruton.BLMConnector
{
    public class BLMConnectorWindow : EditorWindow
    {
        [MenuItem("Morulab/BLM Connector (Standalone)")]
        // [MorulabTools.Launcher.MenuDescription("Booth Library Manager Connector (Standalone). Manage and import assets from your local library.", "Import & Export")]
        // [MorulabTools.Launcher.ToolLocalize("en", "BLM Connector (Standalone)", "Manage and import assets from your local library.", "Import & Export")]
        // [MorulabTools.Launcher.ToolLocalize("ja", "BLM Connector (単独版)", "ローカルのBOOTHライブラリを管理し、アセットを一括インポートします。", "インポート・エクスポート")]
        // [MorulabTools.Launcher.ToolLocalize("ko", "BLM Connector (Standalone)", "로컬 BOOTH 라이브러리를 관리하고 에셋을 일괄 가져오기 합니다.", "가져오기 및 내보내기")]
        public static void ShowWindow()
        {
            var window = GetWindow<BLMConnectorWindow>();
            window.titleContent = new GUIContent("BLM Connector (Std)");
            window.minSize = new Vector2(800, 500);
        }

        private BLMConnectorApp _app;

        public void OnEnable() { }

        public void OnDisable()
        {
            _app?.Dispose();
        }

        public void CreateGUI()
        {
            _app = new BLMConnectorApp();
            var ui = _app.CreateUI();
            rootVisualElement.Add(ui);
        }

        public static VisualElement CreateEmbeddedView()
        {
            var app = new BLMConnectorApp();
            return app.CreateUI();
        }
    }

    public class BLMConnectorApp
    {
        private VisualElement root;
        private VisualElement gridContainer;
        private VisualElement detailPanel;
        private List<BoothProduct> allProducts = new List<BoothProduct>();
        private BoothProduct selectedProduct;
        private List<string> selectedPackagePaths = new List<string>();

        public VisualElement CreateUI()
        {
            Debug.Log("[BLM Standalone] App CreateUI Started");

            var uxml = LoadAsset<VisualTreeAsset>("BLMConnectorWindow.uxml");
            var uss = LoadAsset<StyleSheet>("BLMConnectorWindow.uss");

            if (uxml == null) { return new Label("Error: BLMConnectorWindow.uxml not found."); }

            root = uxml.CloneTree();
            if (uss != null) root.styleSheets.Add(uss);

            root.style.flexGrow = 1;
            root.style.height = Length.Percent(100);

            gridContainer = root.Q<VisualElement>("grid-container");
            detailPanel = root.Q<VisualElement>("detail-panel");

            BindButton("refresh-db", RefreshData);
            BindButton("close-detail", HideDetail);
            BindButton("add-to-queue", AddSelectedToQueue);
            BindButton("open-folder", () => { if (selectedProduct != null && Directory.Exists(selectedProduct.rootFolderPath)) EditorUtility.RevealInFinder(selectedProduct.rootFolderPath); });
            BindButton("process-queue", () => AssetImportQueue.StartImport());
            BindButton("view-queue", ShowQueueList);
            BindButton("reset-queue", () => { AssetImportQueue.ClearQueue(); UpdateQueueStatus(); });
            BindButton("close-queue-list", () => root.Q<VisualElement>("queue-list-panel")?.AddToClassList("detail-panel-hidden"));

            var hamburger = root.Q<Button>("hamburger-menu");
            var sidebar = root.Q<VisualElement>("sidebar");
            if (hamburger != null && sidebar != null)
            {
                hamburger.clicked += () => sidebar.ToggleInClassList("sidebar-hidden");
            }

            var toggle = root.Q<Toggle>("interactive-toggle");
            if (toggle != null)
            {
                toggle.value = AssetImportQueue.InteractiveMode;
                toggle.RegisterValueChangedCallback(evt => AssetImportQueue.InteractiveMode = evt.newValue);
            }

            root.RegisterCallback<AttachToPanelEvent>(OnAttach);
            root.RegisterCallback<DetachFromPanelEvent>(OnDetach);

            return root;
        }

        private void BindButton(string name, Action action)
        {
            var btn = root?.Q<Button>(name);
            if (btn != null) btn.clicked += action;
        }

        private void OnAttach(AttachToPanelEvent evt)
        {
            AssetImportQueue.OnImportFinishedAction += OnImportItemFinished;
            RefreshData();
            root.schedule.Execute(UpdateQueueStatus).Every(500);
        }

        private void OnDetach(DetachFromPanelEvent evt)
        {
            AssetImportQueue.OnImportFinishedAction -= OnImportItemFinished;
        }

        public void Dispose()
        {
            AssetImportQueue.OnImportFinishedAction -= OnImportItemFinished;
        }

        private void OnImportItemFinished()
        {
            RefreshData();
            UpdateQueueStatus();
        }

        private void ShowQueueList()
        {
            var panel = root.Q<VisualElement>("queue-list-panel");
            var scroll = root.Q<ScrollView>("queue-list-scroll");
            if (panel == null || scroll == null) return;

            scroll.Clear();
            panel.RemoveFromClassList("detail-panel-hidden");

            var items = AssetImportQueue.GetQueueItems();
            if (items.Length == 0)
            {
                scroll.Add(new Label("Queue is empty.") { style = { color = Color.gray } });
            }
            else
            {
                int index = 1;
                foreach (var item in items)
                {
                    scroll.Add(new Label($"{index++}. {Path.GetFileName(item)}") { style = { fontSize = 11 } });
                }
            }
        }

        private void RefreshData()
        {
            BLMHistory.Refresh();
            string dbPath = BLMDatabaseService.GetDefaultDbPath();
            allProducts = BLMDatabaseService.LoadProducts(dbPath);
            RebuildGrid(allProducts);
        }

        private void RebuildGrid(List<BoothProduct> products)
        {
            if (gridContainer == null) return;
            gridContainer.Clear();

            foreach (var product in products)
            {
                var item = new VisualElement();
                item.AddToClassList("grid-item");
                item.RegisterCallback<MouseDownEvent>(evt => OnProductClick(evt, product));

                var thumb = new Image();
                thumb.AddToClassList("thumbnail");
                LoadThumbnail(thumb, product);

                var tc = new VisualElement();
                tc.AddToClassList("thumbnail-container");
                tc.Add(thumb);
                item.Add(tc);

                var info = new VisualElement();
                info.AddToClassList("item-info");

                var nameLabel = new Label(product.name);
                nameLabel.AddToClassList("item-name");
                nameLabel.tooltip = product.name;

                var shopLabel = new Label(product.shopName);
                shopLabel.AddToClassList("item-shop");

                info.Add(nameLabel);
                info.Add(shopLabel);
                item.Add(info);

                if (BLMHistory.IsInstalled(product))
                {
                    item.AddToClassList("installed");
                }

                gridContainer.Add(item);
            }
        }

        private void OnProductClick(MouseDownEvent evt, BoothProduct product)
        {
            if (evt.clickCount == 1)
            {
                ShowDetail(product);
            }
            else if (evt.clickCount == 2)
            {
                if (product.packages == null || product.packages.Count == 0 || product.rootFolderPath == null)
                {
                    string path = product.rootFolderPath ?? BLMDatabaseService.FindFuzzyPath(BLMDatabaseService.LibraryRoot, product.id, product.name, product.shopSubdomain);
                    if (!string.IsNullOrEmpty(path))
                    {
                        product.rootFolderPath = path;
                        product.packages = BLMDatabaseService.FindProductPackages(product.id, path);
                    }
                }

                if (product.packages != null && product.packages.Count > 0)
                {
                    var paths = product.packages.Select(p => p.fullPath).ToList();
                    AssetImportQueue.EnqueueMultiple(paths, product.id);
                    UpdateQueueStatus();
                    RefreshData();
                }
            }
        }

        private void LoadThumbnail(Image img, BoothProduct product)
        {
            if (!string.IsNullOrEmpty(product.thumbnailPath) && File.Exists(product.thumbnailPath))
            {
                try
                {
                    var tex = new Texture2D(2, 2);
                    tex.LoadImage(File.ReadAllBytes(product.thumbnailPath));
                    img.image = tex;
                    return;
                }
                catch { }
            }

            string cacheDir = "Library/Moruton.BLMConnector/Thumbnails";
            if (!Directory.Exists(cacheDir)) Directory.CreateDirectory(cacheDir);

            string cachePath = $"{cacheDir}/{product.id}.png";
            if (File.Exists(cachePath))
            {
                img.image = AssetDatabase.LoadAssetAtPath<Texture2D>(cachePath);
                if (img.image != null) return;
            }

            if (!string.IsNullOrEmpty(product.thumbnailUrl))
            {
                DownloadThumbnail(img, product.thumbnailUrl, cachePath);
            }
        }

        private void DownloadThumbnail(Image img, string url, string savePath)
        {
            var request = UnityWebRequestTexture.GetTexture(url);
            var op = request.SendWebRequest();
            op.completed += _ =>
            {
                if (request == null) return;
                try
                {
                    if (img != null && request.result == UnityWebRequest.Result.Success)
                    {
                        var tex = DownloadHandlerTexture.GetContent(request);
                        if (tex != null)
                        {
                            img.image = tex;
                            try { File.WriteAllBytes(savePath, tex.EncodeToPNG()); } catch { }
                        }
                    }
                }
                finally { request.Dispose(); }
            };
        }

        private void ShowDetail(BoothProduct product)
        {
            if (detailPanel == null) return;
            selectedProduct = product;
            selectedPackagePaths.Clear();
            detailPanel.Clear();
            detailPanel.RemoveFromClassList("detail-panel-hidden");

            // ヘッダー
            var header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            header.style.justifyContent = Justify.SpaceBetween;
            header.style.marginBottom = 10;

            var titleLabel = new Label(product.name);
            titleLabel.style.fontSize = 16;
            titleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;

            var closeBtn = new Button(() => detailPanel.AddToClassList("detail-panel-hidden")) { text = "×" };
            closeBtn.style.width = 30;
            closeBtn.style.height = 30;

            header.Add(titleLabel);
            header.Add(closeBtn);
            detailPanel.Add(header);

            var shopLabel = new Label($"Shop: {product.shopName}");
            shopLabel.style.color = new Color(0.7f, 0.7f, 0.7f);
            shopLabel.style.marginBottom = 15;
            detailPanel.Add(shopLabel);

            // アセットをタイプごとにグループ化
            var unityPackages = product.assets.Where(a => a.assetType == AssetType.UnityPackage).ToList();
            var textures = product.assets.Where(a => a.assetType == AssetType.Texture).ToList();
            var models = product.assets.Where(a => a.assetType == AssetType.Model).ToList();
            var audio = product.assets.Where(a => a.assetType == AssetType.Audio).ToList();
            var others = product.assets.Where(a => a.assetType == AssetType.Other).ToList();

            // UnityPackage ゾーン
            if (unityPackages.Count > 0)
            {
                AddAssetZone(detailPanel, "UnityPackages", unityPackages, product);
            }

            // Textures ゾーン
            if (textures.Count > 0)
            {
                AddAssetZone(detailPanel, "Textures", textures, product);
            }

            // Models ゾーン
            if (models.Count > 0)
            {
                AddAssetZone(detailPanel, "Models", models, product);
            }

            // Audio ゾーン
            if (audio.Count > 0)
            {
                AddAssetZone(detailPanel, "Audio", audio, product);
            }

            // Others ゾーン
            if (others.Count > 0)
            {
                AddAssetZone(detailPanel, "Other Files", others, product);
            }
        }

        private void AddAssetZone(VisualElement parent, string zoneName, List<BoothAsset> assets, BoothProduct product)
        {
            var zone = new VisualElement();
            zone.style.marginBottom = 15;
            zone.style.paddingBottom = 10;
            zone.style.paddingTop = 10;
            zone.style.paddingLeft = 10;
            zone.style.paddingRight = 10;
            zone.style.backgroundColor = new Color(0.2f, 0.2f, 0.2f, 0.3f);
            zone.style.borderBottomLeftRadius = 5;
            zone.style.borderBottomRightRadius = 5;
            zone.style.borderTopLeftRadius = 5;
            zone.style.borderTopRightRadius = 5;

            var zoneHeader = new Label($"─ {zoneName} ({assets.Count}) ─");
            zoneHeader.style.unityFontStyleAndWeight = FontStyle.Bold;
            zoneHeader.style.marginBottom = 8;
            zone.Add(zoneHeader);

            foreach (var asset in assets)
            {
                var assetRow = new VisualElement();
                assetRow.style.flexDirection = FlexDirection.Row;
                assetRow.style.justifyContent = Justify.SpaceBetween;
                assetRow.style.marginBottom = 5;
                assetRow.style.paddingLeft = 10;

                var assetLabel = new Label($"○ {asset.fileName}");
                assetLabel.style.flexGrow = 1;

                var importBtn = new Button(() => ImportAsset(asset, product)) { text = "Import" };
                importBtn.style.width = 80;

                assetRow.Add(assetLabel);
                assetRow.Add(importBtn);
                zone.Add(assetRow);
            }

            // Import All ボタン
            if (assets.Count > 1)
            {
                var importAllBtn = new Button(() => ImportAllAssets(assets, product)) { text = $"Import All {zoneName}" };
                importAllBtn.style.marginTop = 8;
                zone.Add(importAllBtn);
            }

            parent.Add(zone);
        }

        private void ImportAsset(BoothAsset asset, BoothProduct product)
        {
            try
            {
                BLMAssetImporter.ImportAsset(asset, product.name);
                Debug.Log($"[BLM] Successfully imported {asset.fileName}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[BLM] Failed to import {asset.fileName}: {ex.Message}");
            }
        }

        private void ImportAllAssets(List<BoothAsset> assets, BoothProduct product)
        {
            foreach (var asset in assets)
            {
                ImportAsset(asset, product);
            }
        }

        private void HideDetail() => detailPanel?.AddToClassList("detail-panel-hidden");

        private void AddSelectedToQueue()
        {
            if (selectedPackagePaths.Count == 0) return;
            AssetImportQueue.EnqueueMultiple(selectedPackagePaths, selectedProduct?.id);
            selectedPackagePaths.Clear();
            HideDetail();
            UpdateQueueStatus();
        }

        private T LoadAsset<T>(string fileName) where T : UnityEngine.Object
        {
            string[] paths = {
                $"Packages/com.moruton.blm-local-connector/Editor/BLMConnector/{fileName}"
            };
            foreach (var path in paths)
            {
                var asset = AssetDatabase.LoadAssetAtPath<T>(path);
                if (asset != null) return asset;
            }
            return null;
        }

        private void UpdateQueueStatus()
        {
            var statusLabel = root?.Q<Label>("queue-status");
            var processBtn = root?.Q<Button>("process-queue");
            if (statusLabel != null) statusLabel.text = AssetImportQueue.IsImporting ? "Importing..." : $"{AssetImportQueue.RemainingCount} items in queue";
            if (processBtn != null) processBtn.text = $"Process Queue ({AssetImportQueue.RemainingCount})";
        }
    }
}
