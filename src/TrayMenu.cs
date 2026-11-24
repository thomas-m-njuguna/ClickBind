using System.Drawing;
using System.Windows.Forms;

namespace ClickBind
{
    internal static class MenuTheme
    {
        public static void Apply(ContextMenuStrip menu)
        {
            menu.Renderer = new ToolStripProfessionalRenderer(new MenuColors());
            menu.BackColor = Color.White;
            menu.ForeColor = Color.FromArgb(25, 28, 33);
            menu.Font = new Font("Segoe UI", 9f);
            menu.ShowImageMargin = false;
            menu.Padding = new Padding(4);
        }
    }

    internal class MenuColors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground { get { return Color.White; } }
        public override Color MenuBorder { get { return Color.FromArgb(193, 198, 212); } }
        public override Color MenuItemBorder { get { return Color.FromArgb(230, 232, 240); } }
        public override Color MenuItemSelected { get { return Color.FromArgb(230, 232, 240); } }
        public override Color ImageMarginGradientBegin { get { return Color.White; } }
        public override Color ImageMarginGradientMiddle { get { return Color.White; } }
        public override Color ImageMarginGradientEnd { get { return Color.White; } }
        public override Color SeparatorDark { get { return Color.FromArgb(193, 198, 212); } }
        public override Color SeparatorLight { get { return Color.FromArgb(193, 198, 212); } }
    }
}
