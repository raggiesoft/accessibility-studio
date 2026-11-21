// ================================================================================
// FILE: Refactoring/GuidedSourceRefactorer.cs
// PROJECT: AccessibilityStudio.Core
// LICENSE: MIT License
// ================================================================================
// PLAIN-ENGLISH OVERVIEW (100-LEVEL ASSISTED CODE REFACTORING GUIDE):
//   How does "Preview Side-by-Side Diff" & "Apply Guided Fix" work in our IDE?
//     Instead of blindly overwriting files behind a developer's back, RaggieSoft
//     Accessibility Studio takes the current editor text + a selected `AuditFinding`
//     that carries a `GuidedFixId`, computes the exact updated source document as
//     a new string (`RefactoredSource`), and hands BOTH (`OriginalSource` and
//     `RefactoredSource`) to Monaco's **Side-by-Side Diff Editor**!
//     Once the engineer reviews the green/red diff lines and clicks "Apply Fix",
//     the refactored code updates the editor buffer and logs a SHA-256 audit entry.
// ================================================================================

using System.Text.RegularExpressions;
using AccessibilityStudio.Core.Analysis;
using AccessibilityStudio.Core.Models;

namespace AccessibilityStudio.Core.Refactoring;

public sealed record RefactorPreviewResult(
    bool Succeeded,
    string OriginalSource,
    string RefactoredSource,
    string SummaryMessage
);

public static class GuidedSourceRefactorer
{
    /// <summary>
    /// Produces a deterministic refactored copy of <paramref name="source"/> for the
    /// specified <paramref name="finding"/> without mutating disk files directly.
    /// </summary>
    public static RefactorPreviewResult BuildPreview(string source, AuditFinding finding)
    {
        if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(finding.GuidedFixId))
        {
            return new RefactorPreviewResult(false, source, source, "This finding requires manual engineering review.");
        }

        string fixId = finding.GuidedFixId;

        // Special whole-file fixes (e.g. prepending `WEBVTT` or appending `prefers-reduced-motion`)
        if (fixId == "FIX-VTT-HEADER")
        {
            string updated = "WEBVTT\n\n" + source.TrimStart('\uFEFF');
            return new RefactorPreviewResult(true, source, updated, "Prepended mandatory `WEBVTT` header on Line 1.");
        }

        if (fixId == "FIX-CSS-APPEND-REDUCED-MOTION")
        {
            string snippet =
                "\n\n/* WCAG 2.3.3 Vestibular Accessibility Safeguard */\n" +
                "@media (prefers-reduced-motion: reduce) {\n" +
                "  *, *::before, *::after {\n" +
                "    animation-duration: 0.01ms !important;\n" +
                "    transition-duration: 0.01ms !important;\n" +
                "  }\n" +
                "}\n";
            return new RefactorPreviewResult(true, source, source.TrimEnd() + snippet, "Appended `@media (prefers-reduced-motion: reduce)` safeguard.");
        }

        // Convert 1-based `(StartLine, StartColumn)` back to exact character index in `source`
        int startIdx = GetCharIndexFromLineAndColumn(source, finding.StartLine, finding.StartColumn);
        if (startIdx < 0 || startIdx >= source.Length)
        {
            return new RefactorPreviewResult(false, source, source, "Source coordinates changed since last scan—press F5 to re-audit first.");
        }

        string tail = source[startIdx..];

        if (fixId == "FIX-HTML-LANG")
        {
            return ReplaceFirstRegexInTail(source, startIdx, tail, @"^<html\b[^>]*>",
                m => HtmlSourceAccessibilityAnalyzer.InjectAttributePreview(m.Value, "lang=\"en\""),
                "Added `lang=\"en\"` to `<html>`.");
        }

        if (fixId == "FIX-INSERT-TITLE")
        {
            return ReplaceFirstRegexInTail(source, startIdx, tail, @"^<head\b[^>]*>",
                m => m.Value + "\n  <title>Document Title</title>",
                "Inserted `<title>Document Title</title>` inside `<head>`.");
        }

        if (fixId == "FIX-NAV-ARIA-LABEL")
        {
            return ReplaceFirstRegexInTail(source, startIdx, tail, @"^<nav\b[^>]*>",
                m => HtmlSourceAccessibilityAnalyzer.InjectAttributePreview(m.Value, "aria-label=\"Section\""),
                "Added `aria-label=\"Section\"` to `<nav>`.");
        }

        if (fixId == "FIX-HEADING-LEVEL" && !string.IsNullOrEmpty(finding.SuggestedReplacementPreview))
        {
            return ReplaceFirstRegexInTail(source, startIdx, tail, @"^<h([1-6])\b[^>]*>[\s\S]*?</h\1>",
                _ => finding.SuggestedReplacementPreview!,
                "Normalized skipped heading level to sequential hierarchy.");
        }

        if (fixId == "FIX-FAKE-TABLE-ROW")
        {
            return ReplaceFirstRegexInTail(source, startIdx, tail, @"^<tr\b[^>]*>[\s\S]*?</tr>",
                m => HtmlSourceAccessibilityAnalyzer.ConvertFakeHeaderRowPreview(m.Value),
                "Refactored bold `<td>` cells into semantic `<th scope=\"col\">` headers.");
        }

        if (fixId == "FIX-TH-SCOPE-COL")
        {
            return ReplaceFirstRegexInTail(source, startIdx, tail, @"^<th\b[^>]*>",
                m => HtmlSourceAccessibilityAnalyzer.InjectAttributePreview(m.Value, "scope=\"col\""),
                "Added `scope=\"col\"` to `<th>`.");
        }

