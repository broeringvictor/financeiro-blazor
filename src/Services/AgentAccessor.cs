using Microsoft.Agents.AI;
using Services.Agents;

namespace Services;

/// <summary>
/// Cria o <see cref="AIAgent"/> sob demanda e o mantém em cache (a criação autentica no
/// Gmail e instancia o cliente da Anthropic, então não deve ser refeita a cada requisição).
/// </summary>
public sealed class AgentAccessor(ContaAgentFactory factory)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private AIAgent? _agent;

    public async Task<AIAgent> GetAsync(CancellationToken ct = default)
    {
        if (_agent is not null)
        {
            return _agent;
        }

        await _gate.WaitAsync(ct);
        try
        {
            return _agent ??= await factory.CriarAsync(ct);
        }
        finally
        {
            _gate.Release();
        }
    }
}
