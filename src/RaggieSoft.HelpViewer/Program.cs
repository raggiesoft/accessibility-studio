// ================================================================================
// FILE: Program.cs
// PROJECT: RaggieSoft.HelpViewer (`RaggieSoftHelp.exe`)
// LICENSE: MIT License
// ================================================================================
// PLAIN-ENGLISH OVERVIEW (100-LEVEL ENTRYPOINT & CLI CONTRACT GUIDE):
//   What is `RaggieSoftHelp.exe`?
//     The standalone, pluggable RaggieSoft Enterprise Help Viewer! Any RaggieSoft
//     desktop application (`AccessibilityStudio.exe`, `RaggieSoftLauncher.exe`,
//     `StoreTrainer.exe`, `BookReader.exe`, etc.)—or an engineer from the Start
//     Menu or PowerShell—can launch:
//
//       RaggieSoftHelp.exe --module raggiesoft.accessibility-studio ^
//                          --topic rule-reference ^
//                          --anchor rule-WCAG-TABLE-FAKE-HEADER ^
//                          --theme auto
//
//   Single-Instance Behavior:
//     If `RaggieSoftHelp.exe` is already open on the user's desktop, this process
//     forwards the navigation request over a local per-user Named Pipe in < 5ms
//     so the existing Help window switches straight to that module & topic!
// ================================================================================

using System.Windows.Forms;
using RaggieSoft.HelpViewer.Forms;
using RaggieSoft.HelpViewer.Models;
using RaggieSoft.HelpViewer.Services;

namespace RaggieSoft.HelpViewer;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        HelpNavigationRequest request = ParseCommandLine(args);

        // If another instance of `RaggieSoftHelp.exe` is already running for this Windows user,
        // hand off the requested module/topic/anchor to that live window and exit cleanly!
        if (SingleInstanceHelpIpc.TryForwardToExistingInstance(request))
        {
            return 0;
        }

        // Enable Per-Monitor V2 High-DPI + .NET 10 Windows 11 System Dark/Light Mode!
        ApplicationConfiguration.Initialize();
        Application.SetColorMode(SystemColorMode.System);

        using ModularHelpViewerForm mainForm = new(request);
        Application.Run(mainForm);
        return 0;
    }

    private static HelpNavigationRequest ParseCommandLine(string[] args)
    {
        HelpNavigationRequest req = new();

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            string? next = i + 1 < args.Length ? args[i + 1] : null;

            switch (arg.ToLowerInvariant())
            {
                case "--module" or "-m" when next is not null:
                    req.ModuleId = next;
                    i++;
                    break;
                case "--topic" or "-t" when next is not null:
                    req.TopicId = next;
                    i++;
                    break;
                case "--anchor" or "-a" when next is not null:
                    req.AnchorId = next.TrimStart('#');
                    i++;
                    break;
                case "--theme" when next is not null:
                    req.Theme = next;
                    i++;
                    break;
                case "--module-dir" when next is not null:
                    req.ExplicitModuleDirectory = next;
                    i++;
                    break;
            }
        }

        return req;
    }
}
