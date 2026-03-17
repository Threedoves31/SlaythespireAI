// PipeServer - Named pipe communication for Python training integration and console commands.
// Receives commands from external processes and exposes AI control API.

using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Logging;
using STS2AIBot.StateExtractor;
using STS2AIBot.AI;

namespace STS2AIBot.Communication;

/// <summary>
/// Named pipe server for external communication.
/// Supports both training mode (step/reset) and console commands (pause/policy/etc).
/// </summary>
public class PipeServer
{
    private CancellationTokenSource? _cts;
    private bool _running = false;

    // Training callbacks
    public Func<string>? GetStateCallback { get; set; }
    public Func<string>? GetActionMaskCallback { get; set; }
    
    // Command callbacks
    public Action? OnResetRequested { get; set; }
    public Func<string, string>? OnStepRequested { get; set; }
    public Action? OnCloseRequested { get; set; }

    private const string PIPE_NAME = "STS2AIBot";

    public void Start()
    {
        if (_running) return;
        _running = true;
        _cts = new CancellationTokenSource();
        
        Task.Run(() => ServerLoop(_cts.Token));
    }

    public void Stop()
    {
        _running = false;
        _cts?.Cancel();
    }

    private async Task ServerLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested && _running)
        {
            NamedPipeServerStream? server = null;
            try
            {
                // Use Byte mode instead of Message mode for better compatibility
                server = new NamedPipeServerStream(
                    PIPE_NAME, 
                    PipeDirection.InOut, 
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);
                
                Log.Info("[PipeServer] Waiting for connection...");
                await server.WaitForConnectionAsync(token);
                Log.Info("[PipeServer] Client connected");

                var buffer = new byte[4096];
                var inputBuffer = new StringBuilder();

                while (server.IsConnected && !token.IsCancellationRequested)
                {
                    // Read raw bytes
                    int bytesRead = await server.ReadAsync(buffer, token);
                    if (bytesRead == 0) break;

                    inputBuffer.Append(Encoding.UTF8.GetString(buffer, 0, bytesRead));

                    // Check for complete line (ends with \n)
                    string input = inputBuffer.ToString();
                    if (input.Contains('\n'))
                    {
                        int newlineIdx = input.IndexOf('\n');
                        string line = input.Substring(0, newlineIdx).Trim('\r', '\n');
                        inputBuffer.Clear();
                        
                        // Keep remaining data
                        if (input.Length > newlineIdx + 1)
                            inputBuffer.Append(input.Substring(newlineIdx + 1));

                        if (string.IsNullOrEmpty(line)) continue;

                        Log.Info($"[PipeServer] Received: {line}");
                        string response = ProcessCommand(line);
                        
                        // Send response with newline
                        byte[] responseBytes = Encoding.UTF8.GetBytes(response + "\n");
                        await server.WriteAsync(responseBytes, token);
                        await server.FlushAsync();
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Log.Info($"[PipeServer] Error: {ex.Message}");
                await Task.Delay(1000, token);
            }
            finally
            {
                server?.Dispose();
            }
        }
    }

    private string ProcessCommand(string command)
    {
        try
        {
            var parts = command.Split(' ', 2, StringSplitOptions.TrimEntries);
            string cmd = parts[0].ToUpperInvariant();
            string? arg = parts.Length > 1 ? parts[1] : null;

            switch (cmd)
            {
                // Training commands
                case "STATE":
                    return GetStateCallback?.Invoke() ?? "{}";
                
                case "ACTION_MASK":
                    return GetActionMaskCallback?.Invoke() ?? "[]";
                
                case "RESET":
                    OnResetRequested?.Invoke();
                    return "{\"status\":\"reset\"}";
                
                case "STEP":
                    return OnStepRequested?.Invoke(arg ?? "") ?? "{}";
                
                case "CLOSE":
                    OnCloseRequested?.Invoke();
                    return "{\"status\":\"closing\"}";
                
                // AI Control commands - use PolicyManager directly (works anytime)
                case "PAUSE":
                case "P":
                    bool previousPaused = PolicyManager.Instance.Paused;
                    PolicyManager.Instance.TogglePause();
                    bool currentPaused = PolicyManager.Instance.Paused;
                    string msg = currentPaused ? "AI paused" : "AI resumed";
                    Log.Info($"[PipeServer] PAUSE toggled: {previousPaused} -> {currentPaused}");
                    return $"{{\"paused\":{currentPaused.ToString().ToLower()},\"message\":\"{msg}\"}}";
                
                case "MANUAL":
                case "M":
                    PolicyManager.Instance.ToggleManualMode();
                    return $"{{\"manual\":{PolicyManager.Instance.ManualMode.ToString().ToLower()}}}";
                
                case "POLICY":
                case "C":
                    string previousPolicy = PolicyManager.Instance.CurrentType.ToString();
                    if (arg != null)
                    {
                        // Set specific policy
                        if (Enum.TryParse<PolicyType>(arg, true, out var policyType))
                        {
                            PolicyManager.Instance.SetPolicy(policyType);
                            Log.Info($"[PipeServer] Policy set to: {policyType}");
                        }
                        else
                        {
                            return $"{{\"error\":\"unknown policy: {arg}. Valid: Heuristic, Simulation, Random\"}}";
                        }
                    }
                    else
                    {
                        // Cycle to next policy
                        PolicyManager.Instance.CyclePolicy();
                        Log.Info($"[PipeServer] Policy cycled: {previousPolicy} -> {PolicyManager.Instance.CurrentType}");
                    }
                    return $"{{\"previous\":\"{previousPolicy}\",\"current\":\"{PolicyManager.Instance.CurrentType}\",\"name\":\"{PolicyManager.Instance.CurrentPolicy.Name}\"}}";
                
                case "VERBOSE":
                case "V":
                    var dbgv = AIDebuggerRegistrar.Debugger;
                    if (dbgv != null)
                    {
                        dbgv.ToggleVerboseLogging();
                        return $"{{\"verbose\":{dbgv.VerboseLogging.ToString().ToLower()}}}";
                    }
                    return "{\"verbose\":false,\"note\":\"not in combat\"}";
                
                case "HISTORY":
                case "H":
                    AIDebuggerRegistrar.Debugger?.ShowHistory();
                    return "{\"status\":\"history printed to log (requires combat)\"}";
                
                case "HELP":
                case "?":
                    return GetHelpText();
                
                case "STATUS":
                case "S":
                    return GetStatus();
                
                default:
                    return $"{{\"error\":\"unknown command: {cmd}\"}}";
            }
        }
        catch (Exception ex)
        {
            return $"{{\"error\":\"{ex.Message}\"}}";
        }
    }

    private string GetHelpText()
    {
        return @"{
  ""commands"": {
    ""PAUSE/P"": ""Toggle AI pause"",
    ""MANUAL/M"": ""Toggle manual mode"",
    ""POLICY/C [type]"": ""Cycle or set policy (Heuristic/Simulation/Random)"",
    ""VERBOSE/V"": ""Toggle verbose logging"",
    ""HISTORY/H"": ""Show decision history"",
    ""STATUS/S"": ""Get current status"",
    ""STATE"": ""Get game state JSON"",
    ""ACTION_MASK"": ""Get valid action mask"",
    ""RESET"": ""Reset environment"",
    ""STEP [action]"": ""Execute action"",
    ""HELP/?"": ""Show this help""
  }
}";
    }

    private string GetStatus()
    {
        var dbg = AIDebuggerRegistrar.Debugger;
        var stats = dbg?.GetStats();
        
        return System.Text.Json.JsonSerializer.Serialize(new
        {
            policy = PolicyManager.Instance.CurrentType.ToString(),
            paused = PolicyManager.Instance.Paused,
            manual = PolicyManager.Instance.ManualMode,
            verbose = dbg?.VerboseLogging ?? false,
            turn = stats?.TurnNumber ?? 0,
            actions = stats?.TotalActions ?? 0
        });
    }
}