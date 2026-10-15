using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using Microsoft.Win32;

// 몽집 설치 프로그램 겸 제거 프로그램. 관리자 권한 없이 사용자 폴더에 설치하고,
// 설치가 끝나면 설치된 NoAdZip.exe 경로로 탐색기 우클릭 메뉴를 자동 등록한다.
//   Setup.exe                설치 창
//   Setup.exe /S [/D=폴더] [/nomenu] [/desktop]   조용히 설치
//   Uninstall.exe (= 설치 폴더에 복사된 Setup.exe)   제거
[assembly: System.Reflection.AssemblyVersion("1.0.5.0")]
[assembly: System.Reflection.AssemblyFileVersion("1.0.5.0")]

namespace NoAdZipSetup
{
    static class Setup
    {
        const string AppName = "몽집";
        const string AppExe = "몽집.exe";
        const string LegacyName = "NoAdZip";        // 이름이 바뀌기 전(옛 버전) 설치본의 이름
        const string LegacyExe = "NoAdZip.exe";
        const string Version = "1.0.5";
        static readonly string[] Payload = { AppExe, "7z.exe", "7z.dll", "7zip-License.txt" };

        // 시험용: NOADZIP_REGTEST 를 주면 제거 목록 키를 그 아래에, NOADZIP_STARTMENU 를 주면 시작 메뉴를 그 폴더에 만든다.
        static string UninstallKey
        {
            get
            {
                string t = Environment.GetEnvironmentVariable("NOADZIP_REGTEST");
                return (string.IsNullOrEmpty(t) ? @"Software\Microsoft\Windows\CurrentVersion\Uninstall" : t + @"\Uninstall") + @"\" + AppName;
            }
        }

