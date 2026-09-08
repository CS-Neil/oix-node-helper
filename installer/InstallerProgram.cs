using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace OixNodeHelper.Setup
{
    internal static class InstallerProgram
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new InstallerForm());
        }
    }

    internal sealed class InstallerForm : Form
    {
        private readonly TextBox _destination = new TextBox();
        private readonly CheckBox _desktopShortcut = new CheckBox();
        private readonly CheckBox _launch = new CheckBox();
        private readonly Button _install = new Button();
        private readonly ProgressBar _progress = new ProgressBar();

        public InstallerForm()
        {
            Text = "安装 OixNodeHelper";
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(590, 330);
            Icon = SystemIcons.Application;
            BuildUi();
        }

        private void BuildUi()
        {
            Label title = new Label
            {
                Text = "OixNodeHelper for Windows",
                Font = new Font(Font.FontFamily, 17, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(24, 22)
            };
            Label description = new Label
            {
                Text = "本机节点助手将节点更新与 FlClash 策略组、规则分离。\r\n安装包已包含官方 Windows AMD64 compatible 版 mihomo-oix 核心。",
                AutoSize = true,
                Location = new Point(27, 65)
            };
            Label destinationLabel = new Label { Text = "安装位置", AutoSize = true, Location = new Point(27, 118) };
            _destination.Text = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Programs", "OixNodeHelper");
            _destination.Location = new Point(29, 141);
            _destination.Size = new Size(442, 23);
            Button browse = new Button { Text = "浏览…", Location = new Point(480, 139), Size = new Size(82, 27) };
            browse.Click += BrowseClicked;

            _desktopShortcut.Text = "创建桌面快捷方式";
            _desktopShortcut.Checked = true;
            _desktopShortcut.AutoSize = true;
            _desktopShortcut.Location = new Point(29, 185);
            _launch.Text = "安装后启动 OixNodeHelper";
            _launch.Checked = true;
            _launch.AutoSize = true;
            _launch.Location = new Point(29, 214);

            _progress.Location = new Point(29, 251);
            _progress.Size = new Size(442, 19);
            _progress.Style = ProgressBarStyle.Marquee;
            _progress.Visible = false;

            Button cancel = new Button { Text = "取消", Location = new Point(388, 286), Size = new Size(82, 30) };
            cancel.Click += delegate { Close(); };
            _install.Text = "安装";
            _install.Location = new Point(480, 286);
            _install.Size = new Size(82, 30);
            _install.Click += InstallClicked;
            AcceptButton = _install;
            CancelButton = cancel;

            Controls.Add(title);
            Controls.Add(description);
            Controls.Add(destinationLabel);
            Controls.Add(_destination);
            Controls.Add(browse);
            Controls.Add(_desktopShortcut);
            Controls.Add(_launch);
            Controls.Add(_progress);
            Controls.Add(cancel);
            Controls.Add(_install);
        }

        private void BrowseClicked(object sender, EventArgs args)
        {
            using (FolderBrowserDialog dialog = new FolderBrowserDialog())
            {
                dialog.Description = "选择 OixNodeHelper 安装目录";
                dialog.SelectedPath = _destination.Text;
                if (dialog.ShowDialog(this) == DialogResult.OK) _destination.Text = dialog.SelectedPath;
            }
        }

        private void InstallClicked(object sender, EventArgs args)
        {
            try
            {
                string destination = Path.GetFullPath(_destination.Text.Trim());
                if (String.IsNullOrWhiteSpace(destination)) throw new InvalidOperationException("请选择安装位置。");
                if (Process.GetProcessesByName("OixNodeHelper").Length > 0)
                    throw new InvalidOperationException("OixNodeHelper 正在运行。请先从系统托盘退出后再安装或升级。");

                SetBusy(true);
                Directory.CreateDirectory(destination);
                Directory.CreateDirectory(Path.Combine(destination, "core"));
                Directory.CreateDirectory(Path.Combine(destination, "config"));

                ExtractResource("OixNodeHelper.exe", Path.Combine(destination, "OixNodeHelper.exe"));
                ExtractResource("mihomo-oix.exe", Path.Combine(destination, "core", "mihomo-oix.exe"));
                ExtractResource("Uninstall.exe", Path.Combine(destination, "Uninstall.exe"));
                ExtractResource("README.md", Path.Combine(destination, "README.md"));
                ExtractResource("flclash-provider.yaml.example", Path.Combine(destination, "config", "flclash-provider.yaml.example"));

                string executable = Path.Combine(destination, "OixNodeHelper.exe");
                string warnings = "";
                try
                {
                    string startMenuDirectory = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.Programs), "OixNodeHelper");
                    Directory.CreateDirectory(startMenuDirectory);
                    CreateShortcut(Path.Combine(startMenuDirectory, "OixNodeHelper.lnk"), executable, destination);
                    CreateShortcut(Path.Combine(startMenuDirectory, "Uninstall OixNodeHelper.lnk"), Path.Combine(destination, "Uninstall.exe"), destination);
                    if (_desktopShortcut.Checked)
                        CreateShortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "OixNodeHelper.lnk"), executable, destination);
                }
                catch (Exception shortcutError)
                {
                    warnings += "\r\n快捷方式创建失败：" + GetInnermostMessage(shortcutError);
                }
                try { RegisterUninstaller(destination); }
                catch (Exception registryError)
                {
                    warnings += "\r\nWindows 卸载项注册失败：" + GetInnermostMessage(registryError);
                }
                SetBusy(false);
                MessageBox.Show(this, "OixNodeHelper 安装完成。" + warnings, "安装完成", MessageBoxButtons.OK,
                    String.IsNullOrEmpty(warnings) ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
                if (_launch.Checked) Process.Start(executable);
                Close();
            }
            catch (Exception ex)
            {
                SetBusy(false);
                MessageBox.Show(this, GetDetailedMessage(ex), "安装失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static string GetDetailedMessage(Exception error)
        {
            Exception current = error;
            while (current.InnerException != null) current = current.InnerException;
            return current.Message + "\r\n\r\n错误代码：0x" + current.HResult.ToString("X8");
        }

        private static string GetInnermostMessage(Exception error)
        {
            Exception current = error;
            while (current.InnerException != null) current = current.InnerException;
            return current.Message;
        }

        private void SetBusy(bool busy)
        {
            _install.Enabled = !busy;
            _destination.Enabled = !busy;
            _progress.Visible = busy;
            Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
            Application.DoEvents();
        }

        private static void ExtractResource(string resourceName, string destination)
        {
            Assembly assembly = Assembly.GetExecutingAssembly();
            using (Stream input = assembly.GetManifestResourceStream(resourceName))
            {
                if (input == null) throw new InvalidDataException("安装资源缺失：" + resourceName);
                string temp = destination + ".installing";
                using (FileStream output = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
                    input.CopyTo(output);
                File.Copy(temp, destination, true);
                File.Delete(temp);
            }
        }

        private static void CreateShortcut(string shortcutPath, string targetPath, string workingDirectory)
        {
            Type shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null) throw new InvalidOperationException("Windows Shortcut 服务不可用。");
            object shell = Activator.CreateInstance(shellType);
            object shortcut = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { shortcutPath });
            Type shortcutType = shortcut.GetType();
            shortcutType.InvokeMember("TargetPath", BindingFlags.SetProperty, null, shortcut, new object[] { targetPath });
            shortcutType.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, shortcut, new object[] { workingDirectory });
            shortcutType.InvokeMember("IconLocation", BindingFlags.SetProperty, null, shortcut, new object[] { targetPath + ",0" });
            shortcutType.InvokeMember("Description", BindingFlags.SetProperty, null, shortcut, new object[] { "OixNodeHelper for Windows" });
            shortcutType.InvokeMember("Save", BindingFlags.InvokeMethod, null, shortcut, null);
        }

        private static void RegisterUninstaller(string destination)
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\OixNodeHelper"))
            {
                key.SetValue("DisplayName", "OixNodeHelper");
                key.SetValue("DisplayVersion", "0.2.3");
                key.SetValue("Publisher", "OixNodeHelper");
                key.SetValue("InstallLocation", destination);
                key.SetValue("DisplayIcon", Path.Combine(destination, "OixNodeHelper.exe"));
                key.SetValue("UninstallString", "\"" + Path.Combine(destination, "Uninstall.exe") + "\"");
                key.SetValue("NoModify", 1, RegistryValueKind.DWord);
                key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            }
        }
    }
}
