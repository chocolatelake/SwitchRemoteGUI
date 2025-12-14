using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO.Ports;
using System.Windows.Forms;
using System.Diagnostics;
using System.Runtime.InteropServices;
#nullable enable

namespace SwitchRemoteGUI
{
    public partial class Form1 : Form
    {
        #region 設定・定数 (Constants)

        // 通信設定
        private const string PORT_NAME = "COM5";
        private const int BAUD_RATE = 9600;
        private const int SEND_INTERVAL = 400;

        // 連射設定 (十字キーの挙動再現)
        private const int REPEAT_DELAY = 500;     // 押し始めのタメ (ms)
        private const int REPEAT_INTERVAL = 100;  // 連射間隔 (ms)
        private const int MIN_SEND_INTERVAL = 50; // 単発連打の最小間隔 (ms)

        // UIサイズ設定
        private const int BASE_W = 800;
        private const int BASE_H = 400;
        private const int RESIZE_GRIP_SIZE = 10;
        private const int BORDER_SIZE = 0;

        // Win32 API 定数
        private const int SW_RESTORE = 9;
        private const int SW_MINIMIZE = 6;
        private const int WM_NCHITTEST = 0x84;

        // リサイズ判定用
        private const int HTLEFT = 10;
        private const int HTRIGHT = 11;
        private const int HTTOP = 12;
        private const int HTTOPLEFT = 13;
        private const int HTTOPRIGHT = 14;
        private const int HTBOTTOM = 15;
        private const int HTBOTTOMLEFT = 16;
        private const int HTBOTTOMRIGHT = 17;

        #endregion

        #region Win32 API Imports

        [DllImport("user32.dll")] public static extern int SendMessage(IntPtr hWnd, int Msg, int wParam, int lParam);
        [DllImport("user32.dll")] public static extern bool ReleaseCapture();
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern bool ShowWindowAsync(IntPtr hWnd, int nCmdShow);
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hWnd);

        #endregion

        #region フィールド (Fields)

        // 通信
        SerialPort? port;

        // 状態フラグ
        bool _isReady = false;
        bool _isSolidBlack = false;
        int _rotationAngle = 0;
        bool _isHoldMode = true;
        bool _isMenuOpen = false;
        bool _showCustomButtons = true;
        double _targetOpacity = 0.3;

        // 連射制御
        System.Windows.Forms.Timer _repeatTimer;
        string _repeatingCmd = "";
        DateTime _lastSendTime = DateTime.MinValue;

        // キーボード制御用 (OSのキーリピート対策)
        Keys _currentPressedKey = Keys.None;

        // UIコントロール
        RotatableButton? btnLayoutToggle;
        RotatableButton? btnBgToggle;
        RotatableLabel? lblTitle;

        RotatableButton? btnCustom;
        RotatableButton? btnHoldToggle;
        RotatableButton? btnMenu;

        RotatableButton? btnZL, btnL, btnLR, btnR, btnZR;
        RotatableButton? btnUp, btnLeft, btnDown, btnRight;
        RotatableButton? btnUpLeft, btnUpRight, btnDownLeft, btnDownRight;
        RotatableButton? btnX, btnY, btnB, btnA;
        RotatableButton? btnXY, btnXA, btnAB, btnYB;
        RotatableButton? btnMinus, btnHome, btnCap, btnPlus;
        RotatableButton? btnL3, btnR3;

        Panel? pnlMenu;
        RotatableButton? mBtnObs, mBtnHide;
        RotatableButton? mBtnRot, mBtnTrans;
        RotatableButton? mBtnFull, mBtnClose;
        RotatableButton? mBtnOp;

        #endregion

        #region コンストラクタ (Constructor)

        public Form1()
        {
            InitializeComponent();

            this.TopMost = true;
            this.FormBorderStyle = FormBorderStyle.None;
            this.DoubleBuffered = true;
            this.MinimumSize = new Size(200, 100);

            _repeatTimer = new System.Windows.Forms.Timer();
            _repeatTimer.Tick += RepeatTimer_Tick;

            this.Padding = new Padding(BORDER_SIZE);
            SetTransparentMode();

            // イベント設定
            this.KeyPreview = true;
            this.KeyDown += Form1_KeyDown;
            this.KeyUp += Form1_KeyUp; // キー離上検知を追加
            this.Paint += Form1_Paint;

            ConnectPort();
            CreateUI();

            this.Size = new Size(BASE_W, BASE_H);
            _isReady = true;
            UpdateLayout();
        }

