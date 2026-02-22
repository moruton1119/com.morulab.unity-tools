using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor.UIElements;
using System.IO;
using UnityEngine.Networking;
using MorulabTools.Launcher;

namespace Moruton.BLMConnector
{
    public class BLMConnectorWindow : EditorWindow
    {
    [MenuItem("Morulab/BLM Connector (Standalone)")]
    [MenuDescription("Booth Library Manager Connector (Standalone). Manage and import assets from your local library.", "Import & Export")]
    [ToolLocalize("en", "BLM Connector (Standalone)", "Manage and import assets from your local library.", "Import & Export")]
    [ToolLocalize("ja", "BLM Connector (単独版)", "ローカルのBOOTHライブラリを管理し、アセットを一括インポートします。", "インポート・エクスポート")]
    [ToolLocalize("ko", "BLM Connector (Standalone)", "로컬 BOOTH 라이브러리를 관리하고 에셋을 일괄 가져오기 합니다.", "가져오기 및 내보내기")]
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
        private VisualElement detailOverlay;
        private VisualElement detailPanel;
        private List<BoothProduct> allProducts = new List<BoothProduct>();
        private BoothProduct selectedProduct;
        private List<string> selectedPackagePaths = new List<string>();
        private HashSet<string> importedProductIds = new HashSet<string>();

        private List<BoothList> availableLists = new List<BoothList>();
        private PopupField<string> filterDropdown;
        private FilterType currentFilterType = FilterType.AllProducts;
        private int currentListId = -1;

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
            detailOverlay = root.Q<VisualElement>("detail-overlay");
            detailPanel = root.Q<VisualElement>("detail-panel");

            // 背景クリックでモーダルを閉じる
            if (detailOverlay != null)
            {
                detailOverlay.RegisterCallback<ClickEvent>(evt =>
                {
                    if (evt.target == detailOverlay)
                    {
                        HideDetail();
                        evt.StopPropagation();
                    }
                });
            }

            BindButton("refresh-db", RefreshData);
            BindButton("close-detail", HideDetail);
            BindButton("add-to-queue", AddSelectedToQueue);
            BindButton("open-folder", () => { if (selectedProduct != null && Directory.Exists(selectedProduct.rootFolderPath)) EditorUtility.RevealInFinder(selectedProduct.rootFolderPath); });
            BindButton("process-queue", () => AssetImportQueue.StartImport());
            BindButton("view-queue", ShowQueueList);
            BindButton("reset-queue", () => { AssetImportQueue.ClearQueue(); UpdateQueueStatus(); });
            BindButton("close-queue-list", () => root.Q<VisualElement>("queue-list-panel")?.AddToClassList("detail-panel-hidden"));
            BindButton("open-local-assets", OpenLocalAssetsFolder);

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

            SetupFilterDropdown();

            root.RegisterCallback<AttachToPanelEvent>(OnAttach);
            root.RegisterCallback<DetachFromPanelEvent>(OnDetach);

            return root;
        }

        private void SetupFilterDropdown()
        {
            var container = root.Q<VisualElement>("filter-dropdown-container");
            if (container == null) return;

            var choices = new List<string> { "All Products", "BLM Products", "Local Products" };
            filterDropdown = new PopupField<string>(choices, 0);
            filterDropdown.style.width = Length.Percent(100);
            filterDropdown.RegisterValueChangedCallback(evt => OnFilterChanged(evt.newValue));
            container.Add(filterDropdown);
        }

        private void OnFilterChanged(string selectedValue)
        {
            if (selectedValue == "All Products")
            {
                currentFilterType = FilterType.AllProducts;
                currentListId = -1;
            }
            else if (selectedValue == "BLM Products")
            {
                currentFilterType = FilterType.BLMProducts;
                currentListId = -1;
            }
            else if (selectedValue == "Local Products")
            {
                currentFilterType = FilterType.LocalProducts;
                currentListId = -1;
            }
            else if (selectedValue.StartsWith("📋 "))
            {
                currentFilterType = FilterType.CustomList;
                var listTitle = selectedValue.Substring(2).Trim();
                var list = availableLists.FirstOrDefault(l => l.title == listTitle);
                if (list != null)
                {
                    currentListId = list.id;
                    UpdateListFilterCache();
                }
            }

            ApplyFilters();
        }

