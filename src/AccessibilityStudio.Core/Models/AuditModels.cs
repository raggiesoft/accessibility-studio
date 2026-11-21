// ================================================================================
// FILE: Models/AuditModels.cs
// PROJECT: AccessibilityStudio.Core
// LICENSE: MIT License
// ================================================================================
// PLAIN-ENGLISH OVERVIEW (100-LEVEL C# DATA MODELING GUIDE):
//   What is this file?
//     Before our analyzer can inspect HTML5, CSS, or WebVTT files—or display
//     findings inside our WinForms Error List grid and SARIF/HTML5 reports—every
//     part of the program needs to agree on the exact shape of an "Audit Finding",
//     a "Document Outline Node", a "Project Workspace", and a "Session Log Entry".
//
//   C# Concepts Taught Here:
//     1. `enum` (Enumeration): A named set of fixed choices (like `AuditSeverity`)
//        so nobody can accidentally type `"Critcal"` as a misspelled string!
//     2. `sealed record` & `sealed class`:
//        - `record` types in modern C# automatically generate value-based equality
//          and clean immutable data carriers (`init;` properties can be set when
//          creating the object, then locked against accidental mutation!).
//        - `sealed` tells the .NET JIT compiler no subclass will inherit from this
//          type, enabling faster method calls and clearer design intent.
// ================================================================================

using System.Text.Json.Serialization;

namespace AccessibilityStudio.Core.Models;

/// <summary>
/// Categorizes how severely an accessibility issue blocks users of assistive
/// technologies (screen readers like JAWS/NVDA/VoiceOver, keyboard-only navigation,
/// screen magnifiers, or Windows High-Contrast themes).
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AuditSeverity
{
    /// <summary>Blocks keyboard or screen-reader access outright (e.g. missing form label, focus trap, missing alt).</summary>
    Critical = 0,

    /// <summary>Causes major barriers (e.g. insufficient &lt;4.5:1 text contrast, fake bold table headers, suppressed focus ring).</summary>
    Serious = 1,

    /// <summary>Degrades structural navigation (e.g. skipped heading level H1 -> H4, unlabeled duplicate nav landmark).</summary>
    Moderate = 2,

    /// <summary>Best-practice improvement (e.g. missing prefers-reduced-motion override on CSS keyframe animations).</summary>
    Advisory = 3
}

/// <summary>
/// Compliance profile filter used in the IDE toolbar, GPO MSI policy defaults,
/// and CLI `--profile` flag.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ComplianceProfile
{
    /// <summary>WCAG 2.2 Level A + Level AA + Revised Section 508 Web Criteria (Enterprise Default).</summary>
    Wcag22AA_Section508 = 0,

    /// <summary>Minimum WCAG 2.2 Level A baseline checks only.</summary>
    Wcag22LevelA = 1,

    /// <summary>ADA Title II Public-Sector &amp; Higher-Education Web Mandate (WCAG 2.1/2.2 AA + Captions + Contrast).</summary>
    AdaTitleIIWeb = 2,

    /// <summary>All rules including AAA &amp; modern CSS resilience advisories.</summary>
    StrictEnterpriseAll = 3
}

/// <summary>
/// Identifies the source language of an audited file so the Monaco syntax editor
/// and rule analyzers pick the right grammar and refactoring strategy.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SourceKind
{
    Html = 0,
    Css = 1,
    WebVtt = 2,
    JavaScriptOrTypeScript = 3,
    Unknown = 4
}

/// <summary>
/// Represents a single source-level WCAG 2.1/2.2 &amp; Revised Section 508 Web finding
/// with exact 1-based Line and Column coordinates so double-clicking a row in the
/// IDE Error List jumps the cursor straight to the offending markup!
/// </summary>
public sealed record AuditFinding
{
    // Unique stable rule identifier (e.g. `"WCAG-TABLE-FAKE-HEADER"` or `"WCAG-CSS-CONTRAST"`).
    public required string RuleId { get; init; }

    // Human-readable short rule title shown in summaries and SARIF rule descriptors.
    public required string Title { get; init; }

    // Severity tier (`Critical`, `Serious`, `Moderate`, or `Advisory`).
    public required AuditSeverity Severity { get; init; }

    // WCAG 2.1 / 2.2 Success Criterion citation (e.g. `"1.3.1 Info and Relationships (Level A)"`).
    public required string WcagCriterion { get; init; }

    // WCAG conformance level (`"A"`, `"AA"`, or `"Advisory"`).
    public required string WcagLevel { get; init; }

    // U.S. Revised Section 508 Web / ADA Title II reference (e.g. `"508 E205.4 / 36 CFR 1194"`).
    public required string Section508Reference { get; init; }

    // Full path or URL of the audited file/document.
    public required string TargetPath { get; init; }

    // 1-based line and column numbers where the issue starts and ends in source text.
    public required int StartLine { get; init; }
    public required int StartColumn { get; init; }
    public required int EndLine { get; init; }
    public required int EndColumn { get; init; }

    // Exact snippet of offending source code (e.g. `<td style="font-weight: bold;">Course</td>`).
    public required string OffendingSnippet { get; init; }

    // Clear, developer-focused explanation of what failed and how assistive tech experiences it.
    public required string Message { get; init; }

    // Step-by-step remediation guidance explaining how to fix the code cleanly.
    public required string RemediationGuidance { get; init; }

    // Optional CSS selector hint used by the Live Chromium (`WebView2`) Inspector to draw a highlight box!
    public string? DomSelectorHint { get; init; }

    // Optional identifier of an automated Guided Fix supported by `GuidedSourceRefactorer`.
    public string? GuidedFixId { get; init; }

    // Optional preview of what the refactored snippet will look like after applying the Guided Fix.
    public string? SuggestedReplacementPreview { get; init; }

