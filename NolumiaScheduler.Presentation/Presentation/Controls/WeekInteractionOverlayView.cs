namespace NolumiaScheduler.Presentation.Controls;

public sealed class WeekInteractionOverlayView : GraphicsView, IDrawable
{
    public WeekInteractionOverlayView()
    {
        Drawable = this;
        InputTransparent = true;
    }

    public static readonly BindableProperty PreviewProperty =
        BindableProperty.Create(nameof(Preview), typeof(WeekInteractionPreview), typeof(WeekInteractionOverlayView), null, propertyChanged: OnPreviewChanged);

    public WeekInteractionPreview? Preview
    {
        get => (WeekInteractionPreview?)GetValue(PreviewProperty);
        set => SetValue(PreviewProperty, value);
    }

    private static void OnPreviewChanged(BindableObject bindable, object oldValue, object newValue)
        => ((WeekInteractionOverlayView)bindable).Invalidate();

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        if (Preview is not { IsVisible: true }) return;

        var top = Preview.StartMinute;
        var bottom = Math.Max(Preview.EndMinute, Preview.StartMinute + 15);
        var height = bottom - top;

        var left = dirtyRect.Width * (float)Preview.LeftRatio;
        var width = Math.Max(24, dirtyRect.Width * (float)Preview.WidthRatio);
        canvas.FillColor = Color.FromArgb("#5A1A73E8");
        canvas.FillRoundedRectangle(left, (float)top, width, (float)height, 6);

        canvas.StrokeColor = Color.FromArgb("#1A73E8");
        canvas.StrokeSize = 2;
        canvas.DrawLine(0, (float)top, dirtyRect.Width, (float)top);

        canvas.FontColor = Colors.White;
        canvas.FontSize = 10;
        var label = $"{Preview.StartMinute / 60:D2}:{Preview.StartMinute % 60:D2} - {Preview.EndMinute / 60:D2}:{Preview.EndMinute % 60:D2}";
        canvas.DrawString(label, left + 6, (float)top + 4, width - 8, 16, HorizontalAlignment.Left, VerticalAlignment.Top);
    }
}
