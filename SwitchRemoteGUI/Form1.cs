using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO.Ports;
using System.Windows.Forms;
using System.Diagnostics;
using System.Runtime.InteropServices;
#nullable disable

namespace SwitchRemoteGUI
{
    public partial class Form1 : Form
    {
        // ★設定必須項目
        string portName = "COM5";

        SerialPort? port;

        // --- Windows API ---
        [DllImport("user32.dll")] public static extern int SendMessage(IntPtr hWnd, int Msg, int wParam, int lParam);
        [DllImport("user32.dll")] public static extern bool ReleaseCapture();
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern bool ShowWindowAsync(IntPtr hWnd, int nCmdShow);
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hWnd);

        private const int SW_RESTORE = 9;
        private const int SW_MINIMIZE = 6;

        // 状態
        bool _isReady = false;
        bool _isSolidBlack = false;
        int _rotationAngle = 0;

        // ★追加: ホールド(連射)モード管理フラグ (初期値: ON = 長押し可能)
        bool _isHoldMode = true;

        // 連射・送信制御
        System.Windows.Forms.Timer _repeatTimer;
        string _repeatingCmd = "";
        DateTime _lastSendTime = DateTime.MinValue;
        private const int SEND_INTERVAL = 400;

        // UIパーツ
        RotatableButton? btnLayoutToggle;
        RotatableButton? btnBgToggle;
        RotatableLabel? lblTitle;

        // ★追加: ホールド切替ボタン
        RotatableButton? btnHoldToggle;

        RotatableButton? btnObsShow, btnObsHide;
        RotatableButton? btnZL, btnL, btnLR, btnR, btnZR;
        RotatableButton? btnUp, btnLeft, btnDown, btnRight;
        RotatableButton? btnX, btnY, btnB, btnA;
        RotatableButton? btnMinus, btnHome, btnCap, btnPlus;
        RotatableButton? btnL3, btnR3;

        private int resizeGrip = 10;
        private int borderSize = 2;
        private const int BASE_W = 800;
        private const int BASE_H = 400;

        public Form1()
        {
            InitializeComponent();
            this.TopMost = true;
            this.FormBorderStyle = FormBorderStyle.None;
            this.DoubleBuffered = true;
            this.MinimumSize = new Size(200, 100);

            _repeatTimer = new System.Windows.Forms.Timer();
            _repeatTimer.Interval = SEND_INTERVAL;
            _repeatTimer.Tick += RepeatTimer_Tick;

            SetTransparentMode();

            this.Padding = new Padding(borderSize);
            this.KeyPreview = true;
            this.KeyDown += Form1_KeyDown;
            this.Paint += Form1_Paint;

            ConnectPort();
            CreateUI();

            this.Size = new Size(BASE_W, BASE_H);

            _isReady = true;
            UpdateLayout();
        }

        private void RepeatTimer_Tick(object? sender, EventArgs e)
        {
            if (!string.IsNullOrEmpty(_repeatingCmd)) Send(_repeatingCmd, true);
        }

        void Send(string cmd, bool force = false)
        {
            if (port != null && port.IsOpen)
            {
                // コマンド回転ロジック
                string finalCmd = RotateCommand(cmd);

                double msSinceLast = (DateTime.Now - _lastSendTime).TotalMilliseconds;
                if (!force && msSinceLast < SEND_INTERVAL) return;

                try
                {
                    port.DiscardOutBuffer();
                    port.Write(finalCmd);
                    _lastSendTime = DateTime.Now;
                }
                catch { }
            }
        }

        // コマンド回転
        string RotateCommand(string cmd)
        {
            if (_rotationAngle == 0) return cmd;
            if (cmd == "I") { if (_rotationAngle == 90) return "J"; if (_rotationAngle == 180) return "K"; if (_rotationAngle == 270) return "L"; }
            if (cmd == "J") { if (_rotationAngle == 90) return "K"; if (_rotationAngle == 180) return "L"; if (_rotationAngle == 270) return "I"; }
            if (cmd == "K") { if (_rotationAngle == 90) return "L"; if (_rotationAngle == 180) return "I"; if (_rotationAngle == 270) return "J"; }
            if (cmd == "L") { if (_rotationAngle == 90) return "I"; if (_rotationAngle == 180) return "J"; if (_rotationAngle == 270) return "K"; }
            return cmd;
        }

