using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace MilkyFrog
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            string directory = AppDomain.CurrentDomain.BaseDirectory;
            if (args.Length == 2 && args[0] == "--diagnose")
            {
                using (GpuMonitor monitor = new GpuMonitor())
                {
                    System.Text.StringBuilder output = new System.Text.StringBuilder();
                    foreach (GpuAdapter adapter in monitor.Adapters) output.AppendLine(adapter.Name + " | " + adapter.Luid + " | " + adapter.Classification + " | discrete=" + adapter.Discrete);
                    Thread.Sleep(1100); output.AppendLine("sample=" + monitor.Sample()); output.AppendLine(monitor.Status);
                    File.WriteAllText(args[1], output.ToString());
                }
                return;
            }
            bool first;
            using (Mutex mutex = new Mutex(true, @"Local\LengJueqingMilkyFrog", out first))
            {
                if (!first) return;
                try
                {
                    try { SetProcessDpiAwareness(2); } catch (EntryPointNotFoundException) { SetProcessDPIAware(); } catch (DllNotFoundException) { SetProcessDPIAware(); }
                    Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
                    using (PetForm pet = new PetForm(directory, Settings.Load(directory))) Application.Run(pet);
                }
                catch (Exception error)
                {
                    Log.Write(error.ToString()); MessageBox.Show("奶蛙暂时无法启动：\n" + error.Message, "奶蛙桌宠", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }
        [DllImport("shcore.dll")] private static extern int SetProcessDpiAwareness(int awareness);
        [DllImport("user32.dll")] private static extern bool SetProcessDPIAware();
    }
}
