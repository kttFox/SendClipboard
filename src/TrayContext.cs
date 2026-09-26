using System.Runtime.InteropServices;

namespace SendClipboard;

/// <summary>タスクトレイ常駐。探索 (Discovery)・待ち受け (Server)・送信 (Sender) を起動する。</summary>
internal sealed class TrayContext : ApplicationContext, IDeviceHost
{
    private readonly NotifyIcon tray;
    private readonly ClipboardWatcher watcher = new();
    private readonly ToolStripMenuItem pauseItem;
    private readonly ToolStripMenuItem notifyItem;
    private readonly SynchronizationContext uiContext;
    private readonly AppConfig config;
    private Sender? sender;
    private Server? server;
    private Discovery? discovery;
    private SettingsForm? settingsForm;

    // 受信内容を反映した直後のクリップボード変更は自分由来なので送り返さない
    private static readonly TimeSpan EchoSuppression = TimeSpan.FromSeconds(1.5);
    private DateTime lastAppliedAt = DateTime.MinValue;

    public event Action? DevicesChanged;

    public TrayContext()
    {
        uiContext = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        config = AppConfig.Load();

        pauseItem = new ToolStripMenuItem("一時停止", null, (_, _) => pauseItem!.Checked = !pauseItem.Checked);
        notifyItem = new ToolStripMenuItem("通知を表示", null, (_, _) =>
        {
            notifyItem!.Checked = !notifyItem.Checked;
            config.NotifyEnabled = notifyItem.Checked;
            config.Save();
        }) { Checked = config.NotifyEnabled };

        var menu = new ContextMenuStrip();
        menu.Items.Add("設定・ペアリング...", null, (_, _) => ShowSettings());
        menu.Items.Add(pauseItem);
        menu.Items.Add(notifyItem);
        menu.Items.Add("ログを開く", null, (_, _) => OpenLog());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("終了", null, (_, _) => ExitThread());

        tray = new NotifyIcon
        {
            Icon = new Icon(AppIcon.Value, SystemInformation.SmallIconSize),
            ContextMenuStrip = menu,
            Visible = true,
        };
        tray.DoubleClick += (_, _) => ShowSettings();

        watcher.ClipboardChanged += OnClipboardChanged;

        Start();
        if (config.GetPairs().Count == 0)
        {
            // 初回はペアリングのために設定画面を開く
            uiContext.Post(_ => ShowSettings(), null);
        }
    }

    public IReadOnlyList<Peer> Peers => discovery?.Peers ?? Array.Empty<Peer>();

    public List<PairedDevice> Pairs => config.GetPairs();

    public Task<PairedDevice> RequestPairingAsync(Peer peer, Action<string> onCode, CancellationToken ct) =>
        Task.Run(async () =>
        {
            PairedDevice device = await Pairing.RequestAsync(config, peer, onCode, ct);
            config.AddPair(device);
            Log.Write($"ペアリングしました: {device.Name}");
            DevicesChanged?.Invoke();
            return device;
        });

    public void Unpair(string id)
    {
        config.RemovePair(id);
        Log.Write($"ペアを解除しました: {id}");
        DevicesChanged?.Invoke();
    }

    public void Refresh() => discovery?.Refresh();

    private void Start()
    {
        Stop();

        if (config.SendEnabled)
        {
            sender = new Sender(config, GetSendTargets);
            sender.StatusChanged += OnStatus;
        }

        // ペアリング要求を受けるため、受け取るが無効でも待ち受けは起動する
        try
        {
            server = new Server(config, config.ReceiveEnabled, uiContext, AskPairingAsync);
            server.StatusChanged += OnStatus;
            server.Applied += OnApplied;
            server.Paired += _ => DevicesChanged?.Invoke();
        }
        catch (System.Net.Sockets.SocketException e)
        {
            Log.Write($"待ち受けに失敗: {e.Message}");
            tray.ShowBalloonTip(5000, "SendClipboard", $"ポート {config.Port} で待ち受けできません: {e.Message}", ToolTipIcon.Error);
        }

        try
        {
            discovery = new Discovery(config, sender != null, server != null && config.ReceiveEnabled);
            discovery.PeersChanged += OnPeersChanged;
        }
        catch (System.Net.Sockets.SocketException e)
        {
            Log.Write($"探索の開始に失敗: {e.Message}");
            tray.ShowBalloonTip(5000, "SendClipboard", $"LAN内の探索を開始できません: {e.Message}", ToolTipIcon.Error);
        }

        UpdateTrayText();

        // 探索を作り直すと一覧は空から始まるので、設定画面の一覧も更新させる
        DevicesChanged?.Invoke();
    }

    private void Stop()
    {
        sender?.Dispose();
        sender = null;
        server?.Dispose();
        server = null;
        discovery?.Dispose();
        discovery = null;
    }

