namespace PrintRxerV3.Capture;

public sealed class CapturedPrintJobWatcher
{
    private readonly CapturedPrintJobWatcherOptions _options;

    public CapturedPrintJobWatcher(CapturedPrintJobWatcherOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public int RunUntilIdle()
    {
        int processedCount = 0;
        int idlePolls = 0;

        while (_options.MaxIdlePolls <= 0 || idlePolls < _options.MaxIdlePolls)
        {
            CapturedPrintJobResult? result = _options.Processor.ProcessOne();
            if (result is null)
            {
                idlePolls++;
                Sleep();
                continue;
            }

            idlePolls = 0;
            processedCount++;
            if (result.PackageCreated && !string.IsNullOrWhiteSpace(result.PackageDirectory))
            {
                _options.NotifyPackageReady(result.PackageDirectory);
            }
            else if (result.Outcome.Equals("PackagePublishDeferred", StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(result.LocalPackageDirectory))
            {
                _options.NotifyPackageQueuedLocal?.Invoke(result.LocalPackageDirectory);
            }
        }

        return processedCount;
    }

    public void RunUntilCancelled(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            CapturedPrintJobResult? result = _options.Processor.ProcessOne();
            if (result is not null)
            {
                if (result.PackageCreated && !string.IsNullOrWhiteSpace(result.PackageDirectory))
                {
                    _options.NotifyPackageReady(result.PackageDirectory);
                }
                else if (result.Outcome.Equals("PackagePublishDeferred", StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(result.LocalPackageDirectory))
                {
                    _options.NotifyPackageQueuedLocal?.Invoke(result.LocalPackageDirectory);
                }
                continue;
            }

            Sleep(cancellationToken);
        }
    }

    private void Sleep(CancellationToken cancellationToken = default)
    {
        if (_options.PollInterval <= TimeSpan.Zero)
        {
            return;
        }

        try
        {
            Task.Delay(_options.PollInterval, cancellationToken).Wait(cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
    }
}
