using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Logging;

namespace STS2AIBot.Communication;

/// <summary>
/// Communication protocol between C# mod and Python trainer.
/// Uses Named Pipes for bidirectional communication.
/// </summary>
public enum MessageCommand
{
    // Python -> C#
    RESET,       // Reset environment, get initial state
    STEP,        // Execute action, get next state
    GET_ACTION_MASK,  // Get valid actions
    GET_STATE,   // Get current state
    CLOSE,       // Close connection

    // C# -> Python
    STATE,       // Send state observation
    DONE,        // Episode finished
    ERROR,       // Error occurred
    ACK          // Acknowledge
}

/// <summary>
/// Message format for pipe communication.
/// Format: COMMAND|JSON_PAYLOAD
/// </summary>
public record PipeMessage(
    MessageCommand Command,
    string Payload
)
{
    public string Serialize()
    {
        var json = Payload ?? "";
        return $"{(int)Command}|{json}";
    }

    public static PipeMessage Deserialize(string data)
    {
        if (string.IsNullOrEmpty(data))
            return new PipeMessage(MessageCommand.ERROR, "Empty message");

        var parts = data.Split('|', 2);
        if (parts.Length < 1 || !int.TryParse(parts[0], out int cmdInt))
            return new PipeMessage(MessageCommand.ERROR, $"Invalid command: {data}");

        var command = (MessageCommand)cmdInt;
        var payload = parts.Length > 1 ? parts[1] : "";
        return new PipeMessage(command, payload);
    }
}

/// <summary>
/// Named pipe server for communicating with Python trainer.
/// </summary>
public class PipeServer : IDisposable
{
    private const string PIPE_NAME = "STS2AIBot_Training";
    private NamedPipeServerStream? _pipe;
    private CancellationTokenSource _cts = new();
    private Task? _listenTask;
    private readonly object _lock = new();
    private bool _disposed = false;

    // Event handlers
    public event Action<string>? OnResetRequested;
    public event Action<int>? OnStepRequested;
    public event Action? OnGetActionMaskRequested;
    public event Action? OnCloseRequested;

    // Callbacks for responses
    public Func<string>? GetStateCallback;
    public Func<string>? GetActionMaskCallback;

    public bool IsConnected => _pipe?.IsConnected == true;

    public PipeServer()
    {
        Log.Info("[PipeServer] Created");
    }

    public void Start()
    {
        if (_listenTask != null && !_listenTask.IsCompleted)
            return;

        Log.Info("[PipeServer] Starting pipe server...");

        _listenTask = Task.Run(async () =>
        {
            while (!_cts.IsCancellationRequested && !_disposed)
            {
                try
                {
                    await WaitForConnectionAsync();
                    await ListenLoopAsync();
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Log.Info($"[PipeServer] Error: {ex.Message}");
                    await Task.Delay(1000); // Wait before retrying
                }
            }
        }, _cts.Token);
    }

    private async Task WaitForConnectionAsync()
    {
        while (!_cts.IsCancellationRequested && !_disposed)
        {
            try
            {
                Log.Info("[PipeServer] Waiting for Python client connection...");
                _pipe = new NamedPipeServerStream(
                    PIPE_NAME,
                    PipeDirection.InOut,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Message,
                    PipeOptions.Asynchronous
                );

                await _pipe.WaitForConnectionAsync();
                Log.Info("[PipeServer] Client connected!");
                break;
            }
            catch (Exception ex) when (!_cts.IsCancellationRequested)
            {
                Log.Info($"[PipeServer] Connection error: {ex.Message}");
                _pipe?.Dispose();
                await Task.Delay(1000);
            }
        }
    }

    private async Task ListenLoopAsync()
    {
        if (_pipe == null) return;

        var buffer = new byte[4096];
        var streamReader = new StreamReader(_pipe, Encoding.UTF8);
        var streamWriter = new StreamWriter(_pipe, Encoding.UTF8) { AutoFlush = true };

        while (_pipe.IsConnected && !_cts.IsCancellationRequested && !_disposed)
        {
            try
            {
                var line = await streamReader.ReadLineAsync();
                if (string.IsNullOrEmpty(line))
                    continue;

                var msg = PipeMessage.Deserialize(line);
                Log.Info($"[PipeServer] Received: {msg.Command} | {msg.Payload.Substring(0, Math.Min(50, msg.Payload.Length))}");

                await HandleMessageAsync(msg, streamWriter);
            }
            catch (Exception ex)
            {
                Log.Info($"[PipeServer] Listen loop error: {ex.Message}");
                break;
            }
        }

        Log.Info("[PipeServer] Client disconnected");
        _pipe?.Dispose();
    }

    private async Task HandleMessageAsync(PipeMessage msg, StreamWriter writer)
    {
        try
        {
            string? response = null;
            MessageCommand responseCommand = MessageCommand.ACK;

            switch (msg.Command)
            {
                case MessageCommand.RESET:
                    OnResetRequested?.Invoke(msg.Payload);
                    responseCommand = MessageCommand.STATE;
                    response = GetStateCallback?.Invoke();
                    break;

                case MessageCommand.STEP:
                    if (int.TryParse(msg.Payload, out int action))
                    {
                        OnStepRequested?.Invoke(action);
                    }
                    responseCommand = MessageCommand.STATE;
                    response = GetStateCallback?.Invoke();
                    break;

                case MessageCommand.GET_ACTION_MASK:
                    responseCommand = MessageCommand.ACK;
                    response = GetActionMaskCallback?.Invoke();
                    break;

                case MessageCommand.GET_STATE:
                    responseCommand = MessageCommand.STATE;
                    response = GetStateCallback?.Invoke();
                    break;

                case MessageCommand.CLOSE:
                    OnCloseRequested?.Invoke();
                    return;

                default:
                    Log.Info($"[PipeServer] Unknown command: {msg.Command}");
                    break;
            }

            if (response != null)
            {
                var responseMsg = new PipeMessage(responseCommand, response);
                await writer.WriteLineAsync(responseMsg.Serialize());
                Log.Info($"[PipeServer] Sent: {responseCommand} ({response.Length} chars)");
            }
        }
        catch (Exception ex)
        {
            Log.Info($"[PipeServer] Handle message error: {ex.Message}");
            var errorMsg = new PipeMessage(MessageCommand.ERROR, ex.Message);
            await writer.WriteLineAsync(errorMsg.Serialize());
        }
    }

    public void SendDone(bool won, int turns, int hpRemaining)
    {
        if (!IsConnected) return;

        var doneJson = $"{{\"won\":{won.ToString().ToLower()},\"turns\":{turns},\"hp_remaining\":{hpRemaining}}}";
        var msg = new PipeMessage(MessageCommand.DONE, doneJson);

        try
        {
            using var writer = new StreamWriter(_pipe, Encoding.UTF8) { AutoFlush = true };
            writer.WriteLine(msg.Serialize());
            Log.Info($"[PipeServer] Sent DONE: {doneJson}");
        }
        catch (Exception ex)
        {
            Log.Info($"[PipeServer] Send done error: {ex.Message}");
        }
    }

    public void Dispose()
    {
        _disposed = true;
        _cts.Cancel();

        try
        {
            _listenTask?.Wait(1000);
        }
        catch { }

        _pipe?.Dispose();
        _cts?.Dispose();

        Log.Info("[PipeServer] Disposed");
    }
}
