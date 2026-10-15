using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;

namespace NoAdZip
{
    static class Flows
    {
        static readonly string SessionId = Guid.NewGuid().ToString("N").Substring(0, 8);

        // ---------- 열기 (목록 읽기 + 암호 재시도) ----------

        static readonly string[] OuterTypes = { "gzip", "bzip2", "xz", "lzma", "zstd", "z" };

        // .tar.gz / .tgz 처럼 압축 한 겹 안에 tar 하나만 든 경우
        static bool IsCompoundTar(ArchiveInfo i)
        {
            return Array.IndexOf(OuterTypes, i.Type.ToLowerInvariant()) >= 0
                && i.Entries.Count == 1 && !i.Entries[0].IsDir
                && i.Entries[0].Path.EndsWith(".tar", StringComparison.OrdinalIgnoreCase);
        }

        // 바깥 압축층을 임시 폴더에 풀고 안쪽 tar 의 목록을 돌려준다. 이후 모든 기능은 tar 기준으로 동작한다.
        static ArchiveInfo UnwrapTar(IWin32Window owner, ArchiveInfo outer, string cp)
        {
            SecurityReport sec = Engine.Analyze(outer);
            if (sec.BombSuspect && !Msg.ConfirmRisk(owner, string.Format("풀면 {0} 이 되며 압축률이 약 {1:0}배입니다.\n압축 폭탄일 수 있습니다. 계속 열까요?",
                Util.FormatSize(sec.TotalSize), sec.Ratio)))
                return null;
            try
            {
                long free = new DriveInfo(Path.GetPathRoot(Util.TempRoot)).AvailableFreeSpace;
                if (outer.Entries[0].Size > free)
                {
                    Msg.Error(owner, "임시 작업 공간이 부족합니다.\n필요: " + Util.FormatSize(outer.Entries[0].Size) + "\n남은 공간: " + Util.FormatSize(free));
                    return null;
                }
            }
            catch { }

            string dir = Path.Combine(Util.TempRoot, "open_" + SessionId, "tar_" + Guid.NewGuid().ToString("N").Substring(0, 6));
            Directory.CreateDirectory(dir);
            ExtractOptions o = new ExtractOptions();
            o.Archive = outer.FilePath;
            o.Dest = dir;
            o.Overwrite = "all";
            o.Password = outer.Password;
            RunResult r;
            try
            {
                r = Jobs.Run<RunResult>(owner, "압축 해제 준비 중", delegate (JobContext ctx) { return Engine.Extract(o, ctx); });
            }
            catch (OperationCanceledException) { return null; }
            catch (EngineException ee) { Msg.Error(owner, ee.Message); return null; }
            if (r.Cancelled) return null;
            string tar = Path.Combine(dir, outer.Entries[0].Path);
            if (r.Failed || !File.Exists(tar))
            {
                Msg.Error(owner, "압축 안의 tar 를 풀지 못했습니다.\n\n" + r.ErrorText());
                return null;
            }
            ArchiveInfo inner;
            try
            {
                inner = Jobs.Run<ArchiveInfo>(owner, "압축파일 여는 중", delegate (JobContext ctx) { return Engine.ListSmart(tar, null, cp, ctx); });
            }
            catch (OperationCanceledException) { return null; }
            catch (Exception ex) { Msg.Error(owner, "압축파일을 열 수 없습니다.\n" + ex.Message); return null; }
            inner.SourcePath = outer.SourcePath;
            inner.PhysicalSize = new FileInfo(outer.SourcePath).Length;
            inner.Type = "tar+" + outer.Type;
            return inner;
        }

        public static ArchiveInfo OpenArchive(IWin32Window owner, string path, string cp, string pw)
        {
            // 분할 압축의 중간 조각(.002 등)을 열면 첫 조각(.001)을 대신 연다.
            System.Text.RegularExpressions.Match sm = System.Text.RegularExpressions.Regex.Match(path, @"^(.*)\.(\d{3})$");
            if (sm.Success && sm.Groups[2].Value != "001" && File.Exists(sm.Groups[1].Value + ".001"))
                path = sm.Groups[1].Value + ".001";
            ArchiveInfo info = OpenArchiveOuter(owner, path, cp, pw);
            if (info != null && IsCompoundTar(info)) return UnwrapTar(owner, info, cp ?? Settings.CodePage);
            return info;
        }

