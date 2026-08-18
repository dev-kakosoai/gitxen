using GitExtensions.WinUI.Services;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using static GitExtensions.WinUI.Theming.ColorMath;

namespace GitExtensions.WinUI.Theming;

/// <summary>
///  Puts the selected theme into effect.
/// </summary>
/// <remarks>
///  Static, like <see cref="AppOptions"/> and for the same reason: the theme is process-wide, and the
///  settings page and the setup wizard both change it from several levels below the window.
/// </remarks>
public static class ThemeService
{
    private static Window? _window;
    private static FrameworkElement? _root;

    /// <summary>Whether Windows is currently set to dark, which the System choice follows.</summary>
    public static bool IsSystemDark => SystemIsDark();

    /// <summary>The palette currently in effect, never the <see cref="AppTheme.System"/> placeholder.</summary>
    public static ThemePalette Current { get; private set; } = ThemeCatalog.GitxenDark;

    /// <summary>
    ///  Raised after a theme has been applied.
    /// </summary>
    /// <remarks>
    ///  Almost nothing needs this — the brushes are mutated in place, so surfaces recolour themselves.
    ///  It is here for the few things that cannot be expressed as a brush, like a picker that has to
    ///  move its own selection tick.
    /// </remarks>
    public static event EventHandler? Changed;

    /// <summary>
    ///  Registers the brushes, before any window exists.
    /// </summary>
    /// <remarks>
    ///  Has to happen this early. The brushes become entries on <c>Application.Resources</c>, and every
    ///  page that is parsed afterwards resolves its <c>ThemeResource</c> references against them — a
    ///  window created first would come up in the WinUI default colours and then visibly change.
    /// </remarks>
    public static void Initialize()
    {
        Current = ThemeCatalog.Resolve(AppOptions.Theme, SystemIsDark());
        ThemeBrushes.Apply(Current);
    }

    /// <summary>
    ///  Takes over the window's backdrop, root background and caption buttons.
    /// </summary>
    /// <param name="window">The shell window.</param>
    /// <param name="root">Its root element, which carries the <c>ElementTheme</c>.</param>
    public static void Attach(Window window, FrameworkElement root)
    {
        _window = window;
        _root = root;

        // Only matters while the choice is System, but subscribing unconditionally means switching
        // back to System mid-session does not need the subscription set up again.
        root.ActualThemeChanged += OnActualThemeChanged;

        Apply();
    }

    /// <summary>Selects a specific palette, pinning it regardless of what Windows is set to.</summary>
    public static void Select(ThemePalette palette)
    {
        AppOptions.Theme = ThemeCatalog.ThemeFor(palette);
        Apply();
    }

    /// <summary>Follows the Windows light/dark setting, using Gitxen's own two themes.</summary>
    public static void SelectSystem()
    {
        AppOptions.Theme = AppTheme.System;
        Apply();
    }

    /// <summary>Puts <see cref="AppOptions.Theme"/> into effect.</summary>
    public static void Apply()
    {
        AppTheme choice = AppOptions.Theme;

        // Set the element theme first: when the choice is System the palette depends on what this
        // resolves to, and ActualTheme is only meaningful once RequestedTheme has been assigned.
        if (_root is not null)
        {
            _root.RequestedTheme = choice == AppTheme.System
                ? ElementTheme.Default
                : ThemeCatalog.Resolve(choice, systemIsDark: false).IsDark
                    ? ElementTheme.Dark
                    : ElementTheme.Light;
        }

        ThemePalette palette = ThemeCatalog.Resolve(choice, SystemIsDark());
        Current = palette;

        ThemeBrushes.Apply(palette);
        ApplyBackdrop(palette);
        ApplyCaptionButtons(palette);

        Changed?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>
    ///  Whether Windows is set to dark, which is what the System choice follows.
    /// </summary>
    /// <remarks>
    ///  Before a window exists the application's own requested theme is the only reading available, and
    ///  WinUI has already set it from the OS. Afterwards the root element's actual theme is used
    ///  instead, so a change made in Windows while the app is running is picked up.
    /// </remarks>
    private static bool SystemIsDark() =>
        _root is not null
            ? _root.ActualTheme == ElementTheme.Dark
            : Application.Current.RequestedTheme == ApplicationTheme.Dark;

    /// <summary>
    ///  Mica for the themes that mean to look like part of Windows, a painted background for the rest.
    /// </summary>
    /// <remarks>
    ///  Mica tints from the desktop wallpaper. A theme that commits to its own background colour cannot
    ///  keep it — the wallpaper would show through and pull the whole palette towards whatever happens
    ///  to be behind the window, which is precisely what GitHub Dark or Cyberpunk must not do.
    /// </remarks>
    private static void ApplyBackdrop(ThemePalette palette)
    {
        if (_window is null)
        {
            return;
        }

        _window.SystemBackdrop = palette.UsesMica ? new MicaBackdrop() : null;

        // With no backdrop the root element is the only thing painting the window, so it has to be
        // opaque; with Mica it has to stay transparent or there would be nothing to see through to.
        if (_root is Panel panel)
        {
            panel.Background = palette.UsesMica ? null : ThemeBrushes.Window;
        }
    }

    /// <summary>
    ///  Recolours the system caption buttons to match.
    /// </summary>
    /// <remarks>
    ///  The minimise, maximise and close buttons are drawn by Windows even with a custom title bar, and
    ///  they take their colours from the OS theme rather than the app's. Without this, a light theme on
    ///  a machine set to dark gets three white glyphs on white.
    /// </remarks>
    private static void ApplyCaptionButtons(ThemePalette palette)
    {
        if (_window?.AppWindow?.TitleBar is not AppWindowTitleBar bar)
        {
            return;
        }

        bar.ButtonBackgroundColor = Colors.Transparent;
        bar.ButtonInactiveBackgroundColor = Colors.Transparent;
        bar.ButtonForegroundColor = palette.TextPrimary;
        bar.ButtonInactiveForegroundColor = palette.TextTertiary;
        bar.ButtonHoverBackgroundColor = Alpha(palette.TextPrimary, 24);
        bar.ButtonHoverForegroundColor = palette.TextPrimary;
        bar.ButtonPressedBackgroundColor = Alpha(palette.TextPrimary, 40);
        bar.ButtonPressedForegroundColor = palette.TextPrimary;
    }

    private static void OnActualThemeChanged(FrameworkElement sender, object args)
    {
        // An explicit choice is unaffected by what Windows is doing.
        if (AppOptions.Theme == AppTheme.System)
        {
            Apply();
        }
    }
}