        private void UpdateFilterDropdownChoices()
        {
            if (filterDropdown == null) return;

            var choices = new List<string> { "All Products", "BLM Products", "Local Products" };
            foreach (var list in availableLists)
            {
                choices.Add($"📋 {list.title}");
            }

            var currentValue = filterDropdown.value;
            filterDropdown.choices = choices;

            if (choices.Contains(currentValue))
            {
                filterDropdown.value = currentValue;
            }
            else
            {
                filterDropdown.index = 0;
                currentFilterType = FilterType.AllProducts;
                currentListId = -1;
            }
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
            importedProductIds.Clear();
            BLMHistory.Refresh();

            string dbPath = BLMDatabaseService.GetDefaultDbPath();

            availableLists = BLMDatabaseService.LoadLists(dbPath);
            UpdateFilterDropdownChoices();

            var blmProducts = BLMDatabaseService.LoadProducts(dbPath);

            var localProducts = new List<BoothProduct>();
            if (!string.IsNullOrEmpty(BLMDatabaseService.LibraryRoot))
            {
                EnsureLocalAssetsFolderExists();
                localProducts = LocalAssetService.LoadLocalAssets(BLMDatabaseService.LibraryRoot);
            }

            allProducts = new List<BoothProduct>();
            allProducts.AddRange(blmProducts);
            allProducts.AddRange(localProducts);

            Debug.Log($"[BLM Standalone] Loaded {blmProducts.Count} BLM products + {localProducts.Count} local products = {allProducts.Count} total");

            ApplyFilters();
        }

        private void EnsureLocalAssetsFolderExists()
        {
            if (string.IsNullOrEmpty(BLMDatabaseService.LibraryRoot)) return;

            string localAssetsPath = Path.Combine(BLMDatabaseService.LibraryRoot, "LocalAssets");
            if (!Directory.Exists(localAssetsPath))
            {
                try
                {
                    Directory.CreateDirectory(localAssetsPath);
                    Debug.Log($"[BLM Standalone] Created LocalAssets folder at: {localAssetsPath}");
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[BLM Standalone] Failed to create LocalAssets folder: {ex.Message}");
                }
            }
        }

        private void OpenLocalAssetsFolder()
        {
            if (string.IsNullOrEmpty(BLMDatabaseService.LibraryRoot))
            {
                EditorUtility.DisplayDialog("Error", "BLM Library Root not found. Please ensure BOOTH Library Manager is configured.", "OK");
                return;
            }

            string localAssetsPath = Path.Combine(BLMDatabaseService.LibraryRoot, "LocalAssets");

            // Create folder if it doesn't exist
            if (!Directory.Exists(localAssetsPath))
            {
                try
                {
                    Directory.CreateDirectory(localAssetsPath);
                    Debug.Log($"[BLM Standalone] Created LocalAssets folder at: {localAssetsPath}");
                }
                catch (Exception ex)
                {
                    EditorUtility.DisplayDialog("Error", $"Failed to create LocalAssets folder: {ex.Message}", "OK");
                    return;
                }
            }

            // Open in file explorer
            EditorUtility.RevealInFinder(localAssetsPath);
            Debug.Log($"[BLM Standalone] Opened LocalAssets folder: {localAssetsPath}");
        }

        private void ApplyFilters()
        {
            var filtered = allProducts.Where(p =>
            {
                switch (currentFilterType)
                {
                    case FilterType.AllProducts:
                        return true;
                    case FilterType.BLMProducts:
                        return p.sourceType == "BLM";
                    case FilterType.LocalProducts:
                        return p.sourceType == "Local";
                    case FilterType.CustomList:
                        if (currentListId < 0) return true;
                        if (p.sourceType != "BLM") return false;
                        if (!int.TryParse(p.id, out int boothId)) return false;
                        return listFilterCache.Contains(boothId);
                    default:
                        return true;
                }
            }).ToList();

            Debug.Log($"[BLM Standalone] Filtered: {filtered.Count}/{allProducts.Count} products (Filter: {currentFilterType})");

            RebuildGrid(filtered);
        }

        private HashSet<int> listFilterCache = new HashSet<int>();

