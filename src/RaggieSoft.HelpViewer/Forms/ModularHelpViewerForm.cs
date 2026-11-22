// ================================================================================
// FILE: Forms/ModularHelpViewerForm.cs
// PROJECT: RaggieSoft.HelpViewer (`RaggieSoftHelp.exe`)
// LICENSE: MIT License
// ================================================================================
// PLAIN-ENGLISH OVERVIEW (100-LEVEL PLUGGABLE ENTERPRISE HELP VIEWER GUIDE):
//   How does `ModularHelpViewerForm` work?
//     Think of this window as the modern Windows 11 evolution of Microsoft
//     Document Explorer (`dexplore.exe`) and HTML Help (`hh.exe`), built from the
//     ground up for accessibility, Dark/Light/High-Contrast themes, and zero
//     proprietary binary lock-in:
//       1. **Pluggable Product Modules (`📚 Module:` Toolbar Selector)**:
//          Auto-discovers every `module.rshm.json` installed across `[INSTALLDIR]\HelpModules`,
//          `C:\Program Files\Common Files\RaggieSoft\HelpModules`, `%LOCALAPPDATA%`,
//          and `HKLM\Software\RaggieSoft\HelpModules`. Switching products dynamically
//          re-maps `https://help.raggiesoft.local/` to that product's HTML5 folder!
//       2. **Three-Tab Left Navigation Dock**:
//          - `Contents`: Hierarchical chapter & sub-anchor tree.
//          - `Index`: Instant filterable alphabetical index (e.g., all 25 WCAG rules).
//          - `Search`: Ranked full-text search across the current product OR across
//            ALL installed RaggieSoft Help Modules at once!
//       3. **Live Named Pipe Listener**:
//          Listens in the background via `SingleInstanceHelpIpc` so pressing `F1`
//          or `Shift+F1` in any open RaggieSoft app navigates this window live!
// ================================================================================

using System.Diagnostics;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using AccessibilityStudio.WinForms.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using Microsoft.Win32;
using RaggieSoft.HelpViewer.Models;
using RaggieSoft.HelpViewer.Services;

namespace RaggieSoft.HelpViewer.Forms;

public sealed class ModularHelpViewerForm : Form
{
    private const string VirtualHostName = "help.raggiesoft.local";

    private readonly CancellationTokenSource _ipcCts = new();
    private List<LoadedHelpModule> _modules = [];
    private LoadedHelpModule? _activeModule;

    private CoreWebView2PreferredColorScheme _colorScheme = CoreWebView2PreferredColorScheme.Auto;
    private string _currentTopicId = "welcome-overview";
    private string? _currentAnchorId;
    private bool _webViewReady;
    private bool _suppressSelectionEvents;

    // Toolbar & Module Switcher
    private readonly ToolStrip _toolbar = new()
    {
        GripStyle = ToolStripGripStyle.Hidden,
        AccessibleName = "RaggieSoft Help Viewer Toolbar"
    };
    private ToolStripComboBox _cmbModules = null!;
    private ToolStripButton _btnBack = null!;
    private ToolStripButton _btnForward = null!;
    private ToolStripTextBox _txtQuickSearch = null!;

    // Split layout
    private SplitContainer _mainSplit = null!;
    private readonly TabControl _leftTabs = new()
    {
        Dock = DockStyle.Fill,
        AccessibleName = "Help Contents, Index, and Search Tabs"
    };

    // Tab 1: Contents Tree
    private readonly TreeView _contentsTree = new()
    {
        Dock = DockStyle.Fill,
        HideSelection = false,
        ShowNodeToolTips = true,
        AccessibleName = "Module Table of Contents"
    };
    private readonly Dictionary<string, TreeNode> _treeNodesByKey = new(StringComparer.OrdinalIgnoreCase);

