using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace FourteenPlayer;

public static class SliderDragHelper
{
    /// <summary>
    /// Enables instant jump and smooth 60fps drag across the entire slider track (not just the thumb).
    /// </summary>
    public static void EnableSmoothDrag(
        Slider slider,
        Action<double>? onDragStarted = null,
        Action<double>? onDrag = null,
        Action<double>? onDragCompleted = null)
    {
        bool isDragging = false;

        double CalculateValue(Track track, System.Windows.Point pt)
        {
            if (track.ActualWidth <= 0) return slider.Value;
            double thumbWidth = track.Thumb != null && track.Thumb.ActualWidth > 0 ? track.Thumb.ActualWidth : 0;
            double usableWidth = track.ActualWidth - thumbWidth;
            double newVal;
            if (usableWidth > 0)
            {
                double offset = pt.X - (thumbWidth / 2.0);
                double ratio = Math.Clamp(offset / usableWidth, 0.0, 1.0);
                newVal = slider.Minimum + ratio * (slider.Maximum - slider.Minimum);
            }
            else
            {
                double ratio = Math.Clamp(pt.X / track.ActualWidth, 0.0, 1.0);
                newVal = slider.Minimum + ratio * (slider.Maximum - slider.Minimum);
            }
            return Math.Clamp(newVal, slider.Minimum, slider.Maximum);
        }

        slider.PreviewMouseLeftButtonDown += (s, e) =>
        {
            if (slider.Template.FindName("PART_Track", slider) is Track track)
            {
                isDragging = true;
                slider.CaptureMouse();

                System.Windows.Point pt = e.GetPosition(track);
                double newVal = CalculateValue(track, pt);
                slider.Value = newVal;

                onDragStarted?.Invoke(slider.Value);
                onDrag?.Invoke(slider.Value);
                e.Handled = true;
            }
        };

        slider.PreviewMouseMove += (s, e) =>
        {
            if (isDragging && e.LeftButton == MouseButtonState.Pressed)
            {
                if (slider.Template.FindName("PART_Track", slider) is Track track)
                {
                    System.Windows.Point pt = e.GetPosition(track);
                    double newVal = CalculateValue(track, pt);
                    slider.Value = newVal;
                    onDrag?.Invoke(slider.Value);
                    e.Handled = true;
                }
            }
        };

        slider.PreviewMouseLeftButtonUp += (s, e) =>
        {
            if (isDragging)
            {
                isDragging = false;
                slider.ReleaseMouseCapture();

                if (slider.Template.FindName("PART_Track", slider) is Track track)
                {
                    System.Windows.Point pt = e.GetPosition(track);
                    double newVal = CalculateValue(track, pt);
                    slider.Value = newVal;
                }

                onDragCompleted?.Invoke(slider.Value);
                e.Handled = true;
            }
        };

        slider.LostMouseCapture += (s, e) =>
        {
            if (isDragging)
            {
                isDragging = false;
                onDragCompleted?.Invoke(slider.Value);
            }
        };
    }
}