        private void UpdateListFilterCache()
        {
            listFilterCache.Clear();
            if (currentFilterType == FilterType.CustomList && currentListId >= 0)
            {
                string dbPath = BLMDatabaseService.GetDefaultDbPath();
                listFilterCache = BLMDatabaseService.LoadListItemBoothIds(dbPath, currentListId);
            }
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
                
                if (importedProductIds.Contains(product.id))
                {
                    item.AddToClassList("batch-imported");
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
                    
                    if (paths.Count >= 2)
                    {
                        bool ok = EditorUtility.DisplayDialog(
                            "Batch Import",
                            $"Importing {paths.Count} packages.\n\nSkip All (Import Dialog) will be automatically enabled.\n\nContinue?",
                            "OK", "Cancel");
                        if (!ok) return;
                        AssetImportQueue.InteractiveMode = false;
                    }
                    
                    importedProductIds.Add(product.id);
                    AssetImportQueue.EnqueueMultiple(paths, product.id);
                    UpdateQueueStatus();
                    ApplyFilters();
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
            if (detailOverlay == null) return;
            selectedProduct = product;
            selectedPackagePaths.Clear();
            detailOverlay.RemoveFromClassList("detail-panel-hidden");

            var nameLbl = detailPanel.Q<Label>("detail-product-name");
            if (nameLbl != null) nameLbl.text = product.name;

            var pathLbl = detailPanel.Q<Label>("detail-path");
            if (pathLbl != null) pathLbl.text = product.rootFolderPath;

            var list = detailPanel.Q<ScrollView>("package-list");
            if (list == null) return;
            list.Clear();

            UpdateDetailFooter(product);

            var unityPackages = product.assets.Where(a => a.assetType == AssetType.UnityPackage).ToList();
            var textures = product.assets.Where(a => a.assetType == AssetType.Texture).ToList();
            var models = product.assets.Where(a => a.assetType == AssetType.Model).ToList();
            var audio = product.assets.Where(a => a.assetType == AssetType.Audio).ToList();
            var others = product.assets.Where(a => a.assetType == AssetType.Other).ToList();

            if (unityPackages.Count > 0)
            {
                AddAssetZone(list, "UnityPackages", unityPackages, product);
            }

            if (textures.Count > 0)
            {
                AddAssetZone(list, "Textures", textures, product);
            }

            if (models.Count > 0)
            {
                AddAssetZone(list, "Models", models, product);
            }

            if (audio.Count > 0)
            {
                AddAssetZone(list, "Audio", audio, product);
            }

            if (others.Count > 0)
            {
                AddAssetZone(list, "Other Files", others, product);
            }

            if (product.assets.Count == 0)
            {
                list.Add(new Label("No assets found.") { style = { color = Color.gray } });
            }
        }

        private void UpdateDetailFooter(BoothProduct product)
        {
            var footer = detailPanel.Q<VisualElement>(className: "modal-footer");
            if (footer == null) return;

            footer.Clear();

            if (BLMHistory.IsInstalled(product))
            {
                var showBtn = new Button(() => ShowInProject(product.id))
                {
                    text = "Show in Project"
                };
                showBtn.style.marginRight = 5;
                footer.Add(showBtn);

                var deleteBtn = new Button(() => DeleteFromProject(product.id))
                {
                    text = "Delete"
                };
                deleteBtn.style.marginRight = 10;
                deleteBtn.style.backgroundColor = new Color(0.6f, 0.2f, 0.2f);
                footer.Add(deleteBtn);
            }

            var spacer = new VisualElement();
            spacer.style.flexGrow = 1;
            footer.Add(spacer);

            var addBtn = new Button(AddSelectedToQueue)
            {
                text = "Add to Queue"
            };
            addBtn.AddToClassList("import-button");
            footer.Add(addBtn);
        }

        private void ShowInProject(string productId)
        {
            string[] guids = AssetDatabase.FindAssets($"l:BLM_PID_{productId}");

            if (guids.Length > 0)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[0]);
                var obj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);

