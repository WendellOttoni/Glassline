using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;

namespace Glassline.App;

public sealed partial class MainWindow : Window
{
    private static readonly SizeInt32 CompactSize = new(360, 64);
    private static readonly SizeInt32 ExpandedSize = new(420, 184);

    public MainWindow()
    {
        InitializeComponent();

        AppWindow.SetIcon("Assets/AppIcon.ico");
        AppWindow.Resize(CompactSize);

        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(false, false);
            presenter.IsAlwaysOnTop = true;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.IsResizable = false;
        }

        RootFrame.Navigate(typeof(MainPage));

        if (RootFrame.Content is MainPage page)
        {
            page.ExpansionChanged += OnExpansionChanged;
        }
    }

    private void OnExpansionChanged(bool isExpanded) =>
        AppWindow.Resize(isExpanded ? ExpandedSize : CompactSize);
}
