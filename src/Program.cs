namespace SendClipboard;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        int index = Array.IndexOf(args, "--data-dir");
        if (index >= 0 && index + 1 < args.Length)
        {
            AppConfig.DirectoryPath = Path.GetFullPath(args[index + 1]);
        }

        // 保存先ごとに1つだけ起動できる
        string instanceKey = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(AppConfig.DirectoryPath.ToUpperInvariant())))[..16];
        using var mutex = new Mutex(true, $@"Local\SendClipboard.{instanceKey}", out bool createdNew);
        if (!createdNew)
        {
            MessageBox.Show("SendClipboard は既に起動しています。", "SendClipboard", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.ThreadException += (_, e) => Log.Write($"UI例外: {e.Exception}");
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Write($"未処理例外: {e.ExceptionObject}");
        Application.Run(new TrayContext());
    }
}
