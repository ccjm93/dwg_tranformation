using System.Windows;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using LayerExporter.UI.ViewModels;
using Microsoft.Win32;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace LayerExporter.UI;

public partial class ExportDialog : Window
{
    private static ExportDialog? _current;
    private readonly Autodesk.AutoCAD.ApplicationServices.Document _doc;

    private ExportDialog(Autodesk.AutoCAD.ApplicationServices.Document doc, ExportViewModel viewModel)
    {
        InitializeComponent();
        _doc = doc;
        DataContext = viewModel;

        // 명령 실행 전 미리 선택해 둔 객체를 다이얼로그가 열릴 때 도면에서 하이라이트로 유지하고,
        // 닫힐 때 하이라이트를 해제한다. 이렇게 하면 "화면에서 객체 선택"으로 추가할 때도
        // 기존 선택이 화면상에서 그대로 보인다.
        Loaded += (_, _) => HighlightSelected(true);
        Closed += (_, _) => HighlightSelected(false);
    }

    /// <summary>
    /// 플러그인 창을 모델리스로 띄운다. 모델리스라 창을 열어둔 채 도면을 확대/축소/이동하고
    /// 위성사진을 켜고 끌 수 있다. 이미 열려 있으면 앞으로 가져온다.
    /// </summary>
    public static void ShowModeless(Autodesk.AutoCAD.ApplicationServices.Document doc, ExportViewModel viewModel)
    {
        if (_current is not null)
        {
            _current.Activate();
            return;
        }

        var dialog = new ExportDialog(doc, viewModel);
        _current = dialog;
        dialog.Closed += (_, _) => _current = null;
        AcApp.ShowModelessWindow(dialog);
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
                    if (id.IsValid && !id.IsErased
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

    private void OnPickObjectsFromScreen(object sender, RoutedEventArgs e)
    {
        var doc = AcApp.DocumentManager.MdiActiveDocument;
        if (doc is null)
        {
            return;
        }

        var ed = doc.Editor;
        // 다이얼로그를 잠시 숨기고 도면에서 객체를 선택하게 한다
        var interaction = ed.StartUserInteraction(this);
        try
        {
            var options = new PromptSelectionOptions
            {
                MessageForAdding = "\n내보낼 객체 선택 (기존 선택에 추가됨)",
            };
            var result = ed.GetSelection(options);
            if (result.Status != PromptStatus.OK)
            {
                return;
            }

            ViewModel.AddSelectedObjects(result.Value.GetObjectIds());
            // 기존 선택 + 새로 고른 객체 전체를 다시 하이라이트해 화면상 선택 상태를 유지한다.
            HighlightSelected(true);
            ed.WriteMessage($"\n[LayerExporter] 현재 {ViewModel.SelectedObjectCount}개 객체가 선택되었습니다.\n");
        }
        finally
        {
            interaction.End();
        }
    }

    private void OnPickLayersFromScreen(object sender, RoutedEventArgs e)
    {
        var doc = AcApp.DocumentManager.MdiActiveDocument;
        if (doc is null)
        {
            return;
        }

        var ed = doc.Editor;
        // 다이얼로그를 잠시 숨기고 도면에서 객체를 선택하게 한다
        var interaction = ed.StartUserInteraction(this);
        try
        {
            var options = new PromptSelectionOptions
            {
                MessageForAdding = "\n내보낼 레이어의 객체 선택",
            };
            var result = ed.GetSelection(options);
            if (result.Status != PromptStatus.OK)
            {
                return;
            }

            var layerNames = Services.LayerService.GetLayerNames(doc.Database, result.Value.GetObjectIds());
            ViewModel.SelectLayers(layerNames);
            ed.WriteMessage($"\n[LayerExporter] {layerNames.Count}개 레이어가 체크되었습니다: {string.Join(", ", layerNames)}\n");
        }
        finally
        {
            interaction.End();
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

    private void OnExport(object sender, RoutedEventArgs e)
    {
        var error = ViewModel.Validate();
        if (error is not null)
        {
            MessageBox.Show(this, error, "레이어 내보내기", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // 모델리스 창은 DialogResult을 설정할 수 없으므로 직접 내보낸 뒤 창을 닫는다.
        if (LayerExporter.Commands.ExportLayersCommand.PerformExport(_doc, ViewModel))
        {
            Close();
        }
    }

    private void OnCancel(object sender, RoutedEventArgs e) => Close();
}
