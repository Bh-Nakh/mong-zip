// NoAdZip 앱 아이콘(res\app.ico)을 생성하는 도구. 한 번 실행해서 만든 결과를 저장소에 둔다.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

static class MakeIcon
{
    static GraphicsPath Round(RectangleF r, float rad)
    {
        GraphicsPath p = new GraphicsPath();
        float d = rad * 2;
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    static Bitmap Draw(int s)
    {
        Bitmap bmp = new Bitmap(s, s, PixelFormat.Format32bppArgb);
        using (Graphics g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            float m = s * 0.05f;
            RectangleF body = new RectangleF(m, m, s - 2 * m, s - 2 * m);
            using (GraphicsPath gp = Round(body, s * 0.2f))
            using (LinearGradientBrush br = new LinearGradientBrush(body, Color.FromArgb(255, 23, 160, 170), Color.FromArgb(255, 9, 70, 98), 70f))
                g.FillPath(br, gp);

            // 지퍼 이빨
            float cx = s / 2f;
            float tw = s * 0.15f, th = s * 0.05f, gap = s * 0.075f;
            float y = s * 0.34f;
            bool left = true;
            using (SolidBrush teeth = new SolidBrush(Color.FromArgb(240, 255, 255, 255)))
            {
                while (y + th < s * 0.90f)
                {
                    float x = left ? cx - tw - s * 0.005f : cx + s * 0.005f;
                    g.FillRectangle(teeth, x, y, tw, th);
                    left = !left;
                    y += gap;
                }
            }

            // 손잡이(슬라이더)
            RectangleF sl = new RectangleF(cx - s * 0.17f, s * 0.14f, s * 0.34f, s * 0.26f);
            using (GraphicsPath sp = Round(sl, s * 0.07f))
            using (SolidBrush amber = new SolidBrush(Color.FromArgb(255, 255, 183, 30)))
                g.FillPath(amber, sp);
            RectangleF hole = new RectangleF(cx - s * 0.065f, s * 0.20f, s * 0.13f, s * 0.14f);
            using (GraphicsPath hp = Round(hole, s * 0.04f))
            using (SolidBrush dark = new SolidBrush(Color.FromArgb(255, 9, 70, 98)))
                g.FillPath(dark, hp);
        }
        return bmp;
    }

    static int Main(string[] args)
    {
        string outPath = args.Length > 0 ? args[0] : "app.ico";
        int[] sizes = { 16, 24, 32, 48, 64, 128, 256 };
        List<byte[]> pngs = new List<byte[]>();
        foreach (int s in sizes)
        {
            using (Bitmap b = Draw(s))
            using (MemoryStream ms = new MemoryStream())
            {
                b.Save(ms, ImageFormat.Png);
                pngs.Add(ms.ToArray());
            }
        }
        using (FileStream fs = new FileStream(outPath, FileMode.Create))
        using (BinaryWriter w = new BinaryWriter(fs))
        {
            w.Write((short)0);
            w.Write((short)1);
            w.Write((short)sizes.Length);
            int offset = 6 + 16 * sizes.Length;
            for (int i = 0; i < sizes.Length; i++)
            {
                w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
                w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
                w.Write((byte)0);
                w.Write((byte)0);
                w.Write((short)1);
                w.Write((short)32);
                w.Write(pngs[i].Length);
                w.Write(offset);
                offset += pngs[i].Length;
            }
            foreach (byte[] p in pngs) w.Write(p);
        }
        // 확인용 미리보기 PNG
        using (Bitmap b = Draw(256)) b.Save(Path.ChangeExtension(outPath, ".png"), ImageFormat.Png);
        return 0;
    }
}
