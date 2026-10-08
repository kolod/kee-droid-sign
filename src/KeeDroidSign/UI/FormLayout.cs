using System.Drawing;
using System.Windows.Forms;

namespace KeeDroidSign.UI
{
    /// <summary>Small helpers so the code-built dialogs share one DPI-friendly layout.</summary>
    internal static class FormLayout
    {
        public static TableLayoutPanel CreateGrid()
        {
            var grid = new TableLayoutPanel
            {
                ColumnCount = 2,
                Dock = DockStyle.Fill,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(8),
            };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            return grid;
        }

        public static T AddRow<T>(TableLayoutPanel grid, string label, T control) where T : Control
        {
            int row = grid.RowCount++;
            grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            grid.Controls.Add(new Label
            {
                Text = label,
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Margin = new Padding(3, 6, 3, 3),
            }, 0, row);
            control.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            grid.Controls.Add(control, 1, row);
            return control;
        }

        public static Label AddNote(TableLayoutPanel grid, string text)
        {
            int row = grid.RowCount++;
            grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var label = new Label
            {
                Text = text,
                AutoSize = true,
                MaximumSize = new Size(520, 0),
                ForeColor = SystemColors.GrayText,
                Margin = new Padding(3, 6, 3, 3),
            };
            grid.Controls.Add(label, 0, row);
            grid.SetColumnSpan(label, 2);
            return label;
        }

        public static FlowLayoutPanel CreateButtonBar(params Button[] buttons)
        {
            var bar = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                FlowDirection = FlowDirection.RightToLeft,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(8),
            };
            bar.Controls.AddRange(buttons);
            return bar;
        }

        public static Button CreateButton(string text)
        {
            return new Button { Text = text, AutoSize = true, MinimumSize = new Size(80, 0) };
        }
    }
}