        #endregion

        #region 入力ロジック (Input Logic)

        /// <summary>
        /// ボタン入力開始処理。マウスダウンやキーダウンから呼ばれる共通ロジック。
        /// </summary>
        void StartInput(string cmd)
        {
            // 既存のMkGame内にあったロジックをそのまま使用
            if (_isHoldMode)
            {
                // Hold ON: 即時送信 -> Delay -> 高速連射
                _repeatingCmd = cmd;
                Send(cmd, true); // 1回目 (強制送信)

                _repeatTimer.Interval = REPEAT_DELAY; // 最初はタメを入れる
                _repeatTimer.Start();
            }
            else
            {
                // Hold OFF: 単発送信 (連打防止あり)
                Send(cmd, false);
            }
        }

        /// <summary>
        /// ボタン入力終了処理。マウスアップやキーアップから呼ばれる共通ロジック。
        /// </summary>
        void StopInput()
        {
            if (_isHoldMode)
            {
                _repeatTimer.Stop();
                _repeatingCmd = "";
            }
            ClearBuffer();
        }

        /// <summary>
        /// 連射タイマー処理
        /// </summary>
        private void RepeatTimer_Tick(object? sender, EventArgs e)
        {
            if (!string.IsNullOrEmpty(_repeatingCmd))
            {
                Send(_repeatingCmd, true);

                // 2回目以降は間隔を短くして高速連射に切り替え
                _repeatTimer.Interval = REPEAT_INTERVAL;
            }
        }

        #endregion

        #region 通信処理 (Communication)