        static ArchiveInfo OpenArchiveOuter(IWin32Window owner, string path, string cp, string pw)
        {
            if (cp == null) cp = Settings.CodePage;
            while (true)
            {
                try
                {
                    string pwNow = pw;
                    return Jobs.Run<ArchiveInfo>(owner, "압축파일 여는 중", delegate (JobContext ctx)
                    {
                        return Engine.ListSmart(path, pwNow, cp, ctx);
                    });
                }
                catch (OperationCanceledException) { return null; }
                catch (PasswordRequiredException pe)
                {
                    pw = PasswordForm.Ask(owner, Path.GetFileName(path), pe.Wrong);
                    if (pw == null) return null;
                }
                catch (EngineException ee)
                {
                    Msg.Error(owner, "압축파일을 열 수 없습니다.\n" + path + "\n\n" + ee.Message);
                    return null;
                }
            }
        }

        // ---------- 풀기 ----------

        public static string ComputeDest(ArchiveInfo info, string baseDir, bool makeFolder)
        {
            if (!makeFolder) return baseDir;
            HashSet<string> tops = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (ArchiveEntry e in info.Entries)
            {
                string p = e.Path.Replace('/', '\\').TrimStart('\\');
                int i = p.IndexOf('\\');
                tops.Add(i < 0 ? p : p.Substring(0, i));
                if (tops.Count > 1) break;
            }
            if (tops.Count <= 1) return baseDir;       // 최상위가 하나뿐이면 폴더를 또 만들지 않는다
            string name = Util.SafeFileName(Util.ArchiveBaseName(info.SourcePath));
            if (name.Length == 0) name = "extracted";
            return Path.Combine(baseDir, name);
        }

        // 압축파일 여러 개 풀기. mode: here | folder | dialog | to(지정 폴더, customDest)
        public static int ExtractArchives(IWin32Window owner, IList<string> archives, string mode, string customDest)
        {
            return ExtractArchives(owner, archives, mode, customDest, null);
        }

        // only 가 있으면 그 경로(폴더면 하위 전체)만 푼다.
        public static int ExtractArchives(IWin32Window owner, IList<string> archives, string mode, string customDest, List<string> only)
        {
            string baseMode = mode;
            ExtractChoice choice = null;
            if (mode == "default") baseMode = Settings.ExtractMode == "ask" ? "dialog" : Settings.ExtractMode;
            if (baseMode == "dialog")
            {
                choice = ExtractForm.Ask(owner, archives, Settings.LastDest);
                if (choice == null) return 0;
                if (choice.Mode == "custom") Settings.LastDest = choice.Dest;
                Settings.Overwrite = choice.Overwrite;
                Settings.OpenFolderAfter = choice.OpenFolder;
                Settings.CodePage = choice.CodePage;
                Settings.Save();
            }

            int ok = 0;
            foreach (string arc in archives)
            {
                string cp = choice != null ? choice.CodePage : Settings.CodePage;
                ArchiveInfo info = OpenArchive(owner, arc, cp, null);
                if (info == null) continue;

                string baseDir = Path.GetDirectoryName(Path.GetFullPath(arc));
                bool make = baseMode == "folder";
                if (baseMode == "to") { baseDir = customDest; make = false; }
                if (choice != null)
                {
                    if (choice.Mode == "custom") baseDir = choice.Dest;
                    make = choice.MakeFolder;
                }
                string ow = choice != null ? choice.Overwrite : Settings.Overwrite;
                List<ArchiveEntry> selection = null;
                if (only != null && only.Count > 0)
                {
                    selection = new List<ArchiveEntry>();
                    foreach (string p in only)
                    {
                        string norm = p.Replace('/', '\\').TrimEnd('\\');
                        ArchiveEntry s = new ArchiveEntry();
                        s.Path = norm;
                        foreach (ArchiveEntry e in info.Entries)
                        {
                            string ep = e.Path.Replace('/', '\\');
                            if (string.Equals(ep, norm, StringComparison.OrdinalIgnoreCase)) { s.Path = e.Path; s.IsDir = e.IsDir; break; }
                            if (ep.StartsWith(norm + "\\", StringComparison.OrdinalIgnoreCase)) s.IsDir = true;
                        }
                        selection.Add(s);
                    }
                    make = false;
                }
                string dest = ComputeDest(info, baseDir, make);
                if (ExtractCore(owner, info, dest, ow, selection, Settings.OpenFolderAfter)) ok++;
            }
            if (ok == archives.Count) Jobs.FinishAction(owner);
            return ok;
        }

