using System.Drawing;
using System.Windows.Forms;

namespace ToolsBox.App.Infrastructure;

/// <summary>托盘菜单沿用工具箱配色，不改变菜单操作或系统通知气泡。</summary>
internal static class ComfortTrayMenu
{
    public static void Apply(ContextMenuStrip menu, bool dark)
    {
        var palette = new MenuPalette(dark);
        menu.Renderer = new ToolStripProfessionalRenderer(palette) { RoundedEdges = true };
        menu.BackColor = palette.ToolStripDropDownBackground;
        menu.ForeColor = dark ? Color.FromArgb(236, 240, 246) : Color.FromArgb(24, 37, 54);
        menu.Font = SystemFonts.MessageBoxFont;
        menu.Padding = new Padding(6);
        foreach (ToolStripItem item in menu.Items)
        {
            item.ForeColor = menu.ForeColor;
            if (item is ToolStripMenuItem) item.Padding = new Padding(8, 5, 8, 5);
        }
    }

    private sealed class MenuPalette(bool dark) : ProfessionalColorTable
    {
        private readonly Color _background = dark ? Color.FromArgb(32, 38, 50) : Color.White;
        private readonly Color _hover = dark ? Color.FromArgb(41, 51, 66) : Color.FromArgb(243, 246, 252);
        private readonly Color _border = dark ? Color.FromArgb(51, 61, 77) : Color.FromArgb(226, 232, 240);
        private readonly Color _accent = dark ? Color.FromArgb(125, 181, 255) : Color.FromArgb(35, 105, 218);

        public override Color ToolStripDropDownBackground => _background;
        public override Color ImageMarginGradientBegin => _background;
        public override Color ImageMarginGradientMiddle => _background;
        public override Color ImageMarginGradientEnd => _background;
        public override Color MenuItemSelected => _hover;
        public override Color MenuItemSelectedGradientBegin => _hover;
        public override Color MenuItemSelectedGradientEnd => _hover;
        public override Color MenuItemBorder => _accent;
        public override Color MenuBorder => _border;
        public override Color SeparatorDark => _border;
        public override Color SeparatorLight => _border;
    }
}
