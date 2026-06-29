using WebApp.Models.Enums;

namespace WebApp.Models.ValueObjects;

/// <summary>
/// Regra de recorrência de uma <see cref="Bill"/>. Value object imutável, mapeado pelo EF como owned type.
/// </summary>
public sealed record RecurrenceRule
{
    public ERecurrenceFrequency Frequency { get; private set; }

    /// <summary>A cada quantos períodos a recorrência ocorre (1 = todo mês/semana/ano).</summary>
    public int Interval { get; private set; }

    /// <summary>Dia do vencimento (1–31, usado em Monthly/Yearly).</summary>
    public int DueDay { get; private set; }

    public DateOnly StartDate { get; private set; }

    public DateOnly? EndDate { get; private set; }

    /// <summary>Construtor usado pelo Entity Framework (owned type).</summary>
    private RecurrenceRule() { }

    public RecurrenceRule(
        ERecurrenceFrequency frequency,
        int interval,
        int dueDay,
        DateOnly startDate,
        DateOnly? endDate = null)
    {
        if (interval < 1)
            throw new ArgumentOutOfRangeException(nameof(interval), "O intervalo deve ser maior ou igual a 1.");

        if (frequency != ERecurrenceFrequency.Weekly && dueDay is < 1 or > 31)
            throw new ArgumentOutOfRangeException(nameof(dueDay), "O dia de vencimento deve estar entre 1 e 31.");

        if (endDate is { } end && end < startDate)
            throw new ArgumentException("EndDate não pode ser anterior a StartDate.", nameof(endDate));

        Frequency = frequency;
        Interval = interval;
        DueDay = dueDay;
        StartDate = startDate;
        EndDate = endDate;
    }

    /// <summary>Indica se a recorrência está ativa na data informada (dentro de Start/End).</summary>
    public bool IsActiveOn(DateOnly date) =>
        date >= StartDate && (EndDate is null || date <= EndDate);

    /// <summary>Próximo vencimento estritamente posterior a <paramref name="fromDate"/>.</summary>
    public DateOnly NextDueDateAfter(DateOnly fromDate)
    {
        // Limite de segurança: ~100 anos de ocorrências mensais.
        for (var i = 0; i < 1200; i++)
        {
            var occurrence = OccurrenceFromStart(i);

            if (EndDate is { } end && occurrence > end)
                break;

            if (occurrence > fromDate)
                return occurrence;
        }

        // Sem próxima ocorrência válida (recorrência encerrada): devolve o último vencimento possível.
        return OccurrenceFromStart(0);
    }

    /// <summary>Competência (1º dia do mês) esperada para a data informada.</summary>
    public DateOnly ExpectedReference(DateOnly today) => new(today.Year, today.Month, 1);

    private DateOnly OccurrenceFromStart(int index) => Frequency switch
    {
        ERecurrenceFrequency.Monthly => DueDateInMonth(StartDate.AddMonths(index * Interval)),
        ERecurrenceFrequency.Yearly => DueDateInMonth(StartDate.AddYears(index * Interval)),
        ERecurrenceFrequency.Weekly => StartDate.AddDays(index * Interval * 7),
        _ => StartDate,
    };

    private DateOnly DueDateInMonth(DateOnly anyDayInMonth)
    {
        var days = DateTime.DaysInMonth(anyDayInMonth.Year, anyDayInMonth.Month);
        return new DateOnly(anyDayInMonth.Year, anyDayInMonth.Month, Math.Min(DueDay, days));
    }
}
