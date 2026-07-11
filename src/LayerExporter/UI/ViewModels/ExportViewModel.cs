using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows.Data;
using Autodesk.AutoCAD.DatabaseServices;
using LayerExporter.Services;

namespace LayerExporter.UI.ViewModels;

/// <summary>내보낼 대상을 고르는 방식. 객체 선택이 우선(기본값)이다.</summary>
public enum SelectionMode
{
    /// <summary>사용자가 고른 객체만 내보낸다.</summary>
    Objects,

    /// <summary>선택 레이어에 속한 모든 객체를 내보낸다.</summary>
    Layers,
}

public sealed class ExportViewModel : INotifyPropertyChanged
{
    private readonly List<ObjectId> _selectedObjectIds = new();
    private readonly HashSet<ObjectId> _selectedObjectSet = new();

    private string _filterText = "";
    private SelectionMode _mode = SelectionMode.Objects;
    private string _outputFolder = "";
    private string _baseName = "export";
    private bool _exportDxf = true;
    private bool _exportShp;
    private bool _closedPolylinesAsPolygons = true;
    private double _tolerance = 0.1;
    private bool _includeZ;

    public ExportViewModel(IEnumerable<LayerInfo> layers, IEnumerable<ObjectId>? preselectedObjects = null)
    {
        Layers = new ObservableCollection<LayerItemViewModel>(layers.Select(l => new LayerItemViewModel(l)));
        LayersView = CollectionViewSource.GetDefaultView(Layers);
        LayersView.Filter = o => string.IsNullOrWhiteSpace(FilterText)
            || ((LayerItemViewModel)o).Name.IndexOf(FilterText, StringComparison.OrdinalIgnoreCase) >= 0;

        if (preselectedObjects is not null)
        {
            AddSelectedObjects(preselectedObjects);
        }
    }

    /// <summary>대상 선택 방식. 객체 선택이 기본이자 우선.</summary>
    public SelectionMode Mode
    {
        get => _mode;
        set
        {
            _mode = value;
            OnChanged(nameof(Mode));
            OnChanged(nameof(IsObjectMode));
            OnChanged(nameof(IsLayerMode));
            OnChanged(nameof(LayerSelectionEnabled));
        }
    }

    public bool IsObjectMode
    {
        get => Mode == SelectionMode.Objects;
        set { if (value) { Mode = SelectionMode.Objects; } }
    }

    public bool IsLayerMode
    {
        get => Mode == SelectionMode.Layers;
        set { if (value) { Mode = SelectionMode.Layers; } }
    }

    /// <summary>레이어 목록 UI는 레이어 모드일 때만 활성화한다.</summary>
    public bool LayerSelectionEnabled => IsLayerMode;

    /// <summary>객체 모드에서 내보낼 대상 ObjectId (실행 전 pickfirst + 화면 선택 누적).</summary>
    public IReadOnlyList<ObjectId> SelectedObjectIds => _selectedObjectIds;

    public int SelectedObjectCount => _selectedObjectIds.Count;

    /// <summary>화면에서 고른 객체를 기존 선택에 누적한다 (중복 제거).</summary>
    public void AddSelectedObjects(IEnumerable<ObjectId> ids)
    {
        foreach (var id in ids)
        {
            if (_selectedObjectSet.Add(id))
            {
                _selectedObjectIds.Add(id);
            }
        }

        OnChanged(nameof(SelectedObjectCount));
    }

    public ObservableCollection<LayerItemViewModel> Layers { get; }

    public ICollectionView LayersView { get; }

    public string FilterText
    {
        get => _filterText;
        set { _filterText = value; OnChanged(nameof(FilterText)); LayersView.Refresh(); }
    }

    /// <summary>DXF로 내보낼지 여부. SHP와 동시 선택 가능.</summary>
    public bool ExportDxf
    {
        get => _exportDxf;
        set { _exportDxf = value; OnChanged(nameof(ExportDxf)); }
    }

    /// <summary>SHP(Shapefile)로 내보낼지 여부. DXF와 동시 선택 가능.</summary>
    public bool ExportShp
    {
        get => _exportShp;
        set { _exportShp = value; OnChanged(nameof(ExportShp)); }
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
        if (!ExportDxf && !ExportShp)
        {
            return "출력 형식(DXF/SHP)을 1개 이상 선택하세요.";
        }

        if (IsObjectMode)
        {
            if (SelectedObjectCount == 0)
            {
                return "내보낼 객체를 1개 이상 선택하세요.";
            }
        }
        else if (SelectedLayerNames.Count == 0)
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

        if (ExportShp && Tolerance <= 0)
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
