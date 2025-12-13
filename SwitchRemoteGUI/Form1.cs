using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO.Ports;
using System.Windows.Forms;
using System.Runtime.InteropServices;
#nullable disable

namespace SwitchRemoteGUI
{
    public partial class Form1 : Form
    {
        // ★設定必須項目：ポート番号
        string portName = "COM5";

        SerialPort? port;

        // --- Windows API ---
        [DllImport("user32.dll")] public static extern int SendMessage(IntPtr hWnd, int Msg, int wParam, int lParam);
        [DllImport("user32.dll")] public static extern bool ReleaseCapture();

        // 状態管理
        bool _isReady = false;
        bool _isSolidBlack = false;

        // ★回転角度 (0, 90, 180, 270)
        int _rotationAngle = 0;

        // 連射・送信制御用
        System.Windows.Forms.Timer _repeatTimer;
        string _repeatingCmd = "";
        DateTime _lastSendTime = DateTime.MinValue;
        private const int SEND_INTERVAL = 400;

        // UIパーツ (自作の回転対応クラス)
        RotatableButton? btnLayoutToggle;
        RotatableButton? btnBgToggle;
        RotatableLabel? lblTitle;

        RotatableButton? btnZL, btnL, btnLR, btnR, btnZR;
        RotatableButton? btnUp, btnLeft, btnDown, btnRight;
        RotatableButton? btnX, btnY, btnB, btnA;
        RotatableButton? btnMinus, btnHome, btnCap, btnPlus;
        RotatableButton? btnL3, btnR3;

        private int borderSize = 5;

        // 横モード時の基準サイズ
        private const int BASE_W = 800;
        private const int BASE_H = 400;

        public Form1()
        {
            InitializeComponent();
            this.TopMost = true;
            this.FormBorderStyle = FormBorderStyle.None;
            this.MinimumSize = new Size(300, 300);

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

            // 初期サイズ
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
                double msSinceLast = (DateTime.Now - _lastSendTime).TotalMilliseconds;
                if (!force && msSinceLast < SEND_INTERVAL) return;

                try
                {
                    port.DiscardOutBuffer();
                    port.Write(cmd);
                    _lastSendTime = DateTime.Now;
                }
                catch { }
            }
        }

        void ClearBuffer()
        {
            if (port != null && port.IsOpen) try { port.DiscardOutBuffer(); } catch { }
        }

        // ★★★ 90度ずつ回転させる関数 ★★★
        void RotateLayout()
        {
            // 90度ずつ加算 (0 -> 90 -> 180 -> 270 -> 0)
            _rotationAngle = (_rotationAngle + 90) % 360;

            if (_rotationAngle == 90 || _rotationAngle == 270)
            {
                // 縦長モード
                this.Size = new Size(BASE_H, BASE_W);
            }
            else
            {
                // 横長モード
                this.Size = new Size(BASE_W, BASE_H);
            }

            // ボタンのテキスト更新
            if (btnLayoutToggle != null) btnLayoutToggle.Text = $"↻ {_rotationAngle}°";

            UpdateLayout();
        }

        void ToggleBlackMode()
        {
            if (_isSolidBlack)
            {
                SetTransparentMode();
                if (btnBgToggle != null) btnBgToggle.Text = "⚫ 透過";
            }
            else
            {
                SetSolidBlackMode();
                if (btnBgToggle != null) btnBgToggle.Text = "⚫ 黒";
            }
            if (btnBgToggle != null)
                btnBgToggle.BackColor = _isSolidBlack ? Color.FromArgb(100, 100, 100) : Color.FromArgb(70, 70, 70);

            this.TopMost = true;
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

            RotatableButton MkGameBtn(string txt, string cmd, bool isRepeat, Color? bg = null)
            {
                RotatableButton b = new RotatableButton();
                b.Text = txt;
                b.BackColor = bg ?? Color.White;
                b.FlatStyle = FlatStyle.Flat;
                b.FlatAppearance.BorderSize = 0;
                b.TabStop = false;

                b.MouseDown += (s, e) => {
                    if (isRepeat)
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
                    if (isRepeat)
                    {
                        _repeatTimer.Stop();
                        _repeatingCmd = "";
                    }
                    ClearBuffer();
                };

                this.Controls.Add(b);
                return b;
            }

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
            // ★ 文字表記に変更
            btnHome = MkGameBtn("Home", "h", false, Color.LightBlue);
            btnCap = MkGameBtn("Capture", "c", false, Color.Pink);

            btnPlus = MkGameBtn("+", "n", false, Color.LightGray);

            btnL3 = MkGameBtn("L3", "3", false, Color.Silver);
            btnR3 = MkGameBtn("R3", "4", false, Color.Silver);
        }

