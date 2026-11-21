# RaggieSoft Accessibility Studio — C# / .NET 10 API Reference

This section documents the public namespaces and assemblies powering **RaggieSoft Accessibility Studio**:

- **`AccessibilityStudio.Core.Analysis`**: Deterministic HTML5/WAI-ARIA (`HtmlSourceAccessibilityAnalyzer`), CSS luminance/focus/motion (`CssAccessibilityAnalyzer`, `ColorContrastEngine`), and WebVTT closed-caption (`WebVttAccessibilityAnalyzer`) static analyzers.
- **`AccessibilityStudio.Core.Refactoring`**: Human-in-the-loop Guided Source Refactoring engine (`GuidedSourceRefactorer`) generating side-by-side Monaco diff previews.
- **`AccessibilityStudio.Core.Documentation`**: Single-source offline HTML5 manual catalog, context-sensitive `F1` topic router, and WCAG 2.2 AA Help bundle exporter (`StudioHelpCatalog`).
- **`AccessibilityStudio.Core.Services`**: Workspace project discovery (`WorkspaceProjectService`), polite rate-limited batch auditing (`BatchAuditQueueRunner`), and SHA-256 tamper-evident session logging (`AuditSessionLogger`).
- **`AccessibilityStudio.Reporting.Exporters`**: Self-audited HTML5 Executive Reports, OASIS SARIF 2.1.0, formula-safe CSV, and JSON exporters (`EnterpriseReportExporter`).
