using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace MemoryBloom;

internal sealed record HistoryProcessSample(string Name, int Pid, long PrivateBytes);

internal sealed class MemoryHistoryPoint
{
    public DateTime TimestampUtc { get; init; }
    public double PhysicalPercent { get; init; }
    public double CommitPercent { get; init; }
    public ulong PhysicalUsedBytes { get; init; }
    public ulong PhysicalTotalBytes { get; init; }
    public ulong CommitUsedBytes { get; init; }
    public ulong CommitLimitBytes { get; init; }
    public string Classification { get; init; } = "Normal";
    public List<HistoryProcessSample> TopProcesses { get; init; } = new();
    public double PressurePercent => Math.Max(PhysicalPercent, CommitPercent);
}

internal sealed record SystemHistoryMarker(
    DateTime TimestampUtc,
    string Kind,
    string Summary,
    string Details,
    double? PhysicalPercent = null,
    double? CommitPercent = null)
{
    public double? PressurePercent => PhysicalPercent is null && CommitPercent is null
        ? null
        : Math.Max(PhysicalPercent ?? 0, CommitPercent ?? 0);
}

internal sealed class MemoryHistoryStore
{
    private static readonly TimeSpan PersistInterval = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan RecentHighResolution = TimeSpan.FromDays(7);
    private static readonly TimeSpan Retention = TimeSpan.FromDays(30);
    private readonly List<MemoryHistoryPoint> _points = new();
    private readonly string _path;
    private DateTime _lastPersistUtc;
    private DateTime _lastSpikeUtc;
    private MemoryHistoryPoint? _lastPoint;
    private bool _elevatedInitialized;
    private bool _wasElevated;

    public IReadOnlyList<MemoryHistoryPoint> Points => _points;
    public DateTime? TrackingSinceUtc => _points.Count == 0 ? null : _points[0].TimestampUtc;

    public MemoryHistoryStore()
    {
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MemoryBloom");
        _path = Path.Combine(folder, "memory-history.jsonl");
        LoadAndCompact();
    }

