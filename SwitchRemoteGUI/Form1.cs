using System;
using System.Drawing;
using System.IO.Ports;
using System.Windows.Forms;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.IO;
#nullable disable

namespace SwitchRemoteGUI
{
    public partial class Form1 : Form
    {
        // ★設定必須項目
        string portName = "COM5";

        SerialPort? port;

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
        bool _isSolidBlack = false;
        bool _isVertical = false;

        // ★連射・送信制御用
        System.Windows.Forms.Timer _repeatTimer;
        string _repeatingCmd = "";

        // ★前回の送信時刻（連打防止用）
        DateTime _lastSendTime = DateTime.MinValue;
        // ★最小送信間隔(ミリ秒)。これを下回る連打は無視する（マイコンのバッファ溢れ防止）
        // マイコン側の処理速度に合わせて調整（通常100ms〜150ms）
        private const int MIN_SEND_INTERVAL = 120;

        // UIパーツ
        Button? btnLayoutToggle;
        Button? btnBgToggle;
        Label? lblTitle;

        Button? btnObsShow, btnObsHide;
        Button? btnZL, btnL, btnLR, btnR, btnZR;
        Button? btnUp, btnLeft, btnDown, btnRight;
        Button? btnX, btnY, btnB, btnA;
        Button? btnMinus, btnHome, btnCap, btnPlus;
        Button? btnL3, btnR3;

        private int borderSize = 5;

        private readonly string LayoutFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "switch_layout.txt");


        public Form1()
        {
            InitializeComponent();
            this.TopMost = true;
            this.FormBorderStyle = FormBorderStyle.None;
            this.MinimumSize = new Size(300, 300);

            // ★連射タイマー設定 
            // マイコンの処理落ちを防ぐため、少し長め(150ms)に設定
            _repeatTimer = new System.Windows.Forms.Timer();
            _repeatTimer.Interval = 400;
            _repeatTimer.Tick += RepeatTimer_Tick;

            SetTransparentMode();

            this.Padding = new Padding(borderSize);
            this.KeyPreview = true;
            this.KeyDown += Form1_KeyDown;
            this.Paint += Form1_Paint;

            ConnectPort();
            CreateUI();

            this.Size = new Size(800, 400);

            _isReady = true;
            UpdateLayout();
        }

        // --- 連射処理 ---
        private void RepeatTimer_Tick(object? sender, EventArgs e)
        {
            if (!string.IsNullOrEmpty(_repeatingCmd))
            {
                // 強制送信フラグを立てて送る（長押し中は間隔管理されているのでそのまま送る）
                Send(_repeatingCmd, true);
            }
        }

        // --- コマンド送信処理（改良版） ---
        // force: trueなら間隔チェックを無視して送信（長押し連射用）
        void Send(string cmd, bool force = false)
        {
            if (port != null && port.IsOpen)
            {
                // ★間隔チェック: 前回の送信から時間が経っていないなら、「送らない」
                // これにより、マイコン側にデータが溜まるのを物理的に防ぐ
                double msSinceLast = (DateTime.Now - _lastSendTime).TotalMilliseconds;
                if (!force && msSinceLast < MIN_SEND_INTERVAL)
                {
                    // まだ早いので無視（これが「以前のキューを却下」の代わりになる）
                    return;
                }

                try
                {
                    // 一応PC側のバッファもクリアしておく
                    port.DiscardOutBuffer();

                    port.Write(cmd);

                    // 送信時刻を記録
                    _lastSendTime = DateTime.Now;
                }
                catch { }
            }
        }

