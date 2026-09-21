using System.Windows;
using System.Windows.Controls;

namespace PhpManager;

public partial class StatusBar : UserControl
{
    public string StatusMessage
    {
        get => StatusText.Text;
        set => StatusText.Text = value;
    }

    public bool IsLoading
    {
        get => (bool)GetValue(IsLoadingProperty);
        set => SetValue(IsLoadingProperty, value);
    }

    public static readonly DependencyProperty IsLoadingProperty =
        DependencyProperty.Register(nameof(IsLoading), typeof(bool), typeof(StatusBar),
            new PropertyMetadata(false, OnIsLoadingChanged));

    private static void OnIsLoadingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is StatusBar bar)
        {
            bar.Spinner.Visibility = (bool)e.NewValue ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    public StatusBar()
    {
        InitializeComponent();
    }

    public void SetStatus(string message, bool loading = false)
    {
        StatusMessage = message;
        IsLoading = loading;
    }
}
