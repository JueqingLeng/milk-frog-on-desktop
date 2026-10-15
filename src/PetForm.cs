using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Media;
using System.Threading;
using System.Windows.Forms;

namespace MilkyFrog
{
    public sealed class PetForm : LayeredWindow
    {
        private readonly string directory;
        private readonly Settings settings;
        private readonly Dictionary<string, Bitmap[]> sprites = new Dictionary<string, Bitmap[]>();
        private readonly System.Windows.Forms.Timer animation = new System.Windows.Forms.Timer();
        private readonly ManualResetEvent stopMonitor = new ManualResetEvent(false);
        private readonly SoundPlayer laughter;
        private readonly NotifyIcon tray;
        private readonly ContextMenuStrip menu;
        private readonly ToolTip tooltip = new ToolTip();
        private Thread gpuThread;
        private string gpuStatus = "独显监测初始化中…";
        private Point mouseOrigin, windowOrigin;
        private bool dragging, moved, feeding, pendingClose, sleepBack;
        private bool exiting;
        private DateTime exitUntil;
        private int sleepTurnFrame = -1;
        private int frame;
        private DateTime laughUntil, messageUntil;
        private string message = "左键大笑 · 拖动移动 · 右键菜单";
        private string previousPeriod = "";

        public PetForm(string directory, Settings settings)
        {
            this.directory = directory; this.settings = settings;
            Text = "冷玦清的奶蛙桌宠"; FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual; ShowInTaskbar = false; TopMost = true; AllowDrop = true;
            ClientSize = new Size(settings.PetSize + 20, settings.PetSize + 58);
            string assets = Path.Combine(directory, "assets");
            foreach (string state in new[] { "sleep", "sleep-back", "sleep-turn-back", "sleep-turn-front", "meal", "study", "computer", "game", "taichi", "laugh", "feed", "wave" })
            {
                Bitmap[] frames = new Bitmap[6];
                for (int index = 0; index < frames.Length; index++)
                    using (Image source = Image.FromFile(Path.Combine(assets, state + "-" + index + ".png"))) frames[index] = new Bitmap(source);
                sprites[state] = frames;
            }
            laughter = new SoundPlayer(Path.Combine(assets, "laugh.wav")); laughter.Load();
            Icon = new Icon(Path.Combine(assets, "frog.ico"));
            Rectangle area = Screen.PrimaryScreen.WorkingArea;
            Location = settings.X == int.MinValue ? new Point(area.Right - Width - 35, area.Bottom - Height - 20) : new Point(settings.X, settings.Y);
            ClampToScreen();
            messageUntil = DateTime.UtcNow.AddSeconds(8);

            menu = new ContextMenuStrip();
            menu.Items.Add("投喂文件到回收站…", null, delegate { ChooseFiles(); });
            menu.Items.Add("投喂文件夹到回收站…", null, delegate { ChooseFolder(); });
            ToolStripMenuItem sound = new ToolStripMenuItem("大笑音效") { Checked = settings.Sound, CheckOnClick = true };
            sound.CheckedChanged += delegate { settings.Sound = sound.Checked; if (!settings.Sound) laughter.Stop(); };
            menu.Items.Add(sound);
            menu.Items.Add("独显监测状态", null, delegate { MessageBox.Show(this, gpuStatus, "奶蛙", MessageBoxButtons.OK, MessageBoxIcon.Information); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("关闭桌宠", null, delegate { RequestClose("右键关闭"); });
            ContextMenuStrip = menu;
            tray = new NotifyIcon { Icon = Icon, Text = "冷玦清的奶蛙 · 右键可关闭", ContextMenuStrip = menu, Visible = true };
            tray.DoubleClick += delegate { Show(); Activate(); ClampToScreen(); };
            tooltip.SetToolTip(this, "左键点击大笑；按住左键拖动；把不要的文件或文件夹拖到奶蛙身上，送入回收站。");
            animation.Interval = 140; animation.Tick += delegate { TickPet(); };
            DragEnter += FilesEnter; DragOver += FilesEnter; DragLeave += delegate { ShowMessage("再见，下次再来投喂。", 2); };
            DragDrop += FilesDropped;
            Shown += delegate { TickPet(); animation.Start(); StartGpuMonitor(); };
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        private void TickPet()
        {
            if (exiting)
            {
                if (DateTime.UtcNow >= exitUntil) { Close(); return; }
                frame = (frame + 1) % 6;
                using (Bitmap goodbye = Render("wave", frame, "先挥挥手，我们下次见！")) Present(goodbye);
                return;
            }
            DateTime now = Schedule.BeijingNow(); Activity activity = Schedule.At(now.Hour * 60 + now.Minute);
            if (previousPeriod != activity.Caption) { previousPeriod = activity.Caption; laughUntil = DateTime.MinValue; sleepBack = false; sleepTurnFrame = -1; laughter.Stop(); }
            string sprite = feeding ? "feed" : DateTime.UtcNow < laughUntil ? "laugh" : activity.Sprite == "sleep" && sleepBack ? "sleep-back" : activity.Sprite;
            string caption = feeding ? "正在投喂回收站…" : DateTime.UtcNow < messageUntil ? message : activity.Caption;
            frame = (frame + 1) % 6;
            int spriteFrame = frame;
            if (activity.Sprite == "sleep" && sleepTurnFrame >= 0 && !feeding)
            {
                sprite = sleepBack ? "sleep-turn-back" : "sleep-turn-front"; spriteFrame = sleepTurnFrame++;
                if (sleepTurnFrame == 6) sleepTurnFrame = -1;
            }
            using (Bitmap picture = Render(sprite, spriteFrame, caption)) Present(picture);
        }

        public Bitmap Render(string state, int index, string caption)
        {
            Bitmap surface = new Bitmap(Width, Height, PixelFormat.Format32bppArgb);
            using (Graphics graphics = Graphics.FromImage(surface))
            {
                graphics.Clear(Color.Transparent); graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.DrawImage(sprites[state][index % 6], new Rectangle(10, 0, settings.PetSize, settings.PetSize));
                Rectangle bubble = new Rectangle(6, settings.PetSize + 4, Width - 12, 45);
                using (GraphicsPath shape = Rounded(bubble, 12))
                using (Brush fill = new SolidBrush(Color.FromArgb(235, 250, 246, 231)))
                using (Pen edge = new Pen(Color.FromArgb(180, 124, 148, 112), 1))
                { graphics.FillPath(fill, shape); graphics.DrawPath(edge, shape); }
                using (Font font = new Font("Microsoft YaHei", 9, FontStyle.Regular))
                using (Brush ink = new SolidBrush(Color.FromArgb(72, 88, 64)))
                using (StringFormat format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter })
                    graphics.DrawString(caption, font, ink, bubble, format);
            }
            return surface;
        }

        private static GraphicsPath Rounded(Rectangle r, int radius)
        {
            GraphicsPath p = new GraphicsPath(); int d = radius * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90); p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.X, r.Bottom - d, d, d, 90, 90); p.CloseFigure(); return p;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left) { dragging = true; moved = false; mouseOrigin = Cursor.Position; windowOrigin = Location; Capture = true; }
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!dragging) return;
            Point cursor = Cursor.Position;
            int dx = cursor.X - mouseOrigin.X, dy = cursor.Y - mouseOrigin.Y;
            if (Math.Abs(dx) + Math.Abs(dy) > 6) moved = true;
            if (moved) { Location = new Point(windowOrigin.X + dx, windowOrigin.Y + dy); ClampToScreen(); }
        }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left) return;
            dragging = false; Capture = false;
            if (!moved && !feeding && !exiting)
            {
                DateTime now = Schedule.BeijingNow();
                if (Schedule.At(now.Hour * 60 + now.Minute).Sprite == "sleep")
                {
                    if (sleepTurnFrame < 0) { sleepBack = !sleepBack; sleepTurnFrame = 0; ShowMessage("翻个身，继续睡…", 2); }
                }
                else Laugh();
            }
        }

        public void Laugh()
        {
            if (exiting) return;
            laughUntil = DateTime.UtcNow.AddSeconds(15.5); ShowMessage("哈哈哈哈！", 3);
            if (settings.Sound) { try { laughter.Stop(); laughter.Play(); } catch (Exception error) { Log.Write(error.Message); } }
            TickPet();
        }

        private void ShowMessage(string text, double seconds) { message = text; messageUntil = DateTime.UtcNow.AddSeconds(seconds); }

        private void ClampToScreen()
        {
            Rectangle area = Screen.FromPoint(new Point(Left + Width / 2, Top + Height / 2)).WorkingArea;
            Location = new Point(Math.Max(area.Left, Math.Min(Left, area.Right - Width)), Math.Max(area.Top, Math.Min(Top, area.Bottom - Height)));
        }

        private void FilesEnter(object sender, DragEventArgs e)
        {
            e.Effect = !feeding && !exiting && e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Move : DragDropEffects.None;
            if (e.Effect == DragDropEffects.Move) ShowMessage("喂给我吧，我送去回收站。", 2);
        }
        private void FilesDropped(object sender, DragEventArgs e)
        {
            string[] files = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (files != null) Feed(files);
        }
        private void ChooseFiles()
        {
            if (feeding || exiting) return;
            using (OpenFileDialog dialog = new OpenFileDialog { Title = "选中要投喂回收站的文件", Multiselect = true, CheckFileExists = true })
                if (dialog.ShowDialog(this) == DialogResult.OK) Feed(dialog.FileNames);
        }
        private void ChooseFolder()
        {
            if (feeding || exiting) return;
            using (FolderBrowserDialog dialog = new FolderBrowserDialog { Description = "选择要投喂到回收站的文件夹（包含其中的内容）", ShowNewFolderButton = false })
                if (dialog.ShowDialog(this) == DialogResult.OK) Feed(new[] { dialog.SelectedPath });
        }
        public void Feed(string[] files)
        {
            if (feeding || exiting || files.Length == 0) return;
            feeding = true; laughter.Stop(); laughUntil = DateTime.MinValue;
            IntPtr owner = Handle;
            Thread worker = new Thread(delegate()
            {
                FeedResult result = WindowsRecycle.Feed(files, directory, owner);
                if (IsDisposed || !IsHandleCreated) return;
                try { BeginInvoke((Action)delegate {
                    feeding = false;
                    ShowMessage(result.Recycled > 0 ? "吃掉 " + result.Recycled + " 份文件，已放入回收站。" : "没有吃掉文件，原文件保留。", 6);
                    if (result.Errors.Count > 0) { Log.Write(String.Join(Environment.NewLine, result.Errors)); MessageBox.Show(this, String.Join(Environment.NewLine, result.Errors), "这些文件没有被投喂", MessageBoxButtons.OK, MessageBoxIcon.Information); }
                    if (pendingClose) RequestClose("投喂完成后退出");
                }); } catch (InvalidOperationException) { }
            });
            worker.SetApartmentState(ApartmentState.STA); worker.IsBackground = true; worker.Start();
        }

        private void StartGpuMonitor()
        {
            if (!settings.AutoCloseOnDiscreteGpu) { gpuStatus = "设置中已关闭独显监测。"; return; }
            gpuThread = new Thread(delegate()
            {
                using (GpuMonitor monitor = new GpuMonitor())
                {
                    gpuStatus = monitor.Status; Log.Write(gpuStatus);
                    ActivityGate gate = new ActivityGate(settings.GpuThresholdPercent, settings.GpuConsecutiveSamples);
                    while (!stopMonitor.WaitOne(settings.GpuPollMilliseconds))
                    {
                        double? activity;
                        try { activity = monitor.Sample(); gpuStatus = monitor.Status; }
                        catch (Exception error) { activity = null; gpuStatus = "独显监测出错：" + error.Message; }
                        if (gate.Observe(activity))
                        {
                            Log.Write("独显正在使用，自动关闭桌宠。" + gpuStatus);
                            if (IsHandleCreated && !IsDisposed) try { BeginInvoke((Action)delegate { RequestClose("独显活动，自动关闭"); }); } catch (InvalidOperationException) { }
                            break;
                        }
                    }
                }
            });
            gpuThread.IsBackground = true; gpuThread.Name = "Discrete GPU monitor"; gpuThread.Start();
        }

        internal void RequestClose(string reason)
        {
            if (exiting) return;
            Log.Write(reason);
            if (feeding) { pendingClose = true; ShowMessage("投喂完成后招手告别。", 10); return; }
            exiting = true; exitUntil = DateTime.UtcNow.AddSeconds(2);
            stopMonitor.Set();
            laughter.Stop(); laughUntil = DateTime.MinValue;
            dragging = false; Capture = false;
            TickPet();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing && (!exiting || DateTime.UtcNow < exitUntil))
            {
                e.Cancel = true;
                RequestClose("手动关闭，招手告别");
                return;
            }
            base.OnFormClosing(e);
        }
        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            stopMonitor.Set(); animation.Stop(); laughter.Stop();
            settings.X = Left; settings.Y = Top; settings.Save(directory);
            base.OnFormClosed(e);
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                stopMonitor.Set(); animation.Dispose(); laughter.Dispose(); tooltip.Dispose();
                tray.Visible = false; tray.Dispose(); menu.Dispose();
                foreach (Bitmap[] sequence in sprites.Values) foreach (Bitmap bitmap in sequence) bitmap.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