        if (fixId == "FIX-IMG-DECORATIVE-ALT")
        {
            return ReplaceFirstRegexInTail(source, startIdx, tail, @"^<img\b[^>]*>",
                m => HtmlSourceAccessibilityAnalyzer.InjectAttributePreview(m.Value, "alt=\"\""),
                "Added `alt=\"\"` attribute to `<img>` (customize text if image is informative).");
        }

        if (fixId == "FIX-SVG-DECORATIVE")
        {
            return ReplaceFirstRegexInTail(source, startIdx, tail, @"^<svg\b[^>]*>",
                m => HtmlSourceAccessibilityAnalyzer.InjectAttributePreview(m.Value, "aria-hidden=\"true\" focusable=\"false\""),
                "Marked decorative inline `<svg>` with `aria-hidden=\"true\" focusable=\"false\"`.");
        }

        if (fixId == "FIX-CONTROL-ARIA-LABEL" && !string.IsNullOrEmpty(finding.SuggestedReplacementPreview))
        {
            return ReplaceFirstRegexInTail(source, startIdx, tail, @"^<(?:input|select|textarea)\b[^>]*>",
                _ => finding.SuggestedReplacementPreview!,
                "Added programmatic `aria-label` to unlabeled form control.");
        }

        if (fixId is "FIX-AUTOCOMPLETE-EMAIL" or "FIX-AUTOCOMPLETE-TEL")
        {
            string token = fixId == "FIX-AUTOCOMPLETE-EMAIL" ? "email" : "tel";
            return ReplaceFirstRegexInTail(source, startIdx, tail, @"^<input\b[^>]*>",
                m => HtmlSourceAccessibilityAnalyzer.InjectAttributePreview(m.Value, $"autocomplete=\"{token}\""),
                $"Added `autocomplete=\"{token}\"` (WCAG 1.3.5 Level AA).");
        }

        if (fixId == "FIX-IFRAME-TITLE")
        {
            return ReplaceFirstRegexInTail(source, startIdx, tail, @"^<iframe\b[^>]*>",
                m => HtmlSourceAccessibilityAnalyzer.InjectAttributePreview(m.Value, "title=\"Embedded content\""),
                "Added descriptive `title` attribute to `<iframe>`.");
        }

        if (fixId == "FIX-TABINDEX-ZERO")
        {
            return ReplaceFirstRegexInTail(source, startIdx, tail, @"^<[a-z0-9-]+\b[^>]*>",
                m => Regex.Replace(m.Value, @"\btabindex\s*=\s*([""']?)\s*[1-9]\d*\1", "tabindex=\"0\"", RegexOptions.IgnoreCase),
                "Normalized positive `tabindex` to `tabindex=\"0\"`.");
        }

        if (fixId == "FIX-REMOVE-ARIA-HIDDEN")
        {
            return ReplaceFirstRegexInTail(source, startIdx, tail, @"^<(?:button|input|select|textarea|a)\b[^>]*>",
                m => Regex.Replace(m.Value, @"\s*\baria-hidden\s*=\s*[""']true[""']", "", RegexOptions.IgnoreCase),
                "Removed `aria-hidden=\"true\"` from focusable interactive element.");
        }

        if (fixId == "FIX-VIDEO-TRACK")
        {
            return ReplaceFirstRegexInTail(source, startIdx, tail, @"^<video\b[^>]*>",
                m => m.Value + "\n  <track kind=\"captions\" src=\"captions.vtt\" srclang=\"en\" label=\"English\">",
                "Inserted synchronized `<track kind=\"captions\">` element inside `<video>`.");
        }

        if (fixId.StartsWith("FIX-CSS-COLOR:", StringComparison.Ordinal))
        {
            string newHex = fixId["FIX-CSS-COLOR:".Length..];
            return ReplaceFirstRegexInTail(source, startIdx, tail, @"^color\s*:\s*[^;\}]+",
                _ => $"color: {newHex}",
                $"Updated foreground CSS `color` to `{newHex}` (>= 4.5:1 AA contrast).");
        }

        if (fixId == "FIX-CSS-RESTORE-OUTLINE")
        {
            return ReplaceFirstRegexInTail(source, startIdx, tail, @"^outline\s*:\s*[^;\}]+",
                _ => "outline: 3px solid currentColor; outline-offset: 2px",
                "Replaced suppressed `outline: none` with high-contrast 3px focus ring.");
        }

        return new RefactorPreviewResult(false, source, source, $"Unrecognized Guided Fix ID: {fixId}");
    }

    private static RefactorPreviewResult ReplaceFirstRegexInTail(
        string fullSource,
        int startIdx,
        string tail,
        string pattern,
        MatchEvaluator evaluator,
        string summary)
    {
        Regex re = new(pattern, RegexOptions.IgnoreCase);
        Match m = re.Match(tail);
        if (!m.Success)
        {
            return new RefactorPreviewResult(false, fullSource, fullSource, "Target snippet moved—press F5 to refresh line positions.");
        }

        string replacedTail = evaluator(m) + tail[(m.Index + m.Length)..];
        string updated = fullSource[..startIdx] + replacedTail;
        return new RefactorPreviewResult(true, fullSource, updated, summary);
    }

    private static int GetCharIndexFromLineAndColumn(string text, int lineOneBased, int colOneBased)
    {
        int currentLine = 1;
        int i = 0;
        while (i < text.Length && currentLine < lineOneBased)
        {
            if (text[i] == '\n')
            {
                currentLine++;
            }
            i++;
        }

        if (currentLine != lineOneBased)
        {
            return -1;
        }

        return Math.Min(text.Length - 1, i + Math.Max(0, colOneBased - 1));
    }
}
