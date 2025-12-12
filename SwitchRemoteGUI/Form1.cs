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

        // Windows API (画面操作用)
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern bool ShowWindowAsync(IntPtr hWnd, int nCmdShow);
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hWnd);
        private const int SW_RESTORE = 9;
        private const int SW_MINIMIZE = 6;

        // --- 安全装置：すべての準備が整ったかどうか ---
        bool _isReady = false;

        // --- ボタンの参照を保存しておく変数 ---
        Button btnObsShow, btnObsHide;
        Button btnZL, btnL, btnR, btnZR;
        Button btnUp, btnLeft, btnDown, btnRight; // 十字キー
        Button btnX, btnY, btnB, btnA;            // ABXY
        Button btnMinus, btnHome, btnCap, btnPlus;
        Button btnL3, btnR3;

        public Form1()
        {
            InitializeComponent();
            this.TopMost = true; // 常に最前面
            this.MinimumSize = new Size(300, 500); // これ以上小さくならないサイズ
            this.BackColor = Color.FromArgb(30, 30, 30);
            this.KeyPreview = true;
            this.KeyDown += Form1_KeyDown;

            ConnectPort();
            CreateButtons(); // ボタンを作る

            // ★全てのボタンを作り終わったので、ここで「準備完了」にする
            _isReady = true;

            // 最後に一回、手動で整列させる
            UpdateLayout();
        }

        // ウィンドウのサイズが変わるたびに呼ばれる関数
        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);
            UpdateLayout(); // サイズに合わせてボタンを並べ直す
        }

        void ConnectPort()
        {
            try
            {
                port = new SerialPort(portName, 9600);
                port.Open();
                this.Text = "Switch Resizable";
            }
            catch (Exception ex) { /* 接続エラーは一旦無視 */ }
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

        // --- 1. ボタンを作成する関数 ---
        void CreateButtons()
        {
            // ヘルパー関数
            Button MkBtn(string txt, string cmd, Color? bg = null)
            {
                Button b = new Button();
                b.Text = txt;
                b.BackColor = bg ?? Color.White;
                b.FlatStyle = FlatStyle.Flat;
                b.TabStop = false; // フォーカスを奪わない
                if (cmd != "") b.Click += (s, e) => Send(cmd);
                this.Controls.Add(b);
                return b;
            }

            // OBS操作
            btnObsShow = MkBtn("📺 戻す", "", Color.LightSkyBlue);
            btnObsShow.Click += (s, e) => ControlApp("obs", true);
            btnObsHide = MkBtn("＿ 隠す", "", Color.LightGray);
            btnObsHide.Click += (s, e) => ControlApp("obs", false);

            // ショルダー
            btnZL = MkBtn("ZL", "e", Color.DarkGray);
            btnL = MkBtn("L", "q", Color.Gray);
            btnR = MkBtn("R", "w", Color.Gray);
            btnZR = MkBtn("ZR", "r", Color.DarkGray);

            // 十字キー
            btnUp = MkBtn("↑", "I");
            btnLeft = MkBtn("←", "J");
            btnDown = MkBtn("↓", "K");
            btnRight = MkBtn("→", "L");

            // ABXY
            btnX = MkBtn("X", "s", Color.Yellow);
            btnY = MkBtn("Y", "a", Color.LightGreen);
            btnB = MkBtn("B", "x", Color.Red);
            btnA = MkBtn("A", "z", Color.Cyan);

            // システム
            btnMinus = MkBtn("-", "m", Color.LightGray);
            btnHome = MkBtn("🏠", "h", Color.LightBlue);
            btnCap = MkBtn("📷", "c", Color.Pink);
            btnPlus = MkBtn("+", "n", Color.LightGray);

            // スティック押し込み
            btnL3 = MkBtn("L3", "3", Color.Silver);
            btnR3 = MkBtn("R3", "4", Color.Silver);
        }

        // --- 2. 画面サイズに合わせて位置と大きさを計算する関数 ---
        void UpdateLayout()
        {
            // 【★最強の安全装置】
            // 全部のボタンを作り終わる(_isReadyがtrueになる)までは、何もしないで帰る！
            if (!_isReady) return;

            int W = this.ClientSize.Width;
            int H = this.ClientSize.Height;

            int baseSize = Math.Min(W / 6, H / 12);
            int fontSz = Math.Max(8, baseSize / 3);

            foreach (Control c in this.Controls) { if (c is Button) c.Font = new Font("Arial", fontSz, FontStyle.Bold); }

            int margin = baseSize / 4;

            // --- 配置計算 (上から順に) ---

            // 1. OBSボタン
            int obsH = baseSize / 2 + 10;
            btnObsShow.Bounds = new Rectangle(margin, margin, (W / 2) - margin * 2, obsH);
            btnObsHide.Bounds = new Rectangle(W / 2 + margin, margin, (W / 2) - margin * 2, obsH);

            // 2. ショルダーボタン
            int shY = margin * 2 + obsH;
            int shW = (W - margin * 5) / 4;
            int shH = baseSize / 2 + 10;

            btnZL.Bounds = new Rectangle(margin, shY, shW, shH);
            btnL.Bounds = new Rectangle(margin * 2 + shW, shY, shW, shH);
            btnR.Bounds = new Rectangle(margin * 3 + shW * 2, shY, shW, shH);
            btnZR.Bounds = new Rectangle(margin * 4 + shW * 3, shY, shW, shH);

            // 3. メインエリア
            int mainY = shY + shH + margin * 2;
            int btnSize = baseSize;
            int leftCenterX = W / 4;
            int rightCenterX = W * 3 / 4;

            // 十字キー
            btnUp.Bounds = new Rectangle(leftCenterX - btnSize / 2, mainY, btnSize, btnSize);
            btnLeft.Bounds = new Rectangle(leftCenterX - btnSize / 2 - btnSize, mainY + btnSize, btnSize, btnSize);
            btnDown.Bounds = new Rectangle(leftCenterX - btnSize / 2, mainY + btnSize, btnSize, btnSize);
            btnRight.Bounds = new Rectangle(leftCenterX - btnSize / 2 + btnSize, mainY + btnSize, btnSize, btnSize);
            btnDown.Location = new Point(leftCenterX - btnSize / 2, mainY + btnSize * 2);
            btnLeft.Location = new Point(leftCenterX - btnSize / 2 - btnSize, mainY + btnSize);
            btnRight.Location = new Point(leftCenterX - btnSize / 2 + btnSize, mainY + btnSize);

            // ABXY
            btnX.Bounds = new Rectangle(rightCenterX - btnSize / 2, mainY, btnSize, btnSize);
            btnA.Bounds = new Rectangle(rightCenterX - btnSize / 2 + btnSize, mainY + btnSize, btnSize, btnSize);
            btnB.Bounds = new Rectangle(rightCenterX - btnSize / 2, mainY + btnSize * 2, btnSize, btnSize);
            btnY.Bounds = new Rectangle(rightCenterX - btnSize / 2 - btnSize, mainY + btnSize, btnSize, btnSize);

            // 4. システムボタン
            int sysY = mainY + btnSize * 3 + margin * 2;
            int sysW = (W - margin * 5) / 4;
            int sysH = baseSize / 2 + 10;

            btnMinus.Bounds = new Rectangle(margin, sysY, sysW, sysH);
            btnHome.Bounds = new Rectangle(margin * 2 + sysW, sysY, sysW, sysH);
            btnCap.Bounds = new Rectangle(margin * 3 + sysW * 2, sysY, sysW, sysH);
            btnPlus.Bounds = new Rectangle(margin * 4 + sysW * 3, sysY, sysW, sysH);

            // 5. スティック押し込み
            int stickY = sysY + sysH + margin * 2;
            int stickW = W / 3;

            btnL3.Bounds = new Rectangle(margin, stickY, stickW, sysH * 2);
            btnR3.Bounds = new Rectangle(W - stickW - margin, stickY, stickW, sysH * 2);
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