        void ClearBuffer()
        {
            if (port != null && port.IsOpen) try { port.DiscardOutBuffer(); } catch { }
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

        void RotateLayout()
        {
            _rotationAngle = (_rotationAngle + 90) % 360;
            int currentW = this.Width;
            int currentH = this.Height;
            this.Size = new Size(currentH, currentW);

            if (btnLayoutToggle != null) btnLayoutToggle.Text = $"↻ {_rotationAngle}°";
            UpdateLayout();
        }

        void ToggleBlackMode()
        {
            if (_isSolidBlack) { SetTransparentMode(); if (btnBgToggle != null) btnBgToggle.Text = "透過"; }
            else { SetSolidBlackMode(); if (btnBgToggle != null) btnBgToggle.Text = "黒"; }
            if (btnBgToggle != null) btnBgToggle.BackColor = _isSolidBlack ? Color.FromArgb(100, 100, 100) : Color.FromArgb(70, 70, 70);
            this.TopMost = true;
        }

        // ★追加: ホールドモード切替処理
        void ToggleHoldMode()
        {
            _isHoldMode = !_isHoldMode;
            if (btnHoldToggle != null)
            {
                // 表示切り替え
                if (_isHoldMode)
                {
                    btnHoldToggle.Text = "Hold: ON";
                    btnHoldToggle.BackColor = Color.LightGreen; // わかりやすく緑に
                }
                else
                {
                    btnHoldToggle.Text = "Hold: OFF";
                    btnHoldToggle.BackColor = Color.LightSalmon; // OFFは赤っぽく
                }
            }
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
                if (pos.X <= resizeGrip && pos.Y <= resizeGrip) m.Result = (IntPtr)13;
                else if (pos.X >= this.ClientSize.Width - resizeGrip && pos.Y <= resizeGrip) m.Result = (IntPtr)14;
                else if (pos.X <= resizeGrip && pos.Y >= this.ClientSize.Height - resizeGrip) m.Result = (IntPtr)16;
                else if (pos.X >= this.ClientSize.Width - resizeGrip && pos.Y >= this.ClientSize.Height - resizeGrip) m.Result = (IntPtr)17;
                else if (pos.X <= resizeGrip) m.Result = (IntPtr)10;
                else if (pos.X >= this.ClientSize.Width - resizeGrip) m.Result = (IntPtr)11;
                else if (pos.Y <= resizeGrip) m.Result = (IntPtr)12;
                else if (pos.Y >= this.ClientSize.Height - resizeGrip) m.Result = (IntPtr)15;
            }
        }

