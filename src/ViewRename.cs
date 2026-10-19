using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Automation;

namespace NoAdZip
{
    // 우클릭 [새 폴더 만들기] 뒤, 우클릭한 창(탐색기·바탕화면·다른 프로그램의 폴더 선택/파일 대화상자)의 파일 목록에서
    // 만든 폴더를 찾아 선택하고 F2 를 보내 이름 바꾸기 상태로 만든다. 창을 앞으로 가져오거나 마우스를 움직이지 않는다.
    static class ViewRename
    {
        delegate bool EnumProc(IntPtr hwnd, IntPtr lParam);

        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
        [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr parent, EnumProc proc, IntPtr lParam);
        [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc proc, IntPtr lParam);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr hwnd, StringBuilder sb, int max);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string title);
        [DllImport("user32.dll")] static extern bool PostMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);

        /// <summary>우클릭 메뉴를 띄운 창 (메뉴가 닫히면 그 창이 맨 앞에 있다). 시험용으로 NOADZIP_HOSTWND=창 번호(10진)를 주면 그 창을 쓴다.</summary>
        public static IntPtr HostWindow()
        {
            string t = Environment.GetEnvironmentVariable("NOADZIP_HOSTWND");
            long v;
            if (!string.IsNullOrEmpty(t) && long.TryParse(t, out v)) return (IntPtr)v;
            IntPtr fg = GetForegroundWindow();
            return fg == IntPtr.Zero ? fg : GetAncestor(fg, 2 /* GA_ROOT */);
        }

        public static int ProcessOf(IntPtr hwnd)
        {
            uint pid;
            GetWindowThreadProcessId(hwnd, out pid);
            return (int)pid;
        }

        static string ClassOf(IntPtr h)
        {
            StringBuilder sb = new StringBuilder(64);
            GetClassName(h, sb, sb.Capacity);
            return sb.ToString();
        }

        /// <summary>창 안의 보이는 파일 목록(SHELLDLL_DefView). 바탕화면이면 Progman/WorkerW 아래에서 찾는다.</summary>
        static IntPtr FindDefView(IntPtr top)
        {
            IntPtr found = IntPtr.Zero;
            EnumProc look = delegate (IntPtr h, IntPtr l)
            {
                if (ClassOf(h) == "SHELLDLL_DefView" && IsWindowVisible(h)) { found = h; return false; }
                return true;
            };
            if (top != IntPtr.Zero) EnumChildWindows(top, look, IntPtr.Zero);
            if (found != IntPtr.Zero) return found;
            string c = top == IntPtr.Zero ? "" : ClassOf(top);
            if (c == "Progman" || c == "WorkerW")
            {
                EnumWindows(delegate (IntPtr w, IntPtr l)
                {
                    string wc = ClassOf(w);
                    if (wc != "Progman" && wc != "WorkerW") return true;
                    IntPtr dv = FindWindowEx(w, IntPtr.Zero, "SHELLDLL_DefView", null);
                    if (dv != IntPtr.Zero) { found = dv; return false; }
                    return true;
                }, IntPtr.Zero);
            }
            return found;
        }

        /// <summary>top 창의 파일 목록에서 name 항목을 선택하고 이름 바꾸기 상태로 만든다. 목록을 못 찾거나 항목이 끝내 안 보이면 false.</summary>
        public static bool Rename(IntPtr top, string name, int waitMs)
        {
            IntPtr defView = FindDefView(top);
            if (defView == IntPtr.Zero) { Log.Write("새 폴더: 파일 목록을 찾지 못함 (" + (top == IntPtr.Zero ? "창 없음" : ClassOf(top)) + ")"); return false; }
            IntPtr itemsWnd = FindWindowEx(defView, IntPtr.Zero, null, null);   // DirectUIHWND(탐색기·대화상자) 또는 SysListView32(바탕화면)
            if (itemsWnd == IntPtr.Zero) itemsWnd = defView;
            DateTime start = DateTime.Now, until = start.AddMilliseconds(waitMs);
            int tries = 0;
            AutomationElement list = null;
            while (true)
            {
                tries++;
                try
                {
                    // 파일 목록(List)은 한 번만 찾아 두고, 새 폴더가 목록에 나타날 때까지 이름으로 묻는다
                    if (list == null) list = AutomationElement.FromHandle(defView).FindFirst(TreeScope.Descendants,
                        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.List));
                    AutomationElement item = list == null ? null : FindItem(list, name);
                    if (item != null)
                    {
                        object p;
                        if (item.TryGetCurrentPattern(SelectionItemPattern.Pattern, out p)) ((SelectionItemPattern)p).Select();
                        if (item.TryGetCurrentPattern(ScrollItemPattern.Pattern, out p)) { try { ((ScrollItemPattern)p).ScrollIntoView(); } catch { } }
                        // 메시지 대기열로 보내야 목록 창의 단축키 처리(F2 = 이름 바꾸기)를 거친다
                        PostMessage(itemsWnd, 0x0100 /* WM_KEYDOWN */, (IntPtr)0x71 /* VK_F2 */, (IntPtr)0x003C0001);
                        PostMessage(itemsWnd, 0x0101 /* WM_KEYUP */, (IntPtr)0x71, unchecked((IntPtr)(int)0xC03C0001));
                        Log.Write("새 폴더: 이름 바꾸기 상태로 둠 (" + ClassOf(top) + ", " + (int)(DateTime.Now - start).TotalMilliseconds + "ms, " + tries + "회)");
                        return true;
                    }
                }
                catch (ElementNotAvailableException) { list = null; }
                catch (Exception ex) { Log.Write("새 폴더: 목록 접근 오류 " + ex.Message); return false; }
                if (DateTime.Now > until) break;
                // 파일 대화상자는 새 폴더를 목록에 늦게 반영하므로, 처음에 없으면 새로 고침(F5)을 한 번 보낸다
                if (tries == 1)
                {
                    PostMessage(itemsWnd, 0x0100 /* WM_KEYDOWN */, (IntPtr)0x74 /* VK_F5 */, (IntPtr)0x003F0001);
                    PostMessage(itemsWnd, 0x0101 /* WM_KEYUP */, (IntPtr)0x74, unchecked((IntPtr)(int)0xC03F0001));
                }
                System.Threading.Thread.Sleep(50);
            }
            Log.Write("새 폴더: 목록에서 항목을 찾지 못함 " + name + " (" + (list == null ? "목록 없음" : "항목 없음") + ")");
            return false;
        }

        static AutomationElement FindItem(AutomationElement list, string name)
        {
            // 목록이 길어 화면 밖 항목이 아직 만들어지지 않았을 수 있으므로, 가능하면 목록에 직접 물어 꺼낸다
            object p;
            if (list.TryGetCurrentPattern(ItemContainerPattern.Pattern, out p))
            {
                AutomationElement it = ((ItemContainerPattern)p).FindItemByProperty(null, AutomationElement.NameProperty, name);
                object vp;
                if (it != null && it.TryGetCurrentPattern(VirtualizedItemPattern.Pattern, out vp)) { try { ((VirtualizedItemPattern)vp).Realize(); } catch { } }
                return it;
            }
            return list.FindFirst(TreeScope.Children, new AndCondition(
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem),
                new PropertyCondition(AutomationElement.NameProperty, name)));
        }
    }
}
