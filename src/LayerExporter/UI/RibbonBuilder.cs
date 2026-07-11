using Autodesk.AutoCAD.ApplicationServices.Core;
using Autodesk.Windows;

namespace LayerExporter.UI;

/// <summary>
/// AutoCAD 기본 "Add-ins" 리본 탭에 EXPORTLAYERS 실행 버튼을 추가한다.
/// 리본은 플러그인 로드 시점에 아직 준비되지 않았을 수 있으므로 Idle 이벤트로 지연 설치한다.
/// 명령(EXPORTLAYERS)은 그대로 유지되며, 버튼은 그 명령을 호출한다.
/// </summary>
public static class RibbonBuilder
{
    private const string PanelSourceId = "LAYEREXPORTER_PANEL";

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

            var tab = FindOrCreateAddinsTab(ribbon);

            var source = new RibbonPanelSource { Title = "레이어 내보내기", Id = PanelSourceId };
            var button = new RibbonButton
            {
                Text = "레이어 내보내기",
                ShowText = true,
                ShowImage = true,
                Size = RibbonItemSize.Large,
                Orientation = System.Windows.Controls.Orientation.Vertical,
                ToolTip = "선택한 객체 또는 레이어를 DXF/SHP로 내보냅니다 (EXPORTLAYERS)",
                // 끝의 공백이 Enter 역할을 해 명령을 즉시 실행한다
                CommandParameter = "EXPORTLAYERS ",
                CommandHandler = new ExportCommandHandler(),
            };

            source.Items.Add(button);
            tab.Panels.Add(new RibbonPanel { Source = source });
        }
        catch
        {
            // 리본 구성 실패는 치명적이지 않다 — 명령(EXPORTLAYERS)으로 계속 사용 가능
        }
    }

    private static RibbonTab FindOrCreateAddinsTab(RibbonControl ribbon)
    {
        // 기본 "Add-ins" 탭 탐색 (표준 Id 또는 현지화된 제목)
        foreach (var t in ribbon.Tabs)
        {
            if (string.Equals(t.Id, "ACAD.Addins", StringComparison.OrdinalIgnoreCase)
                || (t.Title?.IndexOf("Add", StringComparison.OrdinalIgnoreCase) >= 0)
                || (t.Title?.IndexOf("애드인", StringComparison.Ordinal) >= 0))
            {
                return t;
            }
        }

        var tab = new RibbonTab { Title = "Add-ins", Id = "ACAD.Addins" };
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
