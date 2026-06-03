namespace RmaWorker.Configuration;

public sealed class WorkerOptions
{
    public const string SectionName = "Worker";

    public int PollingIntervalSeconds { get; init; } = 60;
}
