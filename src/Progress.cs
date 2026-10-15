using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace NoAdZip
{
    // 사용자에게 보이는 메시지. 무인 모드(/silent)에서는 콘솔에 쓰고 묻지 않는다.
    static class Msg
    {
        public static bool Silent;
        public static bool Force;

        [DllImport("kernel32.dll")]
        static extern bool AttachConsole(int pid);

        public static void AttachConsoleIfSilent()
        {
            if (Silent) AttachConsole(-1);
        }

        static void Echo(string kind, string text)
        {
            try { Console.WriteLine("[" + kind + "] " + text); } catch { }
            Log.Write("[" + kind + "] " + text.Replace(Environment.NewLine, " | "));
        }

        public static void Info(IWin32Window o, string text)
        {
            if (Silent) { Echo("정보", text); return; }
            MessageBox.Show(o, Loc.T(text), "몽집", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        public static void Warn(IWin32Window o, string text)
        {
            if (Silent) { Echo("경고", text); return; }
            MessageBox.Show(o, Loc.T(text), "몽집", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        public static void Error(IWin32Window o, string text)
        {
            if (Silent) { Echo("오류", text); return; }
            MessageBox.Show(o, Loc.T(text), "몽집", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        // 위험을 감수하는 확인. 무인 모드에서는 /force 가 있을 때만 예.
        public static bool ConfirmRisk(IWin32Window o, string text)
        {
            if (Silent)
            {
                Echo(Force ? "확인(강제 진행)" : "확인(거부)", text);
                return Force;
            }
            return MessageBox.Show(o, text, "몽집", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) == DialogResult.Yes;
        }

        public static bool Confirm(IWin32Window o, string text)
        {
            if (Silent) { Echo("확인(예)", text); return true; }
            return MessageBox.Show(o, text, "몽집", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
        }
    }

    static class Jobs
    {
        // 0 = 없음, 1 = 알림음, 2 = PC 종료. 진행률 창에서 고르며 작업 전체가 성공으로 끝났을 때 실행한다.
        public static int AfterAction;

        public static void FinishAction(IWin32Window owner)
        {
            int a = AfterAction;
            AfterAction = 0;
            if (Msg.Silent || a == 0) return;
            if (a == 1) System.Media.SystemSounds.Asterisk.Play();
            else if (a == 2)
            {
                using (ShutdownForm f = new ShutdownForm())
                    if (f.ShowDialog(owner) == DialogResult.OK)
                        System.Diagnostics.Process.Start("shutdown", "/s /t 0");
            }
        }

        // 작업을 별도 스레드에서 돌리며 진행률 창을 모달로 보여 준다. 무인 모드에서는 그냥 실행한다.
        public static T Run<T>(IWin32Window owner, string title, Func<JobContext, T> job)
        {
            JobContext ctx = new JobContext();
            if (Msg.Silent)
            {
                ctx.OnProgress = delegate (int p, string s) { };
                return job(ctx);
            }

            T result = default(T);
            Exception error = null;
            using (ProgressForm f = new ProgressForm(title, ctx))
            {
                Thread t = null;
                f.Shown += delegate
                {
                    t = new Thread(delegate ()
                    {
                        try { result = job(ctx); }
                        catch (Exception ex) { error = ex; }
                        f.Finish();
                    });
                    t.IsBackground = true;
                    t.Start();
                };
                f.ShowDialog(owner);
                if (t != null) t.Join();
            }
            if (error != null)
            {
                if (error is OperationCanceledException || error is PasswordRequiredException || error is EngineException)
                    throw error;
                throw new EngineException(error.Message);
            }
            return result;
        }
    }

    class ProgressForm : DialogBase
    {
        readonly JobContext ctx;
        readonly Label lblFile;
        readonly Label lblTime;
        readonly ProgressBar bar;
        readonly Button btnCancel;
        readonly System.Windows.Forms.Timer timer;
        readonly DateTime start = DateTime.Now;
        readonly object lk = new object();
        int pct = -1;
        string file = "";
        bool done;

        public ProgressForm(string title, JobContext ctx)
        {
            this.ctx = ctx;
            Text = title;
            Font = SystemFonts.MessageBoxFont;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = true;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(480, 156);
            Program.ApplyIcon(this);

            Label lblTitle = new Label();
            lblTitle.Text = title;
            lblTitle.Font = new Font(Font, FontStyle.Bold);
            lblTitle.SetBounds(14, 12, 450, 20);
            Controls.Add(lblTitle);

            lblFile = new Label();
            lblFile.AutoEllipsis = true;
            lblFile.Text = " ";
            lblFile.SetBounds(14, 36, 450, 18);
            Controls.Add(lblFile);

            bar = new ProgressBar();
            bar.Style = ProgressBarStyle.Marquee;
            bar.SetBounds(14, 60, 450, 20);
            Controls.Add(bar);

            lblTime = new Label();
            lblTime.Text = " ";
            lblTime.SetBounds(14, 86, 450, 18);
            Controls.Add(lblTime);

            btnCancel = new Button();
            btnCancel.Text = "취소";
            btnCancel.SetBounds(384, 116, 80, 28);
            btnCancel.Click += delegate { RequestCancel(); };
            Controls.Add(btnCancel);

            Label lblAfter = new Label();
            lblAfter.Text = "작업이 끝나면";
            lblAfter.SetBounds(14, 122, 84, 18);
            Controls.Add(lblAfter);
            ComboBox cmbAfter = new ComboBox();
            cmbAfter.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbAfter.Items.AddRange(new object[] { "아무것도 안 함", "알림음 재생", "PC 종료" });
            cmbAfter.SelectedIndex = Jobs.AfterAction;
            cmbAfter.SetBounds(100, 118, 140, 23);
            cmbAfter.SelectedIndexChanged += delegate { Jobs.AfterAction = cmbAfter.SelectedIndex; };
            Controls.Add(cmbAfter);

            ctx.OnProgress = Post;
            timer = new System.Windows.Forms.Timer();
            timer.Interval = 150;
            timer.Tick += delegate { UpdateUi(); };
            timer.Start();
            FormClosing += delegate (object s, FormClosingEventArgs e)
            {
                if (!done) { e.Cancel = true; RequestCancel(); }
            };
        }

        void RequestCancel()
        {
            btnCancel.Enabled = false;
            btnCancel.Text = "취소 중";
            ctx.Token.Cancel();
        }

        void Post(int p, string f)
        {
            lock (lk)
            {
                if (p >= 0) pct = p;
                if (f != null) file = f;
            }
        }

        void UpdateUi()
        {
            int p;
            string f;
            lock (lk) { p = pct; f = file; }
            if (p >= 0)
            {
                if (bar.Style != ProgressBarStyle.Continuous) bar.Style = ProgressBarStyle.Continuous;
                bar.Value = Math.Min(100, p);
                TimeSpan el = DateTime.Now - start;
                string t = "경과 " + Util.FormatDuration(el);
                if (p >= 3 && p < 100)
                {
                    TimeSpan rem = TimeSpan.FromSeconds(el.TotalSeconds * (100 - p) / p);
                    t += "  /  남은 시간 약 " + Util.FormatDuration(rem);
                }
                lblTime.Text = p + "%   " + t;
            }
            else
            {
                lblTime.Text = "경과 " + Util.FormatDuration(DateTime.Now - start);
            }
            if (f.Length > 0) lblFile.Text = f;
        }

        public void Finish()
        {
            done = true;
            if (IsHandleCreated)
            {
                try { BeginInvoke(new MethodInvoker(delegate { timer.Stop(); Close(); })); } catch { }
            }
        }
    }
}

namespace NoAdZip
{
    // PC 종료 전 60초 카운트다운. 취소를 누르면 종료하지 않는다.
    class ShutdownForm : DialogBase
    {
        int left = 60;
        readonly Label lbl;
        readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();

        public ShutdownForm()
        {
            UiKit.Setup(this, "PC 종료", 360, 130);
            ShowInTaskbar = true;
            TopMost = true;
            lbl = UiKit.Label(this, "", 14, 16, 332, 40);
            Button c = UiKit.Button(this, "취소", 266, 84, 80, 28);
            c.DialogResult = DialogResult.Cancel;
            CancelButton = c;
            UpdateText();
            timer.Interval = 1000;
            timer.Tick += delegate
            {
                left--;
                if (left <= 0) { timer.Stop(); DialogResult = DialogResult.OK; }
                else UpdateText();
            };
            timer.Start();
        }

        void UpdateText()
        {
            lbl.Text = "작업이 끝나 " + left + "초 뒤에 PC를 종료합니다.\n그만두려면 [취소]를 누르세요.";
        }
    }
}
