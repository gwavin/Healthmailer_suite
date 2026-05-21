namespace PrintRxerV3.Capture;

public sealed class PrintRxerV3Log
{
    private readonly string _logsRoot;
    private readonly long _maxLogBytes;
    private readonly int _maxLogFiles;

    public PrintRxerV3Log(string logsRoot, long maxLogBytes, int maxLogFiles)
    {
        _logsRoot = logsRoot;
        _maxLogBytes = maxLogBytes > 0 ? maxLogBytes : 5 * 1024 * 1024;
        _maxLogFiles = maxLogFiles >= 0 ? maxLogFiles : 3;
    }

    public void Write(string message)
    {
        Directory.CreateDirectory(_logsRoot);
        string activePath = Path.Combine(_logsRoot, "printrxer_v3.log");
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
        return Path.Combine(_logsRoot, $"printrxer_v3.{index}.log");
    }
}
