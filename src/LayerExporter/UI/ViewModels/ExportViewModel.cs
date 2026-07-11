using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows.Data;
using LayerExporter.Services;

namespace LayerExporter.UI.ViewModels;

public enum ExportFormat
{
    Dxf,
    Shp,
}

public sealed class ExportViewModel : INotifyPropertyChanged
{
    private string _filterText = "";
    private ExportFormat _format = ExportFormat.Dxf;
    private string _outputFolder = "";
    private string _baseName = "export";
    private bool _closedPolylinesAsPolygons = true;
    private double _tolerance = 0.1;
    private bool _includeZ;
    private bool _usePreselectionOnly;

    public ExportViewModel(IEnumerable<LayerInfo> layers, int preselectionCount = 0)
    {
        Layers = new ObservableCollection<LayerItemViewModel>(layers.Select(l => new LayerItemViewModel(l)));
        LayersView = CollectionViewSource.GetDefaultView(Layers);
        LayersView.Filter = o => string.IsNullOrWhiteSpace(FilterText)
            || ((LayerItemViewModel)o).Name.Contains(FilterText, StringComparison.OrdinalIgnoreCase);
        PreselectionCount = preselectionCount;
        _usePreselectionOnly = preselectionCount > 0;
    }

    public int PreselectionCount { get; }

    public bool HasPreselection => PreselectionCount > 0;

    /// <summary>true면 명령 실행 전 미리 선택한 객체만 내보낸다 (레이어 목록 무시).</summary>
    public bool UsePreselectionOnly
    {
        get => _usePreselectionOnly;
        set
        {
            _usePreselectionOnly = value;
            OnChanged(nameof(UsePreselectionOnly));
            OnChanged(nameof(LayerSelectionEnabled));
        }
    }

    public bool LayerSelectionEnabled => !(HasPreselection && UsePreselectionOnly);

    public ObservableCollection<LayerItemViewModel> Layers { get; }

    public ICollectionView LayersView { get; }

    public string FilterText
    {
        get => _filterText;
        set { _filterText = value; OnChanged(nameof(FilterText)); LayersView.Refresh(); }
    }

    public ExportFormat Format
    {
        get => _format;
        set { _format = value; OnChanged(nameof(Format)); OnChanged(nameof(IsShp)); OnChanged(nameof(IsDxf)); }
    }

    public bool IsDxf
    {
        get => Format == ExportFormat.Dxf;
        set { if (value) { Format = ExportFormat.Dxf; } }
    }

    public bool IsShp
    {
        get => Format == ExportFormat.Shp;
        set { if (value) { Format = ExportFormat.Shp; } }
    }

    public string OutputFolder
    {
        get => _outputFolder;
        set { _outputFolder = value; OnChanged(nameof(OutputFolder)); }
    }

    public string BaseName
    {
        get => _baseName;
        set { _baseName = value; OnChanged(nameof(BaseName)); }
    }

    public bool ClosedPolylinesAsPolygons
    {
        get => _closedPolylinesAsPolygons;
        set { _closedPolylinesAsPolygons = value; OnChanged(nameof(ClosedPolylinesAsPolygons)); }
    }

    public double Tolerance
    {
        get => _tolerance;
        set { _tolerance = value; OnChanged(nameof(Tolerance)); }
    }

    public bool IncludeZ
    {
        get => _includeZ;
        set { _includeZ = value; OnChanged(nameof(IncludeZ)); }
    }

    public List<string> SelectedLayerNames =>
        Layers.Where(l => l.IsSelected).Select(l => l.Name).ToList();

    public void SetAllSelected(bool selected)
    {
        // 필터가 걸려 있으면 보이는 항목만 토글한다
        foreach (var item in LayersView.Cast<LayerItemViewModel>())
        {
            item.IsSelected = selected;
        }
    }

    /// <summary>화면 선택 등으로 얻은 레이어들을 목록에서 체크한다 (기존 체크는 유지).</summary>
    public void SelectLayers(IEnumerable<string> layerNames)
    {
        var names = new HashSet<string>(layerNames, StringComparer.OrdinalIgnoreCase);
        foreach (var item in Layers.Where(l => names.Contains(l.Name)))
        {
            item.IsSelected = true;
        }
    }

    /// <summary>유효성 검사. 문제가 없으면 null, 있으면 사용자 메시지를 반환한다.</summary>
    public string? Validate()
    {
        if (!(HasPreselection && UsePreselectionOnly) && SelectedLayerNames.Count == 0)
        {
            return "내보낼 레이어를 1개 이상 선택하세요.";
        }

        if (string.IsNullOrWhiteSpace(OutputFolder) || !Directory.Exists(OutputFolder))
        {
            return "유효한 출력 폴더를 지정하세요.";
        }

        if (string.IsNullOrWhiteSpace(BaseName)
            || BaseName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            return "유효한 파일 이름을 입력하세요.";
        }

        if (Tolerance <= 0)
        {
            return "곡선 분할 허용오차는 0보다 커야 합니다.";
        }

        return null;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnChanged(string name)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
