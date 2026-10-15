using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace NoAdZip
{
    class CancelToken
    {
        volatile bool cancelled;
        Process proc;
        readonly object lk = new object();

        public bool IsCancelled { get { return cancelled; } }

        public void Cancel()
        {
            lock (lk)
            {
                cancelled = true;
                KillQuiet();
            }
        }

        internal void Attach(Process p)
        {
            lock (lk)
            {
                proc = p;
                if (cancelled) KillQuiet();
            }
        }

        void KillQuiet()
        {
            try { if (proc != null && !proc.HasExited) proc.Kill(); } catch { }
        }
    }

    // 작업 한 건의 진행 보고·취소 통로. 여러 단계 작업은 SetPhase 로 구간을 나눈다.
    class JobContext
    {
        public CancelToken Token = new CancelToken();
        public Action<int, string> OnProgress;
        int phaseBase = 0, phaseSpan = 100;

        public void SetPhase(int from, int to)
        {
            phaseBase = from;
            phaseSpan = to - from;
        }

        public void Report(int pct, string text)
        {
            if (OnProgress == null) return;
            if (pct >= 0) pct = phaseBase + pct * phaseSpan / 100;
            OnProgress(pct, text);
        }
    }

    class RunResult
    {
        public int ExitCode;
        public bool Cancelled;
        public List<string> Lines = new List<string>();
        public List<string> ErrLines = new List<string>();

        // 7-Zip 종료코드: 0 정상, 1 경고(일부 실패), 2 치명 오류
        public bool Ok { get { return !Cancelled && ExitCode == 0; } }
        public bool Failed { get { return !Cancelled && ExitCode >= 2; } }

        public bool WrongPassword
        {
            get
            {
                foreach (string s in ErrLines) if (IsPwLine(s)) return true;
                foreach (string s in Lines) if (IsPwLine(s)) return true;
                return false;
            }
        }

        static bool IsPwLine(string s)
        {
            return s.IndexOf("Wrong password", StringComparison.OrdinalIgnoreCase) >= 0
                || s.IndexOf("Enter password", StringComparison.OrdinalIgnoreCase) >= 0
                || s.IndexOf("encrypted archive", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public string ErrorText()
        {
            List<string> o = new List<string>();
            foreach (string s in ErrLines) if (s.Trim().Length > 0) o.Add(s.Trim());
            foreach (string s in Lines)
                if (s.StartsWith("ERROR", StringComparison.OrdinalIgnoreCase) || s.StartsWith("WARNING", StringComparison.OrdinalIgnoreCase))
                    o.Add(s.Trim());
            if (o.Count > 12)
            {
                int more = o.Count - 12;
                o = o.GetRange(0, 12);
                o.Add("... 외 " + more + "건 (로그 참조)");
            }
            return string.Join(Environment.NewLine, o.ToArray());
        }
    }

    class EngineException : Exception
    {
        public EngineException(string m) : base(m) { }
    }

    class PasswordRequiredException : Exception
    {
        public bool Wrong;
        public PasswordRequiredException(bool wrong) : base("암호가 필요합니다.") { Wrong = wrong; }
    }

    class ArchiveEntry
    {
        public string Path;
        public bool IsDir;
        public long Size;
        public long Packed;
        public string Modified = "";
        public bool Encrypted;
        public string Attr = "";
        public string Crc = "";
        public bool IsLink;
    }

    class ArchiveInfo
    {
        public string FilePath;       // 엔진이 실제로 읽는 파일 (tar.gz 는 풀어 낸 임시 tar)
        public string SourcePath;     // 사용자가 연 원본 압축파일
        public string Type = "";
        public long PhysicalSize;
        public bool Solid;
        public string Method = "";
        public string Password;
        public string CodePage = "auto";
        public List<ArchiveEntry> Entries = new List<ArchiveEntry>();

        public long TotalSize
        {
            get { long t = 0; foreach (ArchiveEntry e in Entries) if (!e.IsDir) t += e.Size; return t; }
        }

        public int FileCount
        {
            get { int n = 0; foreach (ArchiveEntry e in Entries) if (!e.IsDir) n++; return n; }
        }

        public bool AnyEncrypted
        {
            get { foreach (ArchiveEntry e in Entries) if (e.Encrypted) return true; return false; }
        }
    }

    class SecurityReport
    {
        public List<string> BadPaths = new List<string>();
        public List<string> Links = new List<string>();
        public long TotalSize;
        public int Count;
        public double Ratio;
        public bool BombSuspect;
    }

    class ExtractOptions
    {
        public string Archive;
        public string Dest;
        public string Password;
        public string CodePage = "auto";
        public string Overwrite = "all";      // all | skip | rename
        public List<string> Include;          // null 이면 전체
        public List<string> Exclude;
        public int Threads;
    }

    class CompressOptions
    {
        public string OutFile;
        public string BaseDir;
        public List<string> Items = new List<string>();   // BaseDir 기준 상대 이름
        public string Format = "zip";                      // zip 7z tar tar.gz tar.bz2 tar.xz gz bz2 xz
        public int Level = 5;
        public string Password;
        public int Threads;
        public bool Utf8Names = true;
        public bool HeaderEncrypt;                         // 7z 파일명 암호화
        public bool Solid = true;                          // 7z 솔리드 압축
        public long VolumeBytes;                           // 분할 크기 (0 = 분할 안 함)
        public List<string> Excludes = new List<string>();
    }

    static class Engine
    {
        static readonly Regex ReProgress = new Regex(@"^\s*(\d{1,3})%", RegexOptions.Compiled);
        static readonly Regex ReFileLine = new Regex(@"^([+\-=UTR.]) (.+)$", RegexOptions.Compiled);

        public static string ExePath
        {
            get { return Path.Combine(Util.AppDir, "7z.exe"); }
        }

        public static bool Available
        {
            get { return File.Exists(ExePath) && File.Exists(Path.Combine(Util.AppDir, "7z.dll")); }
        }

        // ---------- 프로세스 실행 ----------

        public static RunResult Run(string args, string workDir, JobContext ctx, bool keepAll)
        {
            RunResult r = new RunResult();
            ProcessStartInfo psi = new ProcessStartInfo(ExePath, args);
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.RedirectStandardInput = true;
            psi.StandardOutputEncoding = new UTF8Encoding(false);
            psi.StandardErrorEncoding = new UTF8Encoding(false);
            if (workDir != null) psi.WorkingDirectory = workDir;
            Log.Write("7z " + RedactPw(args));

            using (Process p = new Process())
            {
                p.StartInfo = psi;
                p.ErrorDataReceived += delegate (object s, DataReceivedEventArgs e)
                {
                    if (e.Data != null) lock (r.ErrLines) r.ErrLines.Add(e.Data);
                };
                p.Start();
                if (ctx != null) ctx.Token.Attach(p);
                p.BeginErrorReadLine();
                try { p.StandardInput.Close(); } catch { }

                StringBuilder sb = new StringBuilder();
                StreamReader rd = p.StandardOutput;
                int c;
                while ((c = rd.Read()) >= 0)
                {
                    char ch = (char)c;
                    if (ch == '\r' || ch == '\n')
                    {
                        if (sb.Length > 0) { HandleLine(sb.ToString(), r, ctx, keepAll); sb.Length = 0; }
                    }
                    else if (ch != '\b') sb.Append(ch);
                }
                if (sb.Length > 0) HandleLine(sb.ToString(), r, ctx, keepAll);
                p.WaitForExit();
                r.ExitCode = p.ExitCode;
            }
            r.Cancelled = ctx != null && ctx.Token.IsCancelled;
            Log.Write("  -> 종료코드 " + r.ExitCode + (r.Cancelled ? " (취소됨)" : ""));
            if (r.Failed || r.ExitCode == 1)
            {
                string et = r.ErrorText();
                if (et.Length > 0) Log.Write("  오류: " + et.Replace(Environment.NewLine, " | "));
            }
            return r;
        }

        static string RedactPw(string args)
        {
            return Regex.Replace(args, "\"?-p[^\\s\"]*\"?", "-p***");
        }

        static void HandleLine(string line, RunResult r, JobContext ctx, bool keepAll)
        {
            if (keepAll) { r.Lines.Add(line); return; }
            Match m = ReProgress.Match(line);
            if (m.Success)
            {
                int pct = Math.Min(100, int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture));
                if (ctx != null) ctx.Report(pct, null);
                return;
            }
            Match f = ReFileLine.Match(line);
            if (f.Success)
            {
                if (ctx != null) ctx.Report(-1, f.Groups[2].Value);
                return;
            }
            if (r.Lines.Count < 3000) r.Lines.Add(line);
        }

        static string CommonSwitches()
        {
            return " -y -sccUTF-8 -scsUTF-8";
        }

        static string CpSwitch(string codePage)
        {
            if (string.IsNullOrEmpty(codePage) || codePage == "auto") return "";
            return " -mcp=" + codePage;
        }

        static string PwSwitch(string pw)
        {
            if (string.IsNullOrEmpty(pw)) return "";
            return " " + Util.Quote("-p" + pw);
        }

        static string ThreadSwitch(int threads)
        {
            return threads > 0 ? " -mmt=" + threads : "";
        }

        // ---------- 목록 ----------

        public static ArchiveInfo List(string archive, string password, string codePage, JobContext ctx)
        {
            string args = "l -slt" + CommonSwitches() + CpSwitch(codePage) + PwSwitch(password) + " " + Util.Quote(archive);
            RunResult r = Run(args, null, ctx, true);
            if (r.Cancelled) throw new OperationCanceledException();
            ArchiveInfo info = ParseList(r.Lines);
            foreach (ArchiveEntry e in info.Entries)
                if (e.Path.Length == 0) e.Path = Path.GetFileNameWithoutExtension(archive);
            info.FilePath = archive;
            info.SourcePath = archive;
            info.Password = password;
            info.CodePage = string.IsNullOrEmpty(codePage) ? "auto" : codePage;
            if (info.Type.Length == 0 || (r.ExitCode >= 2 && info.Entries.Count == 0))
            {
                if (r.WrongPassword) throw new PasswordRequiredException(!string.IsNullOrEmpty(password));
                string et = r.ErrorText();
                if (et.Length == 0) et = "지원하지 않거나 손상된 압축파일입니다.";
                throw new EngineException(et);
            }
            return info;
        }

        // 한글 파일명이 깨져 보이면 코드페이지를 바꿔 가며 가장 자연스러운 결과를 고른다.
        public static ArchiveInfo ListSmart(string archive, string password, string codePage, JobContext ctx)
        {
            ArchiveInfo info = List(archive, password, codePage, ctx);
            if ((codePage == null || codePage == "auto") && IsNameEncodingSensitive(info.Type))
            {
                int bad = BadScore(info);
                if (bad > 0)
                {
                    ArchiveInfo best = info;
                    long bestScore = Score(info);
                    foreach (string cp in new string[] { "949", "65001", "932", "936", "950" })
                    {
                        try
                        {
                            ArchiveInfo alt = List(archive, password, cp, ctx);
                            long sc = Score(alt);
                            if (sc < bestScore) { best = alt; bestScore = sc; }
                        }
                        catch (OperationCanceledException) { throw; }
                        catch { }
                    }
                    info = best;
                }
            }
            return info;
        }

        static bool IsNameEncodingSensitive(string type)
        {
            return string.Equals(type, "zip", StringComparison.OrdinalIgnoreCase)
                || string.Equals(type, "tar", StringComparison.OrdinalIgnoreCase)
                || string.Equals(type, "rar", StringComparison.OrdinalIgnoreCase);
        }

        static int BadScore(ArchiveInfo a)
        {
            int bad = 0;
            foreach (ArchiveEntry e in a.Entries)
                foreach (char c in e.Path)
                    if (c == '�' || (c >= 0x80 && c <= 0xFF) || (c >= 0x2500 && c <= 0x257F)) bad++;
            return bad;
        }

        static long Score(ArchiveInfo a)
        {
            long hangul = 0;
            foreach (ArchiveEntry e in a.Entries)
                foreach (char c in e.Path)
                    if (c >= 0xAC00 && c <= 0xD7A3) hangul++;
            return (long)BadScore(a) * 1000 - hangul;
        }

        static ArchiveInfo ParseList(List<string> lines)
        {
            ArchiveInfo info = new ArchiveInfo();
            bool inEntries = false;
            ArchiveEntry cur = null;
            bool sawFirstSep = false;
            foreach (string raw in lines)
            {
                string line = raw;
                if (line.StartsWith("----------"))
                {
                    inEntries = true;
                    sawFirstSep = true;
                    continue;
                }
                if (!sawFirstSep && line == "--") { continue; }
                if (line.Length == 0)
                {
                    if (inEntries && cur != null) { info.Entries.Add(cur); cur = null; }
                    continue;
                }
                int i = line.IndexOf(" = ", StringComparison.Ordinal);
                string key, val;
                if (i > 0) { key = line.Substring(0, i); val = line.Substring(i + 3); }
                else if (line.EndsWith(" =", StringComparison.Ordinal)) { key = line.Substring(0, line.Length - 2); val = ""; }
                else continue;

                if (!inEntries)
                {
                    if (key == "Type") info.Type = val;
                    else if (key == "Physical Size") long.TryParse(val, out info.PhysicalSize);
                    else if (key == "Method" && info.Method.Length == 0) info.Method = val;
                    else if (key == "Solid") info.Solid = val == "+";
                    continue;
                }

                if (key == "Path")
                {
                    if (cur != null) info.Entries.Add(cur);
                    cur = new ArchiveEntry();
                    cur.Path = val;
                    continue;
                }
                // bzip2·xz 같은 단일 스트림은 항목에 Path 가 없다. 이름은 나중에 채운다.
                if (cur == null) { cur = new ArchiveEntry(); cur.Path = ""; }
                switch (key)
                {
                    case "Folder": cur.IsDir = val == "+"; break;
                    case "Size": long.TryParse(val, out cur.Size); break;
                    case "Packed Size": long.TryParse(val, out cur.Packed); break;
                    case "Modified": cur.Modified = val.Length > 19 ? val.Substring(0, 19) : val; break;
                    case "Attributes":
                        cur.Attr = val;
                        if (val.StartsWith("D", StringComparison.Ordinal)) cur.IsDir = true;
                        if (Regex.IsMatch(val, @"(^|\s)l[r-][w-][x-][r-][w-][x-]")) cur.IsLink = true;
                        break;
                    case "Encrypted": cur.Encrypted = val == "+"; break;
                    case "CRC": cur.Crc = val; break;
                    case "Symbolic Link": if (val.Length > 0) cur.IsLink = true; break;
                }
            }
            if (cur != null) info.Entries.Add(cur);
            return info;
        }

        // ---------- 보안 분석 (Zip Slip, 링크, 압축 폭탄) ----------

        public static bool IsUnsafePath(string p)
        {
            string n = p.Replace('/', '\\');
            if (n.StartsWith("\\")) return true;
            if (n.Length >= 2 && n[1] == ':') return true;
            foreach (string seg in n.Split('\\'))
                if (seg == "..") return true;
            return false;
        }

        public static SecurityReport Analyze(ArchiveInfo a)
        {
            SecurityReport s = new SecurityReport();
            s.Count = a.Entries.Count;
            s.TotalSize = a.TotalSize;
            foreach (ArchiveEntry e in a.Entries)
            {
                if (IsUnsafePath(e.Path)) s.BadPaths.Add(e.Path);
                else if (e.IsLink) s.Links.Add(e.Path);
            }
            long phys = a.PhysicalSize > 0 ? a.PhysicalSize : new FileInfo(a.FilePath).Length;
            s.Ratio = phys > 0 ? (double)s.TotalSize / phys : 0;
            s.BombSuspect = s.TotalSize >= Settings.BombMinBytes && s.Ratio >= Settings.BombRatio;
            return s;
        }

        // ---------- 풀기 / 검사 ----------

        public static RunResult Extract(ExtractOptions o, JobContext ctx)
        {
            string incList = null, excList = null;
            try
            {
                string ow = "-aoa";
                if (o.Overwrite == "skip") ow = "-aos";
                else if (o.Overwrite == "rename") ow = "-aou";

                StringBuilder sb = new StringBuilder("x -bsp1 -bb1 -spd -snz");
                sb.Append(CommonSwitches());
                sb.Append(' ').Append(ow);
                sb.Append(' ').Append(Util.Quote("-o" + o.Dest.TrimEnd('\\') + "\\"));
                sb.Append(CpSwitch(o.CodePage));
                sb.Append(PwSwitch(o.Password));
                sb.Append(ThreadSwitch(o.Threads));
                if (o.Exclude != null && o.Exclude.Count > 0)
                {
                    excList = Util.WriteListFile(o.Exclude);
                    sb.Append(' ').Append(Util.Quote("-x@" + excList));
                }
                sb.Append(' ').Append(Util.Quote(o.Archive));
                if (o.Include != null)
                {
                    incList = Util.WriteListFile(o.Include);
                    sb.Append(' ').Append(Util.Quote("@" + incList));
                }
                return Run(sb.ToString(), null, ctx, false);
            }
            finally
            {
                if (incList != null) Util.TryDelete(incList);
                if (excList != null) Util.TryDelete(excList);
            }
        }

        public static RunResult Test(string archive, string password, string codePage, JobContext ctx)
        {
            string args = "t -bsp1 -bb1" + CommonSwitches() + CpSwitch(codePage) + PwSwitch(password) + " " + Util.Quote(archive);
            return Run(args, null, ctx, false);
        }

        // ---------- 압축 ----------

        public static bool IsTarCompound(string fmt)
        {
            return fmt == "tar.gz" || fmt == "tar.bz2" || fmt == "tar.xz";
        }

        public static string FormatExtension(string fmt)
        {
            return "." + fmt;
        }

        static string StreamType(string fmt)
        {
            if (fmt.EndsWith("gz")) return "gzip";
            if (fmt.EndsWith("bz2")) return "bzip2";
            if (fmt.EndsWith("xz")) return "xz";
            return fmt;
        }

        public static RunResult Compress(CompressOptions o, JobContext ctx)
        {
            string tmpOut = o.OutFile + ".nozip~";
            string workTmpDir = null;
            string listFile = null;
            Util.TryDelete(tmpOut);
            try
            {
                string outDir = Path.GetDirectoryName(o.OutFile);
                string lvl = " -mx=" + o.Level;
                listFile = Util.WriteListFile(o.Items);
                StringBuilder ex = new StringBuilder();
                foreach (string pat in o.Excludes)
                    if (pat.Trim().Length > 0) ex.Append(' ').Append(Util.Quote("-xr!" + pat.Trim()));

                RunResult r;
                if (IsTarCompound(o.Format))
                {
                    workTmpDir = Path.Combine(outDir, ".nozip_" + Guid.NewGuid().ToString("N").Substring(0, 8));
                    Directory.CreateDirectory(workTmpDir);
                    string tarName = Util.ArchiveBaseName(o.OutFile) + ".tar";
                    string tarPath = Path.Combine(workTmpDir, tarName);

                    ctx.SetPhase(0, 50);
                    string a1 = "a -ttar -bsp1 -bb1" + CommonSwitches() + ex + " " + Util.Quote(tarPath) + " " + Util.Quote("@" + listFile);
                    r = Run(a1, o.BaseDir, ctx, false);
                    if (!r.Ok && !(r.ExitCode == 1 && !r.Cancelled)) { Util.TryDelete(workTmpDir); return r; }

                    ctx.SetPhase(50, 100);
                    string a2 = "a -t" + StreamType(o.Format) + lvl + ThreadSwitch(o.Threads) + " -bsp1 -bb1" + CommonSwitches()
                        + " " + Util.Quote(tmpOut) + " " + Util.Quote(tarPath);
                    RunResult r2 = Run(a2, workTmpDir, ctx, false);
                    r2.ExitCode = Math.Max(r2.ExitCode, r.ExitCode);
                    r2.Lines.AddRange(r.Lines);
                    r2.ErrLines.AddRange(r.ErrLines);
                    r = r2;
                }
                else
                {
                    StringBuilder sb = new StringBuilder("a -t" + StreamType(o.Format) + lvl + " -bsp1 -bb1");
                    sb.Append(CommonSwitches());
                    sb.Append(ThreadSwitch(o.Threads));
                    if (o.Format == "7z")
                    {
                        sb.Append(o.Solid ? " -ms=on" : " -ms=off");
                        if (o.HeaderEncrypt && !string.IsNullOrEmpty(o.Password)) sb.Append(" -mhe=on");
                    }
                    if (o.VolumeBytes > 0 && (o.Format == "zip" || o.Format == "7z"))
                        sb.Append(" -v").Append(o.VolumeBytes).Append('b');
                    if (o.Format == "zip")
                    {
                        if (o.Utf8Names) sb.Append(" -mcu=on");
                        if (!string.IsNullOrEmpty(o.Password)) sb.Append(" -mem=AES256");
                    }
                    if ((o.Format == "zip" || o.Format == "7z") && !string.IsNullOrEmpty(o.Password))
                        sb.Append(PwSwitch(o.Password));
                    sb.Append(ex);
                    sb.Append(' ').Append(Util.Quote(tmpOut)).Append(' ').Append(Util.Quote("@" + listFile));
                    r = Run(sb.ToString(), o.BaseDir, ctx, false);
                }

                bool split = o.VolumeBytes > 0 && (o.Format == "zip" || o.Format == "7z");
                bool good = !r.Cancelled && r.ExitCode < 2 && (split ? File.Exists(tmpOut + ".001") : File.Exists(tmpOut));
                if (good && split)
                {
                    string exact = "^" + Regex.Escape(Path.GetFileName(o.OutFile)) + @"\.\d{3}$";
                    foreach (string old in Directory.GetFiles(outDir, Path.GetFileName(o.OutFile) + ".*"))
                        if (Regex.IsMatch(Path.GetFileName(old), exact, RegexOptions.IgnoreCase)) Util.TryDelete(old);
                    foreach (string part in Directory.GetFiles(outDir, Path.GetFileName(tmpOut) + ".*"))
                    {
                        string num = Path.GetExtension(part);
                        File.Move(part, o.OutFile + num);
                    }
                }
                else if (good)
                {
                    Util.TryDelete(o.OutFile);
                    File.Move(tmpOut, o.OutFile);
                }
                return r;
            }
            finally
            {
                if (listFile != null) Util.TryDelete(listFile);
                if (workTmpDir != null) Util.TryDelete(workTmpDir);
                Util.TryDelete(tmpOut);
                try
                {
                    foreach (string part in Directory.GetFiles(Path.GetDirectoryName(o.OutFile), Path.GetFileName(tmpOut) + ".*"))
                        Util.TryDelete(part);
                }
                catch { }
            }
        }

        // ---------- 압축파일 판별 ----------

        static readonly string[] ArchiveExts = {
            ".zip", ".7z", ".rar", ".tar", ".gz", ".tgz", ".bz2", ".tbz2", ".tbz", ".xz", ".txz", ".iso",
            ".cab", ".lzh", ".lha", ".arj", ".z", ".lzma", ".zst", ".wim", ".vhd", ".vhdx", ".dmg", ".msi", ".jar", ".war", ".apk", ".001"
        };

        public static string[] AssociableExts
        {
            get { return new string[] { ".zip", ".7z", ".rar", ".tar", ".gz", ".tgz", ".bz2", ".tbz2", ".xz", ".txz", ".iso", ".cab", ".lzh", ".arj", ".001" }; }
        }

        public static bool IsArchive(string path)
        {
            if (!File.Exists(path)) return false;
            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (Array.IndexOf(ArchiveExts, ext) >= 0) return true;
            try
            {
                using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    byte[] b = new byte[8];
                    int n = fs.Read(b, 0, 8);
                    if (n >= 4 && b[0] == 'P' && b[1] == 'K' && (b[2] == 3 || b[2] == 5)) return true;
                    if (n >= 6 && b[0] == '7' && b[1] == 'z' && b[2] == 0xBC && b[3] == 0xAF) return true;
                    if (n >= 4 && b[0] == 'R' && b[1] == 'a' && b[2] == 'r' && b[3] == '!') return true;
                    if (n >= 2 && b[0] == 0x1F && b[1] == 0x8B) return true;
                    if (n >= 3 && b[0] == 'B' && b[1] == 'Z' && b[2] == 'h') return true;
                    if (n >= 6 && b[0] == 0xFD && b[1] == '7' && b[2] == 'z' && b[3] == 'X' && b[4] == 'Z') return true;
                    if (n >= 4 && b[0] == 'M' && b[1] == 'S' && b[2] == 'C' && b[3] == 'F') return true;
                }
            }
            catch { }
            return false;
        }

        public static bool IsExecutableName(string name)
        {
            string e = Path.GetExtension(name).ToLowerInvariant();
            string[] x = { ".exe", ".bat", ".cmd", ".com", ".scr", ".msi", ".ps1", ".vbs", ".vbe", ".js", ".jse", ".wsf", ".hta", ".lnk", ".reg", ".jar", ".dll", ".cpl" };
            return Array.IndexOf(x, e) >= 0;
        }
    }
}
