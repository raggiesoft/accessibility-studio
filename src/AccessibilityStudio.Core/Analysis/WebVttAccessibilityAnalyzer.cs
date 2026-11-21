// ================================================================================
// FILE: Analysis/WebVttAccessibilityAnalyzer.cs
// PROJECT: AccessibilityStudio.Core
// LICENSE: MIT License
// ================================================================================
// PLAIN-ENGLISH OVERVIEW (100-LEVEL WEBVTT CLOSED CAPTION VALIDATOR GUIDE):
//   What is a `.vtt` (WebVTT) file?
//     WebVTT ("Web Video Text Tracks") is the W3C standard text file format used
//     by `<track kind="captions" src="...vtt">` to display synchronized closed
//     captions on `<video>` and `<audio>` players (WCAG 1.2.2 Level A & Section 508).
//
//   What does this validator check?
//     1. `WCAG-VTT-HEADER`   : Line 1 must begin with `WEBVTT`.
//     2. `WCAG-VTT-TIMING`   : Cue `end` timestamp must be strictly greater than `start`.
//     3. `WCAG-VTT-OVERLAP`  : Adjacent cues should not overlap unintendedly out of order.
//     4. `WCAG-VTT-EMPTY`    : Timing line with zero caption text underneath it.
//     5. `WCAG-VTT-CPS`      : Caption reading speed exceeding `25 chars/sec` (hard for
//                              Deaf, hard-of-hearing, and ESL viewers to read in time).
// ================================================================================

using System.Globalization;
using System.Text.RegularExpressions;
using AccessibilityStudio.Core.Models;

namespace AccessibilityStudio.Core.Analysis;

public static partial class WebVttAccessibilityAnalyzer
{
    [GeneratedRegex(@"^(?:(\d{2,}):)?(\d{2}):(\d{2})\.(\d{3})\s*-->\s*(?:(\d{2,}):)?(\d{2}):(\d{2})\.(\d{3})")]
    private static partial Regex CueTimingLineRegex();

