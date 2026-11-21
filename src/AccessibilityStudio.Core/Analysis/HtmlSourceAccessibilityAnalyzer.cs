// ================================================================================
// FILE: Analysis/HtmlSourceAccessibilityAnalyzer.cs
// PROJECT: AccessibilityStudio.Core
// LICENSE: MIT License
// ================================================================================
// PLAIN-ENGLISH OVERVIEW (100-LEVEL HTML5 & WAI-ARIA SOURCE SCANNER GUIDE):
//   Why analyze raw HTML source text in addition to a browser DOM?
//     Browser-only plugins inspect the *mutated* DOM in memory, which means they
//     cannot tell you the exact **Line and Column** inside your `.html`, `.astro`,
//     or `.svelte` source file where `<td style="font-weight: bold;">` lives!
//
//   How Line & Column Tracking Works Here:
//     1. `BuildLineStartOffsets(source)` scans the string once in O(N) time and
//        records the character index of every newline (`\n`).
//     2. `GetLineAndColumn(index)` uses `Array.BinarySearch` in O(log L) time to
//        convert any regex or lexical character offset into exact 1-based
//        `(Line, Column)` coordinates for the Monaco Code Editor!
//
//   Rules Checked (WCAG 2.1/2.2 Level A & AA + Revised Section 508 Web):
//     - `WCAG-HTML-LANG`         (3.1.1 Level A)  : Missing/empty `<html lang="...">`
//     - `WCAG-PAGE-TITLE`        (2.4.2 Level A)  : Missing/empty `<title>` in full documents
//     - `WCAG-MAIN-LANDMARK`     (1.3.1 / 2.4.1)  : Full `<body>` missing `<main>` or multiple visible `<main>`s
//     - `WCAG-MULTI-NAV-LABEL`   (1.3.1 / 2.4.6)  : Multiple `<nav>` elements without distinguishable `aria-label`
//     - `WCAG-HEADING-SKIP`      (1.3.1 / 2.4.6)  : Skipped heading level (e.g. `<h1>` -> `<h4>`)
//     - `WCAG-TABLE-FAKE-HEADER` (1.3.1 Level A)  : First-row `<td style="font-weight:bold">` / `<td><strong>` instead of `<th scope="col">`
//     - `WCAG-TH-SCOPE`          (1.3.1 Level A)  : `<th>` missing explicit `scope="col"` or `scope="row"`
//     - `WCAG-IMG-ALT`           (1.1.1 Level A)  : `<img>` missing `alt` attribute
//     - `WCAG-IMG-REDUNDANT-ALT` (1.1.1 Advisory) : `alt="image of ..."` / `alt="picture of ..."`
//     - `WCAG-SVG-ACCESSIBLE`    (1.1.1 / 4.1.2)  : Inline `<svg>` missing both `aria-hidden="true"` and `role="img"` + label
//     - `WCAG-FORM-LABEL`        (3.3.2 / 4.1.2)  : `<input>`/`<select>`/`<textarea>` without `<label for>`, `aria-label`, or `aria-labelledby`
//     - `WCAG-AUTOCOMPLETE`      (1.3.5 Level AA) : Email/phone/name inputs missing `autocomplete`
//     - `WCAG-IFRAME-TITLE`      (4.1.2 Level A)  : `<iframe>` missing descriptive `title` or using `scrolling="no"`
//     - `WCAG-POSITIVE-TABINDEX` (2.4.3 Level A)  : Positive `tabindex="1"` disrupting sequential focus order
//     - `WCAG-ARIA-HIDDEN-FOCUS` (4.1.2 Level A)  : `aria-hidden="true"` placed directly on focusable `<button>`, `<a href>`, or `<input>`
//     - `WCAG-AMBIGUOUS-LINK`    (2.4.4 Level A)  : Ambiguous `"click here"` / `"read more"` link without `aria-label`
//     - `WCAG-MEDIA-CAPTIONS`    (1.2.2 Level A)  : `<video>` missing `<track kind="captions">`
// ================================================================================

using System.Text.RegularExpressions;
using AccessibilityStudio.Core.Models;

namespace AccessibilityStudio.Core.Analysis;

public static partial class HtmlSourceAccessibilityAnalyzer
{
    private static readonly HashSet<string> AmbiguousLinkPhrases = new(StringComparer.OrdinalIgnoreCase)
    {
        "click here", "here", "read more", "more", "learn more", "link", "details"
    };

