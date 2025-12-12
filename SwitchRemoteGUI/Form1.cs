using System;
using System.Drawing;
using System.IO.Ports;
using System.Windows.Forms;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace SwitchRemoteGUI
{
    public partial class Form1 : Form
    {
        string portName = "COM5"; // ★自分のポート番号
        SerialPort port;

        // --- Windows API ---
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern bool ShowWindowAsync(IntPtr hWnd, int nCmdShow);
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern int SendMessage(IntPtr hWnd, int Msg, int wParam, int lParam);
        [DllImport("user32.dll")] public static extern bool ReleaseCapture();

        private const int SW_RESTORE = 9;
        private const int SW_MINIMIZE = 6;

        bool _isReady = false;

        Label lblDragBar;
        Button btnObsShow, btnObsHide;
        Button btnZL, btnL, btnLR, btnR, btnZR;
        Button btnUp, btnLeft, btnDown, btnRight;
        Button btnX, btnY, btnB, btnA;
        Button btnMinus, btnHome, btnCap, btnPlus;
        Button btnL3, btnR3;

        // 枠線の太さ
        private int borderSize = 5;

        public Form1()
        {
            InitializeComponent();
            this.TopMost = true;
            this.FormBorderStyle = FormBorderStyle.None;
            this.MinimumSize = new Size(300, 400);

            // ★透明化と半透明の設定
            // 1. マゼンタ色を「透明」とみなす設定
            this.TransparencyKey = Color.Magenta;
            this.BackColor = Color.Magenta;

            // 2. ウィンドウ全体の不透明度を下げる (0.7 = 70%見えて30%透ける)
            // これでボタンも枠も「半透明」になります
            this.Opacity = 0.70;

            // 枠線を描くための余白
            this.Padding = new Padding(borderSize);

            this.KeyPreview = true;
            this.KeyDown += Form1_KeyDown;

            // 描画イベント(枠線を描くため)
            this.Paint += Form1_Paint;

            ConnectPort();
            CreateUI();

            _isReady = true;
            UpdateLayout();
        }

        // ★枠線を描画する処理
        private void Form1_Paint(object sender, PaintEventArgs e)
        {
            // 背景はMagenta(透明)なので、縁(ふち)の部分だけ黒く塗る
            // これで「枠」が見えるようになり、リサイズ操作も可能になる
            Color frameColor = Color.Black;

            using (Pen p = new Pen(frameColor, borderSize))
            {
                // 枠の内側に描画
                p.Alignment = System.Drawing.Drawing2D.PenAlignment.Inset;
                e.Graphics.DrawRectangle(p, this.ClientRectangle);
            }
        }

        // ウィンドウの端っこ判定（リサイズ用）
        protected override void WndProc(ref Message m)
        {
            const int WM_NCHITTEST = 0x84;
            base.WndProc(ref m);
            if (m.Msg == WM_NCHITTEST)
            {
                Point pos = this.PointToClient(new Point(m.LParam.ToInt32()));
                int grip = 16;

                // 四隅と端の判定
                if (pos.X <= grip && pos.Y <= grip) m.Result = (IntPtr)13; // TOPLEFT
                else if (pos.X >= this.ClientSize.Width - grip && pos.Y <= grip) m.Result = (IntPtr)14; // TOPRIGHT
                else if (pos.X <= grip && pos.Y >= this.ClientSize.Height - grip) m.Result = (IntPtr)16; // BOTTOMLEFT
                else if (pos.X >= this.ClientSize.Width - grip && pos.Y >= this.ClientSize.Height - grip) m.Result = (IntPtr)17; // BOTTOMRIGHT
                else if (pos.X <= grip) m.Result = (IntPtr)10; // LEFT
                else if (pos.X >= this.ClientSize.Width - grip) m.Result = (IntPtr)11; // RIGHT
                else if (pos.Y <= grip) m.Result = (IntPtr)12; // TOP
                else if (pos.Y >= this.ClientSize.Height - grip) m.Result = (IntPtr)15; // BOTTOM
            }
        }

        private void DragBar_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                ReleaseCapture();
                SendMessage(Handle, 0xA1, 0x2, 0);
            }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            // サイズ変更時に再描画して枠線をきれいに保つ
            this.Invalidate();
            UpdateLayout();
        }

        void ConnectPort()
        {
            try
            {
                port = new SerialPort(portName, 9600);
                port.Open();
            }
            catch { }
        }

        void Send(string cmd)
        {
            if (port != null && port.IsOpen) port.Write(cmd);
        }

        void ControlApp(string keyword, bool show)
        {
            Process[] processList = Process.GetProcesses();
            foreach (Process p in processList)
            {
                if (!string.IsNullOrEmpty(p.MainWindowTitle) &&
                   (p.ProcessName.ToLower().Contains(keyword) || p.MainWindowTitle.ToLower().Contains(keyword)))
                {
                    if (show)
                    {
                        if (IsIconic(p.MainWindowHandle)) ShowWindowAsync(p.MainWindowHandle, SW_RESTORE);
                        SetForegroundWindow(p.MainWindowHandle);
                    }
                    else ShowWindowAsync(p.MainWindowHandle, SW_MINIMIZE);
                    return;
                }
            }
        }

        void CreateUI()
        {
            lblDragBar = new Label();
            lblDragBar.Text = ":::: Switch Controller ::::";
            lblDragBar.TextAlign = ContentAlignment.MiddleCenter;
            lblDragBar.BackColor = Color.FromArgb(40, 40, 40); // バーも少し暗めに
            lblDragBar.ForeColor = Color.White;
            lblDragBar.Height = 24;
            lblDragBar.MouseDown += DragBar_MouseDown;
            this.Controls.Add(lblDragBar);

            Button MkBtn(string txt, string cmd, Color? bg = null)
            {
                Button b = new Button();
                b.Text = txt;
                b.BackColor = bg ?? Color.White;
                b.FlatStyle = FlatStyle.Flat;
                b.FlatAppearance.BorderSize = 0;
                b.TabStop = false;
                if (cmd != "") b.Click += (s, e) => Send(cmd);
                this.Controls.Add(b);
                return b;
            }

            btnObsShow = MkBtn("📺 戻す", "", Color.LightSkyBlue);
            btnObsShow.Click += (s, e) => ControlApp("obs", true);
            btnObsHide = MkBtn("＿ 隠す", "", Color.LightGray);
            btnObsHide.Click += (s, e) => ControlApp("obs", false);

            btnZL = MkBtn("ZL", "e", Color.DarkGray);
            btnL = MkBtn("L", "q", Color.Gray);
            btnLR = MkBtn("LR", "qw", Color.Orange);
            btnR = MkBtn("R", "w", Color.Gray);
            btnZR = MkBtn("ZR", "r", Color.DarkGray);

            btnUp = MkBtn("↑", "I");
            btnLeft = MkBtn("←", "J");
            btnDown = MkBtn("↓", "K");
            btnRight = MkBtn("→", "L");

            btnX = MkBtn("X", "s", Color.Yellow);
            btnY = MkBtn("Y", "a", Color.LightGreen);
            btnB = MkBtn("B", "x", Color.Red);
            btnA = MkBtn("A", "z", Color.Cyan);

            btnMinus = MkBtn("-", "m", Color.LightGray);
            btnHome = MkBtn("🏠", "h", Color.LightBlue);
            btnCap = MkBtn("📷", "c", Color.Pink);
            btnPlus = MkBtn("+", "n", Color.LightGray);

            btnL3 = MkBtn("L3", "3", Color.Silver);
            btnR3 = MkBtn("R3", "4", Color.Silver);
        }

        void UpdateLayout()
        {
            if (!_isReady) return;

            int W = this.ClientSize.Width;
            int H = this.ClientSize.Height;
            // 枠線の分だけ内側に配置
            int pad = borderSize;
            int innerW = W - pad * 2;
            int innerX = pad;
            int innerY = pad;

            // ドラッグバー
            lblDragBar.Bounds = new Rectangle(innerX, innerY, innerW, 24);

            int stdH = 35;
            int margin = 5;

            // --- 上から配置 ---
            int yObs = innerY + 24 + margin;
            btnObsShow.Bounds = new Rectangle(innerX, yObs, (innerW - margin) / 2, stdH);
            btnObsHide.Bounds = new Rectangle(innerX + (innerW - margin) / 2 + margin, yObs, (innerW - margin) / 2, stdH);

            int ySh = yObs + stdH + margin;
            int shW = (innerW - margin * 4) / 5;
            btnZL.Bounds = new Rectangle(innerX, ySh, shW, stdH);
            btnL.Bounds = new Rectangle(innerX + shW + margin, ySh, shW, stdH);
            btnLR.Bounds = new Rectangle(innerX + (shW + margin) * 2, ySh, shW, stdH);
            btnR.Bounds = new Rectangle(innerX + (shW + margin) * 3, ySh, shW, stdH);
            btnZR.Bounds = new Rectangle(innerX + (shW + margin) * 4, ySh, shW, stdH);

            // --- 下から配置 ---
            int yStick = H - pad - stdH;
            int stickW = (innerW - margin) / 2;
            btnL3.Bounds = new Rectangle(innerX, yStick, stickW, stdH);
            btnR3.Bounds = new Rectangle(innerX + stickW + margin, yStick, stickW, stdH);

            int ySys = yStick - margin - stdH;
            int sysW = (innerW - margin * 3) / 4;
            btnMinus.Bounds = new Rectangle(innerX, ySys, sysW, stdH);
            btnHome.Bounds = new Rectangle(innerX + sysW + margin, ySys, sysW, stdH);
            btnCap.Bounds = new Rectangle(innerX + (sysW + margin) * 2, ySys, sysW, stdH);
            btnPlus.Bounds = new Rectangle(innerX + (sysW + margin) * 3, ySys, sysW, stdH);

            // --- 十字キー/ABXY (中央) ---
            int yMainTop = ySh + stdH + margin;
            int yMainBottom = ySys - margin;
            int mainH = yMainBottom - yMainTop;

            int btnSize = Math.Min((innerW / 2 - margin) / 3, mainH / 3);
            btnSize = Math.Max(20, btnSize);

            // 十字キー (左)
            int leftCenterX = innerX + innerW / 4;
            int mainCenterY = yMainTop + mainH / 2;

            btnUp.Bounds = new Rectangle(leftCenterX - btnSize / 2, mainCenterY - btnSize / 2 - btnSize, btnSize, btnSize);
            btnLeft.Bounds = new Rectangle(leftCenterX - btnSize / 2 - btnSize, mainCenterY - btnSize / 2, btnSize, btnSize);
            btnRight.Bounds = new Rectangle(leftCenterX - btnSize / 2 + btnSize, mainCenterY - btnSize / 2, btnSize, btnSize);
            btnDown.Bounds = new Rectangle(leftCenterX - btnSize / 2, mainCenterY - btnSize / 2 + btnSize, btnSize, btnSize);

            // ABXY (右)
            int rightCenterX = innerX + innerW * 3 / 4;

            btnX.Bounds = new Rectangle(rightCenterX - btnSize / 2, mainCenterY - btnSize / 2 - btnSize, btnSize, btnSize);
            btnY.Bounds = new Rectangle(rightCenterX - btnSize / 2 - btnSize, mainCenterY - btnSize / 2, btnSize, btnSize);
            btnA.Bounds = new Rectangle(rightCenterX - btnSize / 2 + btnSize, mainCenterY - btnSize / 2, btnSize, btnSize);
            btnB.Bounds = new Rectangle(rightCenterX - btnSize / 2, mainCenterY - btnSize / 2 + btnSize, btnSize, btnSize);

            // フォント設定
            Font fontMain = new Font("Arial", Math.Max(10, btnSize / 3), FontStyle.Bold);
            Font fontSub = new Font("Arial", 9, FontStyle.Bold);

            foreach (Control c in this.Controls)
            {
                if (c is Button)
                {
                    bool isMain = (c == btnUp || c == btnLeft || c == btnRight || c == btnDown ||
                                   c == btnX || c == btnY || c == btnA || c == btnB);
                    c.Font = isMain ? fontMain : fontSub;
                }
            }
        }

        private void Form1_KeyDown(object sender, KeyEventArgs e)
        {
            switch (e.KeyCode)
            {
                case Keys.Z: Send("z"); break;
                case Keys.X: Send("x"); break;
                case Keys.S: Send("s"); break;
                case Keys.A: Send("a"); break;
                case Keys.Q: Send("q"); break;
                case Keys.W: Send("w"); break;
                case Keys.E: Send("e"); break;
                case Keys.R: Send("r"); break;
                case Keys.Enter: Send("n"); break;
                case Keys.Back: Send("m"); break;
                case Keys.Escape: Send("h"); break;
                case Keys.C: Send("c"); break;
                case Keys.I: Send("I"); break;
                case Keys.J: Send("J"); break;
                case Keys.K: Send("K"); break;
                case Keys.L: Send("L"); break;
                case Keys.T: Send("3"); break;
                case Keys.Y: Send("4"); break;
            }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            if (port != null && port.IsOpen) port.Close();
            base.OnFormClosed(e);
        }
    }
}