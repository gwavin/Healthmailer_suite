namespace HealthMailer;

public sealed class HealthMailerLog
{
    private readonly string _logsRoot;
    private readonly long _maxLogBytes;
    private readonly int _maxLogFiles;

    public HealthMailerLog(string logsRoot, LoggingOptions? options = null)
    {
        _logsRoot = logsRoot;
        LoggingOptions normalized = options ?? new LoggingOptions();
        normalized.Normalize();
        _maxLogBytes = normalized.MaxLogBytes;
        _maxLogFiles = normalized.MaxLogFiles;
    }

    public void Write(string message)
    {
        Directory.CreateDirectory(_logsRoot);
        string activePath = Path.Combine(_logsRoot, "healthmailer.log");
        RotateIfNeeded(activePath);
        File.AppendAllText(activePath, $"[{DateTimeOffset.Now:O}] {message}{Environment.NewLine}");
        RotateIfNeeded(activePath);
    }

    private void RotateIfNeeded(string activePath)
    {
        if (!File.Exists(activePath) || new FileInfo(activePath).Length <= _maxLogBytes)
        {
            return;
        }

        if (_maxLogFiles <= 0)
        {
            File.Delete(activePath);
            return;
        }

        string oldest = RotatedPath(_maxLogFiles);
        if (File.Exists(oldest))
        {
            File.Delete(oldest);
        }

        for (int index = _maxLogFiles - 1; index >= 1; index--)
        {
            string source = RotatedPath(index);
            if (File.Exists(source))
            {
                File.Move(source, RotatedPath(index + 1), overwrite: true);
            }
        }

        File.Move(activePath, RotatedPath(1), overwrite: true);
    }

    private string RotatedPath(int index)
    {
        return Path.Combine(_logsRoot, $"healthmailer.{index}.log");
    }
}
