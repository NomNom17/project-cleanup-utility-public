// -----------------------------------------------------------------------
// Project Cleanup Utility
// Copyright (C) 2026 NomNom
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program. If not, see <https://www.gnu.org/licenses/>.
//
// Source: https://github.com/NomNom17/Project-Cleanup-Utility
// -----------------------------------------------------------------------

using ProjectCleanupUtility.Core;
using ProjectCleanupUtility.Data;
using ProjectCleanupUtility.UI;
using ProjectCleanupUtility.Utilities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.VersionControl;
using UnityEngine;
using UnityEngine.UIElements;

#region UNICODE CHARACTERS USED

/*
 * \u25B2 = ▲ (up arrow) [https://www.compart.com/en/unicode/U+25B2]
 * \u25BC = ▼ (down arrow) [https://www.compart.com/en/unicode/U+25BC]
 * \u2699 = ⚙ (gear) [https://www.compart.com/en/unicode/U+2699]
 * \u2192 = → (right arrow) [https://www.compart.com/en/unicode/U+2192]
 * \u2190 = ← (left arrow) [https://www.compart.com/en/unicode/U+2190]
 */

#endregion

// --- References ---
// MultiColumnListView:             https://docs.unity3d.com/6000.4/Documentation/ScriptReference/UIElements.MultiColumnListView.html
// MultiColumnListView (manual):    https://docs.unity3d.com/6000.3/Documentation/Manual/UIE-uxml-element-MultiColumnListView.html
// EditorWindow:                    https://docs.unity3d.com/ScriptReference/EditorWindow.html
// GenericMenu:                     https://docs.unity3d.com/ScriptReference/GenericMenu.html
// MD5 hashing (System.Security.Cryptography.MD5): https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.md5

namespace ProjectCleanupUtility.UI.Views
{
    /// <summary>
    /// The main editor window for the Project Cleanup Utility.
    /// Provides scanning, filtering, quarantine, and dependency viewing.
    /// </summary>
    public class ProjectCleanupWindow : EditorWindow
    {
        private const string WINDOW_TITLE = "Project Cleanup Utility";

        // Scan history is persisted as JSON next to the whitelist config
        private const string SCAN_HISTORY_FILENAME = "ProjectCleanup_ScanHistory.json";

        // EditorPrefs keys - because remembering your preferences is the least we can do
        private const string PREF_SORT_COLUMN = "ProjectCleanup_SortColumn";
        private const string PREF_SORT_ASCENDING = "ProjectCleanup_SortAscending";
        private const string PREF_FILTER_CATEGORY = "ProjectCleanup_FilterCategory";
        private const string PREF_SHOW_ONLY_UNUSED = "ProjectCleanup_ShowOnlyUnused";
        private const string PREF_LAST_ACTIVE_TAB = "ProjectCleanup_LastActiveTab";
        private const string PREF_DEPENDENCY_PANEL_WIDTH = "ProjectCleanup_DependencyPanelWidth";

        // Quarantine age thresholds (days)
        private const int QUARANTINE_WARNING_DAYS = 30;
        private const int QUARANTINE_DANGER_DAYS = 60;

        // State
        private readonly ScanOrchestrator _scanOrchestrator = new ScanOrchestrator();
        private DependencyGraphBuilder _dependencyGraph;
        private ScanResult _scanResult;
        private WhitelistConfig _whitelist;

        // Extracted services/controllers
        private readonly ExportService _exportService = new ExportService();
        private UndoController _undoController;
        private AccessibilityController _accessibilityController;
        private ToastService _toastService;

        // Scan history for diff comparison
        private ScanHistoryEntry _previousScan;

        // Filtered / displayed list
        private List<AssetInfo> _displayedAssets = new List<AssetInfo>();

        // Filter state
        private string _searchQuery = "";
        private AssetCategory _filterCategory = (AssetCategory)(-1); // -1 = All
        private SortColumn _sortColumn = SortColumn.Size;
        private bool _sortAscending = false;
        private bool _showOnlyUnused = true;

        // Duplicate detection results - finding the copycats in your project
        private Dictionary<string, List<AssetInfo>> _duplicateGroups = new Dictionary<string, List<AssetInfo>>();

        // Guards all reads/writes of _duplicateGroups. FindDuplicates() populates it on a background thread (via Task.Run); the ContinueWith ordering already ensures UpdateStatsPanel/UpdateDuplicateSection only run after it completes, but this lock is kept as defense in depth in case another code path reads _duplicateGroups while a scan is still running.
        private readonly object _duplicateGroupsLock = new object();

        // Quarantine tab data
        private List<QuarantineEntry> _quarantineEntries = new List<QuarantineEntry>();

        // UI References
        private VisualElement _root;

        private VisualElement _actionGroup;

        // Tab system
        private VisualElement _tabBar;
        private Button _assetsTabBtn;
        private Button _overviewTabBtn;
        private Button _quarantineTabBtn;
        private VisualElement _assetsTabContent;
        private VisualElement _overviewTabContent;
        private VisualElement _quarantineTabContent;
        private int _activeTab = 1; // 0 = Assets, 1 = Overview, 2 = Quarantine

        // Stats (shown on Overview tab)
        private VisualElement _statsPanel;

        // Scan diff banner (shown on Overview tab when previous scan exists)
        private VisualElement _diffBanner;

        // Category breakdown (shown on Overview tab)
        private VisualElement _categoryBreakdown;
        private VisualElement _categoryContent;

        // Duplicate section (shown on Overview tab)
        private VisualElement _duplicateSection;
        private VisualElement _duplicateContent;

        // Asset list (shown on Assets tab)
        private VisualElement _splitContainer;
        private VisualElement _assetListContainer;
        private MultiColumnListView _assetListView;
        private VisualElement _dependencyPanel;
        private VisualElement _emptyState;

        // Select All button
        private Button _selectAllBtn;
        private Button _scanBtn;

        // Quarantine tab elements
        private MultiColumnListView _quarantineListView;
        private VisualElement _quarantineEmptyState;
        private Label _quarantineWarningBanner;

        // Undo system - because everyone deserves a second chance (except permanent deletes)
        private Button _undoBtn;

        // Error log panel - where the skeletons come out of the closet
        private List<string> _scanLog = new List<string>();
        private Foldout _logFoldout;
        private VisualElement _logContent;

        // Asset preview image in dependency panel
        private Image _assetPreviewImage;

        // Category dropdown (stored as field so double-click filter can update it)
        private DropdownField _categoryDropdown;

        // Preserved asset selection across tab switches
        private List<int> _preservedAssetSelection = new List<int>();

        // Accessibility State - settings/logic now live in AccessibilityController;
        // the window keeps only the UI-construction-related fields.
        private VisualElement _accessibilityPanel;
        private bool _accessibilityPanelVisible = false;

        // Shared
        private Label _statusMessage;
        private Label _statusCount;
        private Label _selectionSummary;
        private VisualElement _progressOverlay;
        private Label _progressLabel;
        private VisualElement _progressBarFill;

        // Menu Item
        [MenuItem("Tools/Project Cleanup Utility", priority = 100)]
        public static void ShowWindow()
        {
            var window = GetWindow<ProjectCleanupWindow>();
            window.titleContent = new GUIContent(WINDOW_TITLE, EditorGUIUtility.IconContent("d_Search Icon").image);
            window.minSize = new Vector2(700, 450);
            window.Show();
        }

        // Lifecycle
        private void OnEnable()
        {
            _previousScan = LoadScanHistory();

            _scanOrchestrator.Enable();
            _scanOrchestrator.OnProgressUpdated += HandleScanProgress;
            _scanOrchestrator.OnScanComplete += HandleScanComplete;
            _scanOrchestrator.OnScanError += HandleScanError;
            _scanOrchestrator.OnQuarantineStatusMessage += HandleQuarantineStatusMessage;
            _scanOrchestrator.OnQuarantineError += HandleQuarantineError;

            _undoController = new UndoController(_scanOrchestrator.QuarantineManager);
            _undoController.OnActionRecorded += HandleUndoActionRecorded;
            _undoController.OnUndoApplied += HandleUndoApplied;
        }

        /// <summary>
        /// Cleanup when the window is closed or the editor recompiles.
        /// </summary>
        private void OnDisable()
        {
            // Make sure we're not still eavesdropping on log messages
            Application.logMessageReceived -= OnLogMessageReceived;

            _scanOrchestrator.OnProgressUpdated -= HandleScanProgress;
            _scanOrchestrator.OnScanComplete -= HandleScanComplete;
            _scanOrchestrator.OnScanError -= HandleScanError;
            _scanOrchestrator.OnQuarantineStatusMessage -= HandleQuarantineStatusMessage;
            _scanOrchestrator.OnQuarantineError -= HandleQuarantineError;
            _scanOrchestrator.Disable();

            if (_undoController != null)
            {
                _undoController.OnActionRecorded -= HandleUndoActionRecorded;
                _undoController.OnUndoApplied -= HandleUndoApplied;
            }
        }

        /// <summary>
        /// Handles progress updates from <see cref="ScanOrchestrator.OnProgressUpdated"/> during a scan.
        /// Extracted into a named method (rather than a lambda re-created per scan) so it can be subscribed once in <see cref="OnEnable"/> and unsubscribed once in <see cref="OnDisable"/>.
        /// </summary>
        private void HandleScanProgress(string msg, float progress)
        {
            if (_progressLabel != null) _progressLabel.text = msg;
            if (_progressBarFill != null) _progressBarFill.style.width = new Length(progress * 100, LengthUnit.Percent);
        }

        /// <summary>
        /// Handles <see cref="ScanOrchestrator.OnScanComplete"/>. The bulk of post-scan UI updates are still driven from the OnScanClicked delayCall continuation; this handler exists so the event has a consistent, single subscriber for logging/diagnostics purposes.
        /// </summary>
        private void HandleScanComplete(ScanResult result)
        {
            // Intentionally minimal - OnScanClicked's delayCall continuation owns the detailed UI refresh sequence for a completed scan.
        }

        /// <summary>
        /// Handles <see cref="ScanOrchestrator.OnScanError"/> by routing the error into the same toast/log-panel system used for quarantine errors, so scan failures are surfaced to the user consistently.
        /// </summary>
        private void HandleScanError(string message)
        {
            _toastService.Show(message, ToastType.Error);
            _scanLog.Add($"[ERROR] {message}");
            UpdateLogPanel();
        }

        /// <summary>
        /// Handles <see cref="ScanOrchestrator.OnQuarantineStatusMessage"/> by appending it to the log panel.
        /// </summary>
        private void HandleQuarantineStatusMessage(string message)
        {
            _scanLog.Add($"[INFO] {message}");
            UpdateLogPanel();
        }

        /// <summary>
        /// Handles <see cref="ScanOrchestrator.OnQuarantineError"/> by surfacing the error as an error toast and appending it to the log panel, matching how scanner errors are surfaced.
        /// </summary>
        private void HandleQuarantineError(string message)
        {
            _toastService.Show(message, ToastType.Error);
            _scanLog.Add($"[ERROR] {message}");
            UpdateLogPanel();
        }

        /// <summary>
        /// Handles <see cref="UndoController.OnActionRecorded"/> by enabling the Undo button and updating its tooltip/label to describe the action that can be reversed.
        /// </summary>
        private void HandleUndoActionRecorded(UndoableAction action)
        {
            if (_undoBtn == null) return;

            _undoBtn.SetEnabled(true);
            _undoBtn.tooltip = $"Undo: {action.Description}";
            _undoBtn.text = $"Undo ({action.Assets.Count})";
        }

        /// <summary>
        /// Handles <see cref="UndoController.OnUndoApplied"/> by resetting the Undo button, showing a success toast with the controller's result message, refreshing the quarantine tab, and - for Quarantine/Restore undos - applying the matching inverse incremental update so the live asset list stays correct without a full re-scan. This preserves the exact behaviour of the old inline function <c>OnUndoLastAction</c>.
        /// </summary>
        private void HandleUndoApplied(string message, UndoActionType undoneType, List<AssetInfo> assetInfos)
        {
            _toastService.Show(message, ToastType.Success);

            if (_undoBtn != null)
            {
                _undoBtn.SetEnabled(false);
                _undoBtn.tooltip = "Nothing to undo";
                _undoBtn.text = "Undo";
            }

            // Refresh views
            RefreshQuarantineList();

            // Undo re-applies the inverse of the original action, so it gets the matching inverse incremental update rather than a full re-scan
            switch (undoneType)
            {
                case UndoActionType.Quarantine:
                    ApplyIncrementalUpdate(assetInfos, IncrementalUpdateKind.Restore);
                    break;

                case UndoActionType.Restore:
                    ApplyIncrementalUpdate(assetInfos, IncrementalUpdateKind.Quarantine);
                    break;
            }
        }

        private void CreateGUI()
        {
            _root = rootVisualElement;

            _accessibilityController = new AccessibilityController(_root);

            // Load persisted preferences here (not in OnEnable) - welcome back, we missed you.
            LoadPreferences();

            LoadStyleSheet();

            // Build the UI tree
            _root.AddToClassList("root-container");

            BuildMenuBar();
            BuildToolbar();
            BuildAccessibilityPanel();
            BuildTabBar();

            // Overview Tab Content
            _overviewTabContent = new VisualElement();
            _overviewTabContent.AddToClassList("tab-content");

            BuildStatsPanel();
            BuildDiffBanner();
            BuildCategoryBreakdown();
            BuildDuplicateSection();
            BuildLogPanel();

            _root.Add(_overviewTabContent);

            // Assets Tab Content
            _assetsTabContent = new VisualElement();
            _assetsTabContent.AddToClassList("tab-content");
            _assetsTabContent.style.display = DisplayStyle.None;

            BuildFilterBar();

            // Horizontal split: asset list (left) + dependency panel (right)
            _splitContainer = new VisualElement();
            _splitContainer.AddToClassList("split-container");

            BuildAssetList();
            BuildDependencyPanel();

            _assetsTabContent.Add(_splitContainer);

            BuildEmptyState();

            _root.Add(_assetsTabContent);

            // Quarantine Tab Content
            _quarantineTabContent = new VisualElement();
            _quarantineTabContent.AddToClassList("tab-content");
            _quarantineTabContent.style.display = DisplayStyle.None;

            BuildQuarantineTab();

            _root.Add(_quarantineTabContent);

            // Shared elements
            BuildStatusBar();
            BuildProgressOverlay();
            BuildToastContainer();

            // Show empty state initially
            ShowEmptyState(true);
            ShowDependencyPanel(false);

            // Register keyboard shortcuts on root
            _root.RegisterCallback<KeyDownEvent>(OnKeyDown);
            _root.focusable = true;

            // Apply saved tab preference (or default to Assets)
            SwitchToTab(_activeTab);

            // Apply persisted accessibility settings
            _accessibilityController.ApplyAll();
        }

        // Stylesheet Loading
        private void LoadStyleSheet()
        {
            // Try loading from multiple paths
            string[] guids = AssetDatabase.FindAssets("t:StyleSheet ProjectCleanupUtility");
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(path);
                if (sheet != null)
                {
                    _root.styleSheets.Add(sheet);
                    return;
                }
            }

            // If no stylesheet found, log a warning
            Debug.LogWarning("[Project Cleanup Utility] Could not find USS stylesheet. The UI will use default styles.");
        }

