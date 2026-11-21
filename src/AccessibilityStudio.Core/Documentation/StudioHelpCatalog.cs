// ================================================================================
// FILE: Documentation/StudioHelpCatalog.cs
// PROJECT: AccessibilityStudio.Core
// LICENSE: MIT License
// ================================================================================
// PLAIN-ENGLISH OVERVIEW (100-LEVEL MODERN WINDOWS HELP ARCHITECTURE GUIDE):
//   What is this file, and why did it replace Windows 2000/XP `.chm` files?
//     In the Windows 2000 and Windows XP era, desktop software compiled Help
//     manuals with Microsoft HTML Help Workshop (`hhc.exe`) into ITSS-compressed
//     `.chm` archives rendered by `hh.exe`. Today, `.chm` is retired and unsafe
//     for modern enterprise accessibility workstations for three reasons:
//       1. `hh.exe` embeds legacy Internet Explorer (`mshtml.dll`), which cannot
//          parse modern CSS (`color-scheme: light dark`, `prefers-color-scheme`,
//          `forced-colors: active`, CSS Grid, or 400% WCAG 1.4.10 reflow).
//       2. Windows blocks `.chm` files carrying Mark-of-the-Web (`Zone.Identifier`)
//          or opened from network shares, and many CIS/STIG security policies
//          disable ActiveX/ITSS archives outright.
//       3. HTML Help Workshop is discontinued and cannot run in headless CI/CD.
//
//     `StudioHelpCatalog` implements the modern Windows/.NET replacement:
//       - A single-source, strongly typed Help Knowledge Base covering all IDE
//         workflows, CLI exit codes, MSI/Group Policy deployment properties, and
//         all 25 deterministic WCAG 2.2 AA & Revised Section 508 rules.
//       - A zero-dependency Offline HTML5 Help Bundle Compiler (`ExportOfflineHtml5Bundle`)
//         that generates standalone, air-gapped `.html` pages, `help.css`, and
//         `search-index.json` into `<InstallDir>\Help\` (installed by our
//         InstallAware Studio X18 MSI package and viewable in `HelpViewerForm`
//         via `WebView2.SetVirtualHostNameToFolderMapping("help.a11y.local", ...)`).
//       - Every generated Help page follows WCAG 2.2 Level AA, `color-scheme: light dark`,
//         Windows High-Contrast `@media (forced-colors: active)`, skip links,
//         semantic landmarks, and 400% zoom reflow—and is verified by our own
//         automated test suite (`AccessibilityStudio.Tests`) for 0 violations!
// ================================================================================

using System.Net;
using System.Text;
using System.Text.Json;

namespace AccessibilityStudio.Core.Documentation;

/// <summary>
/// Represents a conceptual or reference chapter inside the RaggieSoft Accessibility Studio
/// offline HTML5 manual and context-sensitive <c>F1</c> Help Viewer.
/// </summary>
public sealed record HelpTopic(
    string TopicId,
    string Category,
    string Title,
    string Summary,
    IReadOnlyList<string> Keywords,
    string HtmlContent
);

/// <summary>
/// Represents a documented WCAG 2.2 &amp; Revised Section 508 static-analysis rule
/// indexed by <c>RuleId</c> so pressing <c>F1</c> or <c>Shift+F1</c> on any finding
/// in the IDE Error List jumps straight to its remediation guide and code examples.
/// </summary>
public sealed record HelpRuleEntry(
    string RuleId,
    string EngineCategory,
    string Severity,
    string Title,
    string WcagCriterion,
    string Section508Reference,
    bool HasAutomatedGuidedFix,
    string AssistiveTechImpact,
    string RemediationSummary,
    string FailingCodeExample,
    string PassingCodeExample
);

/// <summary>
/// Represents a ranked search hit returned by <see cref="StudioHelpCatalog.Search"/>
/// inside the WinForms <c>HelpViewerForm</c> instant search tab.
/// </summary>
public sealed record HelpSearchHit(
    string TopicId,
    string? AnchorId,
    string DisplayTitle,
    string Category,
    string Snippet,
    int Score
);

/// <summary>
/// Single-source enterprise documentation catalog, context-sensitive <c>F1</c> router,
/// and WCAG 2.2 AA offline HTML5 documentation bundle generator.
/// </summary>
public static class StudioHelpCatalog
{
    // Base URL for the companion online documentation portal (`https://a11y.raggiesoft.com/docs`).
    // Used ONLY when the engineer explicitly clicks "Open Online Docs" in the Help toolbar;
    // all normal `F1` help renders 100% offline with zero network requests!
    public const string OnlineDocsBaseUrl = "https://a11y.raggiesoft.com/docs";

    private static readonly IReadOnlyList<HelpRuleEntry> s_rules = BuildRuleEntries();
    private static readonly IReadOnlyList<HelpTopic> s_topics = BuildTopics(s_rules);

    /// <summary>
    /// Returns all ordered Help Manual chapters for the Contents tree.
    /// </summary>
    public static IReadOnlyList<HelpTopic> GetAllTopics() => s_topics;

    /// <summary>
    /// Returns all 25 documented HTML5, WAI-ARIA, CSS, and WebVTT rules sorted by Rule ID
    /// for the Help Viewer's alphabetical Rule Index tab.
    /// </summary>
    public static IReadOnlyList<HelpRuleEntry> GetAllRuleEntries() => s_rules;

