// ================================================================================
// FILE: Forms/MainIdeForm.cs
// PROJECT: AccessibilityStudio.WinForms (RaggieSoft Accessibility Studio IDE)
// LICENSE: MIT License
// ================================================================================
// PLAIN-ENGLISH OVERVIEW (100-LEVEL ENTERPRISE WINFORMS IDE ARCHITECTURE GUIDE):
//   What is this file?
//     `MainIdeForm` is the flagship multi-pane Windows workstation IDE window for
//     **RaggieSoft Accessibility Studio (Enterprise Edition v1.0)**. It combines
//     deterministic native C#/.NET 10 Windows Forms docking/splitter panels with
//     two embedded Chromium (`Microsoft.Web.WebView2`) surfaces:
//       1. **Monaco Syntax & Side-by-Side Diff Editor**: Gives web & compliance
//          engineers full VS Code syntax highlighting, line numbers, inline red/amber
//          WCAG squiggles, jump-to-line navigation, and Side-by-Side Before/After
//          Refactoring Diff previews (with an automatic zero-dependency air-gapped
//          fallback engine when offline).
//       2. **Live Chromium Visual DOM & Emulation Inspector**: Renders the active
//          HTML5/CSS source or authenticated staging URL live, highlights the exact
//          DOM node for any selected WCAG finding, and simulates **Forced-Colors
//          High Contrast**, **400% Reflow Zoom (WCAG 1.4.10)**, and **Sequential
//          Keyboard Tab-Order Badges (`[1]`, `[2]`, `[3]`)**.
//       3. **Authenticated SSO Browser & Rate-Limited Batch Queue**: Lets higher-ed
//          and public-sector engineers sign into SSO-protected portals once in an
//          interactive browser session, load a `.csv` schedule or `sitemap.xml`,
//          and audit hundreds of pages at a polite `500ms`–`10,000ms` throttle.
//       4. **Enterprise Reporting & Tamper-Evident Session Log**: Exports standalone
//          WCAG-compliant HTML5 reports, OASIS SARIF 2.1.0, formula-safe CSV, JSON,
//          and SHA-256 remediation audit logs (`.jsonl` / `.log`).
//
//   How Enterprise Group Policy (GPO) / MSI Registry Defaults Work:
//     When IT administrators deploy RaggieSoft Accessibility Studio via our
//     InstallAware Studio X18 `.msi` package (`msiexec /i ... COMPLIANCE_PROFILE=...`),
//     settings are written under `HKLM\Software\Policies\RaggieSoft\AccessibilityStudio`
//     or `HKCU\Software\RaggieSoft\AccessibilityStudio`. On startup, `LoadEnterprisePolicyDefaults()`
//     reads those registry values safely on Windows (`OperatingSystem.IsWindows()`)!
// ================================================================================

using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Text;
using System.Text.Json;
using System.Windows.Forms;
using AccessibilityStudio.Core.Documentation;
using AccessibilityStudio.Core.Models;
using AccessibilityStudio.Core.Refactoring;
using AccessibilityStudio.Core.Services;
using AccessibilityStudio.Reporting.Exporters;
using AccessibilityStudio.WinForms.Editor;
using AccessibilityStudio.WinForms.Inspector;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using Microsoft.Win32;

namespace AccessibilityStudio.WinForms.Forms;

public sealed class MainIdeForm : Form
{
    // ----------------------------------------------------------------------------
    // 1. STATE & WORKSPACE SERVICES
    // ----------------------------------------------------------------------------

    // Optional path passed from Windows Explorer ProgId / Folder Context Menu (`args[0]`).
    private readonly string? _startupTarget;

    // Thread-safe session logger recording SHA-256 hashes before/after every scan & guided fix.
    private readonly AuditSessionLogger _sessionLogger = new();

    // Active workspace project metadata (`.a11yproj`).
    private AccessibilityWorkspaceProject _currentProject = new();
    private string? _currentProjectFilePath;

    // Active file path & live in-memory source buffer currently open in the Monaco editor.
    private string _activeTargetPath = "SampleCourseModule.html";
    private string _activeBufferContent = string.Empty;
    private bool _isBufferDirty;
    private bool _isInDiffMode;

    // Built-in virtual sample files so engineers can test HTML5, CSS, and WebVTT auditing immediately on first launch.
    private readonly Dictionary<string, string> _inMemorySamples = new(StringComparer.OrdinalIgnoreCase);

    // Latest single-file audit result and full workspace/batch audit report for report exporting.
    private FileAuditResult? _latestFileResult;
    private WorkspaceAuditReport? _latestWorkspaceReport;
    private RefactorPreviewResult? _pendingRefactorPreview;

    // Emulation toggle flags for the Live Chromium DOM Inspector.
    private bool _emulateForcedColors;
    private bool _emulateReflow400;
    private bool _showTabOrderBadges;

    // Color-scheme override for embedded Monaco & Live Inspector WebView2 surfaces
    // (`Auto` = follows Windows `prefers-color-scheme` via .NET 10 `SystemColorMode.System`).
    private CoreWebView2PreferredColorScheme _preferredColorScheme = CoreWebView2PreferredColorScheme.Auto;

    // Main splitters stored as fields so proportional distances apply cleanly after DPI layout.
    private SplitContainer _outerSplit = null!;
    private SplitContainer _rightVerticalSplit = null!;

    // Cancellation token source for stopping long-running polite batch scans cleanly.
    private CancellationTokenSource? _batchCts;

    // Reusable non-modal F1 Offline HTML5 Help Viewer window (`HelpViewerForm`) so engineers
    // can dock Help side-by-side on a second monitor while editing source code!
    private HelpViewerForm? _helpViewerWindow;

    // Readiness flags for our three WebView2 Chromium instances.
    private bool _editorWebViewReady;
    private bool _inspectorWebViewReady;
    private bool _ssoWebViewReady;

    // ----------------------------------------------------------------------------
    // 2. WINFORMS UI CONTROLS (MENUS, TOOLBAR, SPLITTERS, TABS, GRIDS, STATUS BAR)
    // ----------------------------------------------------------------------------

    private readonly MenuStrip _mainMenu = new() { AccessibleName = "Main IDE Menu Bar" };
    private readonly ToolStrip _mainToolbar = new() { GripStyle = ToolStripGripStyle.Hidden, AccessibleName = "Accessibility Engineering Toolbar" };
    private readonly StatusStrip _statusBar = new() { AccessibleName = "IDE Status Bar" };

    // Toolbar dynamic controls
    private readonly ToolStripComboBox _cmbProfile = new()
    {
        DropDownStyle = ComboBoxStyle.DropDownList,
        AutoSize = false,
        Width = 225,
        AccessibleName = "Compliance Standard Profile"
    };
    private readonly ToolStripButton _btnToggleHighContrast = new() { Text = "Forced-Colors HC", CheckOnClick = true, ToolTipText = "Simulate Windows High-Contrast Forced-Colors palette in Live Inspector" };
    private readonly ToolStripButton _btnToggleReflow400 = new() { Text = "400% Reflow", CheckOnClick = true, ToolTipText = "Test WCAG 1.4.10 Reflow at 400% zoom factor in Live Inspector" };
    private readonly ToolStripButton _btnToggleTabOrder = new() { Text = "Tab Order [1..N]", CheckOnClick = true, ToolTipText = "Overlay sequential keyboard focus order badges in Live Inspector" };

    // Left Workspace Explorer controls
    private readonly TextBox _txtExplorerFilter = new()
    {
        PlaceholderText = "Filter workspace files (*.html, *.css, *.vtt)…",
        Dock = DockStyle.Top,
        AccessibleName = "Filter Workspace Files"
    };
    private readonly TreeView _workspaceTree = new()
    {
        Dock = DockStyle.Fill,
        HideSelection = false,
        ShowNodeToolTips = true,
        AccessibleName = "Workspace Solution Explorer"
    };
    private readonly List<string> _discoveredWorkspaceFiles = [];

    // Center Workspace Tabs (`Editor`, `Live DOM Inspector`, `SSO & Batch Queue`)
    private readonly TabControl _centerTabs = new() { Dock = DockStyle.Fill, AccessibleName = "Document and Browser Views" };
    private readonly WebView2 _editorWebView = new() { Dock = DockStyle.Fill, AccessibleName = "Monaco Syntax and Side-by-Side Diff Code Editor" };
    private readonly WebView2 _inspectorWebView = new() { Dock = DockStyle.Fill, AccessibleName = "Live Chromium DOM and Accessibility Emulation Inspector" };
    private readonly WebView2 _ssoBrowserWebView = new() { Dock = DockStyle.Fill, AccessibleName = "Authenticated SSO Portal Browser" };

    // Fallback label shown on non-Windows platforms or if WebView2 Evergreen Runtime is missing
    private readonly TextBox _nativeFallbackEditor = new()
    {
        Dock = DockStyle.Fill,
        Multiline = true,
        ScrollBars = ScrollBars.Both,
        WordWrap = false,
        AcceptsTab = true,
        Font = new Font("Consolas", 10.5F),
        Visible = false,
        AccessibleName = "Fallback Source Code Editor"
    };

    // Batch Queue Tab controls
    private readonly TextBox _txtSsoUrl = new()
    {
        Text = "https://raggiesoft.com",
        Width = 360,
        AccessibleName = "SSO Portal or Target Staging URL"
    };
    private readonly NumericUpDown _numThrottleMs = new()
    {
        Minimum = 0,
        Maximum = 30000,
        Increment = 250,
        Value = 1500,
        Width = 90,
        AccessibleName = "Polite Inter-Request Throttle Delay in Milliseconds"
    };
    private readonly CheckBox _chkUseAuthenticatedWebView = new()
    {
        Text = "Capture rendered DOM via Authenticated SSO Browser",
        AutoSize = true,
        Checked = true
    };
    private readonly ListBox _lstBatchTargets = new()
    {
        Dock = DockStyle.Left,
        Width = 340,
        IntegralHeight = false,
        AccessibleName = "Queued Batch Audit URLs and Files"
    };
    private readonly ProgressBar _batchProgressBar = new() { Width = 200, Height = 20 };
    private readonly Label _lblBatchStatus = new() { Text = "Queue idle.", AutoSize = true, Padding = new Padding(6, 4, 0, 0) };
    private Button _btnStartBatch = null!;
    private Button _btnCancelBatch = null!;

    // Bottom Dock Tabs (`Error List`, `Guided Fix Inspector`, `Document Outline`, `Session Log`)
    private readonly TabControl _bottomTabs = new() { Dock = DockStyle.Fill, AccessibleName = "Audit Diagnostics, Guided Fixes, Document Outline, and Session Log" };
    private readonly ComboBox _cmbSeverityFilter = new()
    {
        DropDownStyle = ComboBoxStyle.DropDownList,
        Width = 190,
        AccessibleName = "Filter Error List by Severity"
    };
    private readonly DataGridView _errorGrid = new()
    {
        Dock = DockStyle.Fill,
        ReadOnly = true,
        AllowUserToAddRows = false,
        AllowUserToDeleteRows = false,
        AllowUserToResizeRows = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        MultiSelect = false,
        AutoGenerateColumns = false,
        RowHeadersVisible = false,
        AccessibleName = "WCAG 2.2 and Revised Section 508 Error List"
    };
    private readonly BindingList<AuditFinding> _visibleFindings = [];