        private void TopBar_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left) { ReleaseCapture(); SendMessage(Handle, 0xA1, 0x2, 0); }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            this.Invalidate();
            UpdateLayout();
        }

        void CreateUI()
        {
            lblTitle = new RotatableLabel();
            lblTitle.Text = ":: Switch Remote ::";
            lblTitle.TextAlign = ContentAlignment.MiddleCenter;
            lblTitle.BackColor = Color.FromArgb(50, 50, 50);
            lblTitle.ForeColor = Color.White;
            lblTitle.Height = 24;
            lblTitle.MouseDown += TopBar_MouseDown;
            this.Controls.Add(lblTitle);

            btnLayoutToggle = new RotatableButton();
            btnLayoutToggle.Text = "↻ 0°";
            btnLayoutToggle.BackColor = Color.FromArgb(70, 70, 70);
            btnLayoutToggle.ForeColor = Color.White;
            btnLayoutToggle.FlatStyle = FlatStyle.Flat;
            btnLayoutToggle.FlatAppearance.BorderSize = 0;
            btnLayoutToggle.TabStop = false;
            btnLayoutToggle.Click += (s, e) => RotateLayout();
            this.Controls.Add(btnLayoutToggle);

            btnBgToggle = new RotatableButton();
            btnBgToggle.Text = "⚫ 透過";
            btnBgToggle.BackColor = Color.FromArgb(70, 70, 70);
            btnBgToggle.ForeColor = Color.White;
            btnBgToggle.FlatStyle = FlatStyle.Flat;
            btnBgToggle.FlatAppearance.BorderSize = 0;
            btnBgToggle.TabStop = false;
            btnBgToggle.Click += (s, e) => ToggleBlackMode();
            this.Controls.Add(btnBgToggle);

            RotatableButton MkControlBtn(string txt, string keyword, bool show, Color? bg = null)
            {
                RotatableButton b = new RotatableButton();
                b.Text = txt;
                b.BackColor = bg ?? Color.White;
                b.FlatStyle = FlatStyle.Flat;
                b.FlatAppearance.BorderSize = 0;
                b.TabStop = false;
                b.Click += (s, e) => ControlApp(keyword, show);
                this.Controls.Add(b);
                return b;
            }

            // ゲームボタン生成（ホールドモード対応）
            RotatableButton MkGameBtn(string txt, string cmd, bool isRepeat, Color? bg = null)
            {
                RotatableButton b = new RotatableButton();
                b.Text = txt;
                b.BackColor = bg ?? Color.White;
                b.FlatStyle = FlatStyle.Flat;
                b.FlatAppearance.BorderSize = 0;
                b.TabStop = false;

                b.MouseDown += (s, e) => {
                    // ★修正: _isHoldModeがONなら全ボタン連射(ホールド)有効。OFFなら全ボタン単発。
                    // 元の isRepeat 引数はここでは無視して、モード設定を優先します。
                    bool doRepeat = _isHoldMode;

                    if (doRepeat)
                    {
                        _repeatingCmd = cmd;
                        Send(cmd, true);
                        _repeatTimer.Start();
                    }
                    else
                    {
                        Send(cmd, false);
                    }
                };

                b.MouseUp += (s, e) => {
                    // 連射モードならタイマーを止める
                    if (_isHoldMode)
                    {
                        _repeatTimer.Stop();
                        _repeatingCmd = "";
                    }
                    ClearBuffer();
                };
                this.Controls.Add(b);
                return b;
            }

            // ★追加: Hold切替ボタン
            btnHoldToggle = new RotatableButton();
            btnHoldToggle.Text = "Hold: ON"; // 初期状態
            btnHoldToggle.BackColor = Color.LightGreen;
            btnHoldToggle.ForeColor = Color.Black;
            btnHoldToggle.FlatStyle = FlatStyle.Flat;
            btnHoldToggle.FlatAppearance.BorderSize = 0;
            btnHoldToggle.TabStop = false;
            btnHoldToggle.Click += (s, e) => ToggleHoldMode();
            this.Controls.Add(btnHoldToggle);

            // OBSボタン (既存)
            btnObsShow = MkControlBtn("📺 OBS", "obs", true, Color.LightSkyBlue);
            btnObsHide = MkControlBtn("Hide", "obs", false, Color.LightGray);

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
            btnHome = MkGameBtn("Home", "h", false, Color.LightBlue);
            btnCap = MkGameBtn("Capture", "c", false, Color.Pink);
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
            int topBarH = 24;
            int margin = 5;

            int contentY = innerY + topBarH + margin;
            int contentH = H - pad - contentY;

            var rects = new System.Collections.Generic.Dictionary<Control, Rectangle>();

            int toggleW = (innerW - margin) / 4;
            if (btnBgToggle != null) rects[btnBgToggle] = new Rectangle(innerX + innerW - toggleW, innerY, toggleW, topBarH);
            if (btnLayoutToggle != null) rects[btnLayoutToggle] = new Rectangle(innerX + innerW - toggleW * 2 - margin, innerY, toggleW, topBarH);
            if (lblTitle != null) rects[lblTitle] = new Rectangle(innerX, innerY, innerW - toggleW * 2 - margin * 2, topBarH);

            int btnBaseSize = 30;

            // ★★★ 縦レイアウト (90度 / 270度) ★★★
            if (_rotationAngle == 90 || _rotationAngle == 270)
            {
                int rowH = contentH / 14;
                int y = contentY;

                // 1. OBS & Hold (3ボタンに分割)
                int obsH = Math.Max(30, rowH);
                int btnW = (innerW - margin * 2) / 3;
                if (btnObsShow != null) rects[btnObsShow] = new Rectangle(innerX, y, btnW, obsH);
                if (btnObsHide != null) rects[btnObsHide] = new Rectangle(innerX + btnW + margin, y, btnW, obsH);
                if (btnHoldToggle != null) rects[btnHoldToggle] = new Rectangle(innerX + (btnW + margin) * 2, y, btnW, obsH);
                y += obsH + margin;

                // 2. ショルダー
                int shH = Math.Max(40, rowH * 2);
                int shW = (innerW - margin * 4) / 5;
                if (btnZL != null) rects[btnZL] = new Rectangle(innerX, y, shW, shH);
                if (btnL != null) rects[btnL] = new Rectangle(innerX + shW + margin, y, shW, shH);
                if (btnLR != null) rects[btnLR] = new Rectangle(innerX + (shW + margin) * 2, y, shW, shH);
                if (btnR != null) rects[btnR] = new Rectangle(innerX + (shW + margin) * 3, y, shW, shH);
                if (btnZR != null) rects[btnZR] = new Rectangle(innerX + (shW + margin) * 4, y, shW, shH);
                y += shH + margin;

                // 3. L3/R3
                int stickH = Math.Max(30, rowH);
                int stickW = (innerW - margin) / 2;
                if (btnL3 != null) rects[btnL3] = new Rectangle(innerX, y, stickW, stickH);
                if (btnR3 != null) rects[btnR3] = new Rectangle(innerX + stickW + margin, y, stickW, stickH);
                y += stickH + margin;

                // 4. 十字キー
                int dpadH = Math.Max(100, rowH * 3);
                btnBaseSize = Math.Min(innerW / 3, dpadH / 3);
                int centerX = innerX + innerW / 2;
                int centerY = y + dpadH / 2;
                if (btnUp != null) rects[btnUp] = new Rectangle(centerX - btnBaseSize / 2, centerY - btnBaseSize / 2 - btnBaseSize, btnBaseSize, btnBaseSize);
                if (btnLeft != null) rects[btnLeft] = new Rectangle(centerX - btnBaseSize / 2 - btnBaseSize, centerY - btnBaseSize / 2, btnBaseSize, btnBaseSize);
                if (btnRight != null) rects[btnRight] = new Rectangle(centerX - btnBaseSize / 2 + btnBaseSize, centerY - btnBaseSize / 2, btnBaseSize, btnBaseSize);
                if (btnDown != null) rects[btnDown] = new Rectangle(centerX - btnBaseSize / 2, centerY - btnBaseSize / 2 + btnBaseSize, btnBaseSize, btnBaseSize);
                y += dpadH + margin;

                // 5. システム
                int sysH = Math.Max(30, rowH);
                int sysW = (innerW - margin * 3) / 4;
                if (btnMinus != null) rects[btnMinus] = new Rectangle(innerX, y, sysW, sysH);
                if (btnHome != null) rects[btnHome] = new Rectangle(innerX + sysW + margin, y, sysW, sysH);
                if (btnCap != null) rects[btnCap] = new Rectangle(innerX + (sysW + margin) * 2, y, sysW, sysH);
                if (btnPlus != null) rects[btnPlus] = new Rectangle(innerX + (sysW + margin) * 3, y, sysW, sysH);
                y += sysH + margin;

                // 6. ABXY
                int abxyH = dpadH;
                centerY = y + abxyH / 2;
                if (btnX != null) rects[btnX] = new Rectangle(centerX - btnBaseSize / 2, centerY - btnBaseSize / 2 - btnBaseSize, btnBaseSize, btnBaseSize);
                if (btnY != null) rects[btnY] = new Rectangle(centerX - btnBaseSize / 2 - btnBaseSize, centerY - btnBaseSize / 2, btnBaseSize, btnBaseSize);
                if (btnA != null) rects[btnA] = new Rectangle(centerX - btnBaseSize / 2 + btnBaseSize, centerY - btnBaseSize / 2, btnBaseSize, btnBaseSize);
                if (btnB != null) rects[btnB] = new Rectangle(centerX - btnBaseSize / 2, centerY - btnBaseSize / 2 + btnBaseSize, btnBaseSize, btnBaseSize);
            }
            // ★★★ 横レイアウト (0度 / 180度) ★★★
            else
            {
                int obsH = (int)(contentH * 0.10);
                int shoulderH = (int)(contentH * 0.12);
                int stickH = (int)(contentH * 0.12);
                int systemH = (int)(contentH * 0.10);

                if (obsH < 25) obsH = 25;
                if (shoulderH < 30) shoulderH = 30;
                if (stickH < 30) stickH = 30;
                if (systemH < 30) systemH = 30;

                int mainH = contentH - obsH - shoulderH - stickH - systemH - (margin * 4);
                if (mainH < 80) mainH = 80;

                int yObs = contentY;
                int ySh = yObs + obsH + margin;
                int yMain = ySh + shoulderH + margin;
                int ySys = yMain + mainH + margin;
                int yStick = ySys + systemH + margin;

                // OBS & Hold (3ボタン)
                int btnW = (innerW - margin * 2) / 3;
                if (btnObsShow != null) rects[btnObsShow] = new Rectangle(innerX, yObs, btnW, obsH);
                if (btnObsHide != null) rects[btnObsHide] = new Rectangle(innerX + btnW + margin, yObs, btnW, obsH);
                if (btnHoldToggle != null) rects[btnHoldToggle] = new Rectangle(innerX + (btnW + margin) * 2, yObs, btnW, obsH);

                // ショルダー
                int shW = (innerW - margin * 4) / 5;
                if (btnZL != null) rects[btnZL] = new Rectangle(innerX, ySh, shW, shoulderH);
                if (btnL != null) rects[btnL] = new Rectangle(innerX + shW + margin, ySh, shW, shoulderH);
                if (btnLR != null) rects[btnLR] = new Rectangle(innerX + (shW + margin) * 2, ySh, shW, shoulderH);
                if (btnR != null) rects[btnR] = new Rectangle(innerX + (shW + margin) * 3, ySh, shW, shoulderH);
                if (btnZR != null) rects[btnZR] = new Rectangle(innerX + (shW + margin) * 4, ySh, shW, shoulderH);

                // メイン
                int leftAreaW = innerW / 2;
                int rightAreaW = innerW - leftAreaW;
                btnBaseSize = Math.Min(mainH / 3, (leftAreaW - margin * 2) / 3);

                int leftCenterX = innerX + leftAreaW / 2;
                int mainCenterY = yMain + mainH / 2;
                if (btnUp != null) rects[btnUp] = new Rectangle(leftCenterX - btnBaseSize / 2, mainCenterY - btnBaseSize / 2 - btnBaseSize, btnBaseSize, btnBaseSize);
                if (btnLeft != null) rects[btnLeft] = new Rectangle(leftCenterX - btnBaseSize / 2 - btnBaseSize, mainCenterY - btnBaseSize / 2, btnBaseSize, btnBaseSize);
                if (btnRight != null) rects[btnRight] = new Rectangle(leftCenterX - btnBaseSize / 2 + btnBaseSize, mainCenterY - btnBaseSize / 2, btnBaseSize, btnBaseSize);
                if (btnDown != null) rects[btnDown] = new Rectangle(leftCenterX - btnBaseSize / 2, mainCenterY - btnBaseSize / 2 + btnBaseSize, btnBaseSize, btnBaseSize);

                int rightCenterX = innerX + leftAreaW + rightAreaW / 2;
                if (btnX != null) rects[btnX] = new Rectangle(rightCenterX - btnBaseSize / 2, mainCenterY - btnBaseSize / 2 - btnBaseSize, btnBaseSize, btnBaseSize);
                if (btnY != null) rects[btnY] = new Rectangle(rightCenterX - btnBaseSize / 2 - btnBaseSize, mainCenterY - btnBaseSize / 2, btnBaseSize, btnBaseSize);
                if (btnA != null) rects[btnA] = new Rectangle(rightCenterX - btnBaseSize / 2 + btnBaseSize, mainCenterY - btnBaseSize / 2, btnBaseSize, btnBaseSize);
                if (btnB != null) rects[btnB] = new Rectangle(rightCenterX - btnBaseSize / 2, mainCenterY - btnBaseSize / 2 + btnBaseSize, btnBaseSize, btnBaseSize);

                int sysW = (innerW - margin * 3) / 4;
                if (btnMinus != null) rects[btnMinus] = new Rectangle(innerX, ySys, sysW, systemH);
                if (btnHome != null) rects[btnHome] = new Rectangle(innerX + sysW + margin, ySys, sysW, systemH);
                if (btnCap != null) rects[btnCap] = new Rectangle(innerX + (sysW + margin) * 2, ySys, sysW, systemH);
                if (btnPlus != null) rects[btnPlus] = new Rectangle(innerX + (sysW + margin) * 3, ySys, sysW, systemH);

                int stickW = (innerW - margin) / 2;
                if (btnL3 != null) rects[btnL3] = new Rectangle(innerX, yStick, stickW, stickH);
                if (btnR3 != null) rects[btnR3] = new Rectangle(innerX + stickW + margin, yStick, stickW, stickH);
            }

            float fontSize = Math.Max(8, btnBaseSize / 2.5f);
            Font fontMain = new Font("Arial", fontSize, FontStyle.Bold);
            Font fontSub = new Font("Arial", Math.Max(8, fontSize * 0.8f), FontStyle.Bold);

            foreach (var kvp in rects)
            {
                Control c = kvp.Key;
                Rectangle r = kvp.Value;

                if (c is RotatableButton rb) rb.RotationAngle = _rotationAngle;
                if (c is RotatableLabel rl) rl.RotationAngle = _rotationAngle;

                // 180度, 270度 は反転
                if (_rotationAngle == 180 || _rotationAngle == 270)
                {
                    r = new Rectangle(W - r.X - r.Width, H - r.Y - r.Height, r.Width, r.Height);
                }
                c.Bounds = r;

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
            switch (e.KeyCode) { case Keys.Z: Send("z"); break; case Keys.X: Send("x"); break; }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            if (port != null && port.IsOpen) port.Close();
            base.OnFormClosed(e);
        }

        void ConnectPort() { try { port = new SerialPort(portName, 9600); port.Open(); } catch { } }
    }

    // ボタンクラス（変更なし）
    public class RotatableButton : Button
    {
        public int RotationAngle { get; set; } = 0;
        public RotatableButton()
        {
            this.SetStyle(ControlStyles.SupportsTransparentBackColor, true);
            this.SetStyle(ControlStyles.Opaque, true);
            this.SetStyle(ControlStyles.ResizeRedraw, true);
            this.BackColor = Color.Transparent;
        }
        protected override CreateParams CreateParams { get { CreateParams cp = base.CreateParams; cp.ExStyle |= 0x20; return cp; } }
        protected override void OnResize(EventArgs e) { base.OnResize(e); UpdateShape(); }
        private void UpdateShape()
        {
            int w = this.Width; int h = this.Height; if (w < 1 || h < 1) return;
            int d = Math.Min(w, h); GraphicsPath path = new GraphicsPath();
            if (Math.Abs(w - h) < 5) path.AddEllipse(0, 0, d, d);
            else { int r = d; path.AddArc(0, 0, r, r, 90, 180); path.AddArc(w - r, 0, r, r, 270, 180); path.CloseFigure(); }
            this.Region = new Region(path);
        }
        protected override void OnPaint(PaintEventArgs pevent)
        {
            Graphics g = pevent.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
            if (this.Parent != null) { using (Brush parentBrush = new SolidBrush(this.Parent.BackColor)) { g.FillRectangle(parentBrush, this.ClientRectangle); } }
            Color c = this.BackColor.A == 0 ? Color.White : this.BackColor;
            using (Brush brush = new SolidBrush(c)) { g.FillRegion(brush, this.Region); }
            if (!string.IsNullOrEmpty(this.Text))
            {
                StringFormat sf = new StringFormat(); sf.Alignment = StringAlignment.Center; sf.LineAlignment = StringAlignment.Center;
                using (Brush textBrush = new SolidBrush(this.ForeColor))
                {
                    if (RotationAngle != 0) { g.TranslateTransform(this.Width / 2, this.Height / 2); g.RotateTransform(RotationAngle); g.DrawString(this.Text, this.Font, textBrush, 0, 0, sf); g.ResetTransform(); }
                    else { g.DrawString(this.Text, this.Font, textBrush, this.ClientRectangle, sf); }
                }
            }
        }
    }

    public class RotatableLabel : Label
    {
        public int RotationAngle { get; set; } = 0;
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics; g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
            using (Brush bgBrush = new SolidBrush(this.BackColor)) { g.FillRectangle(bgBrush, this.ClientRectangle); }
            if (!string.IsNullOrEmpty(this.Text))
            {
                StringFormat sf = new StringFormat(); sf.Alignment = StringAlignment.Center; sf.LineAlignment = StringAlignment.Center;
                using (Brush textBrush = new SolidBrush(this.ForeColor))
                {
                    if (RotationAngle != 0) { g.TranslateTransform(this.Width / 2, this.Height / 2); g.RotateTransform(RotationAngle); g.DrawString(this.Text, this.Font, textBrush, 0, 0, sf); g.ResetTransform(); }
                    else { g.DrawString(this.Text, this.Font, textBrush, this.ClientRectangle, sf); }
                }
            }
        }
    }
}