        // 열려 있는 압축파일에서 선택 항목(또는 전체)을 푸는 동작. 선택 풀기는 이 경로로 호출한다.
        public static bool ExtractWithDialog(IWin32Window owner, ArchiveInfo info, List<ArchiveEntry> selection)
        {
            ExtractChoice c = ExtractForm.Ask(owner, new string[] { info.SourcePath }, Settings.LastDest);
            if (c == null) return false;
            if (c.Mode == "custom") Settings.LastDest = c.Dest;
            Settings.Overwrite = c.Overwrite;
            Settings.OpenFolderAfter = c.OpenFolder;
            Settings.Save();
            string baseDir = c.Mode == "custom" ? c.Dest : Path.GetDirectoryName(Path.GetFullPath(info.SourcePath));
            string dest = ComputeDest(info, baseDir, selection == null && c.MakeFolder);
            bool done = ExtractCore(owner, info, dest, c.Overwrite, selection, c.OpenFolder);
            if (done) Jobs.FinishAction(owner);
            return done;
        }

        static void AddWithChildren(List<ArchiveEntry> all, ArchiveEntry sel, HashSet<string> set)
        {
            set.Add(sel.Path);
            if (!sel.IsDir) return;
            string prefix = sel.Path.TrimEnd('\\', '/') + "\\";
            foreach (ArchiveEntry e in all)
                if (e.Path.Replace('/', '\\').StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) set.Add(e.Path);
        }