    private static readonly HashSet<string> ExemptInputTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "hidden", "submit", "reset", "button", "image"
    };

    [GeneratedRegex(@"<!--[\s\S]*?-->|<script\b[^>]*>[\s\S]*?</script>|<style\b[^>]*>[\s\S]*?</style>", RegexOptions.IgnoreCase)]
    private static partial Regex NonMarkupMaskRegex();

    [GeneratedRegex(@"<html\b([^>]*)>", RegexOptions.IgnoreCase)]
    private static partial Regex HtmlTagRegex();

    [GeneratedRegex(@"<head\b[^>]*>([\s\S]*?)</head>", RegexOptions.IgnoreCase)]
    private static partial Regex HeadBlockRegex();

    [GeneratedRegex(@"<title\b[^>]*>([\s\S]*?)</title>", RegexOptions.IgnoreCase)]
    private static partial Regex TitleTagRegex();

    [GeneratedRegex(@"<body\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex BodyOpenRegex();

    [GeneratedRegex(@"<(header|nav|main|search|aside|footer|h[1-6])\b([^>]*)>([\s\S]*?)</\1>|<(header|nav|main|search|aside|footer)\b([^>]*)>", RegexOptions.IgnoreCase)]
    private static partial Regex OutlineScanRegex();

    [GeneratedRegex(@"<h([1-6])\b([^>]*)>([\s\S]*?)</h\1>", RegexOptions.IgnoreCase)]
    private static partial Regex HeadingBlockRegex();

    [GeneratedRegex(@"<nav\b([^>]*)>", RegexOptions.IgnoreCase)]
    private static partial Regex NavOpenRegex();

    [GeneratedRegex(@"<main\b([^>]*)>", RegexOptions.IgnoreCase)]
    private static partial Regex MainOpenRegex();

    [GeneratedRegex(@"<img\b([^>]*)>", RegexOptions.IgnoreCase)]
    private static partial Regex ImgTagRegex();

    [GeneratedRegex(@"<svg\b([^>]*)>([\s\S]*?)</svg>", RegexOptions.IgnoreCase)]
    private static partial Regex SvgBlockRegex();

    [GeneratedRegex(@"<table\b([^>]*)>([\s\S]*?)</table>", RegexOptions.IgnoreCase)]
    private static partial Regex TableBlockRegex();

    [GeneratedRegex(@"<tr\b[^>]*>([\s\S]*?)</tr>", RegexOptions.IgnoreCase)]
    private static partial Regex TableRowRegex();

    [GeneratedRegex(@"<td\b([^>]*)>([\s\S]*?)</td>", RegexOptions.IgnoreCase)]
    private static partial Regex TableCellRegex();

    [GeneratedRegex(@"<th\b([^>]*)>", RegexOptions.IgnoreCase)]
    private static partial Regex TableHeaderOpenRegex();

    [GeneratedRegex(@"<label\b([^>]*)>([\s\S]*?)</label>", RegexOptions.IgnoreCase)]
    private static partial Regex LabelBlockRegex();

    [GeneratedRegex(@"<(input|select|textarea)\b([^>]*)>", RegexOptions.IgnoreCase)]
    private static partial Regex FormControlOpenRegex();

    [GeneratedRegex(@"<iframe\b([^>]*)>", RegexOptions.IgnoreCase)]
    private static partial Regex IframeOpenRegex();

    [GeneratedRegex(@"<([a-z0-9-]+)\b([^>]*\btabindex\s*=\s*[""']?\s*([1-9]\d*)[""']?[^>]*)>", RegexOptions.IgnoreCase)]
    private static partial Regex PositiveTabIndexRegex();

    [GeneratedRegex(@"<(button|input|select|textarea|a)\b([^>]*\baria-hidden\s*=\s*[""']true[""'][^>]*)>", RegexOptions.IgnoreCase)]
    private static partial Regex AriaHiddenInteractiveRegex();

    [GeneratedRegex(@"<a\b([^>]*)>([\s\S]*?)</a>", RegexOptions.IgnoreCase)]
    private static partial Regex AnchorBlockRegex();

    [GeneratedRegex(@"<video\b([^>]*)>([\s\S]*?)</video>", RegexOptions.IgnoreCase)]
    private static partial Regex VideoBlockRegex();

    [GeneratedRegex(@"([a-zA-Z_:][-a-zA-Z0-9_:.]*)\s*(?:=\s*(?:""([^""]*)""|'([^']*)'|([^\s""'=<>`]+)))?", RegexOptions.Compiled)]
    private static partial Regex AttributePairRegex();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex StripTagsRegex();

    /// <summary>
    /// Runs all source-level HTML5 &amp; WAI-ARIA checks against <paramref name="htmlSource"/>,
    /// returning both exact line/column findings and the document's structural outline.
    /// </summary>
    public static (IReadOnlyList<AuditFinding> Findings, IReadOnlyList<DocumentOutlineNode> Outline) Analyze(
        string htmlSource,
        string targetPath,
        ComplianceProfile profile = ComplianceProfile.Wcag22AA_Section508)
    {
        List<AuditFinding> findings = [];
        List<DocumentOutlineNode> outline = [];

        if (string.IsNullOrEmpty(htmlSource))
        {
            return (findings, outline);
        }

        int[] lineOffsets = BuildLineStartOffsets(htmlSource);

        // Mask HTML comments (`<!-- ... -->`) and `<script>` / `<style>` interiors with spaces
        // while preserving EVERY `\n` character so character indices and line/column numbers stay 100% identical!
        string masked = MaskCommentsAndScriptsPreservingPositions(htmlSource);

        bool isFullDocument = HtmlTagRegex().IsMatch(masked) || BodyOpenRegex().IsMatch(masked);

        // 1. Check `<html lang="...">` (WCAG 3.1.1 Level A)
        Match htmlMatch = HtmlTagRegex().Match(masked);
        if (htmlMatch.Success)
        {
            var attrs = ParseAttributes(htmlMatch.Groups[1].Value);
            if (!attrs.TryGetValue("lang", out string? langVal) || string.IsNullOrWhiteSpace(langVal))
            {
                AddFinding(
                    findings, lineOffsets, htmlSource, targetPath,
                    htmlMatch.Index, htmlMatch.Length,
                    ruleId: "WCAG-HTML-LANG",
                    title: "Missing primary document language on <html>",
                    severity: AuditSeverity.Serious,
                    wcag: "3.1.1 Language of Page",
                    level: "A",
                    sec508: "Revised 508 E205.4 (WCAG 3.1.1)",
                    message: "The root <html> element does not declare a valid BCP 47 `lang` attribute (e.g. `lang=\"en\"`), preventing JAWS, NVDA, and VoiceOver from loading the right pronunciation voice.",
                    guidance: "Add `lang=\"en\"` (or the appropriate BCP 47 language code) to the opening `<html>` tag.",
                    selector: "html",
                    guidedFixId: "FIX-HTML-LANG",
                    preview: InjectAttributePreview(htmlSource.Substring(htmlMatch.Index, htmlMatch.Length), "lang=\"en\"")
                );
            }
        }

        // 2. Check `<title>` inside `<head>` for full HTML documents (WCAG 2.4.2 Level A)
        if (isFullDocument)
        {
            Match headMatch = HeadBlockRegex().Match(masked);
            if (headMatch.Success)
            {
                Match titleMatch = TitleTagRegex().Match(headMatch.Groups[1].Value);
                if (!titleMatch.Success || string.IsNullOrWhiteSpace(StripHtmlText(titleMatch.Groups[1].Value)))
                {
                    AddFinding(
                        findings, lineOffsets, htmlSource, targetPath,
                        headMatch.Index, Math.Min(headMatch.Length, 80),
                        ruleId: "WCAG-PAGE-TITLE",
                        title: "Missing or empty <title> element in <head>",
                        severity: AuditSeverity.Serious,
                        wcag: "2.4.2 Page Titled",
                        level: "A",
                        sec508: "Revised 508 E205.4 (WCAG 2.4.2)",
                        message: "Every HTML document must provide a non-empty, descriptive `<title>` inside `<head>` so screen-reader and multi-tab users know which page they are viewing.",
                        guidance: "Add `<title>Descriptive Page Topic | Site Name</title>` inside `<head>`.",
                        selector: "head",
                        guidedFixId: titleMatch.Success ? null : "FIX-INSERT-TITLE",
                        preview: "<title>Document Title</title>"
                    );
                }
            }
        }

        // 3. Check `<main>` Landmark on Full Documents (WCAG 1.3.1 & 2.4.1)
        MatchCollection mainMatches = MainOpenRegex().Matches(masked);
        if (isFullDocument && BodyOpenRegex().Match(masked) is { Success: true } bodyMatch)
        {
            int visibleMainCount = mainMatches.Count(m => !ParseAttributes(m.Groups[1].Value).ContainsKey("hidden"));
            if (visibleMainCount == 0)
            {
                AddFinding(
                    findings, lineOffsets, htmlSource, targetPath,
                    bodyMatch.Index, bodyMatch.Length,
                    ruleId: "WCAG-MAIN-LANDMARK",
                    title: "Document <body> lacks a <main> landmark",
                    severity: AuditSeverity.Moderate,
                    wcag: "1.3.1 Info and Relationships",
                    level: "A",
                    sec508: "Revised 508 E205.4 (WCAG 1.3.1 / 2.4.1)",
                    message: "No `<main>` landmark was found inside `<body>`. Assistive-technology users rely on `<main>` to jump directly past repeated headers and sidebars.",
                    guidance: "Wrap the page's primary content inside a single `<main id=\"main-content\">...</main>` element.",
                    selector: "body"
                );
            }
            else if (visibleMainCount > 1)
            {
                Match secondMain = mainMatches[1];
                AddFinding(
                    findings, lineOffsets, htmlSource, targetPath,
                    secondMain.Index, secondMain.Length,
                    ruleId: "WCAG-MAIN-LANDMARK",
                    title: "Multiple visible <main> landmarks on a single page",
                    severity: AuditSeverity.Moderate,
                    wcag: "1.3.1 Info and Relationships",
                    level: "A",
                    sec508: "Revised 508 E205.4 (WCAG 1.3.1)",
                    message: "A document should expose only one visible `<main>` landmark at a time; inactive views must carry the `hidden` attribute.",
                    guidance: "Consolidate into a single `<main>` landmark or mark inactive route containers with `hidden`.",
                    selector: "main"
                );
            }
        }

        // 4. Check Multiple `<nav>` Landmarks for Distinguishable Names
        MatchCollection navMatches = NavOpenRegex().Matches(masked);
        if (navMatches.Count > 1)
        {
            foreach (Match nav in navMatches)
            {
                var attrs = ParseAttributes(nav.Groups[1].Value);
                bool hasName = (attrs.TryGetValue("aria-label", out string? al) && !string.IsNullOrWhiteSpace(al)) ||
                               (attrs.TryGetValue("aria-labelledby", out string? alb) && !string.IsNullOrWhiteSpace(alb));
                if (!hasName)
                {
                    AddFinding(
                        findings, lineOffsets, htmlSource, targetPath,
                        nav.Index, nav.Length,
                        ruleId: "WCAG-MULTI-NAV-LABEL",
                        title: "Multiple <nav> landmarks without distinguishable aria-label",
                        severity: AuditSeverity.Moderate,
                        wcag: "1.3.1 Info and Relationships",
                        level: "A",
                        sec508: "Revised 508 E205.4 (WCAG 1.3.1)",
                        message: "When a page contains more than one `<nav>` landmark, unlabeled `<nav>` regions are announced identically as \"navigation\" in screen-reader landmark lists.",
                        guidance: "Add a concise `aria-label` (e.g. `aria-label=\"Primary\"` or `aria-label=\"Breadcrumb\"`—without repeating the word \"navigation\").",
                        selector: "nav",
                        guidedFixId: "FIX-NAV-ARIA-LABEL",
                        preview: InjectAttributePreview(htmlSource.Substring(nav.Index, nav.Length), "aria-label=\"Section\"")
                    );
                }
            }
        }

        // 5. Build Outline & Check Heading Hierarchy (`<h1>`..`<h6>`)
        int previousHeadingLevel = 0;
        foreach (Match h in HeadingBlockRegex().Matches(masked))
        {
            int level = h.Groups[1].Value[0] - '0';
            string text = StripHtmlText(h.Groups[3].Value);
            (int line, _) = GetLineAndColumn(lineOffsets, h.Index);
            bool skipped = previousHeadingLevel > 0 && level > previousHeadingLevel + 1;

            outline.Add(new DocumentOutlineNode
            {
                Kind = "Heading",
                TagOrRole = $"h{level}",
                Label = string.IsNullOrWhiteSpace(text) ? "(Empty Heading!)" : text,
                Line = line,
                HeadingLevel = level,
                HasWarning = skipped || string.IsNullOrWhiteSpace(text),
                WarningNote = skipped ? $"Skipped from H{previousHeadingLevel} to H{level}" : null
            });

            if (string.IsNullOrWhiteSpace(text))
            {
                AddFinding(
                    findings, lineOffsets, htmlSource, targetPath,
                    h.Index, h.Length,
                    ruleId: "WCAG-EMPTY-HEADING",
                    title: $"Empty <h{level}> heading element",
                    severity: AuditSeverity.Serious,
                    wcag: "2.4.6 Headings and Labels",
                    level: "AA",
                    sec508: "Revised 508 E205.4 (WCAG 2.4.6)",
                    message: $"An `<h{level}>` heading contains no accessible text, causing screen readers to announce a blank heading level.",
                    guidance: "Provide descriptive visible text inside the heading or remove the empty heading tag.",
                    selector: $"h{level}"
                );
            }
            else if (skipped)
            {
                int expectedLevel = previousHeadingLevel + 1;
                string origSnippet = htmlSource.Substring(h.Index, h.Length);
                string fixedPreview = Regex.Replace(
                    origSnippet,
                    @"^(<h)[1-6]([\s>][\s\S]*</h)[1-6](>)$",
                    $"${{1}}{expectedLevel}${{2}}{expectedLevel}${{3}}",
                    RegexOptions.IgnoreCase
                );

                AddFinding(
                    findings, lineOffsets, htmlSource, targetPath,
                    h.Index, h.Length,
                    ruleId: "WCAG-HEADING-SKIP",
                    title: $"Skipped heading hierarchy (<h{previousHeadingLevel}> directly to <h{level}>)",
                    severity: AuditSeverity.Moderate,
                    wcag: "1.3.1 Info and Relationships",
                    level: "A",
                    sec508: "Revised 508 E205.4 (WCAG 1.3.1)",
                    message: $"Heading hierarchy jumps from `<h{previousHeadingLevel}>` directly to `<h{level}>`, skipping `<h{expectedLevel}>` and confusing screen-reader outline navigation.",
                    guidance: $"Change `<h{level}>` to `<h{expectedLevel}>` so headings descend sequentially without gaps.",
                    selector: $"h{level}",
                    guidedFixId: "FIX-HEADING-LEVEL",
                    preview: fixedPreview
                );
            }

            previousHeadingLevel = level;
        }

        // Also record primary Landmarks into the outline sorted by line
        foreach (Match lm in Regex.Matches(masked, @"<(header|nav|main|search|aside|footer)\b([^>]*)>", RegexOptions.IgnoreCase))
        {
            string tag = lm.Groups[1].Value.ToLowerInvariant();
            var attrs = ParseAttributes(lm.Groups[2].Value);
            attrs.TryGetValue("aria-label", out string? ariaLabel);
            (int line, _) = GetLineAndColumn(lineOffsets, lm.Index);
            outline.Add(new DocumentOutlineNode
            {
                Kind = "Landmark",
                TagOrRole = tag,
                Label = string.IsNullOrWhiteSpace(ariaLabel) ? $"<{tag}>" : $"<{tag}> \"{ariaLabel}\"",
                Line = line,
                HeadingLevel = 0
            });
        }
        outline.Sort((a, b) => a.Line.CompareTo(b.Line));

        // 6. Check Data Tables: Fake Bold `<td>` Headers & Missing `<th scope>` (WCAG 1.3.1 Level A)
        foreach (Match table in TableBlockRegex().Matches(masked))
        {
            var tableAttrs = ParseAttributes(table.Groups[1].Value);
            bool isPresentational = tableAttrs.TryGetValue("role", out string? tRole) &&
                                    (tRole.Equals("presentation", StringComparison.OrdinalIgnoreCase) ||
                                     tRole.Equals("none", StringComparison.OrdinalIgnoreCase));
            if (isPresentational)
            {
                continue;
            }

            string tableBody = table.Groups[2].Value;
            int tableBodyStart = table.Groups[2].Index;

            // Check first `<tr>` for fake `<td style="font-weight: bold">` or `<td><strong>...</strong></td>` headers!
            Match firstRow = TableRowRegex().Match(tableBody);
            if (firstRow.Success && !TableHeaderOpenRegex().IsMatch(firstRow.Groups[1].Value))
            {
                MatchCollection firstRowCells = TableCellRegex().Matches(firstRow.Groups[1].Value);
                if (firstRowCells.Count > 0 && firstRowCells.All(IsFakeBoldHeaderCell))
                {
                    int rowAbsIndex = tableBodyStart + firstRow.Index;
                    string origRow = htmlSource.Substring(rowAbsIndex, firstRow.Length);
                    string previewRow = ConvertFakeHeaderRowPreview(origRow);

                    AddFinding(
                        findings, lineOffsets, htmlSource, targetPath,
                        rowAbsIndex, firstRow.Length,
                        ruleId: "WCAG-TABLE-FAKE-HEADER",
                        title: "Visual-only bold <td> cells used instead of semantic <th scope=\"col\"> headers",
                        severity: AuditSeverity.Serious,
                        wcag: "1.3.1 Info and Relationships",
                        level: "A",
                        sec508: "Revised 508 E205.4 (WCAG 1.3.1)",
                        message: "The first row of this `<table>` styles `<td>` cells with bold formatting instead of semantic `<th scope=\"col\">` column headers—leaving JAWS/NVDA unable to announce column names as users navigate cells.",
                        guidance: "Refactor header `<td>` elements into `<th scope=\"col\">` inside `<thead>`.",
                        selector: "table tr:first-child td",
                        guidedFixId: "FIX-FAKE-TABLE-ROW",
                        preview: previewRow
                    );
                }
            }

            // Check every `<th>` inside this table for explicit `scope="col"` or `scope="row"`
            foreach (Match th in TableHeaderOpenRegex().Matches(tableBody))
            {
                var thAttrs = ParseAttributes(th.Groups[1].Value);
                if (!thAttrs.TryGetValue("scope", out string? scopeVal) || string.IsNullOrWhiteSpace(scopeVal))
                {
                    int thAbsIndex = tableBodyStart + th.Index;
                    string origTh = htmlSource.Substring(thAbsIndex, th.Length);
                    AddFinding(
                        findings, lineOffsets, htmlSource, targetPath,
                        thAbsIndex, th.Length,
                        ruleId: "WCAG-TH-SCOPE",
                        title: "Table header <th> missing explicit scope attribute",
                        severity: AuditSeverity.Moderate,
                        wcag: "1.3.1 Info and Relationships",
                        level: "A",
                        sec508: "Revised 508 E205.4 (WCAG 1.3.1)",
                        message: "Table `<th>` header cell omits `scope=\"col\"` or `scope=\"row\"`, creating ambiguous header associations in multi-axis data tables.",
                        guidance: "Add `scope=\"col\"` for column headers or `scope=\"row\"` for row headers.",
                        selector: "table th:not([scope])",
                        guidedFixId: "FIX-TH-SCOPE-COL",
                        preview: InjectAttributePreview(origTh, "scope=\"col\"")
                    );
                }
            }
        }

        // 7. Check `<img>` Alt Text (WCAG 1.1.1 Level A)
        foreach (Match img in ImgTagRegex().Matches(masked))
        {
            var attrs = ParseAttributes(img.Groups[1].Value);
            bool ariaHidden = attrs.TryGetValue("aria-hidden", out string? ah) &&
                              ah.Equals("true", StringComparison.OrdinalIgnoreCase);
            if (ariaHidden) continue;

            if (!attrs.TryGetValue("alt", out string? altVal))
            {
                string origImg = htmlSource.Substring(img.Index, img.Length);
                AddFinding(
                    findings, lineOffsets, htmlSource, targetPath,
                    img.Index, img.Length,
                    ruleId: "WCAG-IMG-ALT",
                    title: "Image <img> missing mandatory alt attribute",
                    severity: AuditSeverity.Critical,
                    wcag: "1.1.1 Non-text Content",
                    level: "A",
                    sec508: "Revised 508 E205.4 (WCAG 1.1.1)",
                    message: "This `<img>` lacks an `alt` attribute, causing screen readers to read out the raw image filename or URL.",
                    guidance: "Provide concise, meaningful `alt=\"...\"` text—or `alt=\"\"` if the image is purely decorative.",
                    selector: "img:not([alt])",
                    guidedFixId: "FIX-IMG-DECORATIVE-ALT",
                    preview: InjectAttributePreview(origImg, "alt=\"\"")
                );
            }
            else if (altVal is not null &&
                     (altVal.StartsWith("image of ", StringComparison.OrdinalIgnoreCase) ||
                      altVal.StartsWith("picture of ", StringComparison.OrdinalIgnoreCase) ||
                      altVal.StartsWith("photo of ", StringComparison.OrdinalIgnoreCase)))
            {
                AddFinding(
                    findings, lineOffsets, htmlSource, targetPath,
                    img.Index, img.Length,
                    ruleId: "WCAG-IMG-REDUNDANT-ALT",
                    title: "Redundant 'image of / picture of' prefix inside img alt attribute",
                    severity: AuditSeverity.Advisory,
                    wcag: "1.1.1 Non-text Content",
                    level: "Advisory",
                    sec508: "Revised 508 Best Practice",
                    message: $"Screen readers already announce `<img>` as \"graphic\" or \"image\", so `{altVal}` is read redundantly as \"Graphic, {altVal}\".",
                    guidance: "Remove \"image of\" / \"picture of\" and describe the visual meaning directly.",
                    selector: "img[alt]"
                );
            }
        }

        // 8. Check Inline `<svg>` Elements (WCAG 1.1.1 & 4.1.2)
        foreach (Match svg in SvgBlockRegex().Matches(masked))
        {
            var attrs = ParseAttributes(svg.Groups[1].Value);
            bool isHidden = attrs.TryGetValue("aria-hidden", out string? ah) &&
                            ah.Equals("true", StringComparison.OrdinalIgnoreCase);
            bool hasImgRole = attrs.TryGetValue("role", out string? role) &&
                              role.Equals("img", StringComparison.OrdinalIgnoreCase);
            bool hasLabel = (attrs.TryGetValue("aria-label", out string? al) && !string.IsNullOrWhiteSpace(al)) ||
                            attrs.ContainsKey("aria-labelledby") ||
                            TitleTagRegex().IsMatch(svg.Groups[2].Value);

            if (!isHidden && !(hasImgRole && hasLabel))
            {
                string origOpen = htmlSource.Substring(svg.Index, svg.Groups[1].Length + 5);
                AddFinding(
                    findings, lineOffsets, htmlSource, targetPath,
                    svg.Index, Math.Min(svg.Length, 90),
                    ruleId: "WCAG-SVG-ACCESSIBLE",
                    title: "Inline <svg> lacks aria-hidden=\"true\" (decorative) or role=\"img\" + accessible name",
                    severity: AuditSeverity.Moderate,
                    wcag: "1.1.1 Non-text Content",
                    level: "A",
                    sec508: "Revised 508 E205.4 (WCAG 1.1.1 / 4.1.2)",
                    message: "Unmarked inline `<svg>` graphics can expose noisy internal vector nodes or unnamed graphics to screen readers.",
                    guidance: "Add `aria-hidden=\"true\" focusable=\"false\"` for decorative icons, or `role=\"img\"` paired with `<title>` / `aria-label` for informative diagrams.",
                    selector: "svg:not([aria-hidden='true']):not([role='img'])",
                    guidedFixId: "FIX-SVG-DECORATIVE",
                    preview: InjectAttributePreview(origOpen, "aria-hidden=\"true\" focusable=\"false\"")
                );
            }
        }

        // 9. Collect Explicit `<label for="...">` IDs & Implicit Wrapping `<label>` Spans, Then Check Form Controls
        HashSet<string> labeledIds = new(StringComparer.Ordinal);
        List<(int Start, int End)> wrappingLabelRanges = [];
        foreach (Match lbl in LabelBlockRegex().Matches(masked))
        {
            var lAttrs = ParseAttributes(lbl.Groups[1].Value);
            if (lAttrs.TryGetValue("for", out string? forId) && !string.IsNullOrWhiteSpace(forId))
            {
                labeledIds.Add(forId);
            }
            wrappingLabelRanges.Add((lbl.Index, lbl.Index + lbl.Length));
        }

        foreach (Match ctrl in FormControlOpenRegex().Matches(masked))
        {
            string tag = ctrl.Groups[1].Value.ToLowerInvariant();
            var attrs = ParseAttributes(ctrl.Groups[2].Value);

            if (tag == "input" && attrs.TryGetValue("type", out string? inputType) && ExemptInputTypes.Contains(inputType))
            {
                continue;
            }
            if (attrs.TryGetValue("aria-hidden", out string? ah) && ah.Equals("true", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            bool hasForLabel = attrs.TryGetValue("id", out string? idVal) && !string.IsNullOrWhiteSpace(idVal) && labeledIds.Contains(idVal);
            bool wrappedInLabel = wrappingLabelRanges.Any(r => ctrl.Index >= r.Start && ctrl.Index <= r.End);
            bool hasAriaLabel = (attrs.TryGetValue("aria-label", out string? al) && !string.IsNullOrWhiteSpace(al)) ||
                                (attrs.TryGetValue("aria-labelledby", out string? alb) && !string.IsNullOrWhiteSpace(alb));

            if (!hasForLabel && !wrappedInLabel && !hasAriaLabel)
            {
                string origCtrl = htmlSource.Substring(ctrl.Index, ctrl.Length);
                string fallbackLabel = attrs.TryGetValue("placeholder", out string? ph) && !string.IsNullOrWhiteSpace(ph)
                    ? ph
                    : "Input value";
                AddFinding(
                    findings, lineOffsets, htmlSource, targetPath,
                    ctrl.Index, ctrl.Length,
                    ruleId: "WCAG-FORM-LABEL",
                    title: $"Form control <{tag}> has no programmatic label",
                    severity: AuditSeverity.Critical,
                    wcag: "3.3.2 Labels or Instructions",
                    level: "A",
                    sec508: "Revised 508 E205.4 (WCAG 1.3.1 / 3.3.2 / 4.1.2)",
                    message: $"This `<{tag}>` control lacks an associated `<label for=\"...\">`, `aria-label`, or `aria-labelledby`. Note: `placeholder` disappears on typing and fails as an accessible label.",
                    guidance: "Connect a visible `<label for=\"control-id\">` to `id=\"control-id\"` (or add `aria-label` on compact toolbar search inputs).",
                    selector: $"{tag}:not([aria-label]):not([aria-labelledby])",
                    guidedFixId: "FIX-CONTROL-ARIA-LABEL",
                    preview: InjectAttributePreview(origCtrl, $"aria-label=\"{EscapeAttr(fallbackLabel)}\"")
                );
            }

            // WCAG 1.3.5 Level AA: Identify Input Purpose (`autocomplete`) on email/tel personal inputs
            if (profile != ComplianceProfile.Wcag22LevelA && tag == "input")
            {
                attrs.TryGetValue("type", out string? tVal);
                attrs.TryGetValue("name", out string? nVal);
                bool isEmail = string.Equals(tVal, "email", StringComparison.OrdinalIgnoreCase) ||
                               (nVal?.Contains("email", StringComparison.OrdinalIgnoreCase) ?? false);
                bool isTel = string.Equals(tVal, "tel", StringComparison.OrdinalIgnoreCase);

                if ((isEmail || isTel) && !attrs.ContainsKey("autocomplete"))
                {
                    string suggestedAuto = isEmail ? "email" : "tel";
                    string origCtrl = htmlSource.Substring(ctrl.Index, ctrl.Length);
                    AddFinding(
                        findings, lineOffsets, htmlSource, targetPath,
                        ctrl.Index, ctrl.Length,
                        ruleId: "WCAG-AUTOCOMPLETE",
                        title: $"Personal input (`{suggestedAuto}`) missing WCAG 1.3.5 autocomplete attribute",
                        severity: AuditSeverity.Moderate,
                        wcag: "1.3.5 Identify Input Purpose",
                        level: "AA",
                        sec508: "WCAG 2.1/2.2 Level AA / ADA Title II",
                        message: $"Personal contact field (`{suggestedAuto}`) omits `autocomplete=\"{suggestedAuto}\"`, hindering cognitive accessibility tools and password/autofill assistive agents.",
                        guidance: $"Add `autocomplete=\"{suggestedAuto}\"` to the `<input>` element.",
                        selector: $"input[type='{suggestedAuto}']:not([autocomplete])",
                        guidedFixId: isEmail ? "FIX-AUTOCOMPLETE-EMAIL" : "FIX-AUTOCOMPLETE-TEL",
                        preview: InjectAttributePreview(origCtrl, $"autocomplete=\"{suggestedAuto}\"")
                    );
                }
            }
        }

        // 10. Check `<iframe>` Title & `scrolling="no"` (WCAG 4.1.2 & 1.4.10)
        foreach (Match ifr in IframeOpenRegex().Matches(masked))
        {
            var attrs = ParseAttributes(ifr.Groups[1].Value);
            string origIframe = htmlSource.Substring(ifr.Index, ifr.Length);
            if (!attrs.TryGetValue("title", out string? iframeTitle) || string.IsNullOrWhiteSpace(iframeTitle))
            {
                AddFinding(
                    findings, lineOffsets, htmlSource, targetPath,
                    ifr.Index, ifr.Length,
                    ruleId: "WCAG-IFRAME-TITLE",
                    title: "Embedded <iframe> missing descriptive title attribute",
                    severity: AuditSeverity.Serious,
                    wcag: "4.1.2 Name, Role, Value",
                    level: "A",
                    sec508: "Revised 508 E205.4 (WCAG 2.4.1 / 4.1.2)",
                    message: "Screen readers announce untitled `<iframe>` elements by raw embed URL, leaving users unable to identify or skip embedded widgets cleanly.",
                    guidance: "Add a clear `title=\"...\"` attribute describing the embedded frame's purpose.",
                    selector: "iframe:not([title])",
                    guidedFixId: "FIX-IFRAME-TITLE",
                    preview: InjectAttributePreview(origIframe, "title=\"Embedded content\"")
                );
            }
            if (attrs.TryGetValue("scrolling", out string? scr) && scr.Equals("no", StringComparison.OrdinalIgnoreCase))
            {
                AddFinding(
                    findings, lineOffsets, htmlSource, targetPath,
                    ifr.Index, ifr.Length,
                    ruleId: "WCAG-IFRAME-SCROLL",
                    title: "<iframe> disables scrolling (`scrolling=\"no\"`), blocking 200%/400% zoom reflow",
                    severity: AuditSeverity.Moderate,
                    wcag: "1.4.10 Reflow",
                    level: "AA",
                    sec508: "WCAG 2.1/2.2 Level AA (1.4.4 / 1.4.10)",
                    message: "Disabling iframe scrollbars clips enlarged text when low-vision users zoom to 200% or 400%.",
                    guidance: "Remove the deprecated `scrolling=\"no\"` attribute.",
                    selector: "iframe[scrolling='no']"
                );
            }
        }

        // 11. Check Positive `tabindex > 0` (WCAG 2.4.3 Level A)
        foreach (Match ti in PositiveTabIndexRegex().Matches(masked))
        {
            string badVal = ti.Groups[3].Value;
            string origTag = htmlSource.Substring(ti.Index, ti.Length);
            string previewTag = Regex.Replace(origTag, @"\btabindex\s*=\s*([""']?)\s*[1-9]\d*\1", "tabindex=\"0\"", RegexOptions.IgnoreCase);
            AddFinding(
                findings, lineOffsets, htmlSource, targetPath,
                ti.Index, ti.Length,
                ruleId: "WCAG-POSITIVE-TABINDEX",
                title: $"Positive tabindex=\"{badVal}\" overrides natural DOM keyboard focus order",
                severity: AuditSeverity.Serious,
                wcag: "2.4.3 Focus Order",
                level: "A",
                sec508: "Revised 508 E205.4 (WCAG 2.4.3)",
                message: $"Setting `tabindex=\"{badVal}\"` forces keyboard focus to jump here ahead of every normal button and link on the page, breaking visual-to-keyboard predictability.",
                guidance: "Never use `tabindex` greater than `0`. Arrange elements in logical DOM order and use native `<button>`/`<a>` or `tabindex=\"0\"`.",
                selector: "[tabindex]",
                guidedFixId: "FIX-TABINDEX-ZERO",
                preview: previewTag
            );
        }

        // 12. Check `aria-hidden="true"` on Focusable Interactive Elements (WCAG 4.1.2 Level A)
        foreach (Match ahFocus in AriaHiddenInteractiveRegex().Matches(masked))
        {
            var attrs = ParseAttributes(ahFocus.Groups[2].Value);
            bool hasNegTabIndex = attrs.TryGetValue("tabindex", out string? tIdx) && tIdx == "-1";
            bool isDisabled = attrs.ContainsKey("disabled");
            if (!hasNegTabIndex && !isDisabled)
            {
                string origTag = htmlSource.Substring(ahFocus.Index, ahFocus.Length);
                string previewTag = Regex.Replace(origTag, @"\s*\baria-hidden\s*=\s*[""']true[""']", "", RegexOptions.IgnoreCase);
                AddFinding(
                    findings, lineOffsets, htmlSource, targetPath,
                    ahFocus.Index, ahFocus.Length,
                    ruleId: "WCAG-ARIA-HIDDEN-FOCUS",
                    title: $"Focusable <{ahFocus.Groups[1].Value}> hidden from screen readers via aria-hidden=\"true\"",
                    severity: AuditSeverity.Critical,
                    wcag: "4.1.2 Name, Role, Value",
                    level: "A",
                    sec508: "Revised 508 E205.4 (WCAG 4.1.2)",
                    message: "Applying `aria-hidden=\"true\"` to an element that still receives keyboard `Tab` focus causes JAWS/NVDA/VoiceOver users to land on a silent, unannounced control.",
                    guidance: "Remove `aria-hidden=\"true\"` if the control is interactive, or use the `hidden` attribute / `disabled` if it should be hidden from everyone.",
                    selector: "[aria-hidden='true']",
                    guidedFixId: "FIX-REMOVE-ARIA-HIDDEN",
                    preview: previewTag
                );
            }
        }

        // 13. Check Ambiguous Links & `href`-less Anchor Buttons (WCAG 2.4.4 & 4.1.2)
        foreach (Match a in AnchorBlockRegex().Matches(masked))
        {
            var attrs = ParseAttributes(a.Groups[1].Value);
            string linkText = StripHtmlText(a.Groups[2].Value);
            bool hasAriaName = (attrs.TryGetValue("aria-label", out string? al) && !string.IsNullOrWhiteSpace(al)) ||
                               attrs.ContainsKey("aria-labelledby");

            if (!attrs.TryGetValue("href", out string? href) || string.IsNullOrWhiteSpace(href) || href == "#")
            {
                AddFinding(
                    findings, lineOffsets, htmlSource, targetPath,
                    a.Index, a.Length,
                    ruleId: "WCAG-ANCHOR-NO-HREF",
                    title: "Anchor <a> used without a valid destination href (use <button type=\"button\"> for actions)",
                    severity: AuditSeverity.Serious,
                    wcag: "4.1.2 Name, Role, Value",
                    level: "A",
                    sec508: "Revised 508 E205.4 (WCAG 2.1.1 / 4.1.2)",
                    message: "An `<a>` without a real `href` is removed from keyboard `Tab` order (or `href=\"#\"` jumps scroll position to the top) and lacks Spacebar button activation.",
                    guidance: "Use `<button type=\"button\">` for in-page actions, or provide a real URL in `href` for navigation.",
                    selector: "a:not([href]), a[href='#']"
                );
            }
            else if (!hasAriaName && AmbiguousLinkPhrases.Contains(linkText))
            {
                AddFinding(
                    findings, lineOffsets, htmlSource, targetPath,
                    a.Index, a.Length,
                    ruleId: "WCAG-AMBIGUOUS-LINK",
                    title: $"Ambiguous link text (\"{linkText}\") lacks descriptive context",
                    severity: AuditSeverity.Moderate,
                    wcag: "2.4.4 Link Purpose (In Context)",
                    level: "A",
                    sec508: "Revised 508 E205.4 (WCAG 2.4.4)",
                    message: $"Screen-reader users browsing the Links List (`Insert+F7` in JAWS / NVDA) hear \"{linkText}\" out of context with zero clue where the link leads.",
                    guidance: "Replace generic link phrases like \"Click here\" or \"Read more\" with specific destination text (e.g. \"Read the Q3 Accessibility Report\").",
                    selector: "a[href]"
                );
            }
        }

        // 14. Check `<video>` for Synchronous `<track kind="captions">` (WCAG 1.2.2 Level A)
        foreach (Match vid in VideoBlockRegex().Matches(masked))
        {
            var vAttrs = ParseAttributes(vid.Groups[1].Value);
            if (vAttrs.TryGetValue("aria-hidden", out string? ah) && ah.Equals("true", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            bool hasCaptionTrack = Regex.IsMatch(
                vid.Groups[2].Value,
                @"<track\b[^>]*\bkind\s*=\s*[""']?(?:captions|subtitles)[""']?",
                RegexOptions.IgnoreCase
            );

            if (!hasCaptionTrack)
            {
                AddFinding(
                    findings, lineOffsets, htmlSource, targetPath,
                    vid.Index, Math.Min(vid.Length, 120),
                    ruleId: "WCAG-MEDIA-CAPTIONS",
                    title: "<video> element lacks a synchronized <track kind=\"captions\"> WebVTT track",
                    severity: AuditSeverity.Critical,
                    wcag: "1.2.2 Captions (Prerecorded)",
                    level: "A",
                    sec508: "Revised 508 E205.4 / 503.4 (Captions)",
                    message: "Prerecorded `<video>` media does not include a `<track kind=\"captions\" src=\"...vtt\">` element for Deaf and hard-of-hearing users.",
                    guidance: "Add `<track kind=\"captions\" src=\"captions.vtt\" srclang=\"en\" label=\"English\">` inside `<video>`.",
                    selector: "video",
                    guidedFixId: "FIX-VIDEO-TRACK",
                    preview: "<track kind=\"captions\" src=\"captions.vtt\" srclang=\"en\" label=\"English\">"
                );
            }
        }

        // Sort findings cleanly by line number, then column
        findings.Sort((a, b) =>
        {
            int cmp = a.StartLine.CompareTo(b.StartLine);
            return cmp != 0 ? cmp : a.StartColumn.CompareTo(b.StartColumn);
        });

        return (findings, outline);
    }

    // ------------------------------------------------------------------------
    // INTERNAL SOURCE COORDINATE & PARSING HELPERS
    // ------------------------------------------------------------------------

    public static int[] BuildLineStartOffsets(string text)
    {
        List<int> starts = [0];
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '\n')
            {
                starts.Add(i + 1);
            }
        }
        return [.. starts];
    }

    public static (int Line, int Column) GetLineAndColumn(int[] lineStarts, int charIndex)
    {
        int clamped = Math.Clamp(charIndex, 0, int.MaxValue);
        int idx = Array.BinarySearch(lineStarts, clamped);
        int lineZeroBased = idx >= 0 ? idx : (~idx) - 1;
        lineZeroBased = Math.Max(0, lineZeroBased);
        int colOneBased = (clamped - lineStarts[lineZeroBased]) + 1;
        return (lineZeroBased + 1, colOneBased);
    }

    private static string MaskCommentsAndScriptsPreservingPositions(string html)
    {
        char[] buf = html.ToCharArray();
        foreach (Match m in NonMarkupMaskRegex().Matches(html))
        {
            for (int i = m.Index; i < m.Index + m.Length; i++)
            {
                if (buf[i] != '\n' && buf[i] != '\r')
                {
                    buf[i] = ' ';
                }
            }
        }
        return new string(buf);
    }

    private static Dictionary<string, string> ParseAttributes(string rawAttrFragment)
    {
        Dictionary<string, string> map = new(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in AttributePairRegex().Matches(rawAttrFragment))
        {
            string name = m.Groups[1].Value;
            string val = m.Groups[2].Success ? m.Groups[2].Value
                : m.Groups[3].Success ? m.Groups[3].Value
                : m.Groups[4].Success ? m.Groups[4].Value
                : string.Empty;
            map[name] = val;
        }
        return map;
    }

    private static string StripHtmlText(string fragment)
    {
        string noTags = StripTagsRegex().Replace(fragment, " ");
        string decoded = System.Net.WebUtility.HtmlDecode(noTags);
        return Regex.Replace(decoded, @"\s+", " ").Trim();
    }

    private static bool IsFakeBoldHeaderCell(Match tdMatch)
    {
        string attrs = tdMatch.Groups[1].Value;
        string inner = tdMatch.Groups[2].Value.Trim();

        if (Regex.IsMatch(attrs, @"font-weight\s*:\s*(?:bold|[6-9]00)", RegexOptions.IgnoreCase))
        {
            return true;
        }

        return Regex.IsMatch(inner, @"^<(?:strong|b)\b[^>]*>[\s\S]+</(?:strong|b)>$", RegexOptions.IgnoreCase);
    }

    public static string ConvertFakeHeaderRowPreview(string rowHtml)
    {
        return Regex.Replace(
            rowHtml,
            @"<td\b([^>]*)>([\s\S]*?)</td>",
            m =>
            {
                string attrs = Regex.Replace(m.Groups[1].Value, @"\s*style\s*=\s*[""'][^""']*font-weight\s*:\s*(?:bold|[6-9]00)\s*;?\s*[""']", "", RegexOptions.IgnoreCase);
                string inner = Regex.Replace(m.Groups[2].Value.Trim(), @"^<(?:strong|b)\b[^>]*>([\s\S]*?)</(?:strong|b)>$", "$1", RegexOptions.IgnoreCase);
                return $"<th scope=\"col\"{attrs}>{inner}</th>";
            },
            RegexOptions.IgnoreCase
        );
    }

    public static string InjectAttributePreview(string openTag, string attrToInsert)
    {
        if (openTag.EndsWith("/>", StringComparison.Ordinal))
        {
            return openTag[..^2].TrimEnd() + " " + attrToInsert + " />";
        }
        if (openTag.EndsWith('>'))
        {
            return openTag[..^1].TrimEnd() + " " + attrToInsert + ">";
        }
        return openTag + " " + attrToInsert;
    }

    private static string EscapeAttr(string text) =>
        text.Replace("&", "&amp;").Replace("\"", "&quot;");

    private static void AddFinding(
        List<AuditFinding> list,
        int[] lineOffsets,
        string rawSource,
        string targetPath,
        int startIndex,
        int length,
        string ruleId,
        string title,
        AuditSeverity severity,
        string wcag,
        string level,
        string sec508,
        string message,
        string guidance,
        string? selector = null,
        string? guidedFixId = null,
        string? preview = null)
    {
        int safeLen = Math.Clamp(length, 1, Math.Max(1, rawSource.Length - startIndex));
        (int startLine, int startCol) = GetLineAndColumn(lineOffsets, startIndex);
        (int endLine, int endCol) = GetLineAndColumn(lineOffsets, startIndex + safeLen);

        string snippet = rawSource.Substring(startIndex, Math.Min(safeLen, 160)).Trim();
        snippet = Regex.Replace(snippet, @"\s+", " ");

        list.Add(new AuditFinding
        {
            RuleId = ruleId,
            Title = title,
            Severity = severity,
            WcagCriterion = wcag,
            WcagLevel = level,
            Section508Reference = sec508,
            TargetPath = targetPath,
            StartLine = startLine,
            StartColumn = startCol,
            EndLine = endLine,
            EndColumn = endCol,
            OffendingSnippet = snippet,
            Message = message,
            RemediationGuidance = guidance,
            DomSelectorHint = selector,
            GuidedFixId = guidedFixId,
            SuggestedReplacementPreview = preview
        });
    }
}
