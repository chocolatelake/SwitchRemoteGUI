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

        // スナップモード（左右寄せ時）のフラグ
        bool _isSnapMode = false;

        // デフォルトでCustomボタンを非表示にする
        bool _showCustomButtons = false;

        // コントローラー非表示フラグ
        bool _isControllerHidden = false;

        double _targetOpacity = 0.3;

        // 連射制御
        System.Windows.Forms.Timer _repeatTimer;
        string _repeatingCmd = "";
        DateTime _lastSendTime = DateTime.MinValue;

        // キーボード制御用 (OSのキーリピート対策)
        Keys _currentPressedKey = Keys.None;

        // UIコントロール
        RotatableLabel? lblTitle;

        // 再表示用ボタン
        RotatableButton? btnShowController;

        // メイン画面のCustomボタンは削除し、メニューへ移動
        // RotatableButton? btnCustom; 
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
        RotatableButton? mBtnFull, mBtnClose;
        RotatableButton? mBtnOp;

        // メニュー内に追加するCustom切り替えボタン
        RotatableButton? mBtnCustom;

        // 左右寄せボタン
        RotatableButton? mBtnLeft, mBtnRight;

        // メニュー内の非表示ボタン
        RotatableButton? mBtnHideController;

        // キー入力一覧用
        RotatableButton? mBtnKeys;
        Panel? pnlKeyList;
        RotatableLabel? lblKeyList;
        RotatableButton? btnKeyListClose;

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
            _repeatTimer.Interval = SEND_INTERVAL;
            _repeatTimer.Tick += RepeatTimer_Tick;

            this.Padding = new Padding(BORDER_SIZE);
            SetTransparentMode(); // 初期状態で透過モード

            // イベント設定
            this.KeyPreview = true;
            this.KeyDown += Form1_KeyDown;
            this.KeyUp += Form1_KeyUp;
            this.Paint += Form1_Paint;

            ConnectPort();
            CreateUI();

            this.Size = new Size(BASE_W, BASE_H);
            _isReady = true;
            UpdateLayout();
        }

        #endregion

        #region 入力ロジック (Input Logic)

        // 入力開始 (マウス・キーボード共通)
        void StartInput(string cmd)
        {
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

        // 入力終了 (マウス・キーボード共通)
        void StopInput()
        {
            if (_isHoldMode)
            {
                _repeatTimer.Stop();
                _repeatingCmd = "";
            }
            ClearBuffer();
        }

        private void RepeatTimer_Tick(object? sender, EventArgs e)
        {
            if (!string.IsNullOrEmpty(_repeatingCmd)) Send(_repeatingCmd, true);
        }

        #endregion

        #region 通信処理 (Communication)

        void Send(string cmd, bool force = false)
        {
            if (port != null && port.IsOpen)
            {
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

        // キー押下時
        private void Form1_KeyDown(object? sender, KeyEventArgs e)
        {
            // キーリピート防止
            if (e.KeyCode == _currentPressedKey) return;

            string? cmd = GetCommandFromKey(e.KeyCode);

            if (cmd != null)
            {
                _currentPressedKey = e.KeyCode;
                StartInput(cmd);
            }
            // MenuキーをTabに変更
            else if (e.KeyCode == Keys.Tab)
            {
                e.SuppressKeyPress = true; // Tabによるフォーカス移動や音を抑制
                // もしキー一覧が開いていたら閉じる、そうでなければメニュー
                if (pnlKeyList != null && pnlKeyList.Visible) ToggleKeyList();
                else ToggleMenu();
            }
        }

        // キー離上時
        private void Form1_KeyUp(object? sender, KeyEventArgs e)
        {
            if (e.KeyCode == _currentPressedKey)
            {
                StopInput();
                _currentPressedKey = Keys.None;
            }
        }

        // キー割り当て
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
                if (_isMenuOpen)
                {
                    pnlMenu.BringToFront();
                    // キー一覧が開いていたら閉じる
                    if (pnlKeyList != null) pnlKeyList.Visible = false;
                }
                UpdateLayout();
            }
        }

        void ToggleKeyList()
        {
            if (pnlKeyList != null)
            {
                pnlKeyList.Visible = !pnlKeyList.Visible;
                if (pnlKeyList.Visible)
                {
                    pnlKeyList.BringToFront();
                    _isMenuOpen = false; // メニューは閉じる
                    if (pnlMenu != null) pnlMenu.Visible = false;
                }
                UpdateLayout();
            }
        }

        // コントローラー表示・非表示の切り替え
        void SetControllerVisibility(bool visible)
        {
            _isControllerHidden = !visible;
            _isMenuOpen = false; // メニューは閉じる
            if (pnlMenu != null) pnlMenu.Visible = false;
            if (pnlKeyList != null) pnlKeyList.Visible = false;

            // 再表示ボタンの切り替え
            if (btnShowController != null) btnShowController.Visible = !visible;

            // コントローラーパーツの表示切替
            Control?[] ctrls = {
                // btnCustom, // メイン画面から削除済み
                btnHoldToggle, btnMenu,
                btnZL, btnL, btnLR, btnR, btnZR,
                btnUp, btnLeft, btnDown, btnRight,
                btnUpLeft, btnUpRight, btnDownLeft, btnDownRight,
                btnX, btnY, btnB, btnA,
                btnXY, btnXA, btnAB, btnYB,
                btnMinus, btnHome, btnCap, btnPlus,
                btnL3, btnR3
            };

            foreach (var c in ctrls)
            {
                if (c != null) c.Visible = visible;
            }

            // ★修正: 表示時にToggle(切り替え)せず、現在のフラグ状態に従って適用するのみにする
            // これで「ShowGUIを押すとCustomが勝手にONになる」現象を防ぐ
            if (visible) ApplyCustomButtonVisibility();

            UpdateLayout();
        }

        void ToggleCustomButtons()
        {
            // 非表示モード中なら処理しない
            if (_isControllerHidden) return;

            // スナップモード中は切り替えを無効化する
            if (_isSnapMode) return;

            // フラグを反転させる（ここが唯一のON/OFF切り替え箇所）
            _showCustomButtons = !_showCustomButtons;
            ApplyCustomButtonVisibility();

            // メニュー内のボタンの見た目を更新
            if (mBtnCustom != null)
            {
                mBtnCustom.Text = _showCustomButtons ? "Custom: ON" : "Custom: OFF";
                mBtnCustom.BackColor = _showCustomButtons ? Color.Gold : Color.Wheat;
            }
        }

        // Customボタンの表示反映処理
        void ApplyCustomButtonVisibility()
        {
            Control?[] customs = {
                btnUpLeft, btnUpRight, btnDownLeft, btnDownRight,
                btnXY, btnXA, btnAB, btnYB
            };
            // スナップモード中は強制非表示（フラグは変えない）
            foreach (var btn in customs) if (btn != null) btn.Visible = _showCustomButtons && !_isControllerHidden && !_isSnapMode;
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
            UpdateLayout();
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
            _isSnapMode = false; // 最大化したらスナップ解除
            ApplyCustomButtonVisibility(); // カスタムボタン状態復元

            if (pnlMenu != null) pnlMenu.Visible = false;
            UpdateLayout();
        }

        // 画面の左右に寄せる処理
        void SetSnapLayout(bool isRight)
        {
            // 最大化などを解除
            if (this.WindowState == FormWindowState.Maximized)
                this.WindowState = FormWindowState.Normal;

            // スナップモード有効化
            _isSnapMode = true;

            // ★修正: スナップ時に勝手にフラグを変更しない。
            // Customボタンは ApplyCustomButtonVisibility 内で _isSnapMode を見て非表示になる。
            ApplyCustomButtonVisibility();

            // 現在ウィンドウがあるスクリーンを取得
            Screen scr = Screen.FromControl(this);
            Rectangle area = scr.WorkingArea;

            // 幅は画面の1/4、高さは画面いっぱい
            int w = area.Width / 4;
            int h = area.Height;

            // 最小幅のガード
            if (w < 200) w = 200;

            int x = isRight ? (area.Right - w) : area.Left;
            int y = area.Top;

            this.Bounds = new Rectangle(x, y, w, h);
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
            if (m.Msg == WM_NCHITTEST && !_isMenuOpen && !(pnlKeyList != null && pnlKeyList.Visible))
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

            // キー入力一覧パネル
            pnlKeyList = new Panel();
            pnlKeyList.BackColor = Color.FromArgb(220, 30, 30, 30);
            pnlKeyList.Visible = false;
            this.Controls.Add(pnlKeyList);

            // キー入力一覧ラベル
            lblKeyList = new RotatableLabel();
            lblKeyList.Text = "--- Keyboard Mapping ---\n\n" +
                              "Move: W/A/S/D or Arrows\n" +
                              "A: Z   B: X   X: V   Y: C\n" +
                              "L: Q   R: E   ZL: 1  ZR: 3\n" +
                              "L+R: 2   L3: F   R3: 4\n" +
                              "-: M   +: N\n" +
                              "Home: H   Capture: P\n" +
                              "Menu: Tab";
            lblKeyList.ForeColor = Color.White;
            lblKeyList.BackColor = Color.Transparent;
            lblKeyList.TextAlign = ContentAlignment.MiddleCenter;
            if (pnlKeyList != null) pnlKeyList.Controls.Add(lblKeyList);

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
                // 文字色を黒に設定
                b.ForeColor = Color.Black;
                parent.Controls.Add(b);
                return b;
            }

            // ゲームボタン生成
            RotatableButton MkGame(string txt, string cmd, bool isRepeat, Color? bg = null)
            {
                RotatableButton b = new RotatableButton();
                b.Text = txt;
                b.BackColor = bg ?? Color.White;
                b.FlatStyle = FlatStyle.Flat;
                b.FlatAppearance.BorderSize = 0;
                b.TabStop = false;
                b.Opacity = 1.0f;
                b.ForeColor = Color.Black;

                b.MouseDown += (s, e) => StartInput(cmd);
                b.MouseUp += (s, e) => StopInput();

                this.Controls.Add(b);
                return b;
            }

            // --- メイン画面 ---

            // 再表示用ボタン (初期は非表示)
            btnShowController = MkBtn(this, "Show GUI", Color.LightYellow, (s, e) => SetControllerVisibility(true));
            btnShowController.Visible = false;

            // メイン画面にあったbtnCustomは削除。

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

            // メニュー内
            mBtnObs = MkBtn(pnlMenu, "OBS", Color.LightSkyBlue, (s, e) => { ControlApp("obs", true); ToggleMenu(); });
            mBtnHide = MkBtn(pnlMenu, "Hide OBS", Color.LightGray, (s, e) => { ControlApp("obs", false); ToggleMenu(); });

            mBtnFull = MkBtn(pnlMenu, "Full / Win", Color.LightCoral, (s, e) => { ToggleMaximize(); });
            mBtnOp = MkBtn(pnlMenu, $"Op: {(int)(_targetOpacity * 100)}%", Color.White, (s, e) => { ToggleOpacity(); });

            // 左右寄せボタン
            mBtnLeft = MkBtn(pnlMenu, "← Snap", Color.LightSeaGreen, (s, e) => { SetSnapLayout(false); ToggleMenu(); });
            mBtnRight = MkBtn(pnlMenu, "Snap →", Color.LightSeaGreen, (s, e) => { SetSnapLayout(true); ToggleMenu(); });

            // Custom切り替えボタン（メニュー内）
            mBtnCustom = MkBtn(pnlMenu, _showCustomButtons ? "Custom: ON" : "Custom: OFF",
                                    _showCustomButtons ? Color.Gold : Color.Wheat,
                                    (s, e) => ToggleCustomButtons());

            // GUI非表示ボタン
            mBtnHideController = MkBtn(pnlMenu, "Hide GUI", Color.Plum, (s, e) => SetControllerVisibility(false));

            // キー入力一覧ボタン
            mBtnKeys = MkBtn(pnlMenu, "Keys", Color.LightGoldenrodYellow, (s, e) => { ToggleKeyList(); });

            mBtnClose = MkBtn(pnlMenu, "Close Menu", Color.Silver, (s, e) => ToggleMenu());

            // キー一覧閉じるボタン
            if (pnlKeyList != null)
                btnKeyListClose = MkBtn(pnlKeyList, "Close", Color.Silver, (s, e) => ToggleKeyList());

            // Customボタンの初期状態（OFF）を反映して不要なボタンを隠す
            ApplyCustomButtonVisibility();
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
            // キー一覧パネル
            if (pnlKeyList != null)
            {
                pnlKeyList.Bounds = new Rectangle(innerX, innerY, innerW, H - pad * 2);
                if (pnlKeyList.Visible)
                {
                    int kw = pnlKeyList.Width;
                    int kh = pnlKeyList.Height;
                    int btnH = 40;
                    int kMargin = 10;
                    if (btnKeyListClose != null)
                        btnKeyListClose.Bounds = new Rectangle(kMargin, kh - btnH - kMargin, kw - kMargin * 2, btnH);
                    if (lblKeyList != null)
                    {
                        lblKeyList.Bounds = new Rectangle(kMargin, kMargin, kw - kMargin * 2, kh - btnH - kMargin * 3);
                        float fSize = Math.Max(12, Math.Min(kw, kh) / 25);
                        lblKeyList.Font = new Font("MS Gothic", fSize, FontStyle.Bold);
                    }
                }
            }

            if (_isMenuOpen) UpdateMenuLayout(innerW, H - pad * 2);

            int contentY = innerY + topBarH + margin;
            int contentH = H - pad - contentY;

            var rects = new System.Collections.Generic.Dictionary<Control, Rectangle>();

            if (lblTitle != null) rects[lblTitle] = new Rectangle(innerX, innerY, innerW, topBarH);

            // コントローラー非表示時の「Show GUI」ボタン配置
            if (_isControllerHidden && btnShowController != null)
            {
                int showW = 120;
                int showH = 40;
                rects[btnShowController] = new Rectangle(
                    innerX + innerW - showW - margin,  // 右寄せ
                    innerY + topBarH + margin,         // タイトルバーの下
                    showW,
                    showH
                );
            }

            int btnBaseSize = 30;

            if (!_isControllerHidden)
            {
                // スナップモードまたは縦回転時のレイアウト
                if (_isSnapMode || _rotationAngle == 90 || _rotationAngle == 270)
                {
                    int rowH = contentH / 14;
                    int y = contentY;

                    int topRowH = Math.Max(30, rowH);

                    // Customボタンを削除したため、HoldとMenuの2つで配置する
                    int halfW = (innerW - margin) / 2;
                    if (btnHoldToggle != null) rects[btnHoldToggle] = new Rectangle(innerX, y, halfW, topRowH);
                    if (btnMenu != null) rects[btnMenu] = new Rectangle(innerX + halfW + margin, y, halfW, topRowH);

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

                    if (_isSnapMode)
                    {
                        // スナップモード: 十字キーの隙間にXYABを埋め込む
                        // 隙間配置: 左上(Y), 右上(X), 左下(B), 右下(A)
                        SetGridRects(rects, centerX, centerY, b,
                            btnY, btnUp, btnX,
                            btnLeft, btnRight,
                            btnB, btnDown, btnA);

                        // 本来のABXYボタン群は画面外へ（非表示）
                        if (btnXY != null) btnXY.Visible = false;
                        if (btnXA != null) btnXA.Visible = false;
                        if (btnAB != null) btnAB.Visible = false;
                        if (btnYB != null) btnYB.Visible = false;

                        // X,Y,A,BボタンはGridで指定された場所に表示されるためVisible=trueが必要
                        Control?[] abxy = { btnX, btnY, btnA, btnB };
                        foreach (var btn in abxy) if (btn != null) btn.Visible = true;
                    }
                    else
                    {
                        // 通常の縦持ちモード
                        SetGridRects(rects, centerX, centerY, b,
                            btnUpLeft, btnUp, btnUpRight, btnLeft, btnRight, btnDownLeft, btnDown, btnDownRight);
                    }
                    y += dpadH + margin;

                    int sysH = Math.Max(30, rowH);
                    int sysW = (innerW - margin * 3) / 4;
                    if (btnMinus != null) rects[btnMinus] = new Rectangle(innerX, y, sysW, sysH);
                    if (btnHome != null) rects[btnHome] = new Rectangle(innerX + sysW + margin, y, sysW, sysH);
                    if (btnCap != null) rects[btnCap] = new Rectangle(innerX + (sysW + margin) * 2, y, sysW, sysH);
                    if (btnPlus != null) rects[btnPlus] = new Rectangle(innerX + (sysW + margin) * 3, y, sysW, sysH);
                    y += sysH + margin;

                    if (!_isSnapMode)
                    {
                        // スナップモードでなければABXYを描画
                        int abxyH = dpadH;
                        centerY = y + abxyH / 2;
                        SetGridRects(rects, centerX, centerY, b,
                            btnXY, btnX, btnXA, btnY, btnA, btnYB, btnB, btnAB);

                        // ABXYボタンを表示状態にする
                        Control?[] abxy = { btnX, btnY, btnA, btnB };
                        foreach (var btn in abxy) if (btn != null) btn.Visible = true;
                    }
                }
                else
                {
                    // 横持ちモード（通常レイアウト）
                    // ABXYボタンを表示状態にする
                    Control?[] abxy = { btnX, btnY, btnA, btnB };
                    foreach (var btn in abxy) if (btn != null) btn.Visible = true;

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

                    // Customボタン削除に伴い、HoldとMenuの2つに分割
                    int btnW = (innerW - margin) / 2;
                    if (btnHoldToggle != null) rects[btnHoldToggle] = new Rectangle(innerX, yTop, btnW, topH);
                    if (btnMenu != null) rects[btnMenu] = new Rectangle(innerX + btnW + margin, yTop, btnW, topH);

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
            }

            float fontSize = Math.Max(8, btnBaseSize / 2.5f);
            Font fontMain = new Font("Arial", fontSize, FontStyle.Bold);
            Font fontSub = new Font("Arial", Math.Max(8, fontSize * 0.8f), FontStyle.Bold);

            foreach (var kvp in rects)
            {
                Control c = kvp.Key;
                // Null免除
                if (c == null) continue;

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

            // 画面幅400px未満を「狭いモード」と判定し、ボタンの文字を短縮する
            bool narrow = w < 400;

            // ボタンラベルの切り替え (短縮形 vs フル名称)
            if (mBtnObs != null) mBtnObs.Text = narrow ? "O" : "OBS";
            if (mBtnHide != null) mBtnHide.Text = narrow ? "H" : "Hide OBS";
            if (mBtnFull != null) mBtnFull.Text = narrow ? "F" : "Full / Win";
            if (mBtnOp != null) mBtnOp.Text = narrow ? $"{(int)(_targetOpacity * 100)}%" : $"Op: {(int)(_targetOpacity * 100)}%";

            if (mBtnLeft != null) mBtnLeft.Text = "←";
            if (mBtnRight != null) mBtnRight.Text = "→";

            // Customボタンのラベル
            if (mBtnCustom != null)
            {
                if (narrow) mBtnCustom.Text = _showCustomButtons ? "C:ON" : "C:OFF";
                else mBtnCustom.Text = _showCustomButtons ? "Custom: ON" : "Custom: OFF";
            }

            if (mBtnHideController != null) mBtnHideController.Text = narrow ? "G" : "Hide GUI";
            if (mBtnKeys != null) mBtnKeys.Text = narrow ? "K" : "Keys";
            if (mBtnClose != null) mBtnClose.Text = narrow ? "×" : "Close Menu";

            // 文字を短くしたので、狭い画面でも4列レイアウトを維持できる
            int cols = 4;

            Control?[] menuBtns = {
                mBtnObs, mBtnHide, mBtnFull, mBtnOp,
                mBtnLeft, mBtnRight, mBtnCustom, mBtnKeys,
                mBtnHideController, mBtnClose, null, null
            };

            // 行数を要素数に合わせて自動計算
            int rows = (int)Math.Ceiling((double)menuBtns.Length / cols);

            int margin = 10;
            int btnW = (w - margin * (cols + 1)) / cols;
            int btnH = (h - margin * (rows + 1)) / rows;

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

                float fSize = Math.Max(10, Math.Min(btnW, btnH) / 6);
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

            GraphicsPath path = new GraphicsPath();

            if (Math.Abs(w - h) < 5) // 正円に近い場合
            {
                int d = Math.Min(w, h);
                path.AddEllipse((w - d) / 2, (h - d) / 2, d, d);
            }
            else if (w > h) // 横長 (Horizontal Capsule)
            {
                int d = h;
                path.AddArc(0, 0, d, d, 90, 180);
                path.AddArc(w - d, 0, d, d, 270, 180);
            }
            else // 縦長 (Vertical Capsule)
            {
                int d = w;
                path.AddArc(0, 0, d, d, 180, 180); // 上半円
                path.AddArc(0, h - d, d, d, 0, 180); // 下半円
            }

            path.CloseFigure();
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