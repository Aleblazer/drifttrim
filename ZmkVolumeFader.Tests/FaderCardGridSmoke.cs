using System.Drawing;
using System.Windows.Forms;
using ZmkVolumeFader;

internal static class FaderCardGridSmoke
{
    public static void Run()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { Exercise(); }
            catch (Exception ex) { failure = ex; }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        if (!thread.Join(TimeSpan.FromSeconds(15))) throw new TimeoutException("Responsive layout test timed out.");
        if (failure != null) throw new InvalidOperationException("Responsive layout test failed.", failure);
        Console.WriteLine("Responsive layout passed: one/two columns, order, unequal heights, scrolling, resize and empty state.");
    }

    static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    static void Exercise()
    {
        using var form = new Form { ClientSize = new Size(460, 400), Opacity = 0 };
        var viewport = new RoundedScrollPanel { Dock = DockStyle.Fill };
        var grid = new FaderCardGrid();
        viewport.SetContent(grid);
        form.Controls.Add(viewport);
        form.Show();
        // Unequal heights ensure the next row clears both cards, including margins.
        var cards = new[] { 168, 185, 168, 190, 168 }.Select(height => new Panel
        {
            Height = height, Margin = new Padding(0, 0, 0, 12),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
        }).ToArray();
        grid.Controls.AddRange(cards);
        int threshold = (int)Math.Round((FaderCardGrid.MinimumCardWidth * 2 + FaderCardGrid.ColumnGap) * grid.DeviceDpi / 96d);
        int scrollbarSpace = (int)Math.Round(11 * grid.DeviceDpi / 96d);
        foreach (int width in new[] { 460, threshold - 1, threshold, threshold + scrollbarSpace - 1,
            threshold + scrollbarSpace, 1200, 460, 1200 })
        {
            form.ClientSize = new Size(width, 400);
            viewport.Reflow();
            int columns = grid.ColumnsFor(grid.Width);
            Check(columns == (width < threshold + scrollbarSpace ? 1 : 2), "Wrong column count after resize.");
            for (int i = 0; i < cards.Length; i++)
            {
                var card = cards[i];
                Check(card.Left >= 0 && card.Right <= grid.Width, "Card extends outside the viewport.");
                Check(card.Width >= 420, "Card controls have insufficient horizontal space.");
                if (i >= columns)
                    Check(card.Top >= cards[i - columns].Bottom + cards[i - columns].Margin.Bottom,
                        "Rows overlap after resize.");
                if (i % columns == 1)
                    Check(card.Top == cards[i - 1].Top && card.Left > cards[i - 1].Right,
                        "Two-column layout lost row order or gap.");
            }
            Check(grid.Height == grid.GetPreferredSize(new Size(grid.Width, 0)).Height,
                "Scrollbar content height does not match the layout.");
        }
        Check(grid.Height > viewport.Height, "Short window cannot scroll to the final card.");
        form.ClientSize = new Size(1200, 1000);
        viewport.Reflow();
        Check(grid.Width == viewport.ClientSize.Width && grid.Height <= viewport.Height,
            "Scrollbar space was not reclaimed in a taller window.");
        form.ClientSize = new Size(threshold, 1000);
        viewport.Reflow();
        Check(grid.ColumnsFor(grid.Width) == 2, "Unneeded scrollbar prevented two columns at the boundary.");
        // Adding/removing cards must use the current width without rebuilding controls.
        var last = cards[^1];
        grid.Controls.Remove(last);
        last.Dispose();
        viewport.Reflow();
        Check(cards[2].Top == cards[3].Top, "Removing an odd final card changed ordering.");
        foreach (var card in cards.Take(4)) { grid.Controls.Remove(card); card.Dispose(); }
        var empty = new Panel { Height = 168, Margin = new Padding(0, 0, 0, 12) };
        grid.Controls.Add(empty);
        viewport.Reflow();
        Check(grid.ColumnsFor(grid.Width) == 1 && empty.Width == grid.Width,
            "Empty-state card does not span the window.");
        form.Close();
    }
}