        void Send(string cmd, bool force = false)
        {
            if (port != null && port.IsOpen)
            {
                string finalCmd = RotateCommand(cmd);
                double msSinceLast = (DateTime.Now - _lastSendTime).TotalMilliseconds;

                // 単発モード時の連打制限
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

        string RotateCommand(string cmd)
        {
            if (_rotationAngle == 0) return cmd;
            char[] chars = cmd.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                chars[i] = RotateChar(chars[i]);
            }
            return new string(chars);
        }

        char RotateChar(char c)
        {
            if (c == 'I') { if (_rotationAngle == 90) return 'J'; if (_rotationAngle == 180) return 'K'; if (_rotationAngle == 270) return 'L'; }
            if (c == 'J') { if (_rotationAngle == 90) return 'K'; if (_rotationAngle == 180) return 'L'; if (_rotationAngle == 270) return 'I'; }
            if (c == 'K') { if (_rotationAngle == 90) return 'L'; if (_rotationAngle == 180) return 'I'; if (_rotationAngle == 270) return 'J'; }
            if (c == 'L') { if (_rotationAngle == 90) return 'I'; if (_rotationAngle == 180) return 'J'; if (_rotationAngle == 270) return 'K'; }
            return c;
        }

        void ClearBuffer() { if (port != null && port.IsOpen) try { port.DiscardOutBuffer(); } catch { } }

        void ConnectPort() { try { port = new SerialPort(PORT_NAME, BAUD_RATE); port.Open(); } catch { } }

        #endregion

        #region キーボード入力制御 (Keyboard Input)

        private void Form1_KeyDown(object? sender, KeyEventArgs e)
        {
            // OSのキーリピートによる連打イベントを無視する（押しっぱなし対策）
            if (e.KeyCode == _currentPressedKey) return;

            string? cmd = GetCommandFromKey(e.KeyCode);

            if (cmd != null)
            {
                _currentPressedKey = e.KeyCode; // 押下中のキーを記録
                StartInput(cmd);                // 共通ロジックへ流す
            }
            else if (e.KeyCode == Keys.Escape)
            {
                ToggleMenu();
            }
        }

        private void Form1_KeyUp(object? sender, KeyEventArgs e)
        {
            // 押されているキーが離された場合のみ処理
            if (e.KeyCode == _currentPressedKey)
            {
                StopInput();                    // 共通ロジックへ流す
                _currentPressedKey = Keys.None;
            }
        }

        // キー割り当て定義
        private string? GetCommandFromKey(Keys key)
        {
            switch (key)
            {
                // WASD / 矢印 (移動)
                case Keys.W: case Keys.Up: return "I";
                case Keys.A: case Keys.Left: return "J";
                case Keys.S: case Keys.Down: return "K";
                case Keys.D: case Keys.Right: return "L";

                // ABXY
                case Keys.Z: return "z"; // A
                case Keys.X: return "x"; // B
                case Keys.C: return "a"; // Y
                case Keys.V: return "s"; // X

                // ショルダー & スティック
                case Keys.Q: return "q"; // L
                case Keys.E: return "w"; // R
                case Keys.D1: return "e"; // ZL (キー1)
                case Keys.D3: return "r"; // ZR (キー3)
                case Keys.D2: return "qw"; // LR (キー2: L+R同時)
                case Keys.D4: return "4"; // R3 (キー4)
                case Keys.F: return "3"; // L3 (キーF)

                // 機能
                case Keys.M: return "m"; // -
                case Keys.N: return "n"; // +
                case Keys.H: return "h"; // Home
                case Keys.P: return "c"; // Capture

                default: return null;
            }
        }

        #endregion

        #region 外部アプリ制御 & 状態切替

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

        void ToggleCustomButtons()
        {
            _showCustomButtons = !_showCustomButtons;
            Control?[] customs = {
                btnUpLeft, btnUpRight, btnDownLeft, btnDownRight,
                btnXY, btnXA, btnAB, btnYB
            };
            foreach (var btn in customs) if (btn != null) btn.Visible = _showCustomButtons;

            if (btnCustom != null)
            {
                btnCustom.Text = _showCustomButtons ? "Custom: ON" : "Custom: OFF";
                btnCustom.BackColor = _showCustomButtons ? Color.Gold : Color.Wheat;
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

        void SetTransparentMode()
        {
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

        #endregion

        #region 描画 & ウィンドウメッセージ (Paint & WndProc)

        private void Form1_Paint(object? sender, PaintEventArgs e)
        {
            if (!_isSolidBlack) return;
            Color frameColor = Color.DarkGray;
            using (Pen p = new Pen(frameColor, 2))
            {
                p.Alignment = System.Drawing.Drawing2D.PenAlignment.Inset;
                e.Graphics.DrawRectangle(p, this.ClientRectangle);
            }
        }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg == WM_NCHITTEST && !_isMenuOpen)
            {
                Point pos = this.PointToClient(new Point(m.LParam.ToInt32()));
                if (pos.X <= RESIZE_GRIP_SIZE && pos.Y <= RESIZE_GRIP_SIZE) m.Result = (IntPtr)13;
                else if (pos.X >= this.ClientSize.Width - RESIZE_GRIP_SIZE && pos.Y <= RESIZE_GRIP_SIZE) m.Result = (IntPtr)14;
                else if (pos.X <= RESIZE_GRIP_SIZE && pos.Y >= this.ClientSize.Height - RESIZE_GRIP_SIZE) m.Result = (IntPtr)16;
                else if (pos.X >= this.ClientSize.Width - RESIZE_GRIP_SIZE && pos.Y >= this.ClientSize.Height - RESIZE_GRIP_SIZE) m.Result = (IntPtr)17;
                else if (pos.X <= RESIZE_GRIP_SIZE) m.Result = (IntPtr)10;
                else if (pos.X >= this.ClientSize.Width - RESIZE_GRIP_SIZE) m.Result = (IntPtr)11;
                else if (pos.Y <= RESIZE_GRIP_SIZE) m.Result = (IntPtr)12;
                else if (pos.Y >= this.ClientSize.Height - RESIZE_GRIP_SIZE) m.Result = (IntPtr)15;
            }
        }

        private void TopBar_MouseDown(object? sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left) { ReleaseCapture(); SendMessage(Handle, 0xA1, 0x2, 0); }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            if (port != null && port.IsOpen) port.Close();
            base.OnFormClosed(e);
        }

        protected override void OnResize(EventArgs e) { base.OnResize(e); this.Invalidate(); UpdateLayout(); }

        #endregion

        #region UI生成 (CreateUI)

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
                b.Opacity = 1.0f;
                parent.Controls.Add(b);
                return b;
            }

            // ゲームボタン生成 (isRepeatは無視し、共通ロジックを使用)
            RotatableButton MkGame(string txt, string cmd, bool isRepeat, Color? bg = null)
            {
                RotatableButton b = new RotatableButton();
                b.Text = txt;
                b.BackColor = bg ?? Color.White;
                b.FlatStyle = FlatStyle.Flat;
                b.FlatAppearance.BorderSize = 0;
                b.TabStop = false;
                b.Opacity = 1.0f;

                // ★修正: 共通処理(StartInput)に流す
                b.MouseDown += (s, e) => StartInput(cmd);
                b.MouseUp += (s, e) => StopInput();

                this.Controls.Add(b);
                return b;
            }