    // Guided Fix Inspector controls
    private readonly TextBox _txtFixDetails = new()
    {
        Dock = DockStyle.Fill,
        Multiline = true,
        ReadOnly = true,
        ScrollBars = ScrollBars.Vertical,
        Font = new Font("Consolas", 10F),
        AccessibleName = "Selected Rule Guidance and Suggested Code Refactoring"
    };
    private Button _btnPreviewDiff = null!;
    private Button _btnApplyFix = null!;
    private Button _btnExitDiff = null!;

    // Document Heading & Landmark Outline Tree
    private readonly TreeView _outlineTree = new()
    {
        Dock = DockStyle.Fill,
        HideSelection = false,
        AccessibleName = "Screen-Reader Landmark and Heading Hierarchy Outline"
    };

    // Audit & Remediation Session Log ListView
    private readonly ListView _sessionLogView = new()
    {
        Dock = DockStyle.Fill,
        View = View.Details,
        FullRowSelect = true,
        GridLines = true,
        AccessibleName = "Tamper-Evident Audit and Remediation Session Log"
    };

    // StatusStrip labels
    private readonly ToolStripStatusLabel _statusTarget = new() { Text = "Target: SampleCourseModule.html", Spring = true, TextAlign = ContentAlignment.MiddleLeft };
    private readonly ToolStripStatusLabel _statusCursor = new() { Text = "Ln 1, Col 1", BorderSides = ToolStripStatusLabelBorderSides.Left };
    private readonly ToolStripStatusLabel _statusCounts = new() { Text = "Findings: 0", BorderSides = ToolStripStatusLabelBorderSides.Left };
    private readonly ToolStripStatusLabel _statusScore = new() { Text = "Health Score: 100%", BorderSides = ToolStripStatusLabelBorderSides.Left };
    private readonly ToolStripStatusLabel _statusEngine = new() { Text = "Editor: Initializing…", BorderSides = ToolStripStatusLabelBorderSides.Left };

    // ----------------------------------------------------------------------------
    // 3. CONSTRUCTOR & INITIALIZATION PIPELINE
    // ----------------------------------------------------------------------------

    public MainIdeForm(string? startupTarget = null)
    {
        _startupTarget = startupTarget;

        // Configure top-level workstation window geometry and title
        Text = "RaggieSoft Accessibility Studio — Enterprise WCAG 2.2 & Section 508 Web IDE";
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(1480, 920);
        MinimumSize = new Size(1080, 680);
        KeyPreview = true;

        // Load optional window icon from `Assets/AccessibilityStudio.ico`
        string iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "AccessibilityStudio.ico");
        if (File.Exists(iconPath))
        {
            Icon = new Icon(iconPath);
        }

        // Subscribe to real-time session log events so every scan or refactor appears in Tab 4
        _sessionLogger.EntryRecorded += AppendLogEntryToListView;

        // Seed built-in realistic sample HTML5, CSS, and WebVTT files
        SeedBuiltInSampleWorkspace();

        // Build all menus, toolbars, splitters, editors, grids, and status indicators
        BuildMenusAndToolbar();
        BuildMainSplitterWorkspace();
        BuildStatusStrip();

        // Apply any enterprise IT Group Policy (`HKLM` / `HKCU`) registry settings
        LoadEnterprisePolicyDefaults();

        // Listen for live Windows 11 Light/Dark/Contrast theme changes while the IDE is open
        if (OperatingSystem.IsWindows())
        {
            SystemEvents.UserPreferenceChanged += OnSystemUserPreferenceChanged;
        }

        // Route standard Win32 `WM_HELP` (`F1`) events to our context-sensitive HTML5 Help Viewer
        HelpRequested += (_, hlpevent) =>
        {
            hlpevent.Handled = true;
            OpenContextSensitiveHelp(forceRuleTopic: false);
        };

