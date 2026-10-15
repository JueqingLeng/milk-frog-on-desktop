using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace MilkyFrog
{
    public sealed class GpuAdapter
    {
        public string Name;
        public string Luid;
        public bool Discrete;
        public string Classification;
    }

    public sealed class ActivityGate
    {
        private readonly double threshold;
        private readonly int samples;
        private int consecutive;
        public ActivityGate(double threshold, int samples) { this.threshold = threshold; this.samples = samples; }
        public bool Observe(double? utilization)
        {
            consecutive = utilization.HasValue && utilization.Value >= threshold ? consecutive + 1 : 0;
            return consecutive >= samples;
        }
    }

    public sealed class GpuMonitor : IDisposable
    {
        public readonly List<GpuAdapter> Adapters = new List<GpuAdapter>();
        public string Status { get; private set; }
        private IntPtr query;
        private IntPtr counter;
        private readonly int ownPid = Process.GetCurrentProcess().Id;

        public GpuMonitor()
        {
            try
            {
                Adapters.AddRange(EnumerateAdapters());
                bool hasDiscrete = Adapters.Exists(a => a.Discrete);
                if (!hasDiscrete) { Status = "未检测到独立显卡，桌宠正常运行。"; return; }
                Check(PdhOpenQuery(null, IntPtr.Zero, out query));
                Check(PdhAddEnglishCounter(query, @"\GPU Engine(*)\Utilization Percentage", IntPtr.Zero, out counter));
                PdhCollectQueryData(query);
                Status = "正在监测独显活动。";
            }
            catch (Exception error) { Status = "独显监测暂不可用：" + error.Message; Dispose(); }
        }

        public double? Sample()
        {
            if (query == IntPtr.Zero) return null;
            uint collected = PdhCollectQueryData(query);
            if (collected != 0) { Status = "独显计数器暂不可用：0x" + collected.ToString("x8"); return null; }
            uint size = 0, count = 0;
            uint code = PdhGetFormattedCounterArray(counter, 0x200 | 0x8000, ref size, ref count, IntPtr.Zero);
            if (code != 0x800007D2 || size == 0) return null;
            IntPtr buffer = Marshal.AllocHGlobal((int)size);
            try
            {
                code = PdhGetFormattedCounterArray(counter, 0x200 | 0x8000, ref size, ref count, buffer);
                if (code != 0) return null;
                double peak = 0;
                int stride = Marshal.SizeOf(typeof(CounterItem));
                for (int index = 0; index < count; index++)
                {
                    CounterItem entry = (CounterItem)Marshal.PtrToStructure(IntPtr.Add(buffer, stride * index), typeof(CounterItem));
                    if (entry.Value.Status > 1) continue;
                    string instance = Marshal.PtrToStringUni(entry.Name);
                    if (!MatchesDiscrete(instance, ownPid, Adapters)) continue;
                    if (!Double.IsNaN(entry.Value.Value) && !Double.IsInfinity(entry.Value.Value)) peak = Math.Max(peak, entry.Value.Value);
                }
                Status = "独显活动 " + peak.ToString("0.0") + "%";
                return peak;
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }

        public static bool MatchesDiscrete(string instance, int excludedPid, List<GpuAdapter> adapters)
        {
            if (String.IsNullOrEmpty(instance)) return false;
            if (instance.StartsWith("pid_" + excludedPid + "_", StringComparison.OrdinalIgnoreCase)) return false;
            return adapters.Exists(a => a.Discrete && instance.IndexOf(a.Luid + "_", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        public void Dispose()
        {
            if (query != IntPtr.Zero) PdhCloseQuery(query);
            query = IntPtr.Zero; counter = IntPtr.Zero;
        }

        private static IEnumerable<GpuAdapter> EnumerateAdapters()
        {
            List<GpuAdapter> result = new List<GpuAdapter>();
            Guid iid = new Guid("770AAE78-F26F-4DBA-A829-253C83D1B387");
            IntPtr factory;
            Marshal.ThrowExceptionForHR(CreateDXGIFactory1(ref iid, out factory));
            try
            {
                EnumAdapters enumerate = Method<EnumAdapters>(factory, 12);
                for (uint index = 0; ; index++)
                {
                    IntPtr adapter;
                    int code = enumerate(factory, index, out adapter);
                    if (code == unchecked((int)0x887A0002)) break;
                    Marshal.ThrowExceptionForHR(code);
                    try
                    {
                        AdapterDescription description;
                        Marshal.ThrowExceptionForHR(Method<GetDescription>(adapter, 10)(adapter, out description));
                        if ((description.Flags & 2) != 0) continue; // Software adapters.
                        bool? uma = IsUnifiedMemory(adapter);
                        bool discrete = uma.HasValue ? !uma.Value :
                            description.Vendor == 0x10DE || description.DedicatedVideoMemory.ToUInt64() > 1073741824UL;
                        result.Add(new GpuAdapter {
                            Name = description.Description,
                            Luid = "luid_0x" + unchecked((uint)description.Luid.High).ToString("x8") + "_0x" + description.Luid.Low.ToString("x8"),
                            Discrete = discrete,
                            Classification = uma.HasValue ? (uma.Value ? "统一内存，集显" : "非统一内存，独显") : "D3D12 不可用，按厂商/专用显存判断"
                        });
                    }
                    finally { Marshal.Release(adapter); }
                }
            }
            finally { Marshal.Release(factory); }
            return result;
        }

        private static bool? IsUnifiedMemory(IntPtr adapter)
        {
            IntPtr device = IntPtr.Zero;
            try
            {
                Guid iid = new Guid("189819F1-1DB6-4B57-BE54-1821339B85F7");
                if (D3D12CreateDevice(adapter, 0xB000, ref iid, out device) < 0) return null;
                Architecture info = new Architecture();
                int code = Method<CheckFeature>(device, 13)(device, 1, ref info, (uint)Marshal.SizeOf(typeof(Architecture)));
                return code >= 0 ? (bool?)(info.UMA != 0) : null;
            }
            catch (DllNotFoundException) { return null; }
            finally { if (device != IntPtr.Zero) Marshal.Release(device); }
        }

        private static T Method<T>(IntPtr obj, int slot) where T : class
        {
            IntPtr table = Marshal.ReadIntPtr(obj);
            return Marshal.GetDelegateForFunctionPointer(Marshal.ReadIntPtr(table, slot * IntPtr.Size), typeof(T)) as T;
        }
        private static void Check(uint code) { if (code != 0) throw new InvalidOperationException("GPU 计数器错误 0x" + code.ToString("x8")); }

        [StructLayout(LayoutKind.Sequential)] private struct Luid { public uint Low; public int High; }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct AdapterDescription
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Description;
            public uint Vendor, Device, Subsystem, Revision;
            public UIntPtr DedicatedVideoMemory, DedicatedSystemMemory, SharedSystemMemory;
            public Luid Luid;
            public uint Flags;
        }
        [StructLayout(LayoutKind.Sequential)] private struct Architecture { public uint Node; public int TileBased, UMA, CacheCoherentUMA; }
        [StructLayout(LayoutKind.Sequential)] private struct CounterValue { public uint Status; public double Value; }
        [StructLayout(LayoutKind.Sequential)] private struct CounterItem { public IntPtr Name; public CounterValue Value; }
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int EnumAdapters(IntPtr self, uint index, out IntPtr adapter);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetDescription(IntPtr self, out AdapterDescription description);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int CheckFeature(IntPtr self, int feature, ref Architecture info, uint size);
        [DllImport("dxgi.dll")] private static extern int CreateDXGIFactory1(ref Guid iid, out IntPtr factory);
        [DllImport("d3d12.dll")] private static extern int D3D12CreateDevice(IntPtr adapter, int level, ref Guid iid, out IntPtr device);
        [DllImport("pdh.dll", EntryPoint = "PdhOpenQueryW", CharSet = CharSet.Unicode)] private static extern uint PdhOpenQuery(string source, IntPtr userdata, out IntPtr query);
        [DllImport("pdh.dll", EntryPoint = "PdhAddEnglishCounterW", CharSet = CharSet.Unicode)] private static extern uint PdhAddEnglishCounter(IntPtr query, string path, IntPtr userdata, out IntPtr counter);
        [DllImport("pdh.dll")] private static extern uint PdhCollectQueryData(IntPtr query);
        [DllImport("pdh.dll", EntryPoint = "PdhGetFormattedCounterArrayW", CharSet = CharSet.Unicode)] private static extern uint PdhGetFormattedCounterArray(IntPtr counter, uint format, ref uint size, ref uint count, IntPtr buffer);
        [DllImport("pdh.dll")] private static extern uint PdhCloseQuery(IntPtr query);
    }
}