        public static bool ExtractCore(IWin32Window owner, ArchiveInfo info, string dest, string overwrite,
            List<ArchiveEntry> selection, bool openFolder)
        {
            string arcName = Path.GetFileName(info.SourcePath);
            SecurityReport sec = Engine.Analyze(info);

            // 1) 풀 대상과 제외 대상 정리
            HashSet<string> targetPaths = new HashSet<string>(StringComparer.Ordinal);
            if (selection == null)
                foreach (ArchiveEntry e in info.Entries) targetPaths.Add(e.Path);
            else
                foreach (ArchiveEntry e in selection) AddWithChildren(info.Entries, e, targetPaths);

            HashSet<string> skip = new HashSet<string>(StringComparer.Ordinal);
            foreach (string p in sec.BadPaths) if (targetPaths.Contains(p)) skip.Add(p);
            int badCount = skip.Count;
            if (badCount > 0)
            {
                string ex = string.Join("\n", new List<string>(skip).GetRange(0, Math.Min(3, skip.Count)).ToArray());
                if (!Msg.Confirm(owner, "이 압축파일에는 풀 폴더 밖으로 나가려는 위험한 경로가 " + badCount + "개 있습니다.\n(예: " + ex.Replace("\n", ", ") + ")\n\n해당 항목을 제외하고 나머지를 풀까요?"))
                    return false;
            }
            int linkCount = 0;
            foreach (string p in sec.Links) if (targetPaths.Contains(p)) { skip.Add(p); linkCount++; }
            if (linkCount > 0) Log.Write("심볼릭 링크 " + linkCount + "개는 보안상 제외");

            // 2) 압축 폭탄·디스크 공간
            if (selection == null && sec.BombSuspect)
            {
                if (!Msg.ConfirmRisk(owner, string.Format("풀면 {0} 이 되며 압축률이 약 {1:0}배입니다.\n압축 폭탄일 수 있습니다. 계속 풀까요?",
                    Util.FormatSize(sec.TotalSize), sec.Ratio)))
                    return false;
            }
            long need = 0;
            foreach (ArchiveEntry e in info.Entries)
                if (!e.IsDir && targetPaths.Contains(e.Path) && !skip.Contains(e.Path)) need += e.Size;
            try
            {
                string root = Path.GetPathRoot(Path.GetFullPath(dest));
                if (!string.IsNullOrEmpty(root) && !root.StartsWith("\\\\"))
                {
                    long free = new DriveInfo(root).AvailableFreeSpace;
                    if (need > free)
                    {
                        Msg.Error(owner, string.Format("저장 공간이 부족합니다.\n필요: {0}\n남은 공간: {1} ({2})", Util.FormatSize(need), Util.FormatSize(free), root));
                        return false;
                    }
                }
            }
            catch { }

            // 3) 암호
            bool needPw = false;
            foreach (ArchiveEntry e in info.Entries)
                if (e.Encrypted && targetPaths.Contains(e.Path) && !skip.Contains(e.Path)) { needPw = true; break; }
            if (needPw && string.IsNullOrEmpty(info.Password))
            {
                string pw = PasswordForm.Ask(owner, arcName, false);
                if (pw == null) return false;
                info.Password = pw;
            }

            // 4) 같은 이름 파일 처리
            if (overwrite == "ask")
            {
                int conflicts = 0;
                string sample = "";
                if (Directory.Exists(dest))
                {
                    foreach (ArchiveEntry e in info.Entries)
                    {
                        if (e.IsDir || !targetPaths.Contains(e.Path) || skip.Contains(e.Path)) continue;
                        string full = Path.Combine(dest, e.Path);
                        if (File.Exists(full)) { conflicts++; if (sample.Length == 0) sample = e.Path; }
                    }
                }
                if (conflicts > 0)
                {
                    overwrite = ConflictForm.Ask(owner, conflicts, sample);
                    if (overwrite == null) return false;
                }
                else overwrite = "all";
            }

            // 5) 실행
            ExtractOptions o = new ExtractOptions();
            o.Archive = info.FilePath;
            o.Dest = dest;
            o.CodePage = info.CodePage;
            o.Overwrite = overwrite;
            o.Threads = Settings.Threads;
            if (selection != null)
            {
                o.Include = new List<string>();
                foreach (string p in targetPaths) if (!skip.Contains(p)) o.Include.Add(p);
                if (o.Include.Count == 0) { Msg.Warn(owner, "풀 항목이 없습니다."); return false; }
            }
            else if (skip.Count > 0) o.Exclude = new List<string>(skip);

            RunResult r;
            while (true)
            {
                o.Password = info.Password;
                try
                {
                    ExtractOptions oo = o;
                    r = Jobs.Run<RunResult>(owner, "압축 푸는 중", delegate (JobContext ctx) { return Engine.Extract(oo, ctx); });
                }
                catch (OperationCanceledException) { return false; }
                catch (EngineException ee) { Msg.Error(owner, ee.Message); return false; }
                if (r.Cancelled) return false;
                if (r.Failed && r.WrongPassword)
                {
                    string pw = PasswordForm.Ask(owner, arcName, true);
                    if (pw == null) return false;
                    info.Password = pw;
                    o.Overwrite = "all";
                    continue;
                }
                break;
            }

            if (r.Failed)
            {
                Msg.Error(owner, "압축을 푸는 중 오류가 발생했습니다.\n\n" + r.ErrorText() + "\n\n로그: " + Log.TodayFile);
                return false;
            }
            string note = "";
            if (badCount > 0) note += "\n위험한 경로 " + badCount + "개 제외";
            if (linkCount > 0) note += "\n링크 " + linkCount + "개 제외";
            if (r.ExitCode == 1)
                Msg.Warn(owner, "일부 항목을 풀지 못했습니다.\n\n" + r.ErrorText() + note);
            else if (note.Length > 0)
                Msg.Info(owner, "압축 풀기 완료." + note);
            else if (Msg.Silent)
                Msg.Info(owner, "압축 풀기 완료: " + dest);

            if (openFolder && !Msg.Silent) OpenFolder(dest);
            return true;
        }

        public static void OpenFolder(string dir)
        {
            try { if (Directory.Exists(dir)) Process.Start("explorer.exe", Util.Quote(dir)); } catch { }
        }

        // ---------- 검사 ----------

