using WebApp.Models.Enums;
using WebApp.Models.ValueObjects;

namespace Tests.WebApp.Models;

public class RecurrenceRuleTests
{
    private static readonly DateOnly Start = new(2026, 1, 10);

    [Fact]
    public void Create_ComIntervaloInvalido_DeveLancar()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new RecurrenceRule(ERecurrenceFrequency.Monthly, 0, 10, Start));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(32)]
    public void Create_ComDiaDeVencimentoInvalido_DeveLancar(int dueDay)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new RecurrenceRule(ERecurrenceFrequency.Monthly, 1, dueDay, Start));
    }

    [Fact]
    public void OccurrenceDate_Mensal_RetornaDataDaNesimaOcorrencia()
    {
        var regra = new RecurrenceRule(ERecurrenceFrequency.Monthly, 1, 10, Start);

        Assert.Equal(Start, regra.OccurrenceDate(1));                 // 1ª = o próprio início
        Assert.Equal(new DateOnly(2026, 11, 10), regra.OccurrenceDate(11)); // 11ª = início + 10 meses
    }

    [Fact]
    public void OccurrenceDate_MenorQueUm_DeveLancar()
    {
        var regra = new RecurrenceRule(ERecurrenceFrequency.Monthly, 1, 10, Start);

        Assert.Throws<ArgumentOutOfRangeException>(() => regra.OccurrenceDate(0));
    }

    [Fact]
    public void Create_ComEndDateAnteriorAoStart_DeveLancar()
    {
        Assert.Throws<ArgumentException>(() =>
            new RecurrenceRule(ERecurrenceFrequency.Monthly, 1, 10, Start, Start.AddDays(-1)));
    }

    [Fact]
    public void NextDueDateAfter_Mensal_DeveRetornarProximoVencimento()
    {
        var rule = new RecurrenceRule(ERecurrenceFrequency.Monthly, 1, 10, Start);

        var next = rule.NextDueDateAfter(new DateOnly(2026, 3, 5));

        Assert.Equal(new DateOnly(2026, 3, 10), next);
    }

    [Fact]
    public void NextDueDateAfter_Mensal_AjustaDiaParaMesCurto()
    {
        // DueDay 31 em fevereiro deve cair no último dia do mês.
        var rule = new RecurrenceRule(ERecurrenceFrequency.Monthly, 1, 31, new DateOnly(2026, 1, 31));

        var next = rule.NextDueDateAfter(new DateOnly(2026, 2, 1));

        Assert.Equal(new DateOnly(2026, 2, 28), next);
    }

    [Fact]
    public void NextDueDateAfter_ComIntervaloDois_PulaUmMes()
    {
        var rule = new RecurrenceRule(ERecurrenceFrequency.Monthly, 2, 10, Start);

        var next = rule.NextDueDateAfter(new DateOnly(2026, 1, 10));

        Assert.Equal(new DateOnly(2026, 3, 10), next);
    }

    [Fact]
    public void IsActiveOn_RespeitaIntervaloStartEnd()
    {
        var rule = new RecurrenceRule(ERecurrenceFrequency.Monthly, 1, 10, Start, new DateOnly(2026, 6, 30));

        Assert.False(rule.IsActiveOn(new DateOnly(2025, 12, 31)));
        Assert.True(rule.IsActiveOn(new DateOnly(2026, 3, 10)));
        Assert.False(rule.IsActiveOn(new DateOnly(2026, 7, 1)));
    }

    [Fact]
    public void ExpectedReference_RetornaPrimeiroDiaDoMes()
    {
        var rule = new RecurrenceRule(ERecurrenceFrequency.Monthly, 1, 10, Start);

        Assert.Equal(new DateOnly(2026, 5, 1), rule.ExpectedReference(new DateOnly(2026, 5, 23)));
    }
}
