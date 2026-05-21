namespace PrintRxerV3.Capture;

public sealed record CapturedPrintJobWatcherOptions
{
    public required CapturedPrintJobProcessor Processor { get; init; }
    public required Action<string> NotifyPackageReady { get; init; }
    public Action<string>? NotifyPackageQueuedLocal { get; init; }
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(2);
    public int MaxIdlePolls { get; init; } = 0;
}
