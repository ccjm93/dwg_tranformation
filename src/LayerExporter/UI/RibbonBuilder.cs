using System.Reflection;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Autodesk.AutoCAD.ApplicationServices.Core;
using Autodesk.Windows;

namespace LayerExporter.UI;

/// <summary>
/// 전용 "DH 플러그인" 리본 탭을 신설하고 그 안에 DwgToSHP 실행 버튼을 추가한다.
/// 리본은 플러그인 로드 시점에 아직 준비되지 않았을 수 있으므로 Idle 이벤트로 지연 설치한다.
/// 명령(DwgToSHP)은 그대로 유지되며, 버튼은 그 명령을 호출한다.
/// </summary>
public static class RibbonBuilder
{
    private const string PanelSourceId = "LAYEREXPORTER_PANEL";
    private const string TabId = "DH_PLUGIN_TAB";
    private const string TabTitle = "DH 플러그인";

    public static void Install()
    {
        // 이미 리본이 준비되어 있으면 즉시 추가, 아니면 Idle에서 재시도
        if (ComponentManager.Ribbon is not null)
        {
            TryAddButton();
        }
        else
        {
            Application.Idle += OnIdle;
        }
    }

    private static void OnIdle(object? sender, EventArgs e)
    {
        if (ComponentManager.Ribbon is null)
        {
            return; // 아직 준비 안 됨 — 다음 Idle에서 재시도
        }

        Application.Idle -= OnIdle;
        TryAddButton();
    }

    private static void TryAddButton()
    {
        try
        {
            var ribbon = ComponentManager.Ribbon;
            if (ribbon is null)
            {
                return;
            }

            // 재로드 시 중복 추가 방지
            foreach (var t in ribbon.Tabs)
            {
                foreach (var p in t.Panels)
                {
                    if (p.Source?.Id == PanelSourceId)
                    {
                        return;
                    }
                }
            }

            var tab = FindOrCreateDhTab(ribbon);

            var source = new RibbonPanelSource { Title = "레이어 내보내기", Id = PanelSourceId };
            var button = new RibbonButton
            {
                Text = "레이어 내보내기",
                ShowText = true,
                ShowImage = true,
                Size = RibbonItemSize.Large,
                Orientation = System.Windows.Controls.Orientation.Vertical,
                ToolTip = "선택한 객체 또는 레이어를 SHP/DXF로 내보냅니다 (DwgToSHP)",
                // 끝의 공백이 Enter 역할을 해 명령을 즉시 실행한다
                CommandParameter = "DwgToSHP ",
                CommandHandler = new ExportCommandHandler(),
                LargeImage = LoadImage("LayerExporter.Assets.icon32.png"),
                Image = LoadImage("LayerExporter.Assets.icon16.png"),
            };

            source.Items.Add(button);
            tab.Panels.Add(new RibbonPanel { Source = source });
        }
        catch
        {
            // 리본 구성 실패는 치명적이지 않다 — 명령(DwgToSHP)으로 계속 사용 가능
        }
    }

    /// <summary>어셈블리에 내장된 PNG 아이콘을 ImageSource로 로드한다. 실패 시 null(아이콘 없이 표시).</summary>
    private static ImageSource? LoadImage(string resourceName)
    {
        try
        {
            var asm = Assembly.GetExecutingAssembly();
            using var stream = asm.GetManifestResourceStream(resourceName);
            if (stream is null)
            {
                return null;
            }

            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    private static RibbonTab FindOrCreateDhTab(RibbonControl ribbon)
    {
        // 이미 신설된 "DH 플러그인" 탭이 있으면 재사용 (재로드 대비)
        foreach (var t in ribbon.Tabs)
        {
            if (string.Equals(t.Id, TabId, StringComparison.OrdinalIgnoreCase)
                || string.Equals(t.Title, TabTitle, StringComparison.Ordinal))
            {
                return t;
            }
        }

        var tab = new RibbonTab { Title = TabTitle, Id = TabId };
        ribbon.Tabs.Add(tab);
        return tab;
    }

    /// <summary>리본 버튼 클릭 시 CommandParameter의 명령 문자열을 활성 문서에 전송한다.</summary>
    private sealed class ExportCommandHandler : System.Windows.Input.ICommand
    {
        // ICommand 규약상 필요하지만 CanExecute가 항상 true라 발생시키지 않는다
#pragma warning disable CS0067
        public event EventHandler? CanExecuteChanged;
#pragma warning restore CS0067

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter)
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc is null)
            {
                return;
            }

            var macro = (parameter as RibbonButton)?.CommandParameter as string;
            if (!string.IsNullOrEmpty(macro))
            {
                doc.SendStringToExecute(macro, true, false, true);
            }
        }
    }
}
