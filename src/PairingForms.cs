namespace SendClipboard;

/// <summary>設定画面から使う、端末一覧とペアリングの操作。TrayContext が実装する。</summary>
internal interface IDeviceHost
{
    event Action? DevicesChanged;

    IReadOnlyList<Peer> Peers { get; }

    List<PairedDevice> Pairs { get; }

    Task<PairedDevice> RequestPairingAsync(Peer peer, Action<string> onCode, CancellationToken ct);

    void Unpair(string id);

    void Refresh();
}

/// <summary>相手からのペアリング要求を許可/拒否するダイアログ。</summary>
internal sealed class PairingRequestForm : Form
{
    public PairingRequestForm(string remoteName, string code)
    {
        Text = "SendClipboard - ペアリング要求";
        Icon = AppIcon.Value;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        TopMost = true;
        ShowInTaskbar = true;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(16);

        var message = new Label
        {
            Text = $"「{remoteName}」がクリップボード共有のペアリングを求めています。\n相手の画面に同じ確認コードが表示されていれば「許可」を押してください。",
            AutoSize = true,
            MaximumSize = new Size(420, 0),
        };
        var codeLabel = new Label
        {
            Text = FormatCode(code),
            AutoSize = true,
            Font = new Font(Font.FontFamily, 24, FontStyle.Bold),
            Margin = new Padding(3, 12, 3, 12),
        };

        var allow = new Button { Text = "許可", DialogResult = DialogResult.OK, AutoSize = true };
        var deny = new Button { Text = "拒否", DialogResult = DialogResult.Cancel, AutoSize = true };
        var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Dock = DockStyle.Fill };
        buttons.Controls.AddRange(new Control[] { deny, allow });

        var root = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true };
        root.Controls.AddRange(new Control[] { message, codeLabel, buttons });
        Controls.Add(root);

        AcceptButton = allow;
        CancelButton = deny;

        // 相手側のタイムアウトに合わせて自動で閉じる
        var timer = new System.Windows.Forms.Timer { Interval = (int)Pairing.UserTimeout.TotalMilliseconds };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            DialogResult = DialogResult.Cancel;
        };
        timer.Start();
        FormClosed += (_, _) => timer.Dispose();
    }

    public static string FormatCode(string code) => $"{code[..3]} {code[3..]}";
}

/// <summary>こちらから要求したとき、相手の許可を待つダイアログ。</summary>
internal sealed class PairingWaitForm : Form
{
    private readonly Label messageLabel = new() { AutoSize = true, MaximumSize = new Size(420, 0) };
    private readonly Label codeLabel = new() { AutoSize = true, Margin = new Padding(3, 12, 3, 12) };
    private readonly Button cancelButton = new() { Text = "キャンセル", AutoSize = true };
    private readonly CancellationTokenSource cts = new();

    public PairingWaitForm(IDeviceHost host, Peer peer)
    {
        Text = "SendClipboard - ペアリング";
        Icon = AppIcon.Value;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(16);
        codeLabel.Font = new Font(Font.FontFamily, 24, FontStyle.Bold);

        messageLabel.Text = $"「{peer.Alias}」に接続しています…";
        cancelButton.Click += (_, _) => Close();

        var root = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true };
        root.Controls.AddRange(new Control[] { messageLabel, codeLabel, cancelButton });
        Controls.Add(root);

        FormClosing += (_, _) => cts.Cancel();
        Shown += async (_, _) =>
        {
            try
            {
                PairedDevice device = await host.RequestPairingAsync(
                    peer,
                    code => BeginInvoke(() =>
                    {
                        messageLabel.Text = $"「{peer.Alias}」の画面に下の確認コードが表示されていることを確認し、相手側で「許可」を押してください。";
                        codeLabel.Text = PairingRequestForm.FormatCode(code);
                    }),
                    cts.Token);

                if (!IsDisposed)
                {
                    DialogResult = DialogResult.OK;
                }
            }
            catch (Exception e) when (!IsDisposed && !cts.IsCancellationRequested)
            {
                messageLabel.Text = $"ペアリングできませんでした: {e.Message}";
                codeLabel.Text = "";
                cancelButton.Text = "閉じる";
            }
            catch
            {
                // キャンセルで閉じた
            }
        };
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            cts.Dispose();
        }

        base.Dispose(disposing);
    }
}
