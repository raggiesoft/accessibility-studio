// ================================================================================
// FILE: Forms/HelpViewerForm.cs
// PROJECT: AccessibilityStudio.WinForms (RaggieSoft Accessibility Studio IDE)
// LICENSE: MIT License
// ================================================================================
// PLAIN-ENGLISH OVERVIEW (100-LEVEL MODERN WINDOWS F1 HELP VIEWER GUIDE):
//   What is this window?
//     `HelpViewerForm` is the modern Windows 11 replacement for the classic
//     Windows 2000 / XP HTML Help viewer (`hh.exe` / `.chm`). It preserves
//     everything engineers loved about classic desktop Help—instant `F1` launch,
//     a docked left navigation pane with **Contents**, **Index (Rules A–Z)**, and
//     **Instant Search**, plus zero internet requirement on air-gapped networks—
//     while upgrading the rendering engine from retired Internet Explorer
//     (`mshtml.dll`) to Evergreen Chromium (`Microsoft.Web.WebView2`).
//
//   How Local Virtual Host Mapping (`https://help.a11y.local`) Works:
//     1. First, `ResolveOrMaterializeHelpDirectory()` checks whether our
//        InstallAware Studio X18 `.msi` installer placed pre-compiled HTML5 Help
//        files inside `<InstallDir>\Help\`.
//     2. If running directly from Visual Studio (`F5`) or a minimal setup without
//        `<InstallDir>\Help\`, it materializes the 100% offline HTML5 bundle into
//        `%LOCALAPPDATA%\RaggieSoft\AccessibilityStudio\HelpBundle_v1` in milliseconds.
//     3. `CoreWebView2.SetVirtualHostNameToFolderMapping("help.a11y.local", ...)`
//        serves those local HTML5 files over a secure virtual origin—avoiding
//        `.chm` Mark-of-the-Web (`Zone.Identifier`) security blocks, supporting
//        real Back/Forward history, honoring Windows 11 Dark/Light/High-Contrast
//        modes (`prefers-color-scheme` & `forced-colors`), and scrolling smoothly
//        to `#rule-WCAG-...` anchors!
//     4. If `WebView2` runtime itself is unavailable on a locked-down machine,
//        `_fallbackReader` displays a clean native WinForms text rendering so `F1`
//        Help never fails under any workstation configuration.
// ================================================================================

using System.Diagnostics;
using System.Drawing;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using AccessibilityStudio.Core.Documentation;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using Microsoft.Win32;

namespace AccessibilityStudio.WinForms.Forms;

public sealed partial class HelpViewerForm : Form
{
    private const string VirtualHelpHost = "help.a11y.local";

    // Current theme preference inherited from `MainIdeForm` (`Auto` = Windows System Mode).
    private CoreWebView2PreferredColorScheme _colorScheme;

    // Pending navigation target requested before WebView2 finishes async initialization.
    private string _currentTopicId = "welcome-overview";
    private string? _currentAnchorId;
    private bool _webViewReady;
    private bool _suppressTreeSelectionEvent;

    // Top toolbar & navigation buttons
    private readonly ToolStrip _toolbar = new()
    {
        GripStyle = ToolStripGripStyle.Hidden,
        AccessibleName = "Help Viewer Navigation Toolbar"
    };
    private ToolStripButton _btnBack = null!;
    private ToolStripButton _btnForward = null!;
    private ToolStripTextBox _txtQuickSearch = null!;

    // Split layout: Left (`Contents` / `Rule Index` / `Search`), Right (`WebView2` + Fallback)
    private SplitContainer _mainSplit = null!;
    private readonly TabControl _leftTabs = new()
    {
        Dock = DockStyle.Fill,
        AccessibleName = "Help Navigation Panes"
    };

