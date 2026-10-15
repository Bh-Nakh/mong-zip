using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace NoAdZip
{
    class DialogBase : Form
    {
        protected override bool ShowWithoutActivation { get { return Program.NoFocus; } }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Theme.ApplyTitleBar(this);
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            Loc.Apply(this);
            Theme.Apply(this);
        }
    }

    static class UiKit
    {
        public static Label Label(Control parent, string text, int x, int y, int w, int h)
        {
            Label l = new Label();
            l.Text = text;
            l.SetBounds(x, y, w, h);
            parent.Controls.Add(l);
            return l;
        }

        public static Button Button(Control parent, string text, int x, int y, int w, int h)
        {
            Button b = new Button();
            b.Text = text;
            b.SetBounds(x, y, w, h);
            parent.Controls.Add(b);
            return b;
        }

        public static TextBox Text(Control parent, int x, int y, int w)
        {
            TextBox t = new TextBox();
            t.SetBounds(x, y, w, 23);
            parent.Controls.Add(t);
            return t;
        }

        public static CheckBox Check(Control parent, string text, int x, int y, int w)
        {
            CheckBox c = new CheckBox();
            c.Text = text;
            c.SetBounds(x, y, w, 22);
            parent.Controls.Add(c);
            return c;
        }

        public static RadioButton Radio(Control parent, string text, int x, int y, int w)
        {
            RadioButton r = new RadioButton();
            r.Text = text;
            r.SetBounds(x, y, w, 22);
            parent.Controls.Add(r);
            return r;
        }

        public static ComboBox Combo(Control parent, int x, int y, int w)
        {
            ComboBox c = new ComboBox();
            c.DropDownStyle = ComboBoxStyle.DropDownList;
            c.SetBounds(x, y, w, 23);
            parent.Controls.Add(c);
            return c;
        }

        public static GroupBox Group(Control parent, string text, int x, int y, int w, int h)
        {
            GroupBox g = new GroupBox();
            g.Text = text;
            g.SetBounds(x, y, w, h);
            parent.Controls.Add(g);
            return g;
        }

        public static void Setup(Form f, string title, int w, int h)
        {
            f.Text = title;
            f.Font = SystemFonts.MessageBoxFont;
            f.FormBorderStyle = FormBorderStyle.FixedDialog;
            f.MaximizeBox = false;
            f.MinimizeBox = false;
            f.ShowInTaskbar = false;
            f.StartPosition = FormStartPosition.CenterParent;
            f.ClientSize = new Size(w, h);
            Program.ApplyIcon(f);
        }

        public static void OkCancel(Form f, out Button ok, out Button cancel)
        {
            ok = Button(f, "확인", f.ClientSize.Width - 176, f.ClientSize.Height - 40, 80, 28);
            cancel = Button(f, "취소", f.ClientSize.Width - 90, f.ClientSize.Height - 40, 80, 28);
            ok.DialogResult = DialogResult.OK;
            cancel.DialogResult = DialogResult.Cancel;
            f.AcceptButton = ok;
            f.CancelButton = cancel;
        }

        public static string BrowseFolder(IWin32Window owner, string initial, string desc)
        {
            using (FolderBrowserDialog d = new FolderBrowserDialog())
            {
                d.Description = desc;
                if (!string.IsNullOrEmpty(initial) && Directory.Exists(initial)) d.SelectedPath = initial;
                return d.ShowDialog(owner) == DialogResult.OK ? d.SelectedPath : null;
            }
        }
    }

    static class CodePages
    {
        public static readonly string[][] All = new string[][] {
            new string[] { "auto", "자동 감지" },
            new string[] { "949", "한국어 (CP949)" },
            new string[] { "65001", "UTF-8" },
            new string[] { "932", "일본어 (Shift-JIS)" },
            new string[] { "936", "중국어 간체 (GBK)" },
            new string[] { "950", "중국어 번체 (Big5)" },
            new string[] { "1252", "서유럽 (CP1252)" },
            new string[] { "437", "DOS 영어 (CP437)" },
        };

        public static void Fill(ComboBox c, string selected)
        {
            c.Items.Clear();
            int sel = 0;
            for (int i = 0; i < All.Length; i++)
            {
                c.Items.Add(All[i][1]);
                if (All[i][0] == selected) sel = i;
            }
            c.SelectedIndex = sel;
        }

        public static string Get(ComboBox c)
        {
            return c.SelectedIndex >= 0 ? All[c.SelectedIndex][0] : "auto";
        }
    }

    class PasswordForm : DialogBase
    {
        readonly TextBox txt;
        public string Password { get { return txt.Text; } }

        public PasswordForm(string archiveName, bool wrong)
        {
            UiKit.Setup(this, "암호 입력", 380, 150);
            Label l = UiKit.Label(this, (wrong ? "암호가 맞지 않습니다. 다시 입력하세요." : "암호가 걸린 압축파일입니다.") + "\n" + archiveName, 14, 12, 352, 40);
            l.AutoEllipsis = true;
            txt = UiKit.Text(this, 14, 56, 352);
            txt.UseSystemPasswordChar = true;
            CheckBox show = UiKit.Check(this, "암호 보기", 14, 84, 120);
            show.CheckedChanged += delegate { txt.UseSystemPasswordChar = !show.Checked; };
            Button ok, cancel;
            UiKit.OkCancel(this, out ok, out cancel);
        }

        public static string Ask(IWin32Window owner, string archiveName, bool wrong)
        {
            if (Msg.Silent)
            {
                string env = Environment.GetEnvironmentVariable("NOADZIP_PASSWORD");
                return wrong ? null : env;
            }
            using (PasswordForm f = new PasswordForm(archiveName, wrong))
                return f.ShowDialog(owner) == DialogResult.OK ? f.Password : null;
        }
    }

    class ConflictForm : DialogBase
    {
        public string Choice;

        public ConflictForm(int count, string sample)
        {
            UiKit.Setup(this, "같은 이름의 파일이 있습니다", 460, 150);
            Label l = UiKit.Label(this, "풀 위치에 같은 이름의 파일이 " + count + "개 이미 있습니다.\n예: " + sample, 14, 12, 432, 44);
            l.AutoEllipsis = true;
            string[][] b = new string[][] {
                new string[] { "all", "모두 덮어쓰기" }, new string[] { "skip", "모두 건너뛰기" }, new string[] { "rename", "이름 바꿔 풀기" } };
            int x = 14;
            foreach (string[] p in b)
            {
                string val = p[0];
                Button bt = UiKit.Button(this, p[1], x, 70, 134, 30);
                bt.Click += delegate { Choice = val; DialogResult = DialogResult.OK; };
                x += 142;
            }
            Button c = UiKit.Button(this, "취소", 350, 110, 96, 28);
            c.DialogResult = DialogResult.Cancel;
            CancelButton = c;
        }

        public static string Ask(IWin32Window owner, int count, string sample)
        {
            if (Msg.Silent) return "rename";
            using (ConflictForm f = new ConflictForm(count, sample))
                return f.ShowDialog(owner) == DialogResult.OK ? f.Choice : null;
        }
    }

    class ExtractChoice
    {
        public string Mode;          // here | custom
        public string Dest;
        public bool MakeFolder;
        public string Overwrite;     // ask | all | skip | rename
        public string CodePage;
        public bool OpenFolder;
    }

    class ExtractForm : DialogBase
    {
        readonly RadioButton rbHere, rbCustom;
        readonly TextBox txtDest;
        readonly CheckBox chkMake, chkOpen;
        readonly RadioButton[] rbOw = new RadioButton[4];
        readonly string[] owVals = { "ask", "all", "skip", "rename" };
        readonly ComboBox cmbCp;
        public ExtractChoice Choice;

        public ExtractForm(IList<string> archives, string defaultDest)
        {
            UiKit.Setup(this, "압축 풀기", 520, 346);
            string name = archives.Count == 1 ? Path.GetFileName(archives[0]) : Path.GetFileName(archives[0]) + " 외 " + (archives.Count - 1) + "개";
            Label l = UiKit.Label(this, "압축파일: " + name, 14, 12, 492, 20);
            l.AutoEllipsis = true;

            GroupBox g1 = UiKit.Group(this, "풀 위치", 14, 38, 492, 116);
            rbHere = UiKit.Radio(g1, "압축파일이 있는 폴더에 풀기", 12, 20, 300);
            rbCustom = UiKit.Radio(g1, "지정한 폴더에 풀기:", 12, 46, 160);
            txtDest = UiKit.Text(g1, 28, 72, 360);
            Button br = UiKit.Button(g1, "찾아보기", 396, 70, 84, 26);
            chkMake = UiKit.Check(g1, "압축파일 이름의 폴더를 만들어 풀기 (최상위 폴더가 하나면 생략)", 28, 96, 460);
            chkMake.Top = 96;
            g1.Height = 124;
            bool useCustom = !string.IsNullOrEmpty(defaultDest);
            rbHere.Checked = !useCustom;
            rbCustom.Checked = useCustom;
            txtDest.Text = defaultDest ?? "";
            chkMake.Checked = Settings.ExtractMode != "here";
            rbCustom.CheckedChanged += delegate { txtDest.Enabled = br.Enabled = rbCustom.Checked; };
            txtDest.Enabled = br.Enabled = rbCustom.Checked;
            br.Click += delegate
            {
                string d = UiKit.BrowseFolder(this, txtDest.Text, "압축을 풀 폴더를 선택하세요");
                if (d != null) { txtDest.Text = d; rbCustom.Checked = true; }
            };

            GroupBox g2 = UiKit.Group(this, "같은 이름의 파일이 있을 때", 14, 162, 492, 54);
            string[] owNames = { "묻기", "모두 덮어쓰기", "모두 건너뛰기", "이름 바꿔 풀기" };
            int x = 12;
            for (int i = 0; i < 4; i++)
            {
                rbOw[i] = UiKit.Radio(g2, owNames[i], x, 22, 116);
                rbOw[i].Checked = owVals[i] == Settings.Overwrite;
                x += 120;
            }
            if (!Array.Exists(rbOw, delegate (RadioButton r) { return r.Checked; })) rbOw[0].Checked = true;

            UiKit.Label(this, "파일명 인코딩:", 14, 230, 100, 20);
            cmbCp = UiKit.Combo(this, 118, 226, 200);
            CodePages.Fill(cmbCp, Settings.CodePage);
            chkOpen = UiKit.Check(this, "풀기가 끝나면 폴더 열기", 14, 260, 300);
            chkOpen.Checked = Settings.OpenFolderAfter;

            Button ok, cancel;
            UiKit.OkCancel(this, out ok, out cancel);
            ok.Text = "풀기";
            FormClosing += delegate (object s, FormClosingEventArgs e)
            {
                if (DialogResult != DialogResult.OK) return;
                if (rbCustom.Checked && txtDest.Text.Trim().Length == 0)
                {
                    MessageBox.Show(this, "풀 폴더를 지정하세요.", "몽집");
                    e.Cancel = true;
                    return;
                }
                ExtractChoice c = new ExtractChoice();
                c.Mode = rbCustom.Checked ? "custom" : "here";
                c.Dest = txtDest.Text.Trim();
                c.MakeFolder = chkMake.Checked;
                c.CodePage = CodePages.Get(cmbCp);
                c.OpenFolder = chkOpen.Checked;
                c.Overwrite = "ask";
                for (int i = 0; i < 4; i++) if (rbOw[i].Checked) c.Overwrite = owVals[i];
                Choice = c;
            };
        }

        public static ExtractChoice Ask(IWin32Window owner, IList<string> archives, string defaultDest)
        {
            using (ExtractForm f = new ExtractForm(archives, defaultDest))
                return f.ShowDialog(owner) == DialogResult.OK ? f.Choice : null;
        }
    }

    class CompressForm : DialogBase
    {
        readonly ListBox lst;
        readonly TextBox txtOut, txtPw, txtPw2, txtExclude;
        readonly ComboBox cmbFmt, cmbLevel, cmbThreads;
        readonly CheckBox chkUtf8, chkShow;
        readonly Label lblPwNote;
        readonly CheckBox chkSolid, chkHdr;
        readonly ComboBox cmbSplit;
        readonly NumericUpDown numSplit;
        readonly List<string> sources = new List<string>();
        bool updatingFmt;
        public CompressOptions Result;

        static readonly string[][] Formats = new string[][] {
            new string[] { "zip", "ZIP (.zip)  - 어디서나 열림" },
            new string[] { "7z", "7z (.7z)  - 압축률 높음" },
            new string[] { "tar.gz", "TAR.GZ (.tar.gz)" },
            new string[] { "tar.bz2", "TAR.BZ2 (.tar.bz2)" },
            new string[] { "tar.xz", "TAR.XZ (.tar.xz)" },
            new string[] { "tar", "TAR (.tar)  - 묶기만 함" },
            new string[] { "gz", "GZ (.gz)  - 파일 1개만" },
            new string[] { "bz2", "BZ2 (.bz2)  - 파일 1개만" },
            new string[] { "xz", "XZ (.xz)  - 파일 1개만" },
        };
        static readonly int[] LevelVals = { 0, 1, 5, 7, 9 };
        static readonly string[] LevelNames = { "저장 (압축 안 함)", "빠름", "보통", "높음", "최대" };
        static readonly int[] ThreadVals = { 0, 1, 2, 4, 8, 16 };

        public CompressForm(IEnumerable<string> initial)
        {
            UiKit.Setup(this, "압축하기", 560, 584);
            UiKit.Label(this, "압축할 파일·폴더", 14, 10, 300, 18);
            lst = new ListBox();
            lst.SetBounds(14, 30, 440, 100);
            lst.SelectionMode = SelectionMode.MultiExtended;
            lst.HorizontalScrollbar = true;
            Controls.Add(lst);
            Button bAddF = UiKit.Button(this, "파일 추가", 462, 30, 84, 28);
            Button bAddD = UiKit.Button(this, "폴더 추가", 462, 62, 84, 28);
            Button bDel = UiKit.Button(this, "제거", 462, 94, 84, 28);
            bAddF.Click += delegate
            {
                using (OpenFileDialog d = new OpenFileDialog())
                {
                    d.Multiselect = true;
                    d.Title = "압축할 파일 선택";
                    if (d.ShowDialog(this) == DialogResult.OK) AddSources(d.FileNames);
                }
            };
            bAddD.Click += delegate
            {
                string p = UiKit.BrowseFolder(this, null, "압축할 폴더를 선택하세요");
                if (p != null) AddSources(new string[] { p });
            };
            bDel.Click += delegate
            {
                List<string> rm = new List<string>();
                foreach (object o in lst.SelectedItems) rm.Add((string)o);
                foreach (string s in rm) sources.Remove(s);
                RefreshList();
            };

            UiKit.Label(this, "저장 위치", 14, 140, 200, 18);
            txtOut = UiKit.Text(this, 14, 160, 440);
            Button bBr = UiKit.Button(this, "찾아보기", 462, 158, 84, 26);
            bBr.Click += delegate
            {
                using (SaveFileDialog d = new SaveFileDialog())
                {
                    d.Title = "압축파일 저장 위치";
                    d.FileName = txtOut.Text;
                    d.OverwritePrompt = false;
                    d.Filter = "모든 파일|*.*";
                    if (d.ShowDialog(this) == DialogResult.OK) { txtOut.Text = d.FileName; txtOut.Modified = true; }
                }
            };

            UiKit.Label(this, "압축 형식", 14, 196, 120, 18);
            cmbFmt = UiKit.Combo(this, 14, 216, 262);
            UiKit.Label(this, "압축 수준", 290, 196, 120, 18);
            cmbLevel = UiKit.Combo(this, 290, 216, 150);
            foreach (string n in LevelNames) cmbLevel.Items.Add(n);
            int li = Array.IndexOf(LevelVals, Settings.DefaultLevel);
            cmbLevel.SelectedIndex = li < 0 ? 2 : li;
            UiKit.Label(this, "스레드", 452, 196, 90, 18);
            cmbThreads = UiKit.Combo(this, 452, 216, 94);
            foreach (int t in ThreadVals) cmbThreads.Items.Add(t == 0 ? "자동" : t.ToString());
            int ti = Array.IndexOf(ThreadVals, Settings.Threads);
            cmbThreads.SelectedIndex = ti < 0 ? 0 : ti;

            GroupBox gp = UiKit.Group(this, "암호 (ZIP·7z)", 14, 252, 532, 110);
            UiKit.Label(gp, "암호", 12, 24, 80, 18);
            txtPw = UiKit.Text(gp, 100, 20, 200);
            UiKit.Label(gp, "암호 확인", 12, 52, 80, 18);
            txtPw2 = UiKit.Text(gp, 100, 48, 200);
            txtPw.UseSystemPasswordChar = txtPw2.UseSystemPasswordChar = true;
            chkShow = UiKit.Check(gp, "암호 보기", 316, 20, 110);
            chkShow.CheckedChanged += delegate { txtPw.UseSystemPasswordChar = txtPw2.UseSystemPasswordChar = !chkShow.Checked; };
            lblPwNote = UiKit.Label(gp, "", 12, 80, 508, 20);

            UiKit.Label(this, "제외할 파일 패턴 (;로 구분, 예: *.tmp;Thumbs.db;node_modules)", 14, 372, 530, 18);
            txtExclude = UiKit.Text(this, 14, 392, 532);
            chkUtf8 = UiKit.Check(this, "ZIP 파일명을 UTF-8로 저장 (한글 이름 호환 권장)", 14, 424, 400);
            chkUtf8.Checked = Settings.ZipUtf8Names;
            chkSolid = UiKit.Check(this, "7z 솔리드 압축 (압축률 높음, 일부만 풀기는 느림)", 14, 450, 330);
            chkSolid.Checked = true;
            chkHdr = UiKit.Check(this, "7z 파일명도 암호화", 350, 450, 196);
            UiKit.Label(this, "분할 압축", 14, 484, 70, 18);
            cmbSplit = UiKit.Combo(this, 88, 480, 170);
            cmbSplit.Items.AddRange(new object[] { "나누지 않음", "100 MB", "650 MB (CD)", "700 MB (CD)", "1 GB", "4 GB (FAT32)", "직접 입력" });
            cmbSplit.SelectedIndex = 0;
            numSplit = new NumericUpDown();
            numSplit.SetBounds(268, 480, 90, 23);
            numSplit.Minimum = 1; numSplit.Maximum = 1000000; numSplit.Value = 100;
            numSplit.Enabled = false;
            Controls.Add(numSplit);
            UiKit.Label(this, "MB (ZIP·7z)", 364, 484, 100, 18);
            cmbSplit.SelectedIndexChanged += delegate { numSplit.Enabled = cmbSplit.SelectedIndex == 6 && cmbSplit.Enabled; };

            Button ok, cancel;
            UiKit.OkCancel(this, out ok, out cancel);
            ok.Text = "압축";
            ok.DialogResult = DialogResult.None;
            ok.Click += delegate { TryAccept(); };

            AddSources(initial);
            cmbFmt.SelectedIndexChanged += delegate { if (!updatingFmt) OnFormatChanged(); };
            RebuildFormats(Settings.DefaultFormat);
            formatsReady = true;
            OnFormatChanged();
            AllowDrop = true;
            DragEnter += delegate (object s, DragEventArgs e) { if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy; };
            DragDrop += delegate (object s, DragEventArgs e) { AddSources((string[])e.Data.GetData(DataFormats.FileDrop)); };
        }

        bool formatsReady;
        List<string[]> shownFormats = new List<string[]>();
        string FmtKey { get { return cmbFmt.SelectedIndex >= 0 && cmbFmt.SelectedIndex < shownFormats.Count ? shownFormats[cmbFmt.SelectedIndex][0] : "zip"; } }

        bool SingleFile { get { return sources.Count == 1 && File.Exists(sources[0]); } }

        void RebuildFormats(string prefer)
        {
            updatingFmt = true;
            string keep = shownFormats.Count > 0 ? FmtKey : prefer;
            shownFormats.Clear();
            cmbFmt.Items.Clear();
            foreach (string[] f in Formats)
            {
                bool single = f[0] == "gz" || f[0] == "bz2" || f[0] == "xz";
                if (single && !SingleFile) continue;
                shownFormats.Add(f);
                cmbFmt.Items.Add(f[1]);
            }
            int idx = shownFormats.FindIndex(delegate (string[] f) { return f[0] == keep; });
            cmbFmt.SelectedIndex = idx < 0 ? 0 : idx;
            updatingFmt = false;
        }

        void AddSources(IEnumerable<string> paths)
        {
            foreach (string p in paths)
            {
                if (!File.Exists(p) && !Directory.Exists(p)) continue;
                string full = Path.GetFullPath(p).TrimEnd('\\');
                if (full.Length == 2) continue;
                if (!sources.Contains(full)) sources.Add(full);
            }
            RefreshList();
        }

        void RefreshList()
        {
            lst.Items.Clear();
            foreach (string s in sources) lst.Items.Add(s);
            if (formatsReady)
            {
                RebuildFormats(null);
                OnFormatChanged();
            }
        }

        string DefaultOutBase()
        {
            if (sources.Count == 0) return "";
            string dir = Path.GetDirectoryName(sources[0]);
            string nm;
            if (sources.Count == 1)
                nm = File.Exists(sources[0]) ? Path.GetFileNameWithoutExtension(sources[0]) : Path.GetFileName(sources[0]);
            else
                nm = Path.GetFileName(dir);
            if (string.IsNullOrEmpty(nm)) nm = "archive";
            return Path.Combine(dir, nm);
        }

        string FmtExt(string key)
        {
            if (key == "gz" && SingleFile) return Path.GetExtension(sources[0]) + ".gz";
            if (key == "bz2" && SingleFile) return Path.GetExtension(sources[0]) + ".bz2";
            if (key == "xz" && SingleFile) return Path.GetExtension(sources[0]) + ".xz";
            return "." + key;
        }

        void OnFormatChanged()
        {
            string key = FmtKey;
            bool pw = key == "zip" || key == "7z";
            txtPw.Enabled = txtPw2.Enabled = chkShow.Enabled = pw;
            if (!pw) { txtPw.Text = ""; txtPw2.Text = ""; }
            chkUtf8.Enabled = key == "zip";
            chkSolid.Enabled = key == "7z";
            chkHdr.Enabled = key == "7z";
            cmbSplit.Enabled = pw;
            numSplit.Enabled = pw && cmbSplit.SelectedIndex == 6;
            lblPwNote.Text = key == "zip" ? "ZIP은 AES-256으로 암호화합니다. (파일 이름은 보임)"
                : key == "7z" ? "7z는 AES-256으로 암호화합니다."
                : "이 형식은 암호를 지원하지 않습니다.";
            if (sources.Count > 0)
            {
                string cur = txtOut.Text;
                string baseOut = DefaultOutBase();
                // 사용자가 직접 바꾼 이름은 확장자만 교체한다.
                bool userEdited = cur.Length > 0 && txtOut.Modified;
                if (userEdited)
                {
                    string stripped = StripKnownExt(cur);
                    txtOut.Text = stripped + FmtExt(key);
                }
                else txtOut.Text = baseOut + FmtExt(key);
            }
        }

        static string StripKnownExt(string p)
        {
            string[] ex = { ".tar.gz", ".tar.bz2", ".tar.xz", ".zip", ".7z", ".tar", ".gz", ".bz2", ".xz" };
            foreach (string e in ex)
                if (p.EndsWith(e, StringComparison.OrdinalIgnoreCase)) return p.Substring(0, p.Length - e.Length);
            return p;
        }

        static string CommonDir(List<string> items)
        {
            string common = null;
            foreach (string s in items)
            {
                string d = Path.GetDirectoryName(s);
                if (common == null) { common = d; continue; }
                while (common != null && !(d + "\\").StartsWith(common.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase))
                    common = Path.GetDirectoryName(common);
                if (common == null) return null;
            }
            return common;
        }

        void TryAccept()
        {
            if (sources.Count == 0) { MessageBox.Show(this, "압축할 파일이나 폴더를 추가하세요.", "몽집"); return; }
            string key = FmtKey;
            string outFile = txtOut.Text.Trim();
            if (outFile.Length == 0) { MessageBox.Show(this, "저장 위치를 입력하세요.", "몽집"); return; }
            try { outFile = Path.GetFullPath(outFile); } catch { MessageBox.Show(this, "저장 위치가 올바르지 않습니다.", "몽집"); return; }
            if (!Directory.Exists(Path.GetDirectoryName(outFile))) { MessageBox.Show(this, "저장할 폴더가 없습니다.", "몽집"); return; }
            if (txtPw.Text != txtPw2.Text) { MessageBox.Show(this, "암호와 암호 확인이 다릅니다.", "몽집"); return; }
            string baseDir = CommonDir(sources);
            if (baseDir == null) { MessageBox.Show(this, "서로 다른 드라이브의 항목은 한 번에 묶을 수 없습니다.", "몽집"); return; }
            foreach (string s in sources)
                if (string.Equals(s, outFile, StringComparison.OrdinalIgnoreCase)) { MessageBox.Show(this, "압축파일이 자기 자신을 포함할 수 없습니다.", "몽집"); return; }
            if (File.Exists(outFile) && MessageBox.Show(this, "이미 있는 파일입니다. 덮어쓸까요?\n" + outFile, "몽집", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

            CompressOptions o = new CompressOptions();
            o.OutFile = outFile;
            o.BaseDir = baseDir;
            foreach (string s in sources) o.Items.Add(s.Substring(baseDir.TrimEnd('\\').Length).TrimStart('\\'));
            o.Format = key;
            o.Level = LevelVals[cmbLevel.SelectedIndex];
            o.Threads = ThreadVals[cmbThreads.SelectedIndex];
            o.Password = txtPw.Text.Length > 0 ? txtPw.Text : null;
            o.Utf8Names = chkUtf8.Checked;
            o.Solid = chkSolid.Checked;
            o.HeaderEncrypt = key == "7z" && chkHdr.Checked && o.Password != null;
            long[] splitMb = { 0, 100, 650, 700, 1024, 4095, (long)numSplit.Value };
            o.VolumeBytes = cmbSplit.Enabled ? splitMb[cmbSplit.SelectedIndex] * 1024L * 1024L : 0;
            foreach (string p in txtExclude.Text.Split(';')) if (p.Trim().Length > 0) o.Excludes.Add(p.Trim());
            Result = o;
            DialogResult = DialogResult.OK;
        }

        public static CompressOptions Ask(IWin32Window owner, IEnumerable<string> sources)
        {
            using (CompressForm f = new CompressForm(sources))
                return f.ShowDialog(owner) == DialogResult.OK ? f.Result : null;
        }
    }

    class SettingsForm : DialogBase
    {
        readonly ComboBox cmbFmt, cmbLevel, cmbThreads, cmbOw, cmbCp;
        readonly RadioButton rbHere, rbFolder, rbAsk;
        readonly CheckBox chkOpen, chkExec, chkUtf8;
        readonly NumericUpDown numRatio, numGb;
        readonly ComboBox cmbTheme, cmbLang;
        readonly CheckedListBox lstExt;
        static readonly string[] FmtVals = { "zip", "7z", "tar.gz", "tar.bz2", "tar.xz", "tar" };
        static readonly int[] LevelVals = { 0, 1, 5, 7, 9 };
        static readonly string[] LevelNames = { "저장 (압축 안 함)", "빠름", "보통", "높음", "최대" };
        static readonly int[] ThreadVals = { 0, 1, 2, 4, 8, 16 };
        static readonly string[] OwVals = { "ask", "all", "skip", "rename" };
        static readonly string[] OwNames = { "묻기", "모두 덮어쓰기", "모두 건너뛰기", "이름 바꿔 풀기" };

        public SettingsForm()
        {
            UiKit.Setup(this, "몽집 설정", 600, 652);
            GroupBox g1 = UiKit.Group(this, "압축 기본값", 14, 10, 572, 76);
            UiKit.Label(g1, "형식", 12, 22, 54, 18);
            cmbFmt = UiKit.Combo(g1, 68, 18, 110);
            foreach (string f in FmtVals) cmbFmt.Items.Add(f);
            cmbFmt.SelectedIndex = Math.Max(0, Array.IndexOf(FmtVals, Settings.DefaultFormat));
            UiKit.Label(g1, "수준", 196, 22, 46, 18);
            cmbLevel = UiKit.Combo(g1, 244, 18, 136);
            foreach (string n in LevelNames) cmbLevel.Items.Add(n);
            cmbLevel.SelectedIndex = Math.Max(0, Array.IndexOf(LevelVals, Settings.DefaultLevel));
            UiKit.Label(g1, "스레드", 392, 22, 62, 18);
            cmbThreads = UiKit.Combo(g1, 456, 18, 100);
            foreach (int t in ThreadVals) cmbThreads.Items.Add(t == 0 ? "자동" : t.ToString());
            cmbThreads.SelectedIndex = Math.Max(0, Array.IndexOf(ThreadVals, Settings.Threads));
            chkUtf8 = UiKit.Check(g1, "ZIP 파일명을 UTF-8로 저장", 12, 48, 300);
            chkUtf8.Checked = Settings.ZipUtf8Names;

            GroupBox g2 = UiKit.Group(this, "풀기 기본값", 14, 94, 572, 130);
            rbHere = UiKit.Radio(g2, "압축파일이 있는 폴더에 바로 풀기", 12, 20, 260);
            rbFolder = UiKit.Radio(g2, "압축파일 이름의 폴더를 만들어 풀기", 12, 42, 280);
            rbAsk = UiKit.Radio(g2, "풀 때마다 옵션 창 표시", 300, 20, 260);
            rbHere.Checked = Settings.ExtractMode == "here";
            rbFolder.Checked = Settings.ExtractMode == "folder";
            rbAsk.Checked = Settings.ExtractMode == "ask";
            if (!rbHere.Checked && !rbFolder.Checked && !rbAsk.Checked) rbFolder.Checked = true;
            UiKit.Label(g2, "같은 이름 파일", 12, 74, 90, 18);
            cmbOw = UiKit.Combo(g2, 106, 70, 130);
            foreach (string n in OwNames) cmbOw.Items.Add(n);
            cmbOw.SelectedIndex = Math.Max(0, Array.IndexOf(OwVals, Settings.Overwrite));
            UiKit.Label(g2, "파일명 인코딩", 242, 74, 110, 18);
            cmbCp = UiKit.Combo(g2, 354, 70, 200);
            CodePages.Fill(cmbCp, Settings.CodePage);
            chkOpen = UiKit.Check(g2, "풀기가 끝나면 폴더 열기", 12, 98, 220);
            chkOpen.Checked = Settings.OpenFolderAfter;
            chkExec = UiKit.Check(g2, "압축파일 안의 실행 파일을 열 때 확인", 260, 98, 300);
            chkExec.Checked = Settings.ConfirmExecOpen;

            GroupBox g3 = UiKit.Group(this, "압축 폭탄 경고 기준", 14, 232, 572, 52);
            UiKit.Label(g3, "압축률", 12, 24, 46, 18);
            numRatio = new NumericUpDown();
            numRatio.SetBounds(60, 20, 70, 23);
            numRatio.Minimum = 10; numRatio.Maximum = 100000;
            numRatio.Value = Math.Max(10, Math.Min(100000, Settings.BombRatio));
            g3.Controls.Add(numRatio);
            UiKit.Label(g3, "배 이상이면서 풀린 크기가", 138, 24, 160, 18);
            numGb = new NumericUpDown();
            numGb.SetBounds(300, 20, 60, 23);
            numGb.Minimum = 1; numGb.Maximum = 10000;
            numGb.Value = Math.Max(1, Math.Min(10000, (decimal)(Settings.BombMinBytes / (1024 * 1024 * 1024))));
            g3.Controls.Add(numGb);
            UiKit.Label(g3, "GB 이상일 때 확인", 366, 24, 160, 18);

            GroupBox g5 = UiKit.Group(this, "화면과 탐색기", 14, 292, 572, 84);
            UiKit.Label(g5, "테마", 12, 24, 52, 18);
            cmbTheme = UiKit.Combo(g5, 66, 20, 104);
            cmbTheme.Items.AddRange(new object[] { "시스템 설정", "밝게", "어둡게" });
            cmbTheme.SelectedIndex = Settings.Theme == "light" ? 1 : Settings.Theme == "dark" ? 2 : 0;
            UiKit.Label(g5, "언어", 184, 24, 62, 18);
            cmbLang = UiKit.Combo(g5, 250, 20, 90);
            cmbLang.Items.AddRange(new object[] { "한국어", "English" });
            cmbLang.SelectedIndex = Settings.Language == "en" ? 1 : 0;
            UiKit.Label(g5, "(테마·언어는 창을 다시 열면 적용됩니다)", 348, 24, 216, 18);
            Button bMenuOn = UiKit.Button(g5, "우클릭 메뉴 등록", 12, 48, 170, 28);
            Button bMenuOff = UiKit.Button(g5, "우클릭 메뉴 해제", 190, 48, 170, 28);
            UiKit.Label(g5, "Windows 11 은 '추가 옵션 표시' 안에 나옵니다.", 368, 54, 200, 18);
            bMenuOn.Click += delegate
            {
                string err = ShellMenu.Register();
                MessageBox.Show(this, err ?? "탐색기 우클릭 메뉴를 등록했습니다.\nWindows 11 에서는 우클릭 후 '추가 옵션 표시'를 누르면 보입니다.", "몽집");
            };
            bMenuOff.Click += delegate
            {
                string err = ShellMenu.Unregister();
                MessageBox.Show(this, err ?? "탐색기 우클릭 메뉴를 해제했습니다.", "몽집");
            };

            GroupBox g4 = UiKit.Group(this, "파일 연결 (더블클릭으로 몽집 열기)", 14, 384, 572, 200);
            lstExt = new CheckedListBox();
            lstExt.SetBounds(12, 20, 250, 172);
            lstExt.CheckOnClick = true;
            lstExt.MultiColumn = true;
            lstExt.ColumnWidth = 80;
            foreach (string e in Engine.AssociableExts) lstExt.Items.Add(e, Shell.IsAssociated(e));
            g4.Controls.Add(lstExt);
            Button bApply = UiKit.Button(g4, "선택 항목을 NoAdZip에 연결", 276, 20, 280, 30);
            Button bRemove = UiKit.Button(g4, "선택 항목 연결 해제", 276, 56, 280, 30);
            Button bDefault = UiKit.Button(g4, "Windows 기본 앱 설정 열기", 276, 92, 280, 30);
            Label note = UiKit.Label(g4, "Windows 10/11 은 기본 앱을 사용자가 직접 골라야 합니다. [연결] 후 기본 앱 설정에서 몽집을 선택하세요.", 276, 128, 285, 64);
            bApply.Click += delegate
            {
                List<string> sel = CheckedExts();
                if (sel.Count == 0) { MessageBox.Show(this, "연결할 확장자를 체크하세요.", "몽집"); return; }
                string err = Shell.Associate(sel);
                MessageBox.Show(this, err ?? (sel.Count + "개 확장자를 등록했습니다.\n기본 앱 설정에서 몽집을 선택하면 더블클릭으로 열립니다."), "몽집");
            };
            bRemove.Click += delegate
            {
                List<string> sel = CheckedExts();
                if (sel.Count == 0) { MessageBox.Show(this, "해제할 확장자를 체크하세요.", "몽집"); return; }
                string err = Shell.Unassociate(sel);
                MessageBox.Show(this, err ?? "연결을 해제했습니다.", "몽집");
            };
            bDefault.Click += delegate { try { Process.Start("ms-settings:defaultapps"); } catch { } };

            Button bLog = UiKit.Button(this, "로그 폴더 열기", 14, 612, 110, 28);
            bLog.Click += delegate { try { Process.Start("explorer.exe", Util.Quote(Log.LogDir)); } catch { } };

            Button ok, cancel;
            UiKit.OkCancel(this, out ok, out cancel);
            ok.Click += delegate { Apply(); };
        }

        List<string> CheckedExts()
        {
            List<string> l = new List<string>();
            foreach (object o in lstExt.CheckedItems) l.Add((string)o);
            return l;
        }

        void Apply()
        {
            Settings.DefaultFormat = FmtVals[cmbFmt.SelectedIndex];
            Settings.DefaultLevel = LevelVals[cmbLevel.SelectedIndex];
            Settings.Threads = ThreadVals[cmbThreads.SelectedIndex];
            Settings.ZipUtf8Names = chkUtf8.Checked;
            Settings.ExtractMode = rbHere.Checked ? "here" : rbAsk.Checked ? "ask" : "folder";
            Settings.Overwrite = OwVals[cmbOw.SelectedIndex];
            Settings.CodePage = CodePages.Get(cmbCp);
            Settings.OpenFolderAfter = chkOpen.Checked;
            Settings.ConfirmExecOpen = chkExec.Checked;
            Settings.BombRatio = (int)numRatio.Value;
            Settings.BombMinBytes = (long)numGb.Value * 1024L * 1024 * 1024;
            Settings.Theme = cmbTheme.SelectedIndex == 1 ? "light" : cmbTheme.SelectedIndex == 2 ? "dark" : "system";
            Settings.Language = cmbLang.SelectedIndex == 1 ? "en" : "ko";
            Settings.Save();
        }

        public static void Open(IWin32Window owner)
        {
            using (SettingsForm f = new SettingsForm()) f.ShowDialog(owner);
        }
    }
}
