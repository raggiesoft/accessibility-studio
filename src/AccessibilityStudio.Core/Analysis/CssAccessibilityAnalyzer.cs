// ================================================================================
// FILE: Analysis/CssAccessibilityAnalyzer.cs
// PROJECT: AccessibilityStudio.Core
// LICENSE: MIT License
// ================================================================================
// PLAIN-ENGLISH OVERVIEW (100-LEVEL CSS ACCESSIBILITY SCANNER GUIDE):
//   What does this analyzer catch inside `.css` files and `<style>` blocks?
//     1. `WCAG-CSS-CONTRAST` (WCAG 1.4.3 Level AA):
//        When a CSS rule block sets both `color` and `background-color` (or
//        `background`), we parse both colors via `ColorContrastEngine`, compute
//        the exact relative luminance ratio, flag `< 4.5:1` pairs, AND calculate
//        the nearest passing hex replacement for one-click Guided Refactoring!
//     2. `WCAG-CSS-FOCUS-SUPPRESSED` (WCAG 2.4.7 Level AA):
//        Flags `outline: none` / `outline: 0` on `:focus` or interactive rules
//        when no replacement `outline`, `border`, or `box-shadow` focus indicator
//        is provided in that rule block.
//     3. `WCAG-CSS-REDUCED-MOTION` (WCAG 2.3.3 / Best Practice):
//        Flags stylesheets containing `@keyframes` or `animation:` without any
//        `@media (prefers-reduced-motion: reduce)` safeguard.
// ================================================================================

using System.Text.RegularExpressions;
using AccessibilityStudio.Core.Models;

namespace AccessibilityStudio.Core.Analysis;

public static partial class CssAccessibilityAnalyzer
{
    [GeneratedRegex(@"/\*[\s\S]*?\*/")]
    private static partial Regex CssCommentRegex();

    [GeneratedRegex(@"([^{}/]+)\{([^{}]*)\}")]
    private static partial Regex CssRuleBlockRegex();

    [GeneratedRegex(@"(?:^|;)\s*([a-zA-Z-]+)\s*:\s*([^;]+)", RegexOptions.Multiline)]
    private static partial Regex CssDeclarationRegex();

