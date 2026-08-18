using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;

namespace GitExtensions.WinUI.Controls;

/// <summary>
///  Renders a <see cref="DiffLineViewModel"/>'s coloured runs into a TextBlock.
/// </summary>
/// <remarks>
///  TextBlock.Inlines is not a bindable property and TextBlock is sealed, so an attached property is
///  the way to get per-token colouring from a DataTemplate without a converter or code-behind per row.
/// </remarks>
public static class DiffTextBehavior
{
    public static readonly DependencyProperty LineProperty = DependencyProperty.RegisterAttached(
        "Line",
        typeof(DiffLineViewModel),
        typeof(DiffTextBehavior),
        new PropertyMetadata(null, OnLineChanged));

    public static void SetLine(DependencyObject element, DiffLineViewModel? value) => element.SetValue(LineProperty, value);

    public static DiffLineViewModel? GetLine(DependencyObject element) => (DiffLineViewModel?)element.GetValue(LineProperty);

    private static void OnLineChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBlock textBlock)
        {
            return;
        }

        // Containers are recycled during virtualization, so always start from empty.
        textBlock.Inlines.Clear();

        if (e.NewValue is not DiffLineViewModel line)
        {
            return;
        }

        foreach (DiffRun run in line.Runs)
        {
            textBlock.Inlines.Add(new Run { Text = run.Text, Foreground = run.Foreground });
        }
    }
}
