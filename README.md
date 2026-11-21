# RaggieSoft Accessibility Studio

**Enterprise Source-Level WCAG 2.2 AA & Revised Section 508 Web Engineering IDE & Headless CI/CD Scanner**

- **Product Portal:** [https://a11y.raggiesoft.com](https://a11y.raggiesoft.com)
- **Solution File:** `RaggieSoftAccessibilityStudio.slnx` (Visual Studio 2026 / .NET 10 SDK)
- **Primary Stack:** C# 13 • .NET 10 Windows Forms (`SystemColorMode` Dark/Light + Per-Monitor V2 High-DPI) • Monaco Syntax & Side-by-Side Diff Editor (`WebView2`) • Live Chromium DOM & Emulation Inspector • Cross-Platform CLI (`a11y-studio-cli`)
- **Code License:** [MIT License](LICENSE) *(Creative & sample narrative fixtures, if any, under CC BY-SA 4.0)*

---

## Architectural Overview

**RaggieSoft Accessibility Studio** is purpose-built for **source-level web accessibility engineering** (`HTML5`, `WAI-ARIA`, `CSS`, `BCP 47` multilingual markup, and synchronized `WebVTT` captions) across enterprise web applications, university distance-learning portals, and municipal digital services.

| Project | Target | Responsibility |
| :--- | :--- | :--- |
| **`AccessibilityStudio.Core`** | `net10.0` | Line/column-accurate HTML5 semantic analyzer, CSS color-contrast & `forced-colors` analyzer, WebVTT `.vtt` caption timing/CPS linter, Guided Source Refactoring engine, SSO-friendly rate-limited batch queue, and SHA-256 tamper-evident session logger. |
| **`AccessibilityStudio.Reporting`** | `net10.0` | Exports self-contained WCAG 2.2 AA-compliant HTML5 Executive & Developer Audit Reports, **SARIF 2.1.0** static-analysis logs (GitHub Code Scanning / Visual Studio compatible), CSV/JSON ledgers, and Revised Section 508 Web Conformance matrices. |
| **`AccessibilityStudio.Cli`** | `net10.0` | Headless command-line scanner (`a11y-studio-cli`) for CI/CD pipelines, build gates, and scheduled workstation audits with deterministic exit codes. |
| **`AccessibilityStudio.WinForms`** | `net10.0-windows` | Multi-pane enterprise Windows Forms IDE featuring Workspace Explorer, Monaco Syntax-Highlighted Editor + Side-by-Side Refactor Diff, Live Chromium Visual Inspector (`forced-colors`, `400%` reflow, tab-stop badges), Rate-Limited SSO Batch Queue, and Color Contrast Workbench. |
| **`AccessibilityStudio.Tests`** | `net10.0` | Automated verification suite asserting rule detection, line/column accuracy, guided source fixes, contrast luminance math, WebVTT validation, SARIF 2.1.0 schema compliance, and HTML5 report generation. |

---

## Building & Running from Source

```bash
# 1. Compile all 5 projects in the solution (works on Windows 11, macOS, and Linux!)
dotnet build RaggieSoftAccessibilityStudio.slnx

# 2. Run the automated verification suite
dotnet run --project src/AccessibilityStudio.Tests/AccessibilityStudio.Tests.csproj

# 3. Run the headless CLI scanner against any folder or HTML/CSS/VTT file
dotnet run --project src/AccessibilityStudio.Cli/AccessibilityStudio.Cli.csproj -- scan ./sample --sarif audit.sarif --html audit-report.html

# 4. Launch the Windows Forms IDE (on Windows 11 Pro)
dotnet run --project src/AccessibilityStudio.WinForms/AccessibilityStudio.WinForms.csproj
```

---

## CLI Exit Code Contract (`a11y-studio-cli`)

| Exit Code | Meaning |
| :--- | :--- |
| `0` | Audit completed with zero violations at or above the configured failure threshold (`Critical` / `Serious` by default). |
| `1` | Invalid command-line arguments or target path not found. |
| `2` | Audit completed and detected one or more blocking WCAG 2.2 / Section 508 Web violations (fails CI/CD gate). |

---

## Fictitious Entities Notice

While **RaggieSoft Accessibility Studio** is real open-source engineering software created by **Michael P. Ragsdale**, any sample organizational names referenced in demonstration workspaces or sample test fixtures across the wider RaggieSoft universe (including CPI, VDU, OmniCorp, Apex Records, Prairie Coachworks, Sainte-Claire, BRT, CVTA, and any entity with "LLC", "Trust", or "Holdings") are fictitious works of world-building.
