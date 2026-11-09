namespace ZmkVolumeFader;

/// <summary>One or two columns in layout order, with enough room for each card's controls.</summary>
internal sealed class FaderCardGrid : Panel
{
    internal const int MinimumCardWidth = 420;
    internal const int ColumnGap = 12;

    public FaderCardGrid() => DoubleBuffered = true;

    internal int ColumnsFor(int width) => Controls.Count > 1 &&
        width >= LogicalToDeviceUnits(MinimumCardWidth * 2 + ColumnGap) ? 2 : 1;

    public override Size GetPreferredSize(Size proposedSize) =>
        new(proposedSize.Width, Arrange(Math.Max(1, proposedSize.Width), false));

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        Arrange(ClientSize.Width, true);
    }

    int Arrange(int width, bool apply)
    {
        int columns = ColumnsFor(width);
        int gap = columns == 2 ? LogicalToDeviceUnits(ColumnGap) : 0;
        int cellWidth = Math.Max(1, (width - gap) / columns);
        int y = 0;
        for (int first = 0; first < Controls.Count; first += columns)
        {
            int rowHeight = 0;
            for (int col = 0; col < columns && first + col < Controls.Count; col++)
            {
                var card = Controls[first + col];
                var margin = card.Margin;
                // Give the final column any pixel left over from integer division.
                int cellRight = col == columns - 1 ? width : cellWidth;
                int x = col * (cellWidth + gap);
                if (apply) card.SetBounds(x + margin.Left, y + margin.Top,
                    Math.Max(1, cellRight - x - margin.Horizontal), card.Height);
                rowHeight = Math.Max(rowHeight, card.Height + margin.Vertical);
            }
            y += rowHeight;
        }
        return y;
    }
}