        // Toolbar
        private void BuildToolbar()
        {
            var toolbar = new VisualElement();
            toolbar.AddToClassList("toolbar");

            // --- Left side: action buttons (context-dependent) ---
            _actionGroup = new VisualElement();
            _actionGroup.AddToClassList("toolbar-action-group");
            _actionGroup.style.flexDirection = FlexDirection.Row;

            // This button also doubles as the manual "full rescan"
            _scanBtn = new Button(OnScanClicked) { text = "Scan Project", tooltip = "Scan the project for unused assets.\nAlso use this to force a full rescan/refresh after external project changes (e.g. a Git pull) or if reference/dependency data looks stale - quarantine/restore/delete/undo only update the list incrementally." };
            _scanBtn.AddToClassList("toolbar-button");
            _scanBtn.AddToClassList("scan-button");
            _actionGroup.Add(_scanBtn);

            var quarantineBtn = new Button(OnQuarantineClicked) { text = "Quarantine Selected" };
            quarantineBtn.AddToClassList("toolbar-button");
            quarantineBtn.AddToClassList("quarantine-button");
            _actionGroup.Add(quarantineBtn);

            var restoreBtn = new Button(OnRestoreClicked) { text = "Restore Selected" };
            restoreBtn.AddToClassList("toolbar-button");
            restoreBtn.AddToClassList("restore-button");
            _actionGroup.Add(restoreBtn);

            var deleteBtn = new Button(OnDeleteClicked) { text = "Delete Permanently" };
            deleteBtn.AddToClassList("toolbar-button");
            deleteBtn.AddToClassList("delete-button");
            _actionGroup.Add(deleteBtn);

            _selectAllBtn = new Button(OnSelectAllClicked) { text = "Select All" };
            _selectAllBtn.AddToClassList("toolbar-button");
            _actionGroup.Add(_selectAllBtn);

            toolbar.Add(_actionGroup);

            // --- Spacer ---
            var spacer = new VisualElement();
            spacer.AddToClassList("toolbar-spacer");
            toolbar.Add(spacer);

            // --- Right side: utility buttons (always visible) ---
            _undoBtn = new Button(_undoController.OnUndoLastAction) { text = "Undo" };
            _undoBtn.AddToClassList("toolbar-button");
            _undoBtn.SetEnabled(false);
            toolbar.Add(_undoBtn);

            var whitelistBtn = new Button(OnWhitelistClicked) { text = "Whitelist Selected" };
            whitelistBtn.AddToClassList("toolbar-button");
            toolbar.Add(whitelistBtn);

            var settingsBtn = new Button(OnSettingsClicked) { text = "\u2699", tooltip = "Open whitelist settings" };
            settingsBtn.AddToClassList("toolbar-button");
            settingsBtn.style.fontSize = 16;
            toolbar.Add(settingsBtn);

            var helpBtn = new Button(OnShortcutHelpClicked) { text = "?", tooltip = "Show keyboard shortcuts" };
            helpBtn.AddToClassList("toolbar-button");
            helpBtn.style.fontSize = 14;
            helpBtn.style.unityFontStyleAndWeight = FontStyle.Bold;
            toolbar.Add(helpBtn);

            _root.Add(toolbar);
        }

        // Menu Bar (Export & Accessibility)
        /// <summary>
        /// Builds the menu bar strip that sits below the main toolbar.
        /// Houses the export options and accessibility toggle.
        /// </summary>
        private void BuildMenuBar()
        {
            var menuBar = new VisualElement();
            menuBar.AddToClassList("menu-bar");

            // Export button - opens a native Unity GenericMenu dropdown
            var exportBtn = new Button(OnExportDropdown)
            { text = "Export", tooltip = "Export scan results (CSV or Excel)" };
            exportBtn.AddToClassList("menu-bar-item");
            exportBtn.focusable = true;
            menuBar.Add(exportBtn);

            // Separator
            var separator = new VisualElement();
            separator.AddToClassList("menu-bar-separator");
            menuBar.Add(separator);

            // Accessibility button
            var accessibilityBtn = new Button(ToggleAccessibilityPanel)
            { text = "Accessibility", tooltip = "Toggle accessibility settings panel (colour-blind modes, font scaling)" };
            accessibilityBtn.AddToClassList("menu-bar-item");
            accessibilityBtn.focusable = true;
            menuBar.Add(accessibilityBtn);

            _root.Add(menuBar);
        }

        /// <summary>
        /// Spawns a native Unity <see cref="GenericMenu"/> dropdown anchored to the Export button.
        /// </summary>
        private void OnExportDropdown()
        {
            var menu = new GenericMenu();

            bool hasScanData = _scanResult != null && _scanResult.UnusedAssets.Count > 0;

            if (hasScanData)
            {
                menu.AddItem(new GUIContent("Export as CSV"), false, () => ExportAs("csv"));
                menu.AddItem(new GUIContent("Export as Excel (.xlsx)"), false, () => ExportAs("xlsx"));
            }

            else
            {
                menu.AddDisabledItem(new GUIContent("Export as CSV (run a scan first)"));
                menu.AddDisabledItem(new GUIContent("Export as Excel (run a scan first)"));
            }

            menu.ShowAsContext();
        }

        // Tab Bar
        private void BuildTabBar()
        {
            _tabBar = new VisualElement();
            _tabBar.AddToClassList("tab-bar");

            _overviewTabBtn = new Button(() => SwitchToTab(1)) { text = "Overview" };
            _overviewTabBtn.AddToClassList("tab-button");
            _overviewTabBtn.AddToClassList("tab-button--active");
            _overviewTabBtn.focusable = true;
            _tabBar.Add(_overviewTabBtn);

            _assetsTabBtn = new Button(() => SwitchToTab(0)) { text = "Assets" };
            _assetsTabBtn.AddToClassList("tab-button");
            _assetsTabBtn.focusable = true;
            _tabBar.Add(_assetsTabBtn);

            _quarantineTabBtn = new Button(() => SwitchToTab(2)) { text = "Quarantine" };
            _quarantineTabBtn.AddToClassList("tab-button");
            _quarantineTabBtn.focusable = true;
            _tabBar.Add(_quarantineTabBtn);

            _root.Add(_tabBar);
        }

        private void SwitchToTab(int index)
        {
            // Preserve asset list selection before switching away from Assets tab
            if (_activeTab == 0 && index != 0 && _assetListView != null)
            {
                _preservedAssetSelection = _assetListView.selectedIndices.ToList();
            }

            _activeTab = index;

            // Toggle content visibility
            if (_assetsTabContent != null)
                _assetsTabContent.style.display = index == 0 ? DisplayStyle.Flex : DisplayStyle.None;

            if (_overviewTabContent != null)
                _overviewTabContent.style.display = index == 1 ? DisplayStyle.Flex : DisplayStyle.None;

            if (_quarantineTabContent != null)
                _quarantineTabContent.style.display = index == 2 ? DisplayStyle.Flex : DisplayStyle.None;

            // Toggle active button styling
            _assetsTabBtn?.EnableInClassList("tab-button--active", index == 0);
            _overviewTabBtn?.EnableInClassList("tab-button--active", index == 1);
            _quarantineTabBtn?.EnableInClassList("tab-button--active", index == 2);

            // Restore asset list selection when switching back to Assets tab
            if (index == 0 && _assetListView != null && _preservedAssetSelection.Count > 0)
            {
                _assetListView.SetSelection(_preservedAssetSelection);
                _preservedAssetSelection.Clear();
            }

            // Refresh quarantine list when switching to that tab
            if (index == 2)
            {
                RefreshQuarantineList();
            }

            if (_actionGroup != null)
                _actionGroup.style.display = index == 0 ? DisplayStyle.Flex : DisplayStyle.None;

            // Persist the active tab preference
            SavePreferences();
        }

        // Statistics Panel
        private void BuildStatsPanel()
        {
            _statsPanel = new VisualElement();
            _statsPanel.AddToClassList("stats-panel");
            _statsPanel.style.display = DisplayStyle.None;
            _overviewTabContent.Add(_statsPanel);
        }

        private void UpdateStatsPanel()
        {
            _statsPanel.Clear();

            if (_scanResult == null) return;

            _statsPanel.style.display = DisplayStyle.Flex;

            // Total assets
            AddStatCard(_statsPanel, _scanResult.TotalAssetCount.ToString(), "Total Assets", "stat-value");

            // Unused assets
            string unusedClass = _scanResult.UnusedAssetCount > 50 ? "stat-value-danger" : _scanResult.UnusedAssetCount > 10 ? "stat-value-warning" : "stat-value";

            AddStatCard(_statsPanel, _scanResult.UnusedAssetCount.ToString(), "Unused Assets", unusedClass);

            // Total size
            AddStatCard(_statsPanel, FormatBytes(_scanResult.TotalSizeBytes), "Total Size", "stat-value");

            // Unused size
            string unusedSizeClass = _scanResult.UnusedPercentage > 30 ? "stat-value-danger" : _scanResult.UnusedPercentage > 10 ? "stat-value-warning" : "stat-value";
            AddStatCard(_statsPanel, FormatBytes(_scanResult.UnusedSizeBytes), "Unused Size", unusedSizeClass);

            // Unused percentage
            AddStatCard(_statsPanel, $"{_scanResult.UnusedPercentage:F1}%", "Unused %", unusedSizeClass);

            // Scan time
            AddStatCard(_statsPanel, $"{_scanResult.ScanDurationSeconds:F1}s", "Scan Time", "stat-value");

            // Duplicate stats - because copy-paste is the sincerest form of disk waste
            int duplicateGroupCount;
            int duplicateFileCount;
            long wastedBytes;

            lock (_duplicateGroupsLock)
            {
                duplicateGroupCount = _duplicateGroups.Count;
                duplicateFileCount = _duplicateGroups.Values.Sum(g => g.Count);
                wastedBytes = _duplicateGroups.Values
                    .Sum(g => g.Skip(1).Sum(a => a.SizeBytes));
            }

            if (duplicateGroupCount > 0)
            {
                AddStatCard(_statsPanel, duplicateFileCount.ToString(), "Duplicates", "stat-value-warning");
                AddStatCard(_statsPanel, FormatBytes(wastedBytes), "Wasted", "stat-value-warning");
            }
        }

        private void AddStatCard(VisualElement parent, string value, string label, string valueClass)
        {
            var card = new VisualElement();
            card.AddToClassList("stat-card");

            var valueLbl = new Label(value);
            valueLbl.AddToClassList(valueClass);
            card.Add(valueLbl);

            var labelLbl = new Label(label);
            labelLbl.AddToClassList("stat-label");
            card.Add(labelLbl);

            parent.Add(card);
        }

        // Scan Diff Banner (Overview Tab)
        private void BuildDiffBanner()
        {
            _diffBanner = new VisualElement();
            _diffBanner.AddToClassList("diff-banner");
            _diffBanner.style.display = DisplayStyle.None;
            _overviewTabContent.Add(_diffBanner);
        }

