using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Autodesk.AutoCAD.ApplicationServices;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;
using Autodesk.AutoCAD.Runtime;

namespace LayerExporter.Commands;

internal static class CoordinateSystemLibraryInstaller
{
    private const string ImportCommand = "MAPCSLIBRARYIMPORT";
    private const string LibraryFileName = "CSLibrary.xml";
    private static bool waitingForDocument;
    private static bool importQueued;
    private static bool pendingForce;
    private static Document? pendingDocument;
    private static string? pendingMarkerPath;

    public static void InstallOnFirstDocument() => RequestImport(force: false);

    public static void RequestImport(bool force)
    {
        if (importQueued) return;
        var document = AcApp.DocumentManager.MdiActiveDocument;
        if (document is null)
        {
            WaitForDocument(force);
            return;
        }

        QueueImport(document, force);
    }

    private static void WaitForDocument(bool force)
    {
        pendingForce |= force;
        if (waitingForDocument) return;
        waitingForDocument = true;
        AcApp.Idle += OnIdle;
    }

    private static void OnIdle(object? sender, EventArgs e)
    {
        var document = AcApp.DocumentManager.MdiActiveDocument;
        if (document is null) return;
        AcApp.Idle -= OnIdle;
        waitingForDocument = false;
        var force = pendingForce;
        pendingForce = false;
        QueueImport(document, force);
    }

    private static void QueueImport(Document document, bool force)
    {
        var libraryPath = Path.Combine(
            Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? string.Empty,
            LibraryFileName);
        if (!File.Exists(libraryPath))
        {
            TryWriteMessage(document, "\n[LayerExporter] 좌표계 라이브러리 파일(CSLibrary.xml)을 찾을 수 없습니다.\n");
            return;
        }

        var markerPath = GetMarkerPath(libraryPath);
        if (!force && File.Exists(markerPath)) return;

        importQueued = true;
        pendingDocument = document;
        pendingMarkerPath = markerPath;
        document.CommandEnded += OnImportCommandEnded;
        document.CommandCancelled += OnImportCommandCancelled;
        document.CommandFailed += OnImportCommandFailed;

        TryWriteMessage(document,
            "\n[LayerExporter] 좌표계 라이브러리를 처음 설치합니다. 완료 후 Civil 3D를 다시 시작하면 사용할 수 있습니다.\n");
        var escapedPath = libraryPath.Replace("\"", "\"\"");
        document.SendStringToExecute($"_.{ImportCommand}\n\"{escapedPath}\"\n_Y\n", true, false, true);
    }

    private static void OnImportCommandEnded(object sender, CommandEventArgs e)
    {
        if (!IsPendingImport(sender, e)) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(pendingMarkerPath!)!);
            File.WriteAllText(pendingMarkerPath!, DateTimeOffset.UtcNow.ToString("O"), Encoding.UTF8);
            TryWriteMessage((Document)sender,
                "\n[LayerExporter] 좌표계 라이브러리 설치가 완료되었습니다. Civil 3D를 다시 시작하세요.\n");
        }
        catch (System.Exception ex)
        {
            TryWriteMessage((Document)sender,
                $"\n[LayerExporter] 좌표계 라이브러리 설치 상태를 저장하지 못했습니다: {ex.Message}\n");
        }
        finally { ClearPendingImport(); }
    }

    private static void OnImportCommandCancelled(object sender, CommandEventArgs e) =>
        ReportFailedImport(sender, e, "취소");

    private static void OnImportCommandFailed(object sender, CommandEventArgs e) =>
        ReportFailedImport(sender, e, "실패");

    private static void ReportFailedImport(object sender, CommandEventArgs e, string state)
    {
        if (!IsPendingImport(sender, e)) return;
        TryWriteMessage((Document)sender,
            $"\n[LayerExporter] 좌표계 라이브러리 설치가 {state}했습니다. DwgToSHPImportCoordinateLibrary 명령으로 다시 시도하세요.\n");
        ClearPendingImport();
    }

    private static bool IsPendingImport(object sender, CommandEventArgs e) =>
        importQueued && ReferenceEquals(sender, pendingDocument)
        && string.Equals(e.GlobalCommandName, ImportCommand, StringComparison.OrdinalIgnoreCase);

    private static void ClearPendingImport()
    {
        if (pendingDocument is not null)
        {
            pendingDocument.CommandEnded -= OnImportCommandEnded;
            pendingDocument.CommandCancelled -= OnImportCommandCancelled;
            pendingDocument.CommandFailed -= OnImportCommandFailed;
        }

        pendingDocument = null;
        pendingMarkerPath = null;
        importQueued = false;
    }

    private static string GetMarkerPath(string libraryPath)
    {
        using var sha256 = SHA256.Create();
        using var stream = File.OpenRead(libraryPath);
        var hash = BitConverter.ToString(sha256.ComputeHash(stream)).Replace("-", string.Empty);
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LayerExporter", $"coordinate-library-{hash}.installed");
    }

    private static void TryWriteMessage(Document document, string message)
    {
        try { document.Editor.WriteMessage(message); }
        catch { }
    }
}

public sealed class CoordinateSystemLibraryCommands
{
    [CommandMethod("DwgToSHPImportCoordinateLibrary", CommandFlags.Modal)]
    public void ImportCoordinateLibrary() => CoordinateSystemLibraryInstaller.RequestImport(force: true);
}