    // Tab 1: Contents TreeView
    private readonly TreeView _contentsTree = new()
    {
        Dock = DockStyle.Fill,
        HideSelection = false,
        ShowNodeToolTips = true,
        AccessibleName = "Help Manual Table of Contents"
    };
    private readonly Dictionary<string, TreeNode> _topicNodes = new(StringComparer.OrdinalIgnoreCase);

    // Tab 2: Alphabetical Rule Index (`WCAG-*`)
    private readonly TextBox _txtIndexFilter = new()
    {
        Dock = DockStyle.Top,
        PlaceholderText = "Filter 25 Rule IDs (e.g., TABLE, CONTRAST, VTT)…",
        AccessibleName = "Filter Rule ID Index"
    };
    private readonly ListBox _lstRuleIndex = new()
    {
        Dock = DockStyle.Fill,
        IntegralHeight = false,
        AccessibleName = "Alphabetical WCAG and Section 508 Rule Index"
    };

    // Tab 3: Full-Text Offline Search
    private readonly TextBox _txtSearchQuery = new()
    {
        Dock = DockStyle.Top,
        PlaceholderText = "Search chapters, WCAG criteria (1.4.3), CLI flags, GPO…",
        AccessibleName = "Search Offline Help Manual"
    };
    private readonly ListBox _lstSearchResults = new()
    {
        Dock = DockStyle.Fill,
        IntegralHeight = false,
        AccessibleName = "Offline Help Search Results"
    };

    // Right Reading Surface (`WebView2` + Zero-Dependency Plain-Text Fallback)
    private readonly WebView2 _helpWebView = new()
    {
        Dock = DockStyle.Fill,
        AccessibleName = "Help Topic Document Reader"
    };
    private readonly TextBox _fallbackReader = new()
    {
        Dock = DockStyle.Fill,
        Multiline = true,
        ReadOnly = true,
        ScrollBars = ScrollBars.Vertical,
        Font = new Font("Consolas", 10F),
        Visible = false,
        AccessibleName = "Fallback Offline Help Reader"
    };

    // Status bar showing active local topic URI & offline assurance
    private readonly StatusStrip _statusStrip = new() { AccessibleName = "Help Viewer Status Bar" };
    private readonly ToolStripStatusLabel _lblStatusTopic = new()
    {
        Spring = true,
        TextAlign = ContentAlignment.MiddleLeft,
        Text = "Offline HTML5 Manual Ready"
    };
    private readonly ToolStripStatusLabel _lblStatusMode = new()
    {
        BorderSides = ToolStripStatusLabelBorderSides.Left,
        Text = "100% Offline • WCAG 2.2 AA Verified"
    };

    private sealed record NavTarget(string TopicId, string? AnchorId);
    private sealed record RuleListItem(HelpRuleEntry Rule)
    {
        public override string ToString() => $"{Rule.RuleId} — {Rule.Title}";
    }
    private sealed record SearchListItem(HelpSearchHit Hit)
    {
        public override string ToString() => $"[{Hit.Category}] {Hit.DisplayTitle}";
    }

    public HelpViewerForm(
        CoreWebView2PreferredColorScheme initialColorScheme = CoreWebView2PreferredColorScheme.Auto,
        string initialTopicId = "welcome-overview",
        string? initialAnchorId = null)
    {
        _colorScheme = initialColorScheme;
        _currentTopicId = string.IsNullOrWhiteSpace(initialTopicId) ? "welcome-overview" : initialTopicId;
        _currentAnchorId = initialAnchorId;

        Text = "RaggieSoft Accessibility Studio Help — Offline Workstation Manual & Rule Reference";
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(1220, 780);
        MinimumSize = new Size(880, 560);
        KeyPreview = true;

        // Share application icon if present in `Assets/AccessibilityStudio.ico`
        string iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "AccessibilityStudio.ico");
        if (File.Exists(iconPath))
        {
            Icon = new Icon(iconPath);
        }

        BuildToolbar();
        BuildSplitLayout();
        BuildStatusBar();

        PopulateContentsTree();
        PopulateRuleIndexList(string.Empty);
        PopulateSearchResults(string.Empty);