        // ★★★ レイアウト計算（360度回転対応） ★★★
        void UpdateLayout()
        {
            if (!_isReady) return;

            // 1. まず「0度（横モード 800x400）」の基準配置を計算
            int W = BASE_W;
            int H = BASE_H;
            int pad = borderSize;
            int innerW = W - pad * 2;
            int innerX = pad;
            int innerY = pad;
            int topBarH = 24;
            int margin = 5;
            int stdH = 35;
            int contentY = innerY + topBarH + margin;

            var rects = new System.Collections.Generic.Dictionary<Control, Rectangle>();

            // (A) トップバー
            int toggleW = (innerW - margin) / 4;
            if (btnBgToggle != null) rects[btnBgToggle] = new Rectangle(innerX + innerW - toggleW, innerY, toggleW, topBarH);
            if (btnLayoutToggle != null) rects[btnLayoutToggle] = new Rectangle(innerX + innerW - toggleW * 2 - margin, innerY, toggleW, topBarH);
            if (lblTitle != null) rects[lblTitle] = new Rectangle(innerX, innerY, innerW - toggleW * 2 - margin * 2, topBarH);

            // (B) ショルダー
            int ySh = contentY;
            int shW = (innerW - margin * 4) / 5;
            if (btnZL != null) rects[btnZL] = new Rectangle(innerX, ySh, shW, stdH);
            if (btnL != null) rects[btnL] = new Rectangle(innerX + shW + margin, ySh, shW, stdH);
            if (btnLR != null) rects[btnLR] = new Rectangle(innerX + (shW + margin) * 2, ySh, shW, stdH);
            if (btnR != null) rects[btnR] = new Rectangle(innerX + (shW + margin) * 3, ySh, shW, stdH);
            if (btnZR != null) rects[btnZR] = new Rectangle(innerX + (shW + margin) * 4, ySh, shW, stdH);

            // (D) スティック
            int yStick = H - pad - stdH;
            int stickW = (innerW - margin) / 2;
            if (btnL3 != null) rects[btnL3] = new Rectangle(innerX, yStick, stickW, stdH);
            if (btnR3 != null) rects[btnR3] = new Rectangle(innerX + stickW + margin, yStick, stickW, stdH);

            // (C) システム
            int ySys = yStick - margin - stdH;
            int sysW = (innerW - margin * 3) / 4;
            if (btnMinus != null) rects[btnMinus] = new Rectangle(innerX, ySys, sysW, stdH);
            if (btnHome != null) rects[btnHome] = new Rectangle(innerX + sysW + margin, ySys, sysW, stdH);
            if (btnCap != null) rects[btnCap] = new Rectangle(innerX + (sysW + margin) * 2, ySys, sysW, stdH);
            if (btnPlus != null) rects[btnPlus] = new Rectangle(innerX + (sysW + margin) * 3, ySys, sysW, stdH);

            // (E) メインエリア
            int yMainTop = ySh + stdH + margin;
            int yMainBottom = ySys - margin;
            int mainH = yMainBottom - yMainTop;
            int btnSize = Math.Min((innerW / 2 - margin) / 3, mainH / 3);
            btnSize = Math.Max(20, btnSize);

            // 左手
            int leftCenterX = innerX + innerW / 4;
            int mainCenterY = yMainTop + mainH / 2;
            if (btnUp != null) rects[btnUp] = new Rectangle(leftCenterX - btnSize / 2, mainCenterY - btnSize / 2 - btnSize, btnSize, btnSize);
            if (btnLeft != null) rects[btnLeft] = new Rectangle(leftCenterX - btnSize / 2 - btnSize, mainCenterY - btnSize / 2, btnSize, btnSize);
            if (btnRight != null) rects[btnRight] = new Rectangle(leftCenterX - btnSize / 2 + btnSize, mainCenterY - btnSize / 2, btnSize, btnSize);
            if (btnDown != null) rects[btnDown] = new Rectangle(leftCenterX - btnSize / 2, mainCenterY - btnSize / 2 + btnSize, btnSize, btnSize);

            // 右手
            int rightCenterX = innerX + innerW * 3 / 4;
            if (btnX != null) rects[btnX] = new Rectangle(rightCenterX - btnSize / 2, mainCenterY - btnSize / 2 - btnSize, btnSize, btnSize);
            if (btnY != null) rects[btnY] = new Rectangle(rightCenterX - btnSize / 2 - btnSize, mainCenterY - btnSize / 2, btnSize, btnSize);
            if (btnA != null) rects[btnA] = new Rectangle(rightCenterX - btnSize / 2 + btnSize, mainCenterY - btnSize / 2, btnSize, btnSize);
            if (btnB != null) rects[btnB] = new Rectangle(rightCenterX - btnSize / 2, mainCenterY - btnSize / 2 + btnSize, btnSize, btnSize);

            // ----------------------------------------------------
            // 2. 角度に応じた座標変換
            // ----------------------------------------------------
            Font fontMain = new Font("Arial", Math.Max(10, btnSize / 3), FontStyle.Bold);
            Font fontSub = new Font("Arial", 9, FontStyle.Bold);

            foreach (var kvp in rects)
            {
                Control c = kvp.Key;
                Rectangle r = kvp.Value;
                Rectangle newRect = r;

                // 回転情報をセット（文字回転用）
                if (c is RotatableButton rb) rb.RotationAngle = _rotationAngle;
                if (c is RotatableLabel rl) rl.RotationAngle = _rotationAngle;

                switch (_rotationAngle)
                {
                    case 90:
                        // 時計回り90度: (x, y) -> (BASE_H - y - h, x)
                        newRect = new Rectangle(BASE_H - r.Y - r.Height, r.X, r.Height, r.Width);
                        break;
                    case 180:
                        // 180度: (x, y) -> (BASE_W - x - w, BASE_H - y - h)
                        newRect = new Rectangle(BASE_W - r.X - r.Width, BASE_H - r.Y - r.Height, r.Width, r.Height);
                        break;
                    case 270:
                        // 時計回り270度: (x, y) -> (y, BASE_W - x - w)
                        newRect = new Rectangle(r.Y, BASE_W - r.X - r.Width, r.Height, r.Width);
                        break;
                    default: // 0度
                        newRect = r;
                        break;
                }

                c.Bounds = newRect;

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
    }

    // ★★★ 文字回転対応ボタン ★★★
    public class RotatableButton : Button
    {
        public int RotationAngle { get; set; } = 0;

        protected override void OnPaint(PaintEventArgs pevent)
        {
            Graphics g = pevent.Graphics;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;

            using (Brush bgBrush = new SolidBrush(this.BackColor))
            {
                g.FillRectangle(bgBrush, this.ClientRectangle);
            }

            if (!string.IsNullOrEmpty(this.Text))
            {
                StringFormat sf = new StringFormat();
                sf.Alignment = StringAlignment.Center;
                sf.LineAlignment = StringAlignment.Center;

                using (Brush textBrush = new SolidBrush(this.ForeColor))
                {
                    if (RotationAngle != 0)
                    {
                        // 中心を基準に回転 (時計回り)
                        g.TranslateTransform(this.Width / 2, this.Height / 2);
                        g.RotateTransform(RotationAngle);
                        g.DrawString(this.Text, this.Font, textBrush, 0, 0, sf);
                        g.ResetTransform();
                    }
                    else
                    {
                        g.DrawString(this.Text, this.Font, textBrush, this.ClientRectangle, sf);
                    }
                }
            }
        }
    }

    // ★★★ 文字回転対応ラベル ★★★
    public class RotatableLabel : Label
    {
        public int RotationAngle { get; set; } = 0;

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;

            using (Brush bgBrush = new SolidBrush(this.BackColor))
            {
                g.FillRectangle(bgBrush, this.ClientRectangle);
            }

            if (!string.IsNullOrEmpty(this.Text))
            {
                StringFormat sf = new StringFormat();
                sf.Alignment = StringAlignment.Center;
                sf.LineAlignment = StringAlignment.Center;

                using (Brush textBrush = new SolidBrush(this.ForeColor))
                {
                    if (RotationAngle != 0)
                    {
                        g.TranslateTransform(this.Width / 2, this.Height / 2);
                        g.RotateTransform(RotationAngle);
                        g.DrawString(this.Text, this.Font, textBrush, 0, 0, sf);
                        g.ResetTransform();
                    }
                    else
                    {
                        g.DrawString(this.Text, this.Font, textBrush, this.ClientRectangle, sf);
                    }
                }
            }
        }
    }
}