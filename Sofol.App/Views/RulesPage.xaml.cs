using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Sofol.App.ViewModels;
using Sofol.Core.Models;

namespace Sofol.App.Views;

public sealed partial class RulesPage : Page
{
    public MainViewModel ViewModel => App.ViewModel;

    public RulesPage()
    {
        InitializeComponent();
    }

    private void OnRestoreDefaultsClick(object sender, RoutedEventArgs e)
    {
        ViewModel.RestoreDefaultRules();
    }

    private void OnOpenSmartGroupingGuideClick(object sender, RoutedEventArgs e) =>
        GuideWindow.ShowGuide("smart_folder_modes");

    private void OnEnableAllRulesClick(object sender, RoutedEventArgs e)
    {
        ViewModel.SetAllRulesEnabled(true);
    }

    private void OnDisableAllRulesClick(object sender, RoutedEventArgs e)
    {
        ViewModel.SetAllRulesEnabled(false);
    }

    private async void OnAddRuleClick(object sender, RoutedEventArgs e)
    {
        var newRule = new OrganizationRule
        {
            Name = "Quy tắc mới",
            TargetFolderName = "Thư mục mới",
            IconGlyph = "\uE8B7",
            Priority = 15,
            IsEnabled = true
        };

        OrganizationRule? edited = await ShowRuleEditorDialogAsync(newRule, isNew: true);
        if (edited is not null)
        {
            ViewModel.SaveRuleChanges(edited, isNew: true);
        }
    }

    private async void OnEditRuleClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is OrganizationRule rule)
        {
            OrganizationRule? edited = await ShowRuleEditorDialogAsync(rule, isNew: false);
            if (edited is not null)
            {
                ViewModel.SaveRuleChanges(edited, isNew: false);
            }
        }
    }

    private void OnDeleteRuleClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is OrganizationRule rule)
        {
            ViewModel.DeleteRule(rule);
        }
    }

    private void OnRuleToggled(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleSwitch toggle && toggle.Tag is OrganizationRule rule)
        {
            if (rule.IsEnabled != toggle.IsOn)
            {
                ViewModel.ToggleRuleEnabled(rule, toggle.IsOn);
            }
        }
    }

    private async Task<OrganizationRule?> ShowRuleEditorDialogAsync(OrganizationRule source, bool isNew)
    {
        var nameBox = new TextBox
        {
            Header = "Tên quy tắc",
            Text = source.Name,
            PlaceholderText = "Ví dụ: Hóa đơn & Kế toán"
        };

        var folderBox = new TextBox
        {
            Header = "Thư mục đích (tạo bên trong thư mục dọn dẹp)",
            Text = source.TargetFolderName,
            PlaceholderText = "Ví dụ: Kế toán hoặc Tài liệu\\Hóa đơn"
        };

        var extBox = new TextBox
        {
            Header = "Các đuôi tệp (cách nhau bởi dấu phẩy)",
            Text = string.Join(", ", source.Extensions),
            PlaceholderText = ".jpg, .png, .webp"
        };

        var keywordBox = new TextBox
        {
            Header = new TextBlock
            {
                Text = "Từ khóa trong tên tệp (nếu nhập cùng đuôi tệp, tệp phải khớp CẢ đuôi tệp VÀ từ khóa)",
                TextWrapping = TextWrapping.Wrap
            },
            Text = string.Join(", ", source.Keywords),
            PlaceholderText = "hóa đơn, invoice, báo cáo"
        };

        var priorityBox = new NumberBox
        {
            Header = "Độ ưu tiên (số nhỏ hơn được kiểm tra trước)",
            Value = source.Priority,
            Minimum = 1,
            Maximum = 999,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline
        };

        var iconChoices = new List<(string Label, string Glyph)>
        {
            ("Thư mục", "\uE8B7"),
            ("Hình ảnh", "\uE91B"),
            ("Tài liệu", "\uE8A5"),
            ("Bảng tính", "\uE9F9"),
            ("Ứng dụng", "\uE74C"),
            ("Tệp nén", "\uF012"),
            ("Video", "\uE714"),
            ("Âm thanh", "\uE8D6"),
            ("Mã nguồn", "\uE943")
        };

        var iconCombo = new ComboBox
        {
            Header = "Biểu tượng",
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        int selectedIconIdx = 0;
        for (int i = 0; i < iconChoices.Count; i++)
        {
            var itemRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 10
            };
            itemRow.Children.Add(new FontIcon
            {
                Glyph = iconChoices[i].Glyph,
                FontSize = 16
            });
            itemRow.Children.Add(new TextBlock
            {
                Text = iconChoices[i].Label,
                VerticalAlignment = VerticalAlignment.Center
            });

            iconCombo.Items.Add(new ComboBoxItem
            {
                Content = itemRow,
                Tag = iconChoices[i].Glyph
            });

            if (iconChoices[i].Glyph == source.IconGlyph)
            {
                selectedIconIdx = i;
            }
        }
        iconCombo.SelectedIndex = selectedIconIdx;

        var panel = new StackPanel { Spacing = 12, MinWidth = 420 };
        panel.Children.Add(nameBox);
        panel.Children.Add(folderBox);
        panel.Children.Add(extBox);
        panel.Children.Add(keywordBox);
        panel.Children.Add(priorityBox);
        panel.Children.Add(iconCombo);

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = isNew ? "Thêm quy tắc mới" : $"Chỉnh sửa: {source.Name}",
            PrimaryButtonText = "Lưu quy tắc",
            CloseButtonText = "Hủy",
            DefaultButton = ContentDialogButton.Primary,
            Content = panel
        };

        ContentDialogResult res = await dialog.ShowAsync();
        if (res != ContentDialogResult.Primary)
        {
            return null;
        }

        string chosenGlyph = iconCombo.SelectedIndex >= 0
            ? iconChoices[iconCombo.SelectedIndex].Glyph
            : source.IconGlyph;

        return new OrganizationRule
        {
            Id = source.Id,
            Name = string.IsNullOrWhiteSpace(nameBox.Text) ? "Quy tắc tùy chỉnh" : nameBox.Text.Trim(),
            TargetFolderName = string.IsNullOrWhiteSpace(folderBox.Text) ? "Khác" : folderBox.Text.Trim(),
            Extensions = OrganizationRule.ParseCommaSeparatedExtensions(extBox.Text),
            Keywords = OrganizationRule.ParseCommaSeparatedKeywords(keywordBox.Text),
            Priority = double.IsNaN(priorityBox.Value) ? 100 : (int)priorityBox.Value,
            IconGlyph = chosenGlyph,
            IsEnabled = source.IsEnabled
        };
    }
}
