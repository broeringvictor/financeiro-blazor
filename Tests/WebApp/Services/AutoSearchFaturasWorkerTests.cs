using Microsoft.Extensions.Logging.Abstractions;
using WebApp.Models;
using WebApp.Models.Enums;
using WebApp.Models.ValueObjects;
using WebApp.Services;

namespace Tests.WebApp.Services;

public class AutoSearchFaturasWorkerTests
{
    private const string UserId = "user-123";

    private static Bill CriarConta(string nome = "Luz - Celesc") =>
        new(UserId, nome, "Celesc", ETransactionCategory.Utilities,
            new RecurrenceRule(ERecurrenceFrequency.Monthly, 1, 10, new DateOnly(2026, 1, 10)));

    private static AutoSearchFaturasWorker NovoWorker() =>
        new(NullLogger<AutoSearchFaturasWorker>.Instance,
            scopeFactory: null!,
            new AutoSearchFaturasOptions());

    [Theory]
    [InlineData("2026-07-01T01:00:00", "03:00", "2026-07-01T03:00:00")]
    [InlineData("2026-07-01T05:00:00", "03:00", "2026-07-02T03:00:00")]
    [InlineData("2026-07-01T03:00:00", "03:00", "2026-07-02T03:00:00")]
    public void CalcularEsperaAteProximaExecucao_AgendaNoHorarioCorreto(string agoraStr, string horarioStr, string esperadoStr)
    {
        var agora = DateTime.Parse(agoraStr);
        var horario = TimeOnly.Parse(horarioStr);
        var esperado = DateTime.Parse(esperadoStr);

        var espera = AutoSearchFaturasWorker.CalcularEsperaAteProximaExecucao(horario, agora);

        Assert.Equal(esperado, agora + espera);
    }

    [Fact]
    public async Task ExecutarVarreduraAsync_ProcessaTodasAsContasMesmoComFalhaEmUma()
    {
        var worker = NovoWorker();
        var contas = new[] { CriarConta("Luz - Celesc"), CriarConta("Água - Casan"), CriarConta("Internet") };
        var processadas = new List<string>();

        var (sucesso, falhas, total) = await worker.ExecutarVarreduraAsync(
            contas,
            (bill, _) =>
            {
                processadas.Add(bill.Name);
                if (bill.Name == "Água - Casan")
                {
                    throw new InvalidOperationException("Falha simulada no Gmail.");
                }

                return Task.FromResult<Invoice?>(null);
            },
            CancellationToken.None);

        Assert.Equal(new[] { "Luz - Celesc", "Água - Casan", "Internet" }, processadas);
        Assert.Equal(3, total);
        Assert.Equal(1, falhas);
        Assert.Equal(0, sucesso);
    }

    [Fact]
    public async Task ExecutarVarreduraAsync_ContaFaturaEncontrada()
    {
        var worker = NovoWorker();
        var contas = new[] { CriarConta() };
        var invoice = new Invoice(UserId, contas[0].Id, new DateOnly(2026, 6, 1), 100m, new DateOnly(2026, 7, 10));

        var (sucesso, falhas, total) = await worker.ExecutarVarreduraAsync(
            contas,
            (_, _) => Task.FromResult<Invoice?>(invoice),
            CancellationToken.None);

        Assert.Equal(1, sucesso);
        Assert.Equal(0, falhas);
        Assert.Equal(1, total);
    }

    [Fact]
    public async Task ExecutarVarreduraAsync_SemContas_NaoFazNada()
    {
        var worker = NovoWorker();

        var (sucesso, falhas, total) = await worker.ExecutarVarreduraAsync(
            [],
            (_, _) => Task.FromResult<Invoice?>(null),
            CancellationToken.None);

        Assert.Equal(0, sucesso);
        Assert.Equal(0, falhas);
        Assert.Equal(0, total);
    }
}
