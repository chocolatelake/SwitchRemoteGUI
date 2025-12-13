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
        string portName = "COM5";
        SerialPort? port;

        [DllImport("user32.dll")] public static extern int SendMessage(IntPtr hWnd, int Msg, int wParam, int lParam);
        [DllImport("user32.dll")] public static extern bool ReleaseCapture();
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern bool ShowWindowAsync(IntPtr hWnd, int nCmdShow);
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hWnd);

        private const int SW_RESTORE = 9;
        private const int SW_MINIMIZE = 6;

        bool _isReady = false;
        bool _isSolidBlack = false;
        int _rotationAngle = 0;
        bool _isHoldMode = true;
        bool _isMenuOpen = false;

        // 透明度初期値 (30%)
        double _targetOpacity = 0.3;

        System.Windows.Forms.Timer _repeatTimer;
        string _repeatingCmd = "";
        DateTime _lastSendTime = DateTime.MinValue;
        private const int SEND_INTERVAL = 400;

        RotatableButton? btnLayoutToggle;
        RotatableButton? btnBgToggle;
        RotatableLabel? lblTitle;
        RotatableButton? btnMenu;
        RotatableButton? btnHoldToggle;

        RotatableButton? btnZL, btnL, btnR, btnZR;
        RotatableButton? btnUp, btnLeft, btnDown, btnRight;
        RotatableButton? btnX, btnY, btnB, btnA;
        RotatableButton? btnMinus, btnHome, btnCap, btnPlus;
        RotatableButton? btnL3, btnR3;

        Panel? pnlMenu;
        RotatableButton? mBtnLR;
        RotatableButton? mBtnObs, mBtnHide;
        RotatableButton? mBtnRot, mBtnTrans;
        RotatableButton? mBtnFull, mBtnClose;
        RotatableButton? mBtnOp;

        private int resizeGrip = 10;

        // ★修正: パディングやボーダーをなくす
        private int borderSize = 0;
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

            // ★修正: パディングを0にする (紫の帯対策)
            this.Padding = new Padding(0);

            // 初期設定 (透過モード)
            SetTransparentMode();

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
                string finalCmd = RotateCommand(cmd);
                double msSinceLast = (DateTime.Now - _lastSendTime).TotalMilliseconds;
                if (!force && msSinceLast < SEND_INTERVAL) return;
                try { port.DiscardOutBuffer(); port.Write(finalCmd); _lastSendTime = DateTime.Now; } catch { }
            }
        }

        string RotateCommand(string cmd)
        {
            if (_rotationAngle == 0) return cmd;
            if (cmd == "I") { if (_rotationAngle == 90) return "J"; if (_rotationAngle == 180) return "K"; if (_rotationAngle == 270) return "L"; }
            if (cmd == "J") { if (_rotationAngle == 90) return "K"; if (_rotationAngle == 180) return "L"; if (_rotationAngle == 270) return "I"; }
            if (cmd == "K") { if (_rotationAngle == 90) return "L"; if (_rotationAngle == 180) return "I"; if (_rotationAngle == 270) return "J"; }
            if (cmd == "L") { if (_rotationAngle == 90) return "I"; if (_rotationAngle == 180) return "J"; if (_rotationAngle == 270) return "K"; }
            return cmd;
        }

        void ClearBuffer() { if (port != null && port.IsOpen) try { port.DiscardOutBuffer(); } catch { } }

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

        void ToggleMenu()
        {
            _isMenuOpen = !_isMenuOpen;
            if (pnlMenu != null)
            {
                pnlMenu.Visible = _isMenuOpen;
                if (_isMenuOpen) { pnlMenu.BringToFront(); }
                UpdateLayout();
            }
        }

        void RotateLayout()
        {
            _rotationAngle = (_rotationAngle + 90) % 360;
            if (this.WindowState == FormWindowState.Normal)
            {
                int currentW = this.Width;
                int currentH = this.Height;
                this.Size = new Size(currentH, currentW);
            }
            if (mBtnRot != null) mBtnRot.Text = $"Rot: {_rotationAngle}°";
            UpdateLayout();
        }

        void ToggleBlackMode()
        {
            if (_isSolidBlack) { SetTransparentMode(); if (mBtnTrans != null) mBtnTrans.Text = "Trans"; }
            else { SetSolidBlackMode(); if (mBtnTrans != null) mBtnTrans.Text = "Black"; }
            if (mBtnTrans != null) mBtnTrans.BackColor = _isSolidBlack ? Color.Gray : Color.LightGray;
            this.TopMost = true;
        }

        void ToggleHoldMode()
        {
            _isHoldMode = !_isHoldMode;
            if (btnHoldToggle != null)
            {
                btnHoldToggle.Text = _isHoldMode ? "Hold: ON" : "Hold: OFF";
                btnHoldToggle.BackColor = _isHoldMode ? Color.LightGreen : Color.LightSalmon;
            }
        }

        void ToggleMaximize()
        {
            if (this.WindowState == FormWindowState.Maximized)
                this.WindowState = FormWindowState.Normal;
            else
                this.WindowState = FormWindowState.Maximized;

            _isMenuOpen = false;
            if (pnlMenu != null) pnlMenu.Visible = false;
            UpdateLayout();
        }

        void ToggleOpacity()
        {
            if (_targetOpacity < 0.2) _targetOpacity = 0.3;
            else if (_targetOpacity < 0.4) _targetOpacity = 0.5;
            else if (_targetOpacity < 0.7) _targetOpacity = 0.8;
            else if (_targetOpacity < 0.9) _targetOpacity = 1.0;
            else _targetOpacity = 0.1;

            if (mBtnOp != null) mBtnOp.Text = $"Op: {(int)(_targetOpacity * 100)}%";
            if (!_isSolidBlack) this.Opacity = _targetOpacity;
        }

        // ★★★ 修正: 黒背景＆黒透過で統一 ★★★
        void SetTransparentMode()
        {
            // 透過キーと背景色を「ほぼ黒」にする
            Color keyColor = Color.FromArgb(1, 1, 1);
            this.BackColor = keyColor;
            this.TransparencyKey = keyColor;

            this.Opacity = _targetOpacity;
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
            // 透過時は一切何も描画しない (枠線なし)
            if (!_isSolidBlack) return;

            // 黒背景モード時は枠線を描く
            Color frameColor = Color.DarkGray;
            using (Pen p = new Pen(frameColor, 2)) // 固定幅
            {
                p.Alignment = System.Drawing.Drawing2D.PenAlignment.Inset;
                e.Graphics.DrawRectangle(p, this.ClientRectangle);
            }
        }

        protected override void WndProc(ref Message m)
        {
            const int WM_NCHITTEST = 0x84;
            base.WndProc(ref m);
            if (m.Msg == WM_NCHITTEST && !_isMenuOpen)
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

        protected override void OnResize(EventArgs e) { base.OnResize(e); this.Invalidate(); UpdateLayout(); }

        void CreateUI()
        {
            pnlMenu = new Panel();
            pnlMenu.BackColor = Color.FromArgb(220, 30, 30, 30);
            pnlMenu.Visible = false;
            this.Controls.Add(pnlMenu);

            lblTitle = new RotatableLabel();
            lblTitle.Text = ":: Switch Remote ::";
            lblTitle.TextAlign = ContentAlignment.MiddleCenter;
            lblTitle.BackColor = Color.FromArgb(50, 50, 50);
            lblTitle.ForeColor = Color.White;
            lblTitle.Height = 24;
            lblTitle.MouseDown += TopBar_MouseDown;
            this.Controls.Add(lblTitle);

            RotatableButton MkBtn(Control parent, string txt, Color? bg, EventHandler click)
            {
                RotatableButton b = new RotatableButton();
                b.Text = txt;
                b.BackColor = bg ?? Color.White;
                b.FlatStyle = FlatStyle.Flat;
                b.FlatAppearance.BorderSize = 0;
                b.TabStop = false;
                b.Click += click;
                parent.Controls.Add(b);
                return b;
            }

            RotatableButton MkGame(string txt, string cmd, bool isRepeat, Color? bg = null)
            {
                RotatableButton b = new RotatableButton();
                b.Text = txt;
                b.BackColor = bg ?? Color.White;
                b.FlatStyle = FlatStyle.Flat;
                b.FlatAppearance.BorderSize = 0;
                b.TabStop = false;
                b.MouseDown += (s, e) => { if (isRepeat || _isHoldMode) { _repeatingCmd = cmd; Send(cmd, true); _repeatTimer.Start(); } else { Send(cmd, false); } };
                b.MouseUp += (s, e) => { if (isRepeat || _isHoldMode) { _repeatTimer.Stop(); _repeatingCmd = ""; } ClearBuffer(); };
                this.Controls.Add(b);
                return b;
            }

            btnLayoutToggle = MkBtn(this, "↻ 0°", Color.FromArgb(70, 70, 70), (s, e) => RotateLayout());
            btnLayoutToggle.ForeColor = Color.White;

            btnBgToggle = MkBtn(this, "透過", Color.FromArgb(70, 70, 70), (s, e) => ToggleBlackMode());
            btnBgToggle.ForeColor = Color.White;

            btnHoldToggle = MkBtn(this, "Hold: ON", Color.LightGreen, (s, e) => ToggleHoldMode());

            btnMenu = MkBtn(this, "MENU", Color.Orange, (s, e) => ToggleMenu());

            btnZL = MkGame("ZL", "e", false, Color.DarkGray);
            btnL = MkGame("L", "q", false, Color.Gray);
            btnR = MkGame("R", "w", false, Color.Gray);
            btnZR = MkGame("ZR", "r", false, Color.DarkGray);

            btnUp = MkGame("↑", "I", true);
            btnLeft = MkGame("←", "J", true);
            btnDown = MkGame("↓", "K", true);
            btnRight = MkGame("→", "L", true);

            btnX = MkGame("X", "s", false, Color.Yellow);
            btnY = MkGame("Y", "a", false, Color.LightGreen);
            btnB = MkGame("B", "x", false, Color.Red);
            btnA = MkGame("A", "z", false, Color.Cyan);

            btnMinus = MkGame("-", "m", false, Color.LightGray);
            btnHome = MkGame("Home", "h", false, Color.LightBlue);
            btnCap = MkGame("Cap", "c", false, Color.Pink);
            btnPlus = MkGame("+", "n", false, Color.LightGray);

            btnL3 = MkGame("L3", "3", false, Color.Silver);
            btnR3 = MkGame("R3", "4", false, Color.Silver);

            mBtnLR = MkBtn(pnlMenu, "LR Push", Color.Orange, (s, e) => { Send("qw"); ToggleMenu(); });
            mBtnObs = MkBtn(pnlMenu, "OBS", Color.LightSkyBlue, (s, e) => { ControlApp("obs", true); ToggleMenu(); });
            mBtnHide = MkBtn(pnlMenu, "Hide", Color.LightGray, (s, e) => { ControlApp("obs", false); ToggleMenu(); });
            mBtnRot = MkBtn(pnlMenu, "Rotate", Color.White, (s, e) => { RotateLayout(); ToggleMenu(); });
            mBtnTrans = MkBtn(pnlMenu, "Trans", Color.LightGray, (s, e) => { ToggleBlackMode(); ToggleMenu(); });
            mBtnFull = MkBtn(pnlMenu, "Full / Win", Color.LightCoral, (s, e) => { ToggleMaximize(); });

            mBtnOp = MkBtn(pnlMenu, $"Op: {(int)(_targetOpacity * 100)}%", Color.White, (s, e) => { ToggleOpacity(); });

            mBtnClose = MkBtn(pnlMenu, "Close Menu", Color.Silver, (s, e) => ToggleMenu());
        }

        void UpdateLayout()
        {
            if (!_isReady) return;

            int W = this.ClientSize.Width;
            int H = this.ClientSize.Height;
            // ★修正: パディング計算を0にする
            int pad = 0;
            int innerW = W;
            int innerX = 0;
            int innerY = 0;
            int topBarH = 24;
            int margin = 5;

            if (pnlMenu != null) pnlMenu.Bounds = new Rectangle(0, 0, W, H);

            if (_isMenuOpen) UpdateMenuLayout(W, H);

            int contentY = innerY + topBarH + margin;
            int contentH = H - pad - contentY;

            var rects = new System.Collections.Generic.Dictionary<Control, Rectangle>();

            if (lblTitle != null) rects[lblTitle] = new Rectangle(innerX, innerY, innerW, topBarH);

            int toggleW = (innerW - margin) / 4;
            if (btnBgToggle != null) rects[btnBgToggle] = new Rectangle(innerX + innerW - toggleW, innerY, toggleW, topBarH);
            if (btnLayoutToggle != null) rects[btnLayoutToggle] = new Rectangle(innerX + innerW - toggleW * 2 - margin, innerY, toggleW, topBarH);

            int btnBaseSize = 30;

            if (_rotationAngle == 90 || _rotationAngle == 270)
            {
                int rowH = contentH / 14;
                int y = contentY;

                int holdH = Math.Max(30, rowH);
                if (btnHoldToggle != null) rects[btnHoldToggle] = new Rectangle(innerX + margin, y, innerW - margin * 2, holdH);
                y += holdH + margin;

                int shH = Math.Max(40, rowH * 2);
                int shW = (innerW - margin * 6) / 5;
                if (btnZL != null) rects[btnZL] = new Rectangle(innerX + margin, y, shW, shH);
                if (btnL != null) rects[btnL] = new Rectangle(innerX + margin + shW + margin, y, shW, shH);
                if (btnMenu != null) rects[btnMenu] = new Rectangle(innerX + margin + (shW + margin) * 2, y, shW, shH);
                if (btnR != null) rects[btnR] = new Rectangle(innerX + margin + (shW + margin) * 3, y, shW, shH);
                if (btnZR != null) rects[btnZR] = new Rectangle(innerX + margin + (shW + margin) * 4, y, shW, shH);
                y += shH + margin;

                int stickH = Math.Max(30, rowH);
                int stickW = (innerW - margin * 3) / 2;
                if (btnL3 != null) rects[btnL3] = new Rectangle(innerX + margin, y, stickW, stickH);
                if (btnR3 != null) rects[btnR3] = new Rectangle(innerX + margin + stickW + margin, y, stickW, stickH);
                y += stickH + margin;

                int dpadH = Math.Max(100, rowH * 3);
                btnBaseSize = Math.Min(innerW / 3, dpadH / 3);
                int centerX = innerX + innerW / 2;
                int centerY = y + dpadH / 2;
                if (btnUp != null) rects[btnUp] = new Rectangle(centerX - btnBaseSize / 2, centerY - btnBaseSize / 2 - btnBaseSize, btnBaseSize, btnBaseSize);
                if (btnLeft != null) rects[btnLeft] = new Rectangle(centerX - btnBaseSize / 2 - btnBaseSize, centerY - btnBaseSize / 2, btnBaseSize, btnBaseSize);
                if (btnRight != null) rects[btnRight] = new Rectangle(centerX - btnBaseSize / 2 + btnBaseSize, centerY - btnBaseSize / 2, btnBaseSize, btnBaseSize);
                if (btnDown != null) rects[btnDown] = new Rectangle(centerX - btnBaseSize / 2, centerY - btnBaseSize / 2 + btnBaseSize, btnBaseSize, btnBaseSize);
                y += dpadH + margin;

                int sysH = Math.Max(30, rowH);
                int sysW = (innerW - margin * 5) / 4;
                if (btnMinus != null) rects[btnMinus] = new Rectangle(innerX + margin, y, sysW, sysH);
                if (btnHome != null) rects[btnHome] = new Rectangle(innerX + margin + sysW + margin, y, sysW, sysH);
                if (btnCap != null) rects[btnCap] = new Rectangle(innerX + margin + (sysW + margin) * 2, y, sysW, sysH);
                if (btnPlus != null) rects[btnPlus] = new Rectangle(innerX + margin + (sysW + margin) * 3, y, sysW, sysH);
                y += sysH + margin;

                int abxyH = dpadH;
                centerY = y + abxyH / 2;
                if (btnX != null) rects[btnX] = new Rectangle(centerX - btnBaseSize / 2, centerY - btnBaseSize / 2 - btnBaseSize, btnBaseSize, btnBaseSize);
                if (btnY != null) rects[btnY] = new Rectangle(centerX - btnBaseSize / 2 - btnBaseSize, centerY - btnBaseSize / 2, btnBaseSize, btnBaseSize);
                if (btnA != null) rects[btnA] = new Rectangle(centerX - btnBaseSize / 2 + btnBaseSize, centerY - btnBaseSize / 2, btnBaseSize, btnBaseSize);
                if (btnB != null) rects[btnB] = new Rectangle(centerX - btnBaseSize / 2, centerY - btnBaseSize / 2 + btnBaseSize, btnBaseSize, btnBaseSize);
            }
            else
            {
                int holdH = (int)(contentH * 0.10);
                int shoulderH = (int)(contentH * 0.15);
                int stickH = (int)(contentH * 0.12);
                int systemH = (int)(contentH * 0.10);

                if (holdH < 25) holdH = 25;
                if (shoulderH < 40) shoulderH = 40;
                if (stickH < 30) stickH = 30;
                if (systemH < 30) systemH = 30;

                int mainH = contentH - holdH - shoulderH - stickH - systemH - (margin * 4);
                if (mainH < 80) mainH = 80;

                int yHold = contentY;
                int ySh = yHold + holdH + margin;
                int yMain = ySh + shoulderH + margin;
                int ySys = yMain + mainH + margin;
                int yStick = ySys + systemH + margin;

                if (btnHoldToggle != null) rects[btnHoldToggle] = new Rectangle(innerX + margin, yHold, innerW - margin * 2, holdH);

                int shW = (innerW - margin * 6) / 5;
                if (btnZL != null) rects[btnZL] = new Rectangle(innerX + margin, ySh, shW, shoulderH);
                if (btnL != null) rects[btnL] = new Rectangle(innerX + margin + shW + margin, ySh, shW, shoulderH);
                if (btnMenu != null) rects[btnMenu] = new Rectangle(innerX + margin + (shW + margin) * 2, ySh, shW, shoulderH);
                if (btnR != null) rects[btnR] = new Rectangle(innerX + margin + (shW + margin) * 3, ySh, shW, shoulderH);
                if (btnZR != null) rects[btnZR] = new Rectangle(innerX + margin + (shW + margin) * 4, ySh, shW, shoulderH);

                int leftAreaW = innerW / 2;
                btnBaseSize = Math.Min(mainH / 3, (leftAreaW - margin * 2) / 3);

                int leftCenterX = innerX + leftAreaW / 2;
                int mainCenterY = yMain + mainH / 2;
                if (btnUp != null) rects[btnUp] = new Rectangle(leftCenterX - btnBaseSize / 2, mainCenterY - btnBaseSize / 2 - btnBaseSize, btnBaseSize, btnBaseSize);
                if (btnLeft != null) rects[btnLeft] = new Rectangle(leftCenterX - btnBaseSize / 2 - btnBaseSize, mainCenterY - btnBaseSize / 2, btnBaseSize, btnBaseSize);
                if (btnRight != null) rects[btnRight] = new Rectangle(leftCenterX - btnBaseSize / 2 + btnBaseSize, mainCenterY - btnBaseSize / 2, btnBaseSize, btnBaseSize);
                if (btnDown != null) rects[btnDown] = new Rectangle(leftCenterX - btnBaseSize / 2, mainCenterY - btnBaseSize / 2 + btnBaseSize, btnBaseSize, btnBaseSize);

                int rightCenterX = innerX + leftAreaW + (innerW - leftAreaW) / 2;
                if (btnX != null) rects[btnX] = new Rectangle(rightCenterX - btnBaseSize / 2, mainCenterY - btnBaseSize / 2 - btnBaseSize, btnBaseSize, btnBaseSize);
                if (btnY != null) rects[btnY] = new Rectangle(rightCenterX - btnBaseSize / 2 - btnBaseSize, mainCenterY - btnBaseSize / 2, btnBaseSize, btnBaseSize);
                if (btnA != null) rects[btnA] = new Rectangle(rightCenterX - btnBaseSize / 2 + btnBaseSize, mainCenterY - btnBaseSize / 2, btnBaseSize, btnBaseSize);
                if (btnB != null) rects[btnB] = new Rectangle(rightCenterX - btnBaseSize / 2, mainCenterY - btnBaseSize / 2 + btnBaseSize, btnBaseSize, btnBaseSize);

                int sysW = (innerW - margin * 5) / 4;
                if (btnMinus != null) rects[btnMinus] = new Rectangle(innerX + margin, ySys, sysW, systemH);
                if (btnHome != null) rects[btnHome] = new Rectangle(innerX + margin + sysW + margin, ySys, sysW, systemH);
                if (btnCap != null) rects[btnCap] = new Rectangle(innerX + margin + (sysW + margin) * 2, ySys, sysW, systemH);
                if (btnPlus != null) rects[btnPlus] = new Rectangle(innerX + margin + (sysW + margin) * 3, ySys, sysW, systemH);

                int stickW = (innerW - margin * 3) / 2;
                if (btnL3 != null) rects[btnL3] = new Rectangle(innerX + margin, yStick, stickW, stickH);
                if (btnR3 != null) rects[btnR3] = new Rectangle(innerX + margin + stickW + margin, yStick, stickW, stickH);
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

                if (_rotationAngle == 180 || _rotationAngle == 270)
                {
                    r = new Rectangle(W - r.X - r.Width, H - r.Y - r.Height, r.Width, r.Height);
                }
                c.Bounds = r;

                if (c is Button)
                {
                    bool isMain = (c == btnUp || c == btnLeft || c == btnRight || c == btnDown || c == btnX || c == btnY || c == btnA || c == btnB);
                    c.Font = isMain ? fontMain : fontSub;
                }
            }
            this.TopMost = true;
        }

        void UpdateMenuLayout(int w, int h)
        {
            if (pnlMenu == null) return;

            int cols = 2;
            int rows = 4;
            int margin = 10;
            int btnW = (w - margin * (cols + 1)) / cols;
            int btnH = (h - margin * (rows + 1)) / rows;

            Control[] menuBtns = { mBtnLR, mBtnObs, mBtnHide, mBtnRot, mBtnTrans, mBtnFull, mBtnOp, mBtnClose };

            for (int i = 0; i < menuBtns.Length; i++)
            {
                if (menuBtns[i] == null) continue;
                int r = i / cols;
                int c = i % cols;

                Rectangle rect = new Rectangle(margin + c * (btnW + margin), margin + r * (btnH + margin), btnW, btnH);

                if (_rotationAngle == 180 || _rotationAngle == 270)
                {
                    rect = new Rectangle(w - rect.X - rect.Width, h - rect.Y - rect.Height, rect.Width, rect.Height);
                }

                menuBtns[i].Bounds = rect;
                if (menuBtns[i] is RotatableButton rb) rb.RotationAngle = _rotationAngle;

                float fSize = Math.Max(12, Math.Min(btnW, btnH) / 6);
                menuBtns[i].Font = new Font("Arial", fSize, FontStyle.Bold);
            }
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

    public class RotatableButton : Button
    {
        public int RotationAngle { get; set; } = 0;
        public RotatableButton()
        {
            this.SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.Opaque | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
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