    // Tab 2: Alphabetical Index
    private readonly TextBox _txtIndexFilter = new()
    {
        Dock = DockStyle.Top,
        PlaceholderText = "Filter index entries…",
        AccessibleName = "Filter Module Index"
    };
    private readonly ListBox _lstIndex = new()
    {
        Dock = DockStyle.Fill,
        IntegralHeight = false,
        AccessibleName = "Module Alphabetical Index"
    };

    // Tab 3: Full-Text Search (Active Module or All Installed RaggieSoft Modules)
    private readonly TextBox _txtSearchQuery = new()
    {
        Dock = DockStyle.Top,
        PlaceholderText = "Search topics, rules, CLI flags…",
        AccessibleName = "Full-Text Offline Search Query"
    };
    private readonly CheckBox _chkSearchAllModules = new()
    {
        Dock = DockStyle.Top,
        Text = "Search across all installed RaggieSoft modules",
        Checked = true,
        Height = 26,
        Padding = new Padding(4, 2, 0, 0)
    };
    private readonly ListBox _lstSearchResults = new()
    {
        Dock = DockStyle.Fill,
        IntegralHeight = false,
        AccessibleName = "Offline Search Results"
    };

    // Right Reader (`WebView2` + Air-Gapped Plain-Text Fallback)
    private readonly WebView2 _webView = new()
    {
        Dock = DockStyle.Fill,
        AccessibleName = "Offline HTML5 Documentation Reader"
    };
    private readonly TextBox _fallbackReader = new()
    {
        Dock = DockStyle.Fill,
        Multiline = true,
        ReadOnly = true,
        ScrollBars = ScrollBars.Vertical,
        Font = new Font("Consolas", 10F),
        Visible = false,
        AccessibleName = "Fallback Plain-Text Help Reader"
    };

    // Status bar
    private readonly StatusStrip _statusStrip = new() { AccessibleName = "Help Status Bar" };
    private readonly ToolStripStatusLabel _lblStatusPath = new()
    {
        Spring = true,
        TextAlign = ContentAlignment.MiddleLeft,
        Text = "Initializing RaggieSoft Help Viewer…"
    };
    private readonly ToolStripStatusLabel _lblStatusModuleCount = new()
    {
        BorderSides = ToolStripStatusLabelBorderSides.Left,
        Text = "Modules: 0"
    };

    private sealed record TreeTarget(string ModuleId, string TopicId, string? AnchorId);
    private sealed record IndexListItem(HelpModuleIndexEntry Entry)
    {
        public override string ToString() => Entry.DisplayText;
    }
    private sealed record SearchListItem(ModularHelpSearchHit Hit, bool ShowProductPrefix)
    {
        public override string ToString() => ShowProductPrefix
            ? $"[{Hit.ProductName} • {Hit.Category}] {Hit.DisplayTitle}"
            : $"[{Hit.Category}] {Hit.DisplayTitle}";
    }

    public ModularHelpViewerForm(HelpNavigationRequest initialRequest)
    {
        Text = "RaggieSoft Help Viewer — Pluggable Enterprise Documentation Center";
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(1260, 800);
        MinimumSize = new Size(900, 560);
        KeyPreview = true;

        string iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "AccessibilityStudio.ico");
        if (File.Exists(iconPath))
        {
            Icon = new Icon(iconPath);
        }

        _colorScheme = ParseThemePreference(initialRequest.Theme);

        BuildToolbar();
        BuildSplitWorkspace();
        BuildStatusStrip();

        // Discover all plugged-in `module.rshm.json` packages on this workstation
        ReloadDiscoveredModules(initialRequest.ExplicitModuleDirectory);
        ApplyNavigationRequest(initialRequest, activateWindow: false);

        if (OperatingSystem.IsWindows())
        {
            SystemEvents.UserPreferenceChanged += OnSystemUserPreferenceChanged;
        }

