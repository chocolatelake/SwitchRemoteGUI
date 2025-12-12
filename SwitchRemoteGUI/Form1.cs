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

        // Windows API
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern bool ShowWindowAsync(IntPtr hWnd, int nCmdShow);
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hWnd);
        private const int SW_RESTORE = 9;
        private const int SW_MINIMIZE = 6;

        public Form1()
        {
            InitializeComponent();
            this.TopMost = true;
            SetupLayout();
            ConnectPort();
        }

        void ConnectPort()
        {
            try
            {
                port = new SerialPort(portName, 9600);
                port.Open();
                this.Text = "Switch Pad";
            }
            catch (Exception ex) { MessageBox.Show("接続エラー: " + ex.Message); }
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

        void SetupLayout()
        {
            this.Size = new Size(450, 650); // 横幅はキープ、縦は少しコンパクトに
            this.BackColor = Color.FromArgb(30, 30, 30);
            this.KeyPreview = true;
            this.KeyDown += Form1_KeyDown;

            // ボタン生成ヘルパー
            Button CreateBtn(string text, int x, int y, string cmd, int w = 60, int h = 60, Color? bg = null)
            {
                Button b = new Button();
                b.Text = text;
                b.Location = new Point(x, y);
                b.Size = new Size(w, h);
                b.Font = new Font("Arial", 11, FontStyle.Bold);
                b.BackColor = bg ?? Color.White;
                b.FlatStyle = FlatStyle.Flat;
                if (cmd != "") b.Click += (s, e) => Send(cmd);
                b.TabStop = false;
                this.Controls.Add(b);
                return b;
            }

            // --- 1. OBS操作 (最上部) ---
            CreateBtn("📺 戻す", 20, 10, "", 190, 40, Color.LightSkyBlue)
                .Click += (s, e) => ControlApp("obs", true);
            CreateBtn("＿ 隠す", 220, 10, "", 190, 40, Color.LightGray)
                .Click += (s, e) => ControlApp("obs", false);

            // 基準位置（コントローラー部分の開始Y座標）
            int startY = 80;

            // --- 2. ショルダーボタン (L/ZL, R/ZR) ---
            // 左側
            CreateBtn("ZL", 20, startY, "e", 80, 45, Color.DarkGray);
            CreateBtn("L", 110, startY, "q", 80, 45, Color.Gray);
            // 右側
            CreateBtn("R", 240, startY, "w", 80, 45, Color.Gray);
            CreateBtn("ZR", 330, startY, "r", 80, 45, Color.DarkGray);

            // --- 3. メインエリア (十字キーとABXYを同じ高さに！) ---
            int mainY = startY + 60;
            int btnSize = 55; // 少し小さめにして配置しやすく

            // 【左側】十字キーエリア
            int padX = 30;
            CreateBtn("↑", padX + btnSize, mainY, "I", btnSize, btnSize);
            CreateBtn("←", padX, mainY + btnSize, "J", btnSize, btnSize);
            CreateBtn("↓", padX + btnSize, mainY + btnSize, "K", btnSize, btnSize);
            CreateBtn("→", padX + btnSize * 2, mainY + btnSize, "L", btnSize, btnSize);

            // 【右側】ABXYエリア
            int abxyX = 250;
            CreateBtn("X", abxyX + btnSize, mainY, "s", btnSize, btnSize, Color.Yellow);
            CreateBtn("Y", abxyX, mainY + btnSize, "a", btnSize, btnSize, Color.LightGreen);
            CreateBtn("B", abxyX + btnSize, mainY + btnSize * 2, "x", btnSize, btnSize, Color.Red); // Bは下
            CreateBtn("A", abxyX + btnSize * 2, mainY + btnSize, "z", btnSize, btnSize, Color.Cyan); // Aは右

            // --- 4. システムボタン (中央配置) ---
            int sysY = mainY + btnSize * 3 + 20;
            CreateBtn("-", 100, sysY, "m", 50, 40, Color.LightGray);
            CreateBtn("🏠", 160, sysY, "h", 50, 40, Color.LightBlue);
            CreateBtn("📷", 220, sysY, "c", 50, 40, Color.Pink);
            CreateBtn("+", 280, sysY, "n", 50, 40, Color.LightGray);

            // --- 5. スティック押し込み (最下部) ---
            int stickY = sysY + 60;
            CreateBtn("L3", 80, stickY, "3", 100, 50, Color.Silver);
            CreateBtn("R3", 250, stickY, "4", 100, 50, Color.Silver);
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