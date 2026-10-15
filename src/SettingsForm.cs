namespace SendClipboard;

/// <summary>
/// 設定画面。上段はこのPCの設定、下段は同じLANで見つかったPCの一覧とペアリング操作。どちらも変更は即時反映する。
/// </summary>
internal sealed partial class SettingsForm : Form
{
    private readonly IDeviceHost host;
    private readonly Action<AppConfig> apply;
    private AppConfig applied;

    /// <summary>初期値を設定している間は変更イベントで反映しない。</summary>
    private bool loading = true;

    /// <summary>デザイナーで設定した列幅 (96 DPI 基準)。ListView の列幅は自動では拡大されないので DPI に合わせて設定し直す。</summary>
    private readonly int[] baseColumnWidths;

    public SettingsForm(AppConfig current, IDeviceHost host, Action<AppConfig> apply)
    {
        this.host = host;
        this.apply = apply;
        applied = CreateConfig(current);
        InitializeComponent();
        Icon = AppIcon.Value;
        baseColumnWidths = devicesView.Columns.Cast<ColumnHeader>().Select(c => c.Width).ToArray();
        DpiChanged += (_, _) => BeginInvoke(ScaleColumnWidths);

        aliasBox.PlaceholderText = Environment.MachineName;
        aliasBox.Text = current.Alias;
        sendBox.Checked = current.SendEnabled;
        receiveBox.Checked = current.ReceiveEnabled;
        screenshotSendBox.Checked = current.ScreenshotSendEnabled;
        screenshotReceiveBox.Checked = current.ScreenshotReceiveEnabled;
        portBox.Value = current.Port;
        aliasBox.TextChanged += TextOptionChanged;
        loading = false;

        host.DevicesChanged += OnDevicesChanged;
    }

    private void OnDevicesChanged()
    {
        if (IsHandleCreated && !IsDisposed)
        {
            BeginInvoke(RefreshDevices);
        }
    }

    private void ScaleColumnWidths()
    {
        for (int i = 0; i < baseColumnWidths.Length; i++)
        {
            devicesView.Columns[i].Width = LogicalToDeviceUnits(baseColumnWidths[i]);
        }
    }

    private void SettingsForm_Load(object? sender, EventArgs e)
    {
        ScaleColumnWidths();
        RefreshDevices();
        host.Refresh();
    }

    private void SettingsForm_FormClosing(object? sender, FormClosingEventArgs e) => ApplyNow();

    private void SettingsForm_FormClosed(object? sender, FormClosedEventArgs e) => host.DevicesChanged -= OnDevicesChanged;

    private void DevicesView_SelectedIndexChanged(object? sender, EventArgs e) => UpdateButtons();

    private void DevicesView_DoubleClick(object? sender, EventArgs e)
    {
        if (pairButton.Enabled)
        {
            StartPairing();
        }
    }

    private void PairButton_Click(object? sender, EventArgs e) => StartPairing();

    private void UnpairButton_Click(object? sender, EventArgs e) => Unpair();

    private void RefreshButton_Click(object? sender, EventArgs e) => host.Refresh();

    private void ApplyTimer_Tick(object? sender, EventArgs e) => ApplyNow();

    private void OptionCheckedChanged(object? sender, EventArgs e)
    {
        if (!loading)
        {
            ApplyNow();
        }
    }

    // NumericUpDown の ValueChanged はフォーカスが外れるまで発生しないため、入力中の文字でも検知する
    private void TextOptionChanged(object? sender, EventArgs e)
    {
        if (!loading)
        {
            ScheduleApply();
        }
    }

    private string? SelectedId => devicesView.SelectedItems.Count > 0 ? (string)devicesView.SelectedItems[0].Tag! : null;

