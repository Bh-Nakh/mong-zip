using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace NoAdZip
{
    // HKCU 에만 등록한다 (관리자 권한 불필요). 사용자 PC의 기존 연결은 해제 시 되돌린다.
    static class Shell
    {
        const string ProgId = "NoAdZip.Archive";
        const string AppKey = "NoAdZip";
        const string BackupValue = "NoAdZip.Backup";
        const string LegacyExeName = "NoAdZip.exe";

        [DllImport("shell32.dll")]
        static extern void SHChangeNotify(int eventId, int flags, IntPtr item1, IntPtr item2);

        public static void NotifyChanged()
        {
            SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero);
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        static extern void SHChangeNotify(int eventId, int flags, string item1, IntPtr item2);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr ILCreateFromPath(string path);

        [DllImport("shell32.dll")]
        static extern void ILFree(IntPtr pidl);

        [DllImport("shell32.dll")]
        static extern int SHOpenFolderAndSelectItems(IntPtr pidlFolder, uint cidl, IntPtr[] apidl, uint flags);

        [StructLayout(LayoutKind.Sequential)]
        struct PROCESS_BASIC_INFORMATION { public IntPtr ExitStatus, PebBaseAddress, AffinityMask, BasePriority, UniqueProcessId, InheritedFromUniqueProcessId; }

        [StructLayout(LayoutKind.Sequential)]
        struct COPYDATASTRUCT { public IntPtr dwData; public int cbData; public IntPtr lpData; }

        delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

        [DllImport("ntdll.dll")]
        static extern int NtQueryInformationProcess(IntPtr process, int infoClass, ref PROCESS_BASIC_INFORMATION info, int size, out int returned);

        [DllImport("user32.dll")]
        static extern bool EnumWindows(EnumWindowsProc proc, IntPtr lParam);

        [DllImport("user32.dll")]
        static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);

        [DllImport("user32.dll")]
        static extern bool IsWindowVisible(IntPtr hwnd);

        [DllImport("user32.dll")]
        static extern IntPtr SendMessageTimeout(IntPtr hwnd, int msg, IntPtr wParam, ref COPYDATASTRUCT lParam, int flags, int timeout, out IntPtr result);

        // 우클릭 메뉴를 띄운 프로그램에 만든 폴더를 알리는 WM_COPYDATA 표지 (몽탐색기가 받아 이름 바꾸기 상태로 둔다)
        const int NewFolderCopyData = 0x4D4E4644;   // 'MNFD'

        /// <summary>
        /// 탐색기 빈 공간 우클릭 [새 폴더 만들기]: 'dir' 에 "새 폴더"(이미 있으면 "새 폴더 (2)"...)를 만들고
        /// 바로 이름 바꾸기 상태로 만든다. 만든 경로를 돌려준다.
        /// 윈도우 탐색기가 실행했으면 탐색기에 선택을 맡기고, 다른 프로그램(몽탐색기·파일 대화상자 등)이 실행했으면
        /// 탐색기 창을 새로 띄우지 않고 그 프로그램 창에 경로만 알린다.
        /// </summary>
        public static string NewFolder(string dir, bool select)
        {
            string baseName = Loc.English ? "New folder" : "새 폴더";
            string path = Path.Combine(dir, baseName);
            for (int n = 2; Directory.Exists(path) || File.Exists(path); n++)
                path = Path.Combine(dir, baseName + " (" + n + ")");
            Directory.CreateDirectory(path);
            SHChangeNotify(0x00000008, 0x0005, path, IntPtr.Zero);     // SHCNE_MKDIR, SHCNF_PATHW
            int host = ParentProcessId();
            bool explorer = IsExplorer(host);
            Log.Write("새 폴더: " + path + " (실행한 프로그램 " + host + (explorer ? ", 탐색기" : "") + ")");
            if (select)
            {
                if (explorer) SelectInExplorer(dir, path);
                else TellHost(host, path);
            }
            return path;
        }

        static void SelectInExplorer(string dir, string path)
        {
            IntPtr folder = ILCreateFromPath(dir), item = ILCreateFromPath(path);
            try
            {
                if (folder != IntPtr.Zero && item != IntPtr.Zero)
                    SHOpenFolderAndSelectItems(folder, 1, new IntPtr[] { item }, 0x0001);   // OFASI_EDIT
            }
            finally
            {
                if (item != IntPtr.Zero) ILFree(item);
                if (folder != IntPtr.Zero) ILFree(folder);
            }
        }

        static int ParentProcessId()
        {
            try
            {
                var pbi = new PROCESS_BASIC_INFORMATION();
                int len;
                using (var me = System.Diagnostics.Process.GetCurrentProcess())
                    if (NtQueryInformationProcess(me.Handle, 0, ref pbi, Marshal.SizeOf(pbi), out len) == 0)
                        return (int)(long)pbi.InheritedFromUniqueProcessId;
            }
            catch { }
            return 0;
        }

        static bool IsExplorer(int pid)
        {
            if (pid <= 0) return false;
            try
            {
                using (var p = System.Diagnostics.Process.GetProcessById(pid))
                    return string.Equals(p.ProcessName, "explorer", StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        /// <summary>프로그램 pid 의 보이는 최상위 창들에 만든 폴더 경로를 WM_COPYDATA 로 보낸다</summary>
        static void TellHost(int pid, string path)
        {
            if (pid <= 0) return;
            byte[] data = System.Text.Encoding.Unicode.GetBytes(path + "\0");
            IntPtr buf = Marshal.AllocHGlobal(data.Length);
            try
            {
                Marshal.Copy(data, 0, buf, data.Length);
                var cds = new COPYDATASTRUCT { dwData = (IntPtr)NewFolderCopyData, cbData = data.Length, lpData = buf };
                EnumWindows(delegate (IntPtr h, IntPtr l)
                {
                    uint wpid;
                    GetWindowThreadProcessId(h, out wpid);
                    if (wpid == pid && IsWindowVisible(h))
                    {
                        IntPtr r;
                        SendMessageTimeout(h, 0x004A /* WM_COPYDATA */, IntPtr.Zero, ref cds, 0x0002 /* SMTO_ABORTIFHUNG */, 2000, out r);
                    }
                    return true;
                }, IntPtr.Zero);
            }
            finally { Marshal.FreeHGlobal(buf); }
        }

        static string ExePath
        {
            get { return System.Reflection.Assembly.GetExecutingAssembly().Location; }
        }

        static RegistryKey Classes(bool write)
        {
            return Registry.CurrentUser.CreateSubKey(@"Software\Classes");
        }

        /// <summary>
        /// 옛 이름(NoAdZip.exe)으로 설치한 뒤 이름이 몽집.exe 로 바뀌면 파일 연결이 지워진 옛 실행 파일을 가리킨다.
        /// 연결해 둔 확장자가 있고 등록된 실행 파일이 없어졌을 때만 지금 실행 파일로 다시 등록한다
        /// (시험·포터블 실행이 사용자의 연결을 가로채지 않도록, 파일이 살아 있으면 건드리지 않는다).
        /// </summary>
        public static void RefreshIfStale()
        {
            try
            {
                string cmd;
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(@"Software\Classes\" + ProgId + @"\shell\open\command"))
                    cmd = k == null ? null : k.GetValue("") as string;
                if (string.IsNullOrEmpty(cmd)) return;
                int q2 = cmd.IndexOf('"', 1);
                string registered = cmd.StartsWith("\"") && q2 > 1 ? cmd.Substring(1, q2 - 1) : cmd;
                if (File.Exists(registered)) return;
                List<string> exts = new List<string>();
                foreach (string e in Engine.AssociableExts) if (IsAssociated(e)) exts.Add(e);
                if (exts.Count > 0) Associate(exts);
            }
            catch { }
        }

        public static bool IsAssociated(string ext)
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(@"Software\Classes\" + ext + @"\OpenWithProgids"))
                    return k != null && Array.IndexOf(k.GetValueNames(), ProgId) >= 0;
            }
            catch { return false; }
        }

        // 성공하면 null, 실패하면 오류 메시지.
        public static string Associate(IEnumerable<string> exts)
        {
            try
            {
                string exe = ExePath;
                using (RegistryKey cls = Classes(true))
                {
                    using (RegistryKey p = cls.CreateSubKey(ProgId))
                    {
                        p.SetValue("", "압축 파일 (몽집)");
                        using (RegistryKey ic = p.CreateSubKey("DefaultIcon")) ic.SetValue("", "\"" + exe + "\",0");
                        using (RegistryKey cmd = p.CreateSubKey(@"shell\open\command")) cmd.SetValue("", "\"" + exe + "\" \"%1\"");
                    }
                    // 실행 파일 이름이 몽집.exe 로 바뀌었으므로 옛 이름(NoAdZip.exe)으로 등록한 항목은 지운다.
                    cls.DeleteSubKeyTree(@"Applications\" + LegacyExeName, false);
                    using (RegistryKey a = cls.CreateSubKey(@"Applications\" + Path.GetFileName(exe)))
                    {
                        a.SetValue("FriendlyAppName", "몽집");
                        using (RegistryKey ic = a.CreateSubKey("DefaultIcon")) ic.SetValue("", "\"" + exe + "\",0");
                        using (RegistryKey cmd = a.CreateSubKey(@"shell\open\command")) cmd.SetValue("", "\"" + exe + "\" \"%1\"");
                        using (RegistryKey st = a.CreateSubKey("SupportedTypes")) foreach (string e in exts) st.SetValue(e, "");
                    }
                    foreach (string e in exts)
                    {
                        using (RegistryKey k = cls.CreateSubKey(e))
                        {
                            using (RegistryKey ow = k.CreateSubKey("OpenWithProgids")) ow.SetValue(ProgId, new byte[0], RegistryValueKind.None);
                            string cur = k.GetValue("") as string;
                            if (cur != ProgId)
                            {
                                if (k.GetValue(BackupValue) == null) k.SetValue(BackupValue, cur ?? "");
                                k.SetValue("", ProgId);
                            }
                        }
                    }
                }
                // Windows "기본 앱" 목록에 나타나도록 Capabilities 등록
                using (RegistryKey cap = Registry.CurrentUser.CreateSubKey(@"Software\NoAdZip\Capabilities"))
                {
                    cap.SetValue("ApplicationName", "몽집");
                    cap.SetValue("ApplicationDescription", "광고 없는 압축 프로그램");
                    using (RegistryKey fa = cap.CreateSubKey("FileAssociations"))
                        foreach (string e in Engine.AssociableExts) fa.SetValue(e, ProgId);
                }
                using (RegistryKey ra = Registry.CurrentUser.CreateSubKey(@"Software\RegisteredApplications"))
                    ra.SetValue(AppKey, @"Software\NoAdZip\Capabilities");
                SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero);
                Log.Write("파일 연결 등록: " + string.Join(",", new List<string>(exts).ToArray()));
                return null;
            }
            catch (Exception ex) { return "레지스트리에 쓸 수 없습니다: " + ex.Message; }
        }

        public static string Unassociate(IEnumerable<string> exts)
        {
            try
            {
                using (RegistryKey cls = Classes(true))
                {
                    foreach (string e in exts)
                    {
                        using (RegistryKey k = cls.OpenSubKey(e, true))
                        {
                            if (k == null) continue;
                            using (RegistryKey ow = k.OpenSubKey("OpenWithProgids", true))
                                if (ow != null && Array.IndexOf(ow.GetValueNames(), ProgId) >= 0) ow.DeleteValue(ProgId, false);
                            if ((k.GetValue("") as string) == ProgId)
                            {
                                string bak = k.GetValue(BackupValue) as string;
                                if (string.IsNullOrEmpty(bak)) k.DeleteValue("", false); else k.SetValue("", bak);
                            }
                            k.DeleteValue(BackupValue, false);
                        }
                    }
                    bool any = false;
                    foreach (string e in Engine.AssociableExts) if (IsAssociated(e)) { any = true; break; }
                    if (!any)
                    {
                        cls.DeleteSubKeyTree(ProgId, false);
                        cls.DeleteSubKeyTree(@"Applications\" + Path.GetFileName(ExePath), false);
                        cls.DeleteSubKeyTree(@"Applications\" + LegacyExeName, false);
                        Registry.CurrentUser.DeleteSubKeyTree(@"Software\NoAdZip", false);
                        using (RegistryKey ra = Registry.CurrentUser.OpenSubKey(@"Software\RegisteredApplications", true))
                            if (ra != null) ra.DeleteValue(AppKey, false);
                    }
                }
                SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero);
                Log.Write("파일 연결 해제: " + string.Join(",", new List<string>(exts).ToArray()));
                return null;
            }
            catch (Exception ex) { return "레지스트리에서 지울 수 없습니다: " + ex.Message; }
        }
    }
}
