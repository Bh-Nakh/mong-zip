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
