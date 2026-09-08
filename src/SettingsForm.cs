using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace OixNodeHelper
{
    public sealed class SettingsForm : Form
    {
        private readonly AppController _controller;
        private readonly TextBox _corePath = new TextBox();
        private readonly TextBox _token = new TextBox();
        private readonly TextBox _controllerUrl = new TextBox();
        private readonly TextBox _controllerSecret = new TextBox();
        private readonly NumericUpDown _providerPort = new NumericUpDown();
        private readonly NumericUpDown _baseNodePort = new NumericUpDown();
        private readonly NumericUpDown _maxNodes = new NumericUpDown();
        private readonly NumericUpDown _pollSeconds = new NumericUpDown();
        private readonly NumericUpDown _portRetentionDays = new NumericUpDown();
        private readonly NumericUpDown _emptyRefreshThreshold = new NumericUpDown();
        private readonly TextBox _includeRegex = new TextBox();
        private readonly TextBox _excludeRegex = new TextBox();
        private readonly TextBox _oixParams = new TextBox();
        private readonly CheckBox _startWithWindows = new CheckBox();
        private readonly ListBox _nodes = new ListBox();
        private readonly Label _status = new Label();

        public SettingsForm(AppController controller)
        {
            _controller = controller;
            Text = "Oix Node Helper 设置";
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(760, 650);
            Size = new Size(840, 720);
            Icon = SystemIcons.Application;
            BuildUi();
            LoadValues();
            _controller.StatusChanged += ControllerStatusChanged;
        }

        private void BuildUi()
        {
            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.Padding = new Padding(12);
            root.ColumnCount = 1;
            root.RowCount = 4;
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 130));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            Label intro = new Label();
            intro.AutoSize = true;
            intro.MaximumSize = new Size(790, 0);
            intro.Text = "节点由 oixcloud.com/user/token 获取的 Access Token 交给官方 mihomo-oix 托管更新，不读取 FlClash 配置。Token 和 Controller Secret 使用 Windows DPAPI 加密。";
            intro.Margin = new Padding(3, 3, 3, 12);
            root.Controls.Add(intro, 0, 0);

            TableLayoutPanel fields = new TableLayoutPanel();
            fields.Dock = DockStyle.Fill;
            fields.AutoScroll = true;
            fields.ColumnCount = 3;
            fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
            fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 85));

            int row = 0;
            AddPathRow(fields, ref row, "mihomo-oix.exe", _corePath, "选择…", BrowseCore);
            _token.UseSystemPasswordChar = true;
            LinkLabel tokenLink = new LinkLabel { Text = "获取 Token", AutoSize = true, Margin = new Padding(3, 8, 3, 3) };
            tokenLink.LinkClicked += delegate { Process.Start("https://oixcloud.com/user/token"); };
            AddRow(fields, ref row, "Access Token", _token, tokenLink);
            AddRow(fields, ref row, "Controller URL", _controllerUrl, null);
            _controllerSecret.UseSystemPasswordChar = true;
            AddRow(fields, ref row, "Controller Secret", _controllerSecret, null);

            ConfigureNumber(_providerPort, 1024, 65535);
            ConfigureNumber(_baseNodePort, 1024, 65000);
            ConfigureNumber(_maxNodes, 1, 500);
            ConfigureNumber(_pollSeconds, 15, 86400);
            ConfigureNumber(_portRetentionDays, 1, 365);
            ConfigureNumber(_emptyRefreshThreshold, 1, 10);
            AddRow(fields, ref row, "Provider 端口", _providerPort, null);
            AddRow(fields, ref row, "节点起始端口", _baseNodePort, null);
            AddRow(fields, ref row, "最大节点数", _maxNodes, null);
            AddRow(fields, ref row, "轮询秒数", _pollSeconds, null);
            AddRow(fields, ref row, "端口保留天数", _portRetentionDays, null);
            AddRow(fields, ref row, "空节点确认次数", _emptyRefreshThreshold, null);
            AddRow(fields, ref row, "包含正则（可空）", _includeRegex, null);
            AddRow(fields, ref row, "排除正则（可空）", _excludeRegex, null);
            AddRow(fields, ref row, "OIX_PARAMS（可空）", _oixParams, null);

            FlowLayoutPanel checks = new FlowLayoutPanel();
            checks.AutoSize = true;
            _startWithWindows.Text = "登录 Windows 后自动启动";
            _startWithWindows.AutoSize = true;
            checks.Controls.Add(_startWithWindows);
            AddRow(fields, ref row, "运行选项", checks, null);
            root.Controls.Add(fields, 0, 1);

            GroupBox nodeGroup = new GroupBox();
            nodeGroup.Text = "当前节点与本地端口";
            nodeGroup.Dock = DockStyle.Fill;
            _status.Dock = DockStyle.Top;
            _status.AutoSize = true;
            _status.Padding = new Padding(4);
            _nodes.Dock = DockStyle.Fill;
            nodeGroup.Controls.Add(_nodes);
            nodeGroup.Controls.Add(_status);
            root.Controls.Add(nodeGroup, 0, 2);

            FlowLayoutPanel buttons = new FlowLayoutPanel();
            buttons.FlowDirection = FlowDirection.RightToLeft;
            buttons.Dock = DockStyle.Fill;
            buttons.AutoSize = true;
            Button save = new Button { Text = "保存并重启", AutoSize = true };
            Button cancel = new Button { Text = "取消", AutoSize = true };
            Button copyProvider = new Button { Text = "复制 Provider 地址", AutoSize = true };
            Button refresh = new Button { Text = "立即刷新", AutoSize = true };
            save.Click += SaveClicked;
            cancel.Click += delegate { Close(); };
            copyProvider.Click += delegate
            {
                Clipboard.SetText("http://127.0.0.1:" + Decimal.ToInt32(_providerPort.Value) + "/clash");
                MessageBox.Show(this, "Provider 地址已复制。", "OixNodeHelper", MessageBoxButtons.OK, MessageBoxIcon.Information);
            };
            refresh.Click += delegate { _controller.ForceRefreshAsync(); };
            buttons.Controls.Add(save);
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(copyProvider);
            buttons.Controls.Add(refresh);
            root.Controls.Add(buttons, 0, 3);
            Controls.Add(root);
        }

        private static void AddPathRow(TableLayoutPanel panel, ref int row, string label, Control control, string buttonText, EventHandler click)
        {
            Button button = new Button { Text = buttonText, AutoSize = true };
            button.Click += click;
            AddRow(panel, ref row, label, control, button);
        }

        private static void AddRow(TableLayoutPanel panel, ref int row, string label, Control control, Control trailing)
        {
            panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Label caption = new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 8, 3, 3) };
            control.Dock = DockStyle.Top;
            control.Margin = new Padding(3, 4, 3, 4);
            panel.Controls.Add(caption, 0, row);
            panel.Controls.Add(control, 1, row);
            if (trailing != null) panel.Controls.Add(trailing, 2, row);
            row++;
        }

        private static void ConfigureNumber(NumericUpDown control, decimal min, decimal max)
        {
            control.Minimum = min;
            control.Maximum = max;
            control.ThousandsSeparator = false;
        }

        private void LoadValues()
        {
            AppSettings s = _controller.Settings;
            CredentialBundle c = _controller.Credentials;
            _corePath.Text = s.CorePath;
            _token.Text = c.AccessToken;
            _controllerUrl.Text = s.ControllerUrl;
            _controllerSecret.Text = c.ControllerSecret;
            _providerPort.Value = Clamp(s.ProviderPort, _providerPort.Minimum, _providerPort.Maximum);
            _baseNodePort.Value = Clamp(s.BaseNodePort, _baseNodePort.Minimum, _baseNodePort.Maximum);
            _maxNodes.Value = Clamp(s.MaxNodes, _maxNodes.Minimum, _maxNodes.Maximum);
            _pollSeconds.Value = Clamp(s.PollSeconds, _pollSeconds.Minimum, _pollSeconds.Maximum);
            _portRetentionDays.Value = Clamp(s.PortRetentionDays, _portRetentionDays.Minimum, _portRetentionDays.Maximum);
            _emptyRefreshThreshold.Value = Clamp(s.EmptyRefreshThreshold, _emptyRefreshThreshold.Minimum, _emptyRefreshThreshold.Maximum);
            _includeRegex.Text = s.IncludeRegex;
            _excludeRegex.Text = s.ExcludeRegex;
            _oixParams.Text = s.OixParams;
            _startWithWindows.Checked = s.StartWithWindows;
            UpdateStatusAndNodes();
        }

        private void UpdateStatusAndNodes()
        {
            _nodes.Items.Clear();
            foreach (NodeInfo node in _controller.GetNodesSnapshot())
                _nodes.Items.Add(node.Port + "  " + node.Name + "  [" + node.Type + "]");
            HealthDocument health = _controller.GetHealth();
            _status.Text = health.Stage + "  ·  节点 " + health.NodeCount + "  ·  上次更新 " +
                (String.IsNullOrEmpty(health.LastRefreshUtc) ? "尚未完成" : health.LastRefreshUtc);
        }

        private void ControllerStatusChanged(object sender, EventArgs args)
        {
            if (IsDisposed) return;
            if (InvokeRequired)
            {
                try { BeginInvoke(new EventHandler(ControllerStatusChanged), sender, args); } catch { }
                return;
            }
            UpdateStatusAndNodes();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _controller.StatusChanged -= ControllerStatusChanged;
            base.OnFormClosed(e);
        }

        private static decimal Clamp(decimal value, decimal minimum, decimal maximum)
        {
            return Math.Max(minimum, Math.Min(maximum, value));
        }

        private void BrowseCore(object sender, EventArgs args)
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Filter = "Executable (*.exe)|*.exe|All files (*.*)|*.*";
                if (dialog.ShowDialog(this) == DialogResult.OK) _corePath.Text = dialog.FileName;
            }
        }

        private void SaveClicked(object sender, EventArgs args)
        {
            try
            {
                if (!String.IsNullOrWhiteSpace(_includeRegex.Text)) new Regex(_includeRegex.Text);
                if (!String.IsNullOrWhiteSpace(_excludeRegex.Text)) new Regex(_excludeRegex.Text);
                AppSettings settings = new AppSettings
                {
                    CorePath = _corePath.Text.Trim(),
                    ControllerUrl = _controllerUrl.Text.Trim(),
                    ProviderPort = Decimal.ToInt32(_providerPort.Value),
                    BaseNodePort = Decimal.ToInt32(_baseNodePort.Value),
                    MaxNodes = Decimal.ToInt32(_maxNodes.Value),
                    PollSeconds = Decimal.ToInt32(_pollSeconds.Value),
                    PortRetentionDays = Decimal.ToInt32(_portRetentionDays.Value),
                    EmptyRefreshThreshold = Decimal.ToInt32(_emptyRefreshThreshold.Value),
                    IncludeRegex = _includeRegex.Text.Trim(),
                    ExcludeRegex = _excludeRegex.Text.Trim(),
                    OixParams = _oixParams.Text.Trim(),
                    StartWithWindows = _startWithWindows.Checked,
                    FrontendPath = _controller.Settings.FrontendPath
                };
                CredentialBundle credentials = new CredentialBundle
                {
                    AccessToken = _token.Text.Trim(),
                    ControllerSecret = _controllerSecret.Text
                };
                _controller.SaveConfiguration(settings, credentials);
                DialogResult = DialogResult.OK;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "配置无效", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
    }
}