            // --- メイン画面 ---
            btnLayoutToggle = MkBtn(this, "↻ 0°", Color.FromArgb(70, 70, 70), (s, e) => RotateLayout());
            btnLayoutToggle.ForeColor = Color.White;
            btnLayoutToggle.Opacity = 0.8f;

            btnBgToggle = MkBtn(this, "透過", Color.FromArgb(70, 70, 70), (s, e) => ToggleBlackMode());
            btnBgToggle.ForeColor = Color.White;
            btnBgToggle.Opacity = 0.8f;

            btnCustom = MkBtn(this, "Custom: ON", Color.Gold, (s, e) => ToggleCustomButtons());
            btnHoldToggle = MkBtn(this, "Hold: ON", Color.LightGreen, (s, e) => ToggleHoldMode());
            btnMenu = MkBtn(this, "MENU", Color.Orange, (s, e) => ToggleMenu());

            // ショルダー
            btnZL = MkGame("ZL", "e", false, Color.DarkGray);
            btnL = MkGame("L", "q", false, Color.Gray);
            btnLR = MkGame("LR", "qw", false, Color.Orange);
            btnR = MkGame("R", "w", false, Color.Gray);
            btnZR = MkGame("ZR", "r", false, Color.DarkGray);

            Color diagColor = Color.FromArgb(240, 240, 240);
            btnUpLeft = MkGame("↖", "IJ", false, diagColor);
            btnUp = MkGame("↑", "I", false);
            btnUpRight = MkGame("↗", "IL", false, diagColor);
            btnLeft = MkGame("←", "J", false);
            btnRight = MkGame("→", "L", false);
            btnDownLeft = MkGame("↙", "KJ", false, diagColor);
            btnDown = MkGame("↓", "K", false);
            btnDownRight = MkGame("↘", "KL", false, diagColor);

            // ABXY
            btnXY = MkGame("XY", "sa", false, diagColor);
            btnX = MkGame("X", "s", false, Color.Yellow);
            btnXA = MkGame("XA", "sz", false, diagColor);
            btnY = MkGame("Y", "a", false, Color.LightGreen);
            btnA = MkGame("A", "z", false, Color.Cyan);
            btnYB = MkGame("YB", "ax", false, diagColor);
            btnB = MkGame("B", "x", false, Color.Red);
            btnAB = MkGame("AB", "zx", false, diagColor);

            // 機能
            btnMinus = MkGame("-", "m", false, Color.LightGray);
            btnHome = MkGame("Home", "h", false, Color.LightBlue);
            btnCap = MkGame("Cap", "c", false, Color.Pink);
            btnPlus = MkGame("+", "n", false, Color.LightGray);

            btnL3 = MkGame("L3", "3", false, Color.Silver);
            btnR3 = MkGame("R3", "4", false, Color.Silver);

