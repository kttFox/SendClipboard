namespace SendClipboard;

/// <summary>
/// 設定画面。上段はこのPCの設定、下段は同じLANで見つかったPCの一覧とペアリング操作。どちらも変更は即時反映する。
/// </summary>
internal sealed class SettingsForm : Form
{
    private readonly TextBox aliasBox = new() { Width = 220 };
    private readonly CheckBox sendBox = new() { Text = "送る (このPCのクリップボードを相手へ)", AutoSize = true };
    private readonly CheckBox receiveBox = new() { Text = "受け取る (相手のクリップボードをこのPCへ)", AutoSize = true };
    private readonly NumericUpDown portBox = new() { Minimum = 1, Maximum = 65535, Width = 100 };
    private readonly ListView devicesView = new()
    {
        View = View.Details,
        FullRowSelect = true,
        MultiSelect = false,
        HideSelection = false,
        Width = 520,
        Height = 160,
    };

    private readonly Button pairButton = new() { Text = "ペアリング", AutoSize = true };
    private readonly Button unpairButton = new() { Text = "ペア解除", AutoSize = true };
    private readonly Button refreshButton = new() { Text = "再検索", AutoSize = true };
    private readonly IDeviceHost host;
    private readonly Action<AppConfig> apply;

    /// <summary>入力途中の値で何度も再起動しないよう、文字・数値の変更は少し待ってから反映する。</summary>
    private readonly System.Windows.Forms.Timer applyTimer = new() { Interval = 600 };
    private AppConfig applied;

    public SettingsForm(AppConfig current, IDeviceHost host, Action<AppConfig> apply)
    {
        this.host = host;
        this.apply = apply;
        applied = CreateConfig(current);
        Text = "SendClipboard 設定";
        Icon = AppIcon.Value;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(12);

        aliasBox.PlaceholderText = Environment.MachineName;
        aliasBox.Text = current.Alias;
        sendBox.Checked = current.SendEnabled;
        receiveBox.Checked = current.ReceiveEnabled;
        portBox.Value = current.Port;

        var table = new TableLayoutPanel { ColumnCount = 2, AutoSize = true };
        void AddRow(string label, Control control)
        {
            table.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 7, 12, 3) });
            table.Controls.Add(control);
        }

        var modePanel = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, Margin = Padding.Empty };
        modePanel.Controls.AddRange(new Control[] { sendBox, receiveBox });
        AddRow("表示名", aliasBox);
        AddRow("動作", modePanel);
        AddRow("ポート", portBox);

        devicesView.Columns.Add("名前", 150);
        devicesView.Columns.Add("IPアドレス", 120);
        devicesView.Columns.Add("動作", 110);
        devicesView.Columns.Add("状態", 120);
        devicesView.SelectedIndexChanged += (_, _) => UpdateButtons();
        devicesView.DoubleClick += (_, _) =>
        {
            if (pairButton.Enabled)
            {
                StartPairing();
            }
        };

        pairButton.Click += (_, _) => StartPairing();
        unpairButton.Click += (_, _) => Unpair();
        refreshButton.Click += (_, _) => host.Refresh();
        var deviceButtons = new FlowLayoutPanel { AutoSize = true, Margin = Padding.Empty };
        deviceButtons.Controls.AddRange(new Control[] { pairButton, unpairButton, refreshButton });

        var devicesGroup = new GroupBox { Text = "同じネットワークのPC", AutoSize = true, Padding = new Padding(8) };
        var devicesPanel = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, Dock = DockStyle.Fill };
        devicesPanel.Controls.Add(new Label
        {
            Text = "共有したい相手を選んで「ペアリング」を押し、相手のPCで「許可」を押してください。",
            AutoSize = true,
        });
        devicesPanel.Controls.Add(devicesView);
        devicesPanel.Controls.Add(deviceButtons);
        devicesGroup.Controls.Add(devicesPanel);

        var close = new Button { Text = "閉じる", DialogResult = DialogResult.Cancel, AutoSize = true };
        var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Anchor = AnchorStyles.Right };
        buttons.Controls.Add(close);

        var root = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, Dock = DockStyle.Fill };
        root.Controls.Add(table);
        root.Controls.Add(devicesGroup);
        root.Controls.Add(buttons);
        Controls.Add(root);

        CancelButton = close;

        applyTimer.Tick += (_, _) => ApplyNow();
        aliasBox.TextChanged += (_, _) => ScheduleApply();
        // NumericUpDown の ValueChanged はフォーカスが外れるまで発生しないため、入力中の文字でも検知する
        portBox.TextChanged += (_, _) => ScheduleApply();
        sendBox.CheckedChanged += (_, _) => ApplyNow();
        receiveBox.CheckedChanged += (_, _) => ApplyNow();

        FormClosing += (_, _) => ApplyNow();
        FormClosed += (_, _) => applyTimer.Dispose();

        Action onChanged = () =>
        {
            if (IsHandleCreated && !IsDisposed)
            {
                BeginInvoke(RefreshDevices);
            }
        };
        host.DevicesChanged += onChanged;
        FormClosed += (_, _) => host.DevicesChanged -= onChanged;
        Load += (_, _) =>
        {
            RefreshDevices();
            host.Refresh();
        };
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
            string role = (p.CanSend, p.CanReceive) switch
            {
                (true, true) => "送る+受け取る",
                (true, false) => "送る",
                (false, true) => "受け取る",
                _ => "-",
            };
            var item = new ListViewItem(new[] { p.Alias, p.Address.ToString(), role, paired ? "ペア済み" : "未ペア" }) { Tag = p.Id };
            if (paired)
            {
                item.Font = new Font(devicesView.Font, FontStyle.Bold);
            }

            devicesView.Items.Add(item);
        }

        foreach (PairedDevice d in pairs.Where(d => peers.All(p => p.Id != d.Id)))
        {
            devicesView.Items.Add(new ListViewItem(new[] { d.Name, "-", "-", "ペア済み (オフライン)" })
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
        Port = c.Port,
    };

    private static bool IsSame(AppConfig a, AppConfig b) =>
        a.Alias == b.Alias && a.SendEnabled == b.SendEnabled && a.ReceiveEnabled == b.ReceiveEnabled
        && a.Port == b.Port;
}
