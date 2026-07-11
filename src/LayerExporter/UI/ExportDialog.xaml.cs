using System.Windows;
using Autodesk.AutoCAD.EditorInput;
using LayerExporter.UI.ViewModels;
using Microsoft.Win32;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace LayerExporter.UI;

public partial class ExportDialog : Window
{
    public ExportDialog(ExportViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private ExportViewModel ViewModel => (ExportViewModel)DataContext;

    private void OnSelectAllChecked(object sender, RoutedEventArgs e)
    {
        ViewModel.SetAllSelected(true);
    }

    private void OnSelectAllUnchecked(object sender, RoutedEventArgs e)
    {
        ViewModel.SetAllSelected(false);
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
        var dialog = new OpenFolderDialog { Title = "출력 폴더 선택" };
        if (dialog.ShowDialog(this) == true)
        {
            ViewModel.OutputFolder = dialog.FolderName;
        }
    }

    private void OnExport(object sender, RoutedEventArgs e)
    {
        var error = ViewModel.Validate();
        if (error is not null)
        {
            MessageBox.Show(this, error, "레이어 내보내기", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        DialogResult = true;
    }
}