            // メニュー
            mBtnObs = MkBtn(pnlMenu, "OBS", Color.LightSkyBlue, (s, e) => { ControlApp("obs", true); ToggleMenu(); });
            mBtnHide = MkBtn(pnlMenu, "Hide", Color.LightGray, (s, e) => { ControlApp("obs", false); ToggleMenu(); });
            mBtnRot = MkBtn(pnlMenu, "Rotate", Color.White, (s, e) => { RotateLayout(); ToggleMenu(); });
            mBtnTrans = MkBtn(pnlMenu, "Trans", Color.LightGray, (s, e) => { ToggleBlackMode(); ToggleMenu(); });
            mBtnFull = MkBtn(pnlMenu, "Full / Win", Color.LightCoral, (s, e) => { ToggleMaximize(); });
            mBtnOp = MkBtn(pnlMenu, $"Op: {(int)(_targetOpacity * 100)}%", Color.White, (s, e) => { ToggleOpacity(); });
            mBtnClose = MkBtn(pnlMenu, "Close Menu", Color.Silver, (s, e) => ToggleMenu());
        }

        #endregion

        #region レイアウト計算 (UpdateLayout)

        void UpdateLayout()
        {
            if (!_isReady) return;

            int W = this.ClientSize.Width;
            int H = this.ClientSize.Height;
            int pad = BORDER_SIZE;
            int innerW = W - pad * 2;
            int innerX = pad;
            int innerY = pad;
            int topBarH = 24;
            int margin = 5;

            if (pnlMenu != null) pnlMenu.Bounds = new Rectangle(innerX, innerY, innerW, H - pad * 2);

            if (_isMenuOpen) UpdateMenuLayout(innerW, H - pad * 2);

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

                int topRowH = Math.Max(30, rowH);
                int btnW = (innerW - margin * 2) / 3;
                if (btnCustom != null) rects[btnCustom] = new Rectangle(innerX, y, btnW, topRowH);
                if (btnHoldToggle != null) rects[btnHoldToggle] = new Rectangle(innerX + btnW + margin, y, btnW, topRowH);
                if (btnMenu != null) rects[btnMenu] = new Rectangle(innerX + (btnW + margin) * 2, y, btnW, topRowH);
                y += topRowH + margin;

                int shH = Math.Max(40, rowH * 2);
                int shW = (innerW - margin * 4) / 5;
                if (btnZL != null) rects[btnZL] = new Rectangle(innerX, y, shW, shH);
                if (btnL != null) rects[btnL] = new Rectangle(innerX + shW + margin, y, shW, shH);
                if (btnLR != null) rects[btnLR] = new Rectangle(innerX + (shW + margin) * 2, y, shW, shH);
                if (btnR != null) rects[btnR] = new Rectangle(innerX + (shW + margin) * 3, y, shW, shH);
                if (btnZR != null) rects[btnZR] = new Rectangle(innerX + (shW + margin) * 4, y, shW, shH);
                y += shH + margin;

                int stickH = Math.Max(30, rowH);
                int stickW = (innerW - margin) / 2;
                if (btnL3 != null) rects[btnL3] = new Rectangle(innerX, y, stickW, stickH);
                if (btnR3 != null) rects[btnR3] = new Rectangle(innerX + stickW + margin, y, stickW, stickH);
                y += stickH + margin;

                int dpadH = Math.Max(120, rowH * 3);
                btnBaseSize = Math.Min(innerW / 3, dpadH / 3);
                int centerX = innerX + innerW / 2;
                int centerY = y + dpadH / 2;
                int b = btnBaseSize;

                SetGridRects(rects, centerX, centerY, b,
                    btnUpLeft, btnUp, btnUpRight, btnLeft, btnRight, btnDownLeft, btnDown, btnDownRight);
                y += dpadH + margin;

                int sysH = Math.Max(30, rowH);
                int sysW = (innerW - margin * 3) / 4;
                if (btnMinus != null) rects[btnMinus] = new Rectangle(innerX, y, sysW, sysH);
                if (btnHome != null) rects[btnHome] = new Rectangle(innerX + sysW + margin, y, sysW, sysH);
                if (btnCap != null) rects[btnCap] = new Rectangle(innerX + (sysW + margin) * 2, y, sysW, sysH);
                if (btnPlus != null) rects[btnPlus] = new Rectangle(innerX + (sysW + margin) * 3, y, sysW, sysH);
                y += sysH + margin;

                int abxyH = dpadH;
                centerY = y + abxyH / 2;
                SetGridRects(rects, centerX, centerY, b,
                    btnXY, btnX, btnXA, btnY, btnA, btnYB, btnB, btnAB);
            }
            else
            {
                int topH = (int)(contentH * 0.10);
                int shoulderH = (int)(contentH * 0.15);
                int stickH = (int)(contentH * 0.12);
                int systemH = (int)(contentH * 0.10);

                if (topH < 25) topH = 25;
                if (shoulderH < 40) shoulderH = 40;
                if (stickH < 30) stickH = 30;
                if (systemH < 30) systemH = 30;

                int mainH = contentH - topH - shoulderH - stickH - systemH - (margin * 4);
                if (mainH < 80) mainH = 80;

                int yTop = contentY;
                int ySh = yTop + topH + margin;
                int yMain = ySh + shoulderH + margin;
                int ySys = yMain + mainH + margin;
                int yStick = ySys + systemH + margin;

                int btnW = (innerW - margin * 2) / 3;
                if (btnCustom != null) rects[btnCustom] = new Rectangle(innerX, yTop, btnW, topH);
                if (btnHoldToggle != null) rects[btnHoldToggle] = new Rectangle(innerX + btnW + margin, yTop, btnW, topH);
                if (btnMenu != null) rects[btnMenu] = new Rectangle(innerX + (btnW + margin) * 2, yTop, btnW, topH);

                int shW = (innerW - margin * 4) / 5;
                if (btnZL != null) rects[btnZL] = new Rectangle(innerX, ySh, shW, shoulderH);
                if (btnL != null) rects[btnL] = new Rectangle(innerX + shW + margin, ySh, shW, shoulderH);
                if (btnLR != null) rects[btnLR] = new Rectangle(innerX + (shW + margin) * 2, ySh, shW, shoulderH);
                if (btnR != null) rects[btnR] = new Rectangle(innerX + (shW + margin) * 3, ySh, shW, shoulderH);
                if (btnZR != null) rects[btnZR] = new Rectangle(innerX + (shW + margin) * 4, ySh, shW, shoulderH);

                int leftAreaW = innerW / 2;
                btnBaseSize = Math.Min(mainH / 3, (leftAreaW - margin * 2) / 3);
                int b = btnBaseSize;

                int leftCenterX = innerX + leftAreaW / 2;
                int mainCenterY = yMain + mainH / 2;
                SetGridRects(rects, leftCenterX, mainCenterY, b,
                    btnUpLeft, btnUp, btnUpRight, btnLeft, btnRight, btnDownLeft, btnDown, btnDownRight);

                int rightCenterX = innerX + leftAreaW + (innerW - leftAreaW) / 2;
                SetGridRects(rects, rightCenterX, mainCenterY, b,
                    btnXY, btnX, btnXA, btnY, btnA, btnYB, btnB, btnAB);

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

        // 3x3グリッド配置ヘルパー
        void SetGridRects(System.Collections.Generic.Dictionary<Control, Rectangle> rects,
                          int cx, int cy, int b,
                          Control? ul, Control? u, Control? ur,
                          Control? l, Control? r,
                          Control? dl, Control? d, Control? dr)
        {
            if (ul != null) rects[ul] = new Rectangle(cx - b - b / 2, cy - b - b / 2, b, b);
            if (u != null) rects[u] = new Rectangle(cx - b / 2, cy - b - b / 2, b, b);
            if (ur != null) rects[ur] = new Rectangle(cx + b / 2, cy - b - b / 2, b, b);

            if (l != null) rects[l] = new Rectangle(cx - b - b / 2, cy - b / 2, b, b);
            if (r != null) rects[r] = new Rectangle(cx + b / 2, cy - b / 2, b, b);

            if (dl != null) rects[dl] = new Rectangle(cx - b - b / 2, cy + b / 2, b, b);
            if (d != null) rects[d] = new Rectangle(cx - b / 2, cy + b / 2, b, b);
            if (dr != null) rects[dr] = new Rectangle(cx + b / 2, cy + b / 2, b, b);
        }

        void UpdateMenuLayout(int w, int h)
        {
            if (pnlMenu == null) return;

            int cols = 2;
            int rows = 4;
            int margin = 10;
            int btnW = (w - margin * (cols + 1)) / cols;
            int btnH = (h - margin * (rows + 1)) / rows;

            Control[] menuBtns = { mBtnObs, mBtnHide, mBtnRot, mBtnTrans, mBtnFull, mBtnOp, mBtnClose };

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

        #endregion
    }

    #region カスタムコントロール (Custom Controls)

    public class RotatableButton : Button
    {
        public int RotationAngle { get; set; } = 0;
        public float Opacity { get; set; } = 1.0f;

        public RotatableButton()
        {
            this.SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.Opaque | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            this.BackColor = Color.Transparent;
        }
        protected override CreateParams CreateParams { get { CreateParams cp = base.CreateParams; cp.ExStyle |= 0x20; return cp; } }
        protected override void OnResize(EventArgs e) { base.OnResize(e); UpdateShape(); }
        private void UpdateShape()
        {
            int w = this.Width; int h = this.Height;
            if (w < 1 || h < 1) return;

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
            int alpha = (int)(255 * Opacity);
            Color drawColor = Color.FromArgb(alpha, c);

            if (this.Region != null) { using (Brush brush = new SolidBrush(drawColor)) { g.FillRegion(brush, this.Region); } }

            if (!string.IsNullOrEmpty(this.Text))
            {
                StringFormat sf = new StringFormat(); sf.Alignment = StringAlignment.Center; sf.LineAlignment = StringAlignment.Center;
                int textAlpha = Math.Max(30, (int)(255 * Opacity));
                Color textColor = Color.FromArgb(textAlpha, this.ForeColor);
                using (Brush textBrush = new SolidBrush(textColor))
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

    #endregion
}