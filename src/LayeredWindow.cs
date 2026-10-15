using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MilkyFrog
{
    public class LayeredWindow : Form
    {
        protected override CreateParams CreateParams
        {
            get { CreateParams parameters = base.CreateParams; parameters.ExStyle |= 0x80000 | 0x80; return parameters; }
        }

        protected void Present(Bitmap bitmap)
        {
            if (!IsHandleCreated || IsDisposed) return;
            IntPtr screen = GetDC(IntPtr.Zero), memory = CreateCompatibleDC(screen);
            IntPtr handle = bitmap.GetHbitmap(Color.FromArgb(0)), previous = SelectObject(memory, handle);
            try
            {
                NativePoint source = new NativePoint(0, 0), location = new NativePoint(Left, Top);
                NativeSize size = new NativeSize(bitmap.Width, bitmap.Height);
                Blend blend = new Blend { Operation = 0, Alpha = 255, Format = 1 };
                if (!UpdateLayeredWindow(Handle, screen, ref location, ref size, memory, ref source, 0, ref blend, 2))
                    throw new System.ComponentModel.Win32Exception();
            }
            finally { SelectObject(memory, previous); DeleteObject(handle); DeleteDC(memory); ReleaseDC(IntPtr.Zero, screen); }
        }

        [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X, Y; public NativePoint(int x, int y) { X = x; Y = y; } }
        [StructLayout(LayoutKind.Sequential)] private struct NativeSize { public int Width, Height; public NativeSize(int width, int height) { Width = width; Height = height; } }
        [StructLayout(LayoutKind.Sequential, Pack = 1)] private struct Blend { public byte Operation, Flags, Alpha, Format; }
        [DllImport("user32.dll", SetLastError = true)] private static extern bool UpdateLayeredWindow(IntPtr window, IntPtr screen, ref NativePoint location, ref NativeSize size, IntPtr source, ref NativePoint sourcePoint, uint key, ref Blend blend, uint flags);
        [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr window);
        [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr window, IntPtr dc);
        [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
        [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
        [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
    }
}
