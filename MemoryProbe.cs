using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MemoryBloom;

internal static class MemoryProbe
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private sealed class MemoryStatusEx
    {
        public uint Length = (uint)Marshal.SizeOf<MemoryStatusEx>();
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx([In, Out] MemoryStatusEx buffer);

    [StructLayout(LayoutKind.Sequential)]
    private struct PerformanceInformation
    {
        public uint Size;
        public UIntPtr CommitTotal;
        public UIntPtr CommitLimit;
        public UIntPtr CommitPeak;
        public UIntPtr PhysicalTotal;
        public UIntPtr PhysicalAvailable;
        public UIntPtr SystemCache;
        public UIntPtr KernelTotal;
        public UIntPtr KernelPaged;
        public UIntPtr KernelNonpaged;
        public UIntPtr PageSize;
        public uint HandleCount;
        public uint ProcessCount;
        public uint ThreadCount;
    }

    [DllImport("psapi.dll", SetLastError = true)]
    private static extern bool GetPerformanceInfo(out PerformanceInformation information, uint size);

    public static MemoryReading ReadMemory()
    {
        var status = new MemoryStatusEx();
        if (!GlobalMemoryStatusEx(status))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());

        var physicalUsed = status.TotalPhys - status.AvailPhys;
        var commitUsed = status.TotalPageFile - status.AvailPageFile;
        var cacheBytes = 0UL;
        var pagedBytes = 0UL;
        var nonPagedBytes = 0UL;
        if (GetPerformanceInfo(out var performance, (uint)Marshal.SizeOf<PerformanceInformation>()))
        {
            var pageSize = performance.PageSize.ToUInt64();
            cacheBytes = performance.SystemCache.ToUInt64() * pageSize;
            pagedBytes = performance.KernelPaged.ToUInt64() * pageSize;
            nonPagedBytes = performance.KernelNonpaged.ToUInt64() * pageSize;
        }

        return new MemoryReading(
            status.MemoryLoad,
            physicalUsed,
            status.TotalPhys,
            status.TotalPageFile == 0 ? 0 : commitUsed * 100d / status.TotalPageFile,
            commitUsed,
            status.TotalPageFile,
            cacheBytes,
            pagedBytes,
            nonPagedBytes);
    }

    public static FirefoxTabsSnapshot ReadFirefoxTabs()
    {
        try
        {
            var profiles = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Mozilla", "Firefox", "Profiles");
            var source = Directory.Exists(profiles)
                ? Directory.EnumerateFiles(profiles, "recovery.jsonlz4", SearchOption.AllDirectories)
                    .Select(path => new FileInfo(path))
                    .OrderByDescending(file => file.LastWriteTimeUtc)
                    .FirstOrDefault()
                : null;
            if (source is null) return new FirefoxTabsSnapshot { Error = "Firefox session data was not found." };

            byte[] bytes;
            using (var stream = new FileStream(source.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                bytes = new byte[stream.Length];
                stream.ReadExactly(bytes);
            }
            if (bytes.Length < 12 || Encoding.ASCII.GetString(bytes, 0, 8) != "mozLz40\0")
                return new FirefoxTabsSnapshot { SourcePath = source.FullName, SourceTimeUtc = source.LastWriteTimeUtc, Error = "Firefox session data had an unknown format." };

            var json = DecompressLz4Block(bytes.AsSpan(8));
            using var document = JsonDocument.Parse(json);
            var tabs = new List<FirefoxTabSnapshot>();
            if (document.RootElement.TryGetProperty("windows", out var windows))
            {
                foreach (var window in windows.EnumerateArray())
                {
                    if (!window.TryGetProperty("tabs", out var windowTabs)) continue;
                    foreach (var tab in windowTabs.EnumerateArray())
                    {
                        if (!tab.TryGetProperty("entries", out var entries) || entries.GetArrayLength() == 0) continue;
                        var index = tab.TryGetProperty("index", out var indexNode) ? indexNode.GetInt32() - 1 : entries.GetArrayLength() - 1;
                        index = Math.Clamp(index, 0, entries.GetArrayLength() - 1);
                        var entry = entries[index];
                        var title = entry.TryGetProperty("title", out var titleNode) ? titleNode.GetString() ?? "Untitled tab" : "Untitled tab";
                        var url = entry.TryGetProperty("url", out var urlNode) ? urlNode.GetString() ?? "" : "";
                        var lastAccessed = tab.TryGetProperty("lastAccessed", out var accessedNode) && accessedNode.TryGetInt64(out var milliseconds)
                            ? DateTimeOffset.FromUnixTimeMilliseconds(milliseconds).UtcDateTime
                            : source.LastWriteTimeUtc;
                        var hidden = tab.TryGetProperty("hidden", out var hiddenNode) && hiddenNode.ValueKind == JsonValueKind.True;
                        tabs.Add(new FirefoxTabSnapshot(title, url, lastAccessed, hidden));
                    }
                }
            }

            return new FirefoxTabsSnapshot
            {
                Tabs = tabs.OrderByDescending(tab => tab.LastAccessedUtc).ToList(),
                SourcePath = source.FullName,
                SourceTimeUtc = source.LastWriteTimeUtc
            };
        }
        catch (Exception ex)
        {
            return new FirefoxTabsSnapshot { Error = ex.Message };
        }
    }

    private static byte[] DecompressLz4Block(ReadOnlySpan<byte> input)
    {
        if (input.Length < 4) throw new InvalidDataException("LZ4 block is incomplete.");
        var expected = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(input[..4]);
        if (expected < 0 || expected > 256 * 1024 * 1024) throw new InvalidDataException("LZ4 output size is invalid.");
        var output = new byte[expected];
        var source = 4;
        var target = 0;
        while (source < input.Length && target < output.Length)
        {
            var token = input[source++];
            var literalLength = token >> 4;
            if (literalLength == 15) literalLength += ReadExtendedLength(input, ref source);
            if (source + literalLength > input.Length || target + literalLength > output.Length) throw new InvalidDataException("LZ4 literal exceeds its buffer.");
            input.Slice(source, literalLength).CopyTo(output.AsSpan(target));
            source += literalLength;
            target += literalLength;
            if (source >= input.Length || target >= output.Length) break;

            if (source + 2 > input.Length) throw new InvalidDataException("LZ4 match offset is missing.");
            var offset = input[source] | (input[source + 1] << 8);
            source += 2;
            if (offset <= 0 || offset > target) throw new InvalidDataException("LZ4 match offset is invalid.");
            var matchLength = (token & 0x0F) + 4;
            if ((token & 0x0F) == 15) matchLength += ReadExtendedLength(input, ref source);
            if (target + matchLength > output.Length) throw new InvalidDataException("LZ4 match exceeds its buffer.");
            for (var index = 0; index < matchLength; index++) output[target + index] = output[target - offset + index];
            target += matchLength;
        }
        if (target != output.Length) throw new InvalidDataException("LZ4 output was incomplete.");
        return output;
    }

    private static int ReadExtendedLength(ReadOnlySpan<byte> input, ref int position)
    {
        var total = 0;
        byte next;
        do
        {
            if (position >= input.Length) throw new InvalidDataException("LZ4 length is incomplete.");
            next = input[position++];
            total += next;
        } while (next == 255);
        return total;
    }

    public static List<ProcessSnapshot> ReadProcesses()
    {
        var cim = new Dictionary<int, (int Parent, string Command)>();
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT ProcessId, ParentProcessId, CommandLine FROM Win32_Process");
            foreach (ManagementObject item in searcher.Get())
            {
                var pid = Convert.ToInt32((uint)item["ProcessId"]);
                var parent = Convert.ToInt32((uint)item["ParentProcessId"]);
                cim[pid] = (parent, Convert.ToString(item["CommandLine"]) ?? "");
            }
        }
        catch { }

        var rows = new List<ProcessSnapshot>();
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    var name = process.ProcessName;
                    var extra = cim.TryGetValue(process.Id, out var found) ? found : default;
                    rows.Add(new ProcessSnapshot
                    {
                        Name = name,
                        DisplayName = FriendlyName(name),
                        Pid = process.Id,
                        ParentPid = extra.Parent,
                        PrivateBytes = process.PrivateMemorySize64,
                        WorkingSetBytes = process.WorkingSet64,
                        CommandLine = extra.Command ?? "",
                        WindowTitle = process.MainWindowTitle,
                        Role = RoleFor(name, extra.Command ?? "", process.MainWindowTitle)
                    });
                }
                catch { }
            }
        }
        return rows;
    }

    private static string FriendlyName(string name)
    {
        if (name.Equals("firefox", StringComparison.OrdinalIgnoreCase)) return "Firefox";
        if (name.Equals("msedge", StringComparison.OrdinalIgnoreCase)) return "Microsoft Edge";
        if (name.Equals("chrome", StringComparison.OrdinalIgnoreCase)) return "Google Chrome";
        if (name.Equals("Memory Compression", StringComparison.OrdinalIgnoreCase)) return "Memory Compression";
        return Regex.Replace(name, "([a-z])([A-Z])", "$1 $2");
    }

    private static string RoleFor(string name, string command, string title)
    {
        if (!name.Equals("firefox", StringComparison.OrdinalIgnoreCase))
            return string.IsNullOrWhiteSpace(title) ? "process" : title;
        if (!command.Contains("-contentproc", StringComparison.OrdinalIgnoreCase))
        {
            var activeTitle = Regex.Replace(title ?? "", @"\s*[—-]\s*Mozilla Firefox\s*$", "", RegexOptions.IgnoreCase).Trim();
            return string.IsNullOrWhiteSpace(activeTitle) ? "main browser · windows and UI" : $"main browser · active: {activeTitle}";
        }
        var role = Regex.Match(command, @"-\s+\d+\s+(?<role>tab|gpu|socket|rdd|utility|privilegedabout|extension)", RegexOptions.IgnoreCase);
        if (role.Success)
        {
            return role.Groups["role"].Value.ToLowerInvariant() switch
            {
                "tab" => "web content · one or more tabs",
                "gpu" => "graphics · compositing and WebGL",
                "socket" => "networking · sockets and connections",
                "rdd" => "media · sandboxed audio/video decoding",
                "utility" => "sandboxed utility service",
                "privilegedabout" => "built-in Firefox pages",
                "extension" => "browser extensions",
                var value => value
            };
        }
        if (command.Contains("-isForBrowser", StringComparison.OrdinalIgnoreCase)) return "web content · one or more tabs";
        if (command.Contains("sandboxingKind", StringComparison.OrdinalIgnoreCase)) return "sandboxed utility service";
        return "content process";
    }
}