    public static IReadOnlyList<AuditFinding> Analyze(
        string cssSource,
        string targetPath,
        ComplianceProfile profile = ComplianceProfile.Wcag22AA_Section508)
    {
        List<AuditFinding> findings = [];
        if (string.IsNullOrWhiteSpace(cssSource))
        {
            return findings;
        }

        int[] lineOffsets = HtmlSourceAccessibilityAnalyzer.BuildLineStartOffsets(cssSource);

        // Mask `/* ... */` comments with spaces while keeping `\n` line numbers intact
        char[] buf = cssSource.ToCharArray();
        foreach (Match cm in CssCommentRegex().Matches(cssSource))
        {
            for (int i = cm.Index; i < cm.Index + cm.Length; i++)
            {
                if (buf[i] != '\n' && buf[i] != '\r')
                {
                    buf[i] = ' ';
                }
            }
        }
        string masked = new(buf);

        bool hasAnimations = false;

        foreach (Match block in CssRuleBlockRegex().Matches(masked))
        {
            string selector = block.Groups[1].Value.Trim();
            string declarations = block.Groups[2].Value;
            int declStart = block.Groups[2].Index;

            if (selector.StartsWith('@'))
            {
                continue;
            }

            Dictionary<string, (string Value, int AbsIndex, int Length)> props = new(StringComparer.OrdinalIgnoreCase);
            foreach (Match decl in CssDeclarationRegex().Matches(declarations))
            {
                string propName = decl.Groups[1].Value.Trim();
                string propVal = decl.Groups[2].Value.Trim();
                props[propName] = (propVal, declStart + decl.Groups[1].Index, decl.Length);

                if (propName.Equals("animation", StringComparison.OrdinalIgnoreCase) &&
                    !propVal.StartsWith("none", StringComparison.OrdinalIgnoreCase))
                {
                    hasAnimations = true;
                }
            }

            // 1. Check Foreground / Background Contrast Ratio (WCAG 1.4.3 Level AA)
            string? bgRaw = props.TryGetValue("background-color", out var bgProp) ? bgProp.Value
                : props.TryGetValue("background", out var bgShort) ? bgShort.Value
                : null;

            if (props.TryGetValue("color", out var fgProp) &&
                bgRaw is not null &&
                ColorContrastEngine.TryParseCssColor(fgProp.Value, out RgbColor fg) &&
                ColorContrastEngine.TryParseCssColor(bgRaw, out RgbColor bg))
            {
                ContrastEvaluation eval = ColorContrastEngine.Evaluate(fg, bg);
                if (!eval.PassesNormalAA)
                {
                    RgbColor suggested = ColorContrastEngine.SuggestNearestPassingForeground(fg, bg, 4.55);
                    double newRatio = Math.Round(ColorContrastEngine.GetContrastRatio(suggested, bg), 2);
                    (int sLine, int sCol) = HtmlSourceAccessibilityAnalyzer.GetLineAndColumn(lineOffsets, fgProp.AbsIndex);
                    (int eLine, int eCol) = HtmlSourceAccessibilityAnalyzer.GetLineAndColumn(lineOffsets, fgProp.AbsIndex + fgProp.Value.Length + 6);

                    findings.Add(new AuditFinding
                    {
                        RuleId = "WCAG-CSS-CONTRAST",
                        Title = $"Insufficient CSS text contrast ({eval.Ratio:F2}:1 < 4.5:1 AA)",
                        Severity = AuditSeverity.Serious,
                        WcagCriterion = "1.4.3 Contrast (Minimum)",
                        WcagLevel = "AA",
                        Section508Reference = "Revised 508 E205.4 (WCAG 1.4.3)",
                        TargetPath = targetPath,
                        StartLine = sLine,
                        StartColumn = sCol,
                        EndLine = eLine,
                        EndColumn = eCol,
                        OffendingSnippet = $"{selector} {{ color: {fgProp.Value}; background: {bgRaw}; }}",
                        Message = $"Selector `{selector}` pairs foreground `{fg.ToHex()}` on background `{bg.ToHex()}` with a contrast ratio of `{eval.Ratio:F2}:1`, failing the WCAG 1.4.3 `4.5:1` Level AA minimum for normal text.",
                        RemediationGuidance = $"Change `color` to `{suggested.ToHex()}` (achieves `{newRatio:F2}:1` contrast while preserving hue tone).",
                        DomSelectorHint = selector,
                        GuidedFixId = $"FIX-CSS-COLOR:{suggested.ToHex()}",
                        SuggestedReplacementPreview = $"color: {suggested.ToHex()};"
                    });
                }
            }

            // 2. Check Suppressed Focus Indicator (`outline: none` / `outline: 0` without replacement)
            if (props.TryGetValue("outline", out var outlineProp))
            {
                string valLower = outlineProp.Value.ToLowerInvariant();
                bool removesOutline = valLower == "none" || valLower == "0" || valLower.StartsWith("0px", StringComparison.Ordinal);
                bool hasReplacement = props.ContainsKey("box-shadow") || props.ContainsKey("border") || props.ContainsKey("border-color");
                // Note: `.fl-main:focus { outline: none }` on `<main tabindex="-1">` is common, whereas `:focus`, `button`, `a`, `input`, `*` are interactive!
                bool targetsMainContainerOnly = selector.Contains("main:focus", StringComparison.OrdinalIgnoreCase);

                if (removesOutline && !hasReplacement && !targetsMainContainerOnly)
                {
                    (int sLine, int sCol) = HtmlSourceAccessibilityAnalyzer.GetLineAndColumn(lineOffsets, outlineProp.AbsIndex);
                    (int eLine, int eCol) = HtmlSourceAccessibilityAnalyzer.GetLineAndColumn(lineOffsets, outlineProp.AbsIndex + outlineProp.Length);

                    findings.Add(new AuditFinding
                    {
                        RuleId = "WCAG-CSS-FOCUS-SUPPRESSED",
                        Title = "Browser keyboard focus indicator suppressed (`outline: none`) without replacement",
                        Severity = AuditSeverity.Serious,
                        WcagCriterion = "2.4.7 Focus Visible",
                        WcagLevel = "AA",
                        Section508Reference = "Revised 508 E205.4 (WCAG 2.4.7 / 1.4.11)",
                        TargetPath = targetPath,
                        StartLine = sLine,
                        StartColumn = sCol,
                        EndLine = eLine,
                        EndColumn = eCol,
                        OffendingSnippet = $"{selector} {{ outline: {outlineProp.Value}; }}",
                        Message = $"Rule `{selector}` removes the browser's focus ring (`outline: {outlineProp.Value}`) without providing a high-contrast replacement, leaving sighted keyboard users unable to see where focus is.",
                        RemediationGuidance = "Use `:focus-visible { outline: 3px solid currentColor; outline-offset: 2px; }` instead of removing focus outlines.",
                        DomSelectorHint = selector,
                        GuidedFixId = "FIX-CSS-RESTORE-OUTLINE",
                        SuggestedReplacementPreview = "outline: 3px solid currentColor; outline-offset: 2px"
                    });
                }
            }
        }

        // 3. Check `prefers-reduced-motion` when animations or `@keyframes` exist
        if (profile != ComplianceProfile.Wcag22LevelA &&
            (hasAnimations || masked.Contains("@keyframes", StringComparison.OrdinalIgnoreCase)) &&
            !masked.Contains("prefers-reduced-motion", StringComparison.OrdinalIgnoreCase))
        {
            findings.Add(new AuditFinding
            {
                RuleId = "WCAG-CSS-REDUCED-MOTION",
                Title = "CSS animations lack `@media (prefers-reduced-motion: reduce)` safeguard",
                Severity = AuditSeverity.Advisory,
                WcagCriterion = "2.3.3 Animation from Interactions",
                WcagLevel = "Advisory",
                Section508Reference = "Revised 508 / Vestibular Accessibility Best Practice",
                TargetPath = targetPath,
                StartLine = 1,
                StartColumn = 1,
                EndLine = 1,
                EndColumn = 1,
                OffendingSnippet = "@keyframes / animation without prefers-reduced-motion",
                Message = "This stylesheet defines CSS animations without a `@media (prefers-reduced-motion: reduce)` rule to calm motion for users with vestibular sensitivity.",
                RemediationGuidance = "Append `@media (prefers-reduced-motion: reduce) { *, *::before, *::after { animation-duration: 0.01ms !important; transition-duration: 0.01ms !important; } }`.",
                GuidedFixId = "FIX-CSS-APPEND-REDUCED-MOTION",
                SuggestedReplacementPreview = "@media (prefers-reduced-motion: reduce) { *, *::before, *::after { animation-duration: 0.01ms !important; transition-duration: 0.01ms !important; } }"
            });
        }

        return findings;
    }
}
