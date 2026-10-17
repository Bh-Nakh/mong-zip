using System;
using System.Collections.Generic;
using Microsoft.Win32;

namespace NoAdZip
{
    // 탐색기 우클릭 메뉴. COM 확장 없이 HKCU 의 정적 동작(verb)을 최상위에 바로 나열한다.
    // (하위 메뉴 SubCommands 는 HKCU 의 CommandStore 를 읽지 못해 쓰지 않는다.)
    // 윈도우 10 에서는 바로 보이고, 윈도우 11 에서는 "추가 옵션 표시" 안에 나타난다.
    static class ShellMenu
    {
        // 시험용으로 NOADZIP_REGTEST=Software\NoAdZipTest 를 주면 실제 연결을 건드리지 않고 이 아래에 쓴다.
        static string Root
        {
            get
            {
                string t = Environment.GetEnvironmentVariable("NOADZIP_REGTEST");
                return string.IsNullOrEmpty(t) ? @"Software\Classes" : t + @"\Classes";
            }
        }

        static string Store
        {
            get
            {
                string t = Environment.GetEnvironmentVariable("NOADZIP_REGTEST");
                return string.IsNullOrEmpty(t) ? @"Software\Microsoft\Windows\CurrentVersion\Explorer\CommandStore\shell" : t + @"\CommandStore\shell";
            }
        }

        // 모든 파일·폴더 우클릭에 나오는 항목
        static readonly string[][] FileVerbs = new string[][] {
            new string[] { "NoAdZip.Compress", "몽집으로 압축하기...", "/compress" },
            new string[] { "NoAdZip.Zip", "몽집으로 ZIP 압축", "/czip" },
        };

        // 압축파일 우클릭에 나오는 항목
        static readonly string[][] ArchiveVerbs = new string[][] {
            new string[] { "NoAdZip.XHere", "몽집으로 여기에 압축풀기", "/xhere" },
            new string[] { "NoAdZip.XFolder", "몽집으로 폴더를 만들어 압축풀기", "/xfolder" },
        };

        // 폴더 창·바탕화면의 빈 공간 우클릭에 나오는 항목 (%V = 지금 보고 있는 폴더)
        static readonly string[][] BackgroundVerbs = new string[][] {
            new string[] { "NoAdZip.NewFolder", "새 폴더 만들기", "/newfolder" },
        };

        // 우클릭 메뉴 맨 위에 올릴 항목 (레지스트리 Position=Top)
        static readonly string[] TopVerbs = { "NoAdZip.NewFolder" };

        // 이전 버전(하위 메뉴 방식)이 CommandStore 에 남긴 항목 이름
        static readonly string[] LegacyVerbs = { "NoAdZip.Compress", "NoAdZip.Zip", "NoAdZip.7z", "NoAdZip.Open", "NoAdZip.XHere", "NoAdZip.XFolder", "NoAdZip.XDialog", "NoAdZip.Test" };

        static string Exe
        {
            get { return System.Reflection.Assembly.GetExecutingAssembly().Location; }
        }

        public static bool IsRegistered()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(Root + @"\*\shell\NoAdZip.Compress")) return k != null;
            }
            catch { return false; }
        }

        public static string Register()
        {
            try
            {
                string exe = Exe;
                string icon = "\"" + exe + "\",0";
                RemoveLegacy();
                using (RegistryKey cls = Registry.CurrentUser.CreateSubKey(Root))
                {
                    foreach (string[] v in FileVerbs)
                    {
                        WriteVerb(cls, @"*\shell\" + v[0], v, exe, icon);
                        WriteVerb(cls, @"Directory\shell\" + v[0], v, exe, icon);
                    }
                    foreach (string ext in Engine.AssociableExts)
                        foreach (string[] v in ArchiveVerbs)
                            WriteVerb(cls, @"SystemFileAssociations\" + ext + @"\shell\" + v[0], v, exe, icon);
                    foreach (string[] v in BackgroundVerbs)
                        WriteVerb(cls, @"Directory\Background\shell\" + v[0], v, exe, icon, "%V");
                }
                Shell.NotifyChanged();
                Log.Write("우클릭 메뉴 등록");
                return null;
            }
            catch (Exception ex) { return "우클릭 메뉴를 등록할 수 없습니다: " + ex.Message; }
        }

        static void WriteVerb(RegistryKey cls, string path, string[] v, string exe, string icon, string arg = "%1")
        {
            using (RegistryKey k = cls.CreateSubKey(path))
            {
                k.SetValue("MUIVerb", v[1]);
                k.SetValue("Icon", icon);
                if (Array.IndexOf(TopVerbs, v[0]) >= 0) k.SetValue("Position", "Top");
                using (RegistryKey c = k.CreateSubKey("command"))
                    c.SetValue("", "\"" + exe + "\" " + v[2] + " \"" + arg + "\"");
            }
        }

        // 이전 버전이 만든 하위 메뉴 키와 CommandStore 항목을 지운다.
        static void RemoveLegacy()
        {
            using (RegistryKey cls = Registry.CurrentUser.OpenSubKey(Root, true))
            {
                if (cls != null)
                {
                    cls.DeleteSubKeyTree(@"*\shell\NoAdZip", false);
                    cls.DeleteSubKeyTree(@"Directory\shell\NoAdZip", false);
                    foreach (string ext in Engine.AssociableExts)
                        cls.DeleteSubKeyTree(@"SystemFileAssociations\" + ext + @"\shell\NoAdZip.Extract", false);
                }
            }
            using (RegistryKey store = Registry.CurrentUser.OpenSubKey(Store, true))
            {
                if (store != null) foreach (string n in LegacyVerbs) store.DeleteSubKeyTree(n, false);
            }
        }

        public static string Unregister()
        {
            try
            {
                RemoveLegacy();
                using (RegistryKey cls = Registry.CurrentUser.OpenSubKey(Root, true))
                {
                    if (cls != null)
                    {
                        foreach (string[] v in FileVerbs)
                        {
                            cls.DeleteSubKeyTree(@"*\shell\" + v[0], false);
                            cls.DeleteSubKeyTree(@"Directory\shell\" + v[0], false);
                        }
                        foreach (string ext in Engine.AssociableExts)
                            foreach (string[] v in ArchiveVerbs)
                                cls.DeleteSubKeyTree(@"SystemFileAssociations\" + ext + @"\shell\" + v[0], false);
                        foreach (string[] v in BackgroundVerbs)
                            cls.DeleteSubKeyTree(@"Directory\Background\shell\" + v[0], false);
                    }
                }
                Shell.NotifyChanged();
                Log.Write("우클릭 메뉴 해제");
                return null;
            }
            catch (Exception ex) { return "우클릭 메뉴를 해제할 수 없습니다: " + ex.Message; }
        }
    }
}