        private void UpdateDiffBanner()
        {
            _diffBanner.Clear();
            _diffBanner.style.display = DisplayStyle.None;

            if (_scanResult == null || _previousScan == null) return;

            int prevCount = _previousScan.UnusedAssetCount;
            long prevSize = _previousScan.UnusedSizeBytes;
            int curCount = _scanResult.UnusedAssetCount;
            long curSize = _scanResult.UnusedSizeBytes;

            int countDelta = curCount - prevCount;
            long sizeDelta = curSize - prevSize;

            // Determine new and resolved assets by comparing path lists
            var prevPaths = new HashSet<string>(_previousScan.UnusedAssetPaths ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
            var curPaths = new HashSet<string>(_scanResult.UnusedAssets.Select(a => a.Path), StringComparer.OrdinalIgnoreCase);

            int newUnused = curPaths.Count(p => !prevPaths.Contains(p));
            int resolved = prevPaths.Count(p => !curPaths.Contains(p));

            _diffBanner.style.display = DisplayStyle.Flex;

            // Header
            var header = new Label($"Changes since last scan ({_previousScan.ScanTimestamp})");
            header.AddToClassList("diff-banner-header");
            _diffBanner.Add(header);

            // Summary row
            var summary = new VisualElement();
            summary.AddToClassList("diff-banner-row");

            string countText = countDelta == 0 ? "No change in count" : countDelta > 0 ? $"\u25B2 {countDelta} more unused assets" : $"\u25BC {Math.Abs(countDelta)} fewer unused assets";

            string sizeText = sizeDelta == 0 ? "" : sizeDelta > 0 ? $" (+{FormatBytes(sizeDelta)})" : $" (-{FormatBytes(Math.Abs(sizeDelta))})";

            var countLabel = new Label(countText + sizeText);
            countLabel.style.color = countDelta > 0 ? new Color(0.85f, 0.4f, 0.4f) : countDelta < 0 ? new Color(0.4f, 0.75f, 0.4f) : new Color(0.6f, 0.6f, 0.6f);
            countLabel.style.fontSize = 12;
            summary.Add(countLabel);
            _diffBanner.Add(summary);

            // New / Resolved counts
            if (newUnused > 0 || resolved > 0)
            {
                var detailRow = new VisualElement();
                detailRow.AddToClassList("diff-banner-row");

                if (newUnused > 0)
                {
                    var newLbl = new Label($"- {newUnused} new unused asset(s)");
                    newLbl.style.color = new Color(0.85f, 0.55f, 0.35f);
                    newLbl.style.fontSize = 11;
                    detailRow.Add(newLbl);
                }

                if (resolved > 0)
                {
                    var resolvedLbl = new Label($"- {resolved} resolved (no longer unused)");
                    resolvedLbl.style.color = new Color(0.45f, 0.75f, 0.45f);
                    resolvedLbl.style.fontSize = 11;
                    detailRow.Add(resolvedLbl);
                }

                _diffBanner.Add(detailRow);
            }
        }

        // Filter Bar
        private void BuildFilterBar()
        {
            var filterBar = new VisualElement();
            filterBar.AddToClassList("filter-bar");

            // Search field
            var searchField = new TextField();
            searchField.AddToClassList("search-field");
            searchField.value = "";
            searchField.tooltip = "Search by asset name or path";

            // Add placeholder text via a label
            var placeholder = new Label("Search assets...");
            placeholder.style.color = new Color(0.5f, 0.5f, 0.5f, 0.6f);
            placeholder.style.position = Position.Absolute;
            placeholder.style.left = 4;
            placeholder.style.top = 2;
            placeholder.pickingMode = PickingMode.Ignore;
            searchField.Add(placeholder);

            searchField.RegisterValueChangedCallback(evt =>
            {
                _searchQuery = evt.newValue ?? "";
                placeholder.style.display = string.IsNullOrEmpty(_searchQuery) ? DisplayStyle.Flex : DisplayStyle.None;
                ApplyFiltersAndSort();
            });

            filterBar.Add(searchField);

            // Category filter dropdown
            var categories = new List<string> { "All Categories" };
            categories.AddRange(Enum.GetValues(typeof(AssetCategory))
                    .Cast<AssetCategory>()
                    .Select(c => AssetCategoryResolver.GetDisplayName(c)));

            _categoryDropdown = new DropdownField("Category:", categories, 0);
            _categoryDropdown.AddToClassList("filter-dropdown");
            _categoryDropdown.RegisterValueChangedCallback(evt =>
            {
                if (evt.newValue == "All Categories")
                {
                    _filterCategory = (AssetCategory)(-1);
                }

                else
                {
                    // Find the enum value by display name
                    foreach (AssetCategory cat in Enum.GetValues(typeof(AssetCategory)))
                    {
                        if (AssetCategoryResolver.GetDisplayName(cat) == evt.newValue)
                        {
                            _filterCategory = cat;
                            break;
                        }
                    }
                }

                ApplyFiltersAndSort();
            });

            filterBar.Add(_categoryDropdown);

            // Show only unused toggle
            var unusedToggle = new Toggle("Unused Only");
            unusedToggle.value = _showOnlyUnused;
            unusedToggle.tooltip = "Show only unused assets";

            unusedToggle.RegisterValueChangedCallback(evt =>
            {
                _showOnlyUnused = evt.newValue;
                ApplyFiltersAndSort();
            });

            filterBar.Add(unusedToggle);

            _assetsTabContent.Add(filterBar);
        }

        // Category Breakdown (Overview Tab)
        private void BuildCategoryBreakdown()
        {
            _categoryBreakdown = new VisualElement();
            _categoryBreakdown.AddToClassList("category-breakdown");
            _categoryBreakdown.style.display = DisplayStyle.None;
            _categoryBreakdown.style.flexGrow = 1;
            _categoryBreakdown.style.flexShrink = 1;

            // Section header
            var header = new Label("Size Breakdown by Category");
            header.AddToClassList("category-breakdown-header");
            _categoryBreakdown.Add(header);

            // Scrollable content for category rows
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexGrow = 1;

            _categoryContent = new VisualElement();
            _categoryContent.style.flexDirection = FlexDirection.Column;
            scroll.Add(_categoryContent);

            _categoryBreakdown.Add(scroll);
            _overviewTabContent.Add(_categoryBreakdown);
        }

        private void UpdateCategoryBreakdown()
        {
            _categoryContent.Clear();

            if (_scanResult == null || _scanResult.UnusedAssetCount == 0)
            {
                _categoryBreakdown.style.display = DisplayStyle.None;
                return;
            }

            _categoryBreakdown.style.display = DisplayStyle.Flex;

            var breakdown = _scanResult.GetCategorySizeBreakdown();
            long maxSize = breakdown.Count > 0 ? breakdown[0].TotalSizeBytes : 1;

            // Update the section header with summary
            long totalUnusedSize = breakdown.Sum(b => b.TotalSizeBytes);
            int totalUnusedCount = breakdown.Sum(b => b.Count);
            var header = _categoryBreakdown.Q<Label>(
                className: "category-breakdown-header");

            if (header != null)
            {
                header.text = "Size Breakdown by Category - " + $"{breakdown.Count} types, {totalUnusedCount} assets, " + $"{FormatBytes(totalUnusedSize)} total";
            }

            foreach (var info in breakdown)
            {
                var row = new VisualElement();
                row.AddToClassList("category-row");
                row.tooltip = $"{AssetCategoryResolver.GetDisplayName(info.Category)}\n" + $"{info.Count} unused assets - {info.SizeFormatted}";

                // Make the row clickable to filter by that category
                AssetCategory capturedCategory = info.Category;

                row.RegisterCallback<MouseDownEvent>(evt =>
                {
                    // Double-click to filter by this category - because one click is for selection, two clicks are for commitment
                    if (evt.clickCount == 2)
                    {
                        _filterCategory = capturedCategory;

                        if (_categoryDropdown != null)
                        {
                            _categoryDropdown.SetValueWithoutNotify(
                                AssetCategoryResolver.GetDisplayName(capturedCategory));
                        }

                        ApplyFiltersAndSort();
                        SwitchToTab(0);
                        UpdateStatus($"Filtered to: {AssetCategoryResolver.GetDisplayName(capturedCategory)}");
                    }
                });
                row.style.cursor = StyleKeyword.Initial;

                var nameLbl = new Label(AssetCategoryResolver.GetDisplayName(info.Category));
                nameLbl.AddToClassList("category-name");
                row.Add(nameLbl);

                // Bar
                var barBg = new VisualElement();
                barBg.AddToClassList("category-bar-background");

                var barFill = new VisualElement();
                barFill.AddToClassList("category-bar-fill");
                float percentage = maxSize > 0 ? (float)info.TotalSizeBytes / maxSize * 100f : 0f;
                barFill.style.width = new Length(percentage, LengthUnit.Percent);

                // Colour based on category
                barFill.style.backgroundColor = GetCategoryColor(info.Category);

                barBg.Add(barFill);
                row.Add(barBg);

                var countLbl = new Label($"{info.Count}");
                countLbl.AddToClassList("category-count");
                row.Add(countLbl);

                var sizeLbl = new Label(info.SizeFormatted);
                sizeLbl.AddToClassList("category-size");
                row.Add(sizeLbl);

                _categoryContent.Add(row);
            }
        }

        private Color GetCategoryColor(AssetCategory category)
        {
            return category switch
            {
                AssetCategory.Texture      => new Color(0.55f, 0.35f, 0.75f, 0.8f),
                AssetCategory.Material     => new Color(0.35f, 0.65f, 0.55f, 0.8f),
                AssetCategory.Model        => new Color(0.65f, 0.50f, 0.30f, 0.8f),
                AssetCategory.Audio        => new Color(0.40f, 0.55f, 0.75f, 0.8f),
                AssetCategory.Prefab       => new Color(0.30f, 0.60f, 0.80f, 0.8f),
                AssetCategory.Animation    => new Color(0.75f, 0.45f, 0.45f, 0.8f),
                AssetCategory.Scene        => new Color(0.60f, 0.70f, 0.30f, 0.8f),
                AssetCategory.Shader       => new Color(0.50f, 0.70f, 0.70f, 0.8f),
                AssetCategory.Font         => new Color(0.65f, 0.55f, 0.70f, 0.8f),
                AssetCategory.Video        => new Color(0.70f, 0.40f, 0.60f, 0.8f),
                AssetCategory.StyleSheet   => new Color(0.35f, 0.70f, 0.70f, 0.8f),
                AssetCategory.UIDocument   => new Color(0.45f, 0.65f, 0.80f, 0.8f),
                _                          => new Color(0.45f, 0.55f, 0.65f, 0.8f),
            };
        }

        // Duplicate Asset Detection (Overview Tab)
        /// <summary>
        /// Builds the duplicate section container on the Overview tab.
        /// The actual content is populated by UpdateDuplicateSection after a scan.
        /// </summary>
        private void BuildDuplicateSection()
        {
            _duplicateSection = new VisualElement();
            _duplicateSection.AddToClassList("duplicate-section");
            _duplicateSection.style.display = DisplayStyle.None;
            _duplicateSection.style.flexGrow = 1;
            _duplicateSection.style.flexShrink = 1;

            var header = new Label("Duplicate Assets");
            header.AddToClassList("category-breakdown-header");
            _duplicateSection.Add(header);

            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexGrow = 1;

            _duplicateContent = new VisualElement();
            _duplicateContent.style.flexDirection = FlexDirection.Column;
            scroll.Add(_duplicateContent);

            _duplicateSection.Add(scroll);
            _overviewTabContent.Add(_duplicateSection);
        }

        /// <summary>
        /// Finds duplicate assets by computing MD5 hashes of all scanned files. <br></br>
        /// Groups with 2+ identical hashes are flagged as duplicates.
        /// </summary>
        private void FindDuplicates()
        {
            lock (_duplicateGroupsLock)
            {
                _duplicateGroups.Clear();
            }

            if (_scanResult == null) return;

            var hashMap = new Dictionary<string, List<AssetInfo>>();

            foreach (var asset in _scanResult.AllAssets)
            {
                try
                {
                    string fullPath = Path.GetFullPath(asset.Path);
                    if (!File.Exists(fullPath)) continue;

                    // Skip very large files (>100 MB) to avoid blocking the editor
                    var fileInfo = new FileInfo(fullPath);
                    if (fileInfo.Length > 100 * 1024 * 1024) continue;

                    string hash;
                    using (var md5 = MD5.Create())
                    using (var stream = File.OpenRead(fullPath))
                    {
                        byte[] hashBytes = md5.ComputeHash(stream);
                        hash = BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
                    }

                    if (!hashMap.ContainsKey(hash))
                    {
                        hashMap[hash] = new List<AssetInfo>();
                    }

                    hashMap[hash].Add(asset);
                }

                catch (Exception)
                {
                    // Skip files we can't read
                }
            }

            // Only keep groups with 2+ entries (actual duplicates)
            lock (_duplicateGroupsLock)
            {
                foreach (var kvp in hashMap)
                {
                    if (kvp.Value.Count >= 2)
                    {
                        _duplicateGroups[kvp.Key] = kvp.Value;
                    }
                }
            }
        }

        /// <summary>
        /// Updates the duplicate section on the Overview tab with current duplicate data.
        /// Each group shows its hash, file count, wasted size, and clickable paths.
        /// </summary>
        private void UpdateDuplicateSection()
        {
            _duplicateContent.Clear();

            List<KeyValuePair<string, List<AssetInfo>>> duplicateGroupsSnapshot;
            int duplicateGroupCount;

            lock (_duplicateGroupsLock)
            {
                duplicateGroupsSnapshot = _duplicateGroups.ToList();
                duplicateGroupCount = _duplicateGroups.Count;
            }

            if (duplicateGroupCount == 0)
            {
                _duplicateSection.style.display = DisplayStyle.None;
                return;
            }

            _duplicateSection.style.display = DisplayStyle.Flex;

            // Update header with summary
            int totalDuplicateFiles = duplicateGroupsSnapshot.Sum(g => g.Value.Count);
            long totalWasted = duplicateGroupsSnapshot
                .Sum(g => g.Value.Skip(1).Sum(a => a.SizeBytes));

            var header = _duplicateSection.Q<Label>(className: "category-breakdown-header");

            if (header != null)
            {
                header.text = "Duplicate Assets - " + $"{duplicateGroupCount} groups, {totalDuplicateFiles} files, " + $"{FormatBytes(totalWasted)} wasted";
            }

            foreach (var kvp in duplicateGroupsSnapshot.OrderByDescending(g => g.Value.Skip(1).Sum(a => a.SizeBytes)))
            {
                string hash = kvp.Key;
                List<AssetInfo> group = kvp.Value;
                long wastedSize = group.Skip(1).Sum(a => a.SizeBytes);

                var groupContainer = new VisualElement();
                groupContainer.AddToClassList("duplicate-group");

                // Group header: truncated hash, count, wasted size
                var groupHeader = new Label($"# {hash.Substring(0, 12)}... - " + $"{group.Count} copies, {FormatBytes(wastedSize)} wasted");
                groupHeader.AddToClassList("duplicate-group-header");
                groupHeader.style.unityFontStyleAndWeight = FontStyle.Bold;
                groupHeader.style.fontSize = 11;
                groupHeader.style.color = new Color(0.85f, 0.70f, 0.25f);
                groupContainer.Add(groupHeader);

                // Individual file entries
                foreach (var asset in group)
                {
                    var item = new VisualElement();
                    item.AddToClassList("duplicate-item");
                    item.style.flexDirection = FlexDirection.Row;
                    item.style.paddingLeft = 16;

                    var pathLabel = new Label($"- {asset.Name} ({asset.SizeFormatted}) - {asset.Path}");
                    pathLabel.style.fontSize = 10;
                    pathLabel.style.color = new Color(0.65f, 0.65f, 0.65f);
                    pathLabel.tooltip = BuildRichTooltip(asset);
                    item.Add(pathLabel);

                    // Click to ping in Project window
                    AssetInfo capturedAsset = asset;
                    item.RegisterCallback<MouseDownEvent>(evt =>
                    {
                        if (evt.clickCount >= 1)
                        {
                            var obj = AssetDatabase.LoadMainAssetAtPath(capturedAsset.Path);
                            if (obj != null) EditorGUIUtility.PingObject(obj);
                        }
                    });

                    groupContainer.Add(item);
                }

                _duplicateContent.Add(groupContainer);
            }
        }

        // Quarantine Tab
        /// <summary>
        /// Builds the Quarantine tab content: warning banner, toolbar, list view, and empty state.
        /// Where assets go to think about what they've done.
        /// </summary>
        private void BuildQuarantineTab()
        {
            // Warning banner for aged quarantine items (hidden by default)
            _quarantineWarningBanner = new Label();
            _quarantineWarningBanner.AddToClassList("quarantine-age-warning");
            _quarantineWarningBanner.style.display = DisplayStyle.None;
            _quarantineWarningBanner.style.backgroundColor = new Color(0.6f, 0.45f, 0.1f, 0.3f);
            _quarantineWarningBanner.style.paddingLeft = 8;
            _quarantineWarningBanner.style.paddingRight = 8;
            _quarantineWarningBanner.style.paddingTop = 6;
            _quarantineWarningBanner.style.paddingBottom = 6;
            _quarantineWarningBanner.style.marginBottom = 4;
            _quarantineWarningBanner.style.color = new Color(0.95f, 0.80f, 0.30f);
            _quarantineWarningBanner.style.fontSize = 12;
            _quarantineTabContent.Add(_quarantineWarningBanner);

            // Quarantine-specific toolbar
            var toolbar = new VisualElement();
            toolbar.AddToClassList("quarantine-tab-toolbar");
            toolbar.style.flexDirection = FlexDirection.Row;
            toolbar.style.paddingTop = 4;
            toolbar.style.paddingBottom = 4;

            var restoreBtn = new Button(OnQuarantineRestoreSelectedClicked)
            { text = "Restore Selected", tooltip = "Restore selected quarantined assets to their original locations" };
            restoreBtn.AddToClassList("toolbar-button");
            restoreBtn.AddToClassList("restore-button");
            toolbar.Add(restoreBtn);

            var purgeBtn = new Button(OnQuarantinePurgeSelectedClicked)
            { text = "Purge Selected", tooltip = "Permanently delete selected quarantined assets" };
            purgeBtn.AddToClassList("toolbar-button");
            purgeBtn.AddToClassList("delete-button");
            toolbar.Add(purgeBtn);

            var purgeAllBtn = new Button(OnQuarantinePurgeAllClicked)
            { text = "Purge All Quarantine", tooltip = "Permanently delete ALL quarantined assets (nuclear option)" };
            purgeAllBtn.AddToClassList("toolbar-button");
            purgeAllBtn.AddToClassList("delete-button");
            toolbar.Add(purgeAllBtn);

            _quarantineTabContent.Add(toolbar);

            // Quarantine list view
            _quarantineListView = new MultiColumnListView();
            _quarantineListView.AddToClassList("asset-list");
            _quarantineListView.fixedItemHeight = 24;
            _quarantineListView.showAlternatingRowBackgrounds = AlternatingRowBackground.ContentOnly;
            _quarantineListView.selectionType = SelectionType.Multiple;
            _quarantineListView.sortingMode = ColumnSortingMode.Default;

            // Columns: Name, Original Path, Size, Quarantine Date, Age
            _quarantineListView.columns.Add(new Column
            {
                name = "q-name",
                title = "Name",
                width = 200,
                minWidth = 100,
                stretchable = true
            });

            _quarantineListView.columns.Add(new Column
            {
                name = "q-path",
                title = "Original Path",
                width = 300,
                minWidth = 100,
                stretchable = true
            });

            _quarantineListView.columns.Add(new Column
            {
                name = "q-size",
                title = "Size",
                width = 90,
                minWidth = 60
            });

            _quarantineListView.columns.Add(new Column
            {
                name = "q-date",
                title = "Quarantine Date",
                width = 140,
                minWidth = 100
            });

            _quarantineListView.columns.Add(new Column
            {
                name = "q-age",
                title = "Age (Days)",
                width = 100,
                minWidth = 60
            });

            // Cell creation and binding - Name
            _quarantineListView.columns["q-name"].makeCell = () =>
            {
                var label = new Label();
                label.style.overflow = Overflow.Hidden;
                label.style.textOverflow = TextOverflow.Ellipsis;
                label.style.unityTextAlign = TextAnchor.MiddleLeft;
                label.style.paddingLeft = 4;
                return label;
            };

            _quarantineListView.columns["q-name"].bindCell = (element, index) =>
            {
                if (index >= 0 && index < _quarantineEntries.Count)
                {
                    var label = (Label)element;
                    var entry = _quarantineEntries[index];
                    label.text = Path.GetFileName(entry.OriginalPath);
                    label.tooltip = entry.OriginalPath;
                }
            };

            // Original Path
            _quarantineListView.columns["q-path"].makeCell = () =>
            {
                var label = new Label();
                label.style.overflow = Overflow.Hidden;
                label.style.textOverflow = TextOverflow.Ellipsis;
                label.style.unityTextAlign = TextAnchor.MiddleLeft;
                label.style.paddingLeft = 4;
                label.style.color = new Color(0.55f, 0.55f, 0.55f);
                return label;
            };

            _quarantineListView.columns["q-path"].bindCell = (element, index) =>
            {
                if (index >= 0 && index < _quarantineEntries.Count)
                {
                    ((Label)element).text = _quarantineEntries[index].OriginalPath;
                }
            };

            // Size
            _quarantineListView.columns["q-size"].makeCell = () =>
            {
                var label = new Label();
                label.style.unityTextAlign = TextAnchor.MiddleRight;
                label.style.paddingRight = 4;
                return label;
            };

            _quarantineListView.columns["q-size"].bindCell = (element, index) =>
            {
                if (index >= 0 && index < _quarantineEntries.Count)
                {
                    ((Label)element).text = FormatBytes(_quarantineEntries[index].SizeBytes);
                }
            };

            // Quarantine Date
            _quarantineListView.columns["q-date"].makeCell = () =>
            {
                var label = new Label();
                label.style.unityTextAlign = TextAnchor.MiddleCenter;
                label.style.fontSize = 10;
                return label;
            };

            _quarantineListView.columns["q-date"].bindCell = (element, index) =>
            {
                if (index >= 0 && index < _quarantineEntries.Count)
                {
                    var label = (Label)element;
                    var entry = _quarantineEntries[index];

                    try
                    {
                        var date = DateTime.Parse(entry.QuarantineDate);
                        label.text = date.ToString("yyyy-MM-dd HH:mm");
                    }
                    catch
                    {
                        label.text = entry.QuarantineDate;
                    }
                }
            };

            // Age (Days) - with colour-coded warnings
            _quarantineListView.columns["q-age"].makeCell = () =>
            {
                var label = new Label();
                label.style.unityTextAlign = TextAnchor.MiddleCenter;
                label.style.unityFontStyleAndWeight = FontStyle.Bold;
                return label;
            };

            _quarantineListView.columns["q-age"].bindCell = (element, index) =>
            {
                if (index >= 0 && index < _quarantineEntries.Count)
                {
                    var label = (Label)element;
                    var entry = _quarantineEntries[index];
                    int ageDays = GetQuarantineAgeDays(entry);

                    label.text = ageDays.ToString();

                    // Colour the age based on how long the asset has been languishing
                    if (ageDays > QUARANTINE_DANGER_DAYS)
                    {
                        label.style.color = new Color(0.85f, 0.35f, 0.35f);
                        label.text = $"! {ageDays}";
                    }
                    else if (ageDays > QUARANTINE_WARNING_DAYS)
                    {
                        label.style.color = new Color(0.85f, 0.70f, 0.25f);
                        label.text = $"! {ageDays}";
                    }
                    else
                    {
                        label.style.color = new Color(0.6f, 0.6f, 0.6f);
                    }
                }
            };

            _quarantineTabContent.Add(_quarantineListView);

            // Empty state for quarantine tab
            _quarantineEmptyState = new VisualElement();
            _quarantineEmptyState.AddToClassList("empty-state");

            var emptyText = new Label("No quarantined assets");
            emptyText.AddToClassList("empty-state-text");
            _quarantineEmptyState.Add(emptyText);

            var emptyHint = new Label("Quarantined assets will appear here. Use 'Quarantine Selected' from the Assets tab to move assets to quarantine.");
            emptyHint.AddToClassList("empty-state-hint");
            _quarantineEmptyState.Add(emptyHint);

            _quarantineTabContent.Add(_quarantineEmptyState);
        }

        /// <summary>
        /// Refreshes the quarantine list from the QuarantineManager.
        /// </summary>
        private void RefreshQuarantineList()
        {
            _quarantineEntries = _scanOrchestrator.QuarantineManager.GetQuarantinedAssets()?.ToList() ?? new List<QuarantineEntry>();

            if (_quarantineListView != null)
            {
                _quarantineListView.itemsSource = _quarantineEntries;
                _quarantineListView.RefreshItems();
            }

            // Toggle empty state vs list
            bool hasEntries = _quarantineEntries.Count > 0;

            if (_quarantineListView != null)
                _quarantineListView.style.display = hasEntries ? DisplayStyle.Flex : DisplayStyle.None;

            if (_quarantineEmptyState != null)
                _quarantineEmptyState.style.display = hasEntries ? DisplayStyle.None : DisplayStyle.Flex;

            // Update age warning banner
            UpdateQuarantineWarningBanner();
        }

        /// <summary>
        /// Shows or hides the warning banner based on how many assets have overstayed their welcome.
        /// </summary>
        private void UpdateQuarantineWarningBanner()
        {
            if (_quarantineWarningBanner == null) return;

            int agedCount = _quarantineEntries.Count(e => GetQuarantineAgeDays(e) > QUARANTINE_WARNING_DAYS);

            if (agedCount > 0)
            {
                _quarantineWarningBanner.text = $"WARNING: {agedCount} asset(s) have been quarantined for over " + $"{QUARANTINE_WARNING_DAYS} days. Consider purging or restoring them.";
                _quarantineWarningBanner.style.display = DisplayStyle.Flex;
            }

            else
            {
                _quarantineWarningBanner.style.display = DisplayStyle.None;
            }
        }

        /// <summary>
        /// Calculates how many days an asset has been in quarantine.
        /// Time flies when you're sitting in a temp folder.
        /// </summary>
        private int GetQuarantineAgeDays(QuarantineEntry entry)
        {
            try
            {
                var date = DateTime.Parse(entry.QuarantineDate);
                return (int)(DateTime.Now - date).TotalDays;
            }

            catch
            {
                return 0;
            }
        }

        // Quarantine Tab Button Handlers
        private void OnQuarantineRestoreSelectedClicked()
        {
            var selectedIndices = _quarantineListView.selectedIndices.ToList();
            if (selectedIndices.Count == 0)
            {
                EditorUtility.DisplayDialog("No Selection", "Please select one or more quarantined assets to restore.",
                    "OK");
                return;
            }

            var selectedEntries = selectedIndices
                .Where(i => i >= 0 && i < _quarantineEntries.Count)
                .Select(i => _quarantineEntries[i])
                .ToList();

            // Build AssetInfo list from quarantine entries for the RestoreAssets API
            var assetsToRestore = selectedEntries.Select(e => new AssetInfo
            {
                Path = e.OriginalPath,
                GUID = e.AssetGUID
            }).ToList();

            int count = _scanOrchestrator.QuarantineManager.RestoreAssets(assetsToRestore);
            _toastService.Show($"Restored {count} asset(s) from quarantine.", ToastType.Success);
            RefreshQuarantineList();
        }

        private async void OnQuarantinePurgeSelectedClicked()
        {
            var selectedIndices = _quarantineListView.selectedIndices.ToList();

            if (selectedIndices.Count == 0)
            {
                EditorUtility.DisplayDialog("No Selection", "Please select one or more quarantined assets to purge.",
                    "OK");
                return;
            }

            var selectedEntries = selectedIndices
                .Where(i => i >= 0 && i < _quarantineEntries.Count)
                .Select(i => _quarantineEntries[i])
                .ToList();

            long totalSize = selectedEntries.Sum(e => e.SizeBytes);

            bool confirm = EditorUtility.DisplayDialog("Confirm Purge", $"Permanently delete {selectedEntries.Count} quarantined asset(s) " + $"({FormatBytes(totalSize)})?\n\nThis action CANNOT be undone!", "Purge Permanently", "Cancel");

            if (!confirm) return;

            var assetsToPurge = selectedEntries.Select(e => new AssetInfo
            {
                Path = e.QuarantinePath,
                GUID = e.AssetGUID
            }).ToList();

            int count = await _scanOrchestrator.QuarantineManager.PermanentlyDelete(assetsToPurge);
            _toastService.Show($"Purged {count} asset(s) from quarantine.", ToastType.Success);
            RefreshQuarantineList();
        }

        private async void OnQuarantinePurgeAllClicked()
        {
            if (_quarantineEntries.Count == 0)
            {
                EditorUtility.DisplayDialog("Nothing to Purge", "There are no quarantined assets to purge.", "OK");
                return;
            }

            long totalSize = _quarantineEntries.Sum(e => e.SizeBytes);

            bool confirm = EditorUtility.DisplayDialog("Purge ALL Quarantine", $"Permanently delete ALL {_quarantineEntries.Count} quarantined asset(s) " + $"({FormatBytes(totalSize)})?\n\nThis is the nuclear option. " + $"This action CANNOT be undone!",
                "Purge Everything", "Cancel");

            if (!confirm) return;

            await _scanOrchestrator.QuarantineManager.ClearQuarantine();
            _toastService.Show("Purged all quarantined assets. Fresh start!", ToastType.Success);
            RefreshQuarantineList();
        }

        // Asset List (MultiColumnListView)
        private void BuildAssetList()
        {
            _assetListContainer = new VisualElement();
            _assetListContainer.AddToClassList("asset-list-container");

            _assetListView = new MultiColumnListView();
            _assetListView.AddToClassList("asset-list");
            _assetListView.fixedItemHeight = 24;
            _assetListView.showAlternatingRowBackgrounds =
                AlternatingRowBackground.ContentOnly;
            _assetListView.selectionType = SelectionType.Multiple;
            _assetListView.sortingMode = ColumnSortingMode.Default;

            // Define columns
            _assetListView.columns.Add(new Column
            {
                name = "name",
                title = "Asset Name",
                width = 250,
                minWidth = 100,
                stretchable = true
            });

            _assetListView.columns.Add(new Column
            {
                name = "category",
                title = "Type",
                width = 120,
                minWidth = 80
            });

            _assetListView.columns.Add(new Column
            {
                name = "size",
                title = "Size",
                width = 90,
                minWidth = 60
            });

            _assetListView.columns.Add(new Column
            {
                name = "refs",
                title = "Refs",
                width = 50,
                minWidth = 40
            });

            _assetListView.columns.Add(new Column
            {
                name = "deps",
                title = "Deps",
                width = 50,
                minWidth = 40
            });

            _assetListView.columns.Add(new Column
            {
                name = "safety",
                title = "Safety",
                width = 80,
                minWidth = 60
            });

            _assetListView.columns.Add(new Column
            {
                name = "readonly",
                title = "R/O",
                width = 40,
                minWidth = 36,
                makeHeader = () =>
                {
                    var header = new Label("R/O");
                    header.style.unityTextAlign = TextAnchor.MiddleCenter;
                    header.tooltip = "Read-Only: file has the OS read-only attribute set. " + "Quarantine will strip this automatically, but it is shown " + "here so you can see which assets need the extra step.";
                    return header;
                }
            });

            _assetListView.columns.Add(new Column
            {
                name = "vcs",
                title = "VCS",
                width = 90,
                minWidth = 70,
                optional = true,
                visible = Provider.isActive
            });

            _assetListView.columns.Add(new Column
            {
                name = "path",
                title = "Path",
                width = 300,
                minWidth = 100,
                stretchable = true
            });

            // Cell creation and binding
            _assetListView.columns["name"].makeCell = () =>
            {
                var label = new Label();
                label.style.overflow = Overflow.Hidden;
                label.style.textOverflow = TextOverflow.Ellipsis;
                label.style.unityTextAlign = TextAnchor.MiddleLeft;
                label.style.paddingLeft = 4;
                return label;
            };

            _assetListView.columns["name"].bindCell = (element, index) =>
            {
                if (index >= 0 && index < _displayedAssets.Count)
                {
                    var label = (Label)element;
                    var asset = _displayedAssets[index];
                    label.text = asset.Name;
                    label.tooltip = BuildRichTooltip(asset);
                }
            };

            _assetListView.columns["category"].makeCell = () =>
            {
                var label = new Label();
                label.style.unityTextAlign = TextAnchor.MiddleLeft;
                label.style.paddingLeft = 4;
                return label;
            };

            _assetListView.columns["category"].bindCell = (element, index) =>
            {
                if (index >= 0 && index < _displayedAssets.Count)
                {
                    ((Label)element).text = AssetCategoryResolver.GetDisplayName(_displayedAssets[index].Category);
                }
            };

            _assetListView.columns["size"].makeCell = () =>
            {
                var label = new Label();
                label.style.unityTextAlign = TextAnchor.MiddleRight;
                label.style.paddingRight = 4;
                return label;
            };

            _assetListView.columns["size"].bindCell = (element, index) =>
            {
                if (index >= 0 && index < _displayedAssets.Count)
                {
                    ((Label)element).text = _displayedAssets[index].SizeFormatted;
                }
            };

            _assetListView.columns["refs"].makeCell = () =>
            {
                var label = new Label();
                label.style.unityTextAlign = TextAnchor.MiddleCenter;
                return label;
            };

            _assetListView.columns["refs"].bindCell = (element, index) =>
            {
                if (index >= 0 && index < _displayedAssets.Count)
                {
                    var label = (Label)element;
                    int count = _displayedAssets[index].ReferenceCount;
                    label.text = count.ToString();
                    label.style.color = count == 0 ? new Color(0.8f, 0.4f, 0.4f) : new Color(0.6f, 0.75f, 0.6f);
                }
            };

            _assetListView.columns["deps"].makeCell = () =>
            {
                var label = new Label();
                label.style.unityTextAlign = TextAnchor.MiddleCenter;
                return label;
            };

            _assetListView.columns["deps"].bindCell = (element, index) =>
            {
                if (index >= 0 && index < _displayedAssets.Count)
                {
                    var label = (Label)element;
                    int count = _displayedAssets[index].DependencyCount;
                    label.text = count.ToString();
                    label.style.color = count == 0 ? new Color(0.55f, 0.55f, 0.55f) : new Color(0.55f, 0.65f, 0.80f);
                }
            };

            // Safety column
            _assetListView.columns["safety"].makeCell = () =>
            {
                var label = new Label();
                label.style.unityTextAlign = TextAnchor.MiddleCenter;
                label.style.unityFontStyleAndWeight = FontStyle.Bold;
                label.style.fontSize = 11;
                return label;
            };

            _assetListView.columns["safety"].bindCell = (element, index) =>
            {
                if (index >= 0 && index < _displayedAssets.Count)
                {
                    var label = (Label)element;
                    var asset = _displayedAssets[index];

                    label.RemoveFromClassList("safety-safe");
                    label.RemoveFromClassList("safety-caution");
                    label.RemoveFromClassList("safety-unsafe");
                    label.RemoveFromClassList("safety-unknown");

                    switch (asset.Safety)
                    {
                        case DeletionSafety.Safe:
                            label.text = "Safe";
                            label.AddToClassList("safety-safe");
                            label.tooltip = "No incoming references - safe to remove.";
                            break;

                        case DeletionSafety.Caution:
                            label.text = "Caution";
                            label.AddToClassList("safety-caution");
                            label.tooltip = "Referenced by Project Settings or Build Settings only.\n" + "Removing may affect project configuration.";
                            break;

                        case DeletionSafety.Unsafe:
                            label.text = "Unsafe";
                            label.AddToClassList("safety-unsafe");
                            label.tooltip = "Referenced by other project assets.\n" + "Removing WILL cause missing references.";
                            break;

                        default:
                            label.text = " -";
                            label.AddToClassList("safety-unknown");
                            label.tooltip = "Not yet analysed - run a scan first.";
                            break;
                    }
                }
            };

            _assetListView.columns["readonly"].makeCell = () =>
            {
                var label = new Label();
                label.style.unityTextAlign = TextAnchor.MiddleCenter;
                label.style.fontSize = 11;
                return label;
            };

            _assetListView.columns["readonly"].bindCell = (element, index) =>
            {
                if (index >= 0 && index < _displayedAssets.Count)
                {
                    var label = (Label)element;
                    var asset = _displayedAssets[index];

                    label.RemoveFromClassList("readonly-flag");

                    if (asset.IsReadOnly)
                    {
                        label.text = "R/O";
                        label.AddToClassList("readonly-flag");
                        label.tooltip = $"{asset.Name} has the OS read-only attribute set.\n" + "Quarantine will strip this automatically.";
                    }
                    else
                    {
                        label.text = "";
                        label.tooltip = "";
                    }
                }
            };

            _assetListView.columns["vcs"].makeCell = () =>
            {
                var label = new Label();
                label.style.unityTextAlign = TextAnchor.MiddleCenter;
                label.style.fontSize = 11;
                return label;
            };

            _assetListView.columns["vcs"].bindCell = (element, index) =>
            {
                if (index < 0 || index >= _displayedAssets.Count) return;
                var label = (Label)element;
                var asset = _displayedAssets[index];

                // Clear previous classes
                label.RemoveFromClassList("vcs-clean");
                label.RemoveFromClassList("vcs-local");
                label.RemoveFromClassList("vcs-other");
                label.RemoveFromClassList("vcs-locked");
                label.RemoveFromClassList("vcs-stale");
                label.RemoveFromClassList("vcs-unversioned");

                switch (asset.PerforceStatus)
                {
                    case VcsStatus.UpToDate:
                        label.text = ""; label.tooltip = "Synced"; break;
                    case VcsStatus.CheckedOutLocal:
                        label.text = "Checked Out";
                        label.AddToClassList("vcs-local");
                        label.tooltip = "Checked out by you."; break;
                    case VcsStatus.CheckedOutOther:
                        label.text = "In Use";
                        label.AddToClassList("vcs-other");
                        label.tooltip = $"Checked out by: {asset.VcsOtherUser}"; break;
                    case VcsStatus.LockedByOther:
                        label.text = "Locked";
                        label.AddToClassList("vcs-locked");
                        label.tooltip = $"Exclusively locked by: {asset.VcsOtherUser}\nQuarantine and delete are blocked."; break;
                    case VcsStatus.OutOfDate:
                        label.text = "Out of Date";
                        label.AddToClassList("vcs-stale");
                        label.tooltip = "Local file is behind depot head. Sync before modifying."; break;
                    case VcsStatus.Added:
                        label.text = "Added";
                        label.AddToClassList("vcs-local");
                        label.tooltip = "Scheduled for add — not yet submitted."; break;
                    case VcsStatus.Deleted:
                        label.text = "Deleted";
                        label.AddToClassList("vcs-other");
                        label.tooltip = "Scheduled for delete in Perforce."; break;
                    case VcsStatus.Unversioned:
                        label.text = "Local";
                        label.AddToClassList("vcs-unversioned");
                        label.tooltip = "Not tracked by Perforce."; break;
                    default:
                        label.text = ""; label.tooltip = ""; break;
                }
            };

            _assetListView.columns["path"].makeCell = () =>
            {
                var label = new Label();
                label.style.overflow = Overflow.Hidden;
                label.style.textOverflow = TextOverflow.Ellipsis;
                label.style.unityTextAlign = TextAnchor.MiddleLeft;
                label.style.paddingLeft = 4;
                label.style.color = new Color(0.55f, 0.55f, 0.55f);
                return label;
            };

            _assetListView.columns["path"].bindCell = (element, index) =>
            {
                if (index >= 0 && index < _displayedAssets.Count)
                {
                    var label = (Label)element;
                    var asset = _displayedAssets[index];
                    label.text = asset.Path;
                    label.tooltip = BuildRichTooltip(asset);
                }
            };

            // Handle column sorting
            _assetListView.columnSortingChanged += OnColumnSortingChanged;

            // Handle selection changed - show dependency info
            _assetListView.selectedIndicesChanged += OnSelectionChanged;

            // Handle double-click - ping asset in Project window
            _assetListView.RegisterCallback<MouseDownEvent>(evt =>
            {
                if (evt.clickCount == 2)
                {
                    OnAssetDoubleClicked();
                }
            });

            // Right-click context menu
            _assetListView.RegisterCallback<ContextClickEvent>(OnAssetContextMenu);

            _assetListContainer.Add(_assetListView);
            _splitContainer.Add(_assetListContainer);
        }

        // Right-Click Context Menu
        private void OnAssetContextMenu(ContextClickEvent evt)
        {
            var selected = GetSelectedAssets();
            if (selected.Count == 0) return;

            var menu = new GenericMenu();
            bool isSingle = selected.Count == 1;
            AssetInfo first = selected[0];

            string header = isSingle ? first.Name : $"{selected.Count} assets selected";

            // Disabled header showing what's selected
            menu.AddDisabledItem(new GUIContent(header));
            menu.AddSeparator("");

            // Ping / Select in Project
            if (isSingle)
            {
                menu.AddItem(new GUIContent("Ping in Project Window"), false, () =>
                    {
                        var obj = AssetDatabase.LoadMainAssetAtPath(first.Path);
                        if (obj != null) EditorGUIUtility.PingObject(obj);
                    });

                menu.AddItem(new GUIContent("Select in Project Window"), false, () =>
                    {
                        Selection.activeObject =
                            AssetDatabase.LoadMainAssetAtPath(first.Path);
                    });

                menu.AddItem(new GUIContent("Reveal in File Explorer"), false, () =>
                    {
                        EditorUtility.RevealInFinder(first.Path);
                    });

                menu.AddSeparator("");
            }

            // Quarantine / Restore - context-aware based on asset state
            long totalSize = selected.Sum(a => a.SizeBytes);
            bool hasRefs = selected.Any(a => a.ReferenceCount > 0);
            int quarantinedCount = selected.Count(a => a.IsQuarantined);
            int normalCount = selected.Count - quarantinedCount;

            if (normalCount > 0 && quarantinedCount == 0)
            {
                // All selected assets are NOT quarantined - show Quarantine only
                string quarantineLabel = hasRefs ? $"Quarantine ({FormatBytes(totalSize)}) - has references!" : $"Quarantine ({FormatBytes(totalSize)})";
                menu.AddItem(new GUIContent(quarantineLabel), false, OnQuarantineClicked);
            }

            else if (quarantinedCount > 0 && normalCount == 0)
            {
                // All selected assets ARE quarantined - show Restore only
                menu.AddItem(new GUIContent($"Restore from Quarantine ({quarantinedCount})"), false, OnRestoreClicked);
            }
            else
            {
                // Mixed selection - show both with counts
                string quarantineLabel = hasRefs ? $"Quarantine {normalCount} Asset(s) - has references!" : $"Quarantine {normalCount} Asset(s)";
                menu.AddItem(new GUIContent(quarantineLabel), false, OnQuarantineClicked);
                menu.AddItem(new GUIContent($"Restore {quarantinedCount} from Quarantine"), false, OnRestoreClicked);
            }

            // Delete
            string deleteLabel = hasRefs ? $"Delete Permanently ({selected.Count}) - has references!" : $"Delete Permanently ({selected.Count})";

            menu.AddItem(new GUIContent(deleteLabel), false, OnDeleteClicked);
            menu.AddSeparator("");

            // Whitelist options
            menu.AddItem(new GUIContent("Whitelist/Whitelist Asset(s)"), false, OnWhitelistClicked);

            if (isSingle) // #singlelife
            {
                // Whitelist the containing folder
                string folder = Path.GetDirectoryName(first.Path)?.Replace("\\", "/");

                if (!string.IsNullOrEmpty(folder))
                {
                    menu.AddItem(new GUIContent($"Whitelist/Whitelist Folder: {folder}"),
                        false, () =>
                        {
                            var config = WhitelistConfig.GetOrCreateConfig();
                            config.AddFolder(folder);
                            AssetDatabase.SaveAssets();
                            _toastService.Show($"Whitelisted folder: {folder}", ToastType.Info);
                            ApplyFiltersAndSort();
                        });
                }

                // Whitelist by extension
                if (!string.IsNullOrEmpty(first.Extension))
                {
                    menu.AddItem(new GUIContent($"Whitelist/Whitelist All '{first.Extension}' Files"),
                        false, () =>
                        {
                            var config = WhitelistConfig.GetOrCreateConfig();
                            config.AddExtension(first.Extension);
                            AssetDatabase.SaveAssets();
                            _toastService.Show($"Whitelisted extension: {first.Extension}", ToastType.Info);
                            ApplyFiltersAndSort();
                        });
                }
            }

            menu.AddSeparator("");

            // Copy path to clipboard
            if (isSingle)
            {
                menu.AddItem(new GUIContent("Copy Path to Clipboard"), false, () =>
                    {
                        GUIUtility.systemCopyBuffer = first.Path;
                        _toastService.Show($"Copied: {first.Path}", ToastType.Info);
                    });

                menu.AddItem(new GUIContent("Copy GUID to Clipboard"), false, () =>
                    {
                        GUIUtility.systemCopyBuffer = first.GUID;
                        _toastService.Show($"Copied GUID: {first.GUID}", ToastType.Info);
                    });
            }

            menu.ShowAsContext();
            evt.StopPropagation();
        }

        private void RefreshAssetList()
        {
            _assetListView.itemsSource = _displayedAssets;
            _assetListView.RefreshItems();
        }

        // Column Sorting
        private void OnColumnSortingChanged()
        {
            var sortedColumns = _assetListView.sortedColumns.ToList();

            if (sortedColumns.Count > 0)
            {
                var first = sortedColumns[0];
                _sortAscending = first.direction == SortDirection.Ascending;

                _sortColumn = first.columnName switch
                {
                    "name" => SortColumn.Name,
                    "category" => SortColumn.Category,
                    "size" => SortColumn.Size,
                    "refs" => SortColumn.References,
                    "deps" => SortColumn.Dependencies,
                    "safety" => SortColumn.Safety,
                    "path" => SortColumn.Path,
                    _ => SortColumn.Size
                };
            }

            // Persist sort preferences
            SavePreferences();

            ApplyFiltersAndSort();
        }

        // Dependency Side Panel
        private void BuildDependencyPanel()
        {
            _dependencyPanel = new VisualElement();
            _dependencyPanel.AddToClassList("dependency-side-panel");
            _dependencyPanel.style.display = DisplayStyle.None;

            // Panel header with asset name
            var header = new Label("Dependencies & References");
            header.AddToClassList("dependency-header");
            _dependencyPanel.Add(header);

            // Scrollable body for foldout sections
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexGrow = 1;
            scroll.name = "dep-scroll";
            _dependencyPanel.Add(scroll);

            _splitContainer.Add(_dependencyPanel);
        }

        private void ShowDependencyPanel(bool visible)
        {
            _dependencyPanel.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void UpdateDependencyPanel(AssetInfo asset)
        {
            var scroll = _dependencyPanel.Q<ScrollView>("dep-scroll");

            if (scroll == null) return;
            scroll.Clear();

            if (asset == null || _dependencyGraph == null)
            {
                ShowDependencyPanel(false);
                return;
            }

            ShowDependencyPanel(true);

            var header = _dependencyPanel.Q<Label>(className: "dependency-header");

            if (header != null)
                header.text = asset.Name;

            var previewObj = AssetDatabase.LoadMainAssetAtPath(asset.Path);

            if (previewObj != null)
            {
                var previewTexture = AssetPreview.GetAssetPreview(previewObj);
                if (previewTexture != null)
                {
                    _assetPreviewImage = new Image();
                    _assetPreviewImage.image = previewTexture;
                    _assetPreviewImage.AddToClassList("asset-preview-image");
                    _assetPreviewImage.style.width = 128;
                    _assetPreviewImage.style.height = 128;
                    _assetPreviewImage.style.alignSelf = Align.Center;
                    _assetPreviewImage.style.marginTop = 8;
                    _assetPreviewImage.style.marginBottom = 8;
                    _assetPreviewImage.style.borderTopLeftRadius = 4;
                    _assetPreviewImage.style.borderTopRightRadius = 4;
                    _assetPreviewImage.style.borderBottomLeftRadius = 4;
                    _assetPreviewImage.style.borderBottomRightRadius = 4;
                    scroll.Add(_assetPreviewImage);
                }

                else
                {
                    // Show a mini-preview using the icon instead
                    var miniThumb = AssetPreview.GetMiniThumbnail(previewObj);
                    if (miniThumb != null)
                    {
                        _assetPreviewImage = new Image();
                        _assetPreviewImage.image = miniThumb;
                        _assetPreviewImage.AddToClassList("asset-preview-image");
                        _assetPreviewImage.style.width = 64;
                        _assetPreviewImage.style.height = 64;
                        _assetPreviewImage.style.alignSelf = Align.Center;
                        _assetPreviewImage.style.marginTop = 8;
                        _assetPreviewImage.style.marginBottom = 8;
                        scroll.Add(_assetPreviewImage);
                    }
                }
            }

            // Forward dependencies - collapsible foldout
            var deps = _dependencyGraph.GetDirectDependencies(asset.Path);
            {
                var foldout = new Foldout();
                foldout.text = $"Dependencies ({deps.Count})";
                foldout.value = deps.Count > 0 && deps.Count <= 30;
                foldout.AddToClassList("dep-foldout");

                // Style the foldout label
                StyleFoldoutLabel(foldout, new Color(0.5f, 0.7f, 0.5f));

                if (deps.Count == 0)
                {
                    var none = new Label("None");
                    none.AddToClassList("dep-none-label");
                    foldout.Add(none);
                }
                else
                {
                    foreach (var dep in deps)
                    {
                        AddDependencyItem(foldout, dep, "\u2192");
                    }
                }

                scroll.Add(foldout);
            }

            // Reverse references - collapsible foldout
            var refs = _dependencyGraph.GetDirectReferences(asset.Path);
            var syntheticRefs = _dependencyGraph.GetSyntheticReferences(asset.Path);
            int totalRefCount = refs.Count + syntheticRefs.Count;
            {
                var foldout = new Foldout();
                foldout.text = $"Referenced By ({totalRefCount})";
                foldout.value = totalRefCount > 0 && totalRefCount <= 30;
                foldout.AddToClassList("dep-foldout");

                // Style the foldout label
                StyleFoldoutLabel(foldout, new Color(0.7f, 0.5f, 0.5f));

                if (totalRefCount == 0)
                {
                    var none = new Label("None");
                    none.AddToClassList("dep-none-label");
                    foldout.Add(none);
                }
                else
                {
                    // Real asset references (clickable)
                    foreach (var refAsset in refs)
                    {
                        AddDependencyItem(foldout, refAsset, "\u2190");
                    }

                    // Synthetic references (Script/, ProjectSettings/, BuildSettings/)
                    foreach (string synRef in syntheticRefs)
                    {
                        AddSyntheticReferenceItem(foldout, synRef);
                    }
                }

                scroll.Add(foldout);
            }

            if (Provider.isActive)
            {
                var vcsSection = new VisualElement();
                vcsSection.AddToClassList("detail-section");

                var vcsHeader = new Label("Version Control");
                vcsHeader.AddToClassList("detail-section-header");
                vcsSection.Add(vcsHeader);

                var vcsStatusLabel = new Label(VcsStatusText(asset));
                vcsStatusLabel.AddToClassList("detail-row");
                vcsSection.Add(vcsStatusLabel);

                // Only show Checkout button if asset is synced (UpToDate) and not already checked out
                if (asset.PerforceStatus == VcsStatus.UpToDate || asset.PerforceStatus == VcsStatus.Unknown)
                {
                    Button checkoutBtn = null;
                    checkoutBtn = new Button(() =>
                        {
                            checkoutBtn.SetEnabled(false);
                            string originalText = checkoutBtn.text;
                            checkoutBtn.text = "Checking out...";

                            try
                            {
                                var task = Provider.Checkout(asset.Path, CheckoutMode.Asset);
                                task.Wait();

                                if (task.success)
                                {
                                    asset.PerforceStatus = VcsStatus.CheckedOutLocal;
                                    UpdateDependencyPanel(asset);   // refresh panel
                                    _assetListView.RefreshItems();  // refresh column
                                    _toastService.Show($"Checked out {asset.Name}.", ToastType.Success);
                                }
                                else
                                {
                                    _toastService.Show($"Checkout failed for {asset.Name}.", ToastType.Error);
                                    checkoutBtn.SetEnabled(true);
                                    checkoutBtn.text = originalText;
                                }
                            }
                            catch (Exception ex)
                            {
                                _toastService.Show($"Checkout failed for {asset.Name}: {ex.Message}", ToastType.Error);
                                checkoutBtn.SetEnabled(true);
                                checkoutBtn.text = originalText;
                            }
                        })
                        { text = "Check Out in Perforce", tooltip = "Check out this asset for editing in Perforce." };
                    checkoutBtn.AddToClassList("detail-action-button");
                    vcsSection.Add(checkoutBtn);
                }

                scroll.Add(vcsSection);
            }
        }

        private static string VcsStatusText(AssetInfo asset) => asset.PerforceStatus switch
        {
            VcsStatus.UpToDate => "Status: Synced",
            VcsStatus.CheckedOutLocal => "Status: Checked out by you",
            VcsStatus.CheckedOutOther => $"Status: Checked out by {asset.VcsOtherUser}",
            VcsStatus.LockedByOther => $"Status: Locked by {asset.VcsOtherUser} — operations blocked",
            VcsStatus.OutOfDate => "Status: Out of date — sync before modifying",
            VcsStatus.Added => "Status: Scheduled for add",
            VcsStatus.Deleted => "Status: Scheduled for delete",
            VcsStatus.Unversioned => "Status: Not tracked by Perforce",
            _ => "Status: Unknown (VCS not queried)"
        };

        /// <summary>
        /// Applies consistent styling to a dependency foldout toggle label.
        /// </summary>
        private void StyleFoldoutLabel(Foldout foldout, Color colour)
        {
            var toggle = foldout.Q<Toggle>();
            if (toggle != null)
            {
                var label = toggle.Q<Label>();
                if (label != null)
                {
                    label.style.fontSize = 11;
                    label.style.unityFontStyleAndWeight = FontStyle.Bold;
                    label.style.color = colour;
                }
            }
        }

        private void AddDependencyItem(VisualElement parent, AssetInfo asset,
            string directionSymbol)
        {
            var item = new VisualElement();
            item.AddToClassList("dependency-item");

            var dirLabel = new Label(directionSymbol);
            dirLabel.AddToClassList("dependency-direction");
            item.Add(dirLabel);

            var pathLabel = new Label($"{asset.Name} ({asset.SizeFormatted})");
            pathLabel.AddToClassList("dependency-path");
            pathLabel.tooltip = BuildRichTooltip(asset);
            item.Add(pathLabel);

            // Click to ping, double-click to select
            item.RegisterCallback<MouseDownEvent>(evt =>
            {
                if (evt.clickCount == 1)
                {
                    var obj = AssetDatabase.LoadMainAssetAtPath(asset.Path);
                    if (obj != null) EditorGUIUtility.PingObject(obj);
                }

                if (evt.clickCount == 2)
                {
                    Selection.activeObject = AssetDatabase.LoadMainAssetAtPath(asset.Path);
                }
            });

            parent.Add(item);
        }

        /// <summary>
        /// Adds a non-clickable synthetic reference item to the dependency panel.
        /// These are references injected by the scanner for things like C# scripts, ProjectSettings, and BuildSettings - stuff that references assets but isn't an asset itself.
        /// </summary>
        private void AddSyntheticReferenceItem(VisualElement parent, string syntheticRef)
        {
            var item = new VisualElement();
            item.AddToClassList("dependency-item");

            // Determine a prefix icon / label based on the reference type
            string icon;
            string displayLabel;
            string tooltip;

            if (syntheticRef.StartsWith("Script/"))
            {
                icon = "\u2190";
                // Strip the "Script/" prefix to show the actual script path
                displayLabel = syntheticRef.Substring("Script/".Length);
                tooltip = $"Referenced in C# script:\n{displayLabel}\n\n" +
                          "This asset is loaded via code (e.g. Resources.Load, " +
                          "AssetDatabase.LoadAssetAtPath, or Addressables).";
            }
            else if (syntheticRef.StartsWith("ProjectSettings/"))
            {
                icon = "\u2190";
                displayLabel = syntheticRef;
                tooltip = "Referenced by Unity Project Settings.\n" +
                          "This asset is configured at the engine level.";
            }
            else if (syntheticRef.StartsWith("BuildSettings/"))
            {
                icon = "\u2190";
                displayLabel = syntheticRef;
                tooltip = "Included in Build Settings (Scenes In Build).\n" +
                          "This scene will be included in the final build.";
            }
            else
            {
                icon = "\u2190";
                displayLabel = syntheticRef;
                tooltip = syntheticRef;
            }

            var dirLabel = new Label(icon);
            dirLabel.AddToClassList("dependency-direction");
            item.Add(dirLabel);

            var pathLabel = new Label(displayLabel);
            pathLabel.AddToClassList("dependency-path");
            pathLabel.tooltip = tooltip;

            // Dim synthetic refs slightly to distinguish from clickable asset refs
            pathLabel.style.color = new Color(0.6f, 0.7f, 0.85f, 0.9f);
            item.Add(pathLabel);

            // Script references are clickable - ping the script in the Project window
            if (syntheticRef.StartsWith("Script/"))
            {
                string scriptPath = syntheticRef.Substring("Script/".Length);
                item.RegisterCallback<MouseDownEvent>(evt =>
                {
                    if (evt.clickCount == 1)
                    {
                        var obj = AssetDatabase.LoadMainAssetAtPath(scriptPath);
                        if (obj != null) EditorGUIUtility.PingObject(obj);
                    }
                    if (evt.clickCount == 2)
                    {
                        Selection.activeObject =
                            AssetDatabase.LoadMainAssetAtPath(scriptPath);
                    }
                });
            }

            parent.Add(item);
        }

        // Status Bar
        private void BuildStatusBar()
        {
            var statusBar = new VisualElement();
            statusBar.AddToClassList("status-bar");

            _statusMessage = new Label("Ready. Click 'Scan Project' to begin.");
            _statusMessage.AddToClassList("status-message");
            statusBar.Add(_statusMessage);

            // Selection summary (shown when assets are selected)
            _selectionSummary = new Label("");
            _selectionSummary.AddToClassList("status-count");
            _selectionSummary.style.color = new Color(0.65f, 0.78f, 0.95f);
            statusBar.Add(_selectionSummary);

            _statusCount = new Label("");
            _statusCount.AddToClassList("status-count");
            statusBar.Add(_statusCount);

            _root.Add(statusBar);
        }

        private void UpdateStatus(string message)
        {
            if (_statusMessage != null) _statusMessage.text = message;
        }

        private void UpdateStatusCount()
        {
            if (_statusCount != null && _scanResult != null)
            {
                _statusCount.text = $"Showing {_displayedAssets.Count} of " + $"{_scanResult.UnusedAssetCount} unused assets";
            }
        }

        private void UpdateSelectionSummary()
        {
            if (_selectionSummary == null) return;

            var selected = GetSelectedAssets();
            if (selected.Count == 0)
            {
                _selectionSummary.text = "";
                return;
            }

            long totalSize = selected.Sum(a => a.SizeBytes);
            _selectionSummary.text = $"{selected.Count} selected - {FormatBytes(totalSize)}";
        }

        // Empty State
        private void BuildEmptyState()
        {
            _emptyState = new VisualElement();
            _emptyState.AddToClassList("empty-state");

            var text = new Label("No scan results yet");
            text.AddToClassList("empty-state-text");
            _emptyState.Add(text);

            var hint = new Label("Click 'Scan Project' in the toolbar to analyse your project assets.");
            hint.AddToClassList("empty-state-hint");
            _emptyState.Add(hint);

            _assetsTabContent.Add(_emptyState);
        }

        private void ShowEmptyState(bool visible)
        {
            _emptyState.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            _assetListContainer.style.display = visible ? DisplayStyle.None : DisplayStyle.Flex;
        }

        // Progress Overlay
        private void BuildProgressOverlay()
        {
            _progressOverlay = new VisualElement();
            _progressOverlay.AddToClassList("progress-overlay");
            _progressOverlay.style.display = DisplayStyle.None;

            _progressLabel = new Label("Scanning...");
            _progressLabel.AddToClassList("progress-label");
            _progressOverlay.Add(_progressLabel);

            var barContainer = new VisualElement();
            barContainer.AddToClassList("progress-bar-container");

            _progressBarFill = new VisualElement();
            _progressBarFill.AddToClassList("progress-bar-fill");
            _progressBarFill.style.width = new Length(0, LengthUnit.Percent);
            barContainer.Add(_progressBarFill);

            _progressOverlay.Add(barContainer);

            var cancelBtn = new Button(() => _scanOrchestrator.Scanner?.RequestCancel())
            { text = "Cancel" };
            cancelBtn.AddToClassList("toolbar-button");
            cancelBtn.style.marginTop = 12;
            _progressOverlay.Add(cancelBtn);

            _root.Add(_progressOverlay);
        }

        private void ShowProgress(bool visible)
        {
            _progressOverlay.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        // Toast Notification System
        /// <summary>
        /// Creates the <see cref="ToastService"/> and adds its container to the root, in the
        /// same position in the visual tree the inline toast container used to occupy.
        /// </summary>
        private void BuildToastContainer()
        {
            _toastService = new ToastService();
            _root.Add(_toastService.Container);
        }

        // ---- Keyboard Shortcuts ----

        /// <summary>
        /// Handles keyboard shortcuts registered on the root element.
        /// Because real developers don't need mice. Well, sometimes they do.
        /// </summary>
        private void OnKeyDown(KeyDownEvent evt)
        {
            bool isCtrlOrCmd = evt.ctrlKey || evt.commandKey;

            // Ctrl+A / Cmd+A: Select All
            if (isCtrlOrCmd && evt.keyCode == KeyCode.A)
            {
                SelectAllInCurrentList();
                evt.StopPropagation();
                return;
            }

            // Delete: Quarantine selected (with confirmation)
            if (evt.keyCode == KeyCode.Delete && !evt.shiftKey)
            {
                OnQuarantineClicked();
                evt.StopPropagation();
                return;
            }

            // Shift+Delete: Permanently delete selected (with confirmation)
            if (evt.keyCode == KeyCode.Delete && evt.shiftKey)
            {
                OnDeleteClicked();
                evt.StopPropagation();
                return;
            }

            // Ctrl+E / Cmd+E: Export
            if (isCtrlOrCmd && evt.keyCode == KeyCode.E)
            {
                ExportAs("csv");
                evt.StopPropagation();
                return;
            }

            // Ctrl+R / Cmd+R: Refresh/Re-scan
            if (isCtrlOrCmd && evt.keyCode == KeyCode.R)
            {
                OnScanClicked();
                evt.StopPropagation();
                return;
            }

            // F5: Refresh/Re-scan (alternative)
            if (evt.keyCode == KeyCode.F5)
            {
                OnScanClicked();
                evt.StopPropagation();
                return;
            }

            // Ctrl+Z / Cmd+Z: Undo last action
            if (isCtrlOrCmd && evt.keyCode == KeyCode.Z)
            {
                _undoController.OnUndoLastAction();
                evt.StopPropagation();
                return;
            }

            // Ctrl+1/2/3: Switch tabs - for the keyboard warriors among us
            if (isCtrlOrCmd && evt.keyCode == KeyCode.Alpha1)
            {
                SwitchToTab(1); // Overview
                evt.StopPropagation();
                return;
            }
            if (isCtrlOrCmd && evt.keyCode == KeyCode.Alpha2)
            {
                SwitchToTab(0); // Assets
                evt.StopPropagation();
                return;
            }
            if (isCtrlOrCmd && evt.keyCode == KeyCode.Alpha3)
            {
                SwitchToTab(2); // Quarantine
                evt.StopPropagation();
                return;
            }

            // Escape: Close accessibility panel if open
            if (evt.keyCode == KeyCode.Escape && _accessibilityPanelVisible)
            {
                ToggleAccessibilityPanel();
                evt.StopPropagation();
                return;
            }
        }


        /// <summary>
        /// Toggles select-all on the currently active list view.
        /// If everything is selected, deselect all. Otherwise, select all.
        /// </summary>
        private void OnSelectAllClicked()
        {
            SelectAllInCurrentList();
        }

        /// <summary>
        /// Selects all items in whichever list view is currently active.
        /// Also updates the Select All button text to reflect the new state.
        /// </summary>
        private void SelectAllInCurrentList()
        {
            if (_activeTab == 0 && _assetListView != null && _displayedAssets.Count > 0)
            {
                // Check if all are currently selected
                var currentSelection = _assetListView.selectedIndices.ToList();
                bool allSelected = currentSelection.Count == _displayedAssets.Count;

                if (allSelected)
                {
                    _assetListView.ClearSelection();
                }

                else
                {
                    var allIndices = Enumerable.Range(0, _displayedAssets.Count).ToList();
                    _assetListView.SetSelection(allIndices);
                }

                UpdateSelectAllButtonText();
            }

            else if (_activeTab == 2 && _quarantineListView != null && _quarantineEntries.Count > 0)
            {
                var currentSelection = _quarantineListView.selectedIndices.ToList();
                bool allSelected = currentSelection.Count == _quarantineEntries.Count;

                if (allSelected)
                {
                    _quarantineListView.ClearSelection();
                }

                else
                {
                    var allIndices = Enumerable.Range(0, _quarantineEntries.Count).ToList();
                    _quarantineListView.SetSelection(allIndices);
                }

                UpdateSelectAllButtonText();
            }
        }

        /// <summary>
        /// Updates the Select All button text based on the current selection state.
        /// </summary>
        private void UpdateSelectAllButtonText()
        {
            if (_selectAllBtn == null) return;

            if (_activeTab == 0 && _assetListView != null)
            {
                var currentSelection = _assetListView.selectedIndices.ToList();
                bool allSelected = _displayedAssets.Count > 0 && currentSelection.Count == _displayedAssets.Count;
                _selectAllBtn.text = allSelected ? "Deselect All" : "Select All";
            }

            else if (_activeTab == 2 && _quarantineListView != null)
            {
                var currentSelection = _quarantineListView.selectedIndices.ToList();
                bool allSelected = _quarantineEntries.Count > 0 && currentSelection.Count == _quarantineEntries.Count;
                _selectAllBtn.text = allSelected ? "Deselect All" : "Select All";
            }

            else
            {
                _selectAllBtn.text = "Select All";
            }
        }

        // Button Handlers

        /// <summary>
        /// Callback for Application.logMessageReceived during scans.
        /// Captures warnings and errors so we can display them in the log panel.
        /// </summary>
        private void OnLogMessageReceived(string condition, string stackTrace, LogType type)
        {
            // Only capture warnings and errors from our tool
            if (type == LogType.Warning || type == LogType.Error || type == LogType.Exception)
            {
                string prefix = type switch
                {
                    LogType.Error => "[ERROR]",
                    LogType.Exception => "[ERROR]",
                    LogType.Warning => "[WARNING]",
                    _ => "[INFO]"
                };

                // Flatten to a single line - stack traces and multi-line log messages contain embedded newlines which are illegal inside Open XML <v> elements and will corrupt sheet3 of the xlsx export. Replace with a pipe separator so the context is still readable without blowing up the file.
                string msg = condition.Replace("\r\n", " | ").Replace("\n", " | ").Replace("\r", " | ");

                // Truncate very long messages - brevity is the soul of debugging
                if (msg.Length > 200)
                    msg = msg.Substring(0, 200) + "...";

                _scanLog.Add($"{prefix} {msg}");
            }
        }

        private void OnScanClicked()
        {
            if (_scanBtn != null) _scanBtn.SetEnabled(false);

            _whitelist = WhitelistConfig.GetOrCreateConfig();

            ShowProgress(true);

            // Capture Unity console log messages during the scan - our little wiretap
            _scanLog.Clear();
            Application.logMessageReceived += OnLogMessageReceived;

            // Run scan (async, with progress bar)
            EditorApplication.delayCall += async () =>
            {
                _scanResult = await _scanOrchestrator.Scan(_whitelist);

                // Stop listening for log messages - we've heard enough
                Application.logMessageReceived -= OnLogMessageReceived;

                ShowProgress(false);

                if (_scanResult != null)
                {
                    _dependencyGraph = new DependencyGraphBuilder(_scanResult.AllAssets);

                    // Find duplicates after scan completes
                    System.Threading.Tasks.Task.Run(() => { FindDuplicates(); }).ContinueWith(_ =>
                    {
                        EditorApplication.delayCall += () =>
                        {
                            UpdateDuplicateSection();
                            UpdateStatsPanel();
                        };
                    });

                    UpdateDiffBanner();
                    UpdateCategoryBreakdown();
                    UpdateLogPanel();
                    ApplyFiltersAndSort();
                    ShowEmptyState(false);

                    // Refresh quarantine list since scan may have changed things
                    RefreshQuarantineList();

                    _toastService.Show($"Scan complete! {_scanResult.UnusedAssetCount} unused assets " + $"({FormatBytes(_scanResult.UnusedSizeBytes)}).", ToastType.Success);

                    UpdateStatus($"Showing {_displayedAssets.Count} of " + $"{_scanResult.UnusedAssetCount} unused assets");

                    // Save this scan as history for next time
                    SaveScanHistory(_scanResult);
                }
                else
                {
                    UpdateStatus("Scan was cancelled or failed.");
                    _toastService.Show("Scan was cancelled or failed.", ToastType.Warning);
                }

                if (_scanBtn != null) _scanBtn.SetEnabled(true);
            };
        }

        /// <summary>
        /// The kind of mutating action an incremental update is responding to. Distinguishes whether affected assets should be removed from, or re-added to, the live <see cref="_scanResult"/>/<see cref="_displayedAssets"/> working set.
        /// </summary>
        private enum IncrementalUpdateKind
        {
            Quarantine,
            Restore,
            Delete
        }

        /// <summary>
        /// Important fix!! updates the in-memory scan result and the visible asset list after a quarantine/restore/delete (or an undo of one of those) WITHOUT triggering a full project re-scan. A full re-scan rebuilds the entire asset map, which is inefficient and expensive and was doing exactly that in the past iterations. <br></br>
        /// </summary>
        /// <param name="affectedAssets">The AssetInfo records the action was performed on. For undo, these are constructed lightweight records (Path/GUID/SizeBytes only) - matching is therefore done by GUID/Path rather than by reference.</param>
        /// <param name="kind">Whether the assets left the live set (Quarantine/Delete) or re-entered it (Restore).</param>
        private void ApplyIncrementalUpdate(List<AssetInfo> affectedAssets, IncrementalUpdateKind kind)
        {
            if (_scanResult == null || affectedAssets == null || affectedAssets.Count == 0) return;

            bool Matches(AssetInfo candidate, AssetInfo affected)
            {
                if (!string.IsNullOrEmpty(affected.GUID) && !string.IsNullOrEmpty(candidate.GUID))
                    return string.Equals(candidate.GUID, affected.GUID, StringComparison.OrdinalIgnoreCase);

                return string.Equals(candidate.Path, affected.Path, StringComparison.OrdinalIgnoreCase);
            }

            if (kind == IncrementalUpdateKind.Quarantine || kind == IncrementalUpdateKind.Delete)
            {
                // Assets leave the live working set - remove them from AllAssets, UnusedAssets, and the currently-displayed (filtered) list.
                _scanResult.AllAssets.RemoveAll(candidate => affectedAssets.Any(a => Matches(candidate, a)));
                _scanResult.UnusedAssets.RemoveAll(candidate => affectedAssets.Any(a => Matches(candidate, a)));
                _displayedAssets.RemoveAll(candidate => affectedAssets.Any(a => Matches(candidate, a)));
            }
            else if (kind == IncrementalUpdateKind.Restore)
            {
                // Assets come back into the live working set. Reuse the AssetInfo data already available rather than re-deriving anything from disk. Guard against duplicates in case the asset was already present.
                foreach (var asset in affectedAssets)
                {
                    asset.IsQuarantined = false;

                    if (!_scanResult.AllAssets.Any(candidate => Matches(candidate, asset)))
                    {
                        _scanResult.AllAssets.Add(asset);
                    }

                    // Restored assets are re-added as unused candidates. ReferenceCount on these in-memory records reflects the last full scan.
                    if (!_scanResult.UnusedAssets.Any(candidate => Matches(candidate, asset)))
                    {
                        _scanResult.UnusedAssets.Add(asset);
                    }
                }

                // Re-run just the filtering/sorting/classification step so restored items land in the right place in the current view.
                ApplyFiltersAndSort();
            }

            // Re-render only the list view, not the whole scan pipeline.
            if (kind != IncrementalUpdateKind.Restore)
            {
                // Restore already called ApplyFiltersAndSort(), which itself calls RefreshAssetList()/UpdateStatusCount(); avoid doing it twice.
                RefreshAssetList();
                UpdateStatusCount();
            }

            // Stats/summary panels are recomputed from the already-in-memory _scanResult rather than from a fresh scan.
            UpdateStatsPanel();
            UpdateCategoryBreakdown();

            if (_displayedAssets.Count == 0 && _scanResult.UnusedAssetCount == 0)
            {
                ShowEmptyState(true);
            }
        }

        private void OnQuarantineClicked()
        {
            var selected = GetSelectedAssets();
            if (selected.Count == 0)
            {
                EditorUtility.DisplayDialog("No Selection", "Please select one or more assets to quarantine.", "OK");
                return;
            }

            long totalSize = selected.Sum(a => a.SizeBytes);

            // Check for assets that are still referenced by other assets
            var referenced = selected.Where(a => a.ReferenceCount > 0).ToList();
            string warningBlock = "";

            if (referenced.Count > 0)
            {
                int totalRefCount = referenced.Sum(a => a.ReferenceCount);

                warningBlock = $"\n\nWARNING - DEPENDENCY IMPACT: {referenced.Count} of these asset(s) are " + $"still referenced by {totalRefCount} other asset(s). " + $"Quarantining them may cause missing references:\n";

                foreach (var r in referenced.Take(5))
                {
                    // Show specific dependency impact per asset
                    string depDetail = "";

                    if (r.ReferencedBy != null && r.ReferencedBy.Count > 0)
                    {
                        var refNames = r.ReferencedBy.Take(3).Select(p => Path.GetFileName(p));

                        depDetail = $" (used by: {string.Join(", ", refNames)}";

                        if (r.ReferencedBy.Count > 3) depDetail += $" +{r.ReferencedBy.Count - 3} more";
                        depDetail += ")";
                    }

                    warningBlock += $" - {r.Name} - {r.ReferenceCount} reference(s){depDetail}\n";
                }

                if (referenced.Count > 5) warningBlock += $"  ... and {referenced.Count - 5} more\n";
            }

            bool confirm = EditorUtility.DisplayDialog(referenced.Count > 0 ? "Quarantine (Has References)" : "Confirm Quarantine",
                $"Move {selected.Count} asset(s) ({FormatBytes(totalSize)}) " + $"to quarantine?{warningBlock}" +
                $"\nYou can restore them later from the Quarantine tab.",
                referenced.Count > 0 ? "Quarantine Anyway" : "Quarantine", "Cancel");

            if (!confirm) return;

            // Record undo before action - your safety net is now armed
            _undoController.RecordUndoableAction(UndoActionType.Quarantine, selected);

            int count = _scanOrchestrator.QuarantineManager.QuarantineAssets(selected);
            _toastService.Show($"Quarantined {count} asset(s). Press Undo to reverse.", ToastType.Success);

            // Refresh quarantine list
            RefreshQuarantineList();

            // Incremental update instead of a full re-scan - see ApplyIncrementalUpdate for rationale (fix #6 / code review §2.5).
            ApplyIncrementalUpdate(selected, IncrementalUpdateKind.Quarantine);
        }

        private void OnRestoreClicked()
        {
            var selected = GetSelectedAssets();
            if (selected.Count == 0)
            {
                EditorUtility.DisplayDialog("No Selection", "Please select one or more quarantined assets to restore.", "OK");
                return;
            }

            // Record undo before restoring - in case you change your mind about changing your mind
            _undoController.RecordUndoableAction(UndoActionType.Restore, selected);

            int count = _scanOrchestrator.QuarantineManager.RestoreAssets(selected);
            _toastService.Show($"Restored {count} asset(s). Press Undo to reverse.", ToastType.Success);

            // Refresh quarantine list
            RefreshQuarantineList();

            // Incremental update instead of a full re-scan - see ApplyIncrementalUpdate.
            ApplyIncrementalUpdate(selected, IncrementalUpdateKind.Restore);
        }

        private async void OnDeleteClicked()
        {
            var selected = GetSelectedAssets();

            if (selected.Count == 0)
            {
                EditorUtility.DisplayDialog("No Selection", "Please select one or more assets to delete.", "OK");
                return;
            }

            long totalSize = selected.Sum(a => a.SizeBytes);

            // Check for assets that are still referenced by other assets
            var referenced = selected.Where(a => a.ReferenceCount > 0).ToList();
            string warningBlock = "";
            if (referenced.Count > 0)
            {
                int totalRefCount = referenced.Sum(a => a.ReferenceCount);
                warningBlock =
                    $"\n\nDANGER - DEPENDENCY IMPACT: {referenced.Count} of these asset(s) " +
                    $"are referenced by {totalRefCount} other asset(s)! " +
                    "Deleting them WILL break those references:\n";

                foreach (var r in referenced.Take(5))
                {
                    // Show specific assets that will break
                    string depDetail = "";
                    if (r.ReferencedBy != null && r.ReferencedBy.Count > 0)
                    {
                        var refNames = r.ReferencedBy.Take(3)
                            .Select(p => Path.GetFileName(p));
                        depDetail = $" (will break: {string.Join(", ", refNames)}";
                        if (r.ReferencedBy.Count > 3)
                            depDetail += $" +{r.ReferencedBy.Count - 3} more";
                        depDetail += ")";
                    }
                    warningBlock += $" - {r.Name} - {r.ReferenceCount} reference(s){depDetail}\n";
                }
                if (referenced.Count > 5)
                    warningBlock += $"  ... and {referenced.Count - 5} more\n";
            }

            bool confirm = EditorUtility.DisplayDialog(
                referenced.Count > 0 ? "Delete (HAS REFERENCES - RISKY)" : "Confirm Permanent Deletion",
                $"PERMANENTLY delete {selected.Count} asset(s) " + $"({FormatBytes(totalSize)})?{warningBlock}" +
                $"\nThis action CANNOT be undone! Consider using Quarantine instead.",
                referenced.Count > 0 ? "Delete Anyway (RISKY)" : "Delete Permanently",
                "Cancel");

            if (!confirm) return;

            int count = await _scanOrchestrator.QuarantineManager.PermanentlyDelete(selected);
            _toastService.Show($"Permanently deleted {count} asset(s). Gone forever.", ToastType.Success);

            // Refresh quarantine list
            RefreshQuarantineList();

            // Incremental update instead of a full re-scan - see ApplyIncrementalUpdate.
            ApplyIncrementalUpdate(selected, IncrementalUpdateKind.Delete);
        }

        private void OnWhitelistClicked()
        {
            var selected = GetSelectedAssets();
            if (selected.Count == 0)
            {
                EditorUtility.DisplayDialog("No Selection", "Please select one or more assets to whitelist.", "OK");
                return;
            }

            // Record undo before whitelisting - because sometimes you protect the wrong assets
            _undoController.RecordUndoableAction(UndoActionType.Whitelist, selected);

            var config = WhitelistConfig.GetOrCreateConfig();
            foreach (var asset in selected)
            {
                config.AddPath(asset.Path);
                asset.IsWhitelisted = true;
            }

            AssetDatabase.SaveAssets();
            _toastService.Show($"Added {selected.Count} asset(s) to whitelist. Press Undo to reverse.", ToastType.Info);
            ApplyFiltersAndSort();
        }

        private void OnSettingsClicked()
        {
            var config = WhitelistConfig.GetOrCreateConfig();
            Selection.activeObject = config;
            EditorGUIUtility.PingObject(config);
        }

        private void OnSelectionChanged(IEnumerable<int> selectedIndices)
        {
            var indices = selectedIndices.ToList();

            if (indices.Count == 1 && indices[0] >= 0
                && indices[0] < _displayedAssets.Count)
            {
                UpdateDependencyPanel(_displayedAssets[indices[0]]);
            }
            else
            {
                ShowDependencyPanel(false);
            }

            // Update selection summary in status bar
            UpdateSelectionSummary();

            // Update Select All button text
            UpdateSelectAllButtonText();
        }

        private void OnAssetDoubleClicked()
        {
            var indices = _assetListView.selectedIndices.ToList();
            if (indices.Count > 0 && indices[0] >= 0
                && indices[0] < _displayedAssets.Count)
            {
                var asset = _displayedAssets[indices[0]];
                var obj = AssetDatabase.LoadMainAssetAtPath(asset.Path);
                if (obj != null)
                {
                    EditorGUIUtility.PingObject(obj);
                    Selection.activeObject = obj;
                }
            }
        }

        // ---- Export (CSV, Excel) ----

        /// <summary>
        /// Routes the export to the correct writer based on format.
        /// Supports "csv" and "xlsx" because those are the only two formats that actually matter in the real world.
        /// </summary>
        private void ExportAs(string format)
        {
            if (_scanResult == null || _scanResult.UnusedAssets.Count == 0)
            {
                EditorUtility.DisplayDialog(
                    "No Data",
                    "Run a scan first before exporting.",
                    "OK");
                return;
            }

            string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HHmm");
            string defaultName = $"ProjectCleanup_{timestamp}.{format}";
            string savePath = EditorUtility.SaveFilePanel("Export Scan Report", "", defaultName, format);

            if (string.IsNullOrEmpty(savePath)) return;

            try
            {
                switch (format)
                {
                    case "csv":  _exportService.ExportToCsv(_scanResult, _displayedAssets, _scanLog, savePath); break;
                    case "xlsx": _exportService.ExportToXlsx(_scanResult, _displayedAssets, savePath); break;
                }

                _toastService.Show($"Exported {_displayedAssets.Count} assets to {format.ToUpper()}.", ToastType.Success);
                Debug.Log($"[Project Cleanup Utility] Exported report to: {savePath}");
                EditorUtility.RevealInFinder(savePath);
            }

            catch (Exception ex)
            {
                Debug.LogError($"[Project Cleanup Utility] {format.ToUpper()} export failed: {ex.Message}");
                _toastService.Show($"Export failed: {ex.Message}", ToastType.Error);
                EditorUtility.DisplayDialog("Export Failed", ex.Message, "OK");
            }
        }

        // Scan History (Persistence & Diff)
        private string ScanHistoryPath
        {
            get
            {
                string projectDir = Path.GetDirectoryName(Application.dataPath) ?? ".";

                return Path.Combine(projectDir, "Library", SCAN_HISTORY_FILENAME);
            }
        }

        private void SaveScanHistory(ScanResult result)
        {
            try
            {
                var entry = new ScanHistoryEntry
                {
                    ScanTimestamp = result.ScanTimestamp.ToString("yyyy-MM-dd HH:mm"), // originally formatted in US date style, changed it to UK style
                    UnusedAssetCount = result.UnusedAssetCount,
                    UnusedSizeBytes = result.UnusedSizeBytes,
                    TotalAssetCount = result.TotalAssetCount,
                    UnusedAssetPaths = result.UnusedAssets.Select(a => a.Path).ToList()
                };

                string json = JsonUtility.ToJson(entry, prettyPrint: true);
                File.WriteAllText(ScanHistoryPath, json);
                _previousScan = entry;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Project Cleanup Utility] Could not save scan history: {ex.Message}");
            }
        }

        private ScanHistoryEntry LoadScanHistory()
        {
            try
            {
                string path = ScanHistoryPath;

                if (File.Exists(path))
                {
                    string json = File.ReadAllText(path);
                    return JsonUtility.FromJson<ScanHistoryEntry>(json);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Project Cleanup Utility] Could not load scan history: {ex.Message}");
            }
            return null;
        }

        // EditorPrefs Persistence
        /// <summary>
        /// Loads user preferences from EditorPrefs.
        /// Because nobody wants to re-configure their favourite sort column every session.
        /// </summary>
        private void LoadPreferences()
        {
            _sortColumn = (SortColumn)EditorPrefs.GetInt(PREF_SORT_COLUMN, (int)SortColumn.Size);
            _sortAscending = EditorPrefs.GetBool(PREF_SORT_ASCENDING, false);
            _filterCategory = (AssetCategory)EditorPrefs.GetInt(PREF_FILTER_CATEGORY, -1);
            _showOnlyUnused = EditorPrefs.GetBool(PREF_SHOW_ONLY_UNUSED, true);
            _activeTab = EditorPrefs.GetInt(PREF_LAST_ACTIVE_TAB, 1);

            // Accessibility preferences now live on AccessibilityController.
            _accessibilityController.LoadPreferences();
        }

        /// <summary>
        /// Saves current user preferences to EditorPrefs.
        /// </summary>
        private void SavePreferences()
        {
            EditorPrefs.SetInt(PREF_SORT_COLUMN, (int)_sortColumn);
            EditorPrefs.SetBool(PREF_SORT_ASCENDING, _sortAscending);
            EditorPrefs.SetInt(PREF_FILTER_CATEGORY, (int)_filterCategory);
            EditorPrefs.SetBool(PREF_SHOW_ONLY_UNUSED, _showOnlyUnused);
            EditorPrefs.SetInt(PREF_LAST_ACTIVE_TAB, _activeTab);

            // Accessibility preferences now live on AccessibilityController.
            _accessibilityController.SavePreferences();

            // Save dependency panel width if it has been set
            if (_dependencyPanel != null && _dependencyPanel.resolvedStyle.width > 0)
            {
                EditorPrefs.SetFloat(PREF_DEPENDENCY_PANEL_WIDTH, _dependencyPanel.resolvedStyle.width);
            }
        }

        // Filtering & Sorting
        private void ApplyFiltersAndSort()
        {
            if (_scanResult == null) return;

            IEnumerable<AssetInfo> source = _showOnlyUnused ? _scanResult.UnusedAssets : _scanResult.AllAssets;

            // Apply search filter
            if (!string.IsNullOrEmpty(_searchQuery))
            {
                string query = _searchQuery.ToLowerInvariant();
                source = source.Where(a => a.Name.ToLowerInvariant().Contains(query) || a.Path.ToLowerInvariant().Contains(query));
            }

            // Apply category filter
            if ((int)_filterCategory != -1)
            {
                source = source.Where(a => a.Category == _filterCategory);
            }

            // Apply sorting
            source = _sortColumn switch
            {
                SortColumn.Name => _sortAscending
                    ? source.OrderBy(a => a.Name)
                    : source.OrderByDescending(a => a.Name),

                SortColumn.Category => _sortAscending
                    ? source.OrderBy(a => a.Category)
                    : source.OrderByDescending(a => a.Category),

                SortColumn.Size => _sortAscending
                    ? source.OrderBy(a => a.SizeBytes)
                    : source.OrderByDescending(a => a.SizeBytes),

                SortColumn.References => _sortAscending
                    ? source.OrderBy(a => a.ReferenceCount)
                    : source.OrderByDescending(a => a.ReferenceCount),

                SortColumn.Dependencies => _sortAscending
                    ? source.OrderBy(a => a.DependencyCount)
                    : source.OrderByDescending(a => a.DependencyCount),

                SortColumn.Safety => _sortAscending
                    ? source.OrderBy(a => a.Safety)
                    : source.OrderByDescending(a => a.Safety),

                SortColumn.Path => _sortAscending
                    ? source.OrderBy(a => a.Path)
                    : source.OrderByDescending(a => a.Path),
                _ => source.OrderByDescending(a => a.SizeBytes)
            };

            _displayedAssets = source.ToList();
            RefreshAssetList();
            UpdateStatusCount();

            // Persist filter/sort preferences
            SavePreferences();
        }

        // Helpers
        /// <summary>
        /// Builds a rich, multi-line tooltip string for the given asset.
        /// Shows labelled path, category, format description, size, and reference count.
        /// </summary>
        private string BuildRichTooltip(AssetInfo asset)
        {
            if (asset == null) return string.Empty;

            var sb = new System.Text.StringBuilder();

            // Path (labelled clearly)
            sb.AppendLine($"Path:  {asset.Path}");

            // Category
            string categoryName = AssetCategoryResolver.GetDisplayName(asset.Category);
            sb.AppendLine($"Category:  {categoryName}");

            // Format description (if available for this extension)
            string formatDesc = AssetCategoryResolver.GetFileFormatDescription(asset.Extension);
            if (!string.IsNullOrEmpty(formatDesc))
            {
                sb.AppendLine($"Format:  {formatDesc}");
            }
            else if (!string.IsNullOrEmpty(asset.Extension))
            {
                sb.AppendLine($"Format:  {asset.Extension.ToUpperInvariant().TrimStart('.')} File");
            }

            // Size
            sb.AppendLine($"Size:  {asset.SizeFormatted}");

            // References
            sb.AppendLine($"References:  {asset.ReferenceCount}");

            // Dependencies
            sb.AppendLine($"Dependencies:  {asset.DependencyCount}");

            // Safety rating with detailed explanation
            string safetyText = asset.Safety switch
            {
                DeletionSafety.Safe => "Safe - no incoming references",
                DeletionSafety.Caution => "Caution - referenced by Project/Build Settings",
                DeletionSafety.Unsafe => "Unsafe - referenced by other project assets",
                _ => "Unknown"
            };

            sb.AppendLine($"Safety:  {safetyText}");

            // Detailed safety explanation - because seeing "Unsafe" alone can cause anxiety
            if (asset.Safety == DeletionSafety.Safe)
            {
                sb.AppendLine("  No scenes, prefabs, or scripts reference this asset.");
                sb.AppendLine("  It can be safely removed without breaking anything.");
            }

            else if (asset.Safety == DeletionSafety.Caution && asset.ReferencedBy != null)
            {
                var settingsRefs = asset.ReferencedBy
                    .Where(r => r.StartsWith("ProjectSettings/") || r.StartsWith("BuildSettings/"))
                    .ToList();

                if (settingsRefs.Count > 0)
                {
                    sb.AppendLine($"  Referenced by:");

                    foreach (var r in settingsRefs)
                        sb.AppendLine($"   - {r}");
                }
            }

            else if (asset.Safety == DeletionSafety.Unsafe && asset.ReferencedBy != null)
            {
                var projectRefs = asset.ReferencedBy
                    .Where(r => !r.StartsWith("ProjectSettings/") && !r.StartsWith("BuildSettings/"))
                    .ToList();

                if (projectRefs.Count > 0)
                {
                    sb.AppendLine($"  Referenced by:");

                    foreach (var r in projectRefs.Take(5))
                        sb.AppendLine($"   - {Path.GetFileName(r)}");

                    if (projectRefs.Count > 5)
                        sb.AppendLine($"    ... and {projectRefs.Count - 5} more");
                }
            }

            // State flags
            if (asset.IsQuarantined) sb.AppendLine("[Quarantined]");
            if (asset.IsWhitelisted) sb.AppendLine("[Whitelisted]");

            return sb.ToString().TrimEnd();
        }

        private List<AssetInfo> GetSelectedAssets()
        {
            var selected = new List<AssetInfo>();

            foreach (int index in _assetListView.selectedIndices)
            {
                if (index >= 0 && index < _displayedAssets.Count)
                {
                    selected.Add(_displayedAssets[index]);
                }
            }

            return selected;
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes <= 0) return "0 B";

            string[] suffixes = { "B", "KB", "MB", "GB" };
            int order = 0;
            double size = bytes;

            while (size >= 1024 && order < suffixes.Length - 1)
            {
                order++;
                size /= 1024;
            }

            return order == 0 ? $"{size:0} {suffixes[order]}" : $"{size:0.##} {suffixes[order]}";
        }

        // Sort Enum
        private enum SortColumn
        {
            Name,
            Category,
            Size,
            References,
            Dependencies,
            Safety,
            Path
        }

        // Accessibility Panel & Settings
        /// <summary>
        /// Toggles the accessibility settings panel. Because making your tool usable by everyone shouldn't be an afterthought - it should be a Tuesday.
        /// </summary>
        private void ToggleAccessibilityPanel()
        {
            _accessibilityPanelVisible = !_accessibilityPanelVisible;
            if (_accessibilityPanel != null)
            {
                _accessibilityPanel.style.display = _accessibilityPanelVisible ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        /// <summary>
        /// Builds the accessibility settings panel with colour-blind mode, font scaling, high contrast, and UI scale controls.
        /// </summary>
        private void BuildAccessibilityPanel()
        {
            _accessibilityPanel = new VisualElement();
            _accessibilityPanel.AddToClassList("accessibility-panel");
            _accessibilityPanel.style.display = DisplayStyle.None;

            var header = new Label("Accessibility Settings");
            header.AddToClassList("accessibility-header");
            _accessibilityPanel.Add(header);

            // Colour-Blind Mode (Accessibility)
            var cbRow = new VisualElement();
            cbRow.AddToClassList("accessibility-row");

            var cbLabel = new Label("Colour-Blind Mode:");
            cbLabel.AddToClassList("accessibility-label");
            cbRow.Add(cbLabel);

            var cbModes = new List<string>
            {
                "Off",
                "Deuteranopia (Red-Green)",
                "Protanopia (Red-Green)",
                "Tritanopia (Blue-Yellow)"
            };

            var cbDropdown = new DropdownField(cbModes, _accessibilityController.ColourBlindMode);
            cbDropdown.AddToClassList("accessibility-dropdown");
            cbDropdown.tooltip = "Select a colour palette tailored for different types of colour vision deficiency";
            cbDropdown.RegisterValueChangedCallback(evt =>
            {
                _accessibilityController.ColourBlindMode = cbModes.IndexOf(evt.newValue);
                _accessibilityController.ApplyColourBlindMode();
                SavePreferences();
            });

            cbRow.Add(cbDropdown);
            _accessibilityPanel.Add(cbRow);

            // High Contrast
            var hcRow = new VisualElement();
            hcRow.AddToClassList("accessibility-row");

            var hcLabel = new Label("High Contrast:");
            hcLabel.AddToClassList("accessibility-label");
            hcRow.Add(hcLabel);

            var hcToggle = new Toggle();
            hcToggle.AddToClassList("accessibility-toggle");
            hcToggle.value = _accessibilityController.HighContrast;
            hcToggle.tooltip = "Increase border visibility and contrast for improved readability";
            hcToggle.RegisterValueChangedCallback(evt =>
            {
                _accessibilityController.HighContrast = evt.newValue;
                _accessibilityController.ApplyHighContrast();
                SavePreferences();
            });
            hcRow.Add(hcToggle);
            _accessibilityPanel.Add(hcRow);

            // Font Size Offset
            var fsRow = new VisualElement();
            fsRow.AddToClassList("accessibility-row");

            var fsLabel = new Label("Font Size Adjust:");
            fsLabel.AddToClassList("accessibility-label");
            fsRow.Add(fsLabel);

            var fsSlider = new SliderInt(-2, 4);
            fsSlider.AddToClassList("accessibility-slider");
            fsSlider.value = _accessibilityController.FontSizeOffset;
            fsSlider.tooltip = "Adjust font sizes across the entire UI (-2 to +4)";
            fsSlider.showInputField = true;

            var fsPreview = new Label(AccessibilityController.FontSizeOffsetLabel(_accessibilityController.FontSizeOffset));
            fsPreview.style.minWidth = 60;
            fsPreview.style.fontSize = 11;
            fsPreview.style.color = new Color(0.6f, 0.6f, 0.6f);

            fsSlider.RegisterValueChangedCallback(evt =>
            {
                _accessibilityController.FontSizeOffset = evt.newValue;
                fsPreview.text = AccessibilityController.FontSizeOffsetLabel(_accessibilityController.FontSizeOffset);
                _accessibilityController.ApplyFontSizeOffset();
                SavePreferences();
            });

            fsRow.Add(fsSlider);
            fsRow.Add(fsPreview);
            _accessibilityPanel.Add(fsRow);

            // UI Scale
            var scaleRow = new VisualElement();
            scaleRow.AddToClassList("accessibility-row");

            var scaleLabel = new Label("UI Scale:");
            scaleLabel.AddToClassList("accessibility-label");
            scaleRow.Add(scaleLabel);

            var scaleSlider = new Slider(0.8f, 1.5f);
            scaleSlider.AddToClassList("accessibility-slider");
            scaleSlider.value = _accessibilityController.UIScale;
            scaleSlider.tooltip = "Scale the entire UI (0.8x to 1.5x)";
            scaleSlider.showInputField = true;

            var scalePreview = new Label($"{_accessibilityController.UIScale:F1}x");
            scalePreview.style.minWidth = 40;
            scalePreview.style.fontSize = 11;
            scalePreview.style.color = new Color(0.6f, 0.6f, 0.6f);

            scaleSlider.RegisterValueChangedCallback(evt =>
            {
                _accessibilityController.UIScale = Mathf.Round(evt.newValue * 10f) / 10f; // snap to 0.1 increments
                scalePreview.text = $"{_accessibilityController.UIScale:F1}x";
                _accessibilityController.ApplyUIScale();
                SavePreferences();
            });
            scaleRow.Add(scaleSlider);
            scaleRow.Add(scalePreview);
            _accessibilityPanel.Add(scaleRow);

            // Keyboard Navigation Info
            var kbRow = new VisualElement();
            kbRow.AddToClassList("accessibility-row");

            var kbInfo = new Label("Keyboard: Tab to navigate, Enter to activate, Ctrl+A select all, " + "Delete to quarantine, F5 to re-scan, Ctrl+1/2/3 switch tabs");
            kbInfo.style.fontSize = 10;
            kbInfo.style.color = new Color(0.5f, 0.5f, 0.5f);
            kbInfo.style.whiteSpace = WhiteSpace.Normal;
            kbRow.Add(kbInfo);
            _accessibilityPanel.Add(kbRow);

            _root.Add(_accessibilityPanel);
        }

        /// <summary>
        /// Shows a modal dialog with all keyboard shortcuts.
        /// Because reading source code to find shortcuts is a form of torture.
        /// </summary>
        private void OnShortcutHelpClicked()
        {
            string legend =
                "Keyboard Shortcuts\n" +
                "--------------------\n\n" +
                "Ctrl+A / Cmd+A        Select / Deselect All\n" +
                "Delete                Quarantine Selected\n" +
                "Shift+Delete          Permanently Delete Selected\n" +
                "Ctrl+E / Cmd+E        Export CSV\n" +
                "Ctrl+R / Cmd+R        Re-scan Project\n" +
                "F5                    Re-scan Project\n" +
                "Ctrl+Z / Cmd+Z        Undo Last Action\n" +
                "Ctrl+1 / Cmd+1        Switch to Overview Tab\n" +
                "Ctrl+2 / Cmd+2        Switch to Assets Tab\n" +
                "Ctrl+3 / Cmd+3        Switch to Quarantine Tab\n" +
                "Escape                Close Accessibility Panel\n" +
                "Tab                   Navigate between controls\n\n" +
                "--------------------\n\n" +
                "Double-click an asset to ping it in the Project window.\n" +
                "Right-click for context menu options.\n" +
                "Double-click a category bar on the Overview tab to filter by that category.";

            EditorUtility.DisplayDialog("Keyboard Shortcuts", legend, "Got it!");
        }

        /// <summary>
        /// Builds the collapsible log panel on the Overview tab.
        /// </summary>
        private void BuildLogPanel()
        {
            _logFoldout = new Foldout();
            _logFoldout.text = "Scan Log (0)";
            _logFoldout.value = false;
            _logFoldout.AddToClassList("log-panel");
            _logFoldout.style.display = DisplayStyle.None;

            // Style the foldout label
            StyleFoldoutLabel(_logFoldout, new Color(0.65f, 0.65f, 0.65f));

            _logContent = new VisualElement();
            _logContent.style.flexDirection = FlexDirection.Column;
            _logContent.style.maxHeight = 200;
            _logContent.style.overflow = Overflow.Hidden;

            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexGrow = 1;
            scroll.style.maxHeight = 200;
            scroll.Add(_logContent);

            _logFoldout.Add(scroll);
            _overviewTabContent.Add(_logFoldout);
        }

        /// <summary>
        /// Refreshes the log panel with the current scan log entries.
        /// </summary>
        private void UpdateLogPanel()
        {
            if (_logFoldout == null || _logContent == null) return;

            _logContent.Clear();

            if (_scanLog.Count == 0)
            {
                _logFoldout.style.display = DisplayStyle.None;
                return;
            }

            _logFoldout.style.display = DisplayStyle.Flex;
            _logFoldout.text = $"Scan Log ({_scanLog.Count})";

            foreach (var entry in _scanLog)
            {
                var label = new Label(entry);
                label.AddToClassList("log-entry");

                // Colour based on severity for better user experience
                if (entry.Contains("[ERROR]") || entry.Contains("[error]") || entry.Contains("Exception"))
                {
                    label.AddToClassList("log-entry-error");
                }
                else if (entry.Contains("[WARNING]") || entry.Contains("[warning]") || entry.Contains("Warning"))
                {
                    label.AddToClassList("log-entry-warning");
                }

                _logContent.Add(label);
            }
        }
    }

    // Scan History Data
    /// <summary>
    /// Lightweight snapshot of a scan, persisted to Library/ for diff comparison.
    /// </summary>
    [Serializable]
    public class ScanHistoryEntry
    {
        public string ScanTimestamp;
        public int UnusedAssetCount;
        public long UnusedSizeBytes;
        public int TotalAssetCount;
        public List<string> UnusedAssetPaths = new List<string>();
    }
}