    public MemoryHistoryPoint Record(MemoryReading reading, IReadOnlyList<HistoryProcessSample> topProcesses,
        double alertThreshold, double hideThreshold, bool persist = true)
    {
        var now = DateTime.UtcNow;
        var pressure = reading.PressurePercent;
        var previous = _lastPoint;
        if (!_elevatedInitialized)
        {
            _wasElevated = previous is not null && previous.PressurePercent >= alertThreshold;
            _elevatedInitialized = true;
        }
        var classification = "Normal";
        var minuteBaseline = _points
            .Where(item => now - item.TimestampUtc <= TimeSpan.FromMinutes(1))
            .Select(item => item.PressurePercent)
            .DefaultIfEmpty(pressure)
            .Min();
        if (pressure >= 90 && (previous is null || previous.PressurePercent < 90))
            classification = "Critical";
        else if (pressure - minuteBaseline >= 5 && now - _lastSpikeUtc >= TimeSpan.FromMinutes(1))
            classification = "Spike";
        else if (!_wasElevated && pressure >= alertThreshold)
            classification = "High";
        else if (_wasElevated && pressure < hideThreshold)
            classification = "Recovered";
        if (classification == "Spike") _lastSpikeUtc = now;
        if (pressure >= alertThreshold) _wasElevated = true;
        else if (pressure < hideThreshold) _wasElevated = false;

        var point = new MemoryHistoryPoint
        {
            TimestampUtc = now,
            PhysicalPercent = reading.PhysicalUsedPercent,
            CommitPercent = reading.CommitPercent,
            PhysicalUsedBytes = reading.PhysicalUsedBytes,
            PhysicalTotalBytes = reading.PhysicalTotalBytes,
            CommitUsedBytes = reading.CommitUsedBytes,
            CommitLimitBytes = reading.CommitLimitBytes,
            Classification = classification,
            TopProcesses = topProcesses.Take(8).ToList()
        };
        _points.Add(point);
        _lastPoint = point;

        var exceptional = classification != "Normal";
        if (persist && (exceptional || now - _lastPersistUtc >= PersistInterval))
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                File.AppendAllText(_path, JsonSerializer.Serialize(point) + Environment.NewLine);
                _lastPersistUtc = now;
            }
            catch
            {
                // History is supplementary; a locked or unavailable profile must not stop live monitoring.
            }
        }

        var liveCutoff = now - Retention;
        if (_points.Count > 100_000)
        {
            var detailedCutoff = now - TimeSpan.FromDays(1);
            var compacted = _points
                .Where(item => item.TimestampUtc >= detailedCutoff || item.Classification != "Normal")
                .Concat(_points
                    .Where(item => item.TimestampUtc < detailedCutoff && item.TimestampUtc >= liveCutoff && item.Classification == "Normal")
                    .GroupBy(item => new DateTime(item.TimestampUtc.Ticks - item.TimestampUtc.Ticks % TimeSpan.FromMinutes(1).Ticks, DateTimeKind.Utc))
                    .Select(bucket => bucket.OrderByDescending(item => item.PressurePercent).First()))
                .OrderBy(item => item.TimestampUtc)
                .ToList();
            _points.Clear();
            _points.AddRange(compacted);
        }
        return point;
    }

    private void LoadAndCompact()
    {
        if (!File.Exists(_path)) return;
        try
        {
            var cutoff = DateTime.UtcNow - Retention;
            foreach (var line in File.ReadLines(_path))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var point = JsonSerializer.Deserialize<MemoryHistoryPoint>(line);
                if (point is not null && point.TimestampUtc >= cutoff) _points.Add(point);
            }
            _points.Sort((left, right) => left.TimestampUtc.CompareTo(right.TimestampUtc));
            var compactBefore = DateTime.UtcNow - RecentHighResolution;
            var compacted = _points
                .Where(point => point.TimestampUtc >= compactBefore || point.Classification != "Normal")
                .Concat(_points
                    .Where(point => point.TimestampUtc < compactBefore && point.Classification == "Normal")
                    .GroupBy(point => new DateTime(point.TimestampUtc.Ticks - point.TimestampUtc.Ticks % TimeSpan.FromMinutes(5).Ticks, DateTimeKind.Utc))
                    .Select(bucket => bucket.OrderByDescending(point => point.PressurePercent).First()))
                .OrderBy(point => point.TimestampUtc)
                .ToList();
            _points.Clear();
            _points.AddRange(compacted);
            _lastPoint = _points.LastOrDefault();
            _lastPersistUtc = _lastPoint?.TimestampUtc ?? default;
            _lastSpikeUtc = _points.LastOrDefault(point => point.Classification == "Spike")?.TimestampUtc ?? default;
            Rewrite();
        }
        catch
        {
            _points.Clear();
            _lastPoint = null;
        }
    }

    private void Rewrite()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temporary = _path + ".tmp";
            File.WriteAllLines(temporary, _points.Select(point => JsonSerializer.Serialize(point)));
            File.Move(temporary, _path, true);
        }
        catch
        {
            // Keep the readable in-memory history even if maintenance cannot rewrite the file.
        }
    }
}

internal static class WindowsHistoryProbe
{
    public static List<SystemHistoryMarker> ReadRecentMarkers(int days = 30)
    {
        var result = new List<SystemHistoryMarker>();
        ReadLog("System", "EventID=2004 or EventID=41 or EventID=6008", days, result);
        ReadLog("Application", "EventID=1000 or EventID=1001", days, result);
        return result.OrderBy(marker => marker.TimestampUtc).ToList();
    }

