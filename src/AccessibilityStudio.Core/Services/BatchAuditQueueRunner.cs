// ================================================================================
// FILE: Services/BatchAuditQueueRunner.cs
// PROJECT: AccessibilityStudio.Core
// LICENSE: MIT License
// ================================================================================
// PLAIN-ENGLISH OVERVIEW (100-LEVEL RATE-LIMITED BATCH & SITEMAP SCANNER GUIDE):
//   Why do enterprise & higher-education portals need a rate-limited queue?
//     Scanning 300 staging pages or university learning modules in parallel bursts
//     triggers WAF rate-limiting or degrades production response times!
//     `BatchAuditQueueRunner`:
//       1. Parses `.csv` target lists (one URL or file path per row, skipping `#`
//          comments and header rows) AND `.xml` `sitemap.xml` `<loc>` entries.
//       2. Steps through each target sequentially with a configurable polite
//          `ThrottleDelayMs` (`500ms`–`10,000ms`), supporting `CancellationToken`
//          and live `IProgress<BatchQueueProgress>` UI updates.
//       3. Accepts a pluggable `fetchHtmlAsync` delegate so:
//          - Headless CLI mode fetches public/staging pages via `HttpClient`, while
//          - WinForms IDE mode can fetch through its authenticated `WebView2` SSO
//            browser session (carrying corporate/university login cookies)!
// ================================================================================

using System.Xml.Linq;
using AccessibilityStudio.Core.Models;

namespace AccessibilityStudio.Core.Services;

public sealed record BatchQueueProgress(
    int CurrentIndex,
    int TotalCount,
    string CurrentTarget,
    FileAuditResult? LatestResult
);

public static class BatchAuditQueueRunner
{
    private static readonly HttpClient SharedHttpClient = CreateDefaultHttpClient();

    private static HttpClient CreateDefaultHttpClient()
    {
        HttpClient client = new()
        {
            Timeout = TimeSpan.FromSeconds(20)
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("RaggieSoftAccessibilityStudio/1.0 (+https://a11y.raggiesoft.com)");
        return client;
    }

    /// <summary>
    /// Parses a `.csv` schedule file or `.xml` sitemap file into a clean list of
    /// URLs or file paths.
    /// </summary>
    public static IReadOnlyList<string> LoadTargetsFromScheduleFile(string schedulePath)
    {
        string ext = Path.GetExtension(schedulePath).ToLowerInvariant();
        string raw = File.ReadAllText(schedulePath);

        if (ext == ".xml")
        {
            XDocument doc = XDocument.Parse(raw);
            return doc
                .Descendants()
                .Where(e => e.Name.LocalName.Equals("loc", StringComparison.OrdinalIgnoreCase))
                .Select(e => e.Value.Trim())
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        // CSV / plain line list: take column 1, strip quotes, skip header `"url"`/`"path"`
        List<string> items = [];
        foreach (string rawLine in raw.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            string line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#')) continue;

            string firstCol = line.Split(',')[0].Trim().Trim('"');
            if (firstCol.Equals("url", StringComparison.OrdinalIgnoreCase) ||
                firstCol.Equals("path", StringComparison.OrdinalIgnoreCase) ||
                firstCol.Equals("course_url", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(firstCol))
            {
                items.Add(firstCol);
            }
        }

        return items;
    }

    /// <summary>
    /// Executes a polite, rate-limited batch audit across <paramref name="targets"/>.
    /// </summary>
    public static async Task<WorkspaceAuditReport> RunQueueAsync(
        IReadOnlyList<string> targets,
        string queueName,
        ComplianceProfile profile,
        int throttleDelayMs = 1200,
        Func<string, CancellationToken, Task<string>>? customUrlFetcherAsync = null,
        IProgress<BatchQueueProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        DateTimeOffset started = DateTimeOffset.UtcNow;
        List<FileAuditResult> results = [];
        int clampedDelay = Math.Clamp(throttleDelayMs, 0, 30_000);

        for (int i = 0; i < targets.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string target = targets[i];
            progress?.Report(new BatchQueueProgress(i + 1, targets.Count, target, null));

            string content;
            SourceKind? explicitKind = null;

            if (target.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                target.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                explicitKind = SourceKind.Html;
                if (customUrlFetcherAsync is not null)
                {
                    content = await customUrlFetcherAsync(target, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    content = await SharedHttpClient.GetStringAsync(target, cancellationToken).ConfigureAwait(false);
                }
            }
            else
            {
                content = await File.ReadAllTextAsync(target, cancellationToken).ConfigureAwait(false);
            }

            FileAuditResult res = WorkspaceProjectService.AuditContent(content, target, profile, explicitKind);
            results.Add(res);
            progress?.Report(new BatchQueueProgress(i + 1, targets.Count, target, res));

            // Polite inter-request throttle delay (skipped after the final item)
            if (i + 1 < targets.Count && clampedDelay > 0)
            {
                await Task.Delay(clampedDelay, cancellationToken).ConfigureAwait(false);
            }
        }

        return new WorkspaceAuditReport
        {
            WorkspaceName = queueName,
            RootTarget = $"{targets.Count} queued targets ({clampedDelay}ms throttle)",
            Profile = profile,
            StartedAtUtc = started,
            CompletedAtUtc = DateTimeOffset.UtcNow,
            FileResults = results
        };
    }
}
