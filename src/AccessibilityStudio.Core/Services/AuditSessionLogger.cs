// ================================================================================
// FILE: Services/AuditSessionLogger.cs
// PROJECT: AccessibilityStudio.Core
// LICENSE: MIT License
// ================================================================================
// PLAIN-ENGLISH OVERVIEW (100-LEVEL AUDIT TRAIL & LOGGING GUIDE):
//   Why do enterprise compliance engineers need a structured session log?
//     When auditing a university course shell, municipal portal, or enterprise web
//     release, engineers need an auditable record of WHEN each file was scanned
//     and WHICH guided fixes modified source files (with before/after SHA-256
//     cryptographic digests). `AuditSessionLogger` maintains an in-memory thread-safe
//     log for the IDE's "Audit & Remediation Session Log" tab AND exports JSONL
//     (`.jsonl`) or plain-text `.log` transcripts on demand.
// ================================================================================

using System.Text;
using System.Text.Json;
using AccessibilityStudio.Core.Models;

namespace AccessibilityStudio.Core.Services;

public sealed class AuditSessionLogger
{
    private readonly List<AuditLogEntry> _entries = [];
    private readonly Lock _sync = new();

    public event Action<AuditLogEntry>? EntryRecorded;

    public IReadOnlyList<AuditLogEntry> GetSnapshot()
    {
        lock (_sync)
        {
            return [.. _entries];
        }
    }

    public AuditLogEntry Record(
        string actionType,
        string targetPath,
        string summary,
        string? beforeSha256 = null,
        string? afterSha256 = null)
    {
        AuditLogEntry entry = new()
        {
            TimestampUtc = DateTimeOffset.UtcNow,
            ActionType = actionType,
            TargetPath = targetPath,
            Summary = summary,
            BeforeSha256 = beforeSha256,
            AfterSha256 = afterSha256
        };

        lock (_sync)
        {
            _entries.Add(entry);
        }

        EntryRecorded?.Invoke(entry);
        return entry;
    }

    public void ExportJsonLines(string filePath)
    {
        IReadOnlyList<AuditLogEntry> snapshot = GetSnapshot();
        StringBuilder sb = new();
        foreach (AuditLogEntry e in snapshot)
        {
            sb.AppendLine(JsonSerializer.Serialize(e));
        }
        File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
    }

    public string FormatHumanReadableTranscript()
    {
        IReadOnlyList<AuditLogEntry> snapshot = GetSnapshot();
        StringBuilder sb = new();
        sb.AppendLine("================================================================================");
        sb.AppendLine("RAGGIESOFT ACCESSIBILITY STUDIO — ENTERPRISE AUDIT & REMEDIATION SESSION LOG");
        sb.AppendLine($"Generated (UTC): {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm:ss 'UTC'}");
        sb.AppendLine("================================================================================");
        foreach (AuditLogEntry e in snapshot)
        {
            sb.AppendLine($"[{e.TimestampUtc:HH:mm:ss}] [{e.ActionType}] {e.TargetPath}");
            sb.AppendLine($"    Summary: {e.Summary}");
            if (!string.IsNullOrEmpty(e.BeforeSha256) || !string.IsNullOrEmpty(e.AfterSha256))
            {
                sb.AppendLine($"    SHA-256: {e.BeforeSha256 ?? "n/a"} -> {e.AfterSha256 ?? "n/a"}");
            }
        }
        return sb.ToString();
    }
}