    // Convenience property formatted as `"42:9"` for the IDE Error List DataGridView.
    [JsonIgnore]
    public string LocationDisplay => $"{StartLine}:{StartColumn}";

    // Convenience property showing `"Yes"` or `"Manual"` in the IDE Error List column.
    [JsonIgnore]
    public string HasGuidedFixDisplay => !string.IsNullOrEmpty(GuidedFixId) ? "⚡ Guided Fix" : "Manual Review";
}

/// <summary>
/// Represents a structural landmark (<c>&lt;header&gt;</c>, <c>&lt;nav&gt;</c>, <c>&lt;main&gt;</c>, <c>&lt;footer&gt;</c>) or
/// heading (<c>&lt;h1&gt;</c>–<c>&lt;h6&gt;</c>) extracted from an HTML file to populate the IDE's
/// Screen-Reader Outline &amp; Landmark Tree panel.
/// </summary>
public sealed record DocumentOutlineNode
{
    public required string Kind { get; init; }        // `"Heading"` or `"Landmark"`
    public required string TagOrRole { get; init; }   // e.g. `"h1"`, `"h2"`, `"nav"`, `"main"`
    public required string Label { get; init; }       // Visible heading text or `aria-label`
    public required int Line { get; init; }
    public required int HeadingLevel { get; init; }   // `1..6` for headings, `0` for landmarks
    public bool HasWarning { get; init; }
    public string? WarningNote { get; init; }
}

/// <summary>
/// Complete result of auditing a single file or live URL.
/// </summary>
public sealed class FileAuditResult
{
    public required string TargetPath { get; init; }
    public required SourceKind SourceKind { get; init; }
    public required string Sha256Hash { get; init; }
    public required int TotalLines { get; init; }
    public required DateTimeOffset AuditedAtUtc { get; init; }
    public required IReadOnlyList<AuditFinding> Findings { get; init; }
    public required IReadOnlyList<DocumentOutlineNode> Outline { get; init; }

    // Summary counters computed via LINQ `.Count(...)`
    [JsonIgnore]
    public int CriticalCount => Findings.Count(f => f.Severity == AuditSeverity.Critical);

    [JsonIgnore]
    public int SeriousCount => Findings.Count(f => f.Severity == AuditSeverity.Serious);

    [JsonIgnore]
    public int ModerateCount => Findings.Count(f => f.Severity == AuditSeverity.Moderate);

    [JsonIgnore]
    public int AdvisoryCount => Findings.Count(f => f.Severity == AuditSeverity.Advisory);

    /// <summary>
    /// Computes an engineering health score (`0` to `100`) weighted by violation severity.
    /// Note: Even a `100` automated score is paired with manual keyboard/screen-reader verification!
    /// </summary>
    [JsonIgnore]
    public int HealthScore
    {
        get
        {
            int penalty = (CriticalCount * 18) + (SeriousCount * 10) + (ModerateCount * 4) + (AdvisoryCount * 1);
            return Math.Clamp(100 - penalty, 0, 100);
        }
    }
}

/// <summary>
/// Aggregated multi-file or multi-URL workspace audit run ready for exporting to
/// Accessible HTML5, SARIF 2.1.0, CSV, JSON, and Revised Section 508 Conformance Tables.
/// </summary>
public sealed class WorkspaceAuditReport
{
    public required string WorkspaceName { get; init; }
    public required string RootTarget { get; init; }
    public required ComplianceProfile Profile { get; init; }
    public required DateTimeOffset StartedAtUtc { get; init; }
    public required DateTimeOffset CompletedAtUtc { get; init; }
    public required IReadOnlyList<FileAuditResult> FileResults { get; init; }

    [JsonIgnore]
    public IReadOnlyList<AuditFinding> AllFindings =>
        FileResults.SelectMany(r => r.Findings).ToList();

    [JsonIgnore]
    public int TotalCritical => FileResults.Sum(r => r.CriticalCount);

    [JsonIgnore]
    public int TotalSerious => FileResults.Sum(r => r.SeriousCount);

    [JsonIgnore]
    public int TotalModerate => FileResults.Sum(r => r.ModerateCount);

    [JsonIgnore]
    public int TotalAdvisory => FileResults.Sum(r => r.AdvisoryCount);

    [JsonIgnore]
    public int AverageHealthScore =>
        FileResults.Count == 0 ? 100 : (int)Math.Round(FileResults.Average(r => r.HealthScore));
}

/// <summary>
/// Saved `.a11yproj` Enterprise Workspace Project file format (registered in Windows
/// Explorer by InstallAware Studio X18 MSI `ProgId` tables).
/// </summary>
public sealed class AccessibilityWorkspaceProject
{
    public string SchemaVersion { get; set; } = "1.0";
    public string ProjectName { get; set; } = "Untitled Accessibility Audit";
    public string RootDirectory { get; set; } = string.Empty;
    public ComplianceProfile ComplianceProfile { get; set; } = ComplianceProfile.Wcag22AA_Section508;
    public int BatchThrottleDelayMs { get; set; } = 1500;
    public List<string> IncludedExtensions { get; set; } = [".html", ".htm", ".css", ".vtt", ".astro", ".svelte"];
    public List<string> BatchUrls { get; set; } = [];
}

/// <summary>
/// Tamper-evident audit &amp; remediation log entry recording SHA-256 hashes before
/// and after guided source-code fixes.
/// </summary>
public sealed record AuditLogEntry
{
    public required DateTimeOffset TimestampUtc { get; init; }
    public required string ActionType { get; init; }
    public required string TargetPath { get; init; }
    public required string Summary { get; init; }
    public string? BeforeSha256 { get; init; }
    public string? AfterSha256 { get; init; }
}
