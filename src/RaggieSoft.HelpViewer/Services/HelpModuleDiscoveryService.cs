// ================================================================================
// FILE: Services/HelpModuleDiscoveryService.cs
// PROJECT: RaggieSoft.HelpViewer (`RaggieSoftHelp.exe`)
// LICENSE: MIT License
// ================================================================================
// PLAIN-ENGLISH OVERVIEW (100-LEVEL ENTERPRISE MODULE DISCOVERY GUIDE):
//   How does `RaggieSoftHelp.exe` find Help Modules across an Enterprise PC?
//     When `RaggieSoftHelp.exe` starts, it scans five canonical locations in
//     deterministic priority order, de-duplicating by `moduleId`:
//       1. Any `--module-dir <folder>` explicitly passed on the command line.
//       2. App-local `[INSTALLDIR]\HelpModules\*` (`<ExeDir>\..\HelpModules\*` and
//          `<ExeDir>\HelpModules\*`), which works immediately out of our Visual
//          Studio build folder (`build\INSTALLDIR\<Config>\`) and app-local MSIs!
//       3. Enterprise Shared Common Files (`C:\Program Files\Common Files\RaggieSoft\HelpModules\*`),
//          which is the standard Windows Installer Merge Module (`.msm`) & multi-MSI
//          suite path when multiple RaggieSoft enterprise apps share one viewer!
//       4. Per-User AppData (`%LOCALAPPDATA%\RaggieSoft\HelpModules\*`) for non-admin
//          consumer apps installed via RaggieSoft Launcher / Inno Setup!
//       5. Windows Registry keys (`HKLM\Software\RaggieSoft\HelpModules` and
//          `HKCU\Software\RaggieSoft\HelpModules`), allowing apps installed to custom
//          drives (e.g., `D:\Tools\...`) to register their Help Module directory!
// ================================================================================

using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using RaggieSoft.HelpViewer.Models;

namespace RaggieSoft.HelpViewer.Services;

public static partial class HelpModuleDiscoveryService
{
    public const string ManifestFileName = "module.rshm.json";

    /// <summary>
    /// Discovers and loads all valid RaggieSoft Help Modules (<c>module.rshm.json</c>)
    /// installed on the current workstation.
    /// </summary>
    public static IReadOnlyList<LoadedHelpModule> DiscoverInstalledModules(string? explicitModuleDir = null)
    {
        List<string> candidateModuleDirs = [];

        // 1. Explicit `--module-dir` passed on CLI (can point directly to a module folder or parent container)
        if (!string.IsNullOrWhiteSpace(explicitModuleDir) && Directory.Exists(explicitModuleDir))
        {
            CollectModuleDirectoriesFromRoot(Path.GetFullPath(explicitModuleDir), candidateModuleDirs);
        }

        // 2. `[INSTALLDIR]\HelpModules\*` relative to `[INSTALLDIR]\HelpViewer\RaggieSoftHelp.exe`
        string baseDir = AppContext.BaseDirectory;
        string parentInstallDirModules = Path.GetFullPath(Path.Combine(baseDir, "..", "HelpModules"));
        string siblingModules = Path.Combine(baseDir, "HelpModules");
        CollectModuleDirectoriesFromRoot(parentInstallDirModules, candidateModuleDirs);
        CollectModuleDirectoriesFromRoot(siblingModules, candidateModuleDirs);

        // 3. Enterprise Shared Common Files (`C:\Program Files\Common Files\RaggieSoft\HelpModules`)
        string commonFiles = Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFiles);
        if (!string.IsNullOrWhiteSpace(commonFiles))
        {
            CollectModuleDirectoriesFromRoot(
                Path.Combine(commonFiles, "RaggieSoft", "HelpModules"),
                candidateModuleDirs);
        }

