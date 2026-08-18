using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace GitExtensions.WinUI.Services;

/// <summary>
///  Puts <see cref="AppOptions.Density"/> into effect.
/// </summary>
/// <remarks>
///  <para>
///   Density is two things: the commit graph's row height, which <c>CommitGraphBuilder.RowHeight</c>
///   reads directly, and the padding of every dense list row, which lives in a shared style. A style
///   is sealed once it has been applied, so the existing instance cannot be edited; instead a fresh
///   style replaces the entry in <c>Application.Resources</c>, and every page parsed after that -
///   newly opened repositories, and everything after a restart - resolves against it.
///  </para>
///  <para>
///   The replacement style is built in code rather than in a second XAML dictionary so its setters
///   stay next to the values they change; everything else about the style must match
///   <c>DenseListViewItemStyle</c> in <c>Styles/AppStyles.xaml</c>, which remains the source of truth
///   for the Comfortable shape.
///  </para>
/// </remarks>
public static class DensityService
{
    /// <summary>Replaces the dense-row style to match the current density.</summary>
    public static void Apply()
    {
        Thickness padding = AppOptions.Density == UiDensity.Compact
            ? new Thickness(8, 2, 8, 2)
            : new Thickness(8, 4, 8, 4);

        Style style = new(typeof(ListViewItem));
        style.Setters.Add(new Setter(ListViewItem.MinHeightProperty, 0d));
        style.Setters.Add(new Setter(ListViewItem.PaddingProperty, padding));
        style.Setters.Add(new Setter(ListViewItem.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));

        Application.Current.Resources["DenseListViewItemStyle"] = style;
    }
}