        // Initialize WebView2 instances and proportional splitters once the window handle & DPI layout are ready.
        // Uses our shared 250 ms delayed-threshold splash card so fast warm launches never flash, while cold-start
        // Chromium / workspace initialization shows crisp RaggieSoft progress!
        Load += async (_, _) =>
        {
            using var splash = RaggieSoftStartupSplashScope.Begin(
                "RaggieSoft Accessibility Studio",
                "Preparing workspace and offline rule catalog...");
            ApplyDefaultSplitterProportions();
            await InitializeIdeAsync(splash);
            await splash.CompleteAsync();
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
        // Handle standalone `Escape` here because WinForms `ToolStripManager.IsValidShortcut`
        // forbids unmodified `Keys.Escape` on `ToolStripMenuItem.ShortcutKeys`!
        if (keyData == Keys.Escape && _isInDiffMode)
        {
            ExitDiffMode();
            return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    // ----------------------------------------------------------------------------
    // 4. ENTERPRISE MENU BAR, TOOLBAR, & SPLIT-PANE DOCKING LAYOUT
    // ----------------------------------------------------------------------------

    private void BuildMenusAndToolbar()
    {
        // --- MENU BAR (`MenuStrip`) ---
        ToolStripMenuItem mnuFile = new("&File");
        mnuFile.DropDownItems.Add(CreateMenuItem("&Open Source File…", Keys.Control | Keys.O, (_, _) => PromptOpenSingleFile()));
        mnuFile.DropDownItems.Add(CreateMenuItem("Open &Folder Workspace…", Keys.Control | Keys.Shift | Keys.O, (_, _) => PromptOpenFolderWorkspace()));
        mnuFile.DropDownItems.Add(new ToolStripSeparator());
        mnuFile.DropDownItems.Add(CreateMenuItem("Open Enterprise &Project (.a11yproj)…", Keys.None, (_, _) => PromptOpenProjectFile()));
        mnuFile.DropDownItems.Add(CreateMenuItem("Save Enterprise P&roject (.a11yproj)…", Keys.None, (_, _) => PromptSaveProjectFile()));
        mnuFile.DropDownItems.Add(new ToolStripSeparator());
        mnuFile.DropDownItems.Add(CreateMenuItem("&Save Active Source File", Keys.Control | Keys.S, (_, _) => SaveActiveSourceFile()));
        mnuFile.DropDownItems.Add(CreateMenuItem("Load Built-In &Sample Course Workspace", Keys.None, (_, _) => LoadBuiltInSampleIntoExplorer()));
        mnuFile.DropDownItems.Add(new ToolStripSeparator());
        mnuFile.DropDownItems.Add(CreateMenuItem("E&xit", Keys.Alt | Keys.F4, (_, _) => Close()));

        ToolStripMenuItem mnuEdit = new("&Edit");
        mnuEdit.DropDownItems.Add(CreateMenuItem("Jump to Selected &Finding Line", Keys.F8, (_, _) => JumpToSelectedFinding()));
        mnuEdit.DropDownItems.Add(CreateMenuItem("Copy Selected Rule &Citation", Keys.Control | Keys.Shift | Keys.R, (_, _) => CopySelectedFindingCitation()));

        ToolStripMenuItem mnuAudit = new("&Audit");
        mnuAudit.DropDownItems.Add(CreateMenuItem("&Run Source Audit on Active Buffer", Keys.F5, (_, _) => RunActiveBufferAudit()));
        mnuAudit.DropDownItems.Add(CreateMenuItem("Audit &Entire Open Folder Workspace", Keys.Control | Keys.F5, (_, _) => RunFullFolderAudit()));

        ToolStripMenuItem mnuRefactor = new("&Refactor");
        mnuRefactor.DropDownItems.Add(CreateMenuItem("&Preview Side-by-Side Diff for Selected Finding", Keys.Control | Keys.D, (_, _) => PreviewGuidedFixDiff()));
        mnuRefactor.DropDownItems.Add(CreateMenuItem("&Apply Guided Fix to Editor Buffer", Keys.Control | Keys.Shift | Keys.F, (_, _) => ApplyGuidedFixToBuffer()));
        mnuRefactor.DropDownItems.Add(CreateMenuItem("E&xit Diff View (Return to Code Editor)", Keys.Escape, (_, _) => ExitDiffMode()));

        ToolStripMenuItem mnuEmulate = new("E&mulate");
        mnuEmulate.DropDownItems.Add(CreateMenuItem("Toggle &Forced-Colors High Contrast Simulation", Keys.None, (_, _) =>
        {
            _btnToggleHighContrast.Checked = !_btnToggleHighContrast.Checked;
            _ = ApplyInspectorEmulationStatesAsync();
        }));
        mnuEmulate.DropDownItems.Add(CreateMenuItem("Toggle &400% Reflow Zoom (WCAG 1.4.10)", Keys.None, (_, _) =>
        {
            _btnToggleReflow400.Checked = !_btnToggleReflow400.Checked;
            _ = ApplyInspectorEmulationStatesAsync();
        }));
        mnuEmulate.DropDownItems.Add(CreateMenuItem("Toggle Keyboard &Tab-Order Badges [1..N]", Keys.None, (_, _) =>
        {
            _btnToggleTabOrder.Checked = !_btnToggleTabOrder.Checked;
            _ = ApplyInspectorEmulationStatesAsync();
        }));

        ToolStripMenuItem mnuBatch = new("&Batch Queue");
        mnuBatch.DropDownItems.Add(CreateMenuItem("Open &SSO Browser & Rate-Limited Batch Queue", Keys.None, (_, _) => _centerTabs.SelectedIndex = 2));
        mnuBatch.DropDownItems.Add(CreateMenuItem("&Load CSV Schedule or Sitemap.xml…", Keys.None, (_, _) => PromptLoadBatchSchedule()));

        ToolStripMenuItem mnuReports = new("&Reports");
        mnuReports.DropDownItems.Add(CreateMenuItem("Export Accessible &HTML5 Audit Report…", Keys.None, (_, _) => PromptExportReport("html")));
        mnuReports.DropDownItems.Add(CreateMenuItem("Export OASIS &SARIF 2.1.0 (GitHub / Azure DevOps)…", Keys.None, (_, _) => PromptExportReport("sarif")));
        mnuReports.DropDownItems.Add(CreateMenuItem("Export Formula-Safe &CSV Spreadsheet…", Keys.None, (_, _) => PromptExportReport("csv")));
        mnuReports.DropDownItems.Add(CreateMenuItem("Export Machine-Readable &JSON Snapshot…", Keys.None, (_, _) => PromptExportReport("json")));
        mnuReports.DropDownItems.Add(new ToolStripSeparator());
        mnuReports.DropDownItems.Add(CreateMenuItem("Export Session Audit &Log (.jsonl)…", Keys.None, (_, _) => PromptExportSessionLog(jsonl: true)));
        mnuReports.DropDownItems.Add(CreateMenuItem("Export Human-Readable Session &Transcript (.log)…", Keys.None, (_, _) => PromptExportSessionLog(jsonl: false)));

        ToolStripMenuItem mnuTools = new("&Tools");
        mnuTools.DropDownItems.Add(CreateMenuItem("WCAG 2.2 Color &Contrast Workbench…", Keys.Control | Keys.Shift | Keys.C, (_, _) =>
        {
            using ContrastWorkbenchDialog dlg = new(_preferredColorScheme);
            dlg.ShowDialog(this);
        }));

        ToolStripMenuItem mnuTheme = new("Editor &Theme");
        mnuTheme.DropDownItems.Add(CreateMenuItem("Follow Windows &System Mode (prefers-color-scheme)", Keys.None, (_, _) => ApplyWebViewThemePreference(CoreWebView2PreferredColorScheme.Auto)));
        mnuTheme.DropDownItems.Add(CreateMenuItem("Force &Dark Editor Theme (VS Dark)", Keys.None, (_, _) => ApplyWebViewThemePreference(CoreWebView2PreferredColorScheme.Dark)));
        mnuTheme.DropDownItems.Add(CreateMenuItem("Force &Light Editor Theme (VS Light)", Keys.None, (_, _) => ApplyWebViewThemePreference(CoreWebView2PreferredColorScheme.Light)));
        mnuTools.DropDownItems.Add(new ToolStripSeparator());
        mnuTools.DropDownItems.Add(mnuTheme);

        ToolStripMenuItem mnuHelp = new("&Help");
        mnuHelp.DropDownItems.Add(CreateMenuItem("&View Help && Context Topic", Keys.F1, (_, _) => OpenContextSensitiveHelp(forceRuleTopic: false)));
        mnuHelp.DropDownItems.Add(CreateMenuItem("Rule Reference for &Selected Finding", Keys.Shift | Keys.F1, (_, _) => OpenContextSensitiveHelp(forceRuleTopic: true)));
        mnuHelp.DropDownItems.Add(new ToolStripSeparator());
        mnuHelp.DropDownItems.Add(CreateMenuItem("&Quickstart && Compliance Profiles", Keys.None, (_, _) => OpenStudioHelp("quickstart-workspace")));
        mnuHelp.DropDownItems.Add(CreateMenuItem("&Guided Source Refactoring Guide", Keys.None, (_, _) => OpenStudioHelp("guided-refactoring")));
        mnuHelp.DropDownItems.Add(CreateMenuItem("Complete &25-Rule WCAG && 508 Catalog", Keys.None, (_, _) => OpenStudioHelp("rule-reference")));
        mnuHelp.DropDownItems.Add(CreateMenuItem("Headless &CLI && CI/CD Scanner Reference", Keys.None, (_, _) => OpenStudioHelp("cli-cicd-reference")));
        mnuHelp.DropDownItems.Add(CreateMenuItem("&Enterprise Deployment && Group Policy Guide", Keys.None, (_, _) => OpenStudioHelp("enterprise-msi-gpo")));
        mnuHelp.DropDownItems.Add(CreateMenuItem("&Keyboard Shortcuts", Keys.None, (_, _) => OpenStudioHelp("keyboard-shortcuts")));
        mnuHelp.DropDownItems.Add(new ToolStripSeparator());
        mnuHelp.DropDownItems.Add(CreateMenuItem("Open &Online Documentation (a11y.raggiesoft.com/docs)…", Keys.None, (_, _) => LaunchOnlineDocumentation()));
        mnuHelp.DropDownItems.Add(new ToolStripSeparator());
        mnuHelp.DropDownItems.Add(CreateMenuItem("&About RaggieSoft Accessibility Studio…", Keys.None, (_, _) =>
        {
            using AboutEnterpriseDialog dlg = new();
            dlg.ShowDialog(this);
        }));

        _mainMenu.Items.AddRange([mnuFile, mnuEdit, mnuAudit, mnuRefactor, mnuEmulate, mnuBatch, mnuReports, mnuTools, mnuHelp]);

        // --- QUICK-ACTION ENTERPRISE TOOLBAR (`ToolStrip`) ---
        _cmbProfile.Items.AddRange([
            "WCAG 2.2 AA + Section 508 Web",
            "WCAG 2.2 Level A Baseline",
            "ADA Title II Public/Higher-Ed Web",
            "Strict Enterprise All (+Advisories)"
        ]);
        _cmbProfile.SelectedIndex = 0;
        _cmbProfile.SelectedIndexChanged += (_, _) =>
        {
            _currentProject.ComplianceProfile = GetSelectedProfile();
            RunActiveBufferAudit();
        };

        _btnToggleHighContrast.CheckedChanged += (_, _) =>
        {
            _emulateForcedColors = _btnToggleHighContrast.Checked;
            _ = ApplyInspectorEmulationStatesAsync();
        };
        _btnToggleReflow400.CheckedChanged += (_, _) =>
        {
            _emulateReflow400 = _btnToggleReflow400.Checked;
            _ = ApplyInspectorEmulationStatesAsync();
        };
        _btnToggleTabOrder.CheckedChanged += (_, _) =>
        {
            _showTabOrderBadges = _btnToggleTabOrder.Checked;
            _ = ApplyInspectorEmulationStatesAsync();
        };

        _mainToolbar.Items.Add(CreateToolbarButton("📂 Open Folder", "Open a web project folder for source auditing", (_, _) => PromptOpenFolderWorkspace()));
        _mainToolbar.Items.Add(CreateToolbarButton("🧪 Sample Module", "Load built-in HTML5/CSS/WebVTT sample files", (_, _) => LoadBuiltInSampleIntoExplorer()));
        _mainToolbar.Items.Add(CreateToolbarButton("💾 Save", "Save active editor file (Ctrl+S)", (_, _) => SaveActiveSourceFile()));
        _mainToolbar.Items.Add(new ToolStripSeparator());
        _mainToolbar.Items.Add(CreateToolbarButton("▶ Run Audit (F5)", "Audit current source buffer & refresh Live Inspector", (_, _) => RunActiveBufferAudit()));
        _mainToolbar.Items.Add(CreateToolbarButton("⚡ Audit Folder", "Audit every HTML/CSS/VTT file in workspace", (_, _) => RunFullFolderAudit()));
        _mainToolbar.Items.Add(new ToolStripSeparator());
        _mainToolbar.Items.Add(CreateToolbarButton("🔍 Preview Diff", "Compare Before/After Guided Fix side-by-side (Ctrl+D)", (_, _) => PreviewGuidedFixDiff()));
        _mainToolbar.Items.Add(CreateToolbarButton("✔ Apply Fix", "Apply Guided Fix to editor buffer (Ctrl+Shift+F)", (_, _) => ApplyGuidedFixToBuffer()));
        _mainToolbar.Items.Add(new ToolStripSeparator());
        _mainToolbar.Items.Add(new ToolStripLabel("Profile:"));
        _mainToolbar.Items.Add(_cmbProfile);
        _mainToolbar.Items.Add(new ToolStripSeparator());
        _mainToolbar.Items.Add(_btnToggleHighContrast);
        _mainToolbar.Items.Add(_btnToggleReflow400);
        _mainToolbar.Items.Add(_btnToggleTabOrder);
        _mainToolbar.Items.Add(new ToolStripSeparator());
        _mainToolbar.Items.Add(CreateToolbarButton("📊 HTML5 Report", "Export accessible HTML5 executive & developer report", (_, _) => PromptExportReport("html")));
        _mainToolbar.Items.Add(CreateToolbarButton("🛡 SARIF 2.1", "Export OASIS SARIF 2.1.0 static analysis file", (_, _) => PromptExportReport("sarif")));
        _mainToolbar.Items.Add(CreateToolbarButton("🎨 Contrast", "Open WCAG 2.2 Color Contrast Workbench", (_, _) =>
        {
            using ContrastWorkbenchDialog dlg = new(_preferredColorScheme);
            dlg.ShowDialog(this);
        }));
        _mainToolbar.Items.Add(CreateToolbarButton("❓ Help (F1)", "Open offline HTML5 Help Manual & Rule Reference (F1 / Shift+F1)", (_, _) => OpenContextSensitiveHelp(forceRuleTopic: false)));

        MainMenuStrip = _mainMenu;
    }

    private void BuildMainSplitterWorkspace()
    {
        // Outer horizontal split: Left = Workspace Explorer (`285px`), Right = Editor + Bottom Diagnostics.
        // Note: Set an explicit large `Size` BEFORE `SplitterDistance` because a newly constructed
        // `SplitContainer` defaults to `150x100` prior to layout, which would clamp `490px` down!
        _outerSplit = new SplitContainer
        {
            Size = new Size(1460, 820),
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            SplitterDistance = 285,
            FixedPanel = FixedPanel.Panel1
        };

        // --- LEFT PANE: WORKSPACE SOLUTION EXPLORER ---
        Panel explorerPanel = new() { Dock = DockStyle.Fill, Padding = new Padding(6) };
        Label lblExplorerHeader = new()
        {
            Text = "WORKSPACE EXPLORER",
            Dock = DockStyle.Top,
            Height = 24,
            Font = new Font("Segoe UI", 8.5F, FontStyle.Bold)
        };

        _txtExplorerFilter.TextChanged += (_, _) => RebuildWorkspaceTreeNodes();
        _workspaceTree.NodeMouseDoubleClick += (_, e) =>
        {
            if (e.Node?.Tag is string targetKey)
            {
                OpenWorkspaceTarget(targetKey);
            }
        };
        _workspaceTree.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter && _workspaceTree.SelectedNode?.Tag is string targetKey)
            {
                e.Handled = true;
                OpenWorkspaceTarget(targetKey);
            }
        };

        explorerPanel.Controls.Add(_workspaceTree);
        explorerPanel.Controls.Add(_txtExplorerFilter);
        explorerPanel.Controls.Add(lblExplorerHeader);
        _outerSplit.Panel1.Controls.Add(explorerPanel);

        // Inner vertical split on the right: Top = Editor/Inspector/Batch (`62%`), Bottom = Error List/Outline/Logs (`38%`)
        _rightVerticalSplit = new SplitContainer
        {
            Size = new Size(1160, 820),
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            SplitterDistance = 490
        };

        // --- UPPER-RIGHT CENTER TABS ---
        TabPage tabEditor = new("1. Code & Side-by-Side Diff Editor (Monaco)");
        _nativeFallbackEditor.TextChanged += (_, _) =>
        {
            _activeBufferContent = _nativeFallbackEditor.Text;
            _isBufferDirty = true;
        };
        tabEditor.Controls.Add(_editorWebView);
        tabEditor.Controls.Add(_nativeFallbackEditor);

        TabPage tabInspector = new("2. Live Chromium DOM & Emulation Inspector");
        tabInspector.Controls.Add(_inspectorWebView);

        TabPage tabBatch = new("3. Authenticated SSO Browser & Rate-Limited Batch Queue");
        BuildBatchQueueTab(tabBatch);

        _centerTabs.TabPages.AddRange([tabEditor, tabInspector, tabBatch]);
        _rightVerticalSplit.Panel1.Controls.Add(_centerTabs);

        // --- LOWER-RIGHT DIAGNOSTICS & LOGGING TABS ---
        TabPage tabErrors = new("WCAG 2.2 & Section 508 Error List");
        BuildErrorListTab(tabErrors);

        TabPage tabGuidedFix = new("Rule Reference & Guided Fix Inspector");
        BuildGuidedFixTab(tabGuidedFix);

        TabPage tabOutline = new("Heading & Landmark Outline");
        _outlineTree.NodeMouseDoubleClick += (_, e) =>
        {
            if (e.Node?.Tag is int line && line > 0)
            {
                RevealEditorPosition(line, 1);
            }
        };
        tabOutline.Controls.Add(_outlineTree);

        TabPage tabLogs = new("Audit & Remediation Session Log");
        BuildSessionLogTab(tabLogs);

        _bottomTabs.TabPages.AddRange([tabErrors, tabGuidedFix, tabOutline, tabLogs]);
        _rightVerticalSplit.Panel2.Controls.Add(_bottomTabs);

        _outerSplit.Panel2.Controls.Add(_rightVerticalSplit);

        // Add controls in WinForms Z-order (`DockStyle.Fill` added first, `Top`/`Bottom` bars added after)
        Controls.Add(_outerSplit);
        Controls.Add(_mainToolbar);
        Controls.Add(_mainMenu);
        Controls.Add(_statusBar);
    }

