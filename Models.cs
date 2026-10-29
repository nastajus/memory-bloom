using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace MemoryBloom;

internal sealed class AppSettings
{
    public double ShowAtPercent { get; set; } = 78;
    public double HideBelowPercent { get; set; } = 72;
    public double Scale { get; set; } = 1.0;
    public double ProcessFontSize { get; set; } = 13.0;
    public int VisibilityTimeoutMinutes { get; set; } = 15;
    public string CompactStyle { get; set; } = "Orb";
    public bool OrbLocationMigrated { get; set; }
    public double Left { get; set; } = double.NaN;
    public double Top { get; set; } = double.NaN;
    public bool ExpandedPositionUserSet { get; set; }
    public double? ExpandedLeft { get; set; }
    public double? ExpandedTop { get; set; }

    private static string Folder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MemoryBloom");
    private static string FilePath => Path.Combine(Folder, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new();
        }
        catch { return new(); }
    }

    public void Save()
    {
        Directory.CreateDirectory(Folder);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}

internal sealed record MemoryReading(
    double PhysicalUsedPercent,
    ulong PhysicalUsedBytes,
    ulong PhysicalTotalBytes,
    double CommitPercent,
    ulong CommitUsedBytes,
    ulong CommitLimitBytes,
    ulong SystemCacheBytes,
    ulong KernelPagedBytes,
    ulong KernelNonPagedBytes)
{
    public double PressurePercent => Math.Max(PhysicalUsedPercent, CommitPercent);
}

internal sealed class ProcessSnapshot
{
    public required string Name { get; init; }
    public required string DisplayName { get; init; }
    public int Pid { get; init; }
    public int ParentPid { get; init; }
    public long PrivateBytes { get; init; }
    public long WorkingSetBytes { get; init; }
    public string CommandLine { get; init; } = "";
    public string WindowTitle { get; init; } = "";
    public string Role { get; init; } = "";
    public string Tooltip { get; init; } = "";
    public bool IsSynthetic { get; init; }
    public double CpuTotalMilliseconds { get; init; }
    public DateTime StartTimeUtc { get; init; }
}

internal sealed class ProcessGroup
{
    public required string Name { get; init; }
    public required List<ProcessSnapshot> Processes { get; init; }
    public long TotalPrivateBytes => Processes.Sum(p => p.PrivateBytes);
    public bool IsFirefox => Name.Equals("Firefox", StringComparison.OrdinalIgnoreCase);
    public bool IsSystemEstimate { get; init; }
}

internal sealed record FirefoxTabSnapshot(
    string Title,
    string Url,
    DateTime LastAccessedUtc,
    bool Hidden,
    bool IsLiveObservation = false,
    string StableId = "",
    int WindowId = 0,
    bool Active = false,
    bool Discarded = false,
    string Source = "recovery");

internal sealed record ProcessActivity(int Pid, double CpuTotalMilliseconds, long WorkingSetBytes);

internal sealed class FirefoxTabsSnapshot
{
    public List<FirefoxTabSnapshot> Tabs { get; init; } = new();
    public DateTime SourceTimeUtc { get; init; }
    public string SourcePath { get; init; } = "";
    public string? Error { get; init; }
    public bool IsCompanion { get; init; }
    public string SessionId { get; init; } = "";
}

internal sealed record FirefoxTabSection(string Name);
internal sealed record FirefoxContentSection(string Name);

internal sealed class TabProcessAssociation
{
    public required string Key { get; init; }
    public int Pid { get; set; }
    public int? PreviousPid { get; set; }
    public DateTime ObservedUtc { get; set; }
    public DateTime? DriftDetectedUtc { get; set; }
    public double Confidence { get; set; }
}
