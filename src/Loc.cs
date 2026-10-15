using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace NoAdZip
{
    // 화면 문구 번역(한국어 -> English). 문구가 사전과 정확히 같을 때만 바꾸고, 숫자·파일명이 섞인 동적 문구는 한국어로 남는다.
    static class Loc
    {
        static Dictionary<string, string> map;

        public static bool English { get { return Settings.Language == "en"; } }

        static void Build()
        {
            if (map != null) return;
            map = new Dictionary<string, string>();
            string[] p = {
                // 공통
                "확인", "OK", "취소", "Cancel", "찾아보기", "Browse", "닫기", "Close",
                // 메인 창
                "열기 (Ctrl+O)", "Open (Ctrl+O)", "새 압축 (Ctrl+N)", "New archive (Ctrl+N)", "전체 풀기", "Extract all",
                "선택 풀기", "Extract selected", "검사", "Test", "파일명 인코딩", "Filename encoding", "미리보기", "Preview",
                "설정", "Settings", "상위", "Up", "이름", "Name", "크기", "Size", "압축 크기", "Packed", "종류", "Type",
                "수정한 날짜", "Modified", "암호", "Password",
                "열기", "Open", "선택 항목 풀기...", "Extract selected...", "전체 풀기...", "Extract all...",
                "압축파일을 열어 주세요.", "Open an archive.",
                "압축파일을 이 창에 끌어다 놓거나, 위의 [열기]를 눌러 선택하세요.\n\n파일·폴더를 끌어다 놓으면 압축 창이 열립니다.",
                    "Drop an archive here, or press [Open] above.\n\nDrop files or folders to create an archive.",
                // 인코딩 목록
                "자동 감지", "Auto detect", "한국어 (CP949)", "Korean (CP949)", "UTF-8", "UTF-8", "일본어 (Shift-JIS)", "Japanese (Shift-JIS)",
                "중국어 간체 (GBK)", "Chinese Simplified (GBK)", "중국어 번체 (Big5)", "Chinese Traditional (Big5)",
                "서유럽 (CP1252)", "Western European (CP1252)", "DOS 영어 (CP437)", "DOS English (CP437)",
                // 풀기 창
                "압축 풀기", "Extract", "풀 위치", "Destination", "압축파일이 있는 폴더에 풀기", "Extract to the archive's folder",
                "지정한 폴더에 풀기:", "Extract to this folder:", "압축파일 이름의 폴더를 만들어 풀기 (최상위 폴더가 하나면 생략)",
                    "Create a folder named after the archive (skipped if there is one top-level folder)",
                "같은 이름의 파일이 있을 때", "If a file already exists", "묻기", "Ask", "모두 덮어쓰기", "Overwrite all", "모두 건너뛰기", "Skip all",
                "이름 바꿔 풀기", "Rename", "파일명 인코딩:", "Filename encoding:", "풀기가 끝나면 폴더 열기", "Open the folder when done", "풀기", "Extract",
                // 압축 창
                "압축하기", "Create archive", "압축할 파일·폴더", "Files and folders to compress", "파일 추가", "Add files", "폴더 추가", "Add folder", "제거", "Remove",
                "저장 위치", "Save to", "압축 형식", "Format", "압축 수준", "Level", "스레드", "Threads",
                "저장 (압축 안 함)", "Store (no compression)", "빠름", "Fast", "보통", "Normal", "높음", "High", "최대", "Maximum",
                "자동", "Auto", "암호 (ZIP·7z)", "Password (ZIP/7z)", "암호 확인", "Confirm", "암호 보기", "Show password",
                "제외할 파일 패턴 (;로 구분, 예: *.tmp;Thumbs.db;node_modules)", "Exclude patterns (separate with ;  e.g. *.tmp;Thumbs.db;node_modules)",
                "ZIP 파일명을 UTF-8로 저장 (한글 이름 호환 권장)", "Store ZIP file names as UTF-8 (recommended for non-ASCII names)",
                "7z 솔리드 압축 (압축률 높음, 일부만 풀기는 느림)", "7z solid compression (smaller, slower partial extraction)", "7z 파일명도 암호화", "Also encrypt 7z file names",
                "분할 압축", "Split volumes", "나누지 않음", "No splitting", "직접 입력", "Custom", "MB (ZIP·7z)", "MB (ZIP/7z)", "압축", "Compress",
                "ZIP (.zip)  - 어디서나 열림", "ZIP (.zip)  - opens anywhere", "7z (.7z)  - 압축률 높음", "7z (.7z)  - best compression",
                "TAR (.tar)  - 묶기만 함", "TAR (.tar)  - bundle only", "GZ (.gz)  - 파일 1개만", "GZ (.gz)  - single file", "BZ2 (.bz2)  - 파일 1개만", "BZ2 (.bz2)  - single file",
                "XZ (.xz)  - 파일 1개만", "XZ (.xz)  - single file",
                "ZIP은 AES-256으로 암호화합니다. (파일 이름은 보임)", "ZIP uses AES-256. (file names stay visible)", "7z는 AES-256으로 암호화합니다.", "7z uses AES-256.",
                "이 형식은 암호를 지원하지 않습니다.", "This format does not support passwords.",
                // 설정 창
                "몽집 설정", "몽집 Settings", "압축 기본값", "Compression defaults", "형식", "Format", "수준", "Level", "ZIP 파일명을 UTF-8로 저장", "Store ZIP names as UTF-8",
                "풀기 기본값", "Extraction defaults", "압축파일이 있는 폴더에 바로 풀기", "Extract directly to the archive's folder", "압축파일 이름의 폴더를 만들어 풀기", "Create a folder named after the archive",
                "풀 때마다 옵션 창 표시", "Always show the options window", "같은 이름 파일", "Existing files", "압축파일 안의 실행 파일을 열 때 확인", "Confirm before opening executables",
                "압축 폭탄 경고 기준", "Zip bomb warning", "압축률", "Ratio", "배 이상이면서 풀린 크기가", "x or more and extracted size of", "GB 이상일 때 확인", "GB or more",
                "화면과 탐색기", "Appearance and Explorer", "테마", "Theme", "시스템 설정", "System", "밝게", "Light", "어둡게", "Dark", "언어", "Language",
                "(테마·언어는 창을 다시 열면 적용됩니다)", "(Applies after reopening windows)",
                "우클릭 메뉴 등록", "Add right-click menu", "우클릭 메뉴 해제", "Remove right-click menu", "Windows 11 은 '추가 옵션 표시' 안에 나옵니다.", "Win11: under 'Show more options'",
                "파일 연결 (더블클릭으로 몽집 열기)", "File associations (open with 몽집)", "선택 항목을 NoAdZip에 연결", "Associate checked types with 몽집",
                "선택 항목 연결 해제", "Remove checked associations", "Windows 기본 앱 설정 열기", "Open Windows default apps",
                "Windows 10/11 은 기본 앱을 사용자가 직접 골라야 합니다. [연결] 후 기본 앱 설정에서 몽집을 선택하세요.",
                    "Windows 10/11 requires you to choose default apps yourself. After associating, pick 몽집 in the default apps settings.",
                "로그 폴더 열기", "Open log folder",
                // 진행률·기타 창
                "작업이 끝나면", "When finished", "아무것도 안 함", "Do nothing", "알림음 재생", "Play a sound", "PC 종료", "Shut down PC",
                "압축 푸는 중", "Extracting", "압축하는 중", "Compressing", "압축파일 여는 중", "Opening archive", "압축파일 검사 중", "Testing archive", "파일 여는 중", "Opening file",
                "압축 해제 준비 중", "Preparing", "암호 입력", "Enter password", "같은 이름의 파일이 있습니다", "File already exists",
                "취소 중", "Cancelling",
                // 메시지
                "풀 압축파일을 지정하세요.", "Specify the archive to extract.", "검사할 압축파일을 지정하세요.", "Specify the archive to test.",
                "압축할 파일을 지정하세요.", "Specify the files to compress.", "풀 항목을 먼저 선택하세요.", "Select items to extract first.", "풀 항목이 없습니다.", "Nothing to extract.",
                "암호와 암호 확인이 다릅니다.", "Passwords do not match.", "압축할 파일이나 폴더를 추가하세요.", "Add files or folders to compress.",
                "저장 위치를 입력하세요.", "Enter a save location.", "저장할 폴더가 없습니다.", "The destination folder does not exist.",
                "압축파일이 자기 자신을 포함할 수 없습니다.", "An archive cannot contain itself.",
                "압축파일이 만들어지지 않았습니다.", "The archive was not created.", "풀 폴더를 지정하세요.", "Specify the destination folder.",
                "서로 다른 드라이브의 항목은 한 번에 묶을 수 없습니다.", "Items on different drives cannot be combined.",
                "안전하지 않은 경로라 열 수 없습니다.", "Cannot open: the path is unsafe.", "링크 항목은 열 수 없습니다.", "Cannot open link entries.",
                "연결할 확장자를 체크하세요.", "Check the extensions to associate.", "해제할 확장자를 체크하세요.", "Check the extensions to remove.",
                "연결을 해제했습니다.", "Associations removed.", "탐색기 우클릭 메뉴를 해제했습니다.", "The right-click menu was removed.",
            };
            for (int i = 0; i + 1 < p.Length; i += 2) map[p[i]] = p[i + 1];
        }

        public static string T(string ko)
        {
            if (!English || string.IsNullOrEmpty(ko)) return ko;
            Build();
            string en;
            return map.TryGetValue(ko, out en) ? en : ko;
        }

        public static void Apply(Control c)
        {
            if (!English) return;
            Build();
            Walk(c);
        }

        static void Walk(Control c)
        {
            if (!(c is TextBox) && !(c is ComboBox) && !(c is ListBox) && !(c is NumericUpDown) && c.Text.Length > 0)
                c.Text = T(c.Text);
            ComboBox cb = c as ComboBox;
            if (cb != null) { int sel = cb.SelectedIndex; TranslateItems(cb.Items); if (cb.SelectedIndex != sel) cb.SelectedIndex = sel; }
            ListView lv = c as ListView;
            if (lv != null) foreach (ColumnHeader h in lv.Columns) h.Text = T(h.Text);
            ToolStrip ts = c as ToolStrip;
            if (ts != null) WalkItems(ts.Items);
            if (c.ContextMenuStrip != null) WalkItems(c.ContextMenuStrip.Items);
            foreach (Control k in c.Controls) Walk(k);
        }

        static void TranslateItems(ComboBox.ObjectCollection items)
        {
            for (int i = 0; i < items.Count; i++)
            {
                string s = items[i] as string;
                if (s == null) continue;
                string t = T(s);
                if (t != s) items[i] = t;
            }
        }

        static void WalkItems(ToolStripItemCollection items)
        {
            foreach (ToolStripItem it in items)
            {
                if (it.Text != null && it.Text.Length > 0) it.Text = T(it.Text);
                ToolStripComboBox cb = it as ToolStripComboBox;
                if (cb != null) { int sel = cb.SelectedIndex; TranslateItems(cb.Items); if (cb.SelectedIndex != sel) cb.SelectedIndex = sel; }
                ToolStripDropDownItem dd = it as ToolStripDropDownItem;
                if (dd != null && dd.HasDropDownItems) WalkItems(dd.DropDownItems);
            }
        }
    }
}