    private void ApplyDefaultSplitterProportions()
    {
        try
        {
            if (_outerSplit.Width > 420)
            {
                _outerSplit.SplitterDistance = Math.Clamp(285, 200, _outerSplit.Width - 320);
            }
            if (_rightVerticalSplit.Height > 320)
            {
                _rightVerticalSplit.SplitterDistance = (int)(_rightVerticalSplit.Height * 0.60);
            }
        }
        catch
        {
            // Ignore transient layout bounds on minimized startup
        }
    }

    private void BuildBatchQueueTab(TabPage tabBatch)
    {
        Panel topBar = new() { Dock = DockStyle.Top, Height = 82, Padding = new Padding(8) };

        FlowLayoutPanel row1 = new() { Dock = DockStyle.Top, Height = 34, WrapContents = false };
        Button btnGoSso = new() { Text = "Navigate SSO Browser", AutoSize = true };
        Button btnAuditCurrentPage = new() { Text = "Audit Live SSO DOM Now", AutoSize = true };
        Button btnLoadSchedule = new() { Text = "Load .CSV / sitemap.xml…", AutoSize = true };

        btnGoSso.Click += (_, _) =>
        {
            if (_ssoWebViewReady && Uri.TryCreate(_txtSsoUrl.Text.Trim(), UriKind.Absolute, out Uri? uri))
            {
                _ssoBrowserWebView.CoreWebView2.Navigate(uri.ToString());
            }
        };
        btnAuditCurrentPage.Click += async (_, _) => await AuditCurrentSsoPageDomAsync();
        btnLoadSchedule.Click += (_, _) => PromptLoadBatchSchedule();

        row1.Controls.Add(new Label { Text = "Portal / SSO URL:", AutoSize = true, Padding = new Padding(0, 6, 4, 0) });
        row1.Controls.Add(_txtSsoUrl);
        row1.Controls.Add(btnGoSso);
        row1.Controls.Add(btnAuditCurrentPage);
        row1.Controls.Add(btnLoadSchedule);

        FlowLayoutPanel row2 = new() { Dock = DockStyle.Top, Height = 34, WrapContents = false };
        _btnStartBatch = new Button { Text = "▶ Start Polite Batch Queue", AutoSize = true };
        _btnCancelBatch = new Button { Text = "⏹ Cancel", AutoSize = true, Enabled = false };

        _btnStartBatch.Click += async (_, _) => await StartBatchQueueExecutionAsync();
        _btnCancelBatch.Click += (_, _) => _batchCts?.Cancel();

        row2.Controls.Add(new Label { Text = "Throttle (ms):", AutoSize = true, Padding = new Padding(0, 6, 4, 0) });
        row2.Controls.Add(_numThrottleMs);
        row2.Controls.Add(_chkUseAuthenticatedWebView);
        row2.Controls.Add(_btnStartBatch);
        row2.Controls.Add(_btnCancelBatch);
        row2.Controls.Add(_batchProgressBar);
        row2.Controls.Add(_lblBatchStatus);

        topBar.Controls.Add(row2);
        topBar.Controls.Add(row1);

        Panel bodyPanel = new() { Dock = DockStyle.Fill };
        bodyPanel.Controls.Add(_ssoBrowserWebView);
        bodyPanel.Controls.Add(_lstBatchTargets);

        tabBatch.Controls.Add(bodyPanel);
        tabBatch.Controls.Add(topBar);
    }

