using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace NoAdZip
{
    static class Util
    {
        /// <summary>화면에 표시하는 앱 버전 (src\AssemblyInfo.cs 한 곳에서 정의)</summary>
        public static string VersionText
        {
            get { return "v" + System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString(3); }
        }

        /// <summary>업데이트 비교에 쓰는 앱 버전 (예: 1.0.3)</summary>
        public static string VersionNumber
        {
            get { return System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString(3); }
        }

        public static string AppDir
        {
            get { return AppDomain.CurrentDomain.BaseDirectory; }
        }

        // 설정·로그 저장 폴더. 시험용으로 NOADZIP_DATA 환경변수로 바꿀 수 있다.
        public static string DataDir
        {
            get
            {
                string d = Environment.GetEnvironmentVariable("NOADZIP_DATA");
                if (string.IsNullOrEmpty(d) && File.Exists(Path.Combine(AppDir, "portable.txt")))
                    d = Path.Combine(AppDir, "data");     // 포터블 모드: 설정·로그를 프로그램 폴더 안에 둔다
                if (string.IsNullOrEmpty(d))
                    d = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NoAdZip");
                try { Directory.CreateDirectory(d); } catch { }
                return d;
            }
        }

        public static string TempRoot
        {
            get
            {
                string d = Path.Combine(Path.GetTempPath(), "NoAdZip");
                try { Directory.CreateDirectory(d); } catch { }
                return d;
            }
        }

        // 윈도우 명령줄 인수 인용 규칙.
        public static string Quote(string s)
        {
            if (s.Length > 0 && s.IndexOfAny(new char[] { ' ', '\t', '"' }) < 0) return s;
            StringBuilder sb = new StringBuilder("\"");
            int bs = 0;
            foreach (char c in s)
            {
                if (c == '\\') { bs++; }
                else if (c == '"')
                {
                    sb.Append('\\', bs * 2 + 1);
                    sb.Append('"');
                    bs = 0;
                }
                else
                {
                    sb.Append('\\', bs);
                    bs = 0;
                    sb.Append(c);
                }
            }
            sb.Append('\\', bs * 2);
            sb.Append('"');
            return sb.ToString();
        }

        public static string FormatSize(long n)
        {
            if (n < 0) return "";
            string[] u = { "B", "KB", "MB", "GB", "TB" };
            double v = n;
            int i = 0;
            while (v >= 1024 && i < u.Length - 1) { v /= 1024; i++; }
            if (i == 0) return n.ToString("N0", CultureInfo.InvariantCulture) + " B";
            return v.ToString(v >= 100 ? "0" : "0.0", CultureInfo.InvariantCulture) + " " + u[i];
        }

        public static string FormatDuration(TimeSpan t)
        {
            if (t.TotalHours >= 1) return string.Format("{0}시간 {1}분", (int)t.TotalHours, t.Minutes);
            if (t.TotalMinutes >= 1) return string.Format("{0}분 {1}초", t.Minutes, t.Seconds);
            return string.Format("{0}초", Math.Max(0, t.Seconds));
        }

        public static string ArchiveBaseName(string path)
        {
            string n = Path.GetFileName(path);
            string[] dbl = { ".tar.gz", ".tar.bz2", ".tar.xz", ".tar.zst", ".tar.lzma", ".tar.z" };
            foreach (string d in dbl)
                if (n.EndsWith(d, StringComparison.OrdinalIgnoreCase)) return n.Substring(0, n.Length - d.Length);
            string e = Path.GetExtension(n);
            // 분할 파일 .7z.001 / .part1.rar 등은 앞 이름만 취한다.
            if (e.Length == 4 && char.IsDigit(e[1]) && char.IsDigit(e[2]) && char.IsDigit(e[3]))
                return ArchiveBaseName(n.Substring(0, n.Length - 4));
            return Path.GetFileNameWithoutExtension(n);
        }

        public static string SafeFileName(string s)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            return s.Trim().TrimEnd('.');
        }

        public static string UniquePath(string path)
        {
            if (!File.Exists(path) && !Directory.Exists(path)) return path;
            string dir = Path.GetDirectoryName(path);
            string name = Path.GetFileNameWithoutExtension(path);
            string ext = Path.GetExtension(path);
            for (int i = 2; i < 10000; i++)
            {
                string p = Path.Combine(dir, name + " (" + i + ")" + ext);
                if (!File.Exists(p) && !Directory.Exists(p)) return p;
            }
            return path;
        }

        public static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
                else if (Directory.Exists(path)) Directory.Delete(path, true);
            }
            catch { }
        }

        public static string WriteListFile(IEnumerable<string> lines)
        {
            string p = Path.Combine(TempRoot, "list_" + Guid.NewGuid().ToString("N") + ".txt");
            File.WriteAllLines(p, new List<string>(lines).ToArray(), new UTF8Encoding(true));
            return p;
        }
    }

    static class Log
    {
        static readonly object lk = new object();

        public static string LogDir
        {
            get
            {
                string d = Path.Combine(Util.DataDir, "logs");
                try { Directory.CreateDirectory(d); } catch { }
                return d;
            }
        }

        public static string TodayFile
        {
            get { return Path.Combine(LogDir, DateTime.Now.ToString("yyyyMMdd") + ".log"); }
        }

        public static void Write(string msg)
        {
            try
            {
                lock (lk)
                    File.AppendAllText(TodayFile, DateTime.Now.ToString("HH:mm:ss") + "  " + msg + Environment.NewLine, Encoding.UTF8);
            }
            catch { }
        }
    }

    // %APPDATA%\NoAdZip\settings.ini
    static class Settings
    {
        public static string DefaultFormat = "zip";
        public static int DefaultLevel = 5;
        public static int Threads = 0;                 // 0 = 자동
        public static string ExtractMode = "folder";   // here | folder | ask
        public static string Overwrite = "ask";        // ask | all | skip | rename
        public static string CodePage = "auto";        // auto | 949 | 65001 ...
        public static bool ZipUtf8Names = true;
        public static bool OpenFolderAfter = true;
        public static bool ConfirmExecOpen = true;
        public static int BombRatio = 300;             // 압축률 이 배수 이상이면 경고
        public static long BombMinBytes = 2L * 1024 * 1024 * 1024;
        public static string LastDest = "";
        public static string Theme = "system";         // system | light | dark
        public static string Language = "ko";          // ko | en

        static string FilePath { get { return Path.Combine(Util.DataDir, "settings.ini"); } }

        public static void Load()
        {
            try
            {
                if (!File.Exists(FilePath)) return;
                foreach (string raw in File.ReadAllLines(FilePath, Encoding.UTF8))
                {
                    int i = raw.IndexOf('=');
                    if (i <= 0) continue;
                    string k = raw.Substring(0, i).Trim();
                    string v = raw.Substring(i + 1).Trim();
                    int n;
                    long l;
                    switch (k)
                    {
                        case "DefaultFormat": DefaultFormat = v; break;
                        case "DefaultLevel": if (int.TryParse(v, out n)) DefaultLevel = n; break;
                        case "Threads": if (int.TryParse(v, out n)) Threads = n; break;
                        case "ExtractMode": ExtractMode = v; break;
                        case "Overwrite": Overwrite = v; break;
                        case "CodePage": CodePage = v; break;
                        case "ZipUtf8Names": ZipUtf8Names = v == "1"; break;
                        case "OpenFolderAfter": OpenFolderAfter = v == "1"; break;
                        case "ConfirmExecOpen": ConfirmExecOpen = v == "1"; break;
                        case "BombRatio": if (int.TryParse(v, out n)) BombRatio = n; break;
                        case "BombMinBytes": if (long.TryParse(v, out l)) BombMinBytes = l; break;
                        case "LastDest": LastDest = v; break;
                        case "Theme": Theme = v; break;
                        case "Language": Language = v; break;
                    }
                }
            }
            catch { }
        }

        public static void Save()
        {
            try
            {
                List<string> o = new List<string>();
                o.Add("DefaultFormat=" + DefaultFormat);
                o.Add("DefaultLevel=" + DefaultLevel);
                o.Add("Threads=" + Threads);
                o.Add("ExtractMode=" + ExtractMode);
                o.Add("Overwrite=" + Overwrite);
                o.Add("CodePage=" + CodePage);
                o.Add("ZipUtf8Names=" + (ZipUtf8Names ? "1" : "0"));
                o.Add("OpenFolderAfter=" + (OpenFolderAfter ? "1" : "0"));
                o.Add("ConfirmExecOpen=" + (ConfirmExecOpen ? "1" : "0"));
                o.Add("BombRatio=" + BombRatio);
                o.Add("BombMinBytes=" + BombMinBytes);
                o.Add("LastDest=" + LastDest);
                o.Add("Theme=" + Theme);
                o.Add("Language=" + Language);
                File.WriteAllLines(FilePath, o.ToArray(), Encoding.UTF8);
            }
            catch { }
        }
    }
}
