using CommunityToolkit.Mvvm.ComponentModel;
using Sofol.Core.Models;

namespace Sofol.App.ViewModels;

/// <summary>
/// Wrapper MVVM cho một dòng Preview để CheckBox và trạng thái cập nhật mượt mà trên WinUI 3 ListView.
/// </summary>
public sealed partial class PlanItemViewModel : ObservableObject
{
    private readonly Action? _onSelectionChanged;

    public FileMovePlanItem Model { get; }

    public PlanItemViewModel(FileMovePlanItem model, Action? onSelectionChanged = null)
    {
        Model = model;
        _onSelectionChanged = onSelectionChanged;
        _isSelected = model.IsSelected;
        _status = model.Status;
        _statusText = model.StatusText;
    }

    public string FileName => Model.FileName;

    public string ExtensionDisplay => string.IsNullOrEmpty(Model.Extension) ? "FILE" : Model.Extension.ToUpperInvariant();

    public string SourcePath => Model.SourcePath;

    public string ProposedTargetFolder => Model.ProposedTargetFolder;

    public string FormattedSize => Model.FormattedSize;

    public string LastModifiedDisplay => Model.LastModifiedDisplay;

    public string MatchedRuleName => Model.MatchedRuleName;

    public string IconGlyph => Model.IconGlyph;

    public RuleBadgeType RuleBadgeType => Model.RuleBadgeType;

    public bool CanSelect => Model.CanSelect;

    public double RowOpacity => CanSelect ? 1.0 : 0.55;

    [ObservableProperty]
    private bool _isSelected;

    partial void OnIsSelectedChanged(bool value)
    {
        Model.IsSelected = value;
        _onSelectionChanged?.Invoke();
    }

    [ObservableProperty]
    private PlanItemStatus _status;

    [ObservableProperty]
    private string _statusText;

    public void SyncFromModel()
    {
        Status = Model.Status;
        StatusText = Model.StatusText;
        IsSelected = Model.IsSelected;
        OnPropertyChanged(nameof(CanSelect));
        OnPropertyChanged(nameof(RowOpacity));
    }
}
