using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace OixNodeHelper
{
    public sealed class TrayApplicationContext : ApplicationContext
    {
        private readonly AppController _controller;
        private readonly NotifyIcon _notifyIcon;
        private readonly ToolStripMenuItem _statusItem;
        private readonly Timer _statusTimer;
        private SettingsForm _settingsForm;
        private string _lastNotifiedError = "";

        public TrayApplicationContext()
        {
            _controller = new AppController();
            _statusItem = new ToolStripMenuItem("正在启动…") { Enabled = false };
            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Items.Add(_statusItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("设置…", null, delegate { ShowSettings(); });
            menu.Items.Add("刷新节点", null, delegate { _controller.ForceRefreshAsync(); });
            menu.Items.Add("重启 Core", null, delegate { _controller.RestartAsync(); });
            menu.Items.Add("打开 Clash Provider", null, delegate { SafeAction(_controller.OpenProvider); });
            menu.Items.Add("打开数据目录", null, delegate { SafeAction(_controller.OpenDataFolder); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("退出", null, ExitClicked);

            _notifyIcon = new NotifyIcon();
            _notifyIcon.Icon = SystemIcons.Application;
            _notifyIcon.Text = "Oix Node Helper";
            _notifyIcon.ContextMenuStrip = menu;
            _notifyIcon.Visible = true;
            _notifyIcon.DoubleClick += delegate { ShowSettings(); };

            _statusTimer = new Timer();
            _statusTimer.Interval = 2000;
            _statusTimer.Tick += delegate { UpdateStatus(); };
            _statusTimer.Start();

            try { _controller.Start(); }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Oix Node Helper", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }

            AppSettings settings = _controller.Settings;
            if (String.IsNullOrWhiteSpace(settings.CorePath) || String.IsNullOrWhiteSpace(_controller.Credentials.AccessToken))
                ShowSettings();
        }

        private void ShowSettings()
        {
            if (_settingsForm != null && !_settingsForm.IsDisposed)
            {
                _settingsForm.Activate();
                return;
            }
            _settingsForm = new SettingsForm(_controller);
            _settingsForm.Show();
            _settingsForm.Activate();
        }

        private void UpdateStatus()
        {
            string status = _controller.GetStatusText();
            _statusItem.Text = status;
            string tooltip = "Oix Node Helper · " + status;
            _notifyIcon.Text = tooltip.Length > 63 ? tooltip.Substring(0, 63) : tooltip;
            HealthDocument health = _controller.GetHealth();
            if (!String.IsNullOrEmpty(health.LastError) && !String.Equals(health.LastError, _lastNotifiedError, StringComparison.Ordinal))
            {
                _lastNotifiedError = health.LastError;
                _notifyIcon.BalloonTipTitle = "OixNodeHelper 更新失败";
                _notifyIcon.BalloonTipText = health.LastError.Length > 240 ? health.LastError.Substring(0, 240) : health.LastError;
                _notifyIcon.BalloonTipIcon = ToolTipIcon.Warning;
                _notifyIcon.ShowBalloonTip(5000);
            }
            else if (String.IsNullOrEmpty(health.LastError))
            {
                _lastNotifiedError = "";
            }
        }

        private static void SafeAction(Action action)
        {
            try { action(); }
            catch (Exception ex) { MessageBox.Show(ex.Message, "Oix Node Helper", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }

        private void ExitClicked(object sender, EventArgs args)
        {
            _statusTimer.Stop();
            if (_settingsForm != null && !_settingsForm.IsDisposed) _settingsForm.Close();
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _controller.Dispose();
            ExitThread();
        }
    }
}
