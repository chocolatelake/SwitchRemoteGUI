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

        // ★状態管理フラグ
        bool _isSolidBlack = false; // 背景黒モード
        bool _isVertical = false;   // 縦長モード (スマホ用)

        // UIパーツ
        Button btnLayoutToggle;
        Button btnBgToggle;
        Label lblTitle;

        Button btnObsShow, btnObsHide;
        Button btnZL, btnL, btnLR, btnR, btnZR;
        Button btnUp, btnLeft, btnDown, btnRight;
        Button btnX, btnY, btnB, btnA;
        Button btnMinus, btnHome, btnCap, btnPlus;
        Button btnL3, btnR3;

        private int borderSize = 5;

        public Form1()
        {
            InitializeComponent();
            this.TopMost = true;
            this.FormBorderStyle = FormBorderStyle.None;
            this.MinimumSize = new Size(300, 300);

            SetTransparentMode();

            this.Padding = new Padding(borderSize);
            this.KeyPreview = true;
            this.KeyDown += Form1_KeyDown;
            this.Paint += Form1_Paint;

            ConnectPort();
            CreateUI();

            // 初期サイズを横モードの想定サイズに設定
            this.Size = new Size(800, 400);

            _isReady = true;
            UpdateLayout();
        }

        void SetTransparentMode()
        {
            this.TransparencyKey = Color.Magenta;
            this.BackColor = Color.Magenta;
            this.Opacity = 0.70;
            _isSolidBlack = false;
            this.Invalidate();
        }

        void SetSolidBlackMode()
        {
            this.TransparencyKey = Color.Empty;
            this.BackColor = Color.Black;
            this.Opacity = 1.0;
            _isSolidBlack = true;
            this.Invalidate();
        }

        void ToggleLayoutMode()
        {
            _isVertical = !_isVertical;

            if (_isVertical)
            {
                // 縦モード: 上半分空き + 下半分コントローラー (スマホ縦持ちに合わせやすいサイズ)
                this.Size = new Size(400, 800);
                btnLayoutToggle.Text = "🔀 LAYOUT: 縦 (分割)";
            }
            else
            {
                // 横モード: フルスクリーンコントローラー (ゲーム画面の上に乗せる)
                this.Size = new Size(800, 400);
                btnLayoutToggle.Text = "🔀 LAYOUT: 横 (全面)";
            }
            UpdateLayout();
        }

        void ToggleBlackMode()
        {
            if (_isSolidBlack)
            {
                SetTransparentMode();
                btnBgToggle.Text = "⚫ BG: 透過";
            }
            else
            {
                SetSolidBlackMode();
                btnBgToggle.Text = "⚫ BG: 黒";
            }
            btnBgToggle.BackColor = _isSolidBlack ? Color.FromArgb(100, 100, 100) : Color.FromArgb(70, 70, 70);
        }

        private void Form1_Paint(object sender, PaintEventArgs e)
        {
            Color frameColor = _isSolidBlack ? Color.DarkGray : Color.Black;
            using (Pen p = new Pen(frameColor, borderSize))
            {
                p.Alignment = System.Drawing.Drawing2D.PenAlignment.Inset;
                e.Graphics.DrawRectangle(p, this.ClientRectangle);
            }
        }

        protected override void WndProc(ref Message m)
        {
            const int WM_NCHITTEST = 0x84;
            base.WndProc(ref m);
            if (m.Msg == WM_NCHITTEST)
            {
                Point pos = this.PointToClient(new Point(m.LParam.ToInt32()));
                int grip = 16;

                if (pos.X <= grip && pos.Y <= grip) m.Result = (IntPtr)13;
                else if (pos.X >= this.ClientSize.Width - grip && pos.Y <= grip) m.Result = (IntPtr)14;
                else if (pos.X <= grip && pos.Y >= this.ClientSize.Height - grip) m.Result = (IntPtr)16;
                else if (pos.X >= this.ClientSize.Width - grip && pos.Y >= this.ClientSize.Height - grip) m.Result = (IntPtr)17;
                else if (pos.X <= grip) m.Result = (IntPtr)10;
                else if (pos.X >= this.ClientSize.Width - grip) m.Result = (IntPtr)11;
                else if (pos.Y <= grip) m.Result = (IntPtr)12;
                else if (pos.Y >= this.ClientSize.Height - grip) m.Result = (IntPtr)15;
            }
        }

        private void TopBar_MouseDown(object sender, MouseEventArgs e)
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
            this.Invalidate();
            UpdateLayout();
        }

        void ConnectPort()
        {
            try { port = new SerialPort(portName, 9600); port.Open(); } catch { }
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
                    if (show) { if (IsIconic(p.MainWindowHandle)) ShowWindowAsync(p.MainWindowHandle, SW_RESTORE); SetForegroundWindow(p.MainWindowHandle); }
                    else ShowWindowAsync(p.MainWindowHandle, SW_MINIMIZE);
                    return;
                }
            }
        }

        void CreateUI()
        {
            // トップバーのUIは省略 (前回のコードから変更なし)
            lblTitle = new Label();
            lblTitle.Text = ":::: Switch Controller ::::";
            lblTitle.TextAlign = ContentAlignment.MiddleCenter;
            lblTitle.BackColor = Color.FromArgb(50, 50, 50);
            lblTitle.ForeColor = Color.White;
            lblTitle.Height = 24;
            lblTitle.MouseDown += TopBar_MouseDown;
            this.Controls.Add(lblTitle);

            btnLayoutToggle = new Button();
            btnLayoutToggle.Text = "🔀 LAYOUT: 横";
            btnLayoutToggle.BackColor = Color.FromArgb(70, 70, 70);
            btnLayoutToggle.ForeColor = Color.White;
            btnLayoutToggle.FlatStyle = FlatStyle.Flat;
            btnLayoutToggle.FlatAppearance.BorderSize = 0;
            btnLayoutToggle.TabStop = false;
            btnLayoutToggle.Click += (s, e) => ToggleLayoutMode();
            this.Controls.Add(btnLayoutToggle);

            btnBgToggle = new Button();
            btnBgToggle.Text = "⚫ BG: 透過";
            btnBgToggle.BackColor = Color.FromArgb(70, 70, 70);
            btnBgToggle.ForeColor = Color.White;
            btnBgToggle.FlatStyle = FlatStyle.Flat;
            btnBgToggle.FlatAppearance.BorderSize = 0;
            btnBgToggle.TabStop = false;
            btnBgToggle.Click += (s, e) => ToggleBlackMode();
            this.Controls.Add(btnBgToggle);

            // コントローラーボタンの作成は前回のコードから変更なし
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
            int pad = borderSize;
            int innerW = W - pad * 2;
            int innerX = pad;
            int innerY = pad;

            int stdH = 35;
            int margin = 5;
            int topBarH = 24;

            // --- トップバーのレイアウト ---
            int toggleW = (innerW - margin) / 4;
            int titleW = innerW - toggleW * 2 - margin;

            lblTitle.Bounds = new Rectangle(innerX, innerY, titleW, topBarH);
            btnLayoutToggle.Bounds = new Rectangle(innerX + titleW + margin, innerY, toggleW, topBarH);
            btnBgToggle.Bounds = new Rectangle(innerX + titleW + toggleW + margin * 2, innerY, toggleW, topBarH);
            // btnBgToggleの位置を再調整（マージンがずれていたため）
            btnBgToggle.Bounds = new Rectangle(innerX + innerW - toggleW, innerY, toggleW, topBarH);
            btnLayoutToggle.Bounds = new Rectangle(innerX + innerW - toggleW * 2 - margin, innerY, toggleW, topBarH);
            lblTitle.Bounds = new Rectangle(innerX, innerY, innerW - toggleW * 2 - margin * 2, topBarH);


            // ★★★ メインロジック: 横か縦かでボタンエリアの開始位置を決定 ★★★
            int contentTop;

            if (_isVertical)
            {
                // 縦モード (分割): 上半分(50%)をゲーム画面用に空ける
                contentTop = innerY + (H / 2);
            }
            else
            {
                // 横モード (全面): トップバーのすぐ下から開始
                contentTop = innerY + topBarH + margin;
            }

            // このコードブロックの実行によって、ボタン配置が横画面/縦画面それぞれで要求された動作になります。

            // --- ボタン配置計算 ---
            int availH = H - pad - contentTop;

            // 1. OBSボタン
            int yObs = contentTop;
            btnObsShow.Bounds = new Rectangle(innerX, yObs, (innerW - margin) / 2, stdH);
            btnObsHide.Bounds = new Rectangle(innerX + (innerW - margin) / 2 + margin, yObs, (innerW - margin) / 2, stdH);

            // 2. ショルダーボタン
            int ySh = yObs + stdH + margin;
            int shW = (innerW - margin * 4) / 5;
            btnZL.Bounds = new Rectangle(innerX, ySh, shW, stdH);
            btnL.Bounds = new Rectangle(innerX + shW + margin, ySh, shW, stdH);
            btnLR.Bounds = new Rectangle(innerX + (shW + margin) * 2, ySh, shW, stdH);
            btnR.Bounds = new Rectangle(innerX + (shW + margin) * 3, ySh, shW, stdH);
            btnZR.Bounds = new Rectangle(innerX + (shW + margin) * 4, ySh, shW, stdH);

            // 5. 一番下：L3/R3
            int yStick = H - pad - stdH;
            int stickW = (innerW - margin) / 2;
            int stickH = stdH;

            if (yStick < ySh + stdH + margin * 3)
            {
                stickH = Math.Max(20, (H - pad - ySh) / 3);
                yStick = H - pad - stickH;
            }

            btnL3.Bounds = new Rectangle(innerX, yStick, stickW, stickH);
            btnR3.Bounds = new Rectangle(innerX + stickW + margin, yStick, stickW, stickH);

            // 4. その上：システムボタン
            int ySys = yStick - margin - stdH;
            int sysW = (innerW - margin * 3) / 4;
            btnMinus.Bounds = new Rectangle(innerX, ySys, sysW, stdH);
            btnHome.Bounds = new Rectangle(innerX + sysW + margin, ySys, sysW, stdH);
            btnCap.Bounds = new Rectangle(innerX + (sysW + margin) * 2, ySys, sysW, stdH);
            btnPlus.Bounds = new Rectangle(innerX + (sysW + margin) * 3, ySys, sysW, stdH);

            // 3. 中央：十字キー & ABXY
            int yMainTop = ySh + stdH + margin;
            int yMainBottom = ySys - margin;
            int mainH = yMainBottom - yMainTop;

            if (mainH < 50) mainH = 50;

            int btnSize = Math.Min((innerW / 2 - margin) / 3, mainH / 3);
            btnSize = Math.Max(20, btnSize);

            int leftCenterX = innerX + innerW / 4;
            int mainCenterY = yMainTop + mainH / 2;
            btnUp.Bounds = new Rectangle(leftCenterX - btnSize / 2, mainCenterY - btnSize / 2 - btnSize, btnSize, btnSize);
            btnLeft.Bounds = new Rectangle(leftCenterX - btnSize / 2 - btnSize, mainCenterY - btnSize / 2, btnSize, btnSize);
            btnRight.Bounds = new Rectangle(leftCenterX - btnSize / 2 + btnSize, mainCenterY - btnSize / 2, btnSize, btnSize);
            btnDown.Bounds = new Rectangle(leftCenterX - btnSize / 2, mainCenterY - btnSize / 2 + btnSize, btnSize, btnSize);

            int rightCenterX = innerX + innerW * 3 / 4;
            btnX.Bounds = new Rectangle(rightCenterX - btnSize / 2, mainCenterY - btnSize / 2 - btnSize, btnSize, btnSize);
            btnY.Bounds = new Rectangle(rightCenterX - btnSize / 2 - btnSize, mainCenterY - btnSize / 2, btnSize, btnSize);
            btnA.Bounds = new Rectangle(rightCenterX - btnSize / 2 + btnSize, mainCenterY - btnSize / 2, btnSize, btnSize);
            btnB.Bounds = new Rectangle(rightCenterX - btnSize / 2, mainCenterY - btnSize / 2 + btnSize, btnSize, btnSize);

            // フォント調整
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