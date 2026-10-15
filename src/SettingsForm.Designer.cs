#nullable disable

namespace SendClipboard
{
    partial class SettingsForm
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

		#region Windows Form Designer generated code

		/// <summary>
		/// Required method for Designer support - do not modify
		/// the contents of this method with the code editor.
		/// </summary>
		private void InitializeComponent() {
			this.components = new System.ComponentModel.Container();
			this.rootPanel = new FlowLayoutPanel();
			this.settingsTable = new TableLayoutPanel();
			this.aliasLabel = new Label();
			this.aliasBox = new TextBox();
			this.modeLabel = new Label();
			this.modeTable = new TableLayoutPanel();
			this.textHeaderLabel = new Label();
			this.screenshotHeaderLabel = new Label();
			this.sendBox = new CheckBox();
			this.screenshotSendBox = new CheckBox();
			this.receiveBox = new CheckBox();
			this.screenshotReceiveBox = new CheckBox();
			this.portLabel = new Label();
			this.portBox = new NumericUpDown();
			this.devicesGroup = new GroupBox();
			this.devicesPanel = new FlowLayoutPanel();
			this.devicesHintLabel = new Label();
			this.devicesView = new ListView();
			this.nameColumn = new ColumnHeader();
			this.addressColumn = new ColumnHeader();
			this.textRoleColumn = new ColumnHeader();
			this.screenshotRoleColumn = new ColumnHeader();
			this.stateColumn = new ColumnHeader();
			this.deviceButtons = new FlowLayoutPanel();
			this.pairButton = new Button();
			this.unpairButton = new Button();
			this.refreshButton = new Button();
			this.closeButtons = new FlowLayoutPanel();
			this.closeButton = new Button();
			this.toolTip = new ToolTip( this.components );
			this.applyTimer = new System.Windows.Forms.Timer( this.components );
			this.rootPanel.SuspendLayout();
			this.settingsTable.SuspendLayout();
			this.modeTable.SuspendLayout();
			( (System.ComponentModel.ISupportInitialize)this.portBox ).BeginInit();
			this.devicesGroup.SuspendLayout();
			this.devicesPanel.SuspendLayout();
			this.deviceButtons.SuspendLayout();
			this.closeButtons.SuspendLayout();
			this.SuspendLayout();
			// 
			// rootPanel
			// 
			this.rootPanel.AutoSize = true;
			this.rootPanel.Controls.Add( this.settingsTable );
			this.rootPanel.Controls.Add( this.devicesGroup );
			this.rootPanel.Controls.Add( this.closeButtons );
			this.rootPanel.Dock = DockStyle.Fill;
			this.rootPanel.FlowDirection = FlowDirection.TopDown;
			this.rootPanel.Location = new Point( 12, 12 );
			this.rootPanel.Name = "rootPanel";
			this.rootPanel.Size = new Size( 544, 430 );
			this.rootPanel.TabIndex = 0;
			// 
			// settingsTable
			// 
			this.settingsTable.AutoSize = true;
			this.settingsTable.ColumnCount = 2;
			this.settingsTable.ColumnStyles.Add( new ColumnStyle() );
			this.settingsTable.ColumnStyles.Add( new ColumnStyle() );
			this.settingsTable.Controls.Add( this.aliasLabel, 0, 0 );
			this.settingsTable.Controls.Add( this.aliasBox, 1, 0 );
			this.settingsTable.Controls.Add( this.modeLabel, 0, 1 );
			this.settingsTable.Controls.Add( this.modeTable, 1, 1 );
			this.settingsTable.Controls.Add( this.portLabel, 0, 2 );
			this.settingsTable.Controls.Add( this.portBox, 1, 2 );
			this.settingsTable.Location = new Point( 3, 3 );
			this.settingsTable.Name = "settingsTable";
			this.settingsTable.RowCount = 3;
			this.settingsTable.RowStyles.Add( new RowStyle() );
			this.settingsTable.RowStyles.Add( new RowStyle() );
			this.settingsTable.RowStyles.Add( new RowStyle() );
			this.settingsTable.Size = new Size( 284, 130 );
			this.settingsTable.TabIndex = 0;
			// 
			// aliasLabel
			// 
			this.aliasLabel.AutoSize = true;
			this.aliasLabel.Location = new Point( 3, 7 );
			this.aliasLabel.Margin = new Padding( 3, 7, 12, 3 );
			this.aliasLabel.Name = "aliasLabel";
			this.aliasLabel.Size = new Size( 43, 15 );
			this.aliasLabel.TabIndex = 0;
			this.aliasLabel.Text = "表示名";
			// 
			// aliasBox
			// 
			this.aliasBox.Location = new Point( 61, 3 );
			this.aliasBox.Name = "aliasBox";
			this.aliasBox.Size = new Size( 220, 23 );
			this.aliasBox.TabIndex = 0;
			// 
			// modeLabel
			// 
			this.modeLabel.AutoSize = true;
			this.modeLabel.Location = new Point( 3, 36 );
			this.modeLabel.Margin = new Padding( 3, 7, 12, 3 );
			this.modeLabel.Name = "modeLabel";
			this.modeLabel.Size = new Size( 31, 15 );
			this.modeLabel.TabIndex = 1;
			this.modeLabel.Text = "動作";
			// 
			// modeTable
			// 
			this.modeTable.AutoSize = true;
			this.modeTable.ColumnCount = 2;
			this.modeTable.ColumnStyles.Add( new ColumnStyle() );
			this.modeTable.ColumnStyles.Add( new ColumnStyle() );
			this.modeTable.Controls.Add( this.textHeaderLabel, 0, 0 );
			this.modeTable.Controls.Add( this.screenshotHeaderLabel, 1, 0 );
			this.modeTable.Controls.Add( this.sendBox, 0, 1 );
			this.modeTable.Controls.Add( this.screenshotSendBox, 1, 1 );
			this.modeTable.Controls.Add( this.receiveBox, 0, 2 );
			this.modeTable.Controls.Add( this.screenshotReceiveBox, 1, 2 );
			this.modeTable.Location = new Point( 58, 29 );
			this.modeTable.Margin = new Padding( 0 );
			this.modeTable.Name = "modeTable";
			this.modeTable.RowCount = 3;
			this.modeTable.RowStyles.Add( new RowStyle() );
			this.modeTable.RowStyles.Add( new RowStyle() );
			this.modeTable.RowStyles.Add( new RowStyle() );
			this.modeTable.Size = new Size( 184, 72 );
			this.modeTable.TabIndex = 1;
			// 
			// textHeaderLabel
			// 
			this.textHeaderLabel.AutoSize = true;
			this.textHeaderLabel.Location = new Point( 3, 7 );
			this.textHeaderLabel.Margin = new Padding( 3, 7, 3, 0 );
			this.textHeaderLabel.Name = "textHeaderLabel";
			this.textHeaderLabel.Size = new Size( 42, 15 );
			this.textHeaderLabel.TabIndex = 0;
			this.textHeaderLabel.Text = "テキスト";
			// 
			// screenshotHeaderLabel
			// 
			this.screenshotHeaderLabel.AutoSize = true;
			this.screenshotHeaderLabel.Location = new Point( 98, 7 );
			this.screenshotHeaderLabel.Margin = new Padding( 24, 7, 3, 0 );
			this.screenshotHeaderLabel.Name = "screenshotHeaderLabel";
			this.screenshotHeaderLabel.Size = new Size( 83, 15 );
			this.screenshotHeaderLabel.TabIndex = 1;
			this.screenshotHeaderLabel.Text = "スクリーンショット";
			// 
			// sendBox
			// 
			this.sendBox.AutoSize = true;
			this.sendBox.Location = new Point( 3, 25 );
			this.sendBox.Name = "sendBox";
			this.sendBox.Size = new Size( 47, 19 );
			this.sendBox.TabIndex = 0;
			this.sendBox.Text = "送る";
			this.toolTip.SetToolTip( this.sendBox, "このPCのクリップボードを相手へ" );
			this.sendBox.CheckedChanged +=  this.OptionCheckedChanged ;
			// 
			// screenshotSendBox
			// 
			this.screenshotSendBox.AutoSize = true;
			this.screenshotSendBox.Location = new Point( 98, 25 );
			this.screenshotSendBox.Margin = new Padding( 24, 3, 3, 3 );
			this.screenshotSendBox.Name = "screenshotSendBox";
			this.screenshotSendBox.Size = new Size( 47, 19 );
			this.screenshotSendBox.TabIndex = 1;
			this.screenshotSendBox.Text = "送る";
			this.toolTip.SetToolTip( this.screenshotSendBox, "トレイメニューで選んだウインドウを相手へ" );
			this.screenshotSendBox.CheckedChanged +=  this.OptionCheckedChanged ;
			// 
			// receiveBox
			// 
			this.receiveBox.AutoSize = true;
			this.receiveBox.Location = new Point( 3, 50 );
			this.receiveBox.Name = "receiveBox";
			this.receiveBox.Size = new Size( 68, 19 );
			this.receiveBox.TabIndex = 2;
			this.receiveBox.Text = "受け取る";
			this.toolTip.SetToolTip( this.receiveBox, "相手のクリップボードをこのPCへ" );
			this.receiveBox.CheckedChanged +=  this.OptionCheckedChanged ;
			// 
			// screenshotReceiveBox
			// 
			this.screenshotReceiveBox.AutoSize = true;
			this.screenshotReceiveBox.Location = new Point( 98, 50 );
			this.screenshotReceiveBox.Margin = new Padding( 24, 3, 3, 3 );
			this.screenshotReceiveBox.Name = "screenshotReceiveBox";
			this.screenshotReceiveBox.Size = new Size( 68, 19 );
			this.screenshotReceiveBox.TabIndex = 3;
			this.screenshotReceiveBox.Text = "受け取る";
			this.toolTip.SetToolTip( this.screenshotReceiveBox, "相手のスクリーンショットをこのPCのクリップボードへ" );
			this.screenshotReceiveBox.CheckedChanged +=  this.OptionCheckedChanged ;
			// 
			// portLabel
			// 
			this.portLabel.AutoSize = true;
			this.portLabel.Location = new Point( 3, 108 );
			this.portLabel.Margin = new Padding( 3, 7, 12, 3 );
			this.portLabel.Name = "portLabel";
			this.portLabel.Size = new Size( 33, 15 );
			this.portLabel.TabIndex = 2;
			this.portLabel.Text = "ポート";
			// 
			// portBox
			// 
			this.portBox.Location = new Point( 61, 104 );
			this.portBox.Maximum = new decimal( new int[] { 65535, 0, 0, 0 } );
			this.portBox.Minimum = new decimal( new int[] { 1, 0, 0, 0 } );
			this.portBox.Name = "portBox";
			this.portBox.Size = new Size( 100, 23 );
			this.portBox.TabIndex = 2;
			this.portBox.Value = new decimal( new int[] { 1, 0, 0, 0 } );
			this.portBox.TextChanged +=  this.TextOptionChanged ;
			// 
			// devicesGroup
			// 
			this.devicesGroup.AutoSize = true;
			this.devicesGroup.Controls.Add( this.devicesPanel );
			this.devicesGroup.Location = new Point( 3, 139 );
			this.devicesGroup.Name = "devicesGroup";
			this.devicesGroup.Padding = new Padding( 8 );
			this.devicesGroup.Size = new Size( 525, 244 );
			this.devicesGroup.TabIndex = 1;
			this.devicesGroup.TabStop = false;
			this.devicesGroup.Text = "同じネットワークのPC";
			// 
			// devicesPanel
			// 
			this.devicesPanel.AutoSize = true;
			this.devicesPanel.Controls.Add( this.devicesHintLabel );
			this.devicesPanel.Controls.Add( this.devicesView );
			this.devicesPanel.Controls.Add( this.deviceButtons );
			this.devicesPanel.Dock = DockStyle.Fill;
			this.devicesPanel.FlowDirection = FlowDirection.TopDown;
			this.devicesPanel.Location = new Point( 8, 24 );
			this.devicesPanel.Name = "devicesPanel";
			this.devicesPanel.Size = new Size( 509, 212 );
			this.devicesPanel.TabIndex = 0;
			// 
			// devicesHintLabel
			// 
			this.devicesHintLabel.AutoSize = true;
			this.devicesHintLabel.Location = new Point( 3, 0 );
			this.devicesHintLabel.Name = "devicesHintLabel";
			this.devicesHintLabel.Size = new Size( 391, 15 );
			this.devicesHintLabel.TabIndex = 0;
			this.devicesHintLabel.Text = "共有したい相手を選んで「ペアリング」を押し、相手のPCで「許可」を押してください。";
			// 
			// devicesView
			// 
			this.devicesView.Columns.AddRange( new ColumnHeader[] { this.nameColumn, this.addressColumn, this.textRoleColumn, this.screenshotRoleColumn, this.stateColumn } );
			this.devicesView.FullRowSelect = true;
			this.devicesView.Location = new Point( 3, 18 );
			this.devicesView.MultiSelect = false;
			this.devicesView.Name = "devicesView";
			this.devicesView.Size = new Size( 503, 160 );
			this.devicesView.TabIndex = 0;
			this.devicesView.UseCompatibleStateImageBehavior = false;
			this.devicesView.View = View.Details;
			this.devicesView.SelectedIndexChanged +=  this.DevicesView_SelectedIndexChanged ;
			this.devicesView.DoubleClick +=  this.DevicesView_DoubleClick ;
			// 
			// nameColumn
			// 
			this.nameColumn.Text = "名前";
			this.nameColumn.Width = 120;
			// 
			// addressColumn
			// 
			this.addressColumn.Text = "IPアドレス";
			this.addressColumn.Width = 95;
			// 
			// textRoleColumn
			// 
			this.textRoleColumn.Text = "テキスト";
			this.textRoleColumn.Width = 95;
			// 
			// screenshotRoleColumn
			// 
			this.screenshotRoleColumn.Text = "スクリーンショット";
			this.screenshotRoleColumn.Width = 95;
			// 
			// stateColumn
			// 
			this.stateColumn.Text = "状態";
			this.stateColumn.Width = 70;
			// 
			// deviceButtons
			// 
			this.deviceButtons.AutoSize = true;
			this.deviceButtons.Controls.Add( this.pairButton );
			this.deviceButtons.Controls.Add( this.unpairButton );
			this.deviceButtons.Controls.Add( this.refreshButton );
			this.deviceButtons.Location = new Point( 0, 181 );
			this.deviceButtons.Margin = new Padding( 0 );
			this.deviceButtons.Name = "deviceButtons";
			this.deviceButtons.Size = new Size( 243, 31 );
			this.deviceButtons.TabIndex = 1;
			// 
			// pairButton
			// 
			this.pairButton.AutoSize = true;
			this.pairButton.Location = new Point( 3, 3 );
			this.pairButton.Name = "pairButton";
			this.pairButton.Size = new Size( 75, 25 );
			this.pairButton.TabIndex = 0;
			this.pairButton.Text = "ペアリング";
			this.pairButton.Click +=  this.PairButton_Click ;
			// 
			// unpairButton
			// 
			this.unpairButton.AutoSize = true;
			this.unpairButton.Location = new Point( 84, 3 );
			this.unpairButton.Name = "unpairButton";
			this.unpairButton.Size = new Size( 75, 25 );
			this.unpairButton.TabIndex = 1;
			this.unpairButton.Text = "ペア解除";
			this.unpairButton.Click +=  this.UnpairButton_Click ;
			// 
			// refreshButton
			// 
			this.refreshButton.AutoSize = true;
			this.refreshButton.Location = new Point( 165, 3 );
			this.refreshButton.Name = "refreshButton";
			this.refreshButton.Size = new Size( 75, 25 );
			this.refreshButton.TabIndex = 2;
			this.refreshButton.Text = "再検索";
			this.refreshButton.Click +=  this.RefreshButton_Click ;
			// 
			// closeButtons
			// 
			this.closeButtons.Anchor = AnchorStyles.Right;
			this.closeButtons.AutoSize = true;
			this.closeButtons.Controls.Add( this.closeButton );
			this.closeButtons.FlowDirection = FlowDirection.RightToLeft;
			this.closeButtons.Location = new Point( 447, 389 );
			this.closeButtons.Name = "closeButtons";
			this.closeButtons.Size = new Size( 81, 31 );
			this.closeButtons.TabIndex = 2;
			// 
			// closeButton
			// 
			this.closeButton.AutoSize = true;
			this.closeButton.DialogResult = DialogResult.Cancel;
			this.closeButton.Location = new Point( 3, 3 );
			this.closeButton.Name = "closeButton";
			this.closeButton.Size = new Size( 75, 25 );
			this.closeButton.TabIndex = 0;
			this.closeButton.Text = "閉じる";
			// 
			// applyTimer
			// 
			this.applyTimer.Interval = 600;
			this.applyTimer.Tick +=  this.ApplyTimer_Tick ;
			// 
			// SettingsForm
			// 
			this.AutoScaleDimensions = new SizeF( 7F, 15F );
			this.AutoScaleMode = AutoScaleMode.Font;
			this.AutoSize = true;
			this.AutoSizeMode = AutoSizeMode.GrowAndShrink;
			this.CancelButton = this.closeButton;
			this.ClientSize = new Size( 568, 454 );
			this.Controls.Add( this.rootPanel );
			this.FormBorderStyle = FormBorderStyle.FixedDialog;
			this.MaximizeBox = false;
			this.MinimizeBox = false;
			this.Name = "SettingsForm";
			this.Padding = new Padding( 12 );
			this.StartPosition = FormStartPosition.CenterScreen;
			this.Text = "SendClipboard 設定";
			this.FormClosing +=  this.SettingsForm_FormClosing ;
			this.FormClosed +=  this.SettingsForm_FormClosed ;
			this.Load +=  this.SettingsForm_Load ;
			this.rootPanel.ResumeLayout( false );
			this.rootPanel.PerformLayout();
			this.settingsTable.ResumeLayout( false );
			this.settingsTable.PerformLayout();
			this.modeTable.ResumeLayout( false );
			this.modeTable.PerformLayout();
			( (System.ComponentModel.ISupportInitialize)this.portBox ).EndInit();
			this.devicesGroup.ResumeLayout( false );
			this.devicesGroup.PerformLayout();
			this.devicesPanel.ResumeLayout( false );
			this.devicesPanel.PerformLayout();
			this.deviceButtons.ResumeLayout( false );
			this.deviceButtons.PerformLayout();
			this.closeButtons.ResumeLayout( false );
			this.closeButtons.PerformLayout();
			this.ResumeLayout( false );
			this.PerformLayout();
		}

		#endregion

		private FlowLayoutPanel rootPanel;
        private TableLayoutPanel settingsTable;
        private Label aliasLabel;
        private TextBox aliasBox;
        private Label modeLabel;
        private TableLayoutPanel modeTable;
        private Label textHeaderLabel;
        private Label screenshotHeaderLabel;
        private CheckBox sendBox;
        private CheckBox screenshotSendBox;
        private CheckBox receiveBox;
        private CheckBox screenshotReceiveBox;
        private Label portLabel;
        private NumericUpDown portBox;
        private GroupBox devicesGroup;
        private FlowLayoutPanel devicesPanel;
        private Label devicesHintLabel;
        private ListView devicesView;
        private ColumnHeader nameColumn;
        private ColumnHeader addressColumn;
        private ColumnHeader textRoleColumn;
        private ColumnHeader screenshotRoleColumn;
        private ColumnHeader stateColumn;
        private FlowLayoutPanel deviceButtons;
        private Button pairButton;
        private Button unpairButton;
        private Button refreshButton;
        private FlowLayoutPanel closeButtons;
        private Button closeButton;
        private ToolTip toolTip;
        private System.Windows.Forms.Timer applyTimer;
    }
}
