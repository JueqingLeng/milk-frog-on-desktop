using System;
using System.Collections.Generic;
using System.Drawing;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace MilkyFrog
{
    internal static class Tests
    {
        private static void Check(bool value, string detail) { if (!value) throw new Exception(detail); }
        [STAThread]
        public static int Main(string[] args)
        {
            try
            {
                int[] minutes = {0,389,390,479,480,699,700,779,780,989,990,1079,1080,1259,1260,1379,1380};
                string[] states = {"sleep","sleep","meal","meal","study","study","meal","meal","study","study","meal","meal","computer","computer","game","game","sleep"};
                for (int index = 0; index < minutes.Length; index++) Check(Schedule.At(minutes[index]).Sprite == states[index], "作息边界 " + minutes[index]);
                ActivityGate gate = new ActivityGate(3,3);
                Check(!gate.Observe(6) && !gate.Observe(6) && !gate.Observe(null) && !gate.Observe(6) && !gate.Observe(6) && gate.Observe(6), "连续活动判定与失效重置");
                List<GpuAdapter> adapters = new List<GpuAdapter> {
                    new GpuAdapter {Luid="luid_0x00000000_0x00001234", Discrete=true},
                    new GpuAdapter {Luid="luid_0x00000000_0x00005678", Discrete=false}
                };
                Check(GpuMonitor.MatchesDiscrete("pid_21_luid_0x00000000_0x00001234_phys_0_eng_0_engtype_3D",10,adapters), "独显活动映射");
                Check(!GpuMonitor.MatchesDiscrete("pid_10_luid_0x00000000_0x00001234_phys_0_eng_0_engtype_3D",10,adapters), "忽略桌宠自身");
                Check(!GpuMonitor.MatchesDiscrete("pid_21_luid_0x00000000_0x00005678_phys_0_eng_0_engtype_3D",10,adapters), "集显不触发退出");
                WindowsRecycle.RecycleSink sink = new WindowsRecycle.RecycleSink();
                Check(sink.PreDeleteItem(0,IntPtr.Zero)<0 && sink.PreDeleteItem(0x80,IntPtr.Zero)==0, "回收标志缺失时拒绝删除");
                string directory = AppDomain.CurrentDomain.BaseDirectory;
                bool refused = false;
                try { WindowsRecycle.Validate(Path.Combine(directory,"assets","laugh.wav"),directory); } catch(IOException) { refused=true; }
                Check(refused,"拒绝吞掉桌宠自身文件");
                foreach (string protectedPath in new[] { directory, Directory.GetParent(directory.TrimEnd(Path.DirectorySeparatorChar)).FullName, Path.GetPathRoot(directory), Environment.GetFolderPath(Environment.SpecialFolder.Windows) })
                {
                    refused = false;
                    try { WindowsRecycle.Validate(protectedPath,directory); } catch(IOException) { refused=true; }
                    Check(refused,"保护目录本身及其祖先："+protectedPath);
                }
                string validationFolder = Path.Combine(Directory.GetParent(directory.TrimEnd(Path.DirectorySeparatorChar)).FullName,"pet-validation-fixtures",Guid.NewGuid().ToString("N"));
                string nestedFolder = Path.Combine(validationFolder,"子文件夹"); Directory.CreateDirectory(nestedFolder);
                string nestedFile = Path.Combine(nestedFolder,"测试文本.txt"); File.WriteAllText(nestedFile,"Keep these generated contents intact.");
                Check(WindowsRecycle.Validate(validationFolder,directory)==validationFolder,"接受普通文件夹");
                FeedResult selectionResult = new FeedResult();
                List<string> selection = WindowsRecycle.PrepareInputs(new[]{nestedFile,validationFolder,validationFolder+Path.DirectorySeparatorChar,nestedFolder,nestedFile},directory,selectionResult);
                Check(selectionResult.Errors.Count==0 && selection.Count==1 && selection[0]==validationFolder,"文件夹与子项混合投喂时只回收父文件夹一次");
                Check(File.ReadAllText(nestedFile)=="Keep these generated contents intact.","准备投喂不改变文件夹内容");
                Console.WriteLine("PASS: 文件夹选择、父子路径去重、磁盘/系统/桌宠目录保护。");
                Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
                string settingsPath = Path.Combine(directory,"settings.json");
                byte[] originalSettings = File.Exists(settingsPath) ? File.ReadAllBytes(settingsPath) : null;
                using (PetForm pet = new PetForm(directory,new Settings()))
                {
                    foreach (string state in new[]{"sleep","sleep-back","sleep-turn-back","sleep-turn-front","game","laugh","feed","wave"})
                    using (Bitmap image = pet.Render(state,0,state))
                    {
                        Check(image.GetPixel(0,0).A==0,"透明窗口背景");
                        image.Save(Path.Combine(directory,state+".preview.png"));
                    }
                }
                // Use the real transparent window and message loop; don't create artificial GPU load.
                for (int exitMode = 0; exitMode < 3; exitMode++)
                using (PetForm pet = new PetForm(directory,new Settings { AutoCloseOnDiscreteGpu=false, Sound=false }))
                using (System.Windows.Forms.Timer trigger = new System.Windows.Forms.Timer { Interval=100 })
                {
                    Stopwatch elapsed = new Stopwatch(); bool stillOpen = false, repeated = false;
                    trigger.Tick += delegate {
                        if (!elapsed.IsRunning) {
                            elapsed.Start();
                            if (exitMode == 0) pet.RequestClose("自动招手测试");
                            else if (exitMode == 1) ((ToolStripMenuItem)pet.ContextMenuStrip.Items[pet.ContextMenuStrip.Items.Count - 1]).PerformClick();
                            else pet.Close();
                        }
                        else if (elapsed.ElapsedMilliseconds >= 900 && elapsed.ElapsedMilliseconds < 1600) {
                            stillOpen = !pet.IsDisposed;
                            if (!repeated) { repeated = true; pet.Close(); }
                        }
                        else if (elapsed.ElapsedMilliseconds > 5000) { pet.Dispose(); Application.ExitThread(); }
                    };
                    pet.Shown += delegate { trigger.Start(); };
                    Application.Run(pet);
                    Check(stillOpen && elapsed.ElapsedMilliseconds >= 2000 && elapsed.ElapsedMilliseconds < 3000,"退出方式 "+exitMode+" 需要两秒招手且重复关闭不重置："+elapsed.ElapsedMilliseconds+"ms");
                    Console.WriteLine("PASS: 退出方式 "+exitMode+"，真实透明窗口招手 "+elapsed.ElapsedMilliseconds+"ms 后退出；重复关闭不跳过或延长。");
                }
                if (originalSettings != null) File.WriteAllBytes(settingsPath,originalSettings);
                using(GpuMonitor monitor = new GpuMonitor())
                {
                    foreach(GpuAdapter adapter in monitor.Adapters) Console.WriteLine("GPU: "+adapter.Name+" | "+adapter.Classification+" | "+adapter.Luid);
                    Thread.Sleep(1100); Console.WriteLine("GPU sample: "+monitor.Sample()+" | "+monitor.Status);
                }
                if(args.Length==2 && args[0]=="--recycle-test")
                {
                    string fixtureDirectory = Path.GetFullPath(args[1]);Directory.CreateDirectory(fixtureDirectory);
                    string file = Path.Combine(fixtureDirectory,"奶蛙 回收测试 "+Guid.NewGuid().ToString("N")+".txt");
                    File.WriteAllText(file,"Only this generated test file may be recycled.");
                    FeedResult result = WindowsRecycle.Feed(new[]{file,file},directory,IntPtr.Zero);
                    Check(result.Recycled==1 && result.Errors.Count==0 && !File.Exists(file),"真实回收站测试："+String.Join(";",result.Errors));
                    Console.WriteLine("PASS: Windows 回收站已接收生成的中文文件；重复路径仅处理一次。");
                    string folder = Path.Combine(fixtureDirectory,"奶蛙 文件夹回收测试 "+Guid.NewGuid().ToString("N"));
                    string child = Path.Combine(folder,"嵌套目录","文本.txt"); Directory.CreateDirectory(Path.GetDirectoryName(child)); File.WriteAllText(child,"Generated folder recycle fixture.");
                    FeedResult folderResult = WindowsRecycle.Feed(new[]{child,folder,folder},directory,IntPtr.Zero);
                    Check(folderResult.Recycled==1 && folderResult.Errors.Count==0 && !Directory.Exists(folder),"真实文件夹回收测试："+String.Join(";",folderResult.Errors));
                    Console.WriteLine("PASS: 整份文件夹作为一个项目放入 Windows 回收站。");
                }
                Console.WriteLine("PASS: 北京时间作息、独显映射/持续活动、回收保护、透明渲染、正背面翻身素材、手柄素材。");return 0;
            }
            catch(Exception error){Console.Error.WriteLine(error);return 1;}
        }
    }
}
