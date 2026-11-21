// ================================================================================
// FILE: Models/HelpModuleModels.cs
// PROJECT: RaggieSoft.HelpViewer (`RaggieSoftHelp.exe`)
// LICENSE: MIT License
// ================================================================================
// PLAIN-ENGLISH OVERVIEW (100-LEVEL PLUGGABLE HELP MODULE SCHEMA GUIDE):
//   What is a "RaggieSoft Help Module" (`module.rshm.json`)?
//     Instead of hardcoding documentation inside a single application executable—
//     or relying on legacy Windows 2000 `.chm` binaries—each RaggieSoft desktop
//     application ships a self-contained folder containing:
//       1. `module.rshm.json` — This JSON manifest declaring the product's unique
//          `moduleId` (e.g., `"raggiesoft.accessibility-studio"`), display name,
//          version, table-of-contents `topics`, and alphabetical `indexEntries`.
//       2. `help.css` + `<topicId>.html` — Standards-compliant, WCAG 2.2 AA
//          offline HTML5 documentation files.
//
//   Why this matters for Enterprise MSI / MSM / MSIX Deployment:
//     Because `module.rshm.json` is a deterministic file on disk, Windows Installer
//     (`msiexec`) can use `module.rshm.json` as the MSI Component `KeyPath` for
//     automatic MSI Self-Healing, while `RaggieSoftHelp.exe` dynamically discovers
//     every installed RaggieSoft product's documentation at launch!
// ================================================================================

using System.Text.Json.Serialization;

namespace RaggieSoft.HelpViewer.Models;

/// <summary>
/// Deserializes <c>module.rshm.json</c> from any installed RaggieSoft Help Module folder.
/// </summary>
public sealed class HelpModuleManifest
{
    [JsonPropertyName("schemaVersion")]
    public string SchemaVersion { get; set; } = "1.0";

    [JsonPropertyName("moduleId")]
    public string ModuleId { get; set; } = string.Empty;

    [JsonPropertyName("productName")]
    public string ProductName { get; set; } = string.Empty;

    [JsonPropertyName("productVersion")]
    public string ProductVersion { get; set; } = "1.0.0";

    [JsonPropertyName("publisher")]
    public string Publisher { get; set; } = "RaggieSoft Media";

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("defaultTopicId")]
    public string DefaultTopicId { get; set; } = "welcome-overview";

    [JsonPropertyName("onlineDocsUrl")]
    public string OnlineDocsUrl { get; set; } = "https://raggiesoft.com";

    [JsonPropertyName("topics")]
    public List<HelpModuleTopicEntry> Topics { get; set; } = [];

    [JsonPropertyName("indexEntries")]
    public List<HelpModuleIndexEntry> IndexEntries { get; set; } = [];
}

/// <summary>
/// Single conceptual or reference chapter declared inside <c>module.rshm.json</c>.
/// </summary>
public sealed class HelpModuleTopicEntry
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("fileName")]
    public string FileName { get; set; } = string.Empty;

    [JsonPropertyName("category")]
    public string Category { get; set; } = "General";

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("summary")]
    public string Summary { get; set; } = string.Empty;

    [JsonPropertyName("keywords")]
    public List<string> Keywords { get; set; } = [];
}

/// <summary>
/// Single alphabetical index item (such as a WCAG Rule ID, CLI command, or error code)
/// declared inside <c>module.rshm.json</c>.
/// </summary>
public sealed class HelpModuleIndexEntry
{
    [JsonPropertyName("entryId")]
    public string EntryId { get; set; } = string.Empty;

    [JsonPropertyName("displayText")]
    public string DisplayText { get; set; } = string.Empty;

    [JsonPropertyName("category")]
    public string Category { get; set; } = string.Empty;

    [JsonPropertyName("badge")]
    public string Badge { get; set; } = string.Empty;

    [JsonPropertyName("subtitle")]
    public string Subtitle { get; set; } = string.Empty;

    [JsonPropertyName("topicId")]
    public string TopicId { get; set; } = string.Empty;

    [JsonPropertyName("anchorId")]
    public string? AnchorId { get; set; }
}

/// <summary>
/// Loaded runtime representation combining a parsed <see cref="HelpModuleManifest"/>
/// with its resolved physical directory path and cached plain-text search corpus.
/// </summary>
public sealed class LoadedHelpModule
{
    public required HelpModuleManifest Manifest { get; init; }
    public required string ModuleDirectory { get; init; }
    public required Dictionary<string, string> PlainTextByTopicId { get; init; }

    public override string ToString() => $"{Manifest.ProductName} (v{Manifest.ProductVersion})";
}

/// <summary>
/// Ranked search hit across either the active module or all installed RaggieSoft modules.
/// </summary>
public sealed record ModularHelpSearchHit(
    string ModuleId,
    string ProductName,
    string TopicId,
    string? AnchorId,
    string DisplayTitle,
    string Category,
    int Score
);

/// <summary>
/// Parsed command-line &amp; Named Pipe navigation request sent to <c>RaggieSoftHelp.exe</c>.
/// </summary>
public sealed class HelpNavigationRequest
{
    public string? ModuleId { get; set; }
    public string? TopicId { get; set; }
    public string? AnchorId { get; set; }
    public string? Theme { get; set; }
    public string? ExplicitModuleDirectory { get; set; }
}