    /// <summary>見つかったPCとペア済みPC (オフライン含む) を1つの一覧にまとめる。</summary>
    private void RefreshDevices()
    {
        string? selectedId = SelectedId;
        var peers = host.Peers;
        var pairs = host.Pairs;

        devicesView.BeginUpdate();
        devicesView.Items.Clear();
        foreach (Peer p in peers)
        {
            bool paired = pairs.Any(d => d.Id == p.Id);

            var item = new ListViewItem(new[] { p.Alias, p.Address.ToString(), Role(p.CanSend, p.CanReceive), Role(p.CanSendScreenshot, p.CanReceiveScreenshot), paired ? "ペア済み" : "未ペア" }) { Tag = p.Id };
            if (paired)
            {
                item.Font = new Font(devicesView.Font, FontStyle.Bold);
            }

            devicesView.Items.Add(item);
        }

        foreach (PairedDevice d in pairs.Where(d => peers.All(p => p.Id != d.Id)))
        {
            devicesView.Items.Add(new ListViewItem(new[] { d.Name, "-", "-", "-", "ペア済み (オフライン)" })
            {
                Tag = d.Id,
                ForeColor = SystemColors.GrayText,
            });
        }

        foreach (ListViewItem item in devicesView.Items)
        {
            item.Selected = (string)item.Tag! == selectedId;
        }

        devicesView.EndUpdate();
        UpdateButtons();
    }

    private static string Role(bool send, bool receive) => (send, receive) switch
    {
        (true, true) => "送る+受け取る",
        (true, false) => "送る",
        (false, true) => "受け取る",
        _ => "-",
    };

    private void UpdateButtons()
    {
        string? id = SelectedId;
        bool online = id != null && host.Peers.Any(p => p.Id == id);
        bool paired = id != null && host.Pairs.Any(d => d.Id == id);
        pairButton.Enabled = online;
        pairButton.Text = paired ? "再ペアリング" : "ペアリング";
        unpairButton.Enabled = paired;
    }

    private void StartPairing()
    {
        string? id = SelectedId;
        Peer? peer = host.Peers.FirstOrDefault(p => p.Id == id);
        if (peer == null)
        {
            return;
        }

        using var wait = new PairingWaitForm(host, peer);
        if (wait.ShowDialog(this) == DialogResult.OK)
        {
            MessageBox.Show(this, $"「{peer.Alias}」とペアリングしました。", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        RefreshDevices();
    }

    private void Unpair()
    {
        if (devicesView.SelectedItems.Count == 0)
        {
            return;
        }

        ListViewItem item = devicesView.SelectedItems[0];
        string message = $"「{item.Text}」とのペアを解除しますか。\n(相手側のペア情報は相手のPCで解除してください)";
        if (MessageBox.Show(this, message, Text, MessageBoxButtons.OKCancel, MessageBoxIcon.Question) == DialogResult.OK)
        {
            host.Unpair((string)item.Tag!);
            RefreshDevices();
        }
    }

    private void ScheduleApply()
    {
        applyTimer.Stop();
        applyTimer.Start();
    }

    private void ApplyNow()
    {
        applyTimer.Stop();
        var config = new AppConfig
        {
            Alias = aliasBox.Text.Trim(),
            SendEnabled = sendBox.Checked,
            ReceiveEnabled = receiveBox.Checked,
            ScreenshotSendEnabled = screenshotSendBox.Checked,
            ScreenshotReceiveEnabled = screenshotReceiveBox.Checked,
            Port = (int)portBox.Value,
        };

        if (!config.IsValid(out _) || IsSame(config, applied))
        {
            return;
        }

        applied = config;
        apply(config);
    }

    private static AppConfig CreateConfig(AppConfig c) => new()
    {
        Alias = c.Alias,
        SendEnabled = c.SendEnabled,
        ReceiveEnabled = c.ReceiveEnabled,
        ScreenshotSendEnabled = c.ScreenshotSendEnabled,
        ScreenshotReceiveEnabled = c.ScreenshotReceiveEnabled,
        Port = c.Port,
    };

    private static bool IsSame(AppConfig a, AppConfig b) =>
        a.Alias == b.Alias && a.SendEnabled == b.SendEnabled && a.ReceiveEnabled == b.ReceiveEnabled
        && a.ScreenshotSendEnabled == b.ScreenshotSendEnabled && a.ScreenshotReceiveEnabled == b.ScreenshotReceiveEnabled
        && a.Port == b.Port;
}
