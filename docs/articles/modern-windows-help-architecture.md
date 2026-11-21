# Modern Windows Help Architecture (From `.chm` to DocFX & Offline HTML5)

## Why Windows 2000 / XP HTML Help Workshop (`.chm`) Was Retired

In the Windows 2000 and Windows XP era, desktop engineering tools compiled Help files using **Microsoft HTML Help Workshop (`hhc.exe`)**—packaging `.hhp` projects, `.hhc` tables of contents, and `.hhk` keyword indexes into an ITSS-compressed **`.chm`** binary displayed by `hh.exe` (`Help.ShowHelp()` in classic WinForms).

For a modern **Windows 11 WCAG 2.2 & Section 508 Accessibility IDE**, `.chm` is no longer viable:

| Dimension | Legacy HTML Help (`.chm` / `hh.exe`) | Modern Accessibility Studio Help (`DocFX` + `HelpViewerForm`) |
| :--- | :--- | :--- |
| **Rendering Engine** | Frozen Internet Explorer Trident (`mshtml.dll`) | Evergreen Chromium (`WebView2`) + Modern Browsers |
| **Dark Mode (`prefers-color-scheme`)** | Unsupported (forces glaring white canvas) | Automatic sync with Windows 11 System / Dark / Light mode |
| **Windows Contrast Themes (`forced-colors`)** | Legacy IE quirks only | Full W3C `@media (forced-colors: active)` system color support |
| **WCAG 1.4.10 `400%` Reflow** | Fixed legacy framesets clip horizontally | Responsive CSS Grid + single-column `400%` zoom reflow |
| **Network Share & Security Policies** | Blank page when `Zone.Identifier` (Mark-of-the-Web) is set; often blocked by CIS/STIG | Served locally via `SetVirtualHostNameToFolderMapping("help.a11y.local", ...)` with zero ActiveX/ITSS surface |
| **CI/CD Build Automation** | Retired 32-bit `hhc.exe` installer | Cross-platform `a11y-studio-cli export-help` & `docfx docs/docfx.json` |

## How Context-Sensitive `F1` Routing Works in `MainIdeForm`

1. **Selected Rule Lookup (`Shift+F1` or `F1` inside Error List / Guided Fix)**:
   - Reads the highlighted `AuditFinding.RuleId` (for example, `WCAG-TABLE-FAKE-HEADER`) and navigates `HelpViewerForm` directly to `rule-reference.html#rule-WCAG-TABLE-FAKE-HEADER`.
2. **Active Workspace Pane Lookup (`F1`)**:
   - Maps the active center or bottom tab (`Code & Diff Editor`, `Live Chromium DOM Inspector`, `SSO & Batch Queue`, `Heading & Landmark Outline`, `Audit & Remediation Session Log`, or `Color Contrast Workbench`) to its dedicated chapter.
3. **Self-Audited Accessibility**:
   - `AccessibilityStudio.Tests` runs `WorkspaceProjectService.AuditContent` against every generated HTML5 Help topic on every build to guarantee **0 WCAG 2.2 AA violations**.
