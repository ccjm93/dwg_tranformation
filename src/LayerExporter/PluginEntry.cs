using Autodesk.AutoCAD.ApplicationServices.Core;
using Autodesk.AutoCAD.Runtime;
using LayerExporter.Infrastructure;
using LayerExporter.UI;

[assembly: ExtensionApplication(typeof(LayerExporter.PluginEntry))]
[assembly: CommandClass(typeof(LayerExporter.Commands.ExportLayersCommand))]

namespace LayerExporter;

public sealed class PluginEntry : IExtensionApplication
{
    public void Initialize()
    {
        AssemblyResolver.Register();
        RibbonBuilder.Install();
        var ed = Application.DocumentManager.MdiActiveDocument?.Editor;
        ed?.WriteMessage("\n[LayerExporter] 로드 완료. Add-ins 탭 버튼 또는 EXPORTLAYERS 명령으로 실행하세요.\n");
    }

    public void Terminate()
    {
    }
}
