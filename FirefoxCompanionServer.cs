using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MemoryBloom;

internal sealed record FirefoxCompanionUpdate(FirefoxTabsSnapshot Snapshot, string EventTabKey);

internal sealed class FirefoxCompanionServer : IDisposable
{
    public const string PipeName = "MemoryBloom.FirefoxCompanion.v1";

    private readonly CancellationTokenSource _cancellation = new();
    private readonly Action<FirefoxCompanionUpdate> _onUpdate;
    private readonly Task _listener;

    public FirefoxCompanionServer(Action<FirefoxCompanionUpdate> onUpdate)
    {
        _onUpdate = onUpdate;
        _listener = Task.Run(ListenAsync);
    }

    private async Task ListenAsync()
    {
        while (!_cancellation.IsCancellationRequested)
        {
            try
            {
                await using var pipe = new NamedPipeServerStream(
                    PipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(_cancellation.Token);
                using var reader = new StreamReader(pipe);
                while (!_cancellation.IsCancellationRequested && pipe.IsConnected)
                {
                    var line = await reader.ReadLineAsync(_cancellation.Token);
                    if (line is null) break;
                    var update = Parse(line);
                    if (update is not null) _onUpdate(update);
                }
            }
            catch (OperationCanceledException) when (_cancellation.IsCancellationRequested)
            {
                return;
            }
            catch
            {
                await Task.Delay(750, _cancellation.Token).ConfigureAwait(false);
            }
        }
    }

    private static FirefoxCompanionUpdate? Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (!root.TryGetProperty("type", out var type) || type.GetString() != "snapshot") return null;
        var sessionId = root.TryGetProperty("sessionId", out var sessionNode) ? sessionNode.GetString() ?? "" : "";
        var eventTabId = root.TryGetProperty("eventTabId", out var eventNode) && eventNode.TryGetInt32(out var parsedEventId) ? parsedEventId : -1;
        var focusedWindowId = root.TryGetProperty("focusedWindowId", out var focusNode) && focusNode.TryGetInt32(out var parsedWindowId) ? parsedWindowId : -1;
        var tabs = new List<FirefoxTabSnapshot>();
        if (root.TryGetProperty("tabs", out var tabNodes))
        {
            foreach (var node in tabNodes.EnumerateArray())
            {
                var id = node.TryGetProperty("id", out var idNode) && idNode.TryGetInt32(out var parsedId) ? parsedId : -1;
                var windowId = node.TryGetProperty("windowId", out var windowNode) && windowNode.TryGetInt32(out var parsedWindow) ? parsedWindow : -1;
                var lastAccessedUtc = node.TryGetProperty("lastAccessed", out var accessNode) && accessNode.TryGetInt64(out var milliseconds)
                    ? DateTimeOffset.FromUnixTimeMilliseconds(milliseconds).UtcDateTime
                    : DateTime.UtcNow;
                tabs.Add(new FirefoxTabSnapshot(
                    node.TryGetProperty("title", out var titleNode) ? titleNode.GetString() ?? "Untitled tab" : "Untitled tab",
                    node.TryGetProperty("url", out var urlNode) ? urlNode.GetString() ?? "" : "",
                    lastAccessedUtc,
                    node.TryGetProperty("hidden", out var hiddenNode) && hiddenNode.ValueKind == JsonValueKind.True,
                    IsLiveObservation: true,
                    StableId: id < 0 ? "" : $"companion:{sessionId}:{id}",
                    WindowId: windowId,
                    Active: node.TryGetProperty("active", out var activeNode) && activeNode.ValueKind == JsonValueKind.True && windowId == focusedWindowId,
                    Discarded: node.TryGetProperty("discarded", out var discardedNode) && discardedNode.ValueKind == JsonValueKind.True,
                    Source: "companion"));
            }
        }

        var sourceTime = root.TryGetProperty("sentAt", out var sentNode) && sentNode.TryGetInt64(out var sentMilliseconds)
            ? DateTimeOffset.FromUnixTimeMilliseconds(sentMilliseconds).UtcDateTime
            : DateTime.UtcNow;
        return new FirefoxCompanionUpdate(
            new FirefoxTabsSnapshot
            {
                Tabs = tabs.OrderByDescending(tab => tab.LastAccessedUtc).ToList(),
                SourceTimeUtc = sourceTime,
                SourcePath = "Firefox companion",
                IsCompanion = true,
                SessionId = sessionId
            },
            eventTabId < 0 ? "" : $"companion:{sessionId}:{eventTabId}");
    }

    public void Dispose()
    {
        _cancellation.Cancel();
        _cancellation.Dispose();
    }
}
