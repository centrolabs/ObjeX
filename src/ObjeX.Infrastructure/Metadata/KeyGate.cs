using ObjeX.Core.Interfaces;

namespace ObjeX.Infrastructure.Metadata;

/// <summary>A singleton, so every request of the process sees the same gates.</summary>
public sealed class KeyGate : IKeyGate
{
    private readonly NamedGates _gates = new();

    public async Task<IDisposable> EnterAsync(string bucketName, IEnumerable<string> keys, CancellationToken ctk = default)
    {
        var entered = new Exits();
        try
        {
            foreach (var key in keys.Distinct().Order(StringComparer.Ordinal))
                entered.Add(await _gates.EnterAsync($"{bucketName}/{key}", ctk));
        }
        catch
        {
            entered.Dispose();
            throw;
        }
        return entered;
    }

    private sealed class Exits : List<IDisposable>, IDisposable
    {
        public void Dispose()
        {
            foreach (var exit in this)
                exit.Dispose();
        }
    }
}