        public static bool TestArchive(IWin32Window owner, string path)
        {
            ArchiveInfo info = OpenArchive(owner, path, null, null);
            if (info == null) return false;
            if (info.AnyEncrypted && string.IsNullOrEmpty(info.Password))
            {
                string first = PasswordForm.Ask(owner, Path.GetFileName(path), false);
                if (first == null) return false;
                info.Password = first;
            }
            RunResult r;
            while (true)
            {
                string pw = info.Password;
                try
                {
                    r = Jobs.Run<RunResult>(owner, "압축파일 검사 중", delegate (JobContext ctx)
                    {
                        return Engine.Test(path, pw, info.CodePage == "auto" ? null : info.CodePage, ctx);
                    });
                }
                catch (OperationCanceledException) { return false; }
                catch (EngineException ee) { Msg.Error(owner, ee.Message); return false; }
                if (r.Cancelled) return false;
                if (r.Failed && r.WrongPassword)
                {
                    string np = PasswordForm.Ask(owner, Path.GetFileName(path), true);
                    if (np == null) return false;
                    info.Password = np;
                    continue;
                }
                break;
            }
            if (r.Ok)
            {
                Jobs.FinishAction(owner);
                Msg.Info(owner, "이상 없습니다.\n파일 " + info.FileCount + "개, 풀면 " + Util.FormatSize(info.TotalSize));
                return true;
            }
            Msg.Error(owner, "압축파일에 문제가 있습니다.\n\n" + r.ErrorText());
            return false;
        }

        // ---------- 압축 ----------

        public static bool Compress(IWin32Window owner, CompressOptions o)
        {
            RunResult r;
            try
            {
                r = Jobs.Run<RunResult>(owner, "압축하는 중", delegate (JobContext ctx) { return Engine.Compress(o, ctx); });
            }
            catch (OperationCanceledException) { return false; }
            catch (EngineException ee) { Msg.Error(owner, ee.Message); return false; }
            if (r.Cancelled) return false;
            if (r.Failed)
            {
                Msg.Error(owner, "압축 중 오류가 발생했습니다.\n\n" + r.ErrorText() + "\n\n로그: " + Log.TodayFile);
                return false;
            }
            if (r.ExitCode == 1) Msg.Warn(owner, "일부 파일은 압축하지 못했습니다 (사용 중인 파일 등).\n\n" + r.ErrorText());
            string shown = File.Exists(o.OutFile) ? o.OutFile : o.OutFile + ".001";
            if (!File.Exists(shown)) { Msg.Error(owner, "압축파일이 만들어지지 않았습니다."); return false; }
            if (Msg.Silent) Msg.Info(owner, "압축 완료: " + shown + " (" + Util.FormatSize(new FileInfo(shown).Length) + ")");
            else if (Settings.OpenFolderAfter)
            {
                try { Process.Start("explorer.exe", "/select," + Util.Quote(shown)); } catch { }
            }
            Jobs.FinishAction(owner);
            return true;
        }

        // 확장자만 보고 만드는 빠른 압축 (우클릭·명령줄용). 대화상자 없이 기본값으로 만든다.
        public static bool QuickCompress(IWin32Window owner, IList<string> sources, string format)
        {
            List<string> full = new List<string>();
            foreach (string s in sources) full.Add(Path.GetFullPath(s).TrimEnd('\\'));
            CompressOptions o = new CompressOptions();
            string dir = Path.GetDirectoryName(full[0]);
            o.BaseDir = dir;
            foreach (string s in full)
            {
                if (!string.Equals(Path.GetDirectoryName(s), dir, StringComparison.OrdinalIgnoreCase))
                {
                    Msg.Error(owner, "서로 다른 폴더의 항목은 빠른 압축을 할 수 없습니다. 압축 창을 사용하세요.");
                    return false;
                }
                o.Items.Add(Path.GetFileName(s));
            }
            bool singleStream = format == "gz" || format == "bz2" || format == "xz";
            string nm = full.Count == 1
                ? (File.Exists(full[0]) && !singleStream ? Path.GetFileNameWithoutExtension(full[0]) : Path.GetFileName(full[0]))
                : Path.GetFileName(dir);
            if (string.IsNullOrEmpty(nm)) nm = "archive";
            o.Format = format;
            o.OutFile = Util.UniquePath(Path.Combine(dir, nm + "." + format));
            o.Level = Settings.DefaultLevel;
            o.Threads = Settings.Threads;
            o.Utf8Names = Settings.ZipUtf8Names;
            if (Msg.Silent)
            {
                string envPw = Environment.GetEnvironmentVariable("NOADZIP_PASSWORD");
                if (!string.IsNullOrEmpty(envPw) && (format == "zip" || format == "7z")) o.Password = envPw;
                // 자동화·시험용 환경변수: 분할(MB), 솔리드 끄기, 파일명 암호화
                string envSplit = Environment.GetEnvironmentVariable("NOADZIP_SPLIT_MB");
                long mb;
                if (!string.IsNullOrEmpty(envSplit) && long.TryParse(envSplit, out mb) && mb > 0) o.VolumeBytes = mb * 1024 * 1024;
                if (Environment.GetEnvironmentVariable("NOADZIP_SOLID") == "0") o.Solid = false;
                if (Environment.GetEnvironmentVariable("NOADZIP_HDRENC") == "1") o.HeaderEncrypt = o.Password != null;
                string envEx = Environment.GetEnvironmentVariable("NOADZIP_EXCLUDE");
                if (!string.IsNullOrEmpty(envEx))
                    foreach (string pat in envEx.Split(';')) if (pat.Trim().Length > 0) o.Excludes.Add(pat.Trim());
            }
            return Compress(owner, o);
        }

