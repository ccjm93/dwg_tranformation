# AutoCAD Map 3D 좌표선택 명령 분기 수정

## 증상

AutoCAD Map 3D 2027에서 플러그인 GUI의 **좌표선택**을 누르면 `MAPCSASSIGN` 대신 `GEOGRAPHICLOCATION` 명령이 실행되었다.

Civil 3D 2027에서는 같은 버튼이 정상적으로 `MAPCSASSIGN`을 실행했다.

## 원인

`CoordinateSystemResolver.IsCivil3DAvailable()`가 Civil 3D 전용 어셈블리인 `AeccDbMgd`의 로드 여부만 확인했다. AutoCAD Map 3D는 `MAPCSASSIGN`을 제공하지만 `AeccDbMgd`를 로드하지 않으므로 일반 AutoCAD로 잘못 판정되었다.

그 결과 `ExportDialog.OnAssignCoordinateSystem()`이 Map 3D에서 일반 AutoCAD용 대체 명령인 `GEOGRAPHICLOCATION`을 전송했다.

## 수정

- `IsCivil3DAvailable()`를 `IsMapCsAssignAvailable()`로 교체했다.
- Civil 3D의 `AeccDbMgd` 외에 AutoCAD Map 3D의 `AcMapMgd`, `Autodesk.Gis.Map.Platform` 로드 여부를 검사한다.
- 감지 결과가 참이면 `MAPCSASSIGN`, 거짓이면 기존처럼 `GEOGRAPHICLOCATION`을 실행한다.

변경 파일:

- `src/LayerExporter/Crs/CoordinateSystemResolver.cs`
- `src/LayerExporter/UI/ExportDialog.xaml.cs`

## 검증

- `dotnet test tests\\LayerExporter.Tests\\LayerExporter.Tests.csproj`: 27개 통과
- `powershell -ExecutionPolicy Bypass -File tools\\build-installer.ps1 -Configuration Release`: 2018, 2025, 2027 빌드 성공(경고 0, 오류 0)
- 설치 패키지에 각 버전의 `LayerExporter.dll`과 `CSLibrary.xml` 포함 확인

## 수동 확인

새 `installer\\dist\\LayerExporter-Setup.exe`로 설치한 뒤, AutoCAD Map 3D 2027에서 **좌표선택** 버튼이 `MAPCSASSIGN` 대화상자를 여는지 확인한다.