    /// <summary>
    /// Looks up a specific Help chapter by its stable <paramref name="topicId"/>,
    /// defaulting to <c>welcome-overview</c> if not found.
    /// </summary>
    public static HelpTopic GetTopicOrDefault(string? topicId)
    {
        if (!string.IsNullOrWhiteSpace(topicId))
        {
            HelpTopic? match = s_topics.FirstOrDefault(
                t => string.Equals(t.TopicId, topicId.Trim(), StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                return match;
            }
        }

        return s_topics[0];
    }

    /// <summary>
    /// Resolves the best context-sensitive <c>F1</c> target (<c>TopicId</c> + optional <c>#anchor</c>)
    /// based on what the engineer is currently doing inside <c>MainIdeForm</c>.
    /// </summary>
    public static (string TopicId, string? AnchorId) ResolveContextTopic(
        string? selectedRuleId,
        bool preferSelectedRule,
        int activeCenterTabIndex,
        int activeBottomTabIndex)
    {
        // 1. If the engineer pressed Shift+F1 or has a rule selected while working in the
        //    Error List / Guided Fix pane, jump straight to `#rule-<RuleId>` in the Rule Reference!
        if (preferSelectedRule && !string.IsNullOrWhiteSpace(selectedRuleId))
        {
            HelpRuleEntry? rule = s_rules.FirstOrDefault(
                r => string.Equals(r.RuleId, selectedRuleId.Trim(), StringComparison.OrdinalIgnoreCase));
            if (rule is not null)
            {
                return ("rule-reference", $"rule-{rule.RuleId}");
            }
        }

        // 2. Otherwise inspect which upper workspace tab is active (`0`=Editor, `1`=Live DOM, `2`=SSO Batch)
        if (activeCenterTabIndex == 1)
        {
            return ("live-dom-emulation", null);
        }

        if (activeCenterTabIndex == 2)
        {
            return ("sso-batch-queue", null);
        }

        // 3. Or match specialized bottom diagnostic tabs (`1`=Guided Fix, `2`=Outline, `3`=Session Log)
        return activeBottomTabIndex switch
        {
            1 => ("guided-refactoring", null),
            2 => ("quickstart-workspace", "heading-landmark-outline"),
            3 => ("reports-and-logs", "session-audit-logs"),
            _ => ("welcome-overview", null)
        };
    }

    /// <summary>
    /// Performs fast, offline keyword &amp; rule-ID search across all Help chapters and all 25 rules.
    /// </summary>
    public static IReadOnlyList<HelpSearchHit> Search(string? rawQuery)
    {
        if (string.IsNullOrWhiteSpace(rawQuery))
        {
            // When the search box is empty, return top-level chapters so the list is immediately useful.
            return s_topics
                .Select(t => new HelpSearchHit(t.TopicId, null, t.Title, t.Category, t.Summary, 10))
                .ToList();
        }

        string[] terms = rawQuery
            .Trim()
            .Split([' ', '\t', ',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        List<HelpSearchHit> hits = [];

        // 1. Score conceptual Help chapters
        foreach (HelpTopic topic in s_topics)
        {
            int score = 0;
            foreach (string term in terms)
            {
                if (topic.Title.Contains(term, StringComparison.OrdinalIgnoreCase)) score += 40;
                if (topic.Keywords.Any(k => k.Contains(term, StringComparison.OrdinalIgnoreCase))) score += 30;
                if (topic.Summary.Contains(term, StringComparison.OrdinalIgnoreCase)) score += 20;
                if (topic.HtmlContent.Contains(term, StringComparison.OrdinalIgnoreCase)) score += 8;
            }

            if (score > 0)
            {
                hits.Add(new HelpSearchHit(topic.TopicId, null, topic.Title, topic.Category, topic.Summary, score));
            }
        }

        // 2. Score individual WCAG / Section 508 Rule Reference entries so searching `1.4.3`,
        //    `tabindex`, `WCAG-TABLE-FAKE-HEADER`, or `WebVTT` jumps directly to that rule anchor!
        foreach (HelpRuleEntry rule in s_rules)
        {
            int score = 0;
            foreach (string term in terms)
            {
                if (rule.RuleId.Contains(term, StringComparison.OrdinalIgnoreCase)) score += 60;
                if (rule.WcagCriterion.Contains(term, StringComparison.OrdinalIgnoreCase)) score += 45;
                if (rule.Section508Reference.Contains(term, StringComparison.OrdinalIgnoreCase)) score += 35;
                if (rule.Title.Contains(term, StringComparison.OrdinalIgnoreCase)) score += 35;
                if (rule.AssistiveTechImpact.Contains(term, StringComparison.OrdinalIgnoreCase)) score += 15;
                if (rule.RemediationSummary.Contains(term, StringComparison.OrdinalIgnoreCase)) score += 15;
            }

            if (score > 0)
            {
                hits.Add(new HelpSearchHit(
                    TopicId: "rule-reference",
                    AnchorId: $"rule-{rule.RuleId}",
                    DisplayTitle: $"{rule.RuleId} — {rule.Title}",
                    Category: $"Rule Reference ({rule.EngineCategory})",
                    Snippet: $"{rule.WcagCriterion} • {rule.RemediationSummary}",
                    Score: score));
            }
        }

        return hits
            .OrderByDescending(h => h.Score)
            .ThenBy(h => h.DisplayTitle, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Builds the shared, WCAG 2.2 AA-compliant stylesheet supporting Light Mode, Dark Mode
    /// (<c>color-scheme: light dark</c> + <c>@media (prefers-color-scheme: dark)</c>),
    /// Windows High Contrast (<c>@media (forced-colors: active)</c>), and 400% Reflow zoom.
    /// </summary>
    public static string BuildHelpStylesheetCss() =>
        """
        /* ==========================================================================
           FILE: help.css — Offline HTML5 Product Manual Stylesheet
           STANDARDS: WCAG 2.2 Level AA (>= 4.5:1 text contrast, 3:1 UI contrast,
           color-scheme: light dark, forced-colors: active, 400% Reflow safe)
           ========================================================================== */
        :root {
          color-scheme: light dark;
          --bg-canvas: #f8fafc;
          --bg-surface: #ffffff;
          --bg-subtle: #f1f5f9;
          --bg-code: #0f172a;
          --fg-code: #f8fafc;
          --fg-primary: #0f172a;
          --fg-secondary: #334155;
          --border-color: #64748b;
          --brand-accent: #0b5cab;
          --brand-accent-soft: #e0f2fe;
          --badge-crit-bg: #fef2f2;
          --badge-crit-fg: #991b1b;
          --badge-ser-bg: #fffbeb;
          --badge-ser-fg: #92400e;
          --badge-mod-bg: #eff6ff;
          --badge-mod-fg: #1e40af;
          --focus-ring: #0b5cab;
        }

        @media (prefers-color-scheme: dark) {
          :root {
            --bg-canvas: #0b1120;
            --bg-surface: #111827;
            --bg-subtle: #1e293b;
            --bg-code: #020617;
            --fg-code: #f1f5f9;
            --fg-primary: #f8fafc;
            --fg-secondary: #cbd5e1;
            --border-color: #94a3b8;
            --brand-accent: #60a5fa;
            --brand-accent-soft: #172554;
            --badge-crit-bg: #450a0a;
            --badge-crit-fg: #fecaca;
            --badge-ser-bg: #451a03;
            --badge-ser-fg: #fde68a;
            --badge-mod-bg: #172554;
            --badge-mod-fg: #bfdbfe;
            --focus-ring: #60a5fa;
          }
        }

        * {
          box-sizing: border-box;
        }

        body {
          margin: 0;
          font-family: "Segoe UI", system-ui, -apple-system, sans-serif;
          font-size: 1rem;
          line-height: 1.62;
          background-color: var(--bg-canvas);
          color: var(--fg-primary);
        }

        /* Visible keyboard skip link (WCAG 2.4.1 Bypass Blocks) */
        .skip-link {
          position: absolute;
          left: -9999px;
          top: 0.5rem;
          z-index: 1000;
          padding: 0.6rem 1rem;
          background-color: var(--bg-surface);
          color: var(--fg-primary);
          border: 2px solid var(--focus-ring);
          border-radius: 0.375rem;
          font-weight: 700;
        }

        .skip-link:focus {
          left: 0.75rem;
        }

        a {
          color: var(--brand-accent);
          text-underline-offset: 0.18em;
        }

        a:focus-visible,
        button:focus-visible,
        summary:focus-visible,
        [tabindex="-1"]:focus-visible {
          outline: 3px solid var(--focus-ring);
          outline-offset: 2px;
        }

        header.doc-banner {
          background-color: var(--bg-surface);
          border-bottom: 1px solid var(--border-color);
          padding: 1rem 1.5rem;
        }

        .doc-eyebrow {
          font-size: 0.82rem;
          font-weight: 700;
          text-transform: uppercase;
          letter-spacing: 0.05em;
          color: var(--fg-secondary);
          margin: 0 0 0.35rem 0;
        }

        nav.doc-chapter-strip {
          margin-top: 0.65rem;
          display: flex;
          flex-wrap: wrap;
          gap: 0.45rem;
        }

        nav.doc-chapter-strip a {
          display: inline-block;
          padding: 0.28rem 0.65rem;
          border-radius: 0.35rem;
          border: 1px solid var(--border-color);
          background-color: var(--bg-subtle);
          color: var(--fg-primary);
          text-decoration: none;
          font-size: 0.86rem;
          font-weight: 600;
        }

        nav.doc-chapter-strip a[aria-current="page"] {
          background-color: var(--brand-accent-soft);
          border-color: var(--brand-accent);
        }

        main.doc-main {
          max-width: 66rem;
          margin: 0 auto;
          padding: 1.5rem;
        }

        h1 {
          font-size: 1.75rem;
          line-height: 1.25;
          margin: 0 0 0.5rem 0;
        }

        h2 {
          font-size: 1.32rem;
          line-height: 1.3;
          margin: 1.85rem 0 0.65rem 0;
          padding-bottom: 0.3rem;
          border-bottom: 1px solid var(--border-color);
        }

        h3 {
          font-size: 1.1rem;
          line-height: 1.35;
          margin: 1.25rem 0 0.45rem 0;
        }

        .doc-lead {
          font-size: 1.06rem;
          color: var(--fg-secondary);
          margin: 0 0 1.25rem 0;
        }

        .callout {
          background-color: var(--bg-surface);
          border: 1px solid var(--border-color);
          border-left: 5px solid var(--brand-accent);
          border-radius: 0.5rem;
          padding: 0.95rem 1.15rem;
          margin: 1rem 0;
        }

        .card-grid {
          display: grid;
          grid-template-columns: repeat(auto-fit, minmax(16rem, 1fr));
          gap: 1rem;
          margin: 1rem 0;
        }

        .doc-card,
        .rule-card {
          background-color: var(--bg-surface);
          border: 1px solid var(--border-color);
          border-radius: 0.5rem;
          padding: 1.1rem 1.25rem;
          margin-bottom: 1rem;
        }

        .rule-meta {
          display: flex;
          flex-wrap: wrap;
          gap: 0.5rem;
          margin: 0.4rem 0 0.75rem 0;
          font-size: 0.86rem;
        }

        .badge {
          display: inline-block;
          padding: 0.15rem 0.55rem;
          border-radius: 999px;
          font-weight: 700;
          border: 1px solid var(--border-color);
        }

        .badge-critical {
          background-color: var(--badge-crit-bg);
          color: var(--badge-crit-fg);
        }

        .badge-serious {
          background-color: var(--badge-ser-bg);
          color: var(--badge-ser-fg);
        }

        .badge-moderate {
          background-color: var(--badge-mod-bg);
          color: var(--badge-mod-fg);
        }

        code,
        kbd {
          font-family: Consolas, "Cascadia Mono", ui-monospace, monospace;
          font-size: 0.9em;
        }

        kbd {
          display: inline-block;
          padding: 0.1rem 0.42rem;
          border: 1px solid var(--border-color);
          border-radius: 0.25rem;
          background-color: var(--bg-subtle);
          font-weight: 600;
        }

        p code,
        li code,
        td code,
        th code {
          background-color: var(--bg-subtle);
          padding: 0.1rem 0.35rem;
          border-radius: 0.25rem;
        }

        pre {
          background-color: var(--bg-code);
          color: var(--fg-code);
          padding: 0.9rem 1rem;
          border-radius: 0.45rem;
          border: 1px solid var(--border-color);
          overflow-x: auto;
          margin: 0.55rem 0 1rem 0;
        }

        pre code {
          background: transparent;
          padding: 0;
          color: inherit;
        }

        .table-wrap {
          overflow-x: auto;
          margin: 1rem 0;
        }

        table.doc-table {
          width: 100%;
          border-collapse: collapse;
          background-color: var(--bg-surface);
        }

        table.doc-table caption {
          text-align: left;
          font-weight: 700;
          margin-bottom: 0.45rem;
          color: var(--fg-secondary);
        }

        table.doc-table th,
        table.doc-table td {
          border: 1px solid var(--border-color);
          padding: 0.6rem 0.75rem;
          text-align: left;
          vertical-align: top;
        }

        table.doc-table thead th {
          background-color: var(--bg-subtle);
        }

        footer.doc-footer {
          max-width: 66rem;
          margin: 2rem auto 0 auto;
          padding: 1rem 1.5rem 2rem 1.5rem;
          border-top: 1px solid var(--border-color);
          font-size: 0.88rem;
          color: var(--fg-secondary);
        }

        /* Windows High-Contrast / Forced-Colors Mode explicit system-keyword support */
        @media (forced-colors: active) {
          .doc-card,
          .rule-card,
          .callout,
          pre,
          kbd,
          .badge {
            border: 1px solid CanvasText;
          }

          nav.doc-chapter-strip a[aria-current="page"] {
            outline: 2px solid Highlight;
          }
        }

        @media (prefers-reduced-motion: reduce) {
          * {
            scroll-behavior: auto !important;
          }
        }

        @media print {
          header.doc-banner nav.doc-chapter-strip,
          .skip-link {
            display: none;
          }

          body {
            background: #ffffff;
            color: #000000;
          }
        }
        """;

    /// <summary>
    /// Renders a complete, self-contained HTML5 Help document for <paramref name="topicId"/>.
    /// When <paramref name="externalStylesheet"/> is <c>true</c> (used when writing the disk
    /// bundle to <c>&lt;InstallDir&gt;\Help\</c>), links <c>help.css</c>; otherwise embeds
    /// the CSS inline so <c>WebView2.NavigateToString</c> works even without files on disk!
    /// </summary>
    public static string BuildStandaloneTopicHtml(
        string topicId,
        string? anchorId = null,
        bool externalStylesheet = false)
    {
        HelpTopic topic = GetTopicOrDefault(topicId);

        StringBuilder navLinks = new();
        foreach (HelpTopic item in s_topics)
        {
            string currentAttr = string.Equals(item.TopicId, topic.TopicId, StringComparison.OrdinalIgnoreCase)
                ? " aria-current=\"page\""
                : string.Empty;
            navLinks.AppendLine(
                $"        <a href=\"{Enc(item.TopicId)}.html\"{currentAttr}>{Enc(item.Title)}</a>");
        }

        string styleBlock = externalStylesheet
            ? "  <link rel=\"stylesheet\" href=\"help.css\">"
            : $"  <style>\n{BuildHelpStylesheetCss()}\n  </style>";

        // Optional scroll-to-anchor helper when rendered via in-memory `NavigateToString`
        string scrollScript = !string.IsNullOrWhiteSpace(anchorId)
            ? $$"""
              <script>
                window.addEventListener('DOMContentLoaded', function () {
                  var el = document.getElementById({{JsonSerializer.Serialize(anchorId)}});
                  if (el) { el.scrollIntoView({ block: 'start' }); }
                });
              </script>
              """
            : string.Empty;

        return $$"""
            <!doctype html>
            <html lang="en">
            <head>
              <meta charset="utf-8">
              <meta name="viewport" content="width=device-width, initial-scale=1">
              <meta name="color-scheme" content="light dark">
              <title>{{Enc(topic.Title)}} — RaggieSoft Accessibility Studio Help</title>
            {{styleBlock}}
            </head>
            <body>
              <a class="skip-link" href="#main-content">Skip to main content</a>
              <header class="doc-banner">
                <p class="doc-eyebrow">RaggieSoft Accessibility Studio • Offline Workstation Manual ({{Enc(topic.Category)}})</p>
                <nav class="doc-chapter-strip" aria-label="Help Manual Chapters">
            {{navLinks.ToString().TrimEnd()}}
                </nav>
              </header>
              <main id="main-content" class="doc-main" tabindex="-1">
                <h1 id="topic-heading">{{Enc(topic.Title)}}</h1>
                <p class="doc-lead">{{Enc(topic.Summary)}}</p>
            {{topic.HtmlContent}}
              </main>
              <footer class="doc-footer">
                <p>
                  <strong>RaggieSoft Accessibility Studio (Enterprise Edition v1.0)</strong> • Press <kbd>F1</kbd> inside any IDE pane for context-sensitive documentation, or <kbd>Shift</kbd>+<kbd>F1</kbd> to jump straight to the selected Error List rule.
                </p>
                <p>
                  Fictitious Entities Disclaimer: While RaggieSoft Accessibility Studio is real open-source engineering software created by Michael P. Ragsdale, any sample organizational names referenced in demonstration fixtures across the wider RaggieSoft creative network (such as CPI, VDU, OmniCorp, Apex Records, Prairie Coachworks, Sainte-Claire, BRT, CVTA, or any entity bearing "LLC", "Trust", or "Holdings") are fictitious works of world-building.
                </p>
              </footer>
            {{scrollScript}}
            </body>
            </html>
            """;
    }

    /// <summary>
    /// Exports the complete offline HTML5 Help manual (<c>index.html</c>, per-chapter <c>.html</c>
    /// pages, <c>help.css</c>, and <c>search-index.json</c>) to <paramref name="outputDirectory"/>.
    /// Used by <c>a11y-studio-cli export-help</c>, <c>stage-installaware-payload.ps1</c>, and
    /// local IDE Help cache initialization.
    /// </summary>
    public static IReadOnlyList<string> ExportOfflineHtml5Bundle(string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        List<string> writtenFiles = [];

        // 1. Write shared WCAG 2.2 AA stylesheet (`help.css`)
        string cssPath = Path.Combine(outputDirectory, "help.css");
        File.WriteAllText(cssPath, BuildHelpStylesheetCss(), Encoding.UTF8);
        writtenFiles.Add(cssPath);

        // 2. Write each Help chapter `<topicId>.html`
        foreach (HelpTopic topic in s_topics)
        {
            string html = BuildStandaloneTopicHtml(topic.TopicId, anchorId: null, externalStylesheet: true);
            string topicPath = Path.Combine(outputDirectory, $"{topic.TopicId}.html");
            File.WriteAllText(topicPath, html, Encoding.UTF8);
            writtenFiles.Add(topicPath);
        }

        // 3. Also write `index.html` mirroring `welcome-overview.html` as the root entry point
        string indexPath = Path.Combine(outputDirectory, "index.html");
        File.WriteAllText(
            indexPath,
            BuildStandaloneTopicHtml("welcome-overview", anchorId: null, externalStylesheet: true),
            Encoding.UTF8);
        writtenFiles.Add(indexPath);

        // 4. Write machine-readable `search-index.json` for external static hosting or tooling
        var indexPayload = new
        {
            product = "RaggieSoft Accessibility Studio",
            version = "1.0.0",
            generatedUtc = DateTimeOffset.UtcNow,
            topics = s_topics.Select(t => new
            {
                id = t.TopicId,
                href = $"{t.TopicId}.html",
                category = t.Category,
                title = t.Title,
                summary = t.Summary,
                keywords = t.Keywords
            }),
            rules = s_rules.Select(r => new
            {
                ruleId = r.RuleId,
                href = $"rule-reference.html#rule-{r.RuleId}",
                category = r.EngineCategory,
                severity = r.Severity,
                title = r.Title,
                wcag = r.WcagCriterion,
                section508 = r.Section508Reference,
                hasGuidedFix = r.HasAutomatedGuidedFix
            })
        };

        string searchJsonPath = Path.Combine(outputDirectory, "search-index.json");
        File.WriteAllText(
            searchJsonPath,
            JsonSerializer.Serialize(indexPayload, new JsonSerializerOptions { WriteIndented = true }),
            Encoding.UTF8);
        writtenFiles.Add(searchJsonPath);

        // 5. Write pluggable RaggieSoft Help Module manifest (`module.rshm.json`) so the
        //    standalone `RaggieSoftHelp.exe` viewer auto-discovers this product's manual,
        //    Contents tree, and 25-rule Index alongside any other installed RaggieSoft apps!
        var moduleManifest = new
        {
            schemaVersion = "1.0",
            moduleId = "raggiesoft.accessibility-studio",
            productName = "RaggieSoft Accessibility Studio",
            productVersion = "1.0.0",
            publisher = "RaggieSoft Media (Michael P. Ragsdale)",
            description = "Enterprise WCAG 2.2 AA & Revised Section 508 Workstation IDE, Guided Refactoring & Headless CLI Manual",
            defaultTopicId = "welcome-overview",
            onlineDocsUrl = OnlineDocsBaseUrl,
            topics = s_topics.Select(t => new
            {
                id = t.TopicId,
                fileName = $"{t.TopicId}.html",
                category = t.Category,
                title = t.Title,
                summary = t.Summary,
                keywords = t.Keywords
            }),
            indexEntries = s_rules.Select(r => new
            {
                entryId = r.RuleId,
                displayText = $"{r.RuleId} — {r.Title}",
                category = r.EngineCategory,
                badge = r.Severity,
                subtitle = $"{r.WcagCriterion} • {r.Section508Reference}",
                topicId = "rule-reference",
                anchorId = $"rule-{r.RuleId}"
            })
        };

        string manifestPath = Path.Combine(outputDirectory, "module.rshm.json");
        File.WriteAllText(
            manifestPath,
            JsonSerializer.Serialize(moduleManifest, new JsonSerializerOptions { WriteIndented = true }),
            Encoding.UTF8);
        writtenFiles.Add(manifestPath);

        return writtenFiles;
    }

    private static string Enc(string value) => WebUtility.HtmlEncode(value);

    // --------------------------------------------------------------------------------
    // BUILT-IN 25-RULE WCAG 2.2 & SECTION 508 KNOWLEDGE BASE
    // --------------------------------------------------------------------------------

    private static IReadOnlyList<HelpRuleEntry> BuildRuleEntries() =>
    [
        new(
            "WCAG-AMBIGUOUS-LINK",
            "HTML5 & WAI-ARIA",
            "Moderate",
            "Non-Descriptive Hyperlink Text ('Click Here' / 'Read More')",
            "WCAG 2.4.4 Link Purpose (In Context) (Level A)",
            "Revised 508 E205.4 / 36 CFR 1194",
            false,
            "Screen-reader users navigating via the Links Rotor (Insert+F7 in JAWS/NVDA) hear an alphabetical list of standalone phrases ('Click here', 'Read more') stripped of surrounding paragraph context.",
            "Replace vague call-to-action link text with specific destination nouns, or provide contextual accessible naming that preserves the visible label.",
            "<p>To review the syllabus, <a href=\"syllabus.html\">click here</a>.</p>",
            "<p>Review the <a href=\"syllabus.html\">Fall 2026 Course Syllabus</a>.</p>"
        ),
        new(
            "WCAG-ARIA-HIDDEN-FOCUS",
            "HTML5 & WAI-ARIA",
            "Critical",
            "Focusable Control Marked with aria-hidden=\"true\"",
            "WCAG 4.1.2 Name, Role, Value (Level A)",
            "Revised 508 E205.4 / 36 CFR 1194",
            true,
            "Keyboard Tab focus lands on an invisible ghost stop where NVDA, JAWS, and Narrator announce silence or 'blank' because the element was pruned from the accessibility tree while remaining focusable.",
            "Remove aria-hidden=\"true\" from interactive elements (use Guided Fix Ctrl+D), or add tabindex=\"-1\" and disabled/inert if truly hidden from everyone.",
            "<button type=\"button\" aria-hidden=\"true\">Submit Assignment</button>",
            "<button type=\"button\">Submit Assignment</button>"
        ),
        new(
            "WCAG-AUTOCOMPLETE",
            "HTML5 & WAI-ARIA",
            "Moderate",
            "Personal User Input Missing autocomplete Purpose Token",
            "WCAG 1.3.5 Identify Input Purpose (Level AA)",
            "Revised 508 E205.4 / ADA Title II Web",
            true,
            "Users with motor disabilities, dyslexia, or cognitive fatigue cannot rely on assistive autofill or personalized symbol overlays when email, name, or telephone fields omit standard autocomplete tokens.",
            "Add the matching HTML5 autocomplete token (such as autocomplete=\"email\", \"name\", or \"tel\") using Guided Fix.",
            "<label for=\"student-email\">Email</label>\n<input id=\"student-email\" type=\"email\" name=\"email\">",
            "<label for=\"student-email\">Email</label>\n<input id=\"student-email\" type=\"email\" name=\"email\" autocomplete=\"email\">"
        ),
        new(
            "WCAG-CSS-CONTRAST",
            "CSS Contrast & Motion",
            "Serious",
            "Insufficient CSS Foreground/Background Luminance Contrast (< 4.5:1)",
            "WCAG 1.4.3 Contrast (Minimum) (Level AA)",
            "Revised 508 E205.4 / 36 CFR 1194",
            true,
            "Readers with low vision, age-related contrast loss, or glare conditions cannot distinguish muted gray or pastel foreground text against its paired background.",
            "Adjust the foreground or background color token to achieve at least 4.5:1 for normal copy (3.0:1 for >= 24px or >= 18.66px bold text). Use Guided Fix or Ctrl+Shift+C (Contrast Workbench) to auto-solve the nearest passing hex.",
            ".course-subtitle {\n  color: #8c96a6;\n  background-color: #ffffff;\n}",
            ".course-subtitle {\n  color: #525b69;\n  background-color: #ffffff;\n}"
        ),
        new(
            "WCAG-CSS-FOCUS-SUPPRESSED",
            "CSS Contrast & Motion",
            "Critical",
            "Keyboard Focus Indicator Suppressed via outline: none / 0",
            "WCAG 2.4.7 Focus Visible (Level AA)",
            "Revised 508 E205.4 / 36 CFR 1194",
            true,
            "Sighted keyboard-only and switch-device users completely lose track of which link, button, or form field currently holds focus when CSS strips the focus ring without a high-contrast replacement.",
            "Replace outline: none with a clear :focus-visible outline of at least 2px–3px thickness and >= 3:1 contrast.",
            ".nav-btn:focus {\n  outline: none;\n}",
            ".nav-btn:focus-visible {\n  outline: 3px solid #0b5cab;\n  outline-offset: 2px;\n}"
        ),
        new(
            "WCAG-CSS-REDUCED-MOTION",
            "CSS Contrast & Motion",
            "Advisory",
            "CSS Keyframe Animation Without prefers-reduced-motion Guard",
            "WCAG 2.3.3 Animation from Interactions / 2.2.2 Pause, Stop, Hide",
            "Enterprise Vestibular Safety Profile",
            false,
            "Continuous CSS @keyframes animations or parallax transitions can trigger nausea, migraines, or vestibular disorientation for users who enabled 'Minimize animation' in Windows Accessibility Settings.",
            "Gate non-essential animations or attenuate durations inside @media (prefers-reduced-motion: reduce).",
            "@keyframes pulseBanner {\n  from { transform: scale(1); }\n  to { transform: scale(1.06); }\n}",
            "@media (prefers-reduced-motion: no-preference) {\n  @keyframes pulseBanner {\n    from { transform: scale(1); }\n    to { transform: scale(1.06); }\n  }\n}"
        ),
        new(
            "WCAG-FORM-LABEL",
            "HTML5 & WAI-ARIA",
            "Critical",
            "Form Control Missing Programmatic <label> or Accessible Name",
            "WCAG 1.3.1 Info and Relationships & 3.3.2 Labels or Instructions (Level A)",
            "Revised 508 E205.4 / 36 CFR 1194",
            true,
            "Screen readers announce 'edit text blank' and voice-control software ('Click Email') cannot target inputs that rely solely on disappearing placeholder text.",
            "Associate a persistent visible <label for=\"id\"> element with the control's id attribute.",
            "<input type=\"text\" placeholder=\"Search course catalog\">",
            "<label for=\"catalog-search\">Search course catalog</label>\n<input id=\"catalog-search\" type=\"search\">"
        ),
        new(
            "WCAG-HEADING-SKIP",
            "HTML5 & WAI-ARIA",
            "Serious",
            "Skipped Heading Hierarchy Level (e.g., H1 Directly to H4)",
            "WCAG 1.3.1 Info and Relationships & 2.4.6 Headings and Labels",
            "Revised 508 E205.4 / 36 CFR 1194",
            true,
            "Screen-reader users skimming a document outline with the H shortcut key perceive missing structural subsections when authors pick heading tags for font size instead of hierarchy.",
            "Keep heading levels sequential (H1 -> H2 -> H3) and control visual typography in CSS. Use Guided Fix to normalize the skipped heading level.",
            "<h1>Module 4: Routing</h1>\n<h4>BGP Path Selection</h4>",
            "<h1>Module 4: Routing</h1>\n<h2>BGP Path Selection</h2>"
        ),
        new(
            "WCAG-HTML-LANG",
            "HTML5 & WAI-ARIA",
            "Serious",
            "Root <html> Element Missing Valid BCP 47 lang Attribute",
            "WCAG 3.1.1 Language of Page (Level A)",
            "Revised 508 E205.4 / 36 CFR 1194",
            true,
            "Without lang=\"en\" (or the page's primary BCP 47 language code), speech synthesizers fall back to the OS locale voice and mispronounce every word on multilingual workstations.",
            "Add lang=\"en\" (or appropriate BCP 47 tag) to the root <html> tag via Guided Fix.",
            "<!doctype html>\n<html>",
            "<!doctype html>\n<html lang=\"en\">"
        ),
        new(
            "WCAG-IFRAME-TITLE",
            "HTML5 & WAI-ARIA",
            "Serious",
            "Embedded <iframe> Missing Descriptive title Attribute",
            "WCAG 4.1.2 Name, Role, Value & 2.4.1 Bypass Blocks (Level A)",
            "Revised 508 E205.4 / 36 CFR 1194",
            true,
            "Assistive technologies announce untitled frames by raw embed URL, leaving students and citizens unable to tell an embedded video player apart from a payment widget.",
            "Add a concise, descriptive title=\"...\" attribute to every <iframe>.",
            "<iframe src=\"https://player.example.edu/lecture-04\"></iframe>",
            "<iframe src=\"https://player.example.edu/lecture-04\" title=\"Lecture 4: Packet Switching Video Player\"></iframe>"
        ),
        new(
            "WCAG-IMG-ALT",
            "HTML5 & WAI-ARIA",
            "Critical",
            "Image Element Missing alt Attribute",
            "WCAG 1.1.1 Non-text Content (Level A)",
            "Revised 508 E205.4 / 36 CFR 1194",
            true,
            "When an <img> omits alt entirely, screen readers read out raw hashed filenames ('8f91c2a_hero_v3.jpg, graphic').",
            "Supply meaningful alt=\"...\" text for informative graphics, or explicit empty alt=\"\" when purely decorative.",
            "<img src=\"network-topology.png\">",
            "<img src=\"network-topology.png\" alt=\"Three-tier campus network topology connecting core, distribution, and access switches\">"
        ),
        new(
            "WCAG-IMG-REDUNDANT-ALT",
            "HTML5 & WAI-ARIA",
            "Advisory",
            "Redundant 'Image of' / 'Picture of' Prefix or Raw Filename in alt Text",
            "WCAG 1.1.1 Non-text Content (Level A)",
            "Revised 508 E205.4 Best Practice",
            false,
            "Screen readers already announce the 'graphic' or 'image' role, so alt=\"Image of campus map\" reads aloud as 'Image of campus map, graphic'.",
            "Describe the information conveyed by the graphic directly without repeating 'image of', 'photo of', or '.png'.",
            "<img src=\"map.png\" alt=\"Image of North Campus accessibility ramp map.png\">",
            "<img src=\"map.png\" alt=\"North Campus map highlighting step-free entrances on West Quad\">"
        ),
        new(
            "WCAG-MAIN-LANDMARK",
            "HTML5 & WAI-ARIA",
            "Moderate",
            "Document Missing Single Primary <main> Landmark",
            "WCAG 1.3.1 Info and Relationships & 2.4.1 Bypass Blocks (Level A)",
            "Revised 508 E205.4 / 36 CFR 1194",
            false,
            "Screen-reader landmark navigation (D in NVDA, R in JAWS) cannot jump past repetitive headers and sidebars when a page lacks a <main> container or defines multiple non-inert <main> landmarks.",
            "Wrap primary page content inside exactly one <main id=\"main-content\"> landmark per document.",
            "<body>\n  <div class=\"content\"><h1>Course Overview</h1></div>\n</body>",
            "<body>\n  <main id=\"main-content\"><h1>Course Overview</h1></main>\n</body>"
        ),
        new(
            "WCAG-MEDIA-CAPTIONS",
            "HTML5 & WAI-ARIA",
            "Critical",
            "HTML5 <video> Missing Synchronized <track kind=\"captions\">",
            "WCAG 1.2.2 Captions (Prerecorded) (Level A)",
            "Revised 508 E205.4 / 503.4 Caption Controls",
            true,
            "Deaf and hard-of-hearing learners cannot access spoken instruction or non-speech audio cues in prerecorded lectures without synchronized closed captions.",
            "Include at least one <track kind=\"captions\" src=\"...vtt\" srclang=\"en\" label=\"English\"> child inside <video>.",
            "<video controls src=\"lecture01.mp4\"></video>",
            "<video controls src=\"lecture01.mp4\">\n  <track kind=\"captions\" src=\"lecture01.en.vtt\" srclang=\"en\" label=\"English\">\n</video>"
        ),
        new(
            "WCAG-MULTI-NAV-LABEL",
            "HTML5 & WAI-ARIA",
            "Moderate",
            "Multiple <nav> Landmarks Without Unique aria-label / aria-labelledby",
            "WCAG 1.3.1 Info and Relationships & 2.4.1 Bypass Blocks",
            "Revised 508 E205.4 / 36 CFR 1194",
            false,
            "When a page contains Primary, Breadcrumb, and Footer <nav> regions without labels, screen readers list three identical 'navigation' landmarks.",
            "Give each <nav> a concise, unique accessible name such as aria-label=\"Primary\" and aria-label=\"Breadcrumbs\".",
            "<nav>...</nav>\n<nav>...</nav>",
            "<nav aria-label=\"Primary\">...</nav>\n<nav aria-label=\"Course Breadcrumbs\">...</nav>"
        ),
        new(
            "WCAG-PAGE-TITLE",
            "HTML5 & WAI-ARIA",
            "Serious",
            "Document <head> Missing Non-Empty <title>",
            "WCAG 2.4.2 Page Titled (Level A)",
            "Revised 508 E205.4 / 36 CFR 1194",
            true,
            "The document <title> is the very first announcement spoken by screen readers on page load and distinguishes open browser tabs.",
            "Include a descriptive <title>Page Topic — Site Name</title> inside <head>.",
            "<head>\n  <meta charset=\"utf-8\">\n</head>",
            "<head>\n  <meta charset=\"utf-8\">\n  <title>Financial Aid Application — Student Portal</title>\n</head>"
        ),
        new(
            "WCAG-POSITIVE-TABINDEX",
            "HTML5 & WAI-ARIA",
            "Serious",
            "Positive tabindex Value (> 0) Disrupts Sequential Focus Order",
            "WCAG 2.4.3 Focus Order (Level A)",
            "Revised 508 E205.4 / 36 CFR 1194",
            true,
            "Any tabindex=\"1\" or higher hijacks keyboard navigation ahead of every natural DOM control, jumping focus erratically across the viewport.",
            "Replace positive tabindex values with tabindex=\"0\" (via Guided Fix) and arrange elements in logical DOM order.",
            "<a href=\"#help\" tabindex=\"4\">Help Desk</a>",
            "<a href=\"#help\">Help Desk</a>"
        ),
        new(
            "WCAG-SVG-ACCESSIBLE",
            "HTML5 & WAI-ARIA",
            "Serious",
            "Inline <svg> Graphic Missing Accessible Title or aria-hidden=\"true\"",
            "WCAG 1.1.1 Non-text Content & 4.1.2 Name, Role, Value (Level A)",
            "Revised 508 E205.4 / 36 CFR 1194",
            true,
            "Unmarked inline <svg> icons may be announced as unnamed graphics or skipped inconsistently across browsers and screen readers.",
            "Add aria-hidden=\"true\" to decorative icon SVGs, or role=\"img\" plus aria-label / <title> for standalone informative SVGs.",
            "<svg viewBox=\"0 0 24 24\"><path d=\"M12 2L2 22h20L12 2z\"/></svg>",
            "<svg aria-hidden=\"true\" viewBox=\"0 0 24 24\"><path d=\"M12 2L2 22h20L12 2z\"/></svg>"
        ),
        new(
            "WCAG-TABLE-FAKE-HEADER",
            "HTML5 & WAI-ARIA",
            "Serious",
            "Data Table Uses Visual Bold <td> Cells Instead of Semantic <th scope=\"col\">",
            "WCAG 1.3.1 Info and Relationships (Level A)",
            "Revised 508 E205.4 / 36 CFR 1194",
            true,
            "Legacy WYSIWYG and LMS editors often style top rows with <td><strong> or font-weight:bold. Screen-reader table navigation (Ctrl+Alt+Arrow) cannot announce column headers as users move across data cells.",
            "Convert header cells from <td> to <th scope=\"col\"> inside <thead> using Guided Fix (Ctrl+D).",
            "<table>\n  <tr><td style=\"font-weight:bold\">Course</td><td style=\"font-weight:bold\">Credits</td></tr>\n  <tr><td>CS 101</td><td>3</td></tr>\n</table>",
            "<table>\n  <thead>\n    <tr><th scope=\"col\">Course</th><th scope=\"col\">Credits</th></tr>\n  </thead>\n  <tbody>\n    <tr><td>CS 101</td><td>3</td></tr>\n  </tbody>\n</table>"
        ),
        new(
            "WCAG-TH-SCOPE",
            "HTML5 & WAI-ARIA",
            "Moderate",
            "Table Header <th> Missing Explicit scope=\"col\" or scope=\"row\"",
            "WCAG 1.3.1 Info and Relationships (Level A)",
            "Revised 508 E205.4 / 36 CFR 1194",
            true,
            "Ambiguous <th> cells in multi-axis data tables can bind to the wrong axis in older assistive technology table models unless scope is explicit.",
            "Specify scope=\"col\" for column headers and scope=\"row\" for row headers via Guided Fix.",
            "<tr><th>Quarter</th><th>Enrollment</th></tr>",
            "<tr><th scope=\"col\">Quarter</th><th scope=\"col\">Enrollment</th></tr>"
        ),
        new(
            "WCAG-VTT-CPS",
            "WebVTT Captions",
            "Advisory",
            "Caption Cue Exceeds Comfortable Reading Speed (> 22 Characters/Sec)",
            "WCAG 1.2.2 Captions (Prerecorded) Usability",
            "DCMP / Broadcast Caption Readability",
            false,
            "Rapid-fire dense caption cues vanish before deaf, hard-of-hearing, or ESL viewers can finish reading both the caption text and on-screen slides.",
            "Split long cues into two sequential cues or extend the cue end timestamp where audio pauses allow.",
            "00:01:10.000 --> 00:01:11.200\nThis entire thirty-eight word explanation flashes on screen in barely over one second.",
            "00:01:10.000 --> 00:01:13.500\nSplit dense explanations into balanced two-line cues."
        ),
        new(
            "WCAG-VTT-EMPTY",
            "WebVTT Captions",
            "Moderate",
            "Empty WebVTT Cue Block Without Caption Payload",
            "WCAG 1.2.2 Captions (Prerecorded) (Level A)",
            "Revised 508 E205.4",
            false,
            "Blank cue blocks can flash empty black caption boxes over video players or stall custom transcript parsers.",
            "Populate the spoken text or non-speech sound cue (e.g., [applause]) or delete the empty timestamp block.",
            "00:00:12.000 --> 00:00:15.000\n\n00:00:15.500 --> 00:00:18.000\nWelcome back.",
            "00:00:12.000 --> 00:00:15.000\n[introductory chime]\n\n00:00:15.500 --> 00:00:18.000\nWelcome back."
        ),
        new(
            "WCAG-VTT-HEADER",
            "WebVTT Captions",
            "Critical",
            "Caption File Missing Mandatory Leading WEBVTT Signature",
            "WCAG 1.2.2 Captions (Prerecorded) (Level A)",
            "W3C WebVTT Spec / Revised 508",
            false,
            "HTML5 <video> browsers reject caption tracks completely if line 1 does not start with the exact uppercase WEBVTT signature.",
            "Begin every .vtt file on line 1 with WEBVTT followed by a blank line before the first cue.",
            "1\n00:00:01.000 --> 00:00:04.000\nWelcome to Module 1.",
            "WEBVTT\n\n00:00:01.000 --> 00:00:04.000\nWelcome to Module 1."
        ),
        new(
            "WCAG-VTT-OVERLAP",
            "WebVTT Captions",
            "Moderate",
            "Overlapping Sequential WebVTT Cue Timestamps",
            "WCAG 1.2.2 Captions (Prerecorded) (Level A)",
            "Revised 508 E205.4",
            false,
            "When cue N+1 begins before cue N ends, browsers stack jumping caption boxes vertically over critical instructional video regions.",
            "Ensure each cue's start timestamp is greater than or equal to the preceding cue's end timestamp.",
            "00:00:04.000 --> 00:00:08.500\nFirst sentence.\n\n00:00:07.200 --> 00:00:10.000\nOverlapping second sentence.",
            "00:00:04.000 --> 00:00:07.200\nFirst sentence.\n\n00:00:07.200 --> 00:00:10.000\nCleanly sequenced second sentence."
        ),
        new(
            "WCAG-VTT-TIMING",
            "WebVTT Captions",
            "Critical",
            "Invalid or Reversed WebVTT Cue Timestamp (End <= Start)",
            "WCAG 1.2.2 Captions (Prerecorded) (Level A)",
            "W3C WebVTT Spec / Revised 508",
            false,
            "Cues whose end time is earlier than or equal to their start time never render on screen, silently dropping spoken dialogue.",
            "Correct the timestamp range so end time strictly exceeds start time in HH:MM:SS.mmm format.",
            "00:02:14.000 --> 00:02:11.000\nThis caption never appears.",
            "00:02:14.000 --> 00:02:17.500\nThis caption renders for 3.5 seconds."
        )
    ];

    // --------------------------------------------------------------------------------
    // CONCEPTUAL & REFERENCE HELP CHAPTERS
    // --------------------------------------------------------------------------------

    private static IReadOnlyList<HelpTopic> BuildTopics(IReadOnlyList<HelpRuleEntry> rules)
    {
        return
        [
            new HelpTopic(
                TopicId: "welcome-overview",
                Category: "1. Getting Started",
                Title: "Welcome & Architecture Overview",
                Summary: "How RaggieSoft Accessibility Studio audits, previews, refactors, and verifies WCAG 2.2 AA & Revised Section 508 compliance across local source code and SSO portals.",
                Keywords: ["welcome", "overview", "architecture", "wcag 2.2", "section 508", "odu", "ple", "html5 help", "f1"],
                HtmlContent:
                    """
                    <div class="callout">
                      <strong>Quick Tip — Context-Sensitive <kbd>F1</kbd> Help:</strong> Highlight any finding in the bottom <strong>WCAG 2.2 &amp; Section 508 Error List</strong> and press <kbd>Shift</kbd>+<kbd>F1</kbd> (or click <strong>Rule Help</strong>) to jump straight to that rule's remediation guide and code examples.
                    </div>

                    <h2>What RaggieSoft Accessibility Studio Does</h2>
                    <p>
                      <strong>RaggieSoft Accessibility Studio</strong> is a deterministic Windows workstation IDE and headless CI/CD command-line scanner engineered for web developers, university instructional technologists, and public-sector Section 508 compliance teams. Rather than treating accessibility as a post-deployment PDF checklist, the IDE brings detection, visual assistive-technology emulation, side-by-side code diff refactoring, and tamper-evident audit logging together in one window.
                    </p>

                    <div class="card-grid">
                      <div class="doc-card">
                        <h3>1. Line-Accurate Source Static Analysis</h3>
                        <p>Scans <code>.html</code>, <code>.htm</code>, <code>.astro</code>, <code>.svelte</code>, <code>.css</code>, and <code>.vtt</code> closed-caption tracks with exact 1-based line and column coordinates and inline Monaco squiggles.</p>
                      </div>
                      <div class="doc-card">
                        <h3>2. Guided Side-by-Side Diff Refactoring</h3>
                        <p>Builds safe Before/After code patches in Monaco's split Diff Editor (<kbd>Ctrl</kbd>+<kbd>D</kbd>) so engineers inspect every attribute or tag change before committing it to the buffer (<kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>F</kbd>).</p>
                      </div>
                      <div class="doc-card">
                        <h3>3. Live Chromium Emulation Inspector</h3>
                        <p>Simulates Windows High-Contrast <code>forced-colors</code>, <code>400%</code> WCAG 1.4.10 Reflow zoom, and sequential keyboard <code>[1..N]</code> Tab-Order badges on live rendered markup.</p>
                      </div>
                      <div class="doc-card">
                        <h3>4. Enterprise Reporting &amp; Headless CLI</h3>
                        <p>Exports self-audited HTML5 Executive Reports, OASIS SARIF 2.1.0, formula-safe CSV, JSON snapshots, and SHA-256 remediation logs—plus <code>a11y-studio-cli</code> for CI/CD gates.</p>
                      </div>
                    </div>

                    <h2>Heritage &amp; Engineering Lineage</h2>
                    <p>
                      Accessibility Studio grew out of real higher-education distance-learning engineering—specifically automated Personal Learning Environment (PLE) courseware scanning built to catch legacy WYSIWYG barriers (such as bolded <code>&lt;td&gt;</code> fake table headers, skipped heading levels, missing <code>lang</code> tags, and uncaptioned lecture media) across hundreds of SSO-protected university modules without overwhelming production servers.
                    </p>

                    <h2>Why Offline HTML5 Help Replaced Legacy <code>.chm</code></h2>
                    <p>
                      Unlike Windows 2000/XP Compiled HTML Help (<code>.chm</code> / <code>hh.exe</code>), which relied on the retired Internet Explorer <code>mshtml.dll</code> engine and failed under Windows Mark-of-the-Web network security policies, this manual is compiled as standards-compliant offline HTML5 rendered via an isolated local virtual host (<code>https://help.a11y.local</code>). It honors your Windows 11 Light, Dark, and High-Contrast settings automatically while working 100% offline in air-gapped environments.
                    </p>
                    """
            ),

            new HelpTopic(
                TopicId: "quickstart-workspace",
                Category: "1. Getting Started",
                Title: "Quickstart: Auditing Files, Folders & .a11yproj Workspaces",
                Summary: "Open single files, full multi-folder web repositories, or saved .a11yproj project configurations—and inspect findings in the Error List and Document Outline.",
                Keywords: ["quickstart", "open folder", "a11yproj", "workspace explorer", "sample", "outline", "compliance profile"],
                HtmlContent:
                    """
                    <h2>Step 1: Open a Source File, Folder, or Sample Module</h2>
                    <ol>
                      <li><strong>Try the Built-In Sample Workspace:</strong> On first launch (or via <strong>File &rarr; Load Built-In Sample Course Workspace</strong>), the left <strong>Workspace Explorer</strong> loads realistic HTML5, CSS, and WebVTT files containing intentional WCAG 2.2 violations so you can test auditing and refactoring safely.</li>
                      <li><strong>Open a Local Project Folder (<kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>O</kbd>):</strong> Recursively discovers <code>.html</code>, <code>.htm</code>, <code>.css</code>, <code>.vtt</code>, <code>.astro</code>, and <code>.svelte</code> files while automatically skipping <code>node_modules</code>, <code>bin</code>, <code>obj</code>, and <code>.git</code> directories.</li>
                      <li><strong>Windows Explorer Integration:</strong> Double-clicking any <code>.a11yproj</code> file or choosing <em>Audit Folder in RaggieSoft Accessibility Studio</em> in Windows Explorer launches the IDE directly into that target.</li>
                    </ol>

                    <h2>Step 2: Choose a Compliance Profile</h2>
                    <div class="table-wrap">
                      <table class="doc-table">
                        <caption>Built-In Regulatory &amp; Enterprise Compliance Profiles</caption>
                        <thead>
                          <tr>
                            <th scope="col">Profile Name</th>
                            <th scope="col">Target Standard</th>
                            <th scope="col">Recommended Use Case</th>
                          </tr>
                        </thead>
                        <tbody>
                          <tr>
                            <th scope="row"><code>Wcag22AA_Section508</code></th>
                            <td>W3C WCAG 2.2 Level A + AA &amp; Revised Section 508 (<code>E205.4</code>)</td>
                            <td>Default workstation profile for federal agencies, contractors, and enterprise web teams.</td>
                          </tr>
                          <tr>
                            <th scope="row"><code>Wcag22LevelA</code></th>
                            <td>W3C WCAG 2.2 Level A Baseline</td>
                            <td>Triage pass isolating critical blocker barriers (unlabeled inputs, missing alt, keyboard traps) first.</td>
                          </tr>
                          <tr>
                            <th scope="row"><code>AdaTitleIIWeb</code></th>
                            <td>DOJ ADA Title II Web &amp; Higher-Education Courseware</td>
                            <td>Public universities, community colleges, state agencies, and municipal digital services.</td>
                          </tr>
                          <tr>
                            <th scope="row"><code>StrictEnterpriseAll</code></th>
                            <td>WCAG 2.2 AA + Best-Practice Advisories</td>
                            <td>Design-system QA including reduced-motion keyframes, redundant alt phrasing, and caption CPS checks.</td>
                          </tr>
                        </tbody>
                      </table>
                    </div>

                    <h2 id="heading-landmark-outline">Step 3: Inspect the Error List &amp; Document Outline</h2>
                    <p>
                      Press <kbd>F5</kbd> to audit the active editor buffer or <kbd>Ctrl</kbd>+<kbd>F5</kbd> to scan every file in the open folder. Double-clicking any row in the <strong>WCAG 2.2 &amp; Section 508 Error List</strong> (or any landmark/heading node inside the <strong>Heading &amp; Landmark Outline</strong> tab) scrolls the Monaco editor directly to that line and highlights the corresponding DOM element in the Live Inspector.
                    </p>
                    """
            ),

            new HelpTopic(
                TopicId: "guided-refactoring",
                Category: "2. IDE Remediation & Emulation",
                Title: "Guided Source Refactoring & Side-by-Side Diff Preview",
                Summary: "Preview deterministic HTML5 and CSS code fixes side-by-side in Monaco before committing changes to your buffer and SHA-256 session log.",
                Keywords: ["refactor", "guided fix", "diff", "monaco", "ctrl+d", "ctrl+shift+f", "escape", "sha-256"],
                HtmlContent:
                    """
                    <h2>Why Guided Diffs Beat Blind Auto-Fixing</h2>
                    <p>
                      Automated accessibility overlays or blind regex replacements frequently invent inaccurate <code>alt</code> descriptions or break application layout. RaggieSoft Accessibility Studio uses <strong>human-in-the-loop Guided Source Refactoring</strong>: whenever a finding displays <strong>⚡ Guided Fix</strong> in the Remediation column, the IDE constructs a deterministic source patch and opens a two-pane <strong>Original vs. Remediated Diff</strong> in the Monaco editor.
                    </p>

                    <h2>Step-by-Step Refactoring Workflow</h2>
                    <ol>
                      <li>Select a finding marked <strong>⚡ Guided Fix</strong> in the <strong>WCAG 2.2 &amp; Section 508 Error List</strong>.</li>
                      <li>Review the explanation and snippet inside the <strong>Rule Reference &amp; Guided Fix Inspector</strong> bottom tab.</li>
                      <li>Press <kbd>Ctrl</kbd>+<kbd>D</kbd> (or click <strong>🔍 Preview Diff</strong>) to compare original and refactored markup side-by-side.</li>
                      <li>Press <kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>F</kbd> (or click <strong>✔ Apply Fix</strong>) to apply the patch to the active editor buffer, re-run the audit automatically, and record Before/After SHA-256 hashes in the Session Log.</li>
                      <li>To cancel a diff preview without changing your code, press <kbd>Esc</kbd> (or click <strong>↩ Exit Diff View</strong>).</li>
                    </ol>

                    <h2>Built-In Guided Refactorings</h2>
                    <ul>
                      <li><strong>Fake Bold Table Headers (<code>WCAG-TABLE-FAKE-HEADER</code>):</strong> Converts top-row <code>&lt;td style="font-weight:bold"&gt;</code> or <code>&lt;td&gt;&lt;strong&gt;</code> cells into semantic <code>&lt;th scope="col"&gt;</code> header cells.</li>
                      <li><strong>Missing Language &amp; Title Metadata (<code>WCAG-HTML-LANG</code>, <code>WCAG-PAGE-TITLE</code>):</strong> Injects <code>lang="en"</code> on <code>&lt;html&gt;</code> or scaffolds a <code>&lt;title&gt;</code> inside <code>&lt;head&gt;</code>.</li>
                      <li><strong>Skipped Heading Levels (<code>WCAG-HEADING-SKIP</code>):</strong> Normalizes skipped heading tags (such as <code>&lt;h4&gt;</code> directly beneath <code>&lt;h1&gt;</code>) to the next sequential level (<code>&lt;h2&gt;</code>).</li>
                      <li><strong>Focus &amp; ARIA Traps (<code>WCAG-ARIA-HIDDEN-FOCUS</code>, <code>WCAG-POSITIVE-TABINDEX</code>):</strong> Strips <code>aria-hidden="true"</code> from focusable buttons/links and normalizes positive <code>tabindex</code> values to <code>0</code>.</li>
                      <li><strong>CSS Contrast &amp; Focus Outlines (<code>WCAG-CSS-CONTRAST</code>, <code>WCAG-CSS-FOCUS-SUPPRESSED</code>):</strong> Computes the nearest luminance-compliant hex color (<code>&ge; 4.5:1</code>) and replaces <code>outline: none</code> with a high-contrast <code>3px solid</code> focus ring.</li>
                    </ul>
                    """
            ),

            new HelpTopic(
                TopicId: "live-dom-emulation",
                Category: "2. IDE Remediation & Emulation",
                Title: "Live Chromium DOM Inspector & Assistive Emulation",
                Summary: "Test Forced-Colors High Contrast, 400% WCAG 1.4.10 Reflow zoom, and sequential [1..N] keyboard Tab-Order overlays on rendered pages.",
                Keywords: ["live inspector", "forced-colors", "high contrast", "400% reflow", "zoom", "tab order", "dom selector"],
                HtmlContent:
                    """
                    <h2>Visualizing Assistive &amp; Low-Vision Experiences</h2>
                    <p>
                      Switch to center tab <strong>2. Live Chromium DOM &amp; Emulation Inspector</strong> to inspect how your active HTML5 or CSS stylesheet renders inside an embedded Chromium engine. Selecting any finding in the Error List scrolls the offending DOM node into view and draws a high-visibility diagnostic outline around it.
                    </p>

                    <h2>Toolbar Emulation Modes</h2>
                    <div class="table-wrap">
                      <table class="doc-table">
                        <caption>Live Inspector Emulation Toggles</caption>
                        <thead>
                          <tr>
                            <th scope="col">Toolbar Toggle</th>
                            <th scope="col">Standard Verified</th>
                            <th scope="col">What Engineers Should Check</th>
                          </tr>
                        </thead>
                        <tbody>
                          <tr>
                            <th scope="row"><strong>Forced-Colors HC</strong></th>
                            <td>Windows High Contrast / Forced Colors Mode (WCAG 1.4.1 &amp; 1.4.11)</td>
                            <td>Verifies that custom buttons, cards, badges, and focus rings remain distinguishable when author backgrounds and box-shadows are overridden by system High-Contrast tokens.</td>
                          </tr>
                          <tr>
                            <th scope="row"><strong>400% Reflow</strong></th>
                            <td>WCAG 1.4.10 Reflow (Level AA)</td>
                            <td>Applies a <code>4.0x</code> zoom factor (equivalent to a <code>320 CSS px</code> viewport width on a <code>1280 px</code> monitor) to catch clipped modals, overlapping sticky headers, or two-dimensional horizontal scrolling.</td>
                          </tr>
                          <tr>
                            <th scope="row"><strong>Tab Order [1..N]</strong></th>
                            <td>WCAG 2.4.3 Focus Order &amp; 2.1.1 Keyboard</td>
                            <td>Overlays numbered badges (<code>[1]</code>, <code>[2]</code>, <code>[3]</code>) on every keyboard-focusable element in exact sequential Tab order so visual layout mismatches stand out immediately.</td>
                          </tr>
                        </tbody>
                      </table>
                    </div>
                    """
            ),

            new HelpTopic(
                TopicId: "contrast-workbench",
                Category: "2. IDE Remediation & Emulation",
                Title: "WCAG 2.2 Color Contrast & Relative Luminance Workbench",
                Summary: "Calculate exact W3C relative luminance ratios (1:1 to 21:1) and auto-solve passing AA (4.5:1 / 3.0:1) and AAA (7.0:1) hex colors.",
                Keywords: ["contrast", "color", "luminance", "4.5:1", "3:1", "7:1", "hex", "ctrl+shift+c", "workbench"],
                HtmlContent:
                    """
                    <h2>Opening the Color Contrast Workbench</h2>
                    <p>
                      Press <kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>C</kbd> (or select <strong>Tools &rarr; WCAG 2.2 Color Contrast Workbench…</strong>) from anywhere in the IDE. You can also press <kbd>F1</kbd> or click the title-bar <strong>?</strong> Help button inside the Contrast Workbench dialog to return directly to this guide.
                    </p>

                    <h2>How W3C Relative Luminance Is Evaluated</h2>
                    <p>
                      The workbench parses 3-digit or 6-digit CSS hex codes (<code>#RGB</code>, <code>#RRGGBB</code>) and <code>rgb(r, g, b)</code> functional notation, linearizes sRGB channels per WCAG 2.2 Section 1.4.3, and computes the exact contrast ratio:
                    </p>
                    <ul>
                      <li><strong>Normal Text AA (<code>&ge; 4.50 : 1</code>):</strong> Required for body copy, table cells, and labels smaller than <code>24px</code> regular (or smaller than <code>18.66px</code> bold).</li>
                      <li><strong>Large Text &amp; Non-Text UI AA (<code>&ge; 3.00 : 1</code>):</strong> Required for headings <code>&ge; 24px</code> (or <code>&ge; 18.66px</code> bold), input borders, icons, and focus indicators under WCAG 1.4.11.</li>
                      <li><strong>Enhanced Normal Text AAA (<code>&ge; 7.00 : 1</code>):</strong> Recommended for long-form reading surfaces and executive compliance reports.</li>
                    </ul>

                    <h2>Using Auto-Tune Nearest 4.5:1 AA Hex</h2>
                    <p>
                      Click <strong>⚡ Auto-Tune Nearest 4.5:1 AA Hex</strong> to preserve your brand hue while darkening (against light backgrounds) or lightening (against dark backgrounds) the foreground color just enough to cross the <code>4.55:1</code> safety threshold.
                    </p>
                    """
            ),

            new HelpTopic(
                TopicId: "sso-batch-queue",
                Category: "3. Batch, CLI & Enterprise Reports",
                Title: "Authenticated SSO Browser & Polite Rate-Limited Batch Queue",
                Summary: "Sign into university or enterprise SSO portals once, load a CSV schedule or sitemap.xml, and audit pages at a polite inter-request throttle.",
                Keywords: ["sso", "saml", "oauth", "batch queue", "throttle", "rate limit", "sitemap.xml", "csv", "authenticated"],
                HtmlContent:
                    """
                    <h2>Auditing Behind Enterprise Single Sign-On (SSO)</h2>
                    <p>
                      Headless cloud crawlers often fail on university Learning Management Systems (LMS), student portals, and internal government intranets gated by SAML 2.0, OIDC, hardware security keys, or multi-factor prompts. Center tab <strong>3. Authenticated SSO Browser &amp; Rate-Limited Batch Queue</strong> solves this cleanly on your local workstation:
                    </p>
                    <ol>
                      <li>Enter your staging or portal login URL in <strong>Portal / SSO URL</strong> and click <strong>Navigate SSO Browser</strong>.</li>
                      <li>Complete your normal institutional login (including MFA) directly inside the interactive browser pane. Session cookies stay isolated in your local Windows profile (<code>%LOCALAPPDATA%\RaggieSoft\AccessibilityStudio\WebView2Data</code>) and are never transmitted externally.</li>
                      <li>Click <strong>Audit Live SSO DOM Now</strong> to capture the post-JavaScript rendered DOM of the current page and analyze it immediately, or load a batch schedule below.</li>
                    </ol>

                    <h2>Polite Rate-Limited Batch Scanning (<code>.csv</code> &amp; <code>sitemap.xml</code>)</h2>
                    <p>
                      Click <strong>Load .CSV / sitemap.xml…</strong> to import either a standard XML sitemap (<code>&lt;loc&gt;...&lt;/loc&gt;</code>) or a newline/CSV list of URLs and file paths. Set <strong>Throttle (ms)</strong> (default <code>1,500 ms</code>, configurable from <code>0 ms</code> for local disk files up to <code>30,000 ms</code>) so batch audits never trip institutional WAFs or rate limiters, then click <strong>▶ Start Polite Batch Queue</strong>.
                    </p>
                    """
            ),

            new HelpTopic(
                TopicId: "reports-and-logs",
                Category: "3. Batch, CLI & Enterprise Reports",
                Title: "Enterprise Reports (HTML5, SARIF 2.1.0, CSV, JSON) & SHA-256 Logs",
                Summary: "Generate executive and developer deliverables—including OASIS SARIF 2.1.0 for GitHub/Azure DevOps and tamper-evident SHA-256 remediation logs.",
                Keywords: ["reports", "sarif", "html5 report", "csv", "json", "jsonl", "sha-256", "session log", "vpat"],
                HtmlContent:
                    """
                    <h2>Four Export Formats Built for Engineering &amp; Governance</h2>
                    <div class="table-wrap">
                      <table class="doc-table">
                        <caption>Report Formats Available Under the Reports Menu &amp; CLI</caption>
                        <thead>
                          <tr>
                            <th scope="col">Export Format</th>
                            <th scope="col">Menu / CLI Flag</th>
                            <th scope="col">Audience &amp; Technical Guarantees</th>
                          </tr>
                        </thead>
                        <tbody>
                          <tr>
                            <th scope="row"><strong>Accessible HTML5 Report</strong></th>
                            <td><code>--html report.html</code></td>
                            <td>Self-contained executive summary + line-numbered engineering remediation ledger that passes 100% of our own WCAG 2.2 AA rules (light/dark/print ready).</td>
                          </tr>
                          <tr>
                            <th scope="row"><strong>OASIS SARIF 2.1.0</strong></th>
                            <td><code>--sarif audit.sarif</code></td>
                            <td>Uploads directly to <strong>GitHub Code Scanning</strong>, <strong>Azure DevOps Advanced Security</strong>, and Visual Studio SARIF Viewer with exact <code>physicalLocation</code> line/column regions.</td>
                          </tr>
                          <tr>
                            <th scope="row"><strong>Formula-Safe CSV</strong></th>
                            <td><code>--csv findings.csv</code></td>
                            <td>Ready for Excel, Power BI, and Jira bulk import; neutralizes spreadsheet formula-injection prefixes (<code>=</code>, <code>+</code>, <code>-</code>, <code>@</code>) automatically.</td>
                          </tr>
                          <tr>
                            <th scope="row"><strong>JSON Snapshot</strong></th>
                            <td><code>--json snapshot.json</code></td>
                            <td>Complete structured serialization including per-file SHA-256 digests, compliance scores, and document outline trees for compliance dashboards.</td>
                          </tr>
                        </tbody>
                      </table>
                    </div>

                    <h2 id="session-audit-logs">Tamper-Evident SHA-256 Remediation Session Logs</h2>
                    <p>
                      Every buffer scan, folder audit, Guided Fix commit, file save, and report export appends a UTC-timestamped entry to the <strong>Audit &amp; Remediation Session Log</strong> bottom tab, recording the exact 64-character hexadecimal <strong>SHA-256 digest before and after remediation</strong>. Export structured JSON Lines (<code>.jsonl</code>) for SIEM ingestion or a human-readable transcript (<code>.log</code>) for Section 508 procurement evidence packages.
                    </p>
                    """
            ),

            new HelpTopic(
                TopicId: "cli-cicd-reference",
                Category: "3. Batch, CLI & Enterprise Reports",
                Title: "Headless CLI Scanner (a11y-studio-cli) & CI/CD Gates",
                Summary: "Run deterministic WCAG 2.2 & Section 508 build gates in GitHub Actions, Azure Pipelines, GitLab CI, or scheduled PowerShell tasks.",
                Keywords: ["cli", "a11y-studio-cli", "ci/cd", "github actions", "azure devops", "exit code", "sarif", "export-help"],
                HtmlContent:
                    """
                    <h2>Command-Line Syntax</h2>
                    <pre><code>a11y-studio-cli scan &lt;file-or-directory-or-schedule&gt; [options]
                    a11y-studio-cli export-help &lt;output-directory&gt;</code></pre>

                    <h2>Supported Scan Options</h2>
                    <div class="table-wrap">
                      <table class="doc-table">
                        <caption>Command-Line Options for <code>a11y-studio-cli scan</code></caption>
                        <thead>
                          <tr>
                            <th scope="col">Flag</th>
                            <th scope="col">Values / Default</th>
                            <th scope="col">Description</th>
                          </tr>
                        </thead>
                        <tbody>
                          <tr>
                            <th scope="row"><code>--profile</code></th>
                            <td><code>Wcag22AA_Section508</code> (default), <code>Wcag22LevelA</code>, <code>AdaTitleIIWeb</code>, <code>StrictEnterpriseAll</code></td>
                            <td>Selects the regulatory rule profile applied during static analysis.</td>
                          </tr>
                          <tr>
                            <th scope="row"><code>--fail-on</code></th>
                            <td><code>Serious</code> (default), <code>Critical</code>, <code>Moderate</code>, <code>Advisory</code></td>
                            <td>Minimum finding severity that causes the process to return exit code <code>2</code>.</td>
                          </tr>
                          <tr>
                            <th scope="row"><code>--throttle-ms</code></th>
                            <td><code>1200</code> (default)</td>
                            <td>Polite delay in milliseconds between items when scanning a <code>.csv</code> or <code>sitemap.xml</code> URL schedule.</td>
                          </tr>
                          <tr>
                            <th scope="row"><code>--html</code> / <code>--sarif</code> / <code>--csv</code> / <code>--json</code></th>
                            <td>Output file path</td>
                            <td>Writes one or more report artifacts in a single pass.</td>
                          </tr>
                        </tbody>
                      </table>
                    </div>

                    <h2>Deterministic Exit Code Contract</h2>
                    <ul>
                      <li><code>0</code> — <strong>Pass:</strong> Zero findings at or above the <code>--fail-on</code> threshold.</li>
                      <li><code>1</code> — <strong>Usage / I/O Error:</strong> Invalid CLI arguments or target path does not exist.</li>
                      <li><code>2</code> — <strong>Compliance Gate Failed:</strong> One or more blocking WCAG 2.2 / Section 508 violations detected.</li>
                    </ul>

                    <h2>GitHub Actions SARIF Gate Example</h2>
                    <pre><code>- name: Run WCAG 2.2 AA &amp; Section 508 Static Gate
                      run: &gt;
                        a11y-studio-cli scan ./dist
                        --profile Wcag22AA_Section508
                        --fail-on Serious
                        --sarif a11y-results.sarif
                        --html a11y-report.html</code></pre>
                    """
            ),

            new HelpTopic(
                TopicId: "enterprise-msi-gpo",
                Category: "4. IT Deployment & Accessibility",
                Title: "Enterprise MSI Deployment & Group Policy Registry Reference",
                Summary: "Deploy RaggieSoft Accessibility Studio silently across Windows 11 Pro/Enterprise fleets via msiexec and configure HKLM Group Policy defaults.",
                Keywords: ["msi", "msiexec", "installaware", "group policy", "gpo", "registry", "hklm", "silent install", "sccm", "intune"],
                HtmlContent:
                    """
                    <h2>Silent Enterprise MSI Deployment (Intune / ConfigMgr / GPO)</h2>
                    <p>
                      IT administrators can deploy the Windows Installer package unattended across managed Windows 11 Pro and Enterprise workstations while pre-configuring organization-wide compliance defaults:
                    </p>
                    <pre><code>msiexec /i RaggieSoftAccessibilityStudio-x64.msi /qn ^
                      ADD_CLI_TO_PATH=1 ^
                      COMPLIANCE_PROFILE="Wcag22AA_Section508" ^
                      BATCH_THROTTLE_MS="1500"</code></pre>

                    <h2>Windows Registry &amp; Group Policy Precedence</h2>
                    <p>
                      On startup, <code>MainIdeForm</code> evaluates <code>HKEY_LOCAL_MACHINE\Software\Policies\RaggieSoft\AccessibilityStudio</code> first (enforced IT Group Policy), falling back to <code>HKEY_CURRENT_USER\Software\RaggieSoft\AccessibilityStudio</code> when no machine policy is present.
                    </p>

                    <div class="table-wrap">
                      <table class="doc-table">
                        <caption>Supported Enterprise Registry Values</caption>
                        <thead>
                          <tr>
                            <th scope="col">Registry Value Name</th>
                            <th scope="col">Type</th>
                            <th scope="col">Accepted Values &amp; Behavior</th>
                          </tr>
                        </thead>
                        <tbody>
                          <tr>
                            <th scope="row"><code>ComplianceProfile</code></th>
                            <td><code>REG_SZ</code></td>
                            <td><code>Wcag22AA_Section508</code>, <code>Wcag22LevelA</code>, <code>AdaTitleIIWeb</code>, or <code>StrictEnterpriseAll</code>. Sets initial toolbar profile.</td>
                          </tr>
                          <tr>
                            <th scope="row"><code>BatchThrottleMs</code></th>
                            <td><code>REG_DWORD</code></td>
                            <td><code>0</code> to <code>30000</code> (milliseconds). Sets default polite rate-limiting delay on the SSO Batch Queue tab.</td>
                          </tr>
                        </tbody>
                      </table>
                    </div>
                    """
            ),

            new HelpTopic(
                TopicId: "keyboard-shortcuts",
                Category: "4. IT Deployment & Accessibility",
                Title: "Keyboard Shortcuts & Workstation Accessibility Guide",
                Summary: "Complete keyboard accelerator map, screen-reader landmarks, Dark/Light SystemColorMode sync, and Per-Monitor V2 High-DPI support.",
                Keywords: ["keyboard", "shortcuts", "hotkeys", "f1", "shift+f1", "f5", "f8", "dark mode", "screen reader", "nvda", "jaws"],
                HtmlContent:
                    """
                    <h2>IDE Keyboard Shortcuts</h2>
                    <div class="table-wrap">
                      <table class="doc-table">
                        <caption>Primary Keyboard Accelerators in RaggieSoft Accessibility Studio</caption>
                        <thead>
                          <tr>
                            <th scope="col">Shortcut</th>
                            <th scope="col">Command</th>
                            <th scope="col">Scope</th>
                          </tr>
                        </thead>
                        <tbody>
                          <tr>
                            <th scope="row"><kbd>F1</kbd></th>
                            <td>Open Context-Sensitive Help Manual</td>
                            <td>Global IDE &amp; Dialogs (opens active tab or rule topic)</td>
                          </tr>
                          <tr>
                            <th scope="row"><kbd>Shift</kbd>+<kbd>F1</kbd></th>
                            <td>Open Rule Reference for Selected Error List Finding</td>
                            <td>Jumps straight to <code>#rule-WCAG-...</code></td>
                          </tr>
                          <tr>
                            <th scope="row"><kbd>F5</kbd></th>
                            <td>Run Source Audit on Active Editor Buffer</td>
                            <td>Refreshes Error List, Outline, Squiggles &amp; Live Inspector</td>
                          </tr>
                          <tr>
                            <th scope="row"><kbd>Ctrl</kbd>+<kbd>F5</kbd></th>
                            <td>Audit Entire Open Folder Workspace</td>
                            <td>Scans all discovered HTML, CSS, and WebVTT files</td>
                          </tr>
                          <tr>
                            <th scope="row"><kbd>F8</kbd> / <kbd>Enter</kbd></th>
                            <td>Jump to Selected Finding Line &amp; Column</td>
                            <td>Error List Grid &amp; Edit Menu</td>
                          </tr>
                          <tr>
                            <th scope="row"><kbd>Ctrl</kbd>+<kbd>D</kbd></th>
                            <td>Preview Side-by-Side Guided Fix Diff</td>
                            <td>Opens Monaco Diff Editor for selected finding</td>
                          </tr>
                          <tr>
                            <th scope="row"><kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>F</kbd></th>
                            <td>Apply Guided Fix to Editor Buffer</td>
                            <td>Commits patch, logs SHA-256 transition, and re-audits</td>
                          </tr>
                          <tr>
                            <th scope="row"><kbd>Esc</kbd></th>
                            <td>Exit Side-by-Side Diff View</td>
                            <td>Returns to standard Monaco code editor without modifying code</td>
                          </tr>
                          <tr>
                            <th scope="row"><kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>C</kbd></th>
                            <td>Open WCAG 2.2 Color Contrast Workbench</td>
                            <td>Tools Menu</td>
                          </tr>
                          <tr>
                            <th scope="row"><kbd>Ctrl</kbd>+<kbd>O</kbd> / <kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>O</kbd></th>
                            <td>Open Single File / Open Folder Workspace</td>
                            <td>File Menu</td>
                          </tr>
                          <tr>
                            <th scope="row"><kbd>Ctrl</kbd>+<kbd>S</kbd></th>
                            <td>Save Active Source File</td>
                            <td>Writes buffer and records Before/After SHA-256 digest</td>
                          </tr>
                        </tbody>
                      </table>
                    </div>

                    <h2>Native Windows Accessibility &amp; Theme Synchronization</h2>
                    <p>
                      Built on .NET 10 Windows Forms with <code>Application.SetColorMode(SystemColorMode.System)</code> and <code>PerMonitorV2</code> High-DPI scaling, every native menu, tree view, data grid, and embedded Chromium surface (Monaco Editor, Live DOM Inspector, SSO Browser, and this Help Viewer) synchronizes automatically when you switch Windows 11 between Light Mode, Dark Mode, or Contrast Themes—or can be overridden anytime under <strong>Tools &rarr; Editor Theme</strong>.
                    </p>
                    """
            ),

            new HelpTopic(
                TopicId: "rule-reference",
                Category: "5. WCAG 2.2 & Section 508 Rule Reference",
                Title: "Complete Built-In Rule Catalog (25 HTML5, WAI-ARIA, CSS & WebVTT Rules)",
                Summary: "Detailed engineering remediation reference, assistive-technology user impact, and Before/After code examples for all 25 static-analysis rules.",
                Keywords: ["rules", "catalog", "reference", "wcag", "section 508", "html5", "aria", "css", "webvtt"],
                HtmlContent: BuildRuleReferenceHtmlSection(rules)
            )
        ];
    }

    private static string BuildRuleReferenceHtmlSection(IReadOnlyList<HelpRuleEntry> rules)
    {
        StringBuilder sb = new();

        sb.AppendLine("<h2>Summary Matrix</h2>");
        sb.AppendLine("<div class=\"table-wrap\">");
        sb.AppendLine("  <table class=\"doc-table\">");
        sb.AppendLine("    <caption>Quick Jump Index of All 25 Built-In Accessibility Rules</caption>");
        sb.AppendLine("    <thead>");
        sb.AppendLine("      <tr>");
        sb.AppendLine("        <th scope=\"col\">Rule ID</th>");
        sb.AppendLine("        <th scope=\"col\">Engine</th>");
        sb.AppendLine("        <th scope=\"col\">Severity</th>");
        sb.AppendLine("        <th scope=\"col\">WCAG 2.2 Criterion</th>");
        sb.AppendLine("        <th scope=\"col\">Guided Fix</th>");
        sb.AppendLine("      </tr>");
        sb.AppendLine("    </thead>");
        sb.AppendLine("    <tbody>");

        foreach (HelpRuleEntry r in rules)
        {
            string fixLabel = r.HasAutomatedGuidedFix ? "⚡ Assisted Diff (Ctrl+D)" : "Manual Review";
            sb.AppendLine("      <tr>");
            sb.AppendLine($"        <th scope=\"row\"><a href=\"#rule-{Enc(r.RuleId)}\"><code>{Enc(r.RuleId)}</code></a></th>");
            sb.AppendLine($"        <td>{Enc(r.EngineCategory)}</td>");
            sb.AppendLine($"        <td>{Enc(r.Severity)}</td>");
            sb.AppendLine($"        <td>{Enc(r.WcagCriterion)}</td>");
            sb.AppendLine($"        <td>{Enc(fixLabel)}</td>");
            sb.AppendLine("      </tr>");
        }

        sb.AppendLine("    </tbody>");
        sb.AppendLine("  </table>");
        sb.AppendLine("</div>");

        sb.AppendLine("<h2>Detailed Rule Specifications &amp; Remediation Examples</h2>");

        foreach (HelpRuleEntry r in rules)
        {
            string sevClass = r.Severity switch
            {
                "Critical" => "badge badge-critical",
                "Serious" => "badge badge-serious",
                _ => "badge badge-moderate"
            };
            string fixBadge = r.HasAutomatedGuidedFix
                ? "<span class=\"badge\">⚡ Automated Guided Fix Available (Ctrl+D)</span>"
                : "<span class=\"badge\">Engineering Manual Review</span>";

            sb.AppendLine($"<article id=\"rule-{Enc(r.RuleId)}\" class=\"rule-card\" tabindex=\"-1\">");
            sb.AppendLine($"  <h3><code>{Enc(r.RuleId)}</code> — {Enc(r.Title)}</h3>");
            sb.AppendLine("  <div class=\"rule-meta\">");
            sb.AppendLine($"    <span class=\"{sevClass}\">Severity: {Enc(r.Severity)}</span>");
            sb.AppendLine($"    <span class=\"badge\">{Enc(r.WcagCriterion)}</span>");
            sb.AppendLine($"    <span class=\"badge\">{Enc(r.Section508Reference)}</span>");
            sb.AppendLine($"    {fixBadge}");
            sb.AppendLine("  </div>");
            sb.AppendLine($"  <p><strong>Impact on Assistive Technology Users:</strong> {Enc(r.AssistiveTechImpact)}</p>");
            sb.AppendLine($"  <p><strong>Remediation Guidance:</strong> {Enc(r.RemediationSummary)}</p>");
            sb.AppendLine("  <p><strong>Failing Source Example:</strong></p>");
            sb.AppendLine($"  <pre><code>{Enc(r.FailingCodeExample)}</code></pre>");
            sb.AppendLine("  <p><strong>Passing Remediated Example:</strong></p>");
            sb.AppendLine($"  <pre><code>{Enc(r.PassingCodeExample)}</code></pre>");
            sb.AppendLine("</article>");
        }

        return sb.ToString();
    }
}
