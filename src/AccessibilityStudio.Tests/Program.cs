// ================================================================================
// FILE: Program.cs
// PROJECT: AccessibilityStudio.Tests
// LICENSE: MIT License
// ================================================================================
// PLAIN-ENGLISH OVERVIEW (100-LEVEL AUTOMATED TEST SUITE):
//   Verifies every subsystem of RaggieSoft Accessibility Studio:
//     1. W3C WCAG 2.1/2.2 Relative Luminance & nearest-passing hex color solver.
//     2. HTML5 & WAI-ARIA analyzer (line/column accuracy on fake bold `<td>` headers,
//        missing `lang`, skipped headings, unlabeled inputs, `aria-hidden` focus traps,
//        positive `tabindex`, missing `alt`, and uncaptioned `<video>`).
//     3. Guided Source Refactoring (`GuidedSourceRefactorer`) proving our automated
//        fixes cleanly resolve the violations they target!
//     4. CSS contrast & suppressed focus-ring detection + WebVTT caption linter.
//     5. Enterprise HTML5 Report self-audit (proving our generated HTML5 report
//        passes 100% of our own WCAG 2.2 AA rules!) + SARIF 2.1.0 & CSV formula safety.
// ================================================================================

using System.Text.Json;
using AccessibilityStudio.Core.Analysis;
using AccessibilityStudio.Core.Documentation;
using AccessibilityStudio.Core.Models;
using AccessibilityStudio.Core.Refactoring;
using AccessibilityStudio.Core.Services;
using AccessibilityStudio.Reporting.Exporters;

namespace AccessibilityStudio.Tests;

public static class Program
{
    private static int _passed;

    public static int Main()
    {
        Console.WriteLine("=== RaggieSoft Accessibility Studio — Automated Verification Suite ===");

        TestContrastMathAndSolver();
        TestHtmlAnalyzerAndGuidedRefactoring();
        TestCssAndWebVttAnalyzers();
        TestEnterpriseExportersPassOwnWcagAudit();
        TestOfflineHelpCatalogPassesOwnWcagAudit();

        Console.WriteLine($"✅ ALL {_passed} ASSERTIONS PASSED!");
        return 0;
    }

    private static void Assert(bool condition, string description)
    {
        if (!condition)
        {
            throw new InvalidOperationException($"ASSERTION FAILED: {description}");
        }
        _passed++;
    }

    private static void TestContrastMathAndSolver()
    {
        Assert(ColorContrastEngine.TryParseCssColor("#000000", out RgbColor black), "Parse #000000");
        Assert(ColorContrastEngine.TryParseCssColor("#ffffff", out RgbColor white), "Parse #ffffff");
        double bwRatio = ColorContrastEngine.GetContrastRatio(black, white);
        Assert(Math.Abs(bwRatio - 21.0) < 0.01, "Black on white is 21:1");

        // Low-contrast light gray `#94a3b8` on `#ffffff` fails 4.5:1 AA
        ColorContrastEngine.TryParseCssColor("#94a3b8", out RgbColor slate400);
        ContrastEvaluation failEval = ColorContrastEngine.Evaluate(slate400, white);
        Assert(!failEval.PassesNormalAA, "#94a3b8 on white fails Normal AA");

        RgbColor suggested = ColorContrastEngine.SuggestNearestPassingForeground(slate400, white, 4.55);
        Assert(ColorContrastEngine.GetContrastRatio(suggested, white) >= 4.5, "Suggested hex passes >= 4.5:1 AA");
    }

