using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace OixNodeHelper.Setup
{
    internal static class UninstallerProgram
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new UninstallerForm());
        }
    }

    internal sealed class UninstallerForm : Form
    {
        private readonly CheckBox _removeData = new CheckBox();

        public UninstallerForm()
        {
            Text = "卸载 OixNodeHelper";
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(510, 230);
            Icon = SystemIcons.Application;

            Label title = new Label
            {
                Text = "卸载 OixNodeHelper",
                Font = new Font(Font.FontFamily, 16, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(24, 22)
            };
            Label text = new Label
            {
                Text = "将删除程序、内置核心、开始菜单和桌面快捷方式。\r\n默认保留设置、端口映射和加密凭据，方便以后重新安装。",
                AutoSize = true,
                Location = new Point(27, 67)
            };
            _removeData.Text = "同时删除 %LOCALAPPDATA%\\OixNodeHelper 中的用户数据";
            _removeData.AutoSize = true;
            _removeData.Location = new Point(29, 119);

            Button cancel = new Button { Text = "取消", Location = new Point(318, 178), Size = new Size(78, 30) };
            cancel.Click += delegate { Close(); };
            Button uninstall = new Button { Text = "卸载", Location = new Point(406, 178), Size = new Size(78, 30) };
            uninstall.Click += UninstallClicked;
            AcceptButton = uninstall;
            CancelButton = cancel;

            Controls.Add(title);
            Controls.Add(text);
            Controls.Add(_removeData);
            Controls.Add(cancel);
            Controls.Add(uninstall);
        }

        private void UninstallClicked(object sender, EventArgs args)
        {
            if (MessageBox.Show(this, "确定卸载 OixNodeHelper？", "确认卸载", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;
            try
            {
                foreach (Process process in Process.GetProcessesByName("OixNodeHelper"))
                {
                    try { process.Kill(); process.WaitForExit(3000); } catch { }
                }

                string installDirectory = AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
                DeleteFile(Path.Combine(installDirectory, "OixNodeHelper.exe"));
                DeleteFile(Path.Combine(installDirectory, "README.md"));
                DeleteFile(Path.Combine(installDirectory, "core", "mihomo-oix.exe"));
                DeleteFile(Path.Combine(installDirectory, "config", "flclash-provider.yaml.example"));
                TryDeleteEmptyDirectory(Path.Combine(installDirectory, "core"));
                TryDeleteEmptyDirectory(Path.Combine(installDirectory, "config"));

                string startMenuDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "OixNodeHelper");
                DeleteFile(Path.Combine(startMenuDirectory, "OixNodeHelper.lnk"));
                DeleteFile(Path.Combine(startMenuDirectory, "Uninstall OixNodeHelper.lnk"));
                DeleteFile(Path.Combine(startMenuDirectory, "卸载 OixNodeHelper.lnk"));
                TryDeleteEmptyDirectory(startMenuDirectory);
                DeleteFile(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "OixNodeHelper.lnk"));
                Registry.CurrentUser.DeleteSubKeyTree("Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\OixNodeHelper", false);

                if (_removeData.Checked)
                {
                    string data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OixNodeHelper");
                    if (Directory.Exists(data)) Directory.Delete(data, true);
                }

                ScheduleSelfCleanup(installDirectory);
                MessageBox.Show(this, "OixNodeHelper 已卸载。", "卸载完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "卸载失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static void DeleteFile(string path)
        {
            if (File.Exists(path)) File.Delete(path);
        }

        private static void TryDeleteEmptyDirectory(string path)
        {
            try { if (Directory.Exists(path)) Directory.Delete(path, false); } catch { }
        }

        private static void ScheduleSelfCleanup(string installDirectory)
        {
            string script = Path.Combine(Path.GetTempPath(), "OixNodeHelper-uninstall-" + Guid.NewGuid().ToString("N") + ".cmd");
            string self = Application.ExecutablePath;
            File.WriteAllText(script,
                "@echo off\r\n" +
                "ping 127.0.0.1 -n 3 > nul\r\n" +
                "del /f /q \"" + self.Replace("\"", "\"\"") + "\"\r\n" +
                "rmdir \"" + installDirectory.Replace("\"", "\"\"") + "\" 2>nul\r\n" +
                "del /f /q \"%~f0\"\r\n");
            ProcessStartInfo start = new ProcessStartInfo("cmd.exe", "/c \"\"" + script + "\"\"");
            start.UseShellExecute = false;
            start.CreateNoWindow = true;
            start.WindowStyle = ProcessWindowStyle.Hidden;
            Process.Start(start);
        }
    }
}
