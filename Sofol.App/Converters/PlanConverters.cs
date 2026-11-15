using System;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Sofol.Core.Models;
using Windows.UI;

namespace Sofol.App.Converters;

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is true ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        value is Visibility.Visible;
}

public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        value is Visibility.Collapsed;
}

public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is bool b ? !b : true;

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        value is bool b ? !b : false;
}

public sealed class BadgeBackgroundConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is not RuleBadgeType badge)
        {
            return new SolidColorBrush(Color.FromArgb(32, 128, 128, 128));
        }

        Color color = badge switch
        {
            RuleBadgeType.SmartNameGroup => Color.FromArgb(36, 0, 120, 212),
            RuleBadgeType.Keyword => Color.FromArgb(36, 135, 100, 184),
            RuleBadgeType.Extension => Color.FromArgb(36, 16, 137, 62),
            RuleBadgeType.LargeFile => Color.FromArgb(36, 202, 80, 16),
            RuleBadgeType.LockedOrDownloading => Color.FromArgb(40, 209, 52, 56),
            _ => Color.FromArgb(32, 110, 118, 129)
        };
        return new SolidColorBrush(color);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}

public sealed class BadgeForegroundConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is not RuleBadgeType badge)
        {
            return new SolidColorBrush(Colors.Gray);
        }

        Color color = badge switch
        {
            RuleBadgeType.SmartNameGroup => Color.FromArgb(255, 0, 103, 192),
            RuleBadgeType.Keyword => Color.FromArgb(255, 116, 77, 169),
            RuleBadgeType.Extension => Color.FromArgb(255, 14, 122, 54),
            RuleBadgeType.LargeFile => Color.FromArgb(255, 180, 70, 12),
            RuleBadgeType.LockedOrDownloading => Color.FromArgb(255, 196, 43, 28),
            _ => Color.FromArgb(255, 100, 110, 120)
        };
        return new SolidColorBrush(color);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}

public sealed class StatusForegroundConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is not PlanItemStatus status)
        {
            return new SolidColorBrush(Colors.Gray);
        }

        Color color = status switch
        {
            PlanItemStatus.Ready => Color.FromArgb(255, 0, 103, 192),
            PlanItemStatus.Moved => Color.FromArgb(255, 16, 124, 16),
            PlanItemStatus.LockedOrDownloading => Color.FromArgb(255, 196, 43, 28),
            PlanItemStatus.Failed => Color.FromArgb(255, 196, 43, 28),
            _ => Color.FromArgb(255, 120, 120, 120)
        };
        return new SolidColorBrush(color);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}

public sealed class UndoneBadgeBackgroundConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is true
            ? new SolidColorBrush(Color.FromArgb(30, 128, 128, 128))
            : new SolidColorBrush(Color.FromArgb(36, 16, 137, 62));

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}

public sealed class UndoneBadgeForegroundConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is true
            ? new SolidColorBrush(Color.FromArgb(255, 115, 115, 115))
            : new SolidColorBrush(Color.FromArgb(255, 14, 122, 54));

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}

public sealed class WatcherBadgeBackgroundConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is true
            ? new SolidColorBrush(Color.FromArgb(36, 16, 137, 62))
            : new SolidColorBrush(Color.FromArgb(28, 128, 128, 128));

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}

public sealed class WatcherBadgeForegroundConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is true
            ? new SolidColorBrush(Color.FromArgb(255, 14, 122, 54))
            : new SolidColorBrush(Color.FromArgb(255, 110, 118, 129));

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}
