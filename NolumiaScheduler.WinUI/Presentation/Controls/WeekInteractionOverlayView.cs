using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using NolumiaScheduler.Presentation.Controls;

namespace NolumiaScheduler.WinUI.Presentation.Controls;

public partial class WeekInteractionOverlayView : UserControl
{
    private readonly Canvas _canvas = new();

    public static readonly DependencyProperty PreviewProperty =
        DependencyProperty.Register(nameof(Preview), typeof(WeekInteractionPreview), typeof(WeekInteractionOverlayView),
            new PropertyMetadata(null, (d, _) => ((WeekInteractionOverlayView)d).Redraw()));

    public WeekInteractionPreview? Preview
    {
        get => (WeekInteractionPreview?)GetValue(PreviewProperty);
        set => SetValue(PreviewProperty, value);
    }

    public WeekInteractionOverlayView()
    {
        Content = _canvas;
        IsHitTestVisible = false;
        SizeChanged += (_, _) => Redraw();
    }

    private void Redraw()
    {
        _canvas.Children.Clear();

        if (Preview is not { IsVisible: true }) return;

        var top = (double)Preview.StartMinute;
        var bottom = Math.Max(Preview.EndMinute, Preview.StartMinute + 15);
        var height = bottom - top;
        var width = ActualWidth > 0 ? ActualWidth : 120;

        // Mirror the event chip's horizontal sizing (see
        // WeekCalendarView.ApplyChipHorizontalBounds) so the drag/resize ghost is the
        // same size as the chip being manipulated.
        const double chipMargin = 4;
        var left = width * Preview.LeftRatio + chipMargin;
        var rawWidth = width * Preview.WidthRatio;
        var blockWidth = Math.Max(24, Math.Min(rawWidth - chipMargin, width * 0.8 - chipMargin));

        // Filled rectangle with semi-transparent blue
        var rect = new Rectangle
        {
            Width = blockWidth,
            Height = height,
            Fill = new SolidColorBrush(Windows.UI.Color.FromArgb(90, 26, 115, 232)),
            RadiusX = 6,
            RadiusY = 6
        };
        Canvas.SetLeft(rect, left);
        Canvas.SetTop(rect, top);
        _canvas.Children.Add(rect);

        // Top line
        var line = new Line
        {
            X1 = 0, Y1 = top,
            X2 = width, Y2 = top,
            Stroke = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 26, 115, 232)),
            StrokeThickness = 2
        };
        _canvas.Children.Add(line);

        // Time label
        var label = new TextBlock
        {
            Text = $"{Preview.StartMinute / 60:D2}:{Preview.StartMinute % 60:D2} - {Preview.EndMinute / 60:D2}:{Preview.EndMinute % 60:D2}",
            FontSize = 10,
            Foreground = new SolidColorBrush(Microsoft.UI.Colors.White)
        };
        Canvas.SetLeft(label, left + 6);
        Canvas.SetTop(label, top + 4);
        _canvas.Children.Add(label);
    }
}
