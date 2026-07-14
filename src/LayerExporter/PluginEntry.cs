using Autodesk.AutoCAD.ApplicationServices.Core;
using Autodesk.AutoCAD.Runtime;
using LayerExporter.Commands;
using LayerExporter.Infrastructure;
using LayerExporter.UI;

[assembly: ExtensionApplication(typeof(LayerExporter.PluginEntry))]
[assembly: CommandClass(typeof(LayerExporter.Commands.ExportLayersCommand))]
[assembly: CommandClass(typeof(CoordinateSystemLibraryCommands))]

namespace LayerExporter;

public sealed class PluginEntry : IExtensionApplication
{
    public void Initialize()
    {
        AssemblyResolver.Register();
        RibbonBuilder.Install();
        CoordinateSystemLibraryInstaller.InstallOnFirstDocument();
        var ed = Application.DocumentManager.MdiActiveDocument?.Editor;
        ed?.WriteMessage("\n[LayerExporter] 로드 완료. 'DH 플러그인' 탭 버튼 또는 DwgToSHP 명령으로 실행하세요.\n");
    }

    public void Terminate()
    {
    }
}
