using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace NoAdZip
{
    class Request
    {
        public string Verb;
        public string Xto;
        public string Fmt = "zip";
        public List<string> Files = new List<string>();
        public List<string> Only = new List<string>();

        public string Key { get { return (Verb ?? "") + "|" + (Xto ?? "") + "|" + Fmt; } }
    }

    static class Program
    {
        static Icon appIcon;

        public static Icon AppIcon
        {
            get
            {
                if (appIcon == null)
                {
                    try { appIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
                }
                return appIcon;
            }
        }

        public static void ApplyIcon(Form f)
        {
            Icon ic = AppIcon;
            if (ic != null) f.Icon = ic;
        }

        const string Usage =
            "몽집 - 광고 없는 압축 프로그램\r\n\r\n" +
            "사용법:\r\n" +
            "  몽집.exe <압축파일>            압축파일 열기\r\n" +
            "  몽집.exe <파일·폴더...>        압축 창 열기\r\n" +
            "  /x <압축파일...>                  기본 방식으로 풀기\r\n" +
            "  /xhere  /xfolder  /xdialog        현재 폴더 / 이름 폴더 / 옵션 창\r\n" +
            "  /xto <폴더> <압축파일...>         지정 폴더에 풀기\r\n" +
            "  /only:<항목경로>                  (풀기와 함께) 지정한 파일·폴더만 풀기, 여러 번 지정 가능\r\n" +
            "  /test <압축파일>                  무결성 검사\r\n" +
            "  /c:<형식> <파일...>               zip 7z tar tar.gz tar.bz2 tar.xz gz bz2 xz 로 바로 압축\r\n" +
            "  /compress <파일...>               압축 창 열기\r\n" +
            "  /czip <파일...>  /c7z <파일...>   바로 압축\r\n" +
            "  /newfolder <폴더>                 폴더 안에 '새 폴더' 만들기\r\n" +
            "  /silent                           창 없이 실행 (자동화용, 종료코드 0=성공)\r\n" +
            "  /force                            /silent 에서 위험 확인을 모두 '예'로 처리";

        // 시험용: 창을 포커스 없이 2번 모니터 위치에 띄운다.
        public static bool NoFocus;

        static readonly string[] ShellVerbs = { "x", "xhere", "xfolder", "xdialog", "xto", "test", "compress", "czip", "c7z", "cfmt" };

        static Request Parse(string[] args)
        {
            Request r = new Request();
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                // 드라이브 맨 위는 탐색기가 "J:\" 로 넘기는데, 끝의 \" 가 따옴표로 읽혀 J:" 가 된다 → J:\ 로 되돌린다
                if (a.Length >= 2 && a.EndsWith("\"")) a = a.Substring(0, a.Length - 1) + "\\";
                string low = a.ToLowerInvariant();
                if (low == "/silent" || low == "--silent" || low == "/force" || low == "--force" || low == "/nofocus") continue;
                if (low == "/?" || low == "--help" || low == "-h") { r.Verb = "help"; continue; }
                if (low == "/xto" && i + 1 < args.Length) { r.Verb = "xto"; r.Xto = args[++i]; continue; }
                if (low.StartsWith("/c:")) { r.Verb = "cfmt"; r.Fmt = low.Substring(3); continue; }
                if (low.StartsWith("/only:")) { r.Only.Add(a.Substring(6)); continue; }
                if (low == "/x" || low == "/xhere" || low == "/xfolder" || low == "/xdialog" || low == "/test"
                    || low == "/compress" || low == "/czip" || low == "/c7z" || low == "/open" || low == "/dlg" || low == "/regmenu" || low == "/unregmenu" || low == "/newfolder" || low == "/unregall")
                {
                    r.Verb = low.Substring(1);
                    continue;
                }
                if (a.Length == 0) continue;
                r.Files.Add(a);
            }
            return r;
        }

        [STAThread]
        static int Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            foreach (string a in args)
            {
                string low = a.ToLowerInvariant();
                if (low == "/silent" || low == "--silent") Msg.Silent = true;
                else if (low == "/force" || low == "--force") Msg.Force = true;
                else if (low == "/nofocus") NoFocus = true;
            }

            Msg.AttachConsoleIfSilent();
            Settings.Load();
            Theme.Init();
            Shell.RefreshIfStale();
            Application.ThreadException += delegate (object s, System.Threading.ThreadExceptionEventArgs e)
            {
                Log.Write("예외: " + e.Exception);
                Msg.Error(null, "예기치 않은 오류가 발생했습니다.\n" + e.Exception.Message);
            };

            Request first = Parse(args);
            if (first.Verb == "help")
            {
                if (Msg.Silent) Console.WriteLine(Usage); else MessageBox.Show(Usage, "몽집");
                return 0;
            }
            if (!Engine.Available)
            {
                Msg.Error(null, "압축 엔진(7z.exe, 7z.dll)을 찾을 수 없습니다.\n프로그램 폴더를 확인하세요:\n" + Util.AppDir);
                return 2;
            }
            Flows.PurgeStaleTemp();

            try
            {
                List<Request> reqs = new List<Request>();
                bool queueable = first.Verb != null && Array.IndexOf(ShellVerbs, first.Verb) >= 0 && first.Files.Count == 1
                    && (!Msg.Silent || Environment.GetEnvironmentVariable("NOADZIP_QUEUE") == "1");
                if (queueable)
                {
                    List<string[]> got = JobQueue.Join(args);
                    if (got == null) return 0;           // 대표 프로세스가 대신 처리한다
                    reqs = Merge(got);
                }
                else reqs.Add(first);

                int rc = 0;
                foreach (Request r in reqs) rc |= Dispatch(r);
                return rc;
            }
            catch (Exception ex)
            {
                Log.Write("치명적 오류: " + ex);
                Msg.Error(null, "오류: " + ex.Message);
                return 1;
            }
            finally
            {
                Flows.CleanupTemp();
            }
        }

        // 웹페이지 설치 확인용 로컬 핑 서버 (메인 창을 여는 경우에만 켠다)
        static void StartWebCheck()
        {
            MongWebCheck.WebCheck.Start("mongzip", "몽집", new int[] { 47111, 47112, 47113 });
        }

        // 같은 동작의 요청은 파일 목록을 합친다.
        static List<Request> Merge(List<string[]> all)
        {
            List<Request> res = new List<Request>();
            Dictionary<string, Request> byKey = new Dictionary<string, Request>();
            foreach (string[] a in all)
            {
                Request r = Parse(a);
                Request tgt;
                if (!byKey.TryGetValue(r.Key, out tgt)) { byKey[r.Key] = r; res.Add(r); continue; }
                foreach (string f in r.Files) if (!tgt.Files.Contains(f)) tgt.Files.Add(f);
                foreach (string o in r.Only) if (!tgt.Only.Contains(o)) tgt.Only.Add(o);
            }
            foreach (Request r in res) r.Files.Sort(StringComparer.OrdinalIgnoreCase);
            return res;
        }

        static int Dispatch(Request req)
        {
            string verb = req.Verb;
            List<string> files = req.Files;
            // 동사가 없으면 인수로 판단한다.
            if (verb == null)
            {
                if (files.Count == 0) { StartWebCheck(); Application.Run(new MainForm()); return 0; }
                bool allArc = true;
                foreach (string f in files) if (!Engine.IsArchive(f)) { allArc = false; break; }
                verb = allArc ? "open" : "compress";
            }

            switch (verb)
            {
                case "open":
                    {
                        StartWebCheck();
                        MainForm mf = new MainForm();
                        string first = files.Count > 0 ? files[0] : null;
                        if (first != null) mf.Shown += delegate { mf.OpenArchivePath(first); };
                        Application.Run(mf);
                        return 0;
                    }
                case "x":
                case "xhere":
                case "xfolder":
                case "xdialog":
                case "xto":
                    {
                        if (files.Count == 0) { Msg.Error(null, "풀 압축파일을 지정하세요."); return 1; }
                        string mode = verb == "x" ? "default" : verb == "xdialog" ? "dialog" : verb == "xto" ? "to" : verb == "xhere" ? "here" : "folder";
                        if (verb == "xto") Directory.CreateDirectory(req.Xto);
                        int ok = Flows.ExtractArchives(null, files, mode, req.Xto, req.Only);
                        return ok == files.Count ? 0 : 1;
                    }
                case "test":
                    {
                        if (files.Count == 0) { Msg.Error(null, "검사할 압축파일을 지정하세요."); return 1; }
                        bool all = true;
                        foreach (string f in files) all = Flows.TestArchive(null, f) && all;
                        return all ? 0 : 1;
                    }
                case "compress":
                    {
                        if (Msg.Silent) { Msg.Error(null, "/silent 에서는 /czip, /c7z 를 사용하세요."); return 1; }
                        CompressOptions co = CompressForm.Ask(null, files);
                        if (co == null) return 0;
                        return Flows.Compress(null, co) ? 0 : 1;
                    }
                case "dlg":
                    return RenderDialog(files);
                case "newfolder":
                    {
                        if (files.Count == 0 || !Directory.Exists(files[0])) { Msg.Error(null, "새 폴더를 만들 위치를 찾을 수 없습니다."); return 1; }
                        try
                        {
                            string made = Shell.NewFolder(files[0], !Msg.Silent);
                            if (Msg.Silent) Console.WriteLine(made);
                            return 0;
                        }
                        catch (Exception ex) { Msg.Error(null, "새 폴더를 만들 수 없습니다: " + ex.Message); return 1; }
                    }
                case "regmenu":
                    {
                        string err = ShellMenu.Register();
                        if (err != null) Msg.Error(null, err); else Msg.Info(null, "탐색기 우클릭 메뉴를 등록했습니다.");
                        return err == null ? 0 : 1;
                    }
                case "unregmenu":
                    {
                        string err = ShellMenu.Unregister();
                        if (err != null) Msg.Error(null, err); else Msg.Info(null, "탐색기 우클릭 메뉴를 해제했습니다.");
                        return err == null ? 0 : 1;
                    }
                case "unregall":
                    {
                        // 제거 프로그램용: 우클릭 메뉴와 파일 연결을 모두 해제한다.
                        string e1 = ShellMenu.Unregister();
                        string e2 = Shell.Unassociate(Engine.AssociableExts);
                        return e1 == null && e2 == null ? 0 : 1;
                    }
                case "czip":
                case "c7z":
                case "cfmt":
                    {
                        if (files.Count == 0) { Msg.Error(null, "압축할 파일을 지정하세요."); return 1; }
                        string f = verb == "czip" ? "zip" : verb == "c7z" ? "7z" : req.Fmt;
                        return Flows.QuickCompress(null, files, f) ? 0 : 1;
                    }
            }
            return 1;
        }

        // 개발용: 대화상자를 화면에 띄우지 않고(포커스 이동 없음) 그림 파일로 저장한다. /dlg 이름 저장경로.png [입력파일...]
        static int RenderDialog(List<string> a)
        {
            if (a.Count < 2) return 1;
            NoFocus = true;
            Msg.Silent = true;      // 메시지 상자가 떠서 멈추는 일이 없도록 한다
            string name = a[0], shot = a[1];
            List<string> rest = a.GetRange(2, a.Count - 2);
            Form f;
            switch (name)
            {
                case "extract": f = new ExtractForm(new string[] { rest.Count > 0 ? rest[0] : "sample.zip" }, ""); break;
                case "compress": f = new CompressForm(rest); break;
                case "settings": f = new SettingsForm(); break;
                case "password": f = new PasswordForm("sample.zip", false); break;
                case "conflict": f = new ConflictForm(3, "folder\\a.txt"); break;
                case "main":
                    {
                        MainForm mf = new MainForm();
                        if (rest.Count > 0) mf.OpenArchivePath(rest[0]);
                        string pv = Environment.GetEnvironmentVariable("NOADZIP_PREVIEW");
                        if (!string.IsNullOrEmpty(pv)) mf.DevPreview(pv);
                        f = mf;
                        break;
                    }
                default: return 1;
            }
            f.StartPosition = FormStartPosition.Manual;
            f.Location = new Point(30, -1060);
            f.Show();
            for (int i = 0; i < 20; i++) { Application.DoEvents(); System.Threading.Thread.Sleep(100); }
            using (Bitmap bmp = new Bitmap(f.Width, f.Height))
            {
                f.DrawToBitmap(bmp, new Rectangle(0, 0, f.Width, f.Height));
                bmp.Save(shot, System.Drawing.Imaging.ImageFormat.Png);
            }
            f.Close();
            return 0;
        }
    }
}
