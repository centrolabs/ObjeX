namespace ObjeX.Infrastructure.Metadata;

/// <summary>One gate per name, held by one caller at a time. A gate lives only while someone holds it or waits for it.</summary>
internal sealed class NamedGates
{
    private readonly Dictionary<string, (SemaphoreSlim Gate, int Users)> _gates = [];

    public async Task<IDisposable> EnterAsync(string name, CancellationToken ctk)
    {
        SemaphoreSlim gate;
        lock (_gates)
        {
            var (existing, users) = _gates.GetValueOrDefault(name);
            gate = existing ?? new SemaphoreSlim(1, 1);
            _gates[name] = (gate, users + 1);
        }

        try
        {
            await gate.WaitAsync(ctk);
        }
        catch
        {
            Leave(name);
            throw;
        }
        return new Exit(this, name, gate);
    }

    private void Leave(string name)
    {
        lock (_gates)
        {
            var (gate, users) = _gates[name];
            if (users == 1)
                _gates.Remove(name);
            else
                _gates[name] = (gate, users - 1);
        }
    }

    private sealed class Exit(NamedGates gates, string name, SemaphoreSlim gate) : IDisposable
    {
        public void Dispose()
        {
            gate.Release();
            gates.Leave(name);
        }
    }
}
