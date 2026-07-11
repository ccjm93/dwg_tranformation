using System.IO;
using System.Reflection;
using System.Runtime.Loader;

namespace LayerExporter.Infrastructure;

/// <summary>
/// AutoCAD 프로세스에서 NuGet 의존성(NetTopologySuite 등)을 찾지 못할 때
/// 플러그인 DLL과 같은 폴더에서 우선 로딩하는 핸들러.
/// </summary>
public static class AssemblyResolver
{
    private static bool _registered;

    public static void Register()
    {
        if (_registered)
        {
            return;
        }

        _registered = true;
        AssemblyLoadContext.Default.Resolving += OnResolving;
    }

    private static Assembly? OnResolving(AssemblyLoadContext context, AssemblyName name)
    {
        try
        {
            var pluginDir = Path.GetDirectoryName(typeof(AssemblyResolver).Assembly.Location);
            if (pluginDir is null || name.Name is null)
            {
                return null;
            }

            var candidate = Path.Combine(pluginDir, name.Name + ".dll");
            return File.Exists(candidate) ? context.LoadFromAssemblyPath(candidate) : null;
        }
        catch
        {
            return null;
        }
    }
}
