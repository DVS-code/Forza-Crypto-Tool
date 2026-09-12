using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ForzaCryptoTool;

internal sealed class HexViewer : ListBox
{
    private const int BytesPerRow = 16;

    private const int MaxBytes = 8 * 1024 * 1024;

    public HexViewer()
    {
        FontFamily = (FontFamily)Application.Current.FindResource("MonoFont");
        FontSize = (double)Application.Current.FindResource("FontSmall");
        Background = (Brush)Application.Current.FindResource("Surface");
        Foreground = (Brush)Application.Current.FindResource("TextPrimary");
        BorderBrush = (Brush)Application.Current.FindResource("Border");
        BorderThickness = new Thickness(1);
        SelectionMode = SelectionMode.Extended;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        ScrollViewer.SetHorizontalScrollBarVisibility(this, ScrollBarVisibility.Auto);
        VirtualizingPanel.SetIsVirtualizing(this, true);
        VirtualizingPanel.SetVirtualizationMode(this, VirtualizationMode.Recycling);
    }

    public bool Truncated { get; private set; }

    public void Show(ReadOnlySpan<byte> data)
    {
        Truncated = data.Length > MaxBytes;
        int length = Math.Min(data.Length, MaxBytes);

        var rows = new List<string>(length / BytesPerRow + 1);
        var line = new StringBuilder(80);

        for (int offset = 0; offset < length; offset += BytesPerRow)
        {
            int take = Math.Min(BytesPerRow, length - offset);
            line.Clear();
            line.Append(offset.ToString("X8")).Append("  ");

            for (int i = 0; i < BytesPerRow; i++)
            {
                line.Append(i < take ? data[offset + i].ToString("X2") : "  ").Append(' ');
                if (i == 7) line.Append(' ');
            }

            line.Append(' ');
            for (int i = 0; i < take; i++)
            {
                byte b = data[offset + i];
                line.Append(b >= 32 && b < 127 ? (char)b : '.');
            }
            rows.Add(line.ToString());
        }

        ItemsSource = rows;
    }

    public void Clear() => ItemsSource = null;
}
