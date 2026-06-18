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
    [MenuItem("Morulab/BLM Connector")]
    [MenuDescription("Booth Library Manager Connector. Manage and import assets from your local library.", "Import & Export")]
    [ToolLocalize("en", "BLM Connector", "Manage and import assets from your local library.", "Import & Export")]
    [ToolLocalize("ja", "BLM Connector", "ローカルのBOOTHライブラリを管理し、アセットを一括インポートします。", "インポート・エクスポート")]
    [ToolLocalize("ko", "BLM Connector", "로컬 BOOTH 라이브러리를 관리하고 에셋을 일괄 가져오기 합니다.", "가져오기 및 내보내기")]
    public static void ShowWindow()
        {
            var window = GetWindow<BLMConnectorWindow>();
            window.titleContent = new GUIContent("BLM Connector");
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
        private List<BoothProduct> filteredProducts = new List<BoothProduct>();
        private BoothProduct selectedProduct;
        private List<string> selectedPackagePaths = new List<string>();
        private HashSet<string> importedProductIds = new HashSet<string>();

        private List<BoothList> availableLists = new List<BoothList>();
        private FilterType currentFilterType = FilterType.AllProducts;
        private int currentListId = -1;
        private bool isListView = false;
        private string searchText = "";

        private DropdownField sortDropdown;
        private enum SortMode { NameAsc, NameDesc, ShopAsc }
        private SortMode currentSort = SortMode.NameAsc;

        public VisualElement CreateUI()
        {

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
            BindButton("reset-queue", () => {
                AssetImportQueue.ClearQueue();
                UpdateQueueStatus();
                RefreshQueueListDisplay();
            });
            BindButton("open-local-assets", OpenLocalAssetsFolder);

            // Header local assets button (with tooltip)
            var localBtn = root.Q<Button>("open-local-assets-header");
            if (localBtn != null)
            {
                localBtn.tooltip = "\u30ed\u30fc\u30ab\u30eb\u30a2\u30bb\u30c3\u30c8\u306f\u3053\u3061\u3089\u306b\u683c\u7d0d\u3057\u3066\u304f\u3060\u3055\u3044";
                localBtn.clicked += OpenLocalAssetsFolder;
            }

            // Grid / List view toggle
            var gridBtn = root.Q<Button>("grid-view-btn");
            var listBtn = root.Q<Button>("list-view-btn");
            if (gridBtn != null) gridBtn.clicked += () => SetViewMode(false);
            if (listBtn != null) listBtn.clicked += () => SetViewMode(true);

            // Search field
            var searchField = root.Q<TextField>("search-field");
            if (searchField != null)
            {
                searchField.RegisterValueChangedCallback(evt =>
                {
                    searchText = evt.newValue?.ToLower() ?? "";
                    ApplyFilters();
                });
            }

            // Sort dropdown
            sortDropdown = root.Q<DropdownField>("sort-dropdown");
            if (sortDropdown != null)
            {
                sortDropdown.choices = new List<string> { "Name (A-Z)", "Name (Z-X)", "Shop Name" };
                sortDropdown.value = "Name (A-Z)";
                sortDropdown.RegisterValueChangedCallback(evt =>
                {
                    currentSort = evt.newValue switch
                    {
                        "Name (Z-X)" => SortMode.NameDesc,
                        "Shop Name" => SortMode.ShopAsc,
                        _ => SortMode.NameAsc
                    };
                    ApplyFilters();
                });
            }

            // Setup filter chips
            SetupFilterChips();

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

        private void SetupFilterChips()
        {
            var container = root.Q<VisualElement>("filter-chips");
            if (container == null) return;

            container.Clear();

            var chips = new[] { "All", "BLM", "Local", "Booth" };
            foreach (var chip in chips)
            {
                var btn = new Button { text = chip };
                btn.AddToClassList("blm-chip");
                if (chip == "All") btn.AddToClassList("blm-chip-active");

                var chipValue = chip;
                btn.clicked += () =>
                {
                    container.Query<Button>().ForEach(b => b.RemoveFromClassList("blm-chip-active"));
                    btn.AddToClassList("blm-chip-active");

                    currentFilterType = chipValue switch
                    {
                        "BLM" => FilterType.BLMProducts,
                        "Local" => FilterType.LocalProducts,
                        _ => FilterType.AllProducts
                    };
                    ApplyFilters();
                };

                container.Add(btn);
            }
        }

        private void UpdateFilterDropdownChoices()
        {
            // Chips are static, no dynamic update needed
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
            root.schedule.Execute(UpdateQueueStatus).Every(BLMConstants.QueueStatusUpdateIntervalMs);
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

            // Auto-focus the imported product after a short delay
            // (delay needed because label assignment happens in OnPostprocessAllAssets
            //  which may fire slightly after importPackageCompleted)
            if (selectedProduct != null && !string.IsNullOrEmpty(selectedProduct.id))
            {
                string pid = selectedProduct.id;
                root.schedule.Execute(() =>
                {
                    // Wait for AssetDatabase to finish processing labels
                    AssetDatabase.Refresh();
                    ShowInProject(pid);
                }).ExecuteLater(500); // 500ms delay for label assignment to complete
            }
        }

        private void ShowQueueList()
        {
            var panel = root.Q<VisualElement>("queue-list-panel");
            var scroll = root.Q<ScrollView>("queue-list-scroll");
            if (panel == null || scroll == null) return;

            // Toggle: if visible, hide; if hidden, show
            if (!panel.ClassListContains("blm-detail-hidden"))
            {
                panel.AddToClassList("blm-detail-hidden");
                return;
            }

            scroll.Clear();
            panel.RemoveFromClassList("blm-detail-hidden");

            PopulateQueueList(scroll);
        }

        private void RefreshQueueListDisplay()
        {
            var panel = root.Q<VisualElement>("queue-list-panel");
            var scroll = root.Q<ScrollView>("queue-list-scroll");
            if (panel == null || scroll == null) return;
            // Only update if the panel is currently visible
            if (panel.ClassListContains("blm-detail-hidden")) return;
            scroll.Clear();
            PopulateQueueList(scroll);
        }

        private void PopulateQueueList(ScrollView scroll)
        {
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
            BLMHistory.Refresh(); // Only called on explicit refresh / import completion

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

            // Debug.Log suppressed for performance
            ApplyFilters();
        }

        private void EnsureLocalAssetsFolderExists()
        {
            GetOrCreateLocalAssetsFolder();
        }

        private static string GetOrCreateLocalAssetsFolder()
        {
            if (string.IsNullOrEmpty(BLMDatabaseService.LibraryRoot)) return null;

            string localAssetsPath = Path.Combine(BLMDatabaseService.LibraryRoot, BLMConstants.LocalAssetsFolderName);
            if (!Directory.Exists(localAssetsPath))
            {
                try
                {
                    Directory.CreateDirectory(localAssetsPath);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[BLM] Failed to create LocalAssets folder: {ex.Message}");
                    return null;
                }
            }
            return localAssetsPath;
        }

        private void OpenLocalAssetsFolder()
        {
            string localAssetsPath = GetOrCreateLocalAssetsFolder();
            if (localAssetsPath == null)
            {
                EditorUtility.DisplayDialog("Error", "BLM Library Root not found. Please ensure BOOTH Library Manager is configured.", "OK");
                return;
            }

            EditorUtility.RevealInFinder(localAssetsPath);
        }

        private void ApplyFilters()
        {
            // Single-pass filter + collect
            var filtered = new List<BoothProduct>(allProducts.Count);
            foreach (var p in allProducts)
            {
                bool passesFilter = currentFilterType switch
                {
                    FilterType.AllProducts => true,
                    FilterType.BLMProducts => p.sourceType == "BLM",
                    FilterType.LocalProducts => p.sourceType == "Local",
                    FilterType.CustomList => currentListId < 0 || (p.sourceType == "BLM" && int.TryParse(p.id, out int boothId) && listFilterCache.Contains(boothId)),
                    _ => true
                };

                if (!passesFilter) continue;

                // Search filter
                if (!string.IsNullOrEmpty(searchText))
                {
                    if ((p.name == null || !p.name.ToLower().Contains(searchText)) &&
                        (p.shopName == null || !p.shopName.ToLower().Contains(searchText)))
                        continue;
                }

                filtered.Add(p);
            }

            // Sort in-place
            switch (currentSort)
            {
                case SortMode.NameDesc:
                    filtered.Sort((a, b) => string.Compare(b.name, a.name, StringComparison.OrdinalIgnoreCase));
                    break;
                case SortMode.ShopAsc:
                    filtered.Sort((a, b) => string.Compare(a.shopName, b.shopName, StringComparison.OrdinalIgnoreCase));
                    break;
                default:
                    filtered.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.OrdinalIgnoreCase));
                    break;
            }

            filteredProducts = filtered;

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

        private void SetViewMode(bool listView)
        {
            isListView = listView;
            var gridBtn = root.Q<Button>("grid-view-btn");
            var listBtn = root.Q<Button>("list-view-btn");
            if (gridBtn != null) gridBtn.EnableInClassList("blm-icon-btn-active", !listView);
            if (listBtn != null) listBtn.EnableInClassList("blm-icon-btn-active", listView);
            RebuildGrid(filteredProducts);
        }

        private void RebuildGrid(List<BoothProduct> products)
        {
            if (gridContainer == null) return;

            // Build a lookup of what's currently displayed
            var existing = new Dictionary<string, VisualElement>();
            for (int i = gridContainer.childCount - 1; i >= 0; i--)
            {
                var child = gridContainer[i];
                var tag = child.userData as BoothProduct;
                if (tag != null)
                    existing[tag.id] = child;
                else
                    gridContainer.RemoveAt(i);
            }

            // Determine which products need to be shown
            var newIds = new HashSet<string>(products.Count);
            foreach (var p in products) newIds.Add(p.id);

            // Remove elements that are no longer in the filtered set
            var toRemove = new List<string>();
            foreach (var kv in existing)
            {
                if (!newIds.Contains(kv.Key))
                    toRemove.Add(kv.Key);
            }
            foreach (var id in toRemove)
            {
                gridContainer.Remove(existing[id]);
                existing.Remove(id);
            }

            // Re-add correct class if view mode changed
            gridContainer.ClearClassList();
            if (isListView)
            {
                gridContainer.AddToClassList("blm-list-container");

                // Rebuild all items if switching from grid (different structure)
                bool needsFullRebuild = false;
                foreach (var child in gridContainer.Children())
                {
                    if (!child.ClassListContains("blm-list-item")) { needsFullRebuild = true; break; }
                }
                if (needsFullRebuild)
                {
                    gridContainer.Clear();
                    foreach (var product in products)
                        gridContainer.Add(CreateListItem(product));
                    return;
                }

                // Reorder existing items
                foreach (var product in products)
                {
                    if (existing.TryGetValue(product.id, out var elem))
                    {
                        gridContainer.Add(elem); // Move to end (reorder)
                    }
                    else
                    {
                        var newItem = CreateListItem(product);
                        newItem.userData = product;
                        gridContainer.Add(newItem);
                    }
                }
            }
            else
            {
                gridContainer.AddToClassList("blm-grid-container");

                // Rebuild all items if switching from list (different structure)
                bool needsFullRebuild = false;
                foreach (var child in gridContainer.Children())
                {
                    if (!child.ClassListContains("blm-grid-item")) { needsFullRebuild = true; break; }
                }
                if (needsFullRebuild)
                {
                    gridContainer.Clear();
                    foreach (var product in products)
                    {
                        var item = CreateGridItem(product);
                        item.userData = product;
                        gridContainer.Add(item);
                    }
                    return;
                }

                // Reorder existing items
                foreach (var product in products)
                {
                    if (existing.TryGetValue(product.id, out var elem))
                    {
                        gridContainer.Add(elem); // Move to end (reorder)
                    }
                    else
                    {
                        var newItem = CreateGridItem(product);
                        newItem.userData = product;
                        gridContainer.Add(newItem);
                    }
                }
            }
        }

        private VisualElement CreateGridItem(BoothProduct product)
        {
            var item = new VisualElement();
            item.AddToClassList("blm-grid-item");
            item.userData = product;
            item.RegisterCallback<MouseDownEvent>(evt => OnProductClick(evt, product));

            var thumb = new Image();
            thumb.AddToClassList("blm-thumbnail");
            LoadThumbnail(thumb, product);

            var tc = new VisualElement();
            tc.AddToClassList("blm-thumbnail-container");
            tc.Add(thumb);
            item.Add(tc);

            var info = new VisualElement();
            info.AddToClassList("blm-item-info");

            var nameLabel = new Label(product.name);
            nameLabel.AddToClassList("blm-item-name");
            nameLabel.tooltip = product.name;

            var shopLabel = new Label(product.shopName);
            shopLabel.AddToClassList("blm-item-shop");

            info.Add(nameLabel);
            info.Add(shopLabel);
            item.Add(info);

            if (BLMHistory.IsInstalled(product))
                item.AddToClassList("blm-installed");
            if (importedProductIds.Contains(product.id))
                item.AddToClassList("blm-batch-imported");

            return item;
        }

        private VisualElement CreateListItem(BoothProduct product)
        {
            var item = new VisualElement();
            item.AddToClassList("blm-list-item");
            item.userData = product;
            item.RegisterCallback<MouseDownEvent>(evt => OnProductClick(evt, product));

            var thumb = new Image();
            thumb.AddToClassList("blm-list-thumb");
            LoadThumbnail(thumb, product);
            item.Add(thumb);

            var info = new VisualElement();
            info.AddToClassList("blm-list-info");

            var nameLabel = new Label(product.name);
            nameLabel.AddToClassList("blm-list-name");

            var shopLabel = new Label(product.shopName);
            shopLabel.AddToClassList("blm-list-shop");

            info.Add(nameLabel);
            info.Add(shopLabel);
            item.Add(info);

            var meta = new Label(product.sourceType ?? "");
            meta.AddToClassList("blm-list-meta");
            item.Add(meta);

            if (BLMHistory.IsInstalled(product))
                item.style.opacity = 0.5f;
            if (importedProductIds.Contains(product.id))
            {
                item.style.borderBottomColor = new Color(0.29f, 0.62f, 1f);
                item.style.borderBottomWidth = 2;
            }

            return item;
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

        private static bool thumbnailCacheDirChecked = false;

        private void LoadThumbnail(Image img, BoothProduct product)
        {
            if (!string.IsNullOrEmpty(product.thumbnailPath) && File.Exists(product.thumbnailPath))
            {
                // Defer heavy File.ReadAllBytes + LoadImage to avoid blocking UI
                string path = product.thumbnailPath;
                img.schedule.Execute(() =>
                {
                    try
                    {
                        var tex = new Texture2D(2, 2);
                        tex.LoadImage(File.ReadAllBytes(path));
                        img.image = tex;
                    }
                    catch { }
                }).ExecuteLater(0);
                return;
            }

            string cacheDir = BLMConstants.ThumbnailCacheDir;
            if (!thumbnailCacheDirChecked)
            {
                if (!Directory.Exists(cacheDir)) Directory.CreateDirectory(cacheDir);
                thumbnailCacheDirChecked = true;
            }

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
            selectedPackagePaths.Clear(); // Clear only on fresh open
            detailOverlay.RemoveFromClassList("blm-detail-hidden");

            var nameLbl = detailPanel.Q<Label>("detail-product-name");
            if (nameLbl != null) nameLbl.text = product.name;

            var pathLbl = detailPanel.Q<Label>("detail-path");
            if (pathLbl != null) pathLbl.text = product.rootFolderPath;

            var list = detailPanel.Q<ScrollView>("package-list");
            if (list == null) return;
            list.Clear();

            // Lazy load assets on first open
            if (product.assets == null)
            {
                product.assets = BLMDatabaseService.FindProductAssets(product.id, product.rootFolderPath);
                product.packages = BLMDatabaseService.FindProductPackages(product.id, product.rootFolderPath);
            }

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
            var footer = detailPanel.Q<VisualElement>(className: "blm-modal-footer");
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
            addBtn.AddToClassList("blm-import-button");
            footer.Add(addBtn);
        }

        private void ShowInProject(string productId)
        {
            // Try label-based search first
            string[] guids = AssetDatabase.FindAssets($"l:{BLMConstants.LabelPrefix_PID}{productId}");

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

            // Fallback: search by product name in Assets/
            if (selectedProduct != null)
            {
                // Try exact folder name match
                string safeName = SanitizeFolderName(selectedProduct.name);
                string[] folderGuids = AssetDatabase.FindAssets($"t:DefaultAsset {safeName}");
                foreach (var fg in folderGuids)
                {
                    var p = AssetDatabase.GUIDToAssetPath(fg);
                    if (p.StartsWith("Assets/") && p.Count(c => c == '/') == 1)
                    {
                        var folderObj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(p);
                        if (folderObj != null)
                        {
                            Selection.activeObject = folderObj;
                            EditorGUIUtility.PingObject(folderObj);
                            Debug.Log($"[BLM] Focused imported folder (name fallback): {p}");
                            return;
                        }
                    }
                }

                // Last resort: open in file explorer
                if (Directory.Exists(selectedProduct.rootFolderPath))
                {
                    EditorUtility.RevealInFinder(selectedProduct.rootFolderPath);
                    return;
                }
            }

            Debug.LogWarning($"[BLM] Could not locate imported product {productId}");
        }

        private string SanitizeFolderName(string name)
        {
            // Remove common Booth naming artifacts
            var result = name.Replace("【", "").Replace("】", "").Replace("[", "").Replace("]", "");
            result = System.Text.RegularExpressions.Regex.Replace(result, @"[\{\}]", "");
            // Trim and take first 10 chars for broader match
            result = result.Trim();
            if (result.Length > 10) result = result.Substring(0, 10);
            return result;
        }

        private void DeleteFromProject(string productId)
        {
            string[] guids = AssetDatabase.FindAssets($"l:{BLMConstants.LabelPrefix_PID}{productId}");

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
            if (!labels.Any(l => l.StartsWith(BLMConstants.LabelPrefix_PID)))
            {
                EditorUtility.DisplayDialog("Delete", "Folder has no BLM_PID label.", "OK");
                return;
            }

            var otherProductIds = labels
                .Where(l => l.StartsWith(BLMConstants.LabelPrefix_PID) && l != $"{BLMConstants.LabelPrefix_PID}{productId}")
                .Select(l => l.Substring(BLMConstants.LabelPrefix_PID.Length))
                .ToList();

            if (otherProductIds.Count > 0)
            {
                string otherNames = string.Join("\n", otherProductIds.Take(5).Select(id => $"• Product ID: {id}"));
                if (otherProductIds.Count > 5)
                    otherNames += $"\n... and {otherProductIds.Count - 5} more";

                bool proceed = EditorUtility.DisplayDialog(
                    "Warning: Shared Folder",
                    $"This folder contains other products:\n{otherNames}\n\nDeleting will remove ALL contents including other products.\n\nTarget: {path}",
                    "Delete Anyway", "Cancel");

                if (!proceed) return;
            }
            else
            {
                bool confirm = EditorUtility.DisplayDialog(
                    "Delete Imported Assets",
                    $"Delete the following folder from your project?\n\n{path}",
                    "Delete", "Cancel");

                if (!confirm) return;
            }

            foreach (var otherPid in otherProductIds)
            {
                BLMHistory.Unmark(otherPid);
            }

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
            zone.AddToClassList("blm-asset-zone");


            var headerRow = new VisualElement();
            headerRow.AddToClassList("blm-asset-zone-header");

            var zoneHeader = new Label($"─ {zoneName} ({assets.Count}) ─");
            zoneHeader.AddToClassList("blm-asset-zone-title");
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
                selectAllBtn.AddToClassList("blm-asset-select-btn");
                headerRow.Add(selectAllBtn);

                var deselectAllBtn = new Button(() =>
                {
                    foreach (var asset in assets)
                        selectedPackagePaths.Remove(asset.fullPath);
                    RefreshDetailPanel();
                }) { text = "Deselect" };
                deselectAllBtn.AddToClassList("blm-asset-select-btn");
                headerRow.Add(deselectAllBtn);
            }

            zone.Add(headerRow);

            foreach (var asset in assets)
            {
                var assetRow = new VisualElement();
                assetRow.AddToClassList("blm-asset-row");

                var toggle = new Toggle { text = asset.fileName, value = selectedPackagePaths.Contains(asset.fullPath) };
                toggle.userData = asset.fullPath;
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
                importBtn.AddToClassList("blm-asset-import-btn");

                assetRow.Add(toggle);
                assetRow.Add(importBtn);
                zone.Add(assetRow);
            }

            parent.Add(zone);
        }

        private void RefreshDetailPanel()
        {
            if (selectedProduct == null || detailPanel == null) return;

            // Only update toggle states without rebuilding the whole panel
            var toggles = detailPanel.Query<Toggle>().ToList();
            foreach (var toggle in toggles)
            {
                // The toggle's userData should contain the asset path
                if (toggle.userData is string path)
                {
                    // Temporarily disable callback to avoid feedback loop
                    var previousValue = toggle.value;
                    toggle.SetValueWithoutNotify(selectedPackagePaths.Contains(path));
                }
            }

            UpdateDetailFooter(selectedProduct);
        }

        private void ImportAsset(BoothAsset asset, BoothProduct product)
        {
            if (asset.assetType == AssetType.UnityPackage)
            {
                importedProductIds.Add(product.id);
                AssetImportQueue.Enqueue(asset.fullPath, product.id);
                AssetImportQueue.StartImport();
                UpdateQueueStatus();
                ShowQueueList();
            }
            else
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
        }

        private void HideDetail() => detailOverlay?.AddToClassList("blm-detail-hidden");

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
                $"Packages/com.morulab.unity-tools/Editor/Tools/BLMConnector/{fileName}"
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
            // Early exit: skip query when nothing to update
            if (!AssetImportQueue.IsImporting && AssetImportQueue.RemainingCount == 0)
            {
                var sl = root?.Q<Label>("queue-status");
                var pb = root?.Q<Button>("process-queue");
                if (sl != null && sl.text != "Queue is empty")
                    sl.text = "Queue is empty";
                if (pb != null && !pb.text.EndsWith("(0)"))
                    pb.text = "Process Queue (0)";
                return;
            }

            var statusLabel = root?.Q<Label>("queue-status");
            var processBtn = root?.Q<Button>("process-queue");
            if (statusLabel != null) statusLabel.text = AssetImportQueue.IsImporting ? "Importing..." : $"{AssetImportQueue.RemainingCount} items in queue";
            if (processBtn != null) processBtn.text = $"Process Queue ({AssetImportQueue.RemainingCount})";
        }
    }
}
