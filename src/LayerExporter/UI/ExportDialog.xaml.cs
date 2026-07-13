using System.Windows;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using LayerExporter.UI.ViewModels;
using Microsoft.Win32;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace LayerExporter.UI;

public partial class ExportDialog : Window
{
    private static readonly Dictionary<Document, ExportDialog> OpenDialogs = new();
    private static bool _documentEventsSubscribed;

    private readonly Document _doc;
    private bool _isPicking;
    private bool _isExporting;
    private bool _isClosed;
    private bool _isDocumentClosing;

    private ExportDialog(Document doc, ExportViewModel viewModel)
    {
        InitializeComponent();
        _doc = doc;
        DataContext = viewModel;

        // 명령 실행 전 미리 선택해 둔 객체를 다이얼로그가 열릴 때 도면에서 하이라이트로 유지하고,
        // 닫힐 때 하이라이트를 해제한다. 이렇게 하면 "화면에서 객체 선택"으로 추가할 때도
        // 기존 선택이 화면상에서 그대로 보인다.
        Loaded += (_, _) => HighlightSelected(true);
        Closed += OnClosed;
    }

    /// <summary>
    /// 플러그인 창을 모델리스로 띄운다. 모델리스라 창을 열어둔 채 도면을 확대/축소/이동하고
    /// 위성사진을 켜고 끌 수 있다. 이미 열려 있으면 앞으로 가져온다.
    /// </summary>
    public static void ShowModeless(Document doc, ExportViewModel viewModel)
    {
        if (OpenDialogs.TryGetValue(doc, out var current))
        {
            current.Activate();
            return;
        }

        var dialog = new ExportDialog(doc, viewModel);
        OpenDialogs.Add(doc, dialog);
        SubscribeDocumentEvents();
        AcApp.ShowModelessWindow(dialog);
    }

    private static void SubscribeDocumentEvents()
    {
        if (_documentEventsSubscribed)
        {
            return;
        }

        AcApp.DocumentManager.DocumentToBeDestroyed += OnDocumentToBeDestroyed;
        _documentEventsSubscribed = true;
    }

    private static void UnsubscribeDocumentEventsIfUnused()
    {
        if (!_documentEventsSubscribed || OpenDialogs.Count != 0)
        {
            return;
        }

        AcApp.DocumentManager.DocumentToBeDestroyed -= OnDocumentToBeDestroyed;
        _documentEventsSubscribed = false;
    }

