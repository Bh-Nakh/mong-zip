using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace NoAdZip
{
    class Node
    {
        public string Name;
        public string FullPath;
        public bool IsDir;
        public ArchiveEntry Entry;
        public Node Parent;
        public Dictionary<string, Node> Kids = new Dictionary<string, Node>(StringComparer.OrdinalIgnoreCase);
        public long Size;
        public long Packed;
        public int Files;
        public int Folders;
        public string Modified = "";
        public bool Encrypted;
    }

    static class SysIcons
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        struct SHFILEINFO
        {
            public IntPtr hIcon;
            public int iIcon;
            public uint dwAttributes;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Auto)]
        static extern IntPtr SHGetFileInfo(string path, uint attr, ref SHFILEINFO fi, uint cb, uint flags);

        [DllImport("user32.dll")]
        static extern bool DestroyIcon(IntPtr h);

        public static Icon Get(string nameOrExt, bool dir)
        {
            SHFILEINFO fi = new SHFILEINFO();
            uint attr = dir ? 0x10u : 0x80u;
            IntPtr r = SHGetFileInfo(nameOrExt, attr, ref fi, (uint)Marshal.SizeOf(fi), 0x100 | 0x1 | 0x10);
            if (r == IntPtr.Zero || fi.hIcon == IntPtr.Zero) return null;
            Icon ic = (Icon)Icon.FromHandle(fi.hIcon).Clone();
            DestroyIcon(fi.hIcon);
            return ic;
        }
    }

    class MainForm : DialogBase
    {
        ArchiveInfo info;
        Node root, cur;
        string curCp;
        readonly ToolStrip tools;
        readonly ToolStripButton btnOpen, btnNew, btnExtract, btnExtractSel, btnTest, btnSettings;
        readonly ToolStripComboBox cmbCp;
        readonly TextBox txtPath;
        readonly Button btnUp;
        readonly ListView lv;
        readonly Label lblHint;
        readonly ToolStripStatusLabel stLeft, stRight;
        readonly ImageList il = new ImageList();
        readonly Dictionary<string, int> iconIdx = new Dictionary<string, int>();
        int sortCol;
        bool sortAsc = true;
        bool loadingCp;
        readonly ToolStripButton btnPreview;
        readonly Panel pnlPrev;
        readonly PictureBox pic;
        readonly TextBox txtPrev;
        readonly System.Windows.Forms.Timer prevTimer = new System.Windows.Forms.Timer();
        int prevToken;
        static readonly string[] ImgExts = { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".ico", ".tif", ".tiff" };
        static readonly string[] TxtExts = { ".txt", ".log", ".md", ".json", ".xml", ".csv", ".ini", ".cfg", ".conf", ".yml", ".yaml", ".html", ".htm", ".css", ".js",
            ".cs", ".py", ".java", ".c", ".cpp", ".h", ".bat", ".cmd", ".ps1", ".sh", ".sql", ".srt", ".toml", ".tsv", ".reg", ".inf" };

        public MainForm()
        {
            Text = "몽집";
            Font = SystemFonts.MessageBoxFont;
            ClientSize = new Size(860, 520);
            MinimumSize = new Size(560, 320);
            StartPosition = FormStartPosition.CenterScreen;
            Program.ApplyIcon(this);
            if (Program.NoFocus)
            {
                StartPosition = FormStartPosition.Manual;
                Location = new Point(30, -1060);
            }
            AllowDrop = true;
            KeyPreview = true;
            curCp = Settings.CodePage;

            il.ImageSize = new Size(16, 16);
            il.ColorDepth = ColorDepth.Depth32Bit;

            lv = new ListView();
            lv.View = View.Details;
            lv.FullRowSelect = true;
            lv.MultiSelect = true;
            lv.HideSelection = false;
            lv.SmallImageList = il;
            lv.AllowDrop = true;
            lv.Dock = DockStyle.Fill;
            lv.Columns.Add("이름", 280);
            lv.Columns.Add("크기", 90, HorizontalAlignment.Right);
            lv.Columns.Add("압축 크기", 90, HorizontalAlignment.Right);
            lv.Columns.Add("종류", 110);
            lv.Columns.Add("수정한 날짜", 140);
            lv.Columns.Add("암호", 50);
            lv.ColumnClick += OnColumnClick;
            lv.ItemActivate += delegate { OpenSelected(); };
            lv.KeyDown += OnListKeyDown;

            lblHint = new Label();
            lblHint.Text = "압축파일을 이 창에 끌어다 놓거나, 위의 [열기]를 눌러 선택하세요.\n\n파일·폴더를 끌어다 놓으면 압축 창이 열립니다.";
            lblHint.TextAlign = ContentAlignment.MiddleCenter;
            lblHint.Dock = DockStyle.Fill;
            lblHint.ForeColor = SystemColors.GrayText;

            Panel top = new Panel();
            top.Dock = DockStyle.Top;
            top.Height = 30;
            btnUp = new Button();
            btnUp.Text = "상위";
            btnUp.SetBounds(4, 3, 56, 24);
            btnUp.Click += delegate { GoUp(); };
            txtPath = new TextBox();
            txtPath.ReadOnly = true;
            txtPath.SetBounds(66, 4, 700, 23);
            txtPath.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            top.Controls.Add(btnUp);
            top.Controls.Add(txtPath);
            top.Resize += delegate { txtPath.Width = Math.Max(100, top.ClientSize.Width - 72); };

            tools = new ToolStrip();
            tools.GripStyle = ToolStripGripStyle.Hidden;
            btnOpen = Btn("열기 (Ctrl+O)", delegate { DoOpenDialog(); });
            btnNew = Btn("새 압축 (Ctrl+N)", delegate { DoNewCompress(null); });
            tools.Items.Add(new ToolStripSeparator());
            btnExtract = Btn("전체 풀기", delegate { DoExtract(false); });
            btnExtractSel = Btn("선택 풀기", delegate { DoExtract(true); });
            btnTest = Btn("검사", delegate { if (info != null) Flows.TestArchive(this, info.SourcePath); });
            tools.Items.Add(new ToolStripSeparator());
            ToolStripLabel lc = new ToolStripLabel("파일명 인코딩");
            tools.Items.Add(lc);
            cmbCp = new ToolStripComboBox();
            cmbCp.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbCp.Width = 150;
            foreach (string[] p in CodePages.All) cmbCp.Items.Add(p[1]);
            int ci = Array.FindIndex(CodePages.All, delegate (string[] p) { return p[0] == curCp; });
            cmbCp.SelectedIndex = ci < 0 ? 0 : ci;
            cmbCp.SelectedIndexChanged += delegate
            {
                if (loadingCp) return;
                curCp = CodePages.All[cmbCp.SelectedIndex][0];
                if (info != null) OpenArchivePath(info.SourcePath);
            };
            tools.Items.Add(cmbCp);
            tools.Items.Add(new ToolStripSeparator());
            btnPreview = Btn("미리보기", null);
            btnPreview.CheckOnClick = true;
            tools.Items.Add(new ToolStripSeparator());
            btnSettings = Btn("설정", delegate { SettingsForm.Open(this); });
            tools.Items.Add(new ToolStripSeparator());
            Btn("업데이트 확인", delegate { MongUpdate.UpdateUi.CheckNow(this, "mongzip", "몽집", Util.VersionNumber, null); });

            StatusStrip ss = new StatusStrip();
            stLeft = new ToolStripStatusLabel();
            stLeft.Spring = true;
            stLeft.TextAlign = ContentAlignment.MiddleLeft;
            stRight = new ToolStripStatusLabel();
            ss.Items.Add(stLeft);
            ss.Items.Add(stRight);
            ToolStripStatusLabel stVer = new ToolStripStatusLabel(Util.VersionText);
            stVer.ForeColor = SystemColors.GrayText;
            stVer.ToolTipText = "클릭하면 업데이트를 확인합니다";
            stVer.Click += delegate { MongUpdate.UpdateUi.CheckNow(this, "mongzip", "몽집", Util.VersionNumber, null); };
            ss.Items.Add(stVer);

            ContextMenuStrip cm = new ContextMenuStrip();
            cm.Items.Add("열기", null, delegate { OpenSelected(); });
            cm.Items.Add("선택 항목 풀기...", null, delegate { DoExtract(true); });
            cm.Items.Add("전체 풀기...", null, delegate { DoExtract(false); });
            lv.ContextMenuStrip = cm;
            cm.Opening += delegate (object s, System.ComponentModel.CancelEventArgs e) { e.Cancel = info == null || lv.SelectedItems.Count == 0; };

            Splitter split = new Splitter();
            split.Dock = DockStyle.Right;
            split.Width = 5;
            split.Visible = false;
            pnlPrev = new Panel();
            pnlPrev.Dock = DockStyle.Right;
            pnlPrev.Width = 320;
            pnlPrev.Visible = false;
            pic = new PictureBox();
            pic.Dock = DockStyle.Fill;
            pic.SizeMode = PictureBoxSizeMode.Zoom;
            txtPrev = new TextBox();
            txtPrev.Multiline = true;
            txtPrev.ReadOnly = true;
            txtPrev.ScrollBars = ScrollBars.Both;
            txtPrev.WordWrap = false;
            txtPrev.Dock = DockStyle.Fill;
            pnlPrev.Controls.Add(pic);
            pnlPrev.Controls.Add(txtPrev);
            prevTimer.Interval = 350;
            prevTimer.Tick += delegate { prevTimer.Stop(); DoPreview(); };
            lv.SelectedIndexChanged += delegate { if (btnPreview.Checked) { prevTimer.Stop(); prevTimer.Start(); } };
            btnPreview.CheckedChanged += delegate
            {
                pnlPrev.Visible = split.Visible = btnPreview.Checked;
                if (btnPreview.Checked) DoPreview();
            };

            Controls.Add(lv);
            Controls.Add(lblHint);
            Controls.Add(split);
            Controls.Add(pnlPrev);
            Controls.Add(top);
            Controls.Add(tools);
            Controls.Add(ss);
            lblHint.BringToFront();

            DragEnter += OnDragEnter;
            DragDrop += OnDragDrop;
            lv.DragEnter += OnDragEnter;
            lv.DragDrop += OnDragDrop;
            FormClosed += delegate { Flows.CleanupTemp(); };
            // 앱을 실행할 때 새 버전이 있는지 확인 (있을 때만 안내, 사용자가 눌러야 받는다)
            Shown += delegate { if (!Program.NoFocus) MongUpdate.UpdateUi.CheckAtStartup(this, "mongzip", "몽집", Util.VersionNumber, null); };
            UpdateState();
        }

        ToolStripButton Btn(string text, EventHandler click)
        {
            ToolStripButton b = new ToolStripButton(text);
            b.DisplayStyle = ToolStripItemDisplayStyle.Text;
            b.Click += click;
            tools.Items.Add(b);
            return b;
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.O)) { DoOpenDialog(); return true; }
            if (keyData == (Keys.Control | Keys.N)) { DoNewCompress(null); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        // ---------- 열기 ----------

        void DoOpenDialog()
        {
            using (OpenFileDialog d = new OpenFileDialog())
            {
                d.Title = "압축파일 열기";
                d.Filter = "압축파일|*.zip;*.7z;*.rar;*.tar;*.gz;*.tgz;*.bz2;*.tbz2;*.xz;*.txz;*.iso;*.cab;*.lzh;*.arj;*.001;*.jar;*.apk|모든 파일|*.*";
                if (d.ShowDialog(this) == DialogResult.OK) OpenArchivePath(d.FileName);
            }
        }

        public void OpenArchivePath(string path)
        {
            if (!File.Exists(path)) { Msg.Error(this, "파일이 없습니다.\n" + path); return; }
            ArchiveInfo i = Flows.OpenArchive(this, path, curCp, null);
            if (i == null) return;
            info = i;
            BuildTree();
            Text = Path.GetFileName(path) + " - 몽집";
            // 자동 감지로 인코딩이 바뀌었으면 콤보에 반영
            if (curCp == "auto" && info.CodePage != "auto")
            {
                int idx = Array.FindIndex(CodePages.All, delegate (string[] p) { return p[0] == info.CodePage; });
                if (idx >= 0) { loadingCp = true; cmbCp.SelectedIndex = idx; curCp = info.CodePage; loadingCp = false; }
            }
            ShowNode(root);
            UpdateState();
        }

        void UpdateState()
        {
            bool has = info != null;
            btnExtract.Enabled = btnExtractSel.Enabled = btnTest.Enabled = has;
            lblHint.Visible = !has;
            lv.Visible = has;
            if (!has) { stLeft.Text = "압축파일을 열어 주세요."; stRight.Text = ""; txtPath.Text = ""; btnUp.Enabled = false; }
        }

        // ---------- 트리 ----------

        void BuildTree()
        {
            root = new Node();
            root.Name = "";
            root.FullPath = "";
            root.IsDir = true;
            char[] seps = { '\\', '/' };
            foreach (ArchiveEntry e in info.Entries)
            {
                string[] segs = e.Path.Split(seps, StringSplitOptions.RemoveEmptyEntries);
                if (segs.Length == 0) continue;
                Node n = root;
                for (int i = 0; i < segs.Length; i++)
                {
                    bool last = i == segs.Length - 1;
                    Node k;
                    if (!n.Kids.TryGetValue(segs[i], out k))
                    {
                        k = new Node();
                        k.Name = segs[i];
                        k.Parent = n;
                        k.FullPath = n.FullPath.Length == 0 ? segs[i] : n.FullPath + "\\" + segs[i];
                        k.IsDir = !last || e.IsDir;
                        n.Kids[segs[i]] = k;
                    }
                    if (last)
                    {
                        k.Entry = e;
                        k.Modified = e.Modified;
                        k.Encrypted = e.Encrypted;
                        if (!e.IsDir) { k.Size = e.Size; k.Packed = e.Packed; }
                    }
                    n = k;
                }
            }
            Sum(root);
        }

        static void Sum(Node n)
        {
            n.Files = 0;
            n.Folders = 0;
            if (!n.IsDir) { n.Files = 1; return; }
            long size = 0, packed = 0;
            bool enc = false;
            foreach (Node k in n.Kids.Values)
            {
                Sum(k);
                size += k.Size;
                packed += k.Packed;
                n.Files += k.Files;
                n.Folders += k.IsDir ? 1 + k.Folders : 0;
                enc = enc || k.Encrypted;
            }
            if (n.Parent != null) { n.Size = size; n.Packed = packed; n.Encrypted = enc; }
        }

        int IconFor(Node n)
        {
            string key = n.IsDir ? "<dir>" : Path.GetExtension(n.Name).ToLowerInvariant();
            if (key.Length == 0) key = "<none>";
            int idx;
            if (iconIdx.TryGetValue(key, out idx)) return idx;
            Icon ic = null;
            try { ic = SysIcons.Get(n.IsDir ? "folder" : (key == "<none>" ? "file" : "file" + key), n.IsDir); } catch { }
            if (ic == null) { iconIdx[key] = -1; return -1; }
            il.Images.Add(ic);
            idx = il.Images.Count - 1;
            iconIdx[key] = idx;
            return idx;
        }

        static string KindOf(Node n)
        {
            if (n.IsDir) return "폴더";
            string e = Path.GetExtension(n.Name);
            return e.Length > 1 ? e.Substring(1).ToUpperInvariant() + " 파일" : "파일";
        }

        void ShowNode(Node n)
        {
            cur = n;
            List<Node> kids = new List<Node>(n.Kids.Values);
            kids.Sort(Compare);
            lv.BeginUpdate();
            lv.Items.Clear();
            foreach (Node k in kids)
            {
                ListViewItem it = new ListViewItem(k.Name);
                it.ImageIndex = IconFor(k);
                it.SubItems.Add(k.IsDir && k.Entry == null && k.Size == 0 && k.Files == 0 ? "" : Util.FormatSize(k.Size));
                it.SubItems.Add(Util.FormatSize(k.Packed));
                it.SubItems.Add(KindOf(k));
                it.SubItems.Add(k.Modified);
                it.SubItems.Add(k.Encrypted ? "예" : "");
                it.Tag = k;
                lv.Items.Add(it);
            }
            lv.EndUpdate();
            txtPath.Text = info.SourcePath + (n.FullPath.Length > 0 ? "  >  " + n.FullPath : "");
            btnUp.Enabled = n.Parent != null;
            stLeft.Text = string.Format("{0}개 파일 / {1}개 폴더  (현재 폴더 {2}개 항목)", info.FileCount, CountDirs(), n.Kids.Count);
            string cp = info.CodePage == "auto" ? "" : "  |  인코딩 " + info.CodePage;
            long phys = info.PhysicalSize > 0 ? info.PhysicalSize : new FileInfo(info.SourcePath).Length;
            double ratio = info.TotalSize > 0 ? 100.0 * phys / info.TotalSize : 0;
            stRight.Text = string.Format("{0}  |  풀면 {1}  |  압축파일 {2} ({3:0}%){4}", info.Type.ToUpperInvariant(),
                Util.FormatSize(info.TotalSize), Util.FormatSize(phys), ratio, cp);
        }

        int CountDirs()
        {
            int n = 0;
            if (root != null) n = root.Folders;
            return n;
        }

        int Compare(Node a, Node b)
        {
            if (a.IsDir != b.IsDir) return a.IsDir ? -1 : 1;
            int r;
            switch (sortCol)
            {
                case 1: r = a.Size.CompareTo(b.Size); break;
                case 2: r = a.Packed.CompareTo(b.Packed); break;
                case 3: r = string.Compare(KindOf(a), KindOf(b), StringComparison.CurrentCultureIgnoreCase); break;
                case 4: r = string.CompareOrdinal(a.Modified, b.Modified); break;
                case 5: r = a.Encrypted.CompareTo(b.Encrypted); break;
                default: r = string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase); break;
            }
            if (r == 0) r = string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase);
            return sortAsc ? r : -r;
        }

        void OnColumnClick(object s, ColumnClickEventArgs e)
        {
            if (e.Column == sortCol) sortAsc = !sortAsc; else { sortCol = e.Column; sortAsc = true; }
            if (cur != null) ShowNode(cur);
        }

        void GoUp()
        {
            if (cur != null && cur.Parent != null) ShowNode(cur.Parent);
        }

        void OnListKeyDown(object s, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Back) { GoUp(); e.Handled = true; }
            else if (e.KeyCode == Keys.A && e.Control)
            {
                foreach (ListViewItem it in lv.Items) it.Selected = true;
                e.Handled = true;
            }
        }

        // ---------- 미리보기 ----------

        // 개발용: 이름이 name 인 항목을 골라 미리보기를 켠다.
        public void DevPreview(string name)
        {
            btnPreview.Checked = true;
            foreach (ListViewItem it in lv.Items)
                if (string.Equals(it.Text, name, StringComparison.OrdinalIgnoreCase)) { it.Selected = true; it.Focused = true; break; }
            prevTimer.Stop();
            DoPreview();
        }

        void ShowPrevText(string s)
        {
            if (pic.Image != null) { Image old = pic.Image; pic.Image = null; old.Dispose(); }
            pic.Visible = false;
            txtPrev.Visible = true;
            txtPrev.Text = s;
        }

        void DoPreview()
        {
            int token = ++prevToken;
            if (info == null || lv.SelectedItems.Count == 0) { ShowPrevText(""); return; }
            if (lv.SelectedItems.Count > 1) { ShowPrevText(lv.SelectedItems.Count + "개 항목 선택됨"); return; }
            Node n = (Node)lv.SelectedItems[0].Tag;
            string nl = "\r\n";
            string head = n.Name + nl + "크기 " + Util.FormatSize(n.Size) + (n.Modified.Length > 0 ? nl + "수정 " + n.Modified : "");
            if (n.IsDir) { ShowPrevText(head + nl + nl + "파일 " + n.Files + "개"); return; }
            string ext = Path.GetExtension(n.Name).ToLowerInvariant();
            bool isImg = Array.IndexOf(ImgExts, ext) >= 0, isTxt = Array.IndexOf(TxtExts, ext) >= 0;
            if (n.Entry == null || (!isImg && !isTxt)) { ShowPrevText(head + nl + nl + "미리보기를 지원하지 않는 형식입니다."); return; }
            if (n.Size > (isImg ? 30L : 20L) * 1024 * 1024) { ShowPrevText(head + nl + nl + "파일이 커서 미리보기를 생략합니다."); return; }
            if (n.Encrypted && string.IsNullOrEmpty(info.Password)) { ShowPrevText(head + nl + nl + "암호가 걸린 파일입니다. 더블클릭하면 암호를 묻습니다."); return; }
            ShowPrevText(head + nl + nl + "불러오는 중...");
            ArchiveInfo inf = info;
            ArchiveEntry en = n.Entry;
            System.Threading.Thread th = new System.Threading.Thread(delegate ()
            {
                string dir = Path.Combine(Util.TempRoot, "prev_" + Guid.NewGuid().ToString("N").Substring(0, 8));
                Image img = null;
                string text = null;
                string err = null;
                try
                {
                    Directory.CreateDirectory(dir);
                    ExtractOptions o = new ExtractOptions();
                    o.Archive = inf.FilePath;
                    o.Dest = dir;
                    o.CodePage = inf.CodePage;
                    o.Password = inf.Password;
                    o.Overwrite = "all";
                    o.Include = new List<string>();
                    o.Include.Add(en.Path);
                    RunResult r = Engine.Extract(o, new JobContext());
                    string f = Path.Combine(dir, en.Path);
                    if (!File.Exists(f)) err = r.WrongPassword ? "암호가 맞지 않습니다." : "파일을 풀지 못했습니다.";
                    else if (isImg)
                    {
                        byte[] b = File.ReadAllBytes(f);
                        img = Image.FromStream(new MemoryStream(b));
                    }
                    else
                    {
                        byte[] b;
                        using (FileStream fs = File.OpenRead(f))
                        {
                            b = new byte[(int)Math.Min(fs.Length, 131072)];
                            int got = 0, k;
                            while (got < b.Length && (k = fs.Read(b, got, b.Length - got)) > 0) got += k;
                            Array.Resize(ref b, got);
                        }
                        try { text = new System.Text.UTF8Encoding(false, true).GetString(b); }
                        catch { text = System.Text.Encoding.GetEncoding(949).GetString(b); }
                        if (text.Length > 0 && text[0] == '\uFEFF') text = text.Substring(1);
                        text = text.Replace("\r\n", "\n").Replace("\n", "\r\n");
                    }
                }
                catch (Exception ex) { err = ex.Message; }
                finally { Util.TryDelete(dir); }
                try
                {
                    BeginInvoke(new MethodInvoker(delegate
                    {
                        if (token != prevToken) { if (img != null) img.Dispose(); return; }
                        if (err != null) ShowPrevText(head + nl + nl + err);
                        else if (img != null)
                        {
                            ShowPrevText("");
                            txtPrev.Visible = false;
                            pic.Image = img;
                            pic.Visible = true;
                        }
                        else ShowPrevText(text);
                    }));
                }
                catch { if (img != null) img.Dispose(); }
            });
            th.IsBackground = true;
            th.Start();
        }

        // ---------- 동작 ----------

        void OpenSelected()
        {
            if (info == null || lv.SelectedItems.Count == 0) return;
            Node n = (Node)lv.SelectedItems[0].Tag;
            if (n.IsDir) ShowNode(n);
            else if (n.Entry != null) Flows.OpenEntry(this, info, n.Entry);
        }

        void DoExtract(bool selectedOnly)
        {
            if (info == null) return;
            List<ArchiveEntry> sel = null;
            if (selectedOnly)
            {
                if (lv.SelectedItems.Count == 0) { Msg.Info(this, "풀 항목을 먼저 선택하세요."); return; }
                sel = new List<ArchiveEntry>();
                foreach (ListViewItem it in lv.SelectedItems)
                {
                    Node n = (Node)it.Tag;
                    if (n.Entry != null) sel.Add(n.Entry);
                    else
                    {
                        ArchiveEntry synth = new ArchiveEntry();
                        synth.Path = n.FullPath;
                        synth.IsDir = true;
                        sel.Add(synth);
                    }
                }
            }
            Flows.ExtractWithDialog(this, info, sel);
        }

        public void DoNewCompress(IEnumerable<string> initial)
        {
            CompressOptions o = CompressForm.Ask(this, initial ?? new string[0]);
            if (o != null) Flows.Compress(this, o);
        }

        // ---------- 드래그 앤 드롭 ----------

        void OnDragEnter(object s, DragEventArgs e)
        {
            e.Effect = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        }

        void OnDragDrop(object s, DragEventArgs e)
        {
            string[] files = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (files == null || files.Length == 0) return;
            List<string> archives = new List<string>();
            foreach (string f in files) if (Engine.IsArchive(f)) archives.Add(f);
            BeginInvoke(new MethodInvoker(delegate
            {
                if (archives.Count == files.Length)
                {
                    if (archives.Count == 1) OpenArchivePath(archives[0]);
                    else if (Msg.Confirm(this, archives.Count + "개 압축파일을 모두 풀까요?\n(아니오를 누르면 취소)")) Flows.ExtractArchives(this, archives, "default", null);
                }
                else DoNewCompress(files);
            }));
        }
    }
}
