using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.UI;

namespace GitExtensions.WinUI.Controls;

/// <summary>
///  A drag handle that resizes an adjacent pane.
/// </summary>
/// <remarks>
///  <para>
///   WinUI 3 ships no GridSplitter — the familiar one lives in the Community Toolkit, which this
///   project deliberately does not take a dependency on. This is the small part of it that the shell
///   needs: drag horizontally or vertically to set an explicit <see cref="FrameworkElement.Width"/>
///   or <see cref="FrameworkElement.Height"/> on <see cref="Target"/>.
///  </para>
///  <para>
///   Derived from <see cref="Grid"/> rather than <see cref="Control"/> so it needs no control
///   template to paint: a panel draws its own <see cref="Panel.Background"/>, and that background
///   (transparent, but present) is also what makes the bar hit-testable across its full grab width.
///  </para>
///  <para>
///   Driven by pointer capture rather than manipulation events: manipulation is tuned for touch
///   inertia, and a splitter wants to track the mouse exactly, including outside its own bounds.
///  </para>
/// </remarks>
public sealed partial class PaneSplitter : Grid
{
    /// <summary>How wide the invisible grab area is; the drawn line is thinner than the target.</summary>
    private const double GrabThickness = 6;

    public static readonly DependencyProperty TargetProperty = DependencyProperty.Register(
        nameof(Target), typeof(FrameworkElement), typeof(PaneSplitter), new PropertyMetadata(null));

    public static readonly DependencyProperty OrientationProperty = DependencyProperty.Register(
        nameof(Orientation), typeof(Orientation), typeof(PaneSplitter), new PropertyMetadata(Orientation.Vertical, OnOrientationChanged));

    public static readonly DependencyProperty InvertProperty = DependencyProperty.Register(
        nameof(Invert), typeof(bool), typeof(PaneSplitter), new PropertyMetadata(false));

    public static readonly DependencyProperty MinimumProperty = DependencyProperty.Register(
        nameof(Minimum), typeof(double), typeof(PaneSplitter), new PropertyMetadata(120d));

    public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(
        nameof(Maximum), typeof(double), typeof(PaneSplitter), new PropertyMetadata(1600d));

    /// <summary>The hairline actually drawn inside the wider grab area.</summary>
    private readonly Border _line = new()
    {
        Background = new SolidColorBrush(Color.FromArgb(40, 128, 128, 128))
    };

    /// <summary>Where the drag started, in the coordinate space of the window.</summary>
    private Point _origin;

    /// <summary>The target's size when the drag started; the drag is applied as an offset from this.</summary>
    private double _originalSize;

    private bool _isDragging;

    public PaneSplitter()
    {
        // Transparent rather than null: a Panel with no Background is not hit-testable at all.
        Background = new SolidColorBrush(Colors.Transparent);
        Children.Add(_line);
        ApplyOrientation();

        // Events rather than overrides: the OnPointer* methods are virtual on Control, not on Panel,
        // so a Grid-derived control has to subscribe like any other consumer.
        PointerEntered += OnSplitterPointerEntered;
        PointerPressed += OnSplitterPointerPressed;
        PointerMoved += OnSplitterPointerMoved;
        PointerReleased += OnSplitterPointerReleased;
        PointerCaptureLost += OnSplitterPointerCaptureLost;
    }

    /// <summary>The pane being resized. Its Width (or Height) is what the drag writes to.</summary>
    public FrameworkElement? Target
    {
        get => (FrameworkElement?)GetValue(TargetProperty);
        set => SetValue(TargetProperty, value);
    }

    /// <summary>
    ///  <see cref="Orientation.Vertical"/> for a vertical bar dragged left/right (resizing a width),
    ///  <see cref="Orientation.Horizontal"/> for a horizontal bar dragged up/down (resizing a height).
    /// </summary>
    public Orientation Orientation
    {
        get => (Orientation)GetValue(OrientationProperty);
        set => SetValue(OrientationProperty, value);
    }

    /// <summary>
    ///  True when the pane is on the far side of the splitter, so dragging towards it makes it
    ///  smaller — a right-hand details pane or a bottom diff panel.
    /// </summary>
    public bool Invert
    {
        get => (bool)GetValue(InvertProperty);
        set => SetValue(InvertProperty, value);
    }

    public double Minimum
    {
        get => (double)GetValue(MinimumProperty);
        set => SetValue(MinimumProperty, value);
    }

    public double Maximum
    {
        get => (double)GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    private bool IsHorizontalDrag => Orientation == Orientation.Vertical;

    /// <summary>The cursor is the only thing that tells the user this thin bar is draggable.</summary>
    private void OnSplitterPointerEntered(object sender, PointerRoutedEventArgs e) =>
        ProtectedCursor = InputSystemCursor.Create(
            IsHorizontalDrag ? InputSystemCursorShape.SizeWestEast : InputSystemCursorShape.SizeNorthSouth);

    private void OnSplitterPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (Target is not FrameworkElement target || !CapturePointer(e.Pointer))
        {
            return;
        }

        _isDragging = true;
        _origin = e.GetCurrentPoint(null).Position;

        // ActualWidth is the truth at drag start: the pane may never have had an explicit Width set.
        double explicitSize = IsHorizontalDrag ? target.Width : target.Height;
        _originalSize = double.IsNaN(explicitSize)
            ? (IsHorizontalDrag ? target.ActualWidth : target.ActualHeight)
            : explicitSize;

        e.Handled = true;
    }

    private void OnSplitterPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_isDragging || Target is not FrameworkElement target)
        {
            return;
        }

        Point current = e.GetCurrentPoint(null).Position;
        double delta = IsHorizontalDrag ? current.X - _origin.X : current.Y - _origin.Y;

        if (Invert)
        {
            delta = -delta;
        }

        double size = Math.Clamp(_originalSize + delta, Minimum, Maximum);

        if (IsHorizontalDrag)
        {
            target.Width = size;
        }
        else
        {
            target.Height = size;
        }

        e.Handled = true;
    }

    private void OnSplitterPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_isDragging)
        {
            _isDragging = false;
            ReleasePointerCapture(e.Pointer);
            e.Handled = true;
        }
    }

    private void OnSplitterPointerCaptureLost(object sender, PointerRoutedEventArgs e) => _isDragging = false;

    private static void OnOrientationChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) =>
        ((PaneSplitter)sender).ApplyOrientation();

    private void ApplyOrientation()
    {
        if (IsHorizontalDrag)
        {
            Width = GrabThickness;
            Height = double.NaN;
            HorizontalAlignment = HorizontalAlignment.Center;
            VerticalAlignment = VerticalAlignment.Stretch;
            _line.Width = 1;
            _line.Height = double.NaN;
            _line.HorizontalAlignment = HorizontalAlignment.Center;
            _line.VerticalAlignment = VerticalAlignment.Stretch;
        }
        else
        {
            Height = GrabThickness;
            Width = double.NaN;
            HorizontalAlignment = HorizontalAlignment.Stretch;
            VerticalAlignment = VerticalAlignment.Center;
            _line.Height = 1;
            _line.Width = double.NaN;
            _line.HorizontalAlignment = HorizontalAlignment.Stretch;
            _line.VerticalAlignment = VerticalAlignment.Center;
        }
    }
}