    private static void ReadLog(string log, string eventPredicate, int days, List<SystemHistoryMarker> target)
    {
        try
        {
            var milliseconds = (long)TimeSpan.FromDays(days).TotalMilliseconds;
            var timedQuery = $"*[System[((${eventPredicate}) and TimeCreated[timediff(@SystemTime) <= {milliseconds}])]]";
            var start = new ProcessStartInfo("wevtutil.exe")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            start.ArgumentList.Add("qe");
            start.ArgumentList.Add(log);
            start.ArgumentList.Add($"/q:{timedQuery}");
            start.ArgumentList.Add("/f:xml");
            start.ArgumentList.Add("/rd:true");
            start.ArgumentList.Add("/c:80");
            using var process = Process.Start(start);
            if (process is null) return;
            var xml = process.StandardOutput.ReadToEnd();
            process.WaitForExit(5000);
            foreach (Match match in Regex.Matches(xml, @"<Event[\s\S]*?</Event>"))
                ParseEvent(match.Value, target);
        }
        catch
        {
            // Event history availability depends on Windows policy and log retention.
        }
    }

    private static void ParseEvent(string xml, List<SystemHistoryMarker> target)
    {
        try
        {
            var document = XDocument.Parse(xml);
            XNamespace ns = "http://schemas.microsoft.com/win/2004/08/events/event";
            var system = document.Root?.Element(ns + "System");
            if (system is null) return;
            var eventId = int.Parse(system.Element(ns + "EventID")?.Value ?? "0", CultureInfo.InvariantCulture);
            var timeText = system.Element(ns + "TimeCreated")?.Attribute("SystemTime")?.Value;
            if (!DateTime.TryParse(timeText, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var timestamp)) return;
            var data = document.Descendants(ns + "Data").Select(node => node.Value).Where(value => !string.IsNullOrWhiteSpace(value)).ToList();
            var (kind, summary) = eventId switch
            {
                2004 => ("Resource exhaustion", "Windows recorded low virtual memory or resource exhaustion"),
                41 => ("Unexpected restart", "Windows restarted without a clean shutdown"),
                6008 => ("Unexpected shutdown", "Windows recorded an unexpected shutdown"),
                1000 => ("Application crash", "Windows recorded an application failure"),
                1001 => ("Error report", "Windows recorded an application or system error report"),
                _ => ("Windows event", $"Windows event {eventId}")
            };
            var details = data.Count == 0 ? summary : string.Join(" · ", data.Take(8));
            double? historicalPhysicalPercent = null;
            double? historicalCommitPercent = null;
            if (eventId == 2004)
            {
                string Value(string name) => document.Descendants().FirstOrDefault(node => node.Name.LocalName == name)?.Value ?? "0";
                _ = ulong.TryParse(Value("PhysicalMemorySize"), out var physicalTotal);
                _ = ulong.TryParse(Value("PhysicalMemoryUsage"), out var physicalUsed);
                _ = ulong.TryParse(Value("SystemCommitLimit"), out var commitLimit);
                _ = ulong.TryParse(Value("SystemCommitCharge"), out var commitUsed);
                var topNames = document.Descendants()
                    .Where(node => node.Name.LocalName.StartsWith("Process_", StringComparison.Ordinal))
                    .Select(node => node.Elements().FirstOrDefault(child => child.Name.LocalName == "Name")?.Value)
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .Take(3)
                    .ToList();
                var physicalPercent = physicalTotal == 0 ? 0 : physicalUsed * 100d / physicalTotal;
                var commitPercent = commitLimit == 0 ? 0 : commitUsed * 100d / commitLimit;
                historicalPhysicalPercent = physicalTotal == 0 ? null : physicalPercent;
                historicalCommitPercent = commitLimit == 0 ? null : commitPercent;
                details = $"Physical {physicalPercent:0.0}% · Commit {commitPercent:0.0}%" +
                    (topNames.Count == 0 ? "" : $" · recorded leaders: {string.Join(", ", topNames)}");
            }
            target.Add(new SystemHistoryMarker(timestamp.ToUniversalTime(), kind, summary, details, historicalPhysicalPercent, historicalCommitPercent));
        }
        catch
        {
            // Ignore malformed or access-restricted individual event records.
        }
    }
}
