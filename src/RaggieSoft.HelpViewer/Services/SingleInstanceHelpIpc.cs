// ================================================================================
// FILE: Services/SingleInstanceHelpIpc.cs
// PROJECT: RaggieSoft.HelpViewer (`RaggieSoftHelp.exe`)
// LICENSE: MIT License
// ================================================================================
// PLAIN-ENGLISH OVERVIEW (100-LEVEL NAMED PIPE SINGLE-INSTANCE IPC GUIDE):
//   Why does a standalone Help Viewer need Single-Instance IPC?
//     Suppose an engineer has `AccessibilityStudio.exe` open on Monitor 1 and
//     presses `F1`—launching `RaggieSoftHelp.exe` on Monitor 2. Two minutes later,
//     they highlight `WCAG-TABLE-FAKE-HEADER` and press `Shift+F1`.
//     Instead of spawning a *second* duplicate Help Viewer window every time `F1`
//     is pressed, `TryForwardToExistingInstance()` connects to a local OS Named
//     Pipe (`RaggieSoft.HelpViewer.IPC.<UserName>`), sends the new `{ ModuleId,
//     TopicId, AnchorId, Theme }` JSON payload in < 5 milliseconds, brings the
//     already-open Help Viewer window into focus, and exits cleanly!
// ================================================================================

using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using RaggieSoft.HelpViewer.Models;

namespace RaggieSoft.HelpViewer.Services;

public static class SingleInstanceHelpIpc
{
    /// <summary>
    /// Per-user Named Pipe identifier so Terminal Services / multi-user Windows
    /// workstations isolate each logged-in engineer's Help Viewer session cleanly.
    /// </summary>
    public static string GetPipeNameForCurrentUser()
    {
        string safeUser = string.Concat(
            Environment.UserName.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_'));
        return $"RaggieSoft.HelpViewer.IPC.{safeUser}";
    }

    /// <summary>
    /// Attempts to forward <paramref name="request"/> to an already-running instance
    /// of <c>RaggieSoftHelp.exe</c>. Returns <c>true</c> if delivered (meaning this
    /// secondary process can exit immediately).
    /// </summary>
    public static bool TryForwardToExistingInstance(HelpNavigationRequest request)
    {
        try
        {
            using NamedPipeClientStream client = new(
                serverName: ".",
                pipeName: GetPipeNameForCurrentUser(),
                direction: PipeDirection.Out);

            // Wait at most 180ms; if no server is listening, we are the primary instance!
            client.Connect(timeout: 180);

            byte[] payload = JsonSerializer.SerializeToUtf8Bytes(request);
            client.Write(payload, 0, payload.Length);
            client.Flush();
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Runs a background Named Pipe listener for the lifetime of the primary
    /// <c>RaggieSoftHelp.exe</c> window, invoking <paramref name="onRequestReceived"/>
    /// whenever another RaggieSoft app presses <c>F1</c>.
    /// </summary>
    public static async Task RunListenerLoopAsync(
        Action<HelpNavigationRequest> onRequestReceived,
        CancellationToken cancellationToken)
    {
        string pipeName = GetPipeNameForCurrentUser();

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await using NamedPipeServerStream server = new(
                    pipeName: pipeName,
                    direction: PipeDirection.In,
                    maxNumberOfServerInstances: 1,
                    transmissionMode: PipeTransmissionMode.Byte,
                    options: PipeOptions.Asynchronous);

                await server.WaitForConnectionAsync(cancellationToken);

                using StreamReader reader = new(server, Encoding.UTF8);
                string json = await reader.ReadToEndAsync(cancellationToken);
                if (!string.IsNullOrWhiteSpace(json))
                {
                    HelpNavigationRequest? req = JsonSerializer.Deserialize<HelpNavigationRequest>(json);
                    if (req is not null)
                    {
                        onRequestReceived(req);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                // Brief backoff on transient pipe errors
                await Task.Delay(150, cancellationToken);
            }
        }
    }
}
