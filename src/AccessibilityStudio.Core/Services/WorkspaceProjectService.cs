// ================================================================================
// FILE: Services/WorkspaceProjectService.cs
// PROJECT: AccessibilityStudio.Core
// LICENSE: MIT License
// ================================================================================
// PLAIN-ENGLISH OVERVIEW (100-LEVEL WORKSPACE & FILE AUDIT ORCHESTRATOR):
//   What does this service do?
//     1. Determines whether a file is HTML (`.html`, `.htm`, `.astro`, `.svelte`,
//        `.php`), CSS (`.css`), or WebVTT (`.vtt`) and routes it to the matching
//        source analyzer (`HtmlSourceAccessibilityAnalyzer`, `CssAccessibilityAnalyzer`,
//        or `WebVttAccessibilityAnalyzer`)—AND even audits inline `<style>` blocks
//        embedded inside HTML files with exact line-offset translation!
//     2. Computes cryptographic SHA-256 file hashes (`SHA256.HashData`) so every
//        audit report and remediation log entry carries a verifiable fingerprint.
//     3. Loads & saves `.a11yproj` JSON workspace files and scans whole project
//        folders while skipping `node_modules/`, `.git/`, `bin/`, and `obj/`.
// ================================================================================

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AccessibilityStudio.Core.Analysis;
using AccessibilityStudio.Core.Models;

namespace AccessibilityStudio.Core.Services;