        if (OperatingSystem.IsWindows())
        {
            SystemEvents.UserPreferenceChanged += OnSystemUserPreferenceChanged;
        }

        Load += async (_, _) =>
        {
            ApplySafeSplitterProportion();
            await InitializeHelpWebViewAsync();
        };
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && OperatingSystem.IsWindows())
        {
            SystemEvents.UserPreferenceChanged -= OnSystemUserPreferenceChanged;
        }
        base.Dispose(disposing);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        // Standard Windows Help Viewer keyboard navigation shortcuts
        if (keyData == (Keys.Alt | Keys.Left))
        {
            NavigateBack();
            return true;
        }
        if (keyData == (Keys.Alt | Keys.Right))
        {
            NavigateForward();
            return true;
        }
        if (keyData == (Keys.Alt | Keys.Home))
        {
            NavigateToTopic("welcome-overview");
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
            PrintCurrentTopic();
            return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    /// <summary>
    /// Navigates the Help Viewer to a specific <paramref name="topicId"/> and optional
    /// <paramref name="anchorId"/> (for example, <c>("rule-reference", "rule-WCAG-IMG-ALT")</c>).
    /// </summary>
    public void NavigateToTopic(string topicId, string? anchorId = null)
    {
        HelpTopic resolved = StudioHelpCatalog.GetTopicOrDefault(topicId);
        _currentTopicId = resolved.TopicId;
        _currentAnchorId = string.IsNullOrWhiteSpace(anchorId) ? null : anchorId.Trim().TrimStart('#');

        SyncTreeSelection(_currentTopicId, _currentAnchorId);
        UpdateFallbackText(_currentTopicId, _currentAnchorId);

        string displayAnchor = _currentAnchorId is not null ? $"#{_currentAnchorId}" : string.Empty;
        _lblStatusTopic.Text = $"Topic: {resolved.Title} ({_currentTopicId}.html{displayAnchor})";

        if (_webViewReady && _helpWebView.CoreWebView2 is not null)
        {
            string targetUri = $"https://{VirtualHelpHost}/{_currentTopicId}.html{displayAnchor}";
            _helpWebView.CoreWebView2.Navigate(targetUri);
        }
    }

    /// <summary>
    /// Updates the embedded Chromium Help reader's <c>prefers-color-scheme</c> setting live
    /// whenever the user toggles Dark/Light mode in Windows 11 or <c>MainIdeForm</c>.
    /// </summary>
    public void ApplyColorSchemePreference(CoreWebView2PreferredColorScheme scheme)
    {
        _colorScheme = scheme;
        try
        {
            if (_helpWebView.CoreWebView2 is not null)
            {
                _helpWebView.CoreWebView2.Profile.PreferredColorScheme = scheme;
            }
        }
        catch
        {
            // Ignore transient state while WebView2 initializes
        }
    }

    private void BuildToolbar()
    {
        _btnBack = new ToolStripButton("◀ Back")
        {
            DisplayStyle = ToolStripItemDisplayStyle.Text,
            ToolTipText = "Back (Alt+Left)",
            Enabled = false
        };
        _btnBack.Click += (_, _) => NavigateBack();

        _btnForward = new ToolStripButton("▶ Forward")
        {
            DisplayStyle = ToolStripItemDisplayStyle.Text,
            ToolTipText = "Forward (Alt+Right)",
            Enabled = false
        };
        _btnForward.Click += (_, _) => NavigateForward();

        ToolStripButton btnHome = new("🏠 Contents Home")
        {
            DisplayStyle = ToolStripItemDisplayStyle.Text,
            ToolTipText = "Return to Welcome & Architecture Overview (Alt+Home)"
        };
        btnHome.Click += (_, _) => NavigateToTopic("welcome-overview");

        ToolStripButton btnRuleCatalog = new("📋 All 25 Rules")
        {
            DisplayStyle = ToolStripItemDisplayStyle.Text,
            ToolTipText = "Browse complete WCAG 2.2 & Revised Section 508 Rule Catalog"
        };
        btnRuleCatalog.Click += (_, _) => NavigateToTopic("rule-reference");

        _txtQuickSearch = new ToolStripTextBox
        {
            AutoSize = false,
            Width = 220,
            AccessibleName = "Quick Search Help Topics",
            ToolTipText = "Type a keyword or Rule ID and press Enter (Ctrl+E)"
        };
        _txtQuickSearch.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                string q = _txtQuickSearch.Text.Trim();
                _leftTabs.SelectedIndex = 2;
                _txtSearchQuery.Text = q;
                PopulateSearchResults(q);
                if (_lstSearchResults.Items.Count > 0)
                {
                    _lstSearchResults.SelectedIndex = 0;
                }
            }
        };

        ToolStripButton btnPrint = new("🖨 Print Topic")
        {
            DisplayStyle = ToolStripItemDisplayStyle.Text,
            ToolTipText = "Print current Help topic or save as PDF (Ctrl+P)"
        };
        btnPrint.Click += (_, _) => PrintCurrentTopic();

        ToolStripButton btnOnline = new("🌐 Online Docs")
        {
            DisplayStyle = ToolStripItemDisplayStyle.Text,
            ToolTipText = "Open companion documentation at https://a11y.raggiesoft.com/docs"
        };
        btnOnline.Click += (_, _) => OpenCompanionOnlineDocs();

        _toolbar.Items.AddRange([
            _btnBack,
            _btnForward,
            btnHome,
            btnRuleCatalog,
            new ToolStripSeparator(),
            new ToolStripLabel("Search:"),
            _txtQuickSearch,
            new ToolStripSeparator(),
            btnPrint,
            btnOnline
        ]);
    }

    private void BuildSplitLayout()
    {
        // Set explicit initial dimensions before `SplitterDistance` so High-DPI WinForms
        // scaling never clamps against the default 150x100 unparented control size.
        _mainSplit = new SplitContainer
        {
            Size = new Size(1200, 700),
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            SplitterDistance = 330,
            FixedPanel = FixedPanel.Panel1
        };

        // --- TAB 1: CONTENTS TREE ---
        TabPage tabContents = new("Contents") { Padding = new Padding(4) };
        _contentsTree.AfterSelect += (_, e) =>
        {
            if (_suppressTreeSelectionEvent) return;
            if (e.Node?.Tag is NavTarget nav)
            {
                NavigateToTopic(nav.TopicId, nav.AnchorId);
            }
        };
        tabContents.Controls.Add(_contentsTree);

        // --- TAB 2: RULE INDEX (A–Z) ---
        TabPage tabIndex = new("Rule Index (25)") { Padding = new Padding(4) };
        _txtIndexFilter.TextChanged += (_, _) => PopulateRuleIndexList(_txtIndexFilter.Text);
        _lstRuleIndex.SelectedIndexChanged += (_, _) =>
        {
            if (_lstRuleIndex.SelectedItem is RuleListItem item)
            {
                NavigateToTopic("rule-reference", $"rule-{item.Rule.RuleId}");
            }
        };
        tabIndex.Controls.Add(_lstRuleIndex);
        tabIndex.Controls.Add(_txtIndexFilter);

        // --- TAB 3: FULL-TEXT SEARCH ---
        TabPage tabSearch = new("Search") { Padding = new Padding(4) };
        _txtSearchQuery.TextChanged += (_, _) => PopulateSearchResults(_txtSearchQuery.Text);
        _lstSearchResults.SelectedIndexChanged += (_, _) =>
        {
            if (_lstSearchResults.SelectedItem is SearchListItem item)
            {
                NavigateToTopic(item.Hit.TopicId, item.Hit.AnchorId);
            }
        };
        tabSearch.Controls.Add(_lstSearchResults);
        tabSearch.Controls.Add(_txtSearchQuery);

        _leftTabs.TabPages.AddRange([tabContents, tabIndex, tabSearch]);
        _mainSplit.Panel1.Controls.Add(_leftTabs);

        // --- RIGHT PANE: CHROMIUM HTML5 READER + FALLBACK ---
        _mainSplit.Panel2.Controls.Add(_helpWebView);
        _mainSplit.Panel2.Controls.Add(_fallbackReader);

        Controls.Add(_mainSplit);
        Controls.Add(_toolbar);
        Controls.Add(_statusStrip);
    }

    private void BuildStatusBar()
    {
        _statusStrip.Items.AddRange([_lblStatusTopic, _lblStatusMode]);
    }

    private void ApplySafeSplitterProportion()
    {
        try
        {
            if (_mainSplit.Width > 480)
            {
                _mainSplit.SplitterDistance = Math.Clamp(330, 240, _mainSplit.Width - 340);
            }
        }
        catch
        {
            // Ignore transient layout bounds
        }
    }

    private void PopulateContentsTree()
    {
        _contentsTree.BeginUpdate();
        _contentsTree.Nodes.Clear();
        _topicNodes.Clear();

        var grouped = StudioHelpCatalog.GetAllTopics().GroupBy(t => t.Category);
        foreach (var group in grouped)
        {
            TreeNode categoryNode = new(group.Key);
            foreach (HelpTopic topic in group)
            {
                TreeNode topicNode = new(topic.Title)
                {
                    Tag = new NavTarget(topic.TopicId, null),
                    ToolTipText = topic.Summary
                };
                _topicNodes[topic.TopicId] = topicNode;

                // Expand all 25 individual rules directly beneath the Rule Reference chapter
                // so engineers browsing the Contents tree can click any rule directly!
                if (topic.TopicId == "rule-reference")
                {
                    foreach (HelpRuleEntry rule in StudioHelpCatalog.GetAllRuleEntries())
                    {
                        string anchor = $"rule-{rule.RuleId}";
                        TreeNode ruleNode = new($"{rule.RuleId} ({rule.Severity})")
                        {
                            Tag = new NavTarget("rule-reference", anchor),
                            ToolTipText = $"{rule.Title} — {rule.WcagCriterion}"
                        };
                        _topicNodes[$"rule-reference#{anchor}"] = ruleNode;
                        topicNode.Nodes.Add(ruleNode);
                    }
                }

                categoryNode.Nodes.Add(topicNode);
            }

            _contentsTree.Nodes.Add(categoryNode);
            categoryNode.Expand();
        }

        _contentsTree.EndUpdate();
    }

    private void PopulateRuleIndexList(string filter)
    {
        string q = filter.Trim();
        _lstRuleIndex.BeginUpdate();
        _lstRuleIndex.Items.Clear();

        foreach (HelpRuleEntry rule in StudioHelpCatalog.GetAllRuleEntries())
        {
            if (!string.IsNullOrEmpty(q) &&
                !rule.RuleId.Contains(q, StringComparison.OrdinalIgnoreCase) &&
                !rule.Title.Contains(q, StringComparison.OrdinalIgnoreCase) &&
                !rule.WcagCriterion.Contains(q, StringComparison.OrdinalIgnoreCase) &&
                !rule.EngineCategory.Contains(q, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            _lstRuleIndex.Items.Add(new RuleListItem(rule));
        }

        _lstRuleIndex.EndUpdate();
    }

    private void PopulateSearchResults(string query)
    {
        IReadOnlyList<HelpSearchHit> hits = StudioHelpCatalog.Search(query);
        _lstSearchResults.BeginUpdate();
        _lstSearchResults.Items.Clear();

        foreach (HelpSearchHit hit in hits)
        {
            _lstSearchResults.Items.Add(new SearchListItem(hit));
        }

        _lstSearchResults.EndUpdate();
    }

    private void SyncTreeSelection(string topicId, string? anchorId)
    {
        string compositeKey = !string.IsNullOrEmpty(anchorId) ? $"{topicId}#{anchorId}" : topicId;
        if (!_topicNodes.TryGetValue(compositeKey, out TreeNode? node) &&
            !_topicNodes.TryGetValue(topicId, out node))
        {
            return;
        }

        _suppressTreeSelectionEvent = true;
        try
        {
            node.EnsureVisible();
            _contentsTree.SelectedNode = node;
        }
        finally
        {
            _suppressTreeSelectionEvent = false;
        }
    }

    private async Task InitializeHelpWebViewAsync()
    {
        UpdateFallbackText(_currentTopicId, _currentAnchorId);

        string userDataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RaggieSoft",
            "AccessibilityStudio",
            "WebView2Data"
        );

        try
        {
            Directory.CreateDirectory(userDataFolder);
            string helpFolder = ResolveOrMaterializeHelpDirectory();

            CoreWebView2Environment env = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
            await _helpWebView.EnsureCoreWebView2Async(env);

            _helpWebView.CoreWebView2.Settings.IsStatusBarEnabled = false;
            _helpWebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
            _helpWebView.CoreWebView2.Profile.PreferredColorScheme = _colorScheme;

            // Map `https://help.a11y.local/` to our local offline HTML5 Help folder
            _helpWebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                VirtualHelpHost,
                helpFolder,
                CoreWebView2HostResourceAccessKind.Allow);

            // Keep Back/Forward toolbar buttons & tree selection synced when clicking internal links
            _helpWebView.CoreWebView2.NavigationCompleted += (_, _) =>
            {
                _btnBack.Enabled = _helpWebView.CoreWebView2.CanGoBack;
                _btnForward.Enabled = _helpWebView.CoreWebView2.CanGoForward;
                SyncStateFromWebViewSource(_helpWebView.CoreWebView2.Source);
            };

            // Open any external `https://` links (if clicked) in the user's default system browser
            // rather than leaving the local Help manual!
            _helpWebView.CoreWebView2.NavigationStarting += (_, e) =>
            {
                if (Uri.TryCreate(e.Uri, UriKind.Absolute, out Uri? uri) &&
                    !string.Equals(uri.Host, VirtualHelpHost, StringComparison.OrdinalIgnoreCase) &&
                    uri.Scheme is "http" or "https")
                {
                    e.Cancel = true;
                    LaunchExternalUrl(uri.ToString());
                }
            };

            _webViewReady = true;
            NavigateToTopic(_currentTopicId, _currentAnchorId);
        }
        catch (Exception ex)
        {
            // Graceful air-gapped native fallback if WebView2 runtime is absent
            _helpWebView.Visible = false;
            _fallbackReader.Visible = true;
            _lblStatusMode.Text = $"Offline Native Reader ({ex.GetType().Name})";
        }
    }

    private static string ResolveOrMaterializeHelpDirectory()
    {
        // 1. Prefer `[INSTALLDIR]\HelpModules\raggiesoft.accessibility-studio\` (from `[INSTALLDIR]\StudioIDE\`)
        string installDirModule = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "HelpModules", "raggiesoft.accessibility-studio"));
        if (File.Exists(Path.Combine(installDirModule, "welcome-overview.html")) &&
            File.Exists(Path.Combine(installDirModule, "rule-reference.html")))
        {
            return installDirModule;
        }

        // 2. Check legacy `<BaseDir>\Help\`
        string installedHelpDir = Path.Combine(AppContext.BaseDirectory, "Help");
        if (File.Exists(Path.Combine(installedHelpDir, "welcome-overview.html")) &&
            File.Exists(Path.Combine(installedHelpDir, "rule-reference.html")))
        {
            return installedHelpDir;
        }

        // 3. Otherwise materialize the offline HTML5 Help bundle into `%LOCALAPPDATA%`
        string cachedHelpDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RaggieSoft",
            "AccessibilityStudio",
            "HelpBundle_v1_0"
        );

        StudioHelpCatalog.ExportOfflineHtml5Bundle(cachedHelpDir);
        return cachedHelpDir;
    }

    private void SyncStateFromWebViewSource(string? sourceUri)
    {
        if (string.IsNullOrWhiteSpace(sourceUri) ||
            !Uri.TryCreate(sourceUri, UriKind.Absolute, out Uri? uri) ||
            !string.Equals(uri.Host, VirtualHelpHost, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        string fileName = Path.GetFileNameWithoutExtension(uri.AbsolutePath);
        if (!string.IsNullOrWhiteSpace(fileName) && !string.Equals(fileName, "index", StringComparison.OrdinalIgnoreCase))
        {
            _currentTopicId = fileName;
        }

        _currentAnchorId = !string.IsNullOrWhiteSpace(uri.Fragment)
            ? uri.Fragment.TrimStart('#')
            : null;

        HelpTopic resolved = StudioHelpCatalog.GetTopicOrDefault(_currentTopicId);
        string frag = _currentAnchorId is not null ? $"#{_currentAnchorId}" : string.Empty;
        _lblStatusTopic.Text = $"Topic: {resolved.Title} ({_currentTopicId}.html{frag})";
        SyncTreeSelection(_currentTopicId, _currentAnchorId);
    }

    private void UpdateFallbackText(string topicId, string? anchorId)
    {
        HelpTopic topic = StudioHelpCatalog.GetTopicOrDefault(topicId);
        string plainBody = StripHtmlTagsRegex().Replace(topic.HtmlContent, string.Empty);
        plainBody = System.Net.WebUtility.HtmlDecode(plainBody);

        StringBuilder sb = new();
        sb.AppendLine($"=== {topic.Title.ToUpperInvariant()} ===");
        sb.AppendLine($"Category: {topic.Category}");
        if (!string.IsNullOrEmpty(anchorId))
        {
            sb.AppendLine($"Section:  #{anchorId}");
        }
        sb.AppendLine(new string('=', 76));
        sb.AppendLine(topic.Summary);
        sb.AppendLine();
        sb.AppendLine(plainBody.Trim());

        _fallbackReader.Text = sb.ToString();
    }

    private void NavigateBack()
    {
        if (_webViewReady && _helpWebView.CoreWebView2?.CanGoBack == true)
        {
            _helpWebView.CoreWebView2.GoBack();
        }
    }

    private void NavigateForward()
    {
        if (_webViewReady && _helpWebView.CoreWebView2?.CanGoForward == true)
        {
            _helpWebView.CoreWebView2.GoForward();
        }
    }

    private void PrintCurrentTopic()
    {
        if (_webViewReady && _helpWebView.CoreWebView2 is not null)
        {
            _ = _helpWebView.CoreWebView2.ExecuteScriptAsync("window.print();");
        }
    }

    private void OpenCompanionOnlineDocs()
    {
        string targetUrl = _currentAnchorId is not null && _currentAnchorId.StartsWith("rule-", StringComparison.OrdinalIgnoreCase)
            ? $"{StudioHelpCatalog.OnlineDocsBaseUrl}#{_currentAnchorId}"
            : StudioHelpCatalog.OnlineDocsBaseUrl;
        LaunchExternalUrl(targetUrl);
    }

    private static void LaunchExternalUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch
        {
            // Ignore if shell browser association is restricted by corporate policy
        }
    }

    private void OnSystemUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category is UserPreferenceCategory.General or UserPreferenceCategory.Color or UserPreferenceCategory.VisualStyle)
        {
            if (IsHandleCreated)
            {
                BeginInvoke(() => ApplyColorSchemePreference(_colorScheme));
            }
        }
    }

    [GeneratedRegex("<[^>]+>", RegexOptions.Compiled)]
    private static partial Regex StripHtmlTagsRegex();
}