    /// <summary>ペア済みで「受け取る」が有効な、同じLANのPCすべてが送信先。</summary>
    private IReadOnlyList<SendTarget> GetSendTargets()
    {
        var targets = new List<SendTarget>();
        foreach (Peer p in Peers.Where(p => p.CanReceive))
        {
            PairedDevice? pair = config.FindPair(p.Id);
            if (pair != null)
            {
                targets.Add(new SendTarget(p.Id, p.Addresses, p.Port, pair.KeyBytes, p.Alias));
            }
        }

        return targets;
    }

    /// <summary>相手からのペアリング要求。UI スレッドで許可ダイアログを出す。</summary>
    private Task<bool> AskPairingAsync(string remoteName, string code)
    {
        var result = new TaskCompletionSource<bool>();
        uiContext.Post(
            _ =>
            {
                try
                {
                    using var form = new PairingRequestForm(remoteName, code);
                    result.TrySetResult(form.ShowDialog() == DialogResult.OK);
                }
                catch (Exception e)
                {
                    Log.Write($"ペアリング確認の表示に失敗: {e.Message}");
                    result.TrySetResult(false);
                }
            },
            null);
        return result.Task;
    }

    private void OnPeersChanged()
    {
        uiContext.Post(_ => UpdateTrayText(), null);
        DevicesChanged?.Invoke();
    }

    private void UpdateTrayText()
    {
        var roles = new List<string>();
        if (sender != null)
        {
            roles.Add("送信");
        }

        if (server != null && config.ReceiveEnabled)
        {
            roles.Add("受信");
        }

        var pairIds = config.GetPairs().Select(p => p.Id).ToHashSet();
        int online = Peers.Count(p => pairIds.Contains(p.Id));
        string mode = roles.Count > 0 ? string.Join("+", roles) : "停止中";
        SetTrayText($"{mode} / ペア {online}/{pairIds.Count} 台接続中");
    }

    private async void OnClipboardChanged()
    {
        if (sender == null || pauseItem.Checked)
        {
            return;
        }

        if (DateTime.UtcNow - lastAppliedAt < EchoSuppression)
        {
            return;
        }

        try
        {
            ClipboardPayload payload = await ClipboardPayload.CaptureAsync();
            sender?.Enqueue(payload);
        }
        catch (Exception e)
        {
            Log.Write($"クリップボード取得失敗: {e.Message}");
        }
    }

    private void OnApplied(byte[] serialized)
    {
        lastAppliedAt = DateTime.UtcNow;
        sender?.MarkAsKnown(serialized);
    }

    private void OnStatus(string message, bool success)
    {
        uiContext.Post(
            _ =>
            {
                if (notifyItem.Checked || !success)
                {
                    tray.ShowBalloonTip(success ? 1500 : 4000, "SendClipboard", Truncate(message, 200), success ? ToolTipIcon.Info : ToolTipIcon.Warning);
                }
            },
            null);
    }

    private void ShowSettings()
    {
        if (settingsForm != null)
        {
            settingsForm.Activate();
            return;
        }

        using var form = new SettingsForm(config, this, options =>
        {
            config.ApplyOptions(options);
            config.Save();
            Start();
        });
        settingsForm = form;
        form.ShowDialog();
        settingsForm = null;
    }

    private static void OpenLog()
    {
        string path = Path.Combine(AppConfig.DirectoryPath, "log.txt");
        if (File.Exists(path))
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
        }
    }

    private void SetTrayText(string text) => tray.Text = Truncate($"SendClipboard - {text}", 63);

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";

    protected override void ExitThreadCore()
    {
        Stop();
        watcher.Dispose();
        tray.Visible = false;
        tray.Dispose();
        base.ExitThreadCore();
    }

    /// <summary>AddClipboardFormatListener でクリップボード変更を検知する非表示ウィンドウ。</summary>
    private sealed class ClipboardWatcher : NativeWindow, IDisposable
    {
        private const int WM_CLIPBOARDUPDATE = 0x031D;
        private readonly System.Windows.Forms.Timer debounce = new() { Interval = 300 };

        public event Action? ClipboardChanged;

        public ClipboardWatcher()
        {
            CreateHandle(new CreateParams { Parent = new IntPtr(-3) }); // HWND_MESSAGE
            AddClipboardFormatListener(Handle);

            // コピー操作では短時間に複数回通知が来るのでまとめる
            debounce.Tick += (_, _) =>
            {
                debounce.Stop();
                ClipboardChanged?.Invoke();
            };
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_CLIPBOARDUPDATE)
            {
                debounce.Stop();
                debounce.Start();
            }

            base.WndProc(ref m);
        }

        public void Dispose()
        {
            debounce.Dispose();
            RemoveClipboardFormatListener(Handle);
            DestroyHandle();
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool AddClipboardFormatListener(IntPtr hwnd);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RemoveClipboardFormatListener(IntPtr hwnd);
    }
}
