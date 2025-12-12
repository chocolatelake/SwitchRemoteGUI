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

        // --- Windows操作用の呪文 ---
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern bool ShowWindowAsync(IntPtr hWnd, int nCmdShow);
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hWnd);
        private const int SW_RESTORE = 9;
        private const int SW_MINIMIZE = 6; // 最小化用

        public Form1()
        {
            InitializeComponent();
            this.TopMost = true; // 常に最前面
            SetupLayout();
            ConnectPort();
        }

        void ConnectPort()
        {
            try
            {
                port = new SerialPort(portName, 9600);
                port.Open();
                this.Text = "Switch Pro Controller";
            }
            catch (Exception ex) { MessageBox.Show("接続エラー: " + ex.Message); }
        }

        void Send(string cmd)
        {
            if (port != null && port.IsOpen) port.Write(cmd);
        }

        // 指定したアプリ(OBSなど)を操作する関数
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
                        // 表示・復元
                        if (IsIconic(p.MainWindowHandle)) ShowWindowAsync(p.MainWindowHandle, SW_RESTORE);
                        SetForegroundWindow(p.MainWindowHandle);
                    }
                    else
                    {
                        // 最小化（隠す）
                        ShowWindowAsync(p.MainWindowHandle, SW_MINIMIZE);
                    }
                    return;
                }
            }
        }

        void SetupLayout()
        {
            this.Size = new Size(600, 450); // 横長に変更
            this.BackColor = Color.FromArgb(40, 40, 40); // ダークモードっぽく
            this.KeyPreview = true;
            this.KeyDown += Form1_KeyDown;

            // ボタン生成関数 (色やフォントをかっこよく)
            Button CreateBtn(string text, int x, int y, string cmd, int w = 60, int h = 60, Color? bg = null)
            {
                Button b = new Button();
                b.Text = text;
                b.Location = new Point(x, y);
                b.Size = new Size(w, h);
                b.Font = new Font("Arial", 12, FontStyle.Bold);
                b.BackColor = bg ?? Color.White;
                b.FlatStyle = FlatStyle.Flat; // フラットデザイン
                if (cmd != "") b.Click += (s, e) => Send(cmd);
                b.TabStop = false;
                this.Controls.Add(b);
                return b;
            }

            // --- 上部ツールバー ---
            // OBSを表示
            Button btnShow = CreateBtn("📺 画面を戻す", 20, 10, "", 270, 40, Color.LightSkyBlue);
            btnShow.Click += (s, e) => ControlApp("obs", true); // "obs"を探して表示

            // OBSを隠す
            Button btnHide = CreateBtn("＿ 画面を隠す", 300, 10, "", 270, 40, Color.LightGray);
            btnHide.Click += (s, e) => ControlApp("obs", false); // "obs"を探して最小化

            // --- L/R系 (上段) ---
            CreateBtn("ZL", 30, 70, "e", 80, 40, Color.Gray);
            CreateBtn("L", 120, 70, "q", 80, 40, Color.Gray);

            CreateBtn("R", 390, 70, "w", 80, 40, Color.Gray);
            CreateBtn("ZR", 480, 70, "r", 80, 40, Color.Gray);

            // --- 左側 (十字キー) ---
            int dpadX = 80;
            int dpadY = 150;
            CreateBtn("↑", dpadX + 60, dpadY, "I", 60, 60);
            CreateBtn("←", dpadX, dpadY + 70, "J", 60, 60);
            CreateBtn("↓", dpadX + 60, dpadY + 70, "K", 60, 60);
            CreateBtn("→", dpadX + 120, dpadY + 70, "L", 60, 60);

            // --- 右側 (ABXY) ---
            int abxyX = 400;
            int abxyY = 150;
            CreateBtn("X", abxyX + 60, abxyY, "s", 60, 60);
            CreateBtn("Y", abxyX, abxyY + 70, "a", 60, 60);
            CreateBtn("B", abxyX + 60, abxyY + 140, "x", 60, 60); // Bは下
            CreateBtn("A", abxyX + 120, abxyY + 70, "z", 60, 60); // Aは右

            // --- 中央 (システムボタン) ---
            CreateBtn("-", 230, 150, "m", 40, 40, Color.LightGray);
            CreateBtn("+", 320, 150, "n", 40, 40, Color.LightGray);

            CreateBtn("🏠", 275, 220, "h", 40, 40, Color.LightBlue); // HOME
            CreateBtn("📷", 275, 280, "c", 40, 40, Color.Pink);      // Capture
        }

        private void Form1_KeyDown(object sender, KeyEventArgs e)
        {
            switch (e.KeyCode)
            {
                // ボタン
                case Keys.Z: Send("z"); break; // A
                case Keys.X: Send("x"); break; // B
                case Keys.S: Send("s"); break; // X
                case Keys.A: Send("a"); break; // Y
                case Keys.Q: Send("q"); break; // L
                case Keys.W: Send("w"); break; // R
                case Keys.E: Send("e"); break; // ZL
                case Keys.R: Send("r"); break; // ZR
                case Keys.Enter: Send("n"); break; // Plus
                case Keys.Back: Send("m"); break;  // Minus
                case Keys.Escape: Send("h"); break; // Home
                case Keys.C: Send("c"); break;      // Capture

                // 移動
                case Keys.I: Send("I"); break;
                case Keys.J: Send("J"); break;
                case Keys.K: Send("K"); break;
                case Keys.L: Send("L"); break;
            }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            if (port != null && port.IsOpen) port.Close();
            base.OnFormClosed(e);
        }
    }
}