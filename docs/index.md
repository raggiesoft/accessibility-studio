# RaggieSoft Accessibility Studio Documentation

Welcome to the official product manual, architecture guide, and rule reference for **RaggieSoft Accessibility Studio (Enterprise Edition v1.0)**—our source-level **WCAG 2.2 Level AA & Revised Section 508** workstation IDE and headless CI/CD command-line scanner.

## How Documentation Works in Accessibility Studio

RaggieSoft Accessibility Studio ships a dual documentation architecture designed for both **air-gapped enterprise Windows workstations** and **modern `.NET` web documentation pipelines**:

1. **In-App Context-Sensitive `F1` HTML5 Help Viewer (`HelpViewerForm`)**:
   - Press **`F1`** anywhere inside the Windows Forms IDE (or **`Shift+F1`** with any finding highlighted in the Error List) to open the native two-pane Help Viewer (**Contents**, **Rule Index A–Z**, and **Instant Search**).
   - Backed by `StudioHelpCatalog` (`AccessibilityStudio.Core.Documentation`) and local `WebView2` virtual-host mapping (`https://help.a11y.local`), it honors Windows 11 Light Mode, Dark Mode (`prefers-color-scheme`), and High-Contrast `forced-colors` automatically with zero internet access required.
   - To export the standalone offline HTML5 Help bundle to any folder from the command line:
     ```powershell
     a11y-studio-cli export-help .\Help
     ```

2. **DocFX Static Documentation & C# XML API Site (`docs/docfx.json`)**:
   - Built with Microsoft/.NET Foundation's **DocFX** (`modern` template, dark/light theme toggle, and full client-side search):
     ```powershell
     dotnet tool update -g docfx
     docfx docs/docfx.json --serve
     ```

## Core Chapters

- [Modern Windows Help Architecture (Why HTML5 + DocFX Replaced `.chm`)](articles/modern-windows-help-architecture.md)
- [Complete 25-Rule WCAG 2.2 & Revised Section 508 Catalog](articles/rule-catalog.md)
- [Headless CLI (`a11y-studio-cli`), SARIF 2.1.0 CI/CD & Enterprise MSI/GPO Guide](articles/cli-and-enterprise-deployment.md)

---

*Fictitious Entities Notice: While RaggieSoft Accessibility Studio is real open-source engineering software created by Michael P. Ragsdale, any sample organizational names referenced in demonstration workspaces across the wider RaggieSoft network (including CPI, VDU, OmniCorp, Apex Records, Prairie Coachworks, Sainte-Claire, BRT, CVTA, or any entity bearing "LLC", "Trust", or "Holdings") are fictitious works of world-building.*
