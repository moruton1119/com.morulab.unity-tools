using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using System;
using System.Reflection;

namespace MorulabTools.Launcher
{
    public class MorulabLauncher : EditorWindow
    {
        [MenuItem("Morulab/Launcher")]
        public static void ShowWindow()
        {
            MorulabLauncher wnd = GetWindow<MorulabLauncher>();
            wnd.titleContent = new GUIContent("Morulab Hub");
            wnd.minSize = new Vector2(900, 600);
        }

        // Constants
        private const string PrefKeyLang = "MorulabTools.Launcher.Lang";

        // UI References
        private ScrollView _toolList;
        private VisualElement _toolBody;
        private Label _headerTitle;
        private Label _headerDesc;
        private Button _btnLangEN, _btnLangJA, _btnLangKO;

        // State
        private ToolCommandData _selectedCommand;
        private Dictionary<ToolCommandData, VisualElement> _toolItemElements = new Dictionary<ToolCommandData, VisualElement>();
        private string _currentLang = "en"; // en, ja, ko

        // Cache for loaded tools
        private List<ToolCommandData> _allCommands = new List<ToolCommandData>();

        public void CreateGUI()
        {
            // Load UXML
            var visualTree = Resources.Load<VisualTreeAsset>("MorulabLauncher");
            if (visualTree == null)
            {
                rootVisualElement.Add(new Label("Error: Could not load MorulabLauncher.uxml"));
                return;
            }
            visualTree.CloneTree(rootVisualElement);

            // Load USS
            var styleSheet = Resources.Load<StyleSheet>("MorulabLauncher");
            if (styleSheet != null) rootVisualElement.styleSheets.Add(styleSheet);

            // Find Elements
            _toolList = rootVisualElement.Q<ScrollView>("ToolList");
            _toolBody = rootVisualElement.Q<VisualElement>("ToolBody");
            _headerTitle = rootVisualElement.Q<Label>("HeaderTitle");
            _headerDesc = rootVisualElement.Q<Label>("HeaderDesc");

            // Language Buttons
            _btnLangEN = rootVisualElement.Q<Button>("BtnLangEN");
            _btnLangJA = rootVisualElement.Q<Button>("BtnLangJA");
            _btnLangKO = rootVisualElement.Q<Button>("BtnLangKO");

            if (_btnLangEN != null) _btnLangEN.clicked += () => SetLanguage("en");
            if (_btnLangJA != null) _btnLangJA.clicked += () => SetLanguage("ja");
            if (_btnLangKO != null) _btnLangKO.clicked += () => SetLanguage("ko");

            // Sidebar Logic
            var sidebar = rootVisualElement.Q<VisualElement>("Sidebar");
            var hamburgerBtn = rootVisualElement.Q<Button>("HamburgerBtn");
            if (hamburgerBtn != null && sidebar != null)
            {
                hamburgerBtn.clicked += () => sidebar.ToggleInClassList("collapsed");
            }

            // Load State
            _currentLang = EditorPrefs.GetString(PrefKeyLang, "en");
            UpdateLangButtons();

            // Initialize
            RefreshToolList();
        }

        private void SetLanguage(string lang)
        {
            if (_currentLang == lang) return;
            _currentLang = lang;
            EditorPrefs.SetString(PrefKeyLang, _currentLang);

            UpdateLangButtons();
            // RefreshToolList(); // Sidebar stays EN

            // Re-render body if selected
            if (_selectedCommand != null) LoadToolBody(_selectedCommand);
        }

        private void UpdateLangButtons()
        {
            if (_btnLangEN == null) return;
            _btnLangEN.EnableInClassList("selected", _currentLang == "en");
            _btnLangJA.EnableInClassList("selected", _currentLang == "ja");
            _btnLangKO.EnableInClassList("selected", _currentLang == "ko");
        }

        private void RefreshToolList()
        {
            _toolList.Clear();
            _toolItemElements.Clear();
            _allCommands.Clear();

            // Load commands safely
            try
            {
                var cmds = ReflectionUtils.FindCommands("Morulab");
                if (cmds != null) _allCommands = cmds.Where(c => !c.Title.Contains("Launcher")).ToList();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Launcher] Error loading commands: {ex.Message}");
            }

            // Group by category (ALWAYS EN)
            var groups = _allCommands.OrderBy(c => c.GetInfo("en").Category).GroupBy(c => c.GetInfo("en").Category);

            foreach (var group in groups)
            {
                string categoryName = string.IsNullOrEmpty(group.Key) ? "General" : group.Key;

                // 1. Create Group Container
                var groupContainer = new VisualElement();
                groupContainer.AddToClassList("category-group");

                // 2. Create Header
                var headerArgs = CreateCategoryHeader(categoryName);
                var headerElement = headerArgs.Root;
                var contentContainer = headerArgs.Content;

                groupContainer.Add(headerElement);
                groupContainer.Add(contentContainer);

                // 3. Add Items
                foreach (var command in group)
                {
                    var item = CreateSidebarItem(command);
                    contentContainer.Add(item);
                    _toolItemElements[command] = item;
                }

                _toolList.Add(groupContainer);
            }
        }

        private (VisualElement Root, VisualElement Content) CreateCategoryHeader(string title)
        {
            // Header Row
            var header = new VisualElement();
            header.AddToClassList("category-header");

            // Arrow
            var arrow = new Label("▼");
            arrow.AddToClassList("category-arrow");
            header.Add(arrow);

            // Title
            var label = new Label(title);
            label.AddToClassList("category-label");
            header.Add(label);

            // Content Container
            var content = new VisualElement();
            content.AddToClassList("category-content");

            // Toggle Logic
            header.RegisterCallback<ClickEvent>(evt =>
            {
                bool isCollapsed = content.ClassListContains("collapsed");
                if (isCollapsed)
                {
                    content.RemoveFromClassList("collapsed"); // Expand
                    arrow.text = "▼";
                }
                else
                {
                    content.AddToClassList("collapsed"); // Collapse
                    arrow.text = "▶";
                }
            });

            return (header, content);
        }

