using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace MilkyFrog
{
    public sealed class FeedResult
    {
        public int Recycled;
        public readonly List<string> Errors = new List<string>();
    }

    public static class WindowsRecycle
    {
        public static string Validate(string candidate, string applicationDirectory)
        {
            if (String.IsNullOrWhiteSpace(candidate)) throw new IOException("文件路径为空。");
            string full = Path.GetFullPath(candidate);
            if (full.StartsWith(@"\\", StringComparison.Ordinal)) throw new IOException("网络位置没有本机回收站，无法投喂。");
            bool folder = Directory.Exists(full);
            if (!File.Exists(full) && !folder) throw new IOException("文件或文件夹不存在。");
            if (full.Length > Path.GetPathRoot(full).Length) full = full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (String.Equals(full, Path.GetPathRoot(full), StringComparison.OrdinalIgnoreCase)) throw new IOException("磁盘根目录不能投喂。");
            if ((File.GetAttributes(full) & FileAttributes.ReparsePoint) != 0) throw new IOException("快捷映射或符号链接暂不接收。");
            DriveInfo drive = new DriveInfo(Path.GetPathRoot(full));
            if (drive.DriveType != DriveType.Fixed) throw new IOException("请使用本机固定磁盘上的文件。");
            string[] protectedDirectories = {
                Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                applicationDirectory,
                Path.Combine(Path.GetPathRoot(full), "$Recycle.Bin"),
                Path.Combine(Path.GetPathRoot(full), "System Volume Information")
            };
            foreach (string directory in protectedDirectories)
                if (!String.IsNullOrWhiteSpace(directory) && (Within(full, directory) || (folder && Within(directory, full))))
                    throw new IOException("这个项目属于系统或桌宠本身，或包含受保护目录，不能投喂。");
            return full;
        }

        private static bool Within(string file, string directory)
        {
            string root = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string full = Path.GetFullPath(file).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return full.Equals(root, StringComparison.OrdinalIgnoreCase) || full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }

        internal static List<string> PrepareInputs(IEnumerable<string> files, string applicationDirectory, FeedResult result)
        {
            List<string> candidates = new List<string>();
            HashSet<string> unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string input in files)
            {
                try
                {
                    string full = Validate(input, applicationDirectory);
                    if (!unique.Add(full)) continue;
                    candidates.Add(full);
                }
                catch (Exception error) { result.Errors.Add((input ?? "空路径") + "：" + error.Message); }
            }
            // An explicitly selected parent is recycled as one item. Don't also delete its children,
            // even when the parent's own recycle operation is refused by Windows.
            List<string> selected = new List<string>();
            foreach (string candidate in candidates)
            {
                bool covered = false;
                foreach (string parent in candidates)
                    if (!candidate.Equals(parent, StringComparison.OrdinalIgnoreCase) && Directory.Exists(parent) && Within(candidate, parent)) { covered = true; break; }
                if (!covered) selected.Add(candidate);
            }
            return selected;
        }

        public static FeedResult Feed(IEnumerable<string> files, string applicationDirectory, IntPtr owner)
        {
            FeedResult result = new FeedResult();
            foreach (string path in PrepareInputs(files, applicationDirectory, result))
                try { RecycleOne(path, owner); result.Recycled++; }
                catch (Exception error) { result.Errors.Add(path + "：" + error.Message); }
            return result;
        }

        private static void RecycleOne(string path, IntPtr owner)
        {
            IFileOperation operation = null;
            IShellItem item = null;
            RecycleSink sink = new RecycleSink();
            try
            {
                operation = (IFileOperation)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("3AD05575-8857-4850-9277-11B85BDB8E09")));
                // Windows 10/11: RECYCLEONDELETE + ADDUNDORECORD, with no permanent-delete fallback.
                // PreDeleteItem additionally refuses when Windows cannot mark this item for recycling.
                Check(operation.SetOperationFlags(0x4 | 0x10 | 0x400 | 0x80000 | 0x100000 | 0x20000000));
                Check(operation.SetOwnerWindow(owner));
                Guid iid = typeof(IShellItem).GUID;
                Check(SHCreateItemFromParsingName(path, IntPtr.Zero, ref iid, out item));
                Check(operation.DeleteItem(item, sink));
                int performed = operation.PerformOperations();
                if (sink.RefusedPermanentDelete) throw new IOException("Windows 未将此文件标记为可回收，已拒绝删除，原文件保留。");
                Check(performed);
                bool aborted; Check(operation.GetAnyOperationsAborted(out aborted));
                if (aborted || !sink.Recycled || File.Exists(path) || Directory.Exists(path)) throw new IOException("未能确认放入回收站，未执行永久删除；请检查原位置和系统回收站。");
            }
            finally
            {
                if (item != null) Marshal.ReleaseComObject(item);
                if (operation != null) Marshal.ReleaseComObject(operation);
            }
        }

        private static void Check(int result) { if (result < 0) Marshal.ThrowExceptionForHR(result); }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
        private static extern int SHCreateItemFromParsingName(string path, IntPtr context, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out IShellItem item);

        [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItem { }

        [ComImport, Guid("947AAB5F-0A5C-4C13-B4D6-4BF7836FC9F8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IFileOperation
        {
            [PreserveSig] int Advise(IntPtr sink, out uint cookie);
            [PreserveSig] int Unadvise(uint cookie);
            [PreserveSig] int SetOperationFlags(uint flags);
            [PreserveSig] int SetProgressMessage([MarshalAs(UnmanagedType.LPWStr)] string message);
            [PreserveSig] int SetProgressDialog(IntPtr dialog);
            [PreserveSig] int SetProperties(IntPtr properties);
            [PreserveSig] int SetOwnerWindow(IntPtr owner);
            [PreserveSig] int ApplyPropertiesToItem(IShellItem item);
            [PreserveSig] int ApplyPropertiesToItems(IntPtr items);
            [PreserveSig] int RenameItem(IShellItem item, [MarshalAs(UnmanagedType.LPWStr)] string name, IntPtr sink);
            [PreserveSig] int RenameItems(IntPtr items, [MarshalAs(UnmanagedType.LPWStr)] string name);
            [PreserveSig] int MoveItem(IShellItem item, IShellItem destination, [MarshalAs(UnmanagedType.LPWStr)] string name, IntPtr sink);
            [PreserveSig] int MoveItems(IntPtr items, IShellItem destination);
            [PreserveSig] int CopyItem(IShellItem item, IShellItem destination, [MarshalAs(UnmanagedType.LPWStr)] string name, IntPtr sink);
            [PreserveSig] int CopyItems(IntPtr items, IShellItem destination);
            [PreserveSig] int DeleteItem(IShellItem item, IFileOperationProgressSink sink);
            [PreserveSig] int DeleteItems(IntPtr items);
            [PreserveSig] int NewItem(IShellItem destination, uint attributes, [MarshalAs(UnmanagedType.LPWStr)] string name, [MarshalAs(UnmanagedType.LPWStr)] string template, IntPtr sink);
            [PreserveSig] int PerformOperations();
            [PreserveSig] int GetAnyOperationsAborted([MarshalAs(UnmanagedType.Bool)] out bool aborted);
        }

        [Guid("04B0F1A7-9490-44BC-96E1-4296A31252E2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IFileOperationProgressSink
        {
            [PreserveSig] int StartOperations();
            [PreserveSig] int FinishOperations(int result);
            [PreserveSig] int PreRenameItem(uint flags, IntPtr item, [MarshalAs(UnmanagedType.LPWStr)] string name);
            [PreserveSig] int PostRenameItem(uint flags, IntPtr item, [MarshalAs(UnmanagedType.LPWStr)] string name, int result, IntPtr newItem);
            [PreserveSig] int PreMoveItem(uint flags, IntPtr item, IntPtr destination, [MarshalAs(UnmanagedType.LPWStr)] string name);
            [PreserveSig] int PostMoveItem(uint flags, IntPtr item, IntPtr destination, [MarshalAs(UnmanagedType.LPWStr)] string name, int result, IntPtr newItem);
            [PreserveSig] int PreCopyItem(uint flags, IntPtr item, IntPtr destination, [MarshalAs(UnmanagedType.LPWStr)] string name);
            [PreserveSig] int PostCopyItem(uint flags, IntPtr item, IntPtr destination, [MarshalAs(UnmanagedType.LPWStr)] string name, int result, IntPtr newItem);
            [PreserveSig] int PreDeleteItem(uint flags, IntPtr item);
            [PreserveSig] int PostDeleteItem(uint flags, IntPtr item, int result, IntPtr newItem);
            [PreserveSig] int PreNewItem(uint flags, IntPtr destination, [MarshalAs(UnmanagedType.LPWStr)] string name);
            [PreserveSig] int PostNewItem(uint flags, IntPtr destination, [MarshalAs(UnmanagedType.LPWStr)] string name, [MarshalAs(UnmanagedType.LPWStr)] string template, uint attributes, int result, IntPtr newItem);
            [PreserveSig] int UpdateProgress(uint total, uint complete);
            [PreserveSig] int ResetTimer();
            [PreserveSig] int PauseTimer();
            [PreserveSig] int ResumeTimer();
        }

        [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
        public sealed class RecycleSink : IFileOperationProgressSink
        {
            public bool Recycled;
            public bool RefusedPermanentDelete;
            public int StartOperations() { return 0; }
            public int FinishOperations(int result) { return 0; }
            public int PreDeleteItem(uint flags, IntPtr item)
            {
                const uint TSF_DELETE_RECYCLE_IF_POSSIBLE = 0x80;
                RefusedPermanentDelete = (flags & TSF_DELETE_RECYCLE_IF_POSSIBLE) == 0;
                Log.Write("回收前标志：0x" + flags.ToString("x8"));
                return RefusedPermanentDelete ? unchecked((int)0x80004004) : 0;
            }
            public int PostDeleteItem(uint flags, IntPtr item, int result, IntPtr newItem) { Recycled = result >= 0 && newItem != IntPtr.Zero; return 0; }
            public int PreRenameItem(uint f, IntPtr i, string n) { return unchecked((int)0x80004004); }
            public int PostRenameItem(uint f, IntPtr i, string n, int r, IntPtr j) { return 0; }
            public int PreMoveItem(uint f, IntPtr i, IntPtr d, string n) { return unchecked((int)0x80004004); }
            public int PostMoveItem(uint f, IntPtr i, IntPtr d, string n, int r, IntPtr j) { return 0; }
            public int PreCopyItem(uint f, IntPtr i, IntPtr d, string n) { return unchecked((int)0x80004004); }
            public int PostCopyItem(uint f, IntPtr i, IntPtr d, string n, int r, IntPtr j) { return 0; }
            public int PreNewItem(uint f, IntPtr i, string n) { return unchecked((int)0x80004004); }
            public int PostNewItem(uint f, IntPtr i, string n, string t, uint a, int r, IntPtr j) { return 0; }
            public int UpdateProgress(uint total, uint complete) { return 0; }
            public int ResetTimer() { return 0; }
            public int PauseTimer() { return 0; }
            public int ResumeTimer() { return 0; }
        }
    }
}
