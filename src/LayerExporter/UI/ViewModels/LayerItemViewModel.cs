using System.ComponentModel;
using LayerExporter.Services;

namespace LayerExporter.UI.ViewModels;

public sealed class LayerItemViewModel : INotifyPropertyChanged
{
    private bool _isSelected;

    public LayerItemViewModel(LayerInfo info)
    {
        Info = info;
    }

    public LayerInfo Info { get; }

    public string Name => Info.Name;

    public string Description =>
        $"{Info.EntityCount}개 객체" +
        (Info.IsFrozen ? " · 동결" : "") +
        (Info.IsLocked ? " · 잠금" : "");

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected != value)
            {
                _isSelected = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