        // ---------- 압축파일 안의 파일 열어 보기 ----------

        public static string ExtractToTemp(IWin32Window owner, ArchiveInfo info, ArchiveEntry entry)
        {
            if (Engine.IsUnsafePath(entry.Path)) { Msg.Error(owner, "안전하지 않은 경로라 열 수 없습니다."); return null; }
            if (entry.IsLink) { Msg.Error(owner, "링크 항목은 열 수 없습니다."); return null; }
            if (entry.Encrypted && string.IsNullOrEmpty(info.Password))
            {
                string pw = PasswordForm.Ask(owner, Path.GetFileName(info.FilePath), false);
                if (pw == null) return null;
                info.Password = pw;
            }
            string dir = Path.Combine(Util.TempRoot, "open_" + SessionId, Guid.NewGuid().ToString("N").Substring(0, 6));
            Directory.CreateDirectory(dir);
            ExtractOptions o = new ExtractOptions();
            o.Archive = info.FilePath;
            o.Dest = dir;
            o.CodePage = info.CodePage;
            o.Overwrite = "all";
            o.Include = new List<string>();
            o.Include.Add(entry.Path);
            RunResult r;
            while (true)
            {
                o.Password = info.Password;
                try
                {
                    ExtractOptions oo = o;
                    r = Jobs.Run<RunResult>(owner, "파일 여는 중", delegate (JobContext ctx) { return Engine.Extract(oo, ctx); });
                }
                catch (OperationCanceledException) { return null; }
                catch (EngineException ee) { Msg.Error(owner, ee.Message); return null; }
                if (r.Cancelled) return null;
                if (r.Failed && r.WrongPassword)
                {
                    string pw = PasswordForm.Ask(owner, Path.GetFileName(info.FilePath), true);
                    if (pw == null) return null;
                    info.Password = pw;
                    continue;
                }
                break;
            }
            string result = Path.Combine(dir, entry.Path);
            if (r.Failed || !File.Exists(result))
            {
                Msg.Error(owner, "파일을 풀지 못했습니다.\n\n" + r.ErrorText());
                return null;
            }
            return result;
        }

        public static void OpenEntry(IWin32Window owner, ArchiveInfo info, ArchiveEntry entry)
        {
            if (Settings.ConfirmExecOpen && Engine.IsExecutableName(entry.Path))
            {
                if (!Msg.ConfirmRisk(owner, "실행 파일(" + Path.GetFileName(entry.Path) + ")입니다. 풀어서 실행할까요?\n출처를 모르는 파일은 실행하지 마세요."))
                    return;
            }
            string path = ExtractToTemp(owner, info, entry);
            if (path == null) return;
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(path);
                psi.UseShellExecute = true;
                Process.Start(psi);
            }
            catch (Exception ex) { Msg.Error(owner, "파일을 열 수 없습니다.\n" + ex.Message); }
        }

        // ---------- 임시 폴더 정리 ----------

        public static void CleanupTemp()
        {
            Util.TryDelete(Path.Combine(Util.TempRoot, "open_" + SessionId));
        }

        public static void PurgeStaleTemp()
        {
            try
            {
                foreach (string d in Directory.GetDirectories(Util.TempRoot, "open_*"))
                    if (Directory.GetLastWriteTime(d) < DateTime.Now.AddDays(-1)) Util.TryDelete(d);
                foreach (string f in Directory.GetFiles(Util.TempRoot, "list_*.txt"))
                    if (File.GetLastWriteTime(f) < DateTime.Now.AddDays(-1)) Util.TryDelete(f);
            }
            catch { }
        }
    }
}