        static string StartMenuDir
        {
            get
            {
                string t = Environment.GetEnvironmentVariable("NOADZIP_STARTMENU");
                if (!string.IsNullOrEmpty(t)) return t;
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), AppName);
            }
        }

        static string DesktopLink
        {
            get
            {
                string t = Environment.GetEnvironmentVariable("NOADZIP_STARTMENU");
                string dir = string.IsNullOrEmpty(t) ? Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory) : t;
                return Path.Combine(dir, AppName + ".lnk");
            }
        }

        static string DefaultDir
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\" + AppName); }
        }

        [STAThread]
        static int Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            bool silent = false, uninstall = false, noMenu = false, desktop = false, relaunched = false;
            string dir = null;
            foreach (string a in args)
            {
                string low = a.ToLowerInvariant();
                if (low == "/s" || low == "/silent") silent = true;
                else if (low == "/uninstall") uninstall = true;
                else if (low == "/nomenu") noMenu = true;
                else if (low == "/desktop") desktop = true;
                else if (low == "/relaunched") relaunched = true;
                else if (low.StartsWith("/d=")) dir = a.Substring(3).Trim('"');
                else if (low.StartsWith("/dir=")) dir = a.Substring(5).Trim('"');   // 업데이트 모듈이 넘기는 설치 폴더 (Inno Setup 과 같은 이름)
            }
            // 설치 폴더에 복사된 자신이 실행되면 제거 모드다.
            bool isCopy = Path.GetFileName(Assembly.GetExecutingAssembly().Location).Equals("Uninstall.exe", StringComparison.OrdinalIgnoreCase);
            if (isCopy) uninstall = true;

            if (uninstall) return RunUninstall(silent, dir, relaunched);

            if (string.IsNullOrEmpty(dir)) dir = DefaultDir;
            if (silent)
            {
                string err = Install(dir, !noMenu, desktop);
                if (err != null) { Console.Error.WriteLine(err); return 1; }
                return 0;
            }
            Application.Run(new SetupForm(dir));
            return 0;
        }

        // ---------------- 설치 ----------------

        public static string Install(string dir, bool menu, bool desktop)
        {
            try
            {
                dir = Path.GetFullPath(dir);
                Directory.CreateDirectory(dir);
                Assembly asm = Assembly.GetExecutingAssembly();
                foreach (string name in Payload)
                {
                    string dst = Path.Combine(dir, name);
                    // 업데이트로 실행되면 앱이 아직 끝나는 중일 수 있어, 덮어쓸 수 없으면 잠깐 기다리며 다시 시도한다 (최대 약 10초)
                    for (int attempt = 0; ; attempt++)
                    {
                        try
                        {
                            using (Stream s = asm.GetManifestResourceStream(name))
                            using (FileStream f = new FileStream(dst, FileMode.Create, FileAccess.Write))
                            {
                                if (s == null) return "설치 파일이 손상되었습니다: " + name;
                                s.CopyTo(f);
                            }
                            break;
                        }
                        catch (IOException)
                        {
                            if (attempt >= 9) return "파일을 덮어쓸 수 없습니다. 몽집이 실행 중이면 닫고 다시 시도하세요.\n" + dst;
                            System.Threading.Thread.Sleep(1000);
                        }
                    }
                }
                string exe = Path.Combine(dir, AppExe);
                string un = Path.Combine(dir, "Uninstall.exe");
                File.Copy(asm.Location, un, true);

                // 시작 메뉴 바로가기
                string sm = StartMenuDir;
                Directory.CreateDirectory(sm);
                MakeLink(Path.Combine(sm, AppName + ".lnk"), exe, "", dir, "광고 없는 압축 프로그램");
                MakeLink(Path.Combine(sm, AppName + " 제거.lnk"), un, "", dir, "몽집 제거");
                if (desktop) MakeLink(DesktopLink, exe, "", dir, "광고 없는 압축 프로그램");

                // 프로그램 추가/제거 목록
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(UninstallKey))
                {
                    k.SetValue("DisplayName", AppName + " - 광고 없는 압축 프로그램");
                    k.SetValue("DisplayVersion", Version);
                    k.SetValue("Publisher", AppName);
                    k.SetValue("InstallLocation", dir);
                    k.SetValue("DisplayIcon", "\"" + exe + "\",0");
                    k.SetValue("UninstallString", "\"" + un + "\"");
                    k.SetValue("QuietUninstallString", "\"" + un + "\" /S");
                    k.SetValue("NoModify", 1, RegistryValueKind.DWord);
                    k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                    long size = 0;
                    foreach (string f in Directory.GetFiles(dir)) size += new FileInfo(f).Length;
                    k.SetValue("EstimatedSize", (int)(size / 1024), RegistryValueKind.DWord);
                }

                CleanLegacy(dir);

                // 우클릭 메뉴 자동 등록: 설치된 exe 가 자기 경로로 등록한다.
                if (menu)
                {
                    int rc = RunTool(exe, "/regmenu /silent");
                    if (rc != 0) return "설치는 끝났지만 우클릭 메뉴 등록에 실패했습니다. (설정 > 우클릭 메뉴 등록 으로 다시 시도하세요)";
                }
                return null;
            }
            catch (UnauthorizedAccessException ex) { return "이 폴더에 쓸 수 없습니다: " + ex.Message; }
            catch (Exception ex) { return "설치 실패: " + ex.Message; }
        }

        // 옛 이름(NoAdZip)으로 설치돼 있던 것을 정리한다: 옛 실행 파일, 시작 메뉴·바탕화면 바로가기, 프로그램 목록 항목.
        // (설정 폴더 %AppData%\NoAdZip 과 파일 연결·우클릭 메뉴 키는 그대로 이어 쓴다)
        static void CleanLegacy(string newDir)
        {
            try
            {
                string legacyKey = UninstallKey.Substring(0, UninstallKey.LastIndexOf('\\') + 1) + LegacyName;
                string oldDir = null;
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(legacyKey))
                    if (k != null) oldDir = k.GetValue("InstallLocation") as string;
                if (!string.IsNullOrEmpty(oldDir) && Directory.Exists(oldDir))
                {
                    bool same = string.Equals(Path.GetFullPath(oldDir).TrimEnd('\\'), newDir.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
                    string[] files = same ? new string[] { LegacyExe }
                                          : new string[] { LegacyExe, "7z.exe", "7z.dll", "7zip-License.txt", "Uninstall.exe" };
                    foreach (string f in files) { try { File.Delete(Path.Combine(oldDir, f)); } catch { } }
                    if (!same) { try { Directory.Delete(oldDir, false); } catch { } }   // 비어 있을 때만 지워진다
                }
                if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NOADZIP_STARTMENU")))
                {
                    try { Directory.Delete(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), LegacyName), true); } catch { }
                    try { File.Delete(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), LegacyName + ".lnk")); } catch { }
                }
                Registry.CurrentUser.DeleteSubKeyTree(legacyKey, false);
            }
            catch { }
        }

        static int RunTool(string exe, string args)
        {
            ProcessStartInfo psi = new ProcessStartInfo(exe, args);
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            using (Process p = Process.Start(psi))
            {
                if (!p.WaitForExit(30000)) return -1;
                return p.ExitCode;
            }
        }

        static void MakeLink(string path, string target, string args, string workDir, string desc)
        {
            Type t = Type.GetTypeFromProgID("WScript.Shell");
            if (t == null) return;
            object sh = Activator.CreateInstance(t);
            object lnk = t.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, sh, new object[] { path });
            Type lt = lnk.GetType();
            lt.InvokeMember("TargetPath", BindingFlags.SetProperty, null, lnk, new object[] { target });
            lt.InvokeMember("Arguments", BindingFlags.SetProperty, null, lnk, new object[] { args });
            lt.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, lnk, new object[] { workDir });
            lt.InvokeMember("Description", BindingFlags.SetProperty, null, lnk, new object[] { desc });
            lt.InvokeMember("IconLocation", BindingFlags.SetProperty, null, lnk, new object[] { target + ",0" });
            lt.InvokeMember("Save", BindingFlags.InvokeMethod, null, lnk, null);
        }

        // ---------------- 제거 ----------------

        static int RunUninstall(bool silent, string dir, bool relaunched)
        {
            string self = Assembly.GetExecutingAssembly().Location;
            if (string.IsNullOrEmpty(dir)) dir = Path.GetDirectoryName(self);
            dir = Path.GetFullPath(dir);

            // 설치 폴더 안의 자신은 지울 수 없으니 임시 폴더로 복사해 다시 실행한다.
            if (!relaunched && self.StartsWith(dir, StringComparison.OrdinalIgnoreCase))
            {
                if (!silent && MessageBox.Show("몽집을 제거할까요?\n(설정과 로그는 남겨 둡니다)", "몽집 제거",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return 0;
                string tmp = Path.Combine(Path.GetTempPath(), "NoAdZipUninstall_" + Guid.NewGuid().ToString("N") + ".exe");
                File.Copy(self, tmp, true);
                ProcessStartInfo psi = new ProcessStartInfo(tmp, "/uninstall /relaunched /D=\"" + dir + "\"" + (silent ? " /S" : ""));
                psi.UseShellExecute = false;
                Process.Start(psi);      // 설치 폴더의 자신이 잠겨 있으므로 기다리지 않고 끝낸다
                return 0;
            }

            string exe = Path.Combine(dir, AppExe);
            if (File.Exists(exe)) RunTool(exe, "/unregall /silent");
            else Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\*\shell\NoAdZip", false);

            try { Directory.Delete(StartMenuDir, true); } catch { }
            try { File.Delete(DesktopLink); } catch { }
            try { Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false); } catch { }

            bool ok = false;
            for (int i = 0; i < 10 && !ok; i++)
            {
                try { if (Directory.Exists(dir)) Directory.Delete(dir, true); ok = true; }
                catch { System.Threading.Thread.Sleep(500); }
            }
            // 임시 폴더의 자신은 종료 후 지운다.
            try
            {
                ProcessStartInfo c = new ProcessStartInfo("cmd.exe", "/c ping -n 3 127.0.0.1 >nul & del /f /q \"" + self + "\"");
                c.UseShellExecute = false;
                c.CreateNoWindow = true;
                Process.Start(c);
            }
            catch { }
            if (!silent)
                MessageBox.Show(ok ? "몽집을 제거했습니다." : "일부 파일을 지우지 못했습니다. 몽집을 닫고 폴더를 직접 삭제하세요:\n" + dir,
                    "몽집 제거", MessageBoxButtons.OK, ok ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
            return ok ? 0 : 1;
        }

        // ---------------- 설치 창 ----------------

        sealed class SetupForm : Form
        {
            readonly TextBox txtDir = new TextBox();
            readonly CheckBox chkMenu = new CheckBox();
            readonly CheckBox chkDesk = new CheckBox();
            readonly CheckBox chkRun = new CheckBox();

            public SetupForm(string dir)
            {
                Text = "몽집 설치";
                Font = new Font("맑은 고딕", 9f);
                FormBorderStyle = FormBorderStyle.FixedDialog;
                MaximizeBox = false;
                MinimizeBox = false;
                StartPosition = FormStartPosition.CenterScreen;
                ClientSize = new Size(470, 250);
                try { Icon = Icon.ExtractAssociatedIcon(Assembly.GetExecutingAssembly().Location); } catch { }

                Label title = new Label();
                title.Text = "몽집 - 광고 없는 압축 프로그램";
                title.Font = new Font("맑은 고딕", 12f, FontStyle.Bold);
                title.SetBounds(16, 14, 440, 26);
                Controls.Add(title);

                Label l1 = new Label();
                l1.Text = "설치 폴더 (관리자 권한 필요 없음)";
                l1.SetBounds(16, 54, 300, 18);
                Controls.Add(l1);

                txtDir.Text = dir;
                txtDir.SetBounds(16, 76, 350, 24);
                Controls.Add(txtDir);

                Button browse = new Button();
                browse.Text = "찾아보기...";
                browse.SetBounds(374, 75, 80, 26);
                browse.Click += delegate
                {
                    using (FolderBrowserDialog fb = new FolderBrowserDialog())
                    {
                        fb.Description = "설치할 폴더를 고르세요 (몽집 하위 폴더가 만들어지지 않습니다)";
                        if (fb.ShowDialog(this) == DialogResult.OK) txtDir.Text = fb.SelectedPath;
                    }
                };
                Controls.Add(browse);

                chkMenu.Text = "탐색기 우클릭 메뉴에 등록 (Windows 11 은 '추가 옵션 표시' 안)";
                chkMenu.Checked = true;
                chkMenu.SetBounds(16, 114, 440, 22);
                Controls.Add(chkMenu);

                chkDesk.Text = "바탕화면 바로가기 만들기";
                chkDesk.SetBounds(16, 140, 440, 22);
                Controls.Add(chkDesk);

                chkRun.Text = "설치 후 몽집 실행";
                chkRun.Checked = true;
                chkRun.SetBounds(16, 166, 440, 22);
                Controls.Add(chkRun);

                Button ok = new Button();
                ok.Text = "설치";
                ok.SetBounds(284, 206, 84, 30);
                ok.Click += delegate { DoInstall(); };
                Controls.Add(ok);
                AcceptButton = ok;

                Button cancel = new Button();
                cancel.Text = "취소";
                cancel.SetBounds(374, 206, 84, 30);
                cancel.DialogResult = DialogResult.Cancel;
                Controls.Add(cancel);
                CancelButton = cancel;
            }

            void DoInstall()
            {
                string dir = txtDir.Text.Trim();
                if (dir.Length == 0) { MessageBox.Show(this, "설치 폴더를 입력하세요.", "몽집 설치"); return; }
                UseWaitCursor = true;
                Enabled = false;
                string err = Install(dir, chkMenu.Checked, chkDesk.Checked);
                Enabled = true;
                UseWaitCursor = false;
                if (err != null) { MessageBox.Show(this, err, "몽집 설치", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
                MessageBox.Show(this, "설치를 마쳤습니다." + (chkMenu.Checked ? "\n탐색기 우클릭 메뉴가 등록되었습니다." : ""), "몽집 설치");
                if (chkRun.Checked)
                {
                    try { Process.Start(Path.Combine(Path.GetFullPath(dir), AppExe)); } catch { }
                }
                Close();
            }
        }
    }
}