        // --- モード切り替え ---
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
                this.Size = new Size(400, 800);
                if (btnLayoutToggle != null) btnLayoutToggle.Text = "🔀 LAYOUT: 縦 (分割)";
            }
            else
            {
                this.Size = new Size(800, 400);
                if (btnLayoutToggle != null) btnLayoutToggle.Text = "🔀 LAYOUT: 横 (全面)";
            }
            UpdateLayout();
            WriteGameLayoutFile();
        }

        void ToggleBlackMode()
        {
            if (_isSolidBlack)
            {
                SetTransparentMode();
                if (btnBgToggle != null) btnBgToggle.Text = "⚫ BG: 透過";
            }
            else
            {
                SetSolidBlackMode();
                if (btnBgToggle != null) btnBgToggle.Text = "⚫ BG: 黒";
            }
            if (btnBgToggle != null)
                btnBgToggle.BackColor = _isSolidBlack ? Color.FromArgb(100, 100, 100) : Color.FromArgb(70, 70, 70);

            this.TopMost = true;
        }

        // --- AutoHotkey連携 ---
        void WriteGameLayoutFile()
        {
            if (_isVertical)
            {
                int W = this.ClientSize.Width;
                int H = this.ClientSize.Height;
                int pad = borderSize;
                int innerW = W - pad * 2;
                int innerX = pad;
                int innerY = pad;
                int topBarH = 24;
                int margin = 5;

                int gameX = this.Location.X + innerX;
                int gameY = this.Location.Y + innerY + topBarH + margin;
                int gameW = innerW;
                int gameH = (H / 2) - (topBarH + margin * 2);

                string layoutData = $"{gameX} {gameY} {gameW} {gameH}";

                try { File.WriteAllText(LayoutFilePath, layoutData); }
                catch (Exception ex) { MessageBox.Show($"Error: {ex.Message}", "Error"); }
            }
            else
            {
                try { File.WriteAllText(LayoutFilePath, ""); } catch { }
            }
        }

        // --- 描画・ウィンドウ処理 ---
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

        // --- UI作成 ---
        void CreateUI()
        {
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

            // OBS操作用 (制限なし)
            Button MkControlBtn(string txt, string cmd, Color? bg = null)
            {
                Button b = new Button();
                b.Text = txt;
                b.BackColor = bg ?? Color.White;
                b.FlatStyle = FlatStyle.Flat;
                b.FlatAppearance.BorderSize = 0;
                b.TabStop = false;
                if (cmd != "") b.Click += (s, e) => Send(cmd, true); // Controlは常に送る
                this.Controls.Add(b);
                return b;
            }

            // ゲーム用 (MouseDownで送信、連打制限あり)
            Button MkGameBtn(string txt, string cmd, bool isRepeat, Color? bg = null)
            {
                Button b = new Button();
                b.Text = txt;
                b.BackColor = bg ?? Color.White;
                b.FlatStyle = FlatStyle.Flat;
                b.FlatAppearance.BorderSize = 0;
                b.TabStop = false;

                b.MouseDown += (s, e) => {
                    if (isRepeat)
                    {
                        // 連射モード: 即時送信＆タイマー開始
                        _repeatingCmd = cmd;
                        Send(cmd, true); // 初回は強制送信
                        _repeatTimer.Start();
                    }
                    else
                    {
                        // 単発モード: 制限付きで送信（連打しても間引かれる）
                        Send(cmd, false);
                    }
                };

                b.MouseUp += (s, e) => {
                    if (isRepeat)
                    {
                        _repeatTimer.Stop();
                        _repeatingCmd = "";
                    }
                    // 離したときは何も送らない（送信を止めるだけでいい）
                    // マイコンへの送信が止まれば、マイコンのバッファが尽き次第止まる
                    // 今回は「送信制限」でバッファを枯渇させているので、これでOK
                };

                this.Controls.Add(b);
                return b;
            }

            // OBS
            btnObsShow = MkControlBtn("📺 戻す", "", Color.LightSkyBlue);
            btnObsShow.Click += (s, e) => ControlApp("obs", true);
            btnObsHide = MkControlBtn("＿ 隠す", "", Color.LightGray);
            btnObsHide.Click += (s, e) => ControlApp("obs", false);

            // ゲームボタン
            btnZL = MkGameBtn("ZL", "e", false, Color.DarkGray);
            btnL = MkGameBtn("L", "q", false, Color.Gray);
            btnLR = MkGameBtn("LR", "qw", false, Color.Orange);
            btnR = MkGameBtn("R", "w", false, Color.Gray);
            btnZR = MkGameBtn("ZR", "r", false, Color.DarkGray);

            btnUp = MkGameBtn("↑", "I", true);
            btnLeft = MkGameBtn("←", "J", true);
            btnDown = MkGameBtn("↓", "K", true);
            btnRight = MkGameBtn("→", "L", true);

            btnX = MkGameBtn("X", "s", false, Color.Yellow);
            btnY = MkGameBtn("Y", "a", false, Color.LightGreen);
            btnB = MkGameBtn("B", "x", false, Color.Red);
            btnA = MkGameBtn("A", "z", false, Color.Cyan);

            btnMinus = MkGameBtn("-", "m", false, Color.LightGray);
            btnHome = MkGameBtn("🏠", "h", false, Color.LightBlue);
            btnCap = MkGameBtn("📷", "c", false, Color.Pink);
            btnPlus = MkGameBtn("+", "n", false, Color.LightGray);

            btnL3 = MkGameBtn("L3", "3", false, Color.Silver);
            btnR3 = MkGameBtn("R3", "4", false, Color.Silver);
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

            // --- トップバー ---
            int toggleW = (innerW - margin) / 4;
            if (btnBgToggle != null) btnBgToggle.Bounds = new Rectangle(innerX + innerW - toggleW, innerY, toggleW, topBarH);
            if (btnLayoutToggle != null) btnLayoutToggle.Bounds = new Rectangle(innerX + innerW - toggleW * 2 - margin, innerY, toggleW, topBarH);
            if (lblTitle != null) lblTitle.Bounds = new Rectangle(innerX, innerY, innerW - toggleW * 2 - margin * 2, topBarH);

            int contentTop;
            if (_isVertical) contentTop = innerY + (H / 2);
            else contentTop = innerY + topBarH + margin;

            int availH = H - pad - contentTop;

            // 1. OBS
            int yObs = contentTop;
            if (btnObsShow != null) btnObsShow.Bounds = new Rectangle(innerX, yObs, (innerW - margin) / 2, stdH);
            if (btnObsHide != null) btnObsHide.Bounds = new Rectangle(innerX + (innerW - margin) / 2 + margin, yObs, (innerW - margin) / 2, stdH);

            // 2. ショルダー
            int ySh = yObs + stdH + margin;
            int shW = (innerW - margin * 4) / 5;
            if (btnZL != null) btnZL.Bounds = new Rectangle(innerX, ySh, shW, stdH);
            if (btnL != null) btnL.Bounds = new Rectangle(innerX + shW + margin, ySh, shW, stdH);
            if (btnLR != null) btnLR.Bounds = new Rectangle(innerX + (shW + margin) * 2, ySh, shW, stdH);
            if (btnR != null) btnR.Bounds = new Rectangle(innerX + (shW + margin) * 3, ySh, shW, stdH);
            if (btnZR != null) btnZR.Bounds = new Rectangle(innerX + (shW + margin) * 4, ySh, shW, stdH);

            // 5. L3/R3
            int yStick = H - pad - stdH;
            int stickW = (innerW - margin) / 2;
            int stickH = stdH;

            if (yStick < ySh + stdH + margin * 3)
            {
                stickH = Math.Max(20, (H - pad - ySh) / 3);
                yStick = H - pad - stickH;
            }

            if (btnL3 != null) btnL3.Bounds = new Rectangle(innerX, yStick, stickW, stickH);
            if (btnR3 != null) btnR3.Bounds = new Rectangle(innerX + stickW + margin, yStick, stickW, stickH);

            // 4. システム
            int ySys = yStick - margin - stdH;
            int sysW = (innerW - margin * 3) / 4;
            if (btnMinus != null) btnMinus.Bounds = new Rectangle(innerX, ySys, sysW, stdH);
            if (btnHome != null) btnHome.Bounds = new Rectangle(innerX + sysW + margin, ySys, sysW, stdH);
            if (btnCap != null) btnCap.Bounds = new Rectangle(innerX + (sysW + margin) * 2, ySys, sysW, stdH);
            if (btnPlus != null) btnPlus.Bounds = new Rectangle(innerX + (sysW + margin) * 3, ySys, sysW, stdH);

            // 3. 中央
            int yMainTop = ySh + stdH + margin;
            int yMainBottom = ySys - margin;
            int mainH = yMainBottom - yMainTop;
            if (mainH < 50) mainH = 50;

            int btnSize = Math.Min((innerW / 2 - margin) / 3, mainH / 3);
            btnSize = Math.Max(20, btnSize);

            int leftCenterX = innerX + innerW / 4;
            int mainCenterY = yMainTop + mainH / 2;
            if (btnUp != null) btnUp.Bounds = new Rectangle(leftCenterX - btnSize / 2, mainCenterY - btnSize / 2 - btnSize, btnSize, btnSize);
            if (btnLeft != null) btnLeft.Bounds = new Rectangle(leftCenterX - btnSize / 2 - btnSize, mainCenterY - btnSize / 2, btnSize, btnSize);
            if (btnRight != null) btnRight.Bounds = new Rectangle(leftCenterX - btnSize / 2 + btnSize, mainCenterY - btnSize / 2, btnSize, btnSize);
            if (btnDown != null) btnDown.Bounds = new Rectangle(leftCenterX - btnSize / 2, mainCenterY - btnSize / 2 + btnSize, btnSize, btnSize);

            int rightCenterX = innerX + innerW * 3 / 4;
            if (btnX != null) btnX.Bounds = new Rectangle(rightCenterX - btnSize / 2, mainCenterY - btnSize / 2 - btnSize, btnSize, btnSize);
            if (btnY != null) btnY.Bounds = new Rectangle(rightCenterX - btnSize / 2 - btnSize, mainCenterY - btnSize / 2, btnSize, btnSize);
            if (btnA != null) btnA.Bounds = new Rectangle(rightCenterX - btnSize / 2 + btnSize, mainCenterY - btnSize / 2, btnSize, btnSize);
            if (btnB != null) btnB.Bounds = new Rectangle(rightCenterX - btnSize / 2, mainCenterY - btnSize / 2 + btnSize, btnSize, btnSize);

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
            this.TopMost = true;
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

        void ConnectPort()
        {
            try { port = new SerialPort(portName, 9600); port.Open(); } catch { }
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
    }
}