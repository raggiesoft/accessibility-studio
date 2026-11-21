// ================================================================================
// FILE: Program.cs
// PROJECT: AccessibilityStudio.WinForms (RaggieSoft Accessibility Studio IDE)
// LICENSE: MIT License
// ================================================================================
// PLAIN-ENGLISH OVERVIEW (100-LEVEL C# WINFORMS ENTRYPOINT GUIDE):
//   What is this file?
//     Every Windows desktop `.exe` starts inside a `Main` method marked with the
//     `[STAThread]` ("Single-Threaded Apartment") attribute. Why `[STAThread]`?
//     Windows Shell dialogs (`OpenFileDialog`, `FolderBrowserDialog`), Clipboard
//     operations, Drag-and-Drop, and `WebView2` Chromium controls rely on Windows
//     COM (Component Object Model) UI message pumping on a single dedicated UI thread!
//
//   What inputs feed into it, and what visible output does it produce?
//     - Inputs:
//         1. Windows 11 OS Theme preference (`Dark` or `Light`), applied natively
//            via .NET 10's `Application.SetColorMode(SystemColorMode.System)`!
//         2. Per-Monitor V2 High-DPI scaling (`ApplicationConfiguration.Initialize()`),
//            keeping crisp fonts and splitters across 100%, 150%, and 200% displays.
//         3. Optional command-line startup target (`args[0]`): when an engineer
//            double-clicks an `.a11yproj` file in Windows Explorer or right-clicks
//            a project folder and chooses "Audit Folder in RaggieSoft Accessibility
//            Studio" (registered by our InstallAware Studio X18 `.msi` installer),
//            Windows passes that path in `args[0]` so `MainIdeForm` opens it immediately!
//     - Output:
//         Launches `MainIdeForm`—the full multi-pane WCAG 2.2 AA & Revised Section 508
//         Web Engineering IDE.
// ================================================================================

using System.Windows.Forms;
using AccessibilityStudio.WinForms.Forms;

namespace AccessibilityStudio.WinForms;

internal static class Program
{
    /// <summary>
    /// Primary Windows Forms UI thread entrypoint.
    /// </summary>
    [STAThread]
    private static void Main(string[] args)
    {
        // Step 1: Initialize High-DPI PerMonitorV2 scaling, visual styles, and default font rendering.
        ApplicationConfiguration.Initialize();

        // Step 2: Activate .NET 10's native WinForms Dark/Light theme engine so menus,
        // toolstrips, splitters, trees, and grids automatically match Windows 11 settings!
        Application.SetColorMode(SystemColorMode.System);

        // Step 3: Check whether Windows Explorer or a developer terminal passed an initial
        // `.a11yproj` workspace file, project directory, or `.html`/`.css`/`.vtt` file path.
        string? startupTarget = args.Length > 0 && !string.IsNullOrWhiteSpace(args[0])
            ? args[0].Trim()
            : null;

        // Step 4: Start the Windows message loop hosting our flagship enterprise IDE window.
        Application.Run(new MainIdeForm(startupTarget));
    }
}