    private void BuildErrorListTab(TabPage tabErrors)
    {
        FlowLayoutPanel filterBar = new() { Dock = DockStyle.Top, Height = 34, Padding = new Padding(6, 4, 6, 2) };
        _cmbSeverityFilter.Items.AddRange([
            "All Severities",
            "Critical Only",
            "Critical & Serious",
            "Guided Fixes Available Only"
        ]);
        _cmbSeverityFilter.SelectedIndex = 0;
        _cmbSeverityFilter.SelectedIndexChanged += (_, _) => RefreshVisibleFindingsGrid();

        Button btnJump = new() { Text = "Jump to Line (Enter)", AutoSize = true };
        Button btnDiff = new() { Text = "Preview Guided Fix Diff (Ctrl+D)", AutoSize = true };
        Button btnRuleHelp = new() { Text = "📖 Rule Help (Shift+F1)", AutoSize = true };
        btnJump.Click += (_, _) => JumpToSelectedFinding();
        btnDiff.Click += (_, _) => PreviewGuidedFixDiff();
        btnRuleHelp.Click += (_, _) => OpenContextSensitiveHelp(forceRuleTopic: true);

        filterBar.Controls.Add(new Label { Text = "Show:", AutoSize = true, Padding = new Padding(0, 5, 4, 0) });
        filterBar.Controls.Add(_cmbSeverityFilter);
        filterBar.Controls.Add(btnJump);
        filterBar.Controls.Add(btnDiff);
        filterBar.Controls.Add(btnRuleHelp);

        // Configure explicit typed columns for our Error List DataGridView
        _errorGrid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(AuditFinding.Severity), HeaderText = "Severity", Width = 88 });
        _errorGrid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(AuditFinding.RuleId), HeaderText = "Rule ID", Width = 195 });
        _errorGrid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(AuditFinding.WcagCriterion), HeaderText = "WCAG 2.2 Criterion", Width = 175 });
        _errorGrid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(AuditFinding.Section508Reference), HeaderText = "Section 508 / ADA", Width = 195 });
        _errorGrid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(AuditFinding.LocationDisplay), HeaderText = "Ln:Col", Width = 72 });
        _errorGrid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(AuditFinding.HasGuidedFixDisplay), HeaderText = "Remediation", Width = 115 });
        _errorGrid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(AuditFinding.Message), HeaderText = "Engineering Finding Description", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });

        _errorGrid.DataSource = _visibleFindings;
        _errorGrid.SelectionChanged += (_, _) => OnSelectedFindingChanged();
        _errorGrid.CellDoubleClick += (_, _) => JumpToSelectedFinding();
        _errorGrid.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.Handled = true;
                JumpToSelectedFinding();
            }
        };

        tabErrors.Controls.Add(_errorGrid);
        tabErrors.Controls.Add(filterBar);
    }

    private void BuildGuidedFixTab(TabPage tabGuidedFix)
    {
        FlowLayoutPanel actionRow = new() { Dock = DockStyle.Top, Height = 38, Padding = new Padding(6, 4, 6, 4) };
        _btnPreviewDiff = new Button { Text = "🔍 Preview Side-by-Side Diff in Monaco", AutoSize = true };
        _btnApplyFix = new Button { Text = "✔ Apply Guided Fix to Editor Buffer", AutoSize = true };
        _btnExitDiff = new Button { Text = "↩ Exit Diff View", AutoSize = true, Enabled = false };
        Button btnFixRuleHelp = new() { Text = "📖 Full Rule Documentation (Shift+F1)", AutoSize = true };

        _btnPreviewDiff.Click += (_, _) => PreviewGuidedFixDiff();
        _btnApplyFix.Click += (_, _) => ApplyGuidedFixToBuffer();
        _btnExitDiff.Click += (_, _) => ExitDiffMode();
        btnFixRuleHelp.Click += (_, _) => OpenContextSensitiveHelp(forceRuleTopic: true);

        actionRow.Controls.Add(_btnPreviewDiff);
        actionRow.Controls.Add(_btnApplyFix);
        actionRow.Controls.Add(_btnExitDiff);
        actionRow.Controls.Add(btnFixRuleHelp);

        tabGuidedFix.Controls.Add(_txtFixDetails);
        tabGuidedFix.Controls.Add(actionRow);
    }

    private void BuildSessionLogTab(TabPage tabLogs)
    {
        FlowLayoutPanel bar = new() { Dock = DockStyle.Top, Height = 36, Padding = new Padding(6, 4, 6, 4) };
        Button btnJsonl = new() { Text = "Export Tamper-Evident .JSONL Log…", AutoSize = true };
        Button btnTxt = new() { Text = "Export Human-Readable .LOG Transcript…", AutoSize = true };
        btnJsonl.Click += (_, _) => PromptExportSessionLog(jsonl: true);
        btnTxt.Click += (_, _) => PromptExportSessionLog(jsonl: false);
        bar.Controls.Add(btnJsonl);
        bar.Controls.Add(btnTxt);

        _sessionLogView.Columns.Add("UTC Timestamp", 150);
        _sessionLogView.Columns.Add("Action", 135);
        _sessionLogView.Columns.Add("Target", 250);
        _sessionLogView.Columns.Add("SHA-256 Digest Transition & Summary", 680);

        tabLogs.Controls.Add(_sessionLogView);
        tabLogs.Controls.Add(bar);
    }

    private void BuildStatusStrip()
    {
        _statusBar.Items.AddRange([
            _statusTarget,
            _statusCursor,
            _statusCounts,
            _statusScore,
            _statusEngine
        ]);
    }

    // ----------------------------------------------------------------------------
    // 5. WEBVIEW2 CHROMIUM + MONACO EDITOR INITIALIZATION & GPO POLICY LOADING
    // ----------------------------------------------------------------------------

    private void LoadEnterprisePolicyDefaults()
    {
        // Guard Windows Registry calls with `OperatingSystem.IsWindows()` so cross-platform builds remain 100% clean!
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            // Enterprise Group Policy (`HKLM\Software\Policies\...`) overrides per-user (`HKCU\Software\...`)
            using RegistryKey? policyKey = Registry.LocalMachine.OpenSubKey(@"Software\Policies\RaggieSoft\AccessibilityStudio")
                                           ?? Registry.CurrentUser.OpenSubKey(@"Software\RaggieSoft\AccessibilityStudio");
            if (policyKey is null) return;

            if (policyKey.GetValue("ComplianceProfile") is string profileStr &&
                Enum.TryParse(profileStr, ignoreCase: true, out ComplianceProfile parsedProfile))
            {
                _currentProject.ComplianceProfile = parsedProfile;
                _cmbProfile.SelectedIndex = (int)parsedProfile;
            }

            if (policyKey.GetValue("BatchThrottleMs") is int throttle)
            {
                _numThrottleMs.Value = Math.Clamp(throttle, 0, 30000);
                _currentProject.BatchThrottleDelayMs = throttle;
            }
        }
        catch
        {
            // Non-fatal if corporate workstation restricts registry enumeration
        }
    }

    private async Task InitializeIdeAsync(RaggieSoftStartupSplashScope? splash = null)
    {
        bool isDark = Application.IsDarkModeEnabled;

        // Store WebView2 profile data inside `%LOCALAPPDATA%\RaggieSoft\AccessibilityStudio\WebView2Data`
        string userDataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RaggieSoft",
            "AccessibilityStudio",
            "WebView2Data"
        );

        try
        {
            splash?.UpdateStatus("Starting editor and live preview engines...");
            Directory.CreateDirectory(userDataFolder);
            CoreWebView2Environment env = await CoreWebView2Environment.CreateAsync(null, userDataFolder);

            // 1. Initialize Monaco Code & Side-by-Side Diff Editor WebView2
            await _editorWebView.EnsureCoreWebView2Async(env);
            _editorWebView.CoreWebView2.Settings.IsStatusBarEnabled = false;
            _editorWebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;

            // Map optional offline `Assets/monaco` folder if unpacked by InstallAware Studio X18
            string localMonacoDir = Path.Combine(AppContext.BaseDirectory, "Assets", "monaco");
            string? localMonacoUri = null;
            if (Directory.Exists(localMonacoDir))
            {
                _editorWebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                    "monaco.a11y.local",
                    localMonacoDir,
                    CoreWebView2HostResourceAccessKind.Allow);
                localMonacoUri = "https://monaco.a11y.local";
            }

            _editorWebView.CoreWebView2.WebMessageReceived += OnEditorWebMessageReceived;
            _editorWebView.NavigateToString(MonacoHostHtmlBuilder.BuildEditorHostHtml(isDark, localMonacoUri));

            // 2. Initialize Live Chromium DOM & Emulation Inspector WebView2
            splash?.UpdateStatus("Preparing visual accessibility inspector...");
            await _inspectorWebView.EnsureCoreWebView2Async(env);
            _inspectorWebView.CoreWebView2.Settings.IsStatusBarEnabled = false;
            _inspectorWebViewReady = true;

            // 3. Initialize Authenticated SSO Browser WebView2
            await _ssoBrowserWebView.EnsureCoreWebView2Async(env);
            _ssoWebViewReady = true;

            // Synchronize Chromium `prefers-color-scheme` across all three WebView2 surfaces
            ApplyWebViewThemePreference(_preferredColorScheme);
        }
        catch (Exception ex)
        {
            // Activate pure WinForms fallback editor if WebView2 runtime is unavailable
            _editorWebView.Visible = false;
            _nativeFallbackEditor.Visible = true;
            _statusEngine.Text = $"Editor: Native Fallback ({ex.GetType().Name})";
        }

        // Open startup target passed via CLI/Explorer or load built-in sample course workspace
        splash?.UpdateStatus("Loading initial workspace files...");
        if (!string.IsNullOrWhiteSpace(_startupTarget))
        {
            LoadStartupPath(_startupTarget);
        }
        else
        {
            LoadBuiltInSampleIntoExplorer();
        }
    }

    private void OnEditorWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            using JsonDocument doc = JsonDocument.Parse(e.WebMessageAsJson);
            JsonElement root = doc.RootElement;
            if (!root.TryGetProperty("type", out JsonElement typeProp)) return;

            string? msgType = typeProp.GetString();
            if (msgType == "ready")
            {
                _editorWebViewReady = true;
                string engineName = root.TryGetProperty("engine", out JsonElement eng) ? eng.GetString() ?? "monaco" : "monaco";
                _statusEngine.Text = engineName == "monaco" ? "Editor: Monaco (VS Code Engine)" : "Editor: Air-Gapped Syntax Engine";
                PushBufferToMonacoEditor(_activeBufferContent, _activeTargetPath);
                PushMarkersToMonacoEditor();
            }
            else if (msgType == "contentChanged")
            {
                if (root.TryGetProperty("content", out JsonElement contentProp))
                {
                    _activeBufferContent = contentProp.GetString() ?? string.Empty;
                    _isBufferDirty = true;
                }
            }
            else if (msgType == "cursorMoved")
            {
                int ln = root.TryGetProperty("line", out JsonElement l) ? l.GetInt32() : 1;
                int col = root.TryGetProperty("col", out JsonElement c) ? c.GetInt32() : 1;
                _statusCursor.Text = $"Ln {ln}, Col {col}";
            }
        }
        catch
        {
            // Ignore malformed postMessage payloads
        }
    }

    // ----------------------------------------------------------------------------
    // 6. WORKSPACE LOADING, MONACO SYNC, AUDITING, & GUIDED REFACTORING DIFFS
    // ----------------------------------------------------------------------------

    private void LoadStartupPath(string path)
    {
        if (File.Exists(path) && path.EndsWith(".a11yproj", StringComparison.OrdinalIgnoreCase))
        {
            _currentProjectFilePath = Path.GetFullPath(path);
            _currentProject = WorkspaceProjectService.LoadProjectFile(_currentProjectFilePath);
            _cmbProfile.SelectedIndex = (int)_currentProject.ComplianceProfile;
            _numThrottleMs.Value = Math.Clamp(_currentProject.BatchThrottleDelayMs, 0, 30000);
            if (Directory.Exists(_currentProject.RootDirectory))
            {
                OpenFolderWorkspace(_currentProject.RootDirectory);
                return;
            }
        }
        else if (Directory.Exists(path))
        {
            OpenFolderWorkspace(path);
            return;
        }
        else if (File.Exists(path))
        {
            string? parentDir = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(parentDir))
            {
                OpenFolderWorkspace(parentDir);
            }
            OpenDiskFileInEditor(Path.GetFullPath(path));
            return;
        }

        LoadBuiltInSampleIntoExplorer();
    }

    private void LoadBuiltInSampleIntoExplorer()
    {
        _discoveredWorkspaceFiles.Clear();
        _discoveredWorkspaceFiles.AddRange(_inMemorySamples.Keys.Select(k => $"sample://{k}"));
        _currentProject.ProjectName = "Sample Higher-Ed & Public-Sector Course Module";
        RebuildWorkspaceTreeNodes();
        OpenWorkspaceTarget("sample://SampleCourseModule.html");
    }

    private void OpenFolderWorkspace(string folderPath)
    {
        string full = Path.GetFullPath(folderPath);
        _currentProject.RootDirectory = full;
        _currentProject.ProjectName = Path.GetFileName(Path.TrimEndingDirectorySeparator(full));
        _discoveredWorkspaceFiles.Clear();
        _discoveredWorkspaceFiles.AddRange(WorkspaceProjectService.DiscoverAuditableFiles(full, _currentProject.IncludedExtensions));
        RebuildWorkspaceTreeNodes();

        _sessionLogger.Record("OPEN_WORKSPACE", full, $"Discovered {_discoveredWorkspaceFiles.Count} auditable web source files.");

        if (_discoveredWorkspaceFiles.Count > 0)
        {
            OpenDiskFileInEditor(_discoveredWorkspaceFiles[0]);
        }
    }

    private void RebuildWorkspaceTreeNodes()
    {
        string filter = _txtExplorerFilter.Text.Trim();
        _workspaceTree.BeginUpdate();
        _workspaceTree.Nodes.Clear();

        TreeNode rootNode = new($"{_currentProject.ProjectName} ({_discoveredWorkspaceFiles.Count})");
        foreach (string item in _discoveredWorkspaceFiles)
        {
            string displayName = item.StartsWith("sample://", StringComparison.OrdinalIgnoreCase)
                ? item["sample://".Length..]
                : (!string.IsNullOrEmpty(_currentProject.RootDirectory)
                    ? Path.GetRelativePath(_currentProject.RootDirectory, item)
                    : Path.GetFileName(item));

            if (!string.IsNullOrEmpty(filter) && !displayName.Contains(filter, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            TreeNode child = new(displayName)
            {
                Tag = item,
                ToolTipText = item
            };
            rootNode.Nodes.Add(child);
        }

        _workspaceTree.Nodes.Add(rootNode);
        rootNode.Expand();
        _workspaceTree.EndUpdate();
    }

    private void OpenWorkspaceTarget(string targetKey)
    {
        if (targetKey.StartsWith("sample://", StringComparison.OrdinalIgnoreCase))
        {
            string name = targetKey["sample://".Length..];
            if (_inMemorySamples.TryGetValue(name, out string? content))
            {
                _activeTargetPath = name;
                _activeBufferContent = content;
                _isBufferDirty = false;
                PushBufferToMonacoEditor(_activeBufferContent, _activeTargetPath);
                RunActiveBufferAudit();
            }
            return;
        }

        if (File.Exists(targetKey))
        {
            OpenDiskFileInEditor(targetKey);
        }
    }

    private void OpenDiskFileInEditor(string filePath)
    {
        string full = Path.GetFullPath(filePath);
        _activeTargetPath = full;
        _activeBufferContent = File.ReadAllText(full, Encoding.UTF8);
        _isBufferDirty = false;
        PushBufferToMonacoEditor(_activeBufferContent, _activeTargetPath);
        RunActiveBufferAudit();
    }

    private void PushBufferToMonacoEditor(string content, string targetPath)
    {
        _nativeFallbackEditor.Text = content;
        _isInDiffMode = false;
        _btnExitDiff.Enabled = false;

        if (!_editorWebViewReady || _editorWebView.CoreWebView2 is null) return;

        string lang = WorkspaceProjectService.DetectSourceKind(targetPath) switch
        {
            SourceKind.Css => "css",
            SourceKind.JavaScriptOrTypeScript => "typescript",
            SourceKind.WebVtt => "plaintext",
            _ => "html"
        };

        string payload = JsonSerializer.Serialize(new
        {
            cmd = "setDocument",
            language = lang,
            content
        });
        _editorWebView.CoreWebView2.PostWebMessageAsJson(payload);
    }

    private void PushMarkersToMonacoEditor()
    {
        if (!_editorWebViewReady || _editorWebView.CoreWebView2 is null || _latestFileResult is null) return;

        var markers = _latestFileResult.Findings.Select(f => new
        {
            ruleId = f.RuleId,
            message = f.Message,
            severity = (int)f.Severity,
            startLine = Math.Max(1, f.StartLine),
            startCol = Math.Max(1, f.StartColumn),
            endLine = Math.Max(1, f.EndLine),
            endCol = Math.Max(2, f.EndColumn)
        }).ToList();

        string payload = JsonSerializer.Serialize(new { cmd = "setMarkers", markers });
        _editorWebView.CoreWebView2.PostWebMessageAsJson(payload);
    }

    private void RevealEditorPosition(int line, int col)
    {
        _centerTabs.SelectedIndex = 0;
        if (_editorWebViewReady && _editorWebView.CoreWebView2 is not null)
        {
            string payload = JsonSerializer.Serialize(new { cmd = "revealPosition", line, col });
            _editorWebView.CoreWebView2.PostWebMessageAsJson(payload);
        }
        _statusCursor.Text = $"Ln {line}, Col {col}";
    }

    private void RunActiveBufferAudit()
    {
        DateTimeOffset started = DateTimeOffset.UtcNow;
        ComplianceProfile profile = GetSelectedProfile();

        FileAuditResult result = WorkspaceProjectService.AuditContent(_activeBufferContent, _activeTargetPath, profile);
        _latestFileResult = result;
        _latestWorkspaceReport = new WorkspaceAuditReport
        {
            WorkspaceName = _currentProject.ProjectName,
            RootTarget = _activeTargetPath,
            Profile = profile,
            StartedAtUtc = started,
            CompletedAtUtc = DateTimeOffset.UtcNow,
            FileResults = [result]
        };

        // Update Error List, Outline Tree, Monaco Squiggles, Live Inspector, Status Strip, and Session Log
        RefreshVisibleFindingsGrid();
        PopulateOutlineTree(result.Outline);
        PushMarkersToMonacoEditor();
        RefreshLiveInspectorSurface();

        _statusTarget.Text = $"Target: {_activeTargetPath}{(_isBufferDirty ? " *" : string.Empty)}";
        _statusCounts.Text = $"Findings: {result.Findings.Count} ({result.CriticalCount} Crit, {result.SeriousCount} Ser)";
        _statusScore.Text = $"Health Score: {result.HealthScore}%";

        _sessionLogger.Record(
            "AUDIT_BUFFER",
            _activeTargetPath,
            $"Profile={profile}; Findings={result.Findings.Count} (Crit={result.CriticalCount}, Ser={result.SeriousCount}); Score={result.HealthScore}%",
            beforeSha256: result.Sha256Hash);
    }

    private void RunFullFolderAudit()
    {
        ComplianceProfile profile = GetSelectedProfile();
        DateTimeOffset started = DateTimeOffset.UtcNow;
        List<FileAuditResult> results = [];

        if (!string.IsNullOrEmpty(_currentProject.RootDirectory) && Directory.Exists(_currentProject.RootDirectory))
        {
            WorkspaceAuditReport dirReport = WorkspaceProjectService.AuditDirectory(
                _currentProject.RootDirectory,
                profile,
                _currentProject.ProjectName);
            _latestWorkspaceReport = dirReport;
            results.AddRange(dirReport.FileResults);
        }
        else
        {
            foreach (var kvp in _inMemorySamples)
            {
                results.Add(WorkspaceProjectService.AuditContent(kvp.Value, kvp.Key, profile));
            }
            _latestWorkspaceReport = new WorkspaceAuditReport
            {
                WorkspaceName = _currentProject.ProjectName,
                RootTarget = "Built-In Sample Workspace",
                Profile = profile,
                StartedAtUtc = started,
                CompletedAtUtc = DateTimeOffset.UtcNow,
                FileResults = results
            };
        }

        // Flatten workspace findings into Error List so engineers can double-click across files!
        _visibleFindings.Clear();
        foreach (AuditFinding f in _latestWorkspaceReport.AllFindings)
        {
            _visibleFindings.Add(f);
        }

        _statusCounts.Text = $"Workspace Findings: {_latestWorkspaceReport.AllFindings.Count} across {results.Count} files";
        _statusScore.Text = $"Workspace Avg Score: {_latestWorkspaceReport.AverageHealthScore}%";

        _sessionLogger.Record(
            "AUDIT_WORKSPACE",
            _latestWorkspaceReport.RootTarget,
            $"Audited {results.Count} files; Total Findings={_latestWorkspaceReport.AllFindings.Count}; Avg Score={_latestWorkspaceReport.AverageHealthScore}%.");
    }

    private void RefreshVisibleFindingsGrid()
    {
        _visibleFindings.Clear();
        IReadOnlyList<AuditFinding> source = _latestFileResult?.Findings ?? [];
        int filterIdx = _cmbSeverityFilter.SelectedIndex;

        foreach (AuditFinding f in source)
        {
            bool keep = filterIdx switch
            {
                1 => f.Severity == AuditSeverity.Critical,
                2 => f.Severity is AuditSeverity.Critical or AuditSeverity.Serious,
                3 => !string.IsNullOrEmpty(f.GuidedFixId),
                _ => true
            };
            if (keep)
            {
                _visibleFindings.Add(f);
            }
        }
    }

    private void PopulateOutlineTree(IReadOnlyList<DocumentOutlineNode> nodes)
    {
        _outlineTree.BeginUpdate();
        _outlineTree.Nodes.Clear();

        TreeNode landmarksGroup = new("Semantic HTML5 / ARIA Landmarks");
        TreeNode headingsGroup = new("Heading Hierarchy (H1–H6)");

        foreach (DocumentOutlineNode n in nodes)
        {
            string warnPrefix = n.HasWarning ? "⚠ " : string.Empty;
            string warnSuffix = !string.IsNullOrEmpty(n.WarningNote) ? $" — [{n.WarningNote}]" : string.Empty;
            TreeNode item = new($"{warnPrefix}[Ln {n.Line}] <{n.TagOrRole}> {n.Label}{warnSuffix}")
            {
                Tag = n.Line
            };

            if (n.Kind == "Landmark")
            {
                landmarksGroup.Nodes.Add(item);
            }
            else
            {
                headingsGroup.Nodes.Add(item);
            }
        }

        _outlineTree.Nodes.Add(landmarksGroup);
        _outlineTree.Nodes.Add(headingsGroup);
        landmarksGroup.Expand();
        headingsGroup.Expand();
        _outlineTree.EndUpdate();
    }

    private AuditFinding? GetSelectedFinding() =>
        _errorGrid.CurrentRow?.DataBoundItem as AuditFinding;

    private void OnSelectedFindingChanged()
    {
        AuditFinding? f = GetSelectedFinding();
        if (f is null)
        {
            _txtFixDetails.Text = "Select a finding in the WCAG 2.2 & Section 508 Error List to inspect remediation guidance and preview a Guided Fix.";
            return;
        }

        StringBuilder sb = new();
        sb.AppendLine($"RULE: {f.RuleId} — {f.Title}");
        sb.AppendLine($"SEVERITY: {f.Severity}   |   WCAG: {f.WcagCriterion} (Level {f.WcagLevel})   |   508: {f.Section508Reference}");
        sb.AppendLine($"LOCATION: {f.TargetPath} (Line {f.StartLine}, Column {f.StartColumn})");
        sb.AppendLine(new string('-', 88));
        sb.AppendLine("IMPACT ON ASSISTIVE TECHNOLOGY USERS:");
        sb.AppendLine("  " + f.Message);
        sb.AppendLine();
        sb.AppendLine("CURRENT OFFENDING MARKUP / CSS:");
        sb.AppendLine("  " + f.OffendingSnippet);
        sb.AppendLine();
        sb.AppendLine("ENGINEERING REMEDIATION GUIDANCE:");
        sb.AppendLine("  " + f.RemediationGuidance);

        if (!string.IsNullOrEmpty(f.SuggestedReplacementPreview))
        {
            sb.AppendLine();
            sb.AppendLine("SUGGESTED GUIDED FIX PREVIEW (Press Ctrl+D for Side-by-Side Monaco Diff):");
            sb.AppendLine("  " + f.SuggestedReplacementPreview);
        }

        _txtFixDetails.Text = sb.ToString();

        // Also highlight the offending DOM element inside the Live Chromium Inspector!
        if (_inspectorWebViewReady && _inspectorWebView.CoreWebView2 is not null && !string.IsNullOrEmpty(f.DomSelectorHint))
        {
            _ = _inspectorWebView.CoreWebView2.ExecuteScriptAsync(
                LiveDomInspectorScriptBuilder.BuildHighlightSelectorScript(f.DomSelectorHint));
        }
    }

    private void JumpToSelectedFinding()
    {
        AuditFinding? f = GetSelectedFinding();
        if (f is null) return;

        // If the selected finding belongs to a different file from a full-folder scan, open that file first!
        if (!string.Equals(f.TargetPath, _activeTargetPath, StringComparison.OrdinalIgnoreCase))
        {
            if (_inMemorySamples.ContainsKey(f.TargetPath))
            {
                OpenWorkspaceTarget($"sample://{f.TargetPath}");
            }
            else if (File.Exists(f.TargetPath))
            {
                OpenDiskFileInEditor(f.TargetPath);
            }
        }

        RevealEditorPosition(f.StartLine, f.StartColumn);
    }

    private void PreviewGuidedFixDiff()
    {
        AuditFinding? f = GetSelectedFinding();
        if (f is null)
        {
            MessageBox.Show(this, "Select a finding in the Error List first.", "Preview Guided Fix", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        RefactorPreviewResult preview = GuidedSourceRefactorer.BuildPreview(_activeBufferContent, f);
        _pendingRefactorPreview = preview;

        if (!preview.Succeeded)
        {
            MessageBox.Show(this, preview.SummaryMessage, "Manual Review Required", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        _centerTabs.SelectedIndex = 0;
        _isInDiffMode = true;
        _btnExitDiff.Enabled = true;

        if (_editorWebViewReady && _editorWebView.CoreWebView2 is not null)
        {
            string lang = WorkspaceProjectService.DetectSourceKind(_activeTargetPath) == SourceKind.Css ? "css" : "html";
            string payload = JsonSerializer.Serialize(new
            {
                cmd = "showDiff",
                original = preview.OriginalSource,
                modified = preview.RefactoredSource,
                language = lang
            });
            _editorWebView.CoreWebView2.PostWebMessageAsJson(payload);
        }

        _statusTarget.Text = $"DIFF PREVIEW: {preview.SummaryMessage} (Click '✔ Apply Fix' to commit to buffer)";
    }

    private void ApplyGuidedFixToBuffer()
    {
        AuditFinding? f = GetSelectedFinding();
        if (f is null) return;

        RefactorPreviewResult preview = _pendingRefactorPreview is { Succeeded: true } cached
            ? cached
            : GuidedSourceRefactorer.BuildPreview(_activeBufferContent, f);

        if (!preview.Succeeded)
        {
            MessageBox.Show(this, preview.SummaryMessage, "Guided Fix Unavailable", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        string beforeSha = WorkspaceProjectService.ComputeSha256Hex(_activeBufferContent);
        _activeBufferContent = preview.RefactoredSource;
        _isBufferDirty = true;
        _pendingRefactorPreview = null;

        // Update built-in sample dictionary if editing a sample file
        if (_inMemorySamples.ContainsKey(_activeTargetPath))
        {
            _inMemorySamples[_activeTargetPath] = _activeBufferContent;
        }

        string afterSha = WorkspaceProjectService.ComputeSha256Hex(_activeBufferContent);
        _sessionLogger.Record(
            "APPLY_GUIDED_FIX",
            _activeTargetPath,
            $"[{f.RuleId}] {preview.SummaryMessage}",
            beforeSha256: beforeSha,
            afterSha256: afterSha);

        PushBufferToMonacoEditor(_activeBufferContent, _activeTargetPath);
        RunActiveBufferAudit();
    }

    private void ExitDiffMode()
    {
        if (!_isInDiffMode) return;
        _isInDiffMode = false;
        _btnExitDiff.Enabled = false;
        _pendingRefactorPreview = null;

        if (_editorWebViewReady && _editorWebView.CoreWebView2 is not null)
        {
            _editorWebView.CoreWebView2.PostWebMessageAsJson("{\"cmd\":\"exitDiff\"}");
        }
    }

    // ----------------------------------------------------------------------------
    // 7. LIVE CHROMIUM DOM INSPECTOR & EMULATION MODES
    // ----------------------------------------------------------------------------

    private void RefreshLiveInspectorSurface()
    {
        if (!_inspectorWebViewReady || _inspectorWebView.CoreWebView2 is null) return;

        SourceKind kind = WorkspaceProjectService.DetectSourceKind(_activeTargetPath);
        string htmlToRender = kind switch
        {
            SourceKind.Html => _activeBufferContent,
            SourceKind.Css => $"<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><title>CSS Stylesheet Preview</title><style>{_activeBufferContent}</style></head><body style=\"font-family:system-ui;padding:24px;\"><h1>CSS Stylesheet Live Preview</h1><p class=\"course-subtitle\">Sample subtitle styled by your active CSS stylesheet.</p><p><a href=\"#demo\">Interactive Focusable Link</a> &nbsp; <button type=\"button\">Sample Button</button></p></body></html>",
            _ => $"<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><title>Caption Track Preview</title></head><body style=\"font-family:monospace;padding:24px;white-space:pre-wrap;\">{System.Net.WebUtility.HtmlEncode(_activeBufferContent)}</body></html>"
        };

        _inspectorWebView.NavigateToString(htmlToRender);
        _inspectorWebView.CoreWebView2.NavigationCompleted += OnInspectorNavigationCompletedOnce;
    }

    private async void OnInspectorNavigationCompletedOnce(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (_inspectorWebView.CoreWebView2 is not null)
        {
            _inspectorWebView.CoreWebView2.NavigationCompleted -= OnInspectorNavigationCompletedOnce;
        }
        await ApplyInspectorEmulationStatesAsync();
    }

    private async Task ApplyInspectorEmulationStatesAsync()
    {
        if (!_inspectorWebViewReady || _inspectorWebView.CoreWebView2 is null) return;

        // 400% WCAG 1.4.10 Reflow Zoom
        _inspectorWebView.ZoomFactor = _emulateReflow400 ? 4.0 : 1.0;

        // Forced-Colors High Contrast simulation
        await _inspectorWebView.CoreWebView2.ExecuteScriptAsync(
            LiveDomInspectorScriptBuilder.BuildForcedColorsSimulationScript(_emulateForcedColors));

        // Keyboard Tab-Order Badges `[1..N]`
        await _inspectorWebView.CoreWebView2.ExecuteScriptAsync(
            LiveDomInspectorScriptBuilder.BuildTabOrderOverlayScript(_showTabOrderBadges));
    }

    // ----------------------------------------------------------------------------
    // 8. AUTHENTICATED SSO BROWSER & POLITE RATE-LIMITED BATCH QUEUE
    // ----------------------------------------------------------------------------

    private async Task AuditCurrentSsoPageDomAsync()
    {
        if (!_ssoWebViewReady || _ssoBrowserWebView.CoreWebView2 is null) return;

        string currentUrl = _ssoBrowserWebView.Source?.ToString() ?? _txtSsoUrl.Text.Trim();
        string rawJsonHtml = await _ssoBrowserWebView.CoreWebView2.ExecuteScriptAsync("document.documentElement.outerHTML");
        string renderedDom = JsonSerializer.Deserialize<string>(rawJsonHtml) ?? string.Empty;

        _activeTargetPath = currentUrl;
        _activeBufferContent = "<!doctype html>\n" + renderedDom;
        PushBufferToMonacoEditor(_activeBufferContent, "captured-sso-dom.html");
        _activeTargetPath = currentUrl;
        RunActiveBufferAudit();
        _centerTabs.SelectedIndex = 0;
    }

    private void PromptLoadBatchSchedule()
    {
        using OpenFileDialog dlg = new()
        {
            Title = "Load Batch Audit Schedule (.csv) or Sitemap (.xml)",
            Filter = "Schedules & Sitemaps (*.csv;*.xml)|*.csv;*.xml|All Files (*.*)|*.*"
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        IReadOnlyList<string> targets = BatchAuditQueueRunner.LoadTargetsFromScheduleFile(dlg.FileName);
        _lstBatchTargets.Items.Clear();
        foreach (string t in targets)
        {
            _lstBatchTargets.Items.Add(t);
        }
        _lblBatchStatus.Text = $"Loaded {targets.Count} targets from {Path.GetFileName(dlg.FileName)}.";
        _centerTabs.SelectedIndex = 2;
    }

    private async Task StartBatchQueueExecutionAsync()
    {
        List<string> targets = _lstBatchTargets.Items.Cast<string>().ToList();
        if (targets.Count == 0 && !string.IsNullOrWhiteSpace(_txtSsoUrl.Text))
        {
            targets.Add(_txtSsoUrl.Text.Trim());
            _lstBatchTargets.Items.Add(_txtSsoUrl.Text.Trim());
        }
        if (targets.Count == 0) return;

        _batchCts = new CancellationTokenSource();
        _btnStartBatch.Enabled = false;
        _btnCancelBatch.Enabled = true;
        _batchProgressBar.Minimum = 0;
        _batchProgressBar.Maximum = targets.Count;
        _batchProgressBar.Value = 0;

        int throttleMs = (int)_numThrottleMs.Value;
        bool useAuthBrowser = _chkUseAuthenticatedWebView.Checked && _ssoWebViewReady;

        Progress<BatchQueueProgress> progress = new(p =>
        {
            _batchProgressBar.Value = Math.Clamp(p.CurrentIndex, 0, _batchProgressBar.Maximum);
            _lblBatchStatus.Text = $"[{p.CurrentIndex}/{p.TotalCount}] {p.CurrentTarget}";
        });

        try
        {
            WorkspaceAuditReport report = await BatchAuditQueueRunner.RunQueueAsync(
                targets,
                queueName: "Batch Accessibility Audit",
                profile: GetSelectedProfile(),
                throttleDelayMs: throttleMs,
                customUrlFetcherAsync: useAuthBrowser ? FetchUrlViaSsoBrowserAsync : null,
                progress: progress,
                cancellationToken: _batchCts.Token);

            _latestWorkspaceReport = report;
            _visibleFindings.Clear();
            foreach (AuditFinding f in report.AllFindings)
            {
                _visibleFindings.Add(f);
            }

            _lblBatchStatus.Text = $"Completed {report.FileResults.Count} targets — {report.AllFindings.Count} findings (Avg {report.AverageHealthScore}%).";
            _sessionLogger.Record("BATCH_QUEUE_COMPLETE", report.RootTarget, _lblBatchStatus.Text);
            _bottomTabs.SelectedIndex = 0;
        }
        catch (OperationCanceledException)
        {
            _lblBatchStatus.Text = "Batch queue canceled by user.";
        }
        catch (Exception ex)
        {
            _lblBatchStatus.Text = $"Batch error: {ex.Message}";
        }
        finally
        {
            _btnStartBatch.Enabled = true;
            _btnCancelBatch.Enabled = false;
            _batchCts?.Dispose();
            _batchCts = null;
        }
    }

    private async Task<string> FetchUrlViaSsoBrowserAsync(string url, CancellationToken ct)
    {
        TaskCompletionSource<bool> tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        void Handler(object? s, CoreWebView2NavigationCompletedEventArgs e) => tcs.TrySetResult(e.IsSuccess);

        _ssoBrowserWebView.CoreWebView2.NavigationCompleted += Handler;
        try
        {
            _ssoBrowserWebView.CoreWebView2.Navigate(url);
            using CancellationTokenRegistration reg = ct.Register(() => tcs.TrySetCanceled(ct));
            await tcs.Task;
            string rawJson = await _ssoBrowserWebView.CoreWebView2.ExecuteScriptAsync("document.documentElement.outerHTML");
            return "<!doctype html>\n" + (JsonSerializer.Deserialize<string>(rawJson) ?? string.Empty);
        }
        finally
        {
            _ssoBrowserWebView.CoreWebView2.NavigationCompleted -= Handler;
        }
    }

    // ----------------------------------------------------------------------------
    // 9. FILE SAVING, REPORT EXPORTS (HTML5, SARIF, CSV, JSON), & HELPERS
    // ----------------------------------------------------------------------------

    private void PromptOpenSingleFile()
    {
        using OpenFileDialog dlg = new()
        {
            Title = "Open Web Source File",
            Filter = "Web & Caption Source Files (*.html;*.htm;*.css;*.vtt;*.astro;*.svelte)|*.html;*.htm;*.css;*.vtt;*.astro;*.svelte|All Files (*.*)|*.*"
        };
        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            OpenDiskFileInEditor(dlg.FileName);
        }
    }

    private void PromptOpenFolderWorkspace()
    {
        using FolderBrowserDialog dlg = new()
        {
            Description = "Select Web Application or Course Module Source Folder",
            UseDescriptionForTitle = true
        };
        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            OpenFolderWorkspace(dlg.SelectedPath);
        }
    }

    private void PromptOpenProjectFile()
    {
        using OpenFileDialog dlg = new()
        {
            Title = "Open RaggieSoft Accessibility Studio Project (.a11yproj)",
            Filter = "Accessibility Studio Project (*.a11yproj)|*.a11yproj"
        };
        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            LoadStartupPath(dlg.FileName);
        }
    }

    private void PromptSaveProjectFile()
    {
        using SaveFileDialog dlg = new()
        {
            Title = "Save RaggieSoft Accessibility Studio Project (.a11yproj)",
            Filter = "Accessibility Studio Project (*.a11yproj)|*.a11yproj",
            FileName = string.IsNullOrEmpty(_currentProjectFilePath) ? "workspace.a11yproj" : Path.GetFileName(_currentProjectFilePath)
        };
        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            _currentProject.ComplianceProfile = GetSelectedProfile();
            _currentProject.BatchThrottleDelayMs = (int)_numThrottleMs.Value;
            WorkspaceProjectService.SaveProjectFile(dlg.FileName, _currentProject);
            _currentProjectFilePath = dlg.FileName;
            _sessionLogger.Record("SAVE_PROJECT", dlg.FileName, "Saved .a11yproj workspace configuration.");
        }
    }

    private void SaveActiveSourceFile()
    {
        if (File.Exists(_activeTargetPath))
        {
            string beforeSha = WorkspaceProjectService.ComputeSha256Hex(File.ReadAllText(_activeTargetPath, Encoding.UTF8));
            File.WriteAllText(_activeTargetPath, _activeBufferContent, Encoding.UTF8);
            string afterSha = WorkspaceProjectService.ComputeSha256Hex(_activeBufferContent);
            _isBufferDirty = false;
            _statusTarget.Text = $"Saved: {_activeTargetPath}";
            _sessionLogger.Record("SAVE_FILE", _activeTargetPath, "Wrote remediated source buffer to disk.", beforeSha, afterSha);
            return;
        }

        using SaveFileDialog dlg = new()
        {
            Title = "Save Remediated Source File As",
            FileName = Path.GetFileName(_activeTargetPath),
            Filter = "All Files (*.*)|*.*"
        };
        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            File.WriteAllText(dlg.FileName, _activeBufferContent, Encoding.UTF8);
            _activeTargetPath = dlg.FileName;
            _isBufferDirty = false;
            _statusTarget.Text = $"Saved: {_activeTargetPath}";
            _sessionLogger.Record("SAVE_FILE_AS", dlg.FileName, "Saved remediated buffer to new file.");
        }
    }

    private void PromptExportReport(string kind)
    {
        if (_latestWorkspaceReport is null)
        {
            RunActiveBufferAudit();
        }
        if (_latestWorkspaceReport is null) return;

        (string filter, string defaultExt) = kind switch
        {
            "sarif" => ("OASIS SARIF 2.1.0 (*.sarif)|*.sarif", "audit-report.sarif"),
            "csv" => ("CSV Spreadsheet (*.csv)|*.csv", "audit-findings.csv"),
            "json" => ("JSON Audit Snapshot (*.json)|*.json", "audit-report.json"),
            _ => ("Accessible HTML5 Audit Report (*.html)|*.html", "accessibility-audit-report.html")
        };

        using SaveFileDialog dlg = new()
        {
            Title = "Export Enterprise Accessibility Report",
            Filter = filter,
            FileName = defaultExt
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        switch (kind)
        {
            case "sarif":
                EnterpriseReportExporter.SaveSarifReport(dlg.FileName, _latestWorkspaceReport);
                break;
            case "csv":
                EnterpriseReportExporter.SaveCsvReport(dlg.FileName, _latestWorkspaceReport);
                break;
            case "json":
                EnterpriseReportExporter.SaveJsonReport(dlg.FileName, _latestWorkspaceReport);
                break;
            default:
                EnterpriseReportExporter.SaveHtmlReport(dlg.FileName, _latestWorkspaceReport);
                break;
        }

        _sessionLogger.Record("EXPORT_REPORT", dlg.FileName, $"Exported {kind.ToUpperInvariant()} report ({_latestWorkspaceReport.AllFindings.Count} findings).");
    }

    private void PromptExportSessionLog(bool jsonl)
    {
        using SaveFileDialog dlg = new()
        {
            Title = jsonl ? "Export Tamper-Evident Session Log (.jsonl)" : "Export Human-Readable Session Log (.log)",
            Filter = jsonl ? "JSON Lines (*.jsonl)|*.jsonl" : "Log Transcript (*.log)|*.log",
            FileName = jsonl ? "remediation-session.jsonl" : "remediation-session.log"
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        if (jsonl)
        {
            _sessionLogger.ExportJsonLines(dlg.FileName);
        }
        else
        {
            File.WriteAllText(dlg.FileName, _sessionLogger.FormatHumanReadableTranscript(), Encoding.UTF8);
        }
    }

    private void CopySelectedFindingCitation()
    {
        AuditFinding? f = GetSelectedFinding();
        if (f is null) return;
        Clipboard.SetText($"[{f.RuleId}] {f.WcagCriterion} ({f.Section508Reference}) at {f.TargetPath}:{f.StartLine}:{f.StartColumn} — {f.Message}");
    }

    private void AppendLogEntryToListView(AuditLogEntry entry)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => AppendLogEntryToListView(entry));
            return;
        }

        string shaInfo = !string.IsNullOrEmpty(entry.AfterSha256)
            ? $"[{entry.BeforeSha256?[..8]}… -> {entry.AfterSha256[..8]}…] {entry.Summary}"
            : entry.Summary;

        ListViewItem item = new([
            entry.TimestampUtc.ToString("yyyy-MM-dd HH:mm:ss"),
            entry.ActionType,
            entry.TargetPath,
            shaInfo
        ]);
        _sessionLogView.Items.Insert(0, item);
    }

    private ComplianceProfile GetSelectedProfile() =>
        _cmbProfile.SelectedIndex switch
        {
            1 => ComplianceProfile.Wcag22LevelA,
            2 => ComplianceProfile.AdaTitleIIWeb,
            3 => ComplianceProfile.StrictEnterpriseAll,
            _ => ComplianceProfile.Wcag22AA_Section508
        };

    private void OnSystemUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category is UserPreferenceCategory.General or UserPreferenceCategory.Color or UserPreferenceCategory.VisualStyle)
        {
            if (IsHandleCreated)
            {
                BeginInvoke(() => ApplyWebViewThemePreference(_preferredColorScheme));
            }
        }
    }

    private void ApplyWebViewThemePreference(CoreWebView2PreferredColorScheme scheme)
    {
        _preferredColorScheme = scheme;
        bool useDark = scheme switch
        {
            CoreWebView2PreferredColorScheme.Dark => true,
            CoreWebView2PreferredColorScheme.Light => false,
            _ => Application.IsDarkModeEnabled
        };

        try
        {
            if (_editorWebView.CoreWebView2 is not null)
            {
                _editorWebView.CoreWebView2.Profile.PreferredColorScheme = scheme;
                string payload = JsonSerializer.Serialize(new { cmd = "setTheme", dark = useDark });
                _editorWebView.CoreWebView2.PostWebMessageAsJson(payload);
            }
            if (_inspectorWebView.CoreWebView2 is not null)
            {
                _inspectorWebView.CoreWebView2.Profile.PreferredColorScheme = scheme;
            }
            if (_ssoBrowserWebView.CoreWebView2 is not null)
            {
                _ssoBrowserWebView.CoreWebView2.Profile.PreferredColorScheme = scheme;
            }
            if (_helpViewerWindow is { IsDisposed: false })
            {
                _helpViewerWindow.ApplyColorSchemePreference(scheme);
            }
        }
        catch
        {
            // Ignore if WebView2 profile is still initializing
        }
    }

    private void OpenContextSensitiveHelp(bool forceRuleTopic)
    {
        AuditFinding? selected = GetSelectedFinding();
        bool errorPaneActive = forceRuleTopic || _errorGrid.Focused || _txtFixDetails.Focused;
        (string topicId, string? anchorId) = StudioHelpCatalog.ResolveContextTopic(
            selected?.RuleId,
            preferSelectedRule: errorPaneActive,
            activeCenterTabIndex: _centerTabs.SelectedIndex,
            activeBottomTabIndex: _bottomTabs.SelectedIndex);

        OpenStudioHelp(topicId, anchorId);
    }

    private void OpenStudioHelp(string topicId, string? anchorId = null)
    {
        // 1. Prefer the standalone pluggable RaggieSoft Help Viewer (`[INSTALLDIR]\HelpViewer\RaggieSoftHelp.exe`
        //    or Enterprise Shared `C:\Program Files\Common Files\RaggieSoft\HelpViewer\RaggieSoftHelp.exe`),
        //    which uses Named Pipe Single-Instance IPC to reuse any already-open Help Viewer window!
        if (TryLaunchStandaloneHelpViewer(topicId, anchorId, _preferredColorScheme))
        {
            return;
        }

        // 2. Fallback to the built-in in-process `HelpViewerForm` if the standalone `HelpViewer`
        //    MSI feature was unchecked during a Custom installation!
        if (_helpViewerWindow is null || _helpViewerWindow.IsDisposed)
        {
            _helpViewerWindow = new HelpViewerForm(_preferredColorScheme, topicId, anchorId);
            _helpViewerWindow.Show(this);
        }
        else
        {
            if (_helpViewerWindow.WindowState == FormWindowState.Minimized)
            {
                _helpViewerWindow.WindowState = FormWindowState.Normal;
            }
            _helpViewerWindow.ApplyColorSchemePreference(_preferredColorScheme);
            _helpViewerWindow.NavigateToTopic(topicId, anchorId);
            _helpViewerWindow.Activate();
        }
    }

    internal static bool TryLaunchStandaloneHelpViewer(
        string topicId,
        string? anchorId,
        CoreWebView2PreferredColorScheme scheme)
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        // Check `[INSTALLDIR]\HelpViewer\RaggieSoftHelp.exe` (relative to `[INSTALLDIR]\StudioIDE\`)
        // followed by Enterprise Shared Merge Module path `C:\Program Files\Common Files\RaggieSoft\HelpViewer\RaggieSoftHelp.exe`
        string siblingViewer = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "HelpViewer", "RaggieSoftHelp.exe"));
        string commonViewer = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFiles),
            "RaggieSoft",
            "HelpViewer",
            "RaggieSoftHelp.exe");

        string? exePath = File.Exists(siblingViewer)
            ? siblingViewer
            : (File.Exists(commonViewer) ? commonViewer : null);

        if (exePath is null)
        {
            return false;
        }

        string themeArg = scheme switch
        {
            CoreWebView2PreferredColorScheme.Dark => "dark",
            CoreWebView2PreferredColorScheme.Light => "light",
            _ => "auto"
        };

        try
        {
            ProcessStartInfo psi = new()
            {
                FileName = exePath,
                UseShellExecute = false
            };
            psi.ArgumentList.Add("--module");
            psi.ArgumentList.Add("raggiesoft.accessibility-studio");
            psi.ArgumentList.Add("--topic");
            psi.ArgumentList.Add(topicId);
            if (!string.IsNullOrWhiteSpace(anchorId))
            {
                psi.ArgumentList.Add("--anchor");
                psi.ArgumentList.Add(anchorId);
            }
            psi.ArgumentList.Add("--theme");
            psi.ArgumentList.Add(themeArg);

            Process.Start(psi);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void LaunchOnlineDocumentation()
    {
        AuditFinding? selected = GetSelectedFinding();
        string url = selected is not null
            ? $"{StudioHelpCatalog.OnlineDocsBaseUrl}#rule-{selected.RuleId}"
            : StudioHelpCatalog.OnlineDocsBaseUrl;

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
            // Ignore if corporate shell URL launching is restricted
        }
    }

    private static ToolStripMenuItem CreateMenuItem(string text, Keys shortcut, EventHandler onClick)
    {
        ToolStripMenuItem item = new(text, null, onClick);
        if (shortcut == Keys.Escape)
        {
            // WinForms `ToolStripManager.IsValidShortcut` rejects unmodified `Keys.Escape` (27)
            // on `ShortcutKeys` (throwing `InvalidEnumArgumentException`). Display "Esc" on the
            // menu item and handle the keystroke cleanly inside `MainIdeForm.ProcessCmdKey`!
            item.ShortcutKeyDisplayString = "Esc";
        }
        else if (shortcut != Keys.None && ToolStripManager.IsValidShortcut(shortcut))
        {
            item.ShortcutKeys = shortcut;
        }
        return item;
    }

    private static ToolStripButton CreateToolbarButton(string text, string tooltip, EventHandler onClick)
    {
        ToolStripButton btn = new(text)
        {
            DisplayStyle = ToolStripItemDisplayStyle.Text,
            ToolTipText = tooltip
        };
        btn.Click += onClick;
        return btn;
    }

    private void SeedBuiltInSampleWorkspace()
    {
        _inMemorySamples["SampleCourseModule.html"] =
            """
            <!doctype html>
            <html>
            <head>
              <meta charset="utf-8">
              <style>
                .course-banner { background-color: #ffffff; color: #8c95a4; padding: 12px; }
                .nav-link:focus { outline: none; }
              </style>
            </head>
            <body>
              <nav>
                <a class="nav-link" href="#syllabus" tabindex="3">Click here</a>
              </nav>
              <nav>
                <a class="nav-link" href="#modules">Modules</a>
              </nav>
              <main id="syllabus">
                <h1>CS 410 — Enterprise Systems Architecture</h1>
                <h4>Weekly Schedule &amp; Grading</h4>
                <p class="course-banner">All lecture recordings include synchronized captions.</p>
                <img src="/assets/architecture-diagram.png">
                <table>
                  <tr>
                    <td style="font-weight: bold;">Week</td>
                    <td style="font-weight: bold;">Topic</td>
                  </tr>
                  <tr>
                    <td>Week 1</td>
                    <td>Distributed Fault Tolerance</td>
                  </tr>
                </table>
                <form action="/enroll" method="post">
                  <input type="email" name="studentEmail" placeholder="Enter campus email">
                  <button type="submit" aria-hidden="true">Register</button>
                </form>
                <video controls src="/media/week1-lecture.mp4"></video>
              </main>
            </body>
            </html>
            """;

        _inMemorySamples["SampleCourseStyles.css"] =
            """
            /* Sample Enterprise Portal Stylesheet */
            .hero-callout {
              background-color: #f4f4f4;
              color: #8d8d8d;
            }

            .action-btn:focus {
              outline: 0;
            }

            @keyframes pulseBadge {
              from { opacity: 0.6; }
              to { opacity: 1; }
            }
            """;

        _inMemorySamples["SampleLectureCaptions.vtt"] =
            """
            1
            00:00:01.000 --> 00:00:05.500
            Welcome to Week 1 of Enterprise Systems Architecture.

            2
            00:00:04.800 --> 00:00:08.000
            Today we examine deterministic static accessibility analysis.
            """;
    }
}