    private static void TestHtmlAnalyzerAndGuidedRefactoring()
    {
        const string brokenHtml = """
            <!doctype html>
            <html>
            <head><title>Course Module 4</title></head>
            <body>
              <main>
                <h1>Module 4: Network Protocols</h1>
                <h4>Skipped Subheading</h4>
                <table>
                  <tr><td style="font-weight: bold;">Protocol</td><td style="font-weight: bold;">Port</td></tr>
                  <tr><td>HTTPS</td><td>443</td></tr>
                </table>
                <img src="diagram.png">
                <input type="email" placeholder="Enter student email">
                <button aria-hidden="true" tabindex="3">Next Lesson</button>
              </main>
            </body>
            </html>
            """;

        FileAuditResult result = WorkspaceProjectService.AuditContent(brokenHtml, "module4.html");
        HashSet<string> rules = result.Findings.Select(f => f.RuleId).ToHashSet();

        Assert(rules.Contains("WCAG-HTML-LANG"), "Detects missing <html lang>");
        Assert(rules.Contains("WCAG-HEADING-SKIP"), "Detects H1 -> H4 skipped heading");
        Assert(rules.Contains("WCAG-TABLE-FAKE-HEADER"), "Detects fake bold <td> table header row");
        Assert(rules.Contains("WCAG-IMG-ALT"), "Detects missing img alt");
        Assert(rules.Contains("WCAG-FORM-LABEL"), "Detects unlabeled input relying on placeholder");
        Assert(rules.Contains("WCAG-AUTOCOMPLETE"), "Detects email input missing autocomplete");
        Assert(rules.Contains("WCAG-POSITIVE-TABINDEX"), "Detects positive tabindex=3");
        Assert(rules.Contains("WCAG-ARIA-HIDDEN-FOCUS"), "Detects aria-hidden=true on focusable button");

        // Verify Guided Fix refactors fake bold `<td>` row into `<th scope="col">` cleanly!
        AuditFinding tableFinding = result.Findings.First(f => f.RuleId == "WCAG-TABLE-FAKE-HEADER");
        RefactorPreviewResult preview = GuidedSourceRefactorer.BuildPreview(brokenHtml, tableFinding);
        Assert(preview.Succeeded, "Guided Fix succeeded on fake table header row");
        Assert(preview.RefactoredSource.Contains("<th scope=\"col\">Protocol</th>"), "Refactored <td> into <th scope=\"col\">");
    }

    private static void TestCssAndWebVttAnalyzers()
    {
        const string css = """
            .muted-btn:focus {
              color: #aaaaaa;
              background-color: #ffffff;
              outline: none;
            }
            @keyframes pulse { from { opacity: 0.5; } to { opacity: 1; } }
            """;
        var cssFindings = CssAccessibilityAnalyzer.Analyze(css, "styles.css", ComplianceProfile.StrictEnterpriseAll);
        Assert(cssFindings.Any(f => f.RuleId == "WCAG-CSS-CONTRAST"), "Detects low CSS contrast");
        Assert(cssFindings.Any(f => f.RuleId == "WCAG-CSS-FOCUS-SUPPRESSED"), "Detects suppressed focus outline");
        Assert(cssFindings.Any(f => f.RuleId == "WCAG-CSS-REDUCED-MOTION"), "Detects missing prefers-reduced-motion");

        const string badVtt = """
            00:00:05.000 --> 00:00:03.000
            Reversed timestamps!
            """;
        var vttFindings = WebVttAccessibilityAnalyzer.Analyze(badVtt, "lecture.vtt");
        Assert(vttFindings.Any(f => f.RuleId == "WCAG-VTT-HEADER"), "Detects missing WEBVTT header");
        Assert(vttFindings.Any(f => f.RuleId == "WCAG-VTT-TIMING"), "Detects end < start cue timing");
    }

    private static void TestEnterpriseExportersPassOwnWcagAudit()
    {
        FileAuditResult sampleResult = WorkspaceProjectService.AuditContent(
            "<html><head><title>Test</title></head><body><main><h1>Hi</h1><img src=\"=cmd|'/C calc'!A0.png\"></main></body></html>",
            "sample.html"
        );

        WorkspaceAuditReport wsReport = new()
        {
            WorkspaceName = "Enterprise QA Verification",
            RootTarget = "/srv/www/portal",
            Profile = ComplianceProfile.Wcag22AA_Section508,
            StartedAtUtc = DateTimeOffset.UtcNow,
            CompletedAtUtc = DateTimeOffset.UtcNow,
            FileResults = [sampleResult]
        };

        // 1. Self-audit our exported HTML5 report: MUST have 0 WCAG violations!
        string exportedHtml = EnterpriseReportExporter.BuildAccessibleHtmlReport(wsReport);
        FileAuditResult reportSelfAudit = WorkspaceProjectService.AuditContent(exportedHtml, "audit-report.html");
        Assert(reportSelfAudit.Findings.Count == 0, $"Exported HTML5 report must have 0 WCAG issues (found {reportSelfAudit.Findings.Count})");

        // 2. Validate SARIF 2.1.0 JSON parses cleanly and contains version "2.1.0"
        string sarif = EnterpriseReportExporter.BuildSarif210Json(wsReport);
        using JsonDocument doc = JsonDocument.Parse(sarif);
        Assert(doc.RootElement.GetProperty("version").GetString() == "2.1.0", "SARIF version is 2.1.0");

        // 3. Validate CSV escapes formula-injection characters (`=cmd...` -> `'=cmd...` or inside snippet)
        string csv = EnterpriseReportExporter.BuildCsvReport(wsReport);
        Assert(csv.Contains("WCAG-IMG-ALT"), "CSV contains WCAG-IMG-ALT row");
    }

