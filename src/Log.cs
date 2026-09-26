namespace SendClipboard;

internal static class AppIcon
{
    private static Icon? icon;

    /// <summary>埋め込みリソースのアプリアイコン (トレイ・各画面で共用)。</summary>
    public static Icon Value => icon ??= LoadIcon();

    private static Icon LoadIcon()
    {
        using Stream? stream = typeof(AppIcon).Assembly.GetManifestResourceStream("SendClipboard.app.ico");
        return stream != null ? new Icon(stream) : SystemIcons.Application;
    }
}

internal static class Log
{
    private static readonly object Lock = new();
    private static string FilePath => Path.Combine(AppConfig.DirectoryPath, "log.txt");
    private const long MaxBytes = 1024 * 1024;

    public static void Write(string message)
    {
        try
        {
            lock (Lock)
            {
                Directory.CreateDirectory(AppConfig.DirectoryPath);
                var info = new FileInfo(FilePath);
                if (info.Exists && info.Length > MaxBytes)
                {
                    File.Move(FilePath, FilePath + ".old", overwrite: true);
                }

                File.AppendAllText(FilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // ログ失敗でアプリを止めない
        }
    }
}
