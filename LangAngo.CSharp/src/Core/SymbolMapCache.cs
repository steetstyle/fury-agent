namespace LangAngo.CSharp.Core;

/// <summary>Bounded cache for JIT method ranges (start, size, name). Used to resolve instruction pointers to method names for readable call stacks. Lock-based, minimal overhead.</summary>
public sealed class SymbolMapCache
{
    private readonly record struct Entry(ulong Start, uint Size, string Name) : IComparable<Entry>
    {
        public int CompareTo(Entry other) => Start.CompareTo(other.Start);
    }

    private readonly object _lock = new();
    private readonly List<Entry> _sorted = new();
    private const int MaxEntries = 32_000;

    public void Add(ulong startAddress, uint methodSize, string methodName)
    {
        if (string.IsNullOrEmpty(methodName)) return;
        lock (_lock)
        {
            var e = new Entry(startAddress, methodSize, methodName);
            var i = _sorted.BinarySearch(e);
            if (i < 0) i = ~i;
            _sorted.Insert(i, e);
            while (_sorted.Count > MaxEntries)
                _sorted.RemoveAt(0);
        }
    }

    /// <summary>Resolves an instruction pointer to method name. Returns null if not found.</summary>
    public string? Resolve(ulong ip)
    {
        lock (_lock)
        {
            if (_sorted.Count == 0) return null;
            var idx = _sorted.BinarySearch(new Entry(ip, 0, ""));
            if (idx < 0) idx = ~idx - 1;
            if (idx < 0) return null;
            var e = _sorted[idx];
            if (ip >= e.Start && ip < e.Start + e.Size)
                return e.Name;
            return null;
        }
    }

    /// <summary>Resolves a sequence of IPs to a readable call stack string (at Name1\n   at Name2...). Unresolved IPs shown as 0xHEX.</summary>
    public string BuildCallStack(IEnumerable<ulong> ips, int maxFrames = 64, int maxLength = 4096)
    {
        var sb = new System.Text.StringBuilder();
        var len = 0;
        var n = 0;
        foreach (var ip in ips)
        {
            if (n >= maxFrames || len >= maxLength) break;
            var name = Resolve(ip);
            var line = name != null ? $"   at {name}" : $"   at 0x{ip:X}";
            if (len + line.Length + 1 > maxLength) break;
            if (n > 0) sb.AppendLine();
            sb.Append(line);
            len += line.Length + 1;
            n++;
        }
        if (n >= maxFrames) sb.AppendLine().Append("   ... (truncated)");
        return sb.ToString();
    }
}
