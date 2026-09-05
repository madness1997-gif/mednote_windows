using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace MedNote.Windows.App.Controls;

/// <summary>Scalable outline icon with an optional active-tool label.</summary>
public sealed partial class ReaderToolContent : UserControl
{
    public static readonly DependencyProperty IconDataProperty = DependencyProperty.Register(
        nameof(IconData), typeof(Geometry), typeof(ReaderToolContent), new PropertyMetadata(null));
    public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(
        nameof(Label), typeof(string), typeof(ReaderToolContent), new PropertyMetadata(string.Empty));
    public static readonly DependencyProperty IsActiveProperty = DependencyProperty.Register(
        nameof(IsActive), typeof(bool), typeof(ReaderToolContent), new PropertyMetadata(false, OnIsActiveChanged));

    public ReaderToolContent()
    {
        InitializeComponent();
        RegisterPropertyChangedCallback(IsEnabledProperty, (sender, _) =>
        {
            var content = (ReaderToolContent)sender;
            content.Opacity = content.IsEnabled ? 1d : 0.4d;
        });
    }

    public Geometry IconData
    {
        get => (Geometry)GetValue(IconDataProperty);
        set => SetValue(IconDataProperty, value);
    }

    public string Label
    {
        get => (string)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public bool IsActive
    {
        get => (bool)GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }

    private static void OnIsActiveChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var content = (ReaderToolContent)sender;
        content.LabelText.Visibility = (bool)args.NewValue ? Visibility.Visible : Visibility.Collapsed;
    }
}