        // 4. Per-User LocalAppData (`%LOCALAPPDATA%\RaggieSoft\HelpModules`)
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrWhiteSpace(localAppData))
        {
            CollectModuleDirectoriesFromRoot(
                Path.Combine(localAppData, "RaggieSoft", "HelpModules"),
                candidateModuleDirs);
        }

        // 5. Windows Registry module registrations (`HKLM` & `HKCU`)
        if (OperatingSystem.IsWindows())
        {
            CollectRegistryRegisteredModuleDirectories(Registry.LocalMachine, candidateModuleDirs);
            CollectRegistryRegisteredModuleDirectories(Registry.CurrentUser, candidateModuleDirs);
        }

        // Parse manifests & build plain-text search corpus (de-duplicated by `moduleId`)
        Dictionary<string, LoadedHelpModule> loadedById = new(StringComparer.OrdinalIgnoreCase);
        foreach (string dir in candidateModuleDirs)
        {
            LoadedHelpModule? module = TryLoadModuleFromDirectory(dir);
            if (module is not null && !loadedById.ContainsKey(module.Manifest.ModuleId))
            {
                loadedById[module.Manifest.ModuleId] = module;
            }
        }

        return loadedById.Values
            .OrderBy(m => m.Manifest.ProductName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Searches across topics, index entries, and full HTML chapter text in either the
    /// currently selected module or all installed RaggieSoft modules.
    /// </summary>
    public static IReadOnlyList<ModularHelpSearchHit> SearchModules(
        IReadOnlyList<LoadedHelpModule> modules,
        string? query,
        string? restrictToModuleId = null)
    {
        IEnumerable<LoadedHelpModule> scope = string.IsNullOrWhiteSpace(restrictToModuleId)
            ? modules
            : modules.Where(m => string.Equals(m.Manifest.ModuleId, restrictToModuleId, StringComparison.OrdinalIgnoreCase));

        List<ModularHelpSearchHit> hits = [];
        if (string.IsNullOrWhiteSpace(query))
        {
            foreach (LoadedHelpModule mod in scope)
            {
                foreach (HelpModuleTopicEntry t in mod.Manifest.Topics)
                {
                    hits.Add(new ModularHelpSearchHit(
                        mod.Manifest.ModuleId,
                        mod.Manifest.ProductName,
                        t.Id,
                        null,
                        t.Title,
                        t.Category,
                        10));
                }
            }
            return hits;
        }

        string[] terms = query
            .Trim()
            .Split([' ', '\t', ',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (LoadedHelpModule mod in scope)
        {
            // 1. Score conceptual chapters + full-text HTML body
            foreach (HelpModuleTopicEntry topic in mod.Manifest.Topics)
            {
                int score = 0;
                mod.PlainTextByTopicId.TryGetValue(topic.Id, out string? bodyText);

                foreach (string term in terms)
                {
                    if (topic.Title.Contains(term, StringComparison.OrdinalIgnoreCase)) score += 40;
                    if (topic.Keywords.Any(k => k.Contains(term, StringComparison.OrdinalIgnoreCase))) score += 30;
                    if (topic.Summary.Contains(term, StringComparison.OrdinalIgnoreCase)) score += 20;
                    if (!string.IsNullOrEmpty(bodyText) && bodyText.Contains(term, StringComparison.OrdinalIgnoreCase)) score += 10;
                }

                if (score > 0)
                {
                    hits.Add(new ModularHelpSearchHit(
                        mod.Manifest.ModuleId,
                        mod.Manifest.ProductName,
                        topic.Id,
                        null,
                        topic.Title,
                        topic.Category,
                        score));
                }
            }

            // 2. Score alphabetical index entries (e.g. `WCAG-*` rule IDs)
            foreach (HelpModuleIndexEntry entry in mod.Manifest.IndexEntries)
            {
                int score = 0;
                foreach (string term in terms)
                {
                    if (entry.EntryId.Contains(term, StringComparison.OrdinalIgnoreCase)) score += 60;
                    if (entry.DisplayText.Contains(term, StringComparison.OrdinalIgnoreCase)) score += 35;
                    if (entry.Subtitle.Contains(term, StringComparison.OrdinalIgnoreCase)) score += 25;
                }

                if (score > 0)
                {
                    hits.Add(new ModularHelpSearchHit(
                        mod.Manifest.ModuleId,
                        mod.Manifest.ProductName,
                        entry.TopicId,
                        entry.AnchorId,
                        entry.DisplayText,
                        entry.Category,
                        score));
                }
            }
        }

        return hits
            .OrderByDescending(h => h.Score)
            .ThenBy(h => h.DisplayTitle, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void CollectModuleDirectoriesFromRoot(string rootPath, List<string> targetList)
    {
        if (!Directory.Exists(rootPath))
        {
            return;
        }

        // Direct module directory containing `module.rshm.json`
        if (File.Exists(Path.Combine(rootPath, ManifestFileName)))
        {
            targetList.Add(rootPath);
            return;
        }

        // Parent `HelpModules\` container holding `<module-id>\module.rshm.json` children
        try
        {
            foreach (string childDir in Directory.EnumerateDirectories(rootPath))
            {
                if (File.Exists(Path.Combine(childDir, ManifestFileName)))
                {
                    targetList.Add(childDir);
                }
            }
        }
        catch
        {
            // Ignore unreadable permission-restricted directories
        }
    }

    private static void CollectRegistryRegisteredModuleDirectories(RegistryKey hive, List<string> targetList)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            using RegistryKey? key = hive.OpenSubKey(@"Software\RaggieSoft\HelpModules");
            if (key is null) return;

            foreach (string valueName in key.GetValueNames())
            {
                if (key.GetValue(valueName) is string registeredDir && Directory.Exists(registeredDir))
                {
                    CollectModuleDirectoriesFromRoot(registeredDir, targetList);
                }
            }
        }
        catch
        {
            // Ignore locked corporate registry hives
        }
    }

    private static LoadedHelpModule? TryLoadModuleFromDirectory(string directoryPath)
    {
        try
        {
            string manifestPath = Path.Combine(directoryPath, ManifestFileName);
            if (!File.Exists(manifestPath))
            {
                return null;
            }

            string json = File.ReadAllText(manifestPath, Encoding.UTF8);
            HelpModuleManifest? manifest = JsonSerializer.Deserialize<HelpModuleManifest>(json);
            if (manifest is null || string.IsNullOrWhiteSpace(manifest.ModuleId))
            {
                return null;
            }

            Dictionary<string, string> plainTextByTopic = new(StringComparer.OrdinalIgnoreCase);
            foreach (HelpModuleTopicEntry topic in manifest.Topics)
            {
                string fileName = string.IsNullOrWhiteSpace(topic.FileName) ? $"{topic.Id}.html" : topic.FileName;
                string htmlPath = Path.Combine(directoryPath, fileName);
                if (File.Exists(htmlPath))
                {
                    string rawHtml = File.ReadAllText(htmlPath, Encoding.UTF8);
                    string stripped = WebUtility.HtmlDecode(HtmlTagRegex().Replace(rawHtml, " "));
                    plainTextByTopic[topic.Id] = stripped;
                }
            }

            return new LoadedHelpModule
            {
                Manifest = manifest,
                ModuleDirectory = directoryPath,
                PlainTextByTopicId = plainTextByTopic
            };
        }
        catch
        {
            return null;
        }
    }

    [GeneratedRegex("<[^>]+>", RegexOptions.Compiled)]
    private static partial Regex HtmlTagRegex();
}
