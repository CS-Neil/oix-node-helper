using System;
using System.Threading;
using System.Windows.Forms;

namespace OixNodeHelper
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            bool created;
            using (Mutex mutex = new Mutex(true, "Local\\OixNodeHelper.SingleInstance", out created))
            {
                if (!created)
                {
                    MessageBox.Show("Oix Node Helper 已经在运行。", "Oix Node Helper", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.ThreadException += delegate(object sender, ThreadExceptionEventArgs eventArgs)
                {
                    MessageBox.Show(eventArgs.Exception.Message, "未处理错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                };
                try
                {
                    Application.Run(new TrayApplicationContext());
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message, "Oix Node Helper 启动失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                GC.KeepAlive(mutex);
            }
        }
    }
}