                if (obj != null)
                {
                    Selection.activeObject = obj;
                    EditorGUIUtility.PingObject(obj);
                    Debug.Log($"[BLM] Focused imported folder: {path}");
                    return;
                }
            }

            Debug.LogWarning($"[BLM] No imported folder found for product {productId}");
            if (selectedProduct != null && Directory.Exists(selectedProduct.rootFolderPath))
            {
                EditorUtility.RevealInFinder(selectedProduct.rootFolderPath);
            }
        }

        private void DeleteFromProject(string productId)
        {
            string[] guids = AssetDatabase.FindAssets($"l:BLM_PID_{productId}");

            if (guids.Length == 0)
            {
                EditorUtility.DisplayDialog("Delete", "No imported folder found.", "OK");
                return;
            }

            string path = AssetDatabase.GUIDToAssetPath(guids[0]);
            var obj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
            
            if (obj == null)
            {
                EditorUtility.DisplayDialog("Delete", "Could not load asset.", "OK");
                return;
            }

            var labels = AssetDatabase.GetLabels(obj);
            if (!labels.Any(l => l.StartsWith("BLM_PID_")))
            {
                EditorUtility.DisplayDialog("Delete", "Folder has no BLM_PID label.", "OK");
                return;
            }

            bool confirm = EditorUtility.DisplayDialog(
                "Delete Imported Assets",
                $"Delete the following folder from your project?\n\n{path}",
                "Delete", "Cancel");

            if (!confirm) return;

            AssetDatabase.DeleteAsset(path);
            BLMHistory.Unmark(productId);
            Debug.Log($"[BLM] Deleted: {path}");

            ApplyFilters();
            if (selectedProduct != null)
            {
                UpdateDetailFooter(selectedProduct);
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

            var headerRow = new VisualElement();
            headerRow.style.flexDirection = FlexDirection.Row;
            headerRow.style.justifyContent = Justify.SpaceBetween;
            headerRow.style.marginBottom = 8;

            var zoneHeader = new Label($"─ {zoneName} ({assets.Count}) ─");
            zoneHeader.style.unityFontStyleAndWeight = FontStyle.Bold;
            headerRow.Add(zoneHeader);

            if (assets.Count > 1)
            {
                var selectAllBtn = new Button(() =>
                {
                    foreach (var asset in assets)
                    {
                        if (!selectedPackagePaths.Contains(asset.fullPath))
                            selectedPackagePaths.Add(asset.fullPath);
                    }
                    RefreshDetailPanel();
                }) { text = "Select All" };
                selectAllBtn.style.width = 70;
                headerRow.Add(selectAllBtn);

                var deselectAllBtn = new Button(() =>
                {
                    foreach (var asset in assets)
                        selectedPackagePaths.Remove(asset.fullPath);
                    RefreshDetailPanel();
                }) { text = "Deselect" };
                deselectAllBtn.style.width = 70;
                headerRow.Add(deselectAllBtn);
            }

            zone.Add(headerRow);

            foreach (var asset in assets)
            {
                var assetRow = new VisualElement();
                assetRow.style.flexDirection = FlexDirection.Row;
                assetRow.style.justifyContent = Justify.SpaceBetween;
                assetRow.style.marginBottom = 5;
                assetRow.style.paddingLeft = 10;

                var toggle = new Toggle { text = asset.fileName, value = selectedPackagePaths.Contains(asset.fullPath) };
                toggle.style.flexGrow = 1;
                toggle.RegisterValueChangedCallback(evt =>
                {
                    if (evt.newValue)
                    {
                        if (!selectedPackagePaths.Contains(asset.fullPath))
                            selectedPackagePaths.Add(asset.fullPath);
                    }
                    else
                    {
                        selectedPackagePaths.Remove(asset.fullPath);
                    }
                });

                var importBtn = new Button(() => ImportAsset(asset, product)) { text = "Import" };
                importBtn.style.width = 80;

                assetRow.Add(toggle);
                assetRow.Add(importBtn);
                zone.Add(assetRow);
            }

            parent.Add(zone);
        }

        private void RefreshDetailPanel()
        {
            if (selectedProduct != null)
            {
                ShowDetail(selectedProduct);
            }
        }

        private void ImportAsset(BoothAsset asset, BoothProduct product)
        {
            AssetImportQueue.StartManualImport(product.id);
            try
            {
                BLMAssetImporter.ImportAsset(asset, product.name);
                importedProductIds.Add(product.id);
                Debug.Log($"[BLM] Successfully imported {asset.fileName}");
                ApplyFilters();
                UpdateDetailFooter(product);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[BLM] Failed to import {asset.fileName}: {ex.Message}");
            }
            finally
            {
                AssetImportQueue.EndManualImport();
            }
        }

        private void ImportAllAssets(List<BoothAsset> assets, BoothProduct product)
        {
            AssetImportQueue.StartManualImport(product.id);
            try
            {
                foreach (var asset in assets)
                {
                    BLMAssetImporter.ImportAsset(asset, product.name);
                }
                importedProductIds.Add(product.id);
                Debug.Log($"[BLM] Successfully imported {assets.Count} assets");
                ApplyFilters();
                UpdateDetailFooter(product);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[BLM] Failed to import assets: {ex.Message}");
            }
            finally
            {
                AssetImportQueue.EndManualImport();
            }
        }

        private void HideDetail() => detailOverlay?.AddToClassList("detail-panel-hidden");

        private void AddSelectedToQueue()
        {
            if (selectedPackagePaths.Count == 0) return;
            AssetImportQueue.EnqueueMultiple(selectedPackagePaths, selectedProduct?.id);
            selectedPackagePaths.Clear();
            HideDetail();
            UpdateQueueStatus();
            ShowQueueList();
        }

        private T LoadAsset<T>(string fileName) where T : UnityEngine.Object
        {
            string[] paths = {
                $"Packages/com.morulab.unity-tools/Editor/BLMConnector/{fileName}"
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