        private VisualElement CreateSidebarItem(ToolCommandData command)
        {
            var item = new VisualElement();
            item.AddToClassList("tool-item");

            // Localized Title (ALWAYS EN)
            var info = command.GetInfo("en");
            var label = new Label(info.Title);
            label.AddToClassList("tool-item-label");

            item.Add(label);

            item.RegisterCallback<ClickEvent>(evt => SelectTool(command));
            return item;
        }

        private void SelectTool(ToolCommandData command)
        {
            if (_selectedCommand == command)
            {
                // Force reload body if language changed even if same command
                // But SelectTool usually filters. We can reload body directly in SetLanguage.
            }

            // Update UI State
            if (_selectedCommand != null && _toolItemElements.ContainsKey(_selectedCommand))
                _toolItemElements[_selectedCommand].RemoveFromClassList("selected");

            _selectedCommand = command;

            if (_toolItemElements.ContainsKey(_selectedCommand))
                _toolItemElements[_selectedCommand].AddToClassList("selected");

            // Load Body
            LoadToolBody(command);
        }

        private void LoadToolBody(ToolCommandData command)
        {
            _toolBody.Clear();

            // Always show Description + Open Button first
            var container = new VisualElement();
            container.style.paddingTop = 20;
            container.style.paddingBottom = 20;
            container.style.paddingLeft = 20;
            container.style.paddingRight = 20;

            var info = command.GetInfo(_currentLang);

            // Header Title Update (Manual update since it's outside this container)
            _headerTitle.text = info.Title;
            _headerDesc.text = info.Description;

            // Determine if embedding matches
            bool canEmbed = CanEmbedTool(command);
            string btnText = canEmbed ? "Open Tool (Embedded)" : "Open Tool (Window)";

            var openBtn = new Button(() =>
            {
                if (canEmbed)
                {
                    var view = CreateEmbeddedView(command);
                    if (view != null)
                    {
                        _toolBody.Clear();
                        _toolBody.Add(view);
                    }
                    else
                    {
                        // Fallback if creation fails
                        ExecuteCommand(command);
                    }
                }
                else
                {
                    ExecuteCommand(command);
                }
            })
            { text = btnText };

            openBtn.AddToClassList("launch-button");
            container.Add(openBtn);

            var docLabel = new Label("Documentation:");
            docLabel.style.marginTop = 20;
            docLabel.style.fontSize = 18;
            container.Add(docLabel);

            var docContent = new Label(GetDocumentation(command));
            docContent.style.whiteSpace = WhiteSpace.Normal;
            docContent.style.marginTop = 10;
            docContent.style.color = new Color(0.7f, 0.7f, 0.7f);
            container.Add(docContent);

            _toolBody.Add(container);
        }

        private bool CanEmbedTool(ToolCommandData command)
        {
            if (command.TargetMethod == null) return false;
            var type = command.TargetMethod.DeclaringType;
            if (type == null) return false;
            var method = type.GetMethod("CreateEmbeddedView", BindingFlags.Public | BindingFlags.Static);
            return (method != null && method.ReturnType == typeof(VisualElement));
        }

        private VisualElement CreateEmbeddedView(ToolCommandData command)
        {
            try
            {
                var type = command.TargetMethod.DeclaringType;
                var method = type.GetMethod("CreateEmbeddedView", BindingFlags.Public | BindingFlags.Static);
                var view = method.Invoke(null, null) as VisualElement;
                if (view != null) view.AddToClassList("embedded-tool-root");
                return view;
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error creating embedded view: {ex}");
                return null;
            }
        }

        private string GetDocumentation(ToolCommandData command)
        {
            if (command.TargetMethod == null) return "No documentation.";

            // Try to find localized markdown
            var type = command.TargetMethod.DeclaringType;
            if (type == null) return "No type info.";

            var guids = AssetDatabase.FindAssets($"t:MonoScript {type.Name}");
            if (guids.Length > 0)
            {
                var path = AssetDatabase.GUIDToAssetPath(guids[0]);
                var dir = System.IO.Path.GetDirectoryName(path);

                // 1. Try Localized MD (e.g. MyTool_en.md, MyTool_ja.md)
                // Always check for explicit language file first
                var locPath = System.IO.Path.Combine(dir, $"{type.Name}_{_currentLang}.md");
                if (System.IO.File.Exists(locPath)) return System.IO.File.ReadAllText(locPath);

                // 2. Try Default MD (MyTool.md) - This will be the Fallback (e.g. Japanese)
                var mdPath = System.IO.Path.Combine(dir, $"{type.Name}.md");
                if (System.IO.File.Exists(mdPath)) return System.IO.File.ReadAllText(mdPath);

                // 3. Try ReadMe.md
                var readmePath = System.IO.Path.Combine(dir, "ReadMe.md");
                if (System.IO.File.Exists(readmePath)) return System.IO.File.ReadAllText(readmePath);
            }

            return "No documentation found.";
        }

        private void ExecuteCommand(ToolCommandData command)
        {
            if (command.TargetMethod != null) command.TargetMethod.Invoke(null, null);
            else EditorApplication.ExecuteMenuItem(command.Path);
        }
    }
}
