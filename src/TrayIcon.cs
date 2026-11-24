using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace ClickBind
{
    internal static class TrayIcon
    {
        private static Icon _icon;

        public static Icon Get()
        {
            if (_icon != null)
                return _icon;

            using (Bitmap bmp = new Bitmap(32, 32))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.Clear(Color.Transparent);

                    using (GraphicsPath p = Round(new Rectangle(0, 0, 32, 32), 8))
                    using (SolidBrush b = new SolidBrush(Color.FromArgb(0, 79, 150)))
                        g.FillPath(b, p);

                    Rectangle body = new Rectangle(9, 6, 14, 20);
                    using (GraphicsPath p = Round(body, body.Width / 2))
                    using (SolidBrush b = new SolidBrush(Color.White))
                        g.FillPath(b, p);

                    using (Pen pen = new Pen(Color.FromArgb(0, 79, 150), 1.6f))
                    {
                        g.DrawLine(pen, body.X + body.Width / 2f, body.Y + 1, body.X + body.Width / 2f, body.Y + body.Height * 0.42f);
                        g.DrawLine(pen, body.X + 1, body.Y + body.Height * 0.42f, body.Right - 1, body.Y + body.Height * 0.42f);
                    }
                }
                _icon = Icon.FromHandle(bmp.GetHicon());
            }
            return _icon;
        }

        private static GraphicsPath Round(Rectangle r, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            int d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
