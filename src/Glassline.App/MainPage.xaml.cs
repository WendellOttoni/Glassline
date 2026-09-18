using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace Glassline.App;

public sealed partial class MainPage : Page
{
    private bool _isExpanded;

    public MainPage()
    {
        InitializeComponent();
    }

    public event Action<bool>? ExpansionChanged;

    private void OnPointerEntered(object sender, PointerRoutedEventArgs e) => SetExpanded(true);

    private void OnPointerExited(object sender, PointerRoutedEventArgs e) => SetExpanded(false);

    private void OnSurfaceTapped(object sender, TappedRoutedEventArgs e) => SetExpanded(!_isExpanded);

    private void SetExpanded(bool value)
    {
        if (_isExpanded == value)
        {
            return;
        }

        _isExpanded = value;
        CompactContent.Visibility = value ? Visibility.Collapsed : Visibility.Visible;
        ExpandedContent.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
        ExpansionChanged?.Invoke(value);
    }
}
