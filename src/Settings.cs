using System;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace MilkyFrog
{
    public sealed class Settings
    {
        public int PetSize = 220;
        public bool Sound = true;
        public bool AutoCloseOnDiscreteGpu = true;
        public double GpuThresholdPercent = 3;
        public int GpuConsecutiveSamples = 3;
        public int GpuPollMilliseconds = 1000;
        public int X = int.MinValue;
        public int Y = int.MinValue;

        public static Settings Load(string directory)
        {
            try
            {
                string file = Path.Combine(directory, "settings.json");
                Settings result = new JavaScriptSerializer().Deserialize<Settings>(File.ReadAllText(file, Encoding.UTF8));
                result.PetSize = Math.Max(150, Math.Min(360, result.PetSize));
                result.GpuThresholdPercent = Math.Max(.1, Math.Min(100, result.GpuThresholdPercent));
                result.GpuConsecutiveSamples = Math.Max(1, Math.Min(30, result.GpuConsecutiveSamples));
                result.GpuPollMilliseconds = Math.Max(500, Math.Min(10000, result.GpuPollMilliseconds));
                return result;
            }
            catch { return new Settings(); }
        }

        public void Save(string directory)
        {
            try { File.WriteAllText(Path.Combine(directory, "settings.json"), new JavaScriptSerializer().Serialize(this), Encoding.UTF8); }
            catch (Exception error) { Log.Write("无法保存位置设置：" + error.Message); }
        }
    }

    public static class Log
    {
        private static readonly object Gate = new object();
        public static void Write(string text)
        {
            try { lock (Gate) File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "pet.log"),
                DateTime.Now.ToString("s") + " " + text + Environment.NewLine, Encoding.UTF8); }
            catch { }
        }
    }
}
