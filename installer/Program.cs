using System.IO.Compression;
using System.Reflection;

namespace LayerExporter.Installer;

/// <summary>
/// LayerExporter(DwgToSHP) 플러그인을 Civil 3D / AutoCAD 2018~2027의
/// per-user ApplicationPlugins 폴더에 설치하는 단일 실행파일 설치 프로그램.
/// 번들에 세 밴드(2018-2024/2025-2026/2027)가 모두 포함되어 있으며,
/// AutoCAD가 실행 중인 버전에 맞는 밴드를 자동으로 로드한다.
/// 관리자 권한이 필요 없도록 %AppData% 경로에 설치한다.
/// </summary>
internal static class Program
{
    private const string BundleName = "LayerExporter.bundle";
    private const string ResourceName = "LayerExporter.bundle.zip";

    private static int Main(string[] args)
    {
        bool silent = args.Any(a =>
            a.Equals("/S", StringComparison.OrdinalIgnoreCase) ||
            a.Equals("/silent", StringComparison.OrdinalIgnoreCase));

        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.WriteLine("========================================");
        Console.WriteLine(" LayerExporter (DwgToSHP) 설치 프로그램");
        Console.WriteLine(" 대상: Civil 3D / AutoCAD 2018~2027");
        Console.WriteLine("========================================");
        Console.WriteLine();

        try
        {
            var pluginsDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Autodesk", "ApplicationPlugins");
            var targetDir = Path.Combine(pluginsDir, BundleName);

            Console.WriteLine($"설치 위치: {targetDir}");

            // 실행 중인 AutoCAD가 있으면 파일 잠금으로 설치가 실패한다 — 안내한다.
            if (IsAutoCadRunning())
            {
                Console.WriteLine();
                Console.WriteLine("[경고] AutoCAD/Civil 3D가 실행 중입니다.");
                Console.WriteLine("       설치 중 파일 잠금이 발생할 수 있으니 종료 후 진행하는 것을 권장합니다.");
                if (!silent && !Confirm("계속 진행할까요? (Y/N): "))
                {
                    Console.WriteLine("설치를 취소했습니다.");
                    return 2;
                }
            }

            // 기존 설치본 제거 후 새로 배치 (재설치/업그레이드 대비)
            if (Directory.Exists(targetDir))
            {
                Console.WriteLine("기존 설치본을 제거합니다...");
                Directory.Delete(targetDir, recursive: true);
            }

            Directory.CreateDirectory(pluginsDir);

            Console.WriteLine("플러그인 파일을 배치합니다...");
            ExtractEmbeddedBundle(pluginsDir);

            var manifest = Path.Combine(targetDir, "PackageContents.xml");
            if (!File.Exists(manifest))
            {
                throw new FileNotFoundException("번들 배치 후 PackageContents.xml을 찾을 수 없습니다.", manifest);
            }

            Console.WriteLine();
            Console.WriteLine("설치가 완료되었습니다.");
            Console.WriteLine();
            Console.WriteLine("다음 단계:");
            Console.WriteLine("  1. Civil 3D / AutoCAD (2018~2027)을 시작하면 자동으로 로드됩니다.");
            Console.WriteLine("  2. 상단 'DH 플러그인' 탭의 버튼 또는 DwgToSHP 명령으로 실행하세요.");

            Pause(silent);
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine();
            Console.WriteLine($"[오류] 설치에 실패했습니다: {ex.Message}");
            Pause(silent);
            return 1;
        }
    }

    /// <summary>대화형 실행일 때만 키 입력을 기다린다. 입력이 리다이렉트/무콘솔이면 그냥 종료한다.</summary>
    private static void Pause(bool silent)
    {
        if (silent || Console.IsInputRedirected)
        {
            return;
        }

        Console.WriteLine();
        Console.WriteLine("아무 키나 누르면 종료합니다...");
        try
        {
            Console.ReadKey(intercept: true);
        }
        catch (InvalidOperationException)
        {
            // 콘솔 입력을 사용할 수 없는 환경 — 조용히 종료
        }
    }

    /// <summary>내장된 번들 zip을 지정 폴더에 풀어 LayerExporter.bundle 폴더를 만든다.</summary>
    private static void ExtractEmbeddedBundle(string destinationRoot)
    {
        var asm = Assembly.GetExecutingAssembly();
        using var stream = asm.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"내장 리소스({ResourceName})를 찾을 수 없습니다.");
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        archive.ExtractToDirectory(destinationRoot, overwriteFiles: true);
    }

    private static bool IsAutoCadRunning()
    {
        try
        {
            return System.Diagnostics.Process.GetProcessesByName("acad").Length > 0;
        }
        catch
        {
            return false;
        }
    }

    private static bool Confirm(string prompt)
    {
        Console.Write(prompt);
        var key = Console.ReadKey(intercept: false).Key;
        Console.WriteLine();
        return key == ConsoleKey.Y;
    }
}
