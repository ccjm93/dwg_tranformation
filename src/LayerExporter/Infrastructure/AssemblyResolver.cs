using System.IO;
using System.Reflection;
#if !NETFRAMEWORK
using System.Runtime.Loader;
#endif

namespace LayerExporter.Infrastructure;

/// <summary>
/// AutoCAD 프로세스에서 NuGet 의존성(NetTopologySuite 등)을 찾지 못할 때
/// 플러그인 DLL과 같은 폴더에서 우선 로딩하는 핸들러.
/// .NET Framework(2018–2024)에서는 버전 불일치로 인한 바인딩 실패도 여기서 흡수한다.
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
#if NETFRAMEWORK
        AppDomain.CurrentDomain.AssemblyResolve += OnAssemblyResolve;
#else
        AssemblyLoadContext.Default.Resolving += OnResolving;
#endif
    }

#if NETFRAMEWORK
    private static Assembly? OnAssemblyResolve(object? sender, ResolveEventArgs args)
    {
        try
        {
            var pluginDir = Path.GetDirectoryName(typeof(AssemblyResolver).Assembly.Location);
            var name = new AssemblyName(args.Name).Name;
            if (pluginDir is null || name is null)
            {
                return null;
            }

            var candidate = Path.Combine(pluginDir, name + ".dll");
            return File.Exists(candidate) ? Assembly.LoadFrom(candidate) : null;
        }
        catch
        {
            return null;
        }
    }
#else
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
#endif
}
