using System;
using System.Drawing;
using System.Windows.Forms;

namespace KeeDroidSign.UI
{
    /// <summary>
    /// Common look of the plugin's dialogs: a white header with a title and a subtitle, separators,
    /// a padded scrollable content area, a button bar, a DPI-scaled size, and a centred progress
    /// display for long operations.
    /// </summary>
    internal class HeaderedForm : Form
    {
        /// <summary>Width of wrapped texts at 96 DPI.</summary>
        private const int LogicalTextWidth = 600;

        private readonly Label _title = new Label { AutoSize = true, Margin = new Padding(0, 0, 0, 2) };
        private readonly Label _subtitle = new Label { AutoSize = true, ForeColor = SystemColors.GrayText, Margin = Padding.Empty };
        private readonly Panel _content = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        private readonly ProgressBar _progress = new ProgressBar { Style = ProgressBarStyle.Marquee, Anchor = AnchorStyles.None };
        private readonly Label _busyText = new Label { AutoSize = true, Anchor = AnchorStyles.None, Margin = new Padding(3, 8, 3, 3) };
        private readonly TableLayoutPanel _busy = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Visible = false };
        private readonly int _textWidth;

        /// <param name="windowTitle">Text of the title bar.</param>
        /// <param name="logicalWidth">Client width at 96 DPI.</param>
        /// <param name="logicalHeight">Client height at 96 DPI.</param>
        protected HeaderedForm(string windowTitle, int logicalWidth, int logicalHeight)
        {
            Text = windowTitle;
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = SystemFonts.MessageBoxFont;

            _textWidth = Px(LogicalTextWidth);
            ClientSize = new Size(Px(logicalWidth), Px(logicalHeight));
            MinimumSize = new Size(Px(Math.Min(480, logicalWidth)), Px(Math.Min(320, logicalHeight)));
            _title.Font = new Font(Font.FontFamily, Font.Size + 2, FontStyle.Bold);
            _subtitle.MaximumSize = new Size(_textWidth, 0);
            _content.Padding = new Padding(Px(18), Px(14), Px(18), Px(14));
            _busy.Padding = _content.Padding;

            _progress.Size = new Size(Px(320), Px(14));
            _busy.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));
            _busy.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _busy.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _busy.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));
            _busy.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            _busy.Controls.Add(_progress, 0, 1);
            _busy.Controls.Add(_busyText, 0, 2);
        }

        /// <summary>Width at which texts wrap, scaled to the monitor.</summary>
        protected int TextWidth { get { return _textWidth; } }

        /// <summary>The padded, scrollable area between header and buttons; dock children to its top.</summary>
        protected Panel Content { get { return _content; } }

        /// <summary>Converts a size at 96 DPI to the monitor's pixels.</summary>
        protected int Px(int logical)
        {
            return LogicalToDeviceUnits(logical);
        }

        protected void SetHeader(string title, string subtitle)
        {
            _title.Text = title;
            _subtitle.Text = subtitle ?? string.Empty;
            _subtitle.Visible = !string.IsNullOrEmpty(subtitle);
        }

        /// <summary>Builds the frame. Call once, after the content was added, with the buttons right to left.</summary>
        protected void LayOut(params Button[] buttonsRightToLeft)
        {
            var header = new Panel
            {
                Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BackColor = SystemColors.Window, Padding = new Padding(Px(18), Px(14), Px(18), Px(14)),
            };
            FlowLayoutPanel headerText = Column();
            headerText.Dock = DockStyle.Top;
            headerText.Controls.Add(_title);
            headerText.Controls.Add(_subtitle);
            header.Controls.Add(headerText);

            // Docking runs from the last added control: header and button bar take the edges, the
            // separators sit inside them, and the content (or the busy display) fills the rest.
            Controls.Add(_busy);
            Controls.Add(_content);
            Controls.Add(Separator(DockStyle.Bottom));
            Controls.Add(Separator(DockStyle.Top));
            Controls.Add(FormLayout.CreateButtonBar(buttonsRightToLeft));
            Controls.Add(header);
        }

        /// <summary>Replaces the content by a centred progress bar with <paramref name="text"/>, or restores it.</summary>
        protected void ShowBusy(bool busy, string text)
        {
            _busyText.Text = text ?? string.Empty;
            _busy.Visible = busy;
            _content.Visible = !busy;
        }

        protected static FlowLayoutPanel Column()
        {
            return new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Margin = Padding.Empty,
            };
        }

        protected Label Wrapped(string text)
        {
            return new Label { Text = text, AutoSize = true, MaximumSize = new Size(_textWidth, 0), Margin = new Padding(3, 3, 3, 3) };
        }

        /// <summary>Grey explanation, indented under the control it explains.</summary>
        protected Label Hint(string text)
        {
            Label hint = Wrapped(text);
            hint.ForeColor = SystemColors.GrayText;
            hint.MaximumSize = new Size(_textWidth - Px(20), 0);
            hint.Margin = new Padding(Px(20), 0, 3, Px(6));
            return hint;
        }

        protected static Label Heading(string text)
        {
            return new Label
            {
                Text = text, AutoSize = true, Margin = new Padding(3, 8, 3, 3),
                Font = new Font(SystemFonts.MessageBoxFont, FontStyle.Bold),
            };
        }

        /// <summary>A two-column form grid (label, field) docked to the top of the content.</summary>
        protected TableLayoutPanel Grid()
        {
            TableLayoutPanel grid = FormLayout.CreateGrid();
            grid.Dock = DockStyle.Top;
            grid.Padding = Padding.Empty;
            return grid;
        }

        /// <summary>A bold section title spanning both grid columns.</summary>
        protected void AddSection(TableLayoutPanel grid, string text)
        {
            int row = grid.RowCount++;
            grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Label heading = Heading(text);
            heading.Margin = new Padding(3, row == 0 ? 0 : Px(12), 3, Px(4));
            grid.Controls.Add(heading, 0, row);
            grid.SetColumnSpan(heading, 2);
        }

        /// <summary>A grey note spanning both grid columns.</summary>
        protected Label AddHint(TableLayoutPanel grid, string text)
        {
            Label note = FormLayout.AddNote(grid, text);
            note.MaximumSize = new Size(_textWidth, 0);
            note.Margin = new Padding(3, 0, 3, Px(6));
            return note;
        }

        private static Label Separator(DockStyle dock)
        {
            return new Label { Dock = dock, Height = 1, BackColor = SystemColors.ControlDark, AutoSize = false };
        }
    }
}