        Load += async (_, _) =>
        {
            using var splash = RaggieSoftStartupSplashScope.Begin(
                "RaggieSoft Help Viewer",
                "Opening offline documentation library...");
            ApplySafeSplitterDistance();
            _ = SingleInstanceHelpIpc.RunListenerLoopAsync(OnExternalIpcRequest, _ipcCts.Token);
            await InitializeWebViewAsync();
            await splash.CompleteAsync();
        };
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _ipcCts.Cancel();
            _ipcCts.Dispose();
            if (OperatingSystem.IsWindows())
            {
                SystemEvents.UserPreferenceChanged -= OnSystemUserPreferenceChanged;
            }
        }
        base.Dispose(disposing);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == (Keys.Alt | Keys.Left))
        {
            if (_webViewReady && _webView.CoreWebView2?.CanGoBack == true) _webView.CoreWebView2.GoBack();
            return true;
        }
        if (keyData == (Keys.Alt | Keys.Right))
        {
            if (_webViewReady && _webView.CoreWebView2?.CanGoForward == true) _webView.CoreWebView2.GoForward();
            return true;
        }
        if (keyData == (Keys.Alt | Keys.Home))
        {
            if (_activeModule is not null)
            {
                NavigateWithinActiveModule(_activeModule.Manifest.DefaultTopicId, null);
            }
            return true;
        }
        if (keyData is (Keys.Control | Keys.E) or (Keys.Control | Keys.F))
        {
            _leftTabs.SelectedIndex = 2;
            _txtSearchQuery.Focus();
            _txtSearchQuery.SelectAll();
            return true;
        }
        if (keyData == (Keys.Control | Keys.P))
        {
            if (_webViewReady && _webView.CoreWebView2 is not null)
            {
                _ = _webView.CoreWebView2.ExecuteScriptAsync("window.print();");
            }
            return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void OnExternalIpcRequest(HelpNavigationRequest req)
    {
        if (!IsHandleCreated) return;
        BeginInvoke(() => ApplyNavigationRequest(req, activateWindow: true));
    }

    private void ApplyNavigationRequest(HelpNavigationRequest req, bool activateWindow)
    {
        if (!string.IsNullOrWhiteSpace(req.ExplicitModuleDirectory))
        {
            ReloadDiscoveredModules(req.ExplicitModuleDirectory);
        }

        if (!string.IsNullOrWhiteSpace(req.Theme))
        {
            _colorScheme = ParseThemePreference(req.Theme);
            ApplyWebViewColorScheme();
        }

        // Select requested module (or default to first discovered module)
        if (!string.IsNullOrWhiteSpace(req.ModuleId))
        {
            LoadedHelpModule? match = _modules.FirstOrDefault(
                m => string.Equals(m.Manifest.ModuleId, req.ModuleId, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                SelectModule(match);
            }
        }
        else if (_activeModule is null && _modules.Count > 0)
        {
            SelectModule(_modules[0]);
        }

        if (_activeModule is not null)
        {
            string targetTopic = !string.IsNullOrWhiteSpace(req.TopicId)
                ? req.TopicId
                : _activeModule.Manifest.DefaultTopicId;
            NavigateWithinActiveModule(targetTopic, req.AnchorId);
        }

        if (activateWindow)
        {
            if (WindowState == FormWindowState.Minimized)
            {
                WindowState = FormWindowState.Normal;
            }
            Activate();
            BringToFront();
        }
    }

    private void BuildToolbar()
    {
        _cmbModules = new ToolStripComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            AutoSize = false,
            Width = 285,
            AccessibleName = "Installed RaggieSoft Help Module Selector",
            ToolTipText = "Switch between installed RaggieSoft product documentation modules"
        };
        _cmbModules.SelectedIndexChanged += (_, _) =>
        {
            if (_suppressSelectionEvents) return;
            if (_cmbModules.SelectedItem is LoadedHelpModule mod)
            {
                SelectModule(mod);
                NavigateWithinActiveModule(mod.Manifest.DefaultTopicId, null);
            }
        };

        _btnBack = new ToolStripButton("◀ Back") { Enabled = false, ToolTipText = "Back (Alt+Left)" };
        _btnBack.Click += (_, _) =>
        {
            if (_webViewReady && _webView.CoreWebView2?.CanGoBack == true) _webView.CoreWebView2.GoBack();
        };

        _btnForward = new ToolStripButton("▶ Forward") { Enabled = false, ToolTipText = "Forward (Alt+Right)" };
        _btnForward.Click += (_, _) =>
        {
            if (_webViewReady && _webView.CoreWebView2?.CanGoForward == true) _webView.CoreWebView2.GoForward();
        };

        ToolStripButton btnHome = new("🏠 Home") { ToolTipText = "Module Home Topic (Alt+Home)" };
        btnHome.Click += (_, _) =>
        {
            if (_activeModule is not null)
            {
                NavigateWithinActiveModule(_activeModule.Manifest.DefaultTopicId, null);
            }
        };

        _txtQuickSearch = new ToolStripTextBox
        {
            AutoSize = false,
            Width = 210,
            AccessibleName = "Quick Search Help Modules",
            ToolTipText = "Type search terms and press Enter (Ctrl+E)"
        };
        _txtQuickSearch.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                string q = _txtQuickSearch.Text.Trim();
                _leftTabs.SelectedIndex = 2;
                _txtSearchQuery.Text = q;
                RefreshSearchResults();
            }
        };

        ToolStripButton btnPrint = new("🖨 Print") { ToolTipText = "Print current topic (Ctrl+P)" };
        btnPrint.Click += (_, _) =>
        {
            if (_webViewReady && _webView.CoreWebView2 is not null)
            {
                _ = _webView.CoreWebView2.ExecuteScriptAsync("window.print();");
            }
        };

        ToolStripButton btnOnline = new("🌐 Online Docs") { ToolTipText = "Open product documentation in web browser" };
        btnOnline.Click += (_, _) =>
        {
            if (_activeModule is null) return;
            string url = _activeModule.Manifest.OnlineDocsUrl;
            if (!string.IsNullOrWhiteSpace(_currentAnchorId))
            {
                url = $"{url}#{_currentAnchorId}";
            }
            LaunchExternalBrowser(url);
        };

        _toolbar.Items.AddRange([
            new ToolStripLabel("📚 Module:"),
            _cmbModules,
            new ToolStripSeparator(),
            _btnBack,
            _btnForward,
            btnHome,
            new ToolStripSeparator(),
            new ToolStripLabel("Search:"),
            _txtQuickSearch,
            new ToolStripSeparator(),
            btnPrint,
            btnOnline
        ]);
    }

    private void BuildSplitWorkspace()
    {
        _mainSplit = new SplitContainer
        {
            Size = new Size(1240, 720),
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            SplitterDistance = 335,
            FixedPanel = FixedPanel.Panel1
        };

        TabPage tabContents = new("Contents") { Padding = new Padding(4) };
        _contentsTree.AfterSelect += (_, e) =>
        {
            if (_suppressSelectionEvents) return;
            if (e.Node?.Tag is TreeTarget t)
            {
                NavigateWithinActiveModule(t.TopicId, t.AnchorId);
            }
        };
        tabContents.Controls.Add(_contentsTree);

        TabPage tabIndex = new("Index") { Padding = new Padding(4) };
        _txtIndexFilter.TextChanged += (_, _) => RefreshIndexList();
        _lstIndex.SelectedIndexChanged += (_, _) =>
        {
            if (_lstIndex.SelectedItem is IndexListItem item)
            {
                NavigateWithinActiveModule(item.Entry.TopicId, item.Entry.AnchorId);
            }
        };
        tabIndex.Controls.Add(_lstIndex);
        tabIndex.Controls.Add(_txtIndexFilter);

        TabPage tabSearch = new("Search") { Padding = new Padding(4) };
        _txtSearchQuery.TextChanged += (_, _) => RefreshSearchResults();
        _chkSearchAllModules.CheckedChanged += (_, _) => RefreshSearchResults();
        _lstSearchResults.SelectedIndexChanged += (_, _) =>
        {
            if (_lstSearchResults.SelectedItem is SearchListItem item)
            {
                LoadedHelpModule? targetMod = _modules.FirstOrDefault(
                    m => string.Equals(m.Manifest.ModuleId, item.Hit.ModuleId, StringComparison.OrdinalIgnoreCase));
                if (targetMod is not null && targetMod != _activeModule)
                {
                    SelectModule(targetMod);
                }
                NavigateWithinActiveModule(item.Hit.TopicId, item.Hit.AnchorId);
            }
        };
        tabSearch.Controls.Add(_lstSearchResults);
        tabSearch.Controls.Add(_chkSearchAllModules);
        tabSearch.Controls.Add(_txtSearchQuery);

        _leftTabs.TabPages.AddRange([tabContents, tabIndex, tabSearch]);
        _mainSplit.Panel1.Controls.Add(_leftTabs);

        _mainSplit.Panel2.Controls.Add(_webView);
        _mainSplit.Panel2.Controls.Add(_fallbackReader);

        Controls.Add(_mainSplit);
        Controls.Add(_toolbar);
        Controls.Add(_statusStrip);
    }

    private void BuildStatusStrip()
    {
        _statusStrip.Items.AddRange([_lblStatusPath, _lblStatusModuleCount]);
    }

    private void ApplySafeSplitterDistance()
    {
        try
        {
            if (_mainSplit.Width > 480)
            {
                _mainSplit.SplitterDistance = Math.Clamp(335, 240, _mainSplit.Width - 340);
            }
        }
        catch
        {
            // Ignore transient layout bounds
        }
    }

    private void ReloadDiscoveredModules(string? explicitModuleDir)
    {
        _modules = HelpModuleDiscoveryService.DiscoverInstalledModules(explicitModuleDir).ToList();
        _lblStatusModuleCount.Text = $"Installed Modules: {_modules.Count}";

        _suppressSelectionEvents = true;
        try
        {
            _cmbModules.Items.Clear();
            foreach (LoadedHelpModule mod in _modules)
            {
                _cmbModules.Items.Add(mod);
            }

            if (_modules.Count == 0)
            {
                _fallbackReader.Visible = true;
                _webView.Visible = false;
                _fallbackReader.Text =
                    "No RaggieSoft Help Modules (`module.rshm.json`) were found on this workstation.\r\n\r\n" +
                    "Searched locations:\r\n" +
                    "  1. [INSTALLDIR]\\HelpModules\\*\r\n" +
                    "  2. C:\\Program Files\\Common Files\\RaggieSoft\\HelpModules\\*\r\n" +
                    "  3. %LOCALAPPDATA%\\RaggieSoft\\HelpModules\\*\r\n" +
                    "  4. HKLM\\Software\\RaggieSoft\\HelpModules";
            }
        }
        finally
        {
            _suppressSelectionEvents = false;
        }
    }

    private void SelectModule(LoadedHelpModule module)
    {
        _activeModule = module;
        Text = $"{module.Manifest.ProductName} — RaggieSoft Help Viewer";

        _suppressSelectionEvents = true;
        try
        {
            _cmbModules.SelectedItem = module;
        }
        finally
        {
            _suppressSelectionEvents = false;
        }

        // Re-map `https://help.raggiesoft.local/` to the newly selected module's directory!
        if (_webViewReady && _webView.CoreWebView2 is not null)
        {
            _webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                VirtualHostName,
                module.ModuleDirectory,
                CoreWebView2HostResourceAccessKind.Allow);
        }

        PopulateContentsTreeForActiveModule();
        RefreshIndexList();
        RefreshSearchResults();
    }

    private void PopulateContentsTreeForActiveModule()
    {
        _contentsTree.BeginUpdate();
        _contentsTree.Nodes.Clear();
        _treeNodesByKey.Clear();

        if (_activeModule is null)
        {
            _contentsTree.EndUpdate();
            return;
        }

        string modId = _activeModule.Manifest.ModuleId;
        var byCategory = _activeModule.Manifest.Topics.GroupBy(t => t.Category);
        foreach (var group in byCategory)
        {
            TreeNode catNode = new(group.Key);
            foreach (HelpModuleTopicEntry topic in group)
            {
                TreeNode topicNode = new(topic.Title)
                {
                    Tag = new TreeTarget(modId, topic.Id, null),
                    ToolTipText = topic.Summary
                };
                _treeNodesByKey[topic.Id] = topicNode;

                // Attach child anchor nodes if `indexEntries` point into this topic
                foreach (HelpModuleIndexEntry child in _activeModule.Manifest.IndexEntries.Where(
                             e => string.Equals(e.TopicId, topic.Id, StringComparison.OrdinalIgnoreCase)))
                {
                    string label = string.IsNullOrWhiteSpace(child.Badge)
                        ? child.EntryId
                        : $"{child.EntryId} ({child.Badge})";
                    TreeNode childNode = new(label)
                    {
                        Tag = new TreeTarget(modId, child.TopicId, child.AnchorId),
                        ToolTipText = child.DisplayText
                    };
                    if (!string.IsNullOrWhiteSpace(child.AnchorId))
                    {
                        _treeNodesByKey[$"{child.TopicId}#{child.AnchorId}"] = childNode;
                    }
                    topicNode.Nodes.Add(childNode);
                }

                catNode.Nodes.Add(topicNode);
            }

            _contentsTree.Nodes.Add(catNode);
            catNode.Expand();
        }

        _contentsTree.EndUpdate();
    }

    private void RefreshIndexList()
    {
        _lstIndex.BeginUpdate();
        _lstIndex.Items.Clear();

        if (_activeModule is not null)
        {
            string q = _txtIndexFilter.Text.Trim();
            foreach (HelpModuleIndexEntry entry in _activeModule.Manifest.IndexEntries)
            {
                if (!string.IsNullOrEmpty(q) &&
                    !entry.EntryId.Contains(q, StringComparison.OrdinalIgnoreCase) &&
                    !entry.DisplayText.Contains(q, StringComparison.OrdinalIgnoreCase) &&
                    !entry.Subtitle.Contains(q, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                _lstIndex.Items.Add(new IndexListItem(entry));
            }
        }

        _lstIndex.EndUpdate();
    }

    private void RefreshSearchResults()
    {
        string? restrictId = _chkSearchAllModules.Checked ? null : _activeModule?.Manifest.ModuleId;
        IReadOnlyList<ModularHelpSearchHit> hits = HelpModuleDiscoveryService.SearchModules(
            _modules,
            _txtSearchQuery.Text,
            restrictId);

        bool showPrefix = _chkSearchAllModules.Checked && _modules.Count > 1;
        _lstSearchResults.BeginUpdate();
        _lstSearchResults.Items.Clear();
        foreach (ModularHelpSearchHit hit in hits)
        {
            _lstSearchResults.Items.Add(new SearchListItem(hit, showPrefix));
        }
        _lstSearchResults.EndUpdate();
    }

    private void NavigateWithinActiveModule(string topicId, string? anchorId)
    {
        if (_activeModule is null) return;

        HelpModuleTopicEntry? topic = _activeModule.Manifest.Topics.FirstOrDefault(
            t => string.Equals(t.Id, topicId, StringComparison.OrdinalIgnoreCase))
            ?? _activeModule.Manifest.Topics.FirstOrDefault();

        if (topic is null) return;

        _currentTopicId = topic.Id;
        _currentAnchorId = string.IsNullOrWhiteSpace(anchorId) ? null : anchorId.Trim().TrimStart('#');

        string composite = _currentAnchorId is not null ? $"{_currentTopicId}#{_currentAnchorId}" : _currentTopicId;
        if (_treeNodesByKey.TryGetValue(composite, out TreeNode? node) ||
            _treeNodesByKey.TryGetValue(_currentTopicId, out node))
        {
            _suppressSelectionEvents = true;
            try
            {
                node.EnsureVisible();
                _contentsTree.SelectedNode = node;
            }
            finally
            {
                _suppressSelectionEvents = false;
            }
        }

        // Populate plain-text fallback
        if (_activeModule.PlainTextByTopicId.TryGetValue(_currentTopicId, out string? plainText))
        {
            _fallbackReader.Text = $"=== {topic.Title} ({_activeModule.Manifest.ProductName}) ===\r\n\r\n{plainText}";
        }

        string fileName = string.IsNullOrWhiteSpace(topic.FileName) ? $"{topic.Id}.html" : topic.FileName;
        string frag = _currentAnchorId is not null ? $"#{_currentAnchorId}" : string.Empty;
        _lblStatusPath.Text = $"Module: {_activeModule.Manifest.ModuleId} • {fileName}{frag} ({_activeModule.ModuleDirectory})";

        if (_webViewReady && _webView.CoreWebView2 is not null)
        {
            _webView.CoreWebView2.Navigate($"https://{VirtualHostName}/{fileName}{frag}");
        }
    }

    private async Task InitializeWebViewAsync()
    {
        if (_modules.Count == 0) return;

        string userDataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RaggieSoft",
            "HelpViewer",
            "WebView2Data"
        );

        try
        {
            Directory.CreateDirectory(userDataFolder);
            CoreWebView2Environment env = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
            await _webView.EnsureCoreWebView2Async(env);

            _webView.CoreWebView2.Settings.IsStatusBarEnabled = false;
            _webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
            ApplyWebViewColorScheme();

            if (_activeModule is not null)
            {
                _webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                    VirtualHostName,
                    _activeModule.ModuleDirectory,
                    CoreWebView2HostResourceAccessKind.Allow);
            }

            _webView.CoreWebView2.NavigationCompleted += (_, _) =>
            {
                _btnBack.Enabled = _webView.CoreWebView2.CanGoBack;
                _btnForward.Enabled = _webView.CoreWebView2.CanGoForward;
            };

            _webView.CoreWebView2.NavigationStarting += (_, e) =>
            {
                if (Uri.TryCreate(e.Uri, UriKind.Absolute, out Uri? uri) &&
                    !string.Equals(uri.Host, VirtualHostName, StringComparison.OrdinalIgnoreCase) &&
                    uri.Scheme is "http" or "https")
                {
                    e.Cancel = true;
                    LaunchExternalBrowser(uri.ToString());
                }
            };

            _webViewReady = true;
            NavigateWithinActiveModule(_currentTopicId, _currentAnchorId);
        }
        catch
        {
            _webView.Visible = false;
            _fallbackReader.Visible = true;
        }
    }

    private void ApplyWebViewColorScheme()
    {
        try
        {
            if (_webView.CoreWebView2 is not null)
            {
                _webView.CoreWebView2.Profile.PreferredColorScheme = _colorScheme;
            }
        }
        catch
        {
            // Ignore while initializing
        }
    }

    private static CoreWebView2PreferredColorScheme ParseThemePreference(string? raw) =>
        raw?.Trim().ToLowerInvariant() switch
        {
            "dark" => CoreWebView2PreferredColorScheme.Dark,
            "light" => CoreWebView2PreferredColorScheme.Light,
            _ => CoreWebView2PreferredColorScheme.Auto
        };

    private static void LaunchExternalBrowser(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
        }
        catch
        {
            // Ignore restricted corporate browser policies
        }
    }

    private void OnSystemUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category is UserPreferenceCategory.General or UserPreferenceCategory.Color or UserPreferenceCategory.VisualStyle)
        {
            if (IsHandleCreated)
            {
                BeginInvoke(ApplyWebViewColorScheme);
            }
        }
    }
}