public static partial class WorkspaceProjectService
{
    private static readonly HashSet<string> IgnoredDirectoryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", "node_modules", "bin", "obj", ".vs", ".svelte-kit", ".next", ".nuxt", ".ssr-temp"
    };

    private static readonly JsonSerializerOptions PrettyJsonOptions = new()
    {
        WriteIndented = true
    };

    [GeneratedRegex(@"<style\b[^>]*>([\s\S]*?)</style>", RegexOptions.IgnoreCase)]
    private static partial Regex InlineStyleBlockRegex();

    public static SourceKind DetectSourceKind(string pathOrUrl)
    {
        string ext = Path.GetExtension(pathOrUrl).ToLowerInvariant();
        return ext switch
        {
            ".html" or ".htm" or ".xhtml" or ".astro" or ".svelte" or ".php" => SourceKind.Html,
            ".css" => SourceKind.Css,
            ".vtt" => SourceKind.WebVtt,
            ".js" or ".mjs" or ".ts" or ".tsx" => SourceKind.JavaScriptOrTypeScript,
            _ => SourceKind.Html
        };
    }

    public static string ComputeSha256Hex(string content)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(content ?? string.Empty);
        byte[] hash = SHA256.HashData(bytes);
        return Convert.ToHexStringLower(hash);
    }

    /// <summary>
    /// Audits an in-memory source buffer (either loaded from disk, edited live in
    /// Monaco Editor, or fetched from an authenticated staging/LMS URL).
    /// </summary>
    public static FileAuditResult AuditContent(
        string content,
        string targetPath,
        ComplianceProfile profile = ComplianceProfile.Wcag22AA_Section508,
        SourceKind? explicitKind = null)
    {
        string text = content ?? string.Empty;
        SourceKind kind = explicitKind ?? DetectSourceKind(targetPath);
        int totalLines = text.Length == 0 ? 0 : text.Count(c => c == '\n') + 1;

        List<AuditFinding> allFindings = [];
        IReadOnlyList<DocumentOutlineNode> outline = [];

        if (kind == SourceKind.Html)
        {
            var (htmlFindings, htmlOutline) = HtmlSourceAccessibilityAnalyzer.Analyze(text, targetPath, profile);
            allFindings.AddRange(htmlFindings);
            outline = htmlOutline;

            // Also inspect inline `<style>...</style>` blocks inside HTML files, shifting line numbers
            // so clicking a CSS contrast error inside `<style>` jumps to the exact HTML line!
            int[] htmlLineOffsets = HtmlSourceAccessibilityAnalyzer.BuildLineStartOffsets(text);
            foreach (Match styleMatch in InlineStyleBlockRegex().Matches(text))
            {
                string cssChunk = styleMatch.Groups[1].Value;
                (int baseLine, _) = HtmlSourceAccessibilityAnalyzer.GetLineAndColumn(htmlLineOffsets, styleMatch.Groups[1].Index);
                foreach (AuditFinding cssFinding in CssAccessibilityAnalyzer.Analyze(cssChunk, targetPath, profile))
                {
                    if (cssFinding.RuleId == "WCAG-CSS-REDUCED-MOTION") continue;
                    allFindings.Add(cssFinding with
                    {
                        StartLine = baseLine + cssFinding.StartLine - 1,
                        EndLine = baseLine + cssFinding.EndLine - 1
                    });
                }
            }
        }
        else if (kind == SourceKind.Css)
        {
            allFindings.AddRange(CssAccessibilityAnalyzer.Analyze(text, targetPath, profile));
        }
        else if (kind == SourceKind.WebVtt)
        {
            allFindings.AddRange(WebVttAccessibilityAnalyzer.Analyze(text, targetPath));
        }

        // Apply ComplianceProfile filter
        List<AuditFinding> filtered = allFindings
            .Where(f => MatchesProfile(f, profile))
            .OrderBy(f => f.StartLine)
            .ThenBy(f => f.StartColumn)
            .ToList();

        return new FileAuditResult
        {
            TargetPath = targetPath,
            SourceKind = kind,
            Sha256Hash = ComputeSha256Hex(text),
            TotalLines = totalLines,
            AuditedAtUtc = DateTimeOffset.UtcNow,
            Findings = filtered,
            Outline = outline
        };
    }

    public static FileAuditResult AuditFile(string filePath, ComplianceProfile profile = ComplianceProfile.Wcag22AA_Section508)
    {
        string fullPath = Path.GetFullPath(filePath);
        string content = File.ReadAllText(fullPath, Encoding.UTF8);
        return AuditContent(content, fullPath, profile);
    }

    /// <summary>
    /// Recursively discovers auditable web source files (`*.html`, `*.css`, `*.vtt`, etc.)
    /// inside a workspace folder while skipping build/dependency directories.
    /// </summary>
    public static IReadOnlyList<string> DiscoverAuditableFiles(
        string directoryPath,
        IReadOnlyCollection<string>? allowedExtensions = null)
    {
        HashSet<string> exts = new(
            allowedExtensions ?? [".html", ".htm", ".css", ".vtt", ".astro", ".svelte"],
            StringComparer.OrdinalIgnoreCase
        );

        List<string> results = [];
        void Walk(string dir)
        {
            string name = Path.GetFileName(dir);
            if (!string.IsNullOrEmpty(name) && IgnoredDirectoryNames.Contains(name))
            {
                return;
            }

            foreach (string file in Directory.EnumerateFiles(dir))
            {
                if (exts.Contains(Path.GetExtension(file)))
                {
                    results.Add(Path.GetFullPath(file));
                }
            }

            foreach (string sub in Directory.EnumerateDirectories(dir))
            {
                Walk(sub);
            }
        }

        if (Directory.Exists(directoryPath))
        {
            Walk(Path.GetFullPath(directoryPath));
        }

        results.Sort(StringComparer.OrdinalIgnoreCase);
        return results;
    }

    public static WorkspaceAuditReport AuditDirectory(
        string directoryPath,
        ComplianceProfile profile = ComplianceProfile.Wcag22AA_Section508,
        string? workspaceName = null)
    {
        DateTimeOffset started = DateTimeOffset.UtcNow;
        IReadOnlyList<string> files = DiscoverAuditableFiles(directoryPath);
        List<FileAuditResult> fileResults = files.Select(f => AuditFile(f, profile)).ToList();

        return new WorkspaceAuditReport
        {
            WorkspaceName = workspaceName ?? Path.GetFileName(Path.TrimEndingDirectorySeparator(Path.GetFullPath(directoryPath))),
            RootTarget = Path.GetFullPath(directoryPath),
            Profile = profile,
            StartedAtUtc = started,
            CompletedAtUtc = DateTimeOffset.UtcNow,
            FileResults = fileResults
        };
    }

    public static void SaveProjectFile(string filePath, AccessibilityWorkspaceProject project)
    {
        string json = JsonSerializer.Serialize(project, PrettyJsonOptions);
        File.WriteAllText(filePath, json, Encoding.UTF8);
    }

    public static AccessibilityWorkspaceProject LoadProjectFile(string filePath)
    {
        string json = File.ReadAllText(filePath, Encoding.UTF8);
        return JsonSerializer.Deserialize<AccessibilityWorkspaceProject>(json, PrettyJsonOptions)
               ?? new AccessibilityWorkspaceProject();
    }

    private static bool MatchesProfile(AuditFinding f, ComplianceProfile profile) =>
        profile switch
        {
            ComplianceProfile.Wcag22LevelA => f.WcagLevel == "A",
            ComplianceProfile.Wcag22AA_Section508 => f.WcagLevel is "A" or "AA",
            ComplianceProfile.AdaTitleIIWeb => f.WcagLevel is "A" or "AA",
            ComplianceProfile.StrictEnterpriseAll => true,
            _ => true
        };
}