    private static void OnDocumentToBeDestroyed(object sender, DocumentCollectionEventArgs e)
    {
        if (OpenDialogs.TryGetValue(e.Document, out var dialog))
        {
            dialog._isDocumentClosing = true;
            if (!dialog.Dispatcher.HasShutdownStarted)
            {
                dialog.Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (!dialog._isClosed)
                    {
                        dialog.Close();
                    }
                }));
            }
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _isClosed = true;
        if (!_isDocumentClosing)
        {
            HighlightSelected(false);
        }
        OpenDialogs.Remove(_doc);
        UnsubscribeDocumentEventsIfUnused();
    }

    private ExportViewModel ViewModel => (ExportViewModel)DataContext;

    /// <summary>현재 객체 선택분을 도면에서 하이라이트하거나 해제한다. 부가 기능이므로 실패해도 무시한다.</summary>
    private void HighlightSelected(bool on)
    {
        if (ViewModel.SelectedObjectIds.Count == 0)
        {
            return;
        }

        var doc = _doc;

        try
        {
            using (doc.LockDocument())
            using (var tr = doc.Database.TransactionManager.StartTransaction())
            {
                foreach (var id in ViewModel.SelectedObjectIds)
                {
                    if (id.IsValid && !id.IsErased && id.Database == doc.Database
                        && tr.GetObject(id, OpenMode.ForRead) is Entity ent)
                    {
                        if (on)
                        {
                            ent.Highlight();
                        }
                        else
                        {
                            ent.Unhighlight();
                        }
                    }
                }

                tr.Commit();
            }
        }
        catch
        {
            // 하이라이트는 부가 기능 — 실패해도 내보내기에는 영향 없음
        }
    }

    private void OnSelectAllChecked(object sender, RoutedEventArgs e)
    {
        ViewModel.SetAllSelected(true);
    }

    private void OnSelectAllUnchecked(object sender, RoutedEventArgs e)
    {
        ViewModel.SetAllSelected(false);
    }

    /// <summary>
    /// 도면 좌표계를 지정한다. 모델리스 창이라 명령이 자체 GUI(대화상자)로 정상 표시되며 창은 유지된다.
    /// Civil 3D/Map이면 MAPCSASSIGN, 일반 AutoCAD면 GEOGRAPHICLOCATION으로 자동 전환한다.
    /// </summary>
    private void OnAssignCoordinateSystem(object sender, RoutedEventArgs e)
    {
        SendCommand(Crs.CoordinateSystemResolver.IsCivil3DAvailable()
            ? "_.MAPCSASSIGN "
            : "_.GEOGRAPHICLOCATION ");
    }

    /// <summary>위성사진(GEOMAP 하이브리드)을 켠다. 창을 열어둔 채 도면을 확대/축소/이동해 확인한다.</summary>
    private void OnGeoMapOn(object sender, RoutedEventArgs e) => SendCommand("_.GEOMAP _Hybrid ");

    /// <summary>위성사진(GEOMAP)을 끈다.</summary>
    private void OnGeoMapOff(object sender, RoutedEventArgs e) => SendCommand("_.GEOMAP _Off ");

    /// <summary>활성 도면 커맨드라인에 명령 매크로를 보낸다. 모델리스라 창은 그대로 유지된다.</summary>
    private void SendCommand(string macro)
    {
        try
        {
            _doc.SendStringToExecute(macro, true, false, true);
        }
        catch (System.Exception ex)
        {
            _doc.Editor.WriteMessage($"\n[LayerExporter] 명령 실행 실패: {ex.Message}\n");
        }
    }

    private async void OnPickObjectsFromScreen(object sender, RoutedEventArgs e)
    {
        var ids = await SelectFromScreenAsync("\n내보낼 객체 선택: ");
        if (ids is null || _isClosed || _isDocumentClosing)
        {
            return;
        }

        ViewModel.AddSelectedObjects(ids);
        HighlightSelected(true);
        _doc.Editor.WriteMessage(
            $"\n[LayerExporter] 현재 {ViewModel.SelectedObjectCount}개 객체가 선택되었습니다.\n");
    }

    private async void OnPickLayersFromScreen(object sender, RoutedEventArgs e)
    {
        var ids = await SelectFromScreenAsync("\n내보낼 레이어의 객체 선택: ");
        if (ids is null || _isClosed || _isDocumentClosing)
        {
            return;
        }

        HashSet<string> layerNames;
        using (_doc.LockDocument())
        {
            layerNames = Services.LayerService.GetLayerNames(_doc.Database, ids);
        }
        ViewModel.SelectLayers(layerNames);
        _doc.Editor.WriteMessage(
            $"\n[LayerExporter] {layerNames.Count}개 레이어가 체크되었습니다: {string.Join(", ", layerNames)}\n");
    }

    private async Task<ObjectId[]?> SelectFromScreenAsync(string message)
    {
        if (_isPicking || _isClosed || _isDocumentClosing)
        {
            return null;
        }

        _isPicking = true;
        ObjectId[]? selectedIds = null;
        try
        {
            AcApp.DocumentManager.MdiActiveDocument = _doc;
            IsEnabled = false;
            Hide();

            await AcApp.DocumentManager.ExecuteInCommandContextAsync(_ =>
            {
                if (_isClosed || _isDocumentClosing)
                {
                    return Task.CompletedTask;
                }

                var options = new PromptSelectionOptions { MessageForAdding = message };
                var result = _doc.Editor.GetSelection(options);
                if (result.Status == PromptStatus.OK)
                {
                    selectedIds = result.Value.GetObjectIds();
                }

                return Task.CompletedTask;
            }, null);

            return selectedIds?
                .Where(id => id.IsValid && !id.IsErased && id.Database == _doc.Database)
                .ToArray();
        }
        catch (System.Exception ex)
        {
            if (!_isDocumentClosing)
            {
                try
                {
                    _doc.Editor.WriteMessage($"\n[LayerExporter] 객체 선택 실패: {ex.Message}\n");
                }
                catch
                {
                    // The document may have closed before its destruction event reached the UI.
                }
            }

            return null;
        }
        finally
        {
            _isPicking = false;
            if (!_isClosed && !_isDocumentClosing)
            {
                IsEnabled = true;
                Show();
                Activate();
            }
        }
    }

    private void OnBrowseFolder(object sender, RoutedEventArgs e)
    {
#if NETFRAMEWORK
        // WPF OpenFolderDialog는 .NET 8+ 전용이므로 net48(2018–2024)에서는 WinForms 대화상자를 사용한다
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "출력 폴더 선택",
            SelectedPath = ViewModel.OutputFolder,
        };
        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            ViewModel.OutputFolder = dialog.SelectedPath;
        }
#else
        var dialog = new OpenFolderDialog { Title = "출력 폴더 선택" };
        if (dialog.ShowDialog(this) == true)
        {
            ViewModel.OutputFolder = dialog.FolderName;
        }
#endif
    }

    private async void OnExport(object sender, RoutedEventArgs e)
    {
        if (_isExporting || _isClosed || _isDocumentClosing)
        {
            return;
        }

        var error = ViewModel.Validate();
        if (error is not null)
        {
            MessageBox.Show(this, error, "레이어 내보내기", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _isExporting = true;
        IsEnabled = false;
        try
        {
            // 모델리스 창은 DialogResult를 설정할 수 없으므로 직접 내보낸 뒤 창을 닫는다.
            if (await LayerExporter.Commands.ExportLayersCommand.PerformExportAsync(_doc, ViewModel)
                && !_isClosed)
            {
                Close();
            }
        }
        catch (System.Exception ex)
        {
            if (!_isClosed && !_isDocumentClosing)
            {
                try
                {
                    _doc.Editor.WriteMessage($"\n[LayerExporter] 내보내기 실패: {ex.Message}\n");
                }
                catch
                {
                    // The document may have closed while the detached SHP data was written.
                }
            }
        }
        finally
        {
            _isExporting = false;
            if (!_isClosed && !_isDocumentClosing)
            {
                IsEnabled = true;
            }
        }
    }
    private void OnCancel(object sender, RoutedEventArgs e) => Close();
}
