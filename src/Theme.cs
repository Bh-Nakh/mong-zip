using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace NoAdZip
{
    // 밝은/어두운 화면. WinForms 기본 컨트롤을 직접 색칠한다.
    static class Theme
    {
        public static bool Dark;
        public static readonly Color Back = Color.FromArgb(32, 32, 32);
        public static readonly Color Fore = Color.FromArgb(235, 235, 235);
        public static readonly Color Ctrl = Color.FromArgb(52, 52, 52);
        public static readonly Color Hot = Color.FromArgb(72, 72, 72);
        public static readonly Color Border = Color.FromArgb(96, 96, 96);

        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int val, int size);

        public static void Init()
        {
            string t = Settings.Theme;
            if (t == "dark") Dark = true;
            else if (t == "light") Dark = false;
            else
            {
                try
                {
                    using (RegistryKey k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                    {
                        object v = k == null ? null : k.GetValue("AppsUseLightTheme");
                        Dark = v is int && (int)v == 0;
                    }
                }
                catch { Dark = false; }
            }
        }

        public static void ApplyTitleBar(Form f)
        {
            if (!Dark || !f.IsHandleCreated) return;
            try
            {
                int v = 1;
                DwmSetWindowAttribute(f.Handle, 20, ref v, 4);
                DwmSetWindowAttribute(f.Handle, 19, ref v, 4);
            }
            catch { }
        }

        public static void Apply(Control c)
        {
            if (!Dark) return;
            Paint(c);
        }

        static void Paint(Control c)
        {
            if (c is Form || c is Panel || c is Label || c is GroupBox || c is CheckBox || c is RadioButton)
            {
                c.BackColor = Back;
                c.ForeColor = Fore;
            }
            if (c is Button)
            {
                Button b = (Button)c;
                b.FlatStyle = FlatStyle.Flat;
                b.FlatAppearance.BorderColor = Border;
                b.FlatAppearance.MouseOverBackColor = Hot;
                b.BackColor = Ctrl;
                b.ForeColor = Fore;
                b.UseVisualStyleBackColor = false;
            }
            else if (c is TextBox)
            {
                c.BackColor = Ctrl;
                c.ForeColor = Fore;
                ((TextBox)c).BorderStyle = BorderStyle.FixedSingle;
            }
            else if (c is ComboBox)
            {
                ComboBox cb = (ComboBox)c;
                cb.FlatStyle = FlatStyle.Flat;
                cb.BackColor = Ctrl;
                cb.ForeColor = Fore;
            }
            else if (c is NumericUpDown || c is ListBox)
            {
                c.BackColor = Ctrl;
                c.ForeColor = Fore;
            }
            else if (c is ListView)
            {
                ListView lv = (ListView)c;
                lv.BackColor = Back;
                lv.ForeColor = Fore;
                lv.OwnerDraw = true;
                lv.DrawColumnHeader += delegate (object s, DrawListViewColumnHeaderEventArgs e)
                {
                    using (SolidBrush br = new SolidBrush(Ctrl)) e.Graphics.FillRectangle(br, e.Bounds);
                    using (Pen p = new Pen(Border)) e.Graphics.DrawLine(p, e.Bounds.Right - 1, e.Bounds.Top, e.Bounds.Right - 1, e.Bounds.Bottom);
                    TextFormatFlags fl = TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.Left;
                    if (e.Header.TextAlign == HorizontalAlignment.Right) fl = TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.Right;
                    Rectangle r = Rectangle.Inflate(e.Bounds, -6, 0);
                    TextRenderer.DrawText(e.Graphics, e.Header.Text, lv.Font, r, Fore, fl);
                };
                lv.DrawItem += delegate (object s, DrawListViewItemEventArgs e) { e.DrawDefault = true; };
                lv.DrawSubItem += delegate (object s, DrawListViewSubItemEventArgs e) { e.DrawDefault = true; };
            }
            else if (c is PictureBox || c is ProgressBar)
            {
                c.BackColor = Back;
            }
            else if (c is ToolStrip)
            {
                ToolStrip ts = (ToolStrip)c;
                ts.Renderer = new DarkRenderer();
                ts.BackColor = Back;
                ts.ForeColor = Fore;
                PaintItems(ts.Items);
            }
            if (c.ContextMenuStrip != null) Paint(c.ContextMenuStrip);
            foreach (Control k in c.Controls) Paint(k);
        }

        static void PaintItems(ToolStripItemCollection items)
        {
            foreach (ToolStripItem it in items)
            {
                it.ForeColor = Fore;
                it.BackColor = Back;
                ToolStripComboBox cb = it as ToolStripComboBox;
                if (cb != null)
                {
                    cb.ComboBox.FlatStyle = FlatStyle.Flat;
                    cb.ComboBox.BackColor = Ctrl;
                    cb.ComboBox.ForeColor = Fore;
                }
                ToolStripDropDownItem dd = it as ToolStripDropDownItem;
                if (dd != null && dd.HasDropDownItems) PaintItems(dd.DropDownItems);
            }
        }

        class DarkColors : ProfessionalColorTable
        {
            public override Color ToolStripGradientBegin { get { return Back; } }
            public override Color ToolStripGradientMiddle { get { return Back; } }
            public override Color ToolStripGradientEnd { get { return Back; } }
            public override Color ToolStripBorder { get { return Back; } }
            public override Color ToolStripDropDownBackground { get { return Ctrl; } }
            public override Color ImageMarginGradientBegin { get { return Ctrl; } }
            public override Color ImageMarginGradientMiddle { get { return Ctrl; } }
            public override Color ImageMarginGradientEnd { get { return Ctrl; } }
            public override Color MenuBorder { get { return Border; } }
            public override Color MenuItemBorder { get { return Border; } }
            public override Color MenuItemSelected { get { return Hot; } }
            public override Color MenuItemSelectedGradientBegin { get { return Hot; } }
            public override Color MenuItemSelectedGradientEnd { get { return Hot; } }
            public override Color ButtonSelectedHighlight { get { return Hot; } }
            public override Color ButtonSelectedGradientBegin { get { return Hot; } }
            public override Color ButtonSelectedGradientMiddle { get { return Hot; } }
            public override Color ButtonSelectedGradientEnd { get { return Hot; } }
            public override Color ButtonSelectedBorder { get { return Border; } }
            public override Color ButtonPressedGradientBegin { get { return Border; } }
            public override Color ButtonPressedGradientMiddle { get { return Border; } }
            public override Color ButtonPressedGradientEnd { get { return Border; } }
            public override Color ButtonPressedBorder { get { return Border; } }
            public override Color ButtonCheckedGradientBegin { get { return Hot; } }
            public override Color ButtonCheckedGradientMiddle { get { return Hot; } }
            public override Color ButtonCheckedGradientEnd { get { return Hot; } }
            public override Color SeparatorDark { get { return Border; } }
            public override Color SeparatorLight { get { return Back; } }
            public override Color StatusStripGradientBegin { get { return Back; } }
            public override Color StatusStripGradientEnd { get { return Back; } }
        }

        class DarkRenderer : ToolStripProfessionalRenderer
        {
            public DarkRenderer() : base(new DarkColors()) { }

            protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e) { }

            protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
            {
                e.TextColor = Fore;
                base.OnRenderItemText(e);
            }
        }
    }
}
