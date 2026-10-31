namespace VehicleMotionCues;

/// <summary>Tiny append-only log in %APPDATA%\VehicleMotionCues\log.txt for diagnosing field reports.</summary>
public static class Log
{
    static readonly object Gate = new();
    static readonly string FilePath = Path.Combine(Settings.Directory, "log.txt");

    public static void Write(string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Settings.Directory);
                var info = new FileInfo(FilePath);
                if (info.Exists && info.Length > 256 * 1024)
                    info.Delete();
                File.AppendAllText(FilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}");
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
