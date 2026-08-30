using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ForzaCryptoTool;

internal sealed class NavButton : Button
{
    private readonly Border _root;
    private readonly Border _indicator;
    private readonly TextBlock _label;
    private bool _isActive;

    public NavButton()
    {
        _indicator = new Border
        {
            Width = 3,
            CornerRadius = new CornerRadius(2),
            Background = Brushes.Transparent,
            VerticalAlignment = VerticalAlignment.Stretch,
            Margin = new Thickness(0, 4, 0, 4),
        };

        _label = new TextBlock
        {
            FontSize = 13.5,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 0, 0),
            TextTrimming = TextTrimming.CharacterEllipsis,
        };

        var row = new DockPanel();
        DockPanel.SetDock(_indicator, Dock.Left);
        row.Children.Add(_indicator);
        row.Children.Add(_label);

        _root = new Border
        {
            Padding = new Thickness(0, 10, 12, 10),
            CornerRadius = new CornerRadius(8),
            Background = Brushes.Transparent,
            Child = row,
        };

        Template = BuildTemplate();
        Content = _root;
        Background = Brushes.Transparent;
        BorderThickness = new Thickness(0);
        Cursor = Cursors.Hand;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        Margin = new Thickness(0, 0, 0, 2);
        Focusable = true;

        MouseEnter += (_, _) => Refresh(hover: true);
        MouseLeave += (_, _) => Refresh(hover: false);
        ApplyState();
    }

    public string Label
    {
        get => _label.Text;
        set => _label.Text = value;
    }

    public bool IsActive
    {
        get => _isActive;
        set { _isActive = value; ApplyState(); }
    }

    private void Refresh(bool hover)
    {
        if (_isActive) return;
        _root.Background = hover ? Brush("Overlay") : Brushes.Transparent;
        _label.Foreground = hover ? Brush("TextPrimary") : Brush("TextSecondary");
    }

    private void ApplyState()
    {
        if (_isActive)
        {
            _root.Background = Brush("Overlay");
            _indicator.Background = Brush("Accent");
            _label.Foreground = Brush("TextPrimary");
            _label.FontWeight = FontWeights.SemiBold;
        }
        else
        {
            _root.Background = Brushes.Transparent;
            _indicator.Background = Brushes.Transparent;
            _label.Foreground = Brush("TextSecondary");
            _label.FontWeight = FontWeights.Normal;
        }
    }

    private static ControlTemplate BuildTemplate()
    {
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        return new ControlTemplate(typeof(NavButton)) { VisualTree = presenter };
    }

    private static Brush Brush(string key) =>
        (Brush)Application.Current.FindResource(key);
}
