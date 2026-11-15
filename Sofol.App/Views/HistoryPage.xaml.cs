using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Sofol.App.ViewModels;
using Sofol.Core.Models;

namespace Sofol.App.Views;

public sealed partial class HistoryPage : Page
{
    public MainViewModel ViewModel => App.ViewModel;

    public HistoryPage()
    {
        InitializeComponent();
    }

    private async void OnUndoSessionClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is OrganizationSession session)
        {
            await ViewModel.UndoSessionAsync(session);
        }
    }

    private async void OnViewDetailsClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.Tag is not OrganizationSession session)
        {
            return;
        }

        double contentWidth = ActualWidth > 0
            ? Math.Min(820, Math.Max(640, ActualWidth - 110))
            : 800;

        var rowsContainer = new StackPanel { Spacing = 6 };
        var countLabel = new TextBlock
        {
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = TryGetThemeBrush("TextFillColorSecondaryBrush")
        };

        void PopulateRows(string? query)
        {
            rowsContainer.Children.Clear();
            string term = query?.Trim() ?? string.Empty;

            List<FileMoveRecord> filtered = string.IsNullOrEmpty(term)
                ? session.Records
                : session.Records.Where(r =>
                    r.FileName.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                    r.MatchedRuleName.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                    r.RelativeDestinationPath.Contains(term, StringComparison.OrdinalIgnoreCase)).ToList();

            countLabel.Text = string.IsNullOrEmpty(term)
                ? $"Hiển thị toàn bộ {filtered.Count} tệp đã di chuyển"
                : $"Tìm thấy {filtered.Count} / {session.Records.Count} tệp";

            if (filtered.Count == 0)
            {
                rowsContainer.Children.Add(new TextBlock
                {
                    Margin = new Thickness(8, 24, 8, 24),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Foreground = TryGetThemeBrush("TextFillColorSecondaryBrush"),
                    Text = session.Records.Count == 0
                        ? "Phiên này không có bản ghi chi tiết tệp nào."
                        : "Không tìm thấy tệp nào khớp từ khóa đang lọc."
                });
                return;
            }

            for (int i = 0; i < filtered.Count; i++)
            {
                rowsContainer.Children.Add(BuildFileRecordRow(i + 1, filtered[i]));
            }
        }

        PopulateRows(null);

        var filterBox = new TextBox
        {
            PlaceholderText = "Lọc nhanh theo tên tệp, quy tắc hoặc thư mục đích...",
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        filterBox.TextChanged += (_, _) => PopulateRows(filterBox.Text);

        var filterBar = new Grid { ColumnSpacing = 12 };
        filterBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        filterBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(filterBox, 0);
        Grid.SetColumn(countLabel, 1);
        filterBar.Children.Add(filterBox);
        filterBar.Children.Add(countLabel);

        var listBorder = new Border
        {
            BorderBrush = TryGetThemeBrush("CardStrokeColorDefaultBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Background = TryGetThemeBrush("LayerFillColorDefaultBrush")
        };

        var listGrid = new Grid();
        listGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        listGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(330) });

        Border headerRow = BuildTableHeaderRow();
        Grid.SetRow(headerRow, 0);
        listGrid.Children.Add(headerRow);

        var scrollViewer = new ScrollViewer
        {
            Padding = new Thickness(8, 6, 8, 8),
            Content = rowsContainer
        };
        Grid.SetRow(scrollViewer, 1);
        listGrid.Children.Add(scrollViewer);

        listBorder.Child = listGrid;

        var dialogBody = new StackPanel
        {
            Width = contentWidth,
            Spacing = 12
        };
        dialogBody.Children.Add(BuildSessionSummaryCard(session));
        dialogBody.Children.Add(filterBar);
        dialogBody.Children.Add(listBorder);

        bool canOpenFolder = !string.IsNullOrWhiteSpace(session.SourceFolderPath) && Directory.Exists(session.SourceFolderPath);

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = $"Chi tiết phiên dọn dẹp • {session.ExecutedAtDisplay}",
            PrimaryButtonText = "Mở thư mục gốc",
            IsPrimaryButtonEnabled = canOpenFolder,
            SecondaryButtonText = session.IsUndone ? string.Empty : "Hoàn tác phiên này",
            CloseButtonText = "Đóng",
            DefaultButton = ContentDialogButton.Close,
            Content = dialogBody
        };
        dialog.Resources["ContentDialogMaxWidth"] = 920.0;

        dialog.PrimaryButtonClick += (_, args) =>
        {
            args.Cancel = true;
            if (canOpenFolder)
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = session.SourceFolderPath,
                    UseShellExecute = true
                });
            }
        };

        ContentDialogResult result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Secondary && !session.IsUndone)
        {
            await ViewModel.UndoSessionAsync(session);
        }
    }

    private static Border BuildSessionSummaryCard(OrganizationSession session)
    {
        var infoStack = new StackPanel { Spacing = 6 };

        var topLine = new Grid { ColumnSpacing = 12 };
        topLine.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        topLine.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var folderText = new TextBlock
        {
            FontWeight = FontWeights.SemiBold,
            Text = $"📁 Thư mục đã dọn: {session.SourceFolderPath}",
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        ToolTipService.SetToolTip(folderText, session.SourceFolderPath);

        var statusBadge = new Border
        {
            Padding = new Thickness(10, 3, 10, 3),
            CornerRadius = new CornerRadius(10),
            Background = TryGetThemeBrush("SubtleFillColorSecondaryBrush"),
            Child = new TextBlock
            {
                FontSize = 11.5,
                FontWeight = FontWeights.SemiBold,
                Text = session.StatusDisplay
            }
        };

        Grid.SetColumn(folderText, 0);
        Grid.SetColumn(statusBadge, 1);
        topLine.Children.Add(folderText);
        topLine.Children.Add(statusBadge);
        infoStack.Children.Add(topLine);

        infoStack.Children.Add(new TextBlock
        {
            FontSize = 12.5,
            TextWrapping = TextWrapping.Wrap,
            Foreground = TryGetThemeBrush("TextFillColorSecondaryBrush"),
            Text = $"Tổng hợp: {session.MovedFileCount} tệp ({session.FormattedTotalSize})  •  Phân bổ: {session.RuleBreakdownSummary}"
        });

        List<string> createdFolders = session.RelativeCreatedFolders;
        if (createdFolders.Count > 0)
        {
            infoStack.Children.Add(new TextBlock
            {
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Foreground = TryGetThemeBrush("AccentTextFillColorPrimaryBrush"),
                Text = $"Thư mục mới tạo ({createdFolders.Count}): {string.Join("  |  ", createdFolders.Select(f => $"📂 {f}"))}"
            });
        }

        return new Border
        {
            Padding = new Thickness(14, 10, 14, 10),
            CornerRadius = new CornerRadius(8),
            BorderBrush = TryGetThemeBrush("CardStrokeColorDefaultBrush"),
            BorderThickness = new Thickness(1),
            Background = TryGetThemeBrush("CardBackgroundFillColorDefaultBrush"),
            Child = infoStack
        };
    }

    private static Border BuildTableHeaderRow()
    {
        var grid = CreateRecordColumnsGrid();
        Brush? secondaryBrush = TryGetThemeBrush("TextFillColorSecondaryBrush");

        AddHeaderCell(grid, 0, "#", secondaryBrush);
        AddHeaderCell(grid, 1, "Tệp gốc & Dung lượng", secondaryBrush);
        AddHeaderCell(grid, 2, "Quy tắc áp dụng", secondaryBrush);
        AddHeaderCell(grid, 3, "Đã chuyển vào thư mục & Đường dẫn mới", secondaryBrush);

        return new Border
        {
            Padding = new Thickness(16, 8, 16, 8),
            Background = TryGetThemeBrush("SubtleFillColorSecondaryBrush"),
            BorderBrush = TryGetThemeBrush("DividerStrokeColorDefaultBrush"),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = grid
        };
    }

    private static Border BuildFileRecordRow(int index, FileMoveRecord record)
    {
        var grid = CreateRecordColumnsGrid();

        var indexText = new TextBlock
        {
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = TryGetThemeBrush("TextFillColorTertiaryBrush"),
            Text = index.ToString()
        };
        Grid.SetColumn(indexText, 0);
        grid.Children.Add(indexText);

        // Cột 1: Tên tệp gốc + dung lượng (+ nhãn nếu đổi tên tránh trùng)
        var fileStack = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        var fileNameText = new TextBlock
        {
            FontSize = 12.5,
            FontWeight = FontWeights.SemiBold,
            Text = record.FileName,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        ToolTipService.SetToolTip(fileNameText, record.OriginalPath);
        fileStack.Children.Add(fileNameText);

        string subInfo = record.WasRenamed
            ? $"{record.FormattedSize}  •  Đổi tên tránh trùng → {record.DestinationFileName}"
            : record.FormattedSize;
        fileStack.Children.Add(new TextBlock
        {
            FontSize = 11.5,
            TextWrapping = TextWrapping.Wrap,
            Foreground = TryGetThemeBrush("TextFillColorSecondaryBrush"),
            Text = subInfo
        });

        Grid.SetColumn(fileStack, 1);
        grid.Children.Add(fileStack);

        // Cột 2: Quy tắc áp dụng
        var rulePill = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Padding = new Thickness(8, 3, 8, 3),
            CornerRadius = new CornerRadius(10),
            Background = TryGetThemeBrush("SubtleFillColorSecondaryBrush"),
            Child = new TextBlock
            {
                FontSize = 11.5,
                FontWeight = FontWeights.SemiBold,
                Text = string.IsNullOrWhiteSpace(record.MatchedRuleName) ? "Khác" : record.MatchedRuleName,
                TextTrimming = TextTrimming.CharacterEllipsis
            }
        };
        Grid.SetColumn(rulePill, 2);
        grid.Children.Add(rulePill);

        // Cột 3: Thư mục đích + đường dẫn mới chi tiết
        var destStack = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        var folderTargetText = new TextBlock
        {
            FontSize = 12.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = TryGetThemeBrush("AccentTextFillColorPrimaryBrush"),
            Text = $"📁 {record.RelativeDestinationFolder}",
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        destStack.Children.Add(folderTargetText);

        var pathDiffText = new TextBlock
        {
            FontSize = 11.5,
            Foreground = TryGetThemeBrush("TextFillColorSecondaryBrush"),
            Text = $"➔ {record.RelativeDestinationPath}",
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        ToolTipService.SetToolTip(destStack, $"Gốc: {record.OriginalPath}\nĐích: {record.DestinationPath}");
        destStack.Children.Add(pathDiffText);

        Grid.SetColumn(destStack, 3);
        grid.Children.Add(destStack);

        return new Border
        {
            Padding = new Thickness(8, 8, 8, 8),
            CornerRadius = new CornerRadius(6),
            BorderBrush = TryGetThemeBrush("DividerStrokeColorDefaultBrush"),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = grid
        };
    }

    private static Grid CreateRecordColumnsGrid()
    {
        var grid = new Grid { ColumnSpacing = 12 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(240) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        return grid;
    }

    private static void AddHeaderCell(Grid grid, int col, string text, Brush? foreground)
    {
        var tb = new TextBlock
        {
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = foreground,
            Text = text
        };
        Grid.SetColumn(tb, col);
        grid.Children.Add(tb);
    }

    private static Brush? TryGetThemeBrush(string key) =>
        Application.Current.Resources.TryGetValue(key, out object? val) ? val as Brush : null;
}