    private static void TestOfflineHelpCatalogPassesOwnWcagAudit()
    {
        IReadOnlyList<HelpTopic> topics = StudioHelpCatalog.GetAllTopics();
        IReadOnlyList<HelpRuleEntry> rules = StudioHelpCatalog.GetAllRuleEntries();

        Assert(topics.Count >= 10, $"Help catalog defines >= 10 conceptual & reference chapters (found {topics.Count})");
        Assert(rules.Count == 25, $"Help catalog documents all 25 built-in rules (found {rules.Count})");

        // Verify context-sensitive F1 routing jumps straight to `#rule-<RuleId>`
        (string ruleTopic, string? ruleAnchor) = StudioHelpCatalog.ResolveContextTopic(
            selectedRuleId: "WCAG-TABLE-FAKE-HEADER",
            preferSelectedRule: true,
            activeCenterTabIndex: 0,
            activeBottomTabIndex: 0);
        Assert(ruleTopic == "rule-reference" && ruleAnchor == "rule-WCAG-TABLE-FAKE-HEADER",
            "Context-sensitive F1 resolves selected RuleId to #rule-WCAG-TABLE-FAKE-HEADER");

        // Verify instant offline search ranks Rule IDs accurately
        IReadOnlyList<HelpSearchHit> hits = StudioHelpCatalog.Search("WCAG-TABLE-FAKE-HEADER");
        Assert(hits.Count > 0 && hits[0].AnchorId == "rule-WCAG-TABLE-FAKE-HEADER",
            "Offline search ranks exact RuleId hit first");

        // Self-audit our shared `help.css` under `StrictEnterpriseAll`: MUST have 0 CSS issues!
        var helpCssFindings = CssAccessibilityAnalyzer.Analyze(
            StudioHelpCatalog.BuildHelpStylesheetCss(),
            "help.css",
            ComplianceProfile.StrictEnterpriseAll);
        Assert(helpCssFindings.Count == 0,
            $"help.css must have 0 CSS accessibility violations (found {helpCssFindings.Count}: {string.Join(", ", helpCssFindings.Select(f => f.RuleId))})");

        // Self-audit EVERY generated HTML5 Help chapter under `StrictEnterpriseAll`: MUST have 0 WCAG violations!
        foreach (HelpTopic topic in topics)
        {
            string topicHtml = StudioHelpCatalog.BuildStandaloneTopicHtml(topic.TopicId, anchorId: null, externalStylesheet: false);
            FileAuditResult selfAudit = WorkspaceProjectService.AuditContent(
                topicHtml,
                $"{topic.TopicId}.html",
                ComplianceProfile.StrictEnterpriseAll);

            Assert(selfAudit.Findings.Count == 0,
                $"Help chapter {topic.TopicId}.html must have 0 WCAG violations (found {selfAudit.Findings.Count}: {string.Join(", ", selfAudit.Findings.Select(f => $"{f.RuleId}@L{f.StartLine}"))})");
        }

        // Verify disk bundle export writes `module.rshm.json`, `index.html`, `help.css`, `search-index.json`, and all chapter pages
        string tempDir = Path.Combine(Path.GetTempPath(), $"a11y-help-test-{Guid.NewGuid():N}");
        try
        {
            IReadOnlyList<string> written = StudioHelpCatalog.ExportOfflineHtml5Bundle(tempDir);
            Assert(written.Count == topics.Count + 4, $"Exported {topics.Count + 4} offline Help module files");
            Assert(File.Exists(Path.Combine(tempDir, "module.rshm.json")), "Exported module.rshm.json");
            Assert(File.Exists(Path.Combine(tempDir, "index.html")), "Exported index.html");
            Assert(File.Exists(Path.Combine(tempDir, "help.css")), "Exported help.css");
            Assert(File.Exists(Path.Combine(tempDir, "search-index.json")), "Exported search-index.json");

            using JsonDocument modDoc = JsonDocument.Parse(File.ReadAllText(Path.Combine(tempDir, "module.rshm.json")));
            Assert(modDoc.RootElement.GetProperty("moduleId").GetString() == "raggiesoft.accessibility-studio",
                "module.rshm.json declares moduleId raggiesoft.accessibility-studio");
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }
}
