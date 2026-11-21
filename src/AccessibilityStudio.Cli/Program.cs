// ================================================================================
// FILE: Program.cs
// PROJECT: AccessibilityStudio.Cli (`a11y-studio-cli`)
// LICENSE: MIT License
// ================================================================================
// PLAIN-ENGLISH OVERVIEW (100-LEVEL CLI ENTRYPOINT GUIDE):
//   What does `a11y-studio-cli` do?
//     Lets CI/CD pipelines (GitHub Actions, Jenkins, Azure DevOps, or `jenna-sync.sh`)
//     audit a single file, an entire web folder, or a rate-limited `.csv`/`sitemap.xml`
//     URL schedule from the terminal and export HTML5, SARIF 2.1.0, CSV, or JSON!
//
//   Exit Codes:
//     0 = Clean pass (0 violations at or above `--fail-on` severity threshold)
//     1 = CLI usage or file-not-found error
//     2 = Blocking WCAG 2.2 / Section 508 Web violations detected
// ================================================================================

using AccessibilityStudio.Core.Documentation;
using AccessibilityStudio.Core.Models;
using AccessibilityStudio.Core.Services;
using AccessibilityStudio.Reporting.Exporters;

namespace AccessibilityStudio.Cli;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            PrintUsage();
            return 0;
        }

        string command = args[0].ToLowerInvariant();
        if (command is "--version" or "-v")
        {
            Console.WriteLine("RaggieSoft Accessibility Studio CLI v1.0.0 (https://a11y.raggiesoft.com)");
            return 0;
        }

        // Export standalone offline HTML5 Help bundle (`a11y-studio-cli export-help <output-dir>`)
        // used by `stage-installaware-payload.ps1` and air-gapped documentation builds!
        if (command == "export-help")
        {
            if (args.Length < 2 || string.IsNullOrWhiteSpace(args[1]))
            {
                Console.Error.WriteLine("Error: Expected `export-help <output-directory>` command.");
                PrintUsage();
                return 1;
            }

            string outDir = Path.GetFullPath(args[1]);
            IReadOnlyList<string> written = StudioHelpCatalog.ExportOfflineHtml5Bundle(outDir);
            Console.WriteLine($"[RaggieSoft Accessibility Studio] Exported {written.Count} offline HTML5 Help files to: {outDir}");
            return 0;
        }

        if (command != "scan" || args.Length < 2)
        {
            Console.Error.WriteLine("Error: Expected `scan <file-or-directory-or-schedule>` command.");
            PrintUsage();
            return 1;
        }

        string target = args[1];
        ComplianceProfile profile = ComplianceProfile.Wcag22AA_Section508;
        AuditSeverity failThreshold = AuditSeverity.Serious;
        int throttleMs = 1200;
        string? htmlOut = null;
        string? sarifOut = null;
        string? csvOut = null;
        string? jsonOut = null;

        // Parse optional CLI flags (`--html`, `--sarif`, `--csv`, `--json`, `--profile`, `--throttle-ms`, `--fail-on`)
        for (int i = 2; i < args.Length; i++)
        {
            string flag = args[i];
            string? next = i + 1 < args.Length ? args[i + 1] : null;
            switch (flag)
            {
                case "--html" when next is not null:
                    htmlOut = next; i++; break;
                case "--sarif" when next is not null:
                    sarifOut = next; i++; break;
                case "--csv" when next is not null:
                    csvOut = next; i++; break;
                case "--json" when next is not null:
                    jsonOut = next; i++; break;
                case "--throttle-ms" when next is not null && int.TryParse(next, out int ms):
                    throttleMs = ms; i++; break;
                case "--profile" when next is not null && Enum.TryParse(next, true, out ComplianceProfile parsedProf):
                    profile = parsedProf; i++; break;
                case "--fail-on" when next is not null && Enum.TryParse(next, true, out AuditSeverity parsedSev):
                    failThreshold = parsedSev; i++; break;
            }
        }

        WorkspaceAuditReport report;
        if (Directory.Exists(target))
        {
            report = WorkspaceProjectService.AuditDirectory(target, profile);
        }
        else if (File.Exists(target))
        {
            string ext = Path.GetExtension(target).ToLowerInvariant();
            if (ext is ".csv" or ".xml")
            {
                IReadOnlyList<string> scheduleTargets = BatchAuditQueueRunner.LoadTargetsFromScheduleFile(target);
                report = await BatchAuditQueueRunner.RunQueueAsync(
                    scheduleTargets,
                    Path.GetFileName(target),
                    profile,
                    throttleMs
                );
            }
            else
            {
                FileAuditResult single = WorkspaceProjectService.AuditFile(target, profile);
                report = new WorkspaceAuditReport
                {
                    WorkspaceName = Path.GetFileName(target),
                    RootTarget = Path.GetFullPath(target),
                    Profile = profile,
                    StartedAtUtc = single.AuditedAtUtc,
                    CompletedAtUtc = DateTimeOffset.UtcNow,
                    FileResults = [single]
                };
            }
        }
        else
        {
            Console.Error.WriteLine($"Error: Target path not found: {target}");
            return 1;
        }

        Console.WriteLine($"[RaggieSoft Accessibility Studio] Audited {report.FileResults.Count} target(s) • Score: {report.AverageHealthScore}%");
        Console.WriteLine($"  Critical: {report.TotalCritical} | Serious: {report.TotalSerious} | Moderate: {report.TotalModerate} | Advisory: {report.TotalAdvisory}");

        foreach (AuditFinding f in report.AllFindings)
        {
            Console.WriteLine($"  [{f.Severity}] {f.RuleId} @ {Path.GetFileName(f.TargetPath)}:{f.StartLine}:{f.StartColumn} — {f.Title}");
        }

        if (!string.IsNullOrEmpty(htmlOut))
        {
            EnterpriseReportExporter.SaveHtmlReport(htmlOut, report);
            Console.WriteLine($"  -> Wrote Accessible HTML5 Report: {htmlOut}");
        }
        if (!string.IsNullOrEmpty(sarifOut))
        {
            EnterpriseReportExporter.SaveSarifReport(sarifOut, report);
            Console.WriteLine($"  -> Wrote SARIF 2.1.0 Report:      {sarifOut}");
        }
        if (!string.IsNullOrEmpty(csvOut))
        {
            EnterpriseReportExporter.SaveCsvReport(csvOut, report);
            Console.WriteLine($"  -> Wrote CSV Report:              {csvOut}");
        }
        if (!string.IsNullOrEmpty(jsonOut))
        {
            EnterpriseReportExporter.SaveJsonReport(jsonOut, report);
            Console.WriteLine($"  -> Wrote JSON Report:             {jsonOut}");
        }

        bool shouldFailGate = report.AllFindings.Any(f => f.Severity <= failThreshold);
        return shouldFailGate ? 2 : 0;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("RaggieSoft Accessibility Studio CLI (v1.0.0)");
        Console.WriteLine("Usage:");
        Console.WriteLine("  a11y-studio-cli scan <folder|file|schedule.csv|sitemap.xml> [options]");
        Console.WriteLine("  a11y-studio-cli export-help <output-directory>");
        Console.WriteLine("Options:");
        Console.WriteLine("  --profile <Wcag22AA_Section508|Wcag22LevelA|AdaTitleIIWeb|StrictEnterpriseAll>");
        Console.WriteLine("  --fail-on <Critical|Serious|Moderate|Advisory>   (Default: Serious)");
        Console.WriteLine("  --throttle-ms <milliseconds>                     (Default: 1200)");
        Console.WriteLine("  --html <report.html>   Export WCAG-compliant HTML5 report");
        Console.WriteLine("  --sarif <audit.sarif>  Export OASIS SARIF 2.1.0 report");
        Console.WriteLine("  --csv <findings.csv>   Export formula-safe CSV spreadsheet");
        Console.WriteLine("  --json <audit.json>    Export structured JSON snapshot");
    }
}
