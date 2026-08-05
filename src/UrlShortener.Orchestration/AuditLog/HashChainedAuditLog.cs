using System.Security.Cryptography;
using System.Text;

namespace UrlShortener.Orchestration.AuditLog;

// Append-only log where each entry embeds SHA-256 of the previous entry.
// Tampering with any past entry breaks the chain — verify_chain() equivalent.
public class HashChainedAuditLog : IAuditLog
{
    private readonly List<AuditEntry> _entries = new();
    private readonly object _lock = new();

    public IReadOnlyList<AuditEntry> Entries
    {
        get { lock (_lock) return _entries.AsReadOnly(); }
    }

    public Task AppendAsync(AuditEventType eventType, string stageName, string message,
        Dictionary<string, string>? metadata = null)
    {
        lock (_lock)
        {
            var previousHash = _entries.Count > 0 ? _entries[^1].Hash : string.Empty;
            var entry = new AuditEntry
            {
                Sequence = _entries.Count + 1,
                EventType = eventType,
                StageName = stageName,
                Message = message,
                Metadata = metadata ?? new(),
                PreviousHash = previousHash
            };
            entry.Hash = ComputeHash(entry.ToHashInput());
            _entries.Add(entry);
        }
        return Task.CompletedTask;
    }

    public bool VerifyChain()
    {
        lock (_lock)
        {
            for (int i = 0; i < _entries.Count; i++)
            {
                var entry = _entries[i];

                // Recompute this entry's hash and confirm it matches
                if (entry.Hash != ComputeHash(entry.ToHashInput()))
                    return false;

                // Confirm PreviousHash links correctly
                var expectedPrev = i == 0 ? string.Empty : _entries[i - 1].Hash;
                if (entry.PreviousHash != expectedPrev)
                    return false;
            }
            return true;
        }
    }

    private static string ComputeHash(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