    public static IReadOnlyList<AuditFinding> Analyze(string vttSource, string targetPath)
    {
        List<AuditFinding> findings = [];
        string normalized = (vttSource ?? string.Empty).Replace("\r\n", "\n");
        string[] lines = normalized.Split('\n');

        if (lines.Length == 0 || !lines[0].TrimStart('\uFEFF').StartsWith("WEBVTT", StringComparison.Ordinal))
        {
            findings.Add(new AuditFinding
            {
                RuleId = "WCAG-VTT-HEADER",
                Title = "Missing mandatory `WEBVTT` header on Line 1",
                Severity = AuditSeverity.Critical,
                WcagCriterion = "1.2.2 Captions (Prerecorded)",
                WcagLevel = "A",
                Section508Reference = "Revised 508 503.4 / W3C WebVTT",
                TargetPath = targetPath,
                StartLine = 1,
                StartColumn = 1,
                EndLine = 1,
                EndColumn = Math.Max(1, lines.FirstOrDefault()?.Length ?? 1),
                OffendingSnippet = lines.FirstOrDefault() ?? "(empty file)",
                Message = "Browsers reject `.vtt` caption tracks entirely unless Line 1 starts with the exact uppercase signature `WEBVTT`.",
                RemediationGuidance = "Insert `WEBVTT` followed by a blank line at the very top of the file.",
                GuidedFixId = "FIX-VTT-HEADER",
                SuggestedReplacementPreview = "WEBVTT\n\n"
            });
        }

        double previousStartSec = -1;
        for (int i = 0; i < lines.Length; i++)
        {
            string trimmed = lines[i].Trim();
            Match m = CueTimingLineRegex().Match(trimmed);
            if (!m.Success) continue;

            int cueLineNumber = i + 1;
            double startSec = ParseSeconds(m.Groups[1].Value, m.Groups[2].Value, m.Groups[3].Value, m.Groups[4].Value);
            double endSec = ParseSeconds(m.Groups[5].Value, m.Groups[6].Value, m.Groups[7].Value, m.Groups[8].Value);

            if (endSec <= startSec)
            {
                findings.Add(new AuditFinding
                {
                    RuleId = "WCAG-VTT-TIMING",
                    Title = "WebVTT cue end timestamp is not greater than start timestamp",
                    Severity = AuditSeverity.Critical,
                    WcagCriterion = "1.2.2 Captions (Prerecorded)",
                    WcagLevel = "A",
                    Section508Reference = "Revised 508 503.4 (Synchronized Captions)",
                    TargetPath = targetPath,
                    StartLine = cueLineNumber,
                    StartColumn = 1,
                    EndLine = cueLineNumber,
                    EndColumn = lines[i].Length + 1,
                    OffendingSnippet = trimmed,
                    Message = $"Cue `{trimmed}` ends (`{endSec:F3}s`) at or before its start time (`{startSec:F3}s`), so the caption never appears on screen.",
                    RemediationGuidance = "Ensure the cue end timestamp occurs after the start timestamp."
                });
            }

            if (previousStartSec >= 0 && startSec < previousStartSec)
            {
                findings.Add(new AuditFinding
                {
                    RuleId = "WCAG-VTT-ORDER",
                    Title = "WebVTT cue start timestamp is out of chronological order",
                    Severity = AuditSeverity.Serious,
                    WcagCriterion = "1.2.2 Captions (Prerecorded)",
                    WcagLevel = "A",
                    Section508Reference = "W3C WebVTT Specification",
                    TargetPath = targetPath,
                    StartLine = cueLineNumber,
                    StartColumn = 1,
                    EndLine = cueLineNumber,
                    EndColumn = lines[i].Length + 1,
                    OffendingSnippet = trimmed,
                    Message = "WebVTT requires cue start times to appear in non-decreasing chronological order; out-of-order cues may be dropped by HTML5 media players.",
                    RemediationGuidance = "Sort cues in chronological order by start timestamp."
                });
            }
            previousStartSec = startSec;

            // Gather caption payload lines until next blank line
            List<string> payloadLines = [];
            int j = i + 1;
            while (j < lines.Length && !string.IsNullOrWhiteSpace(lines[j]))
            {
                payloadLines.Add(lines[j].Trim());
                j++;
            }

            string captionText = Regex.Replace(string.Join(" ", payloadLines), @"<[^>]+>", "").Trim();
            if (string.IsNullOrWhiteSpace(captionText))
            {
                findings.Add(new AuditFinding
                {
                    RuleId = "WCAG-VTT-EMPTY",
                    Title = "Empty WebVTT cue payload",
                    Severity = AuditSeverity.Moderate,
                    WcagCriterion = "1.2.2 Captions (Prerecorded)",
                    WcagLevel = "A",
                    Section508Reference = "Revised 508 503.4",
                    TargetPath = targetPath,
                    StartLine = cueLineNumber,
                    StartColumn = 1,
                    EndLine = cueLineNumber,
                    EndColumn = lines[i].Length + 1,
                    OffendingSnippet = trimmed,
                    Message = "This timestamp cue has no spoken text or sound description beneath it.",
                    RemediationGuidance = "Add the spoken caption text beneath the timing line or remove the empty cue."
                });
            }
            else if (endSec > startSec)
            {
                double duration = endSec - startSec;
                double cps = captionText.Length / duration;
                if (duration >= 0.4 && cps > 25.0)
                {
                    findings.Add(new AuditFinding
                    {
                        RuleId = "WCAG-VTT-CPS",
                        Title = $"Caption reading speed too fast ({cps:F1} chars/sec > 25 CPS)",
                        Severity = AuditSeverity.Advisory,
                        WcagCriterion = "1.2.2 Captions (Prerecorded)",
                        WcagLevel = "Advisory",
                        Section508Reference = "DCMP / Broadcast Caption Readability Guideline",
                        TargetPath = targetPath,
                        StartLine = cueLineNumber,
                        StartColumn = 1,
                        EndLine = cueLineNumber,
                        EndColumn = lines[i].Length + 1,
                        OffendingSnippet = $"{trimmed} ({captionText})",
                        Message = $"This cue flashes `{captionText.Length}` characters in `{duration:F2}s` (`{cps:F1} CPS`), exceeding comfortable caption reading speed (`<= 21–25 CPS`).",
                        RemediationGuidance = "Extend the cue end timestamp during vocal pauses or split long sentences across two cues."
                    });
                }
            }
        }

        return findings;
    }

    private static double ParseSeconds(string hours, string minutes, string seconds, string millis)
    {
        int h = string.IsNullOrEmpty(hours) ? 0 : int.Parse(hours, CultureInfo.InvariantCulture);
        int m = int.Parse(minutes, CultureInfo.InvariantCulture);
        int s = int.Parse(seconds, CultureInfo.InvariantCulture);
        int ms = int.Parse(millis, CultureInfo.InvariantCulture);
        return (h * 3600.0) + (m * 60.0) + s + (ms / 1000.0);
    }
}
