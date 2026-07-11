# LayerExporter

Autodesk Civil 3D에서 **선택한 레이어의 객체만 골라 DXF 또는 SHP(Shapefile)로 내보내는** 플러그인입니다.

## 주요 기능

- 도면의 레이어 목록을 체크박스로 표시 (이름 필터, 전체 선택, 객체 수 표시)
- **DXF 내보내기**: 선택 레이어의 객체만 새 DXF 파일로 저장 (원본 객체 그대로 복제)
- **SHP 내보내기**: 지오메트리 타입별로 파일 분리 (`{이름}_point.shp` / `_line.shp` / `_polygon.shp`)
  - 속성 테이블(.dbf): Layer, EntType, Handle, Color, Linetype, Text, BlkName, Elev
  - UTF-8 인코딩 + `.cpg` 파일 생성 (한글 레이어명/문자 지원)
  - **도면 좌표계 자동 반영**: Civil 3D Drawing Settings의 좌표계를 읽어 `.prj` 생성
    (Autodesk API → 내장 한국 좌표계 카탈로그 순으로 폴백, 실패 시 `.prj` 생략 + 경고)
  - 옵션: 닫힌 폴리선 → 폴리곤 변환, 곡선 분할 허용오차, Z 좌표 포함
- Civil 3D 전용 객체(선형, 코리도, 지표면 등)는 SHP 변환에서 제외되며 사유와 함께 개수가 보고됩니다.

## 지원 버전

Civil 3D / AutoCAD **2018 이후 모든 버전**을 지원합니다. AutoCAD 관리(.NET) API는
아래 밴드 안에서 바이너리 호환이므로 밴드당 1개의 바이너리로 동작합니다.

| Civil 3D / AutoCAD | 시리즈 | .NET | 빌드 명령 |
|---|---|---|---|
| 2018 ~ 2024 | R22.0 ~ R24.3 | .NET Framework 4.8 | `tools\build.ps1 -Civil3DVersion 2018` |
| 2025 ~ 2026 | R25.0 ~ R25.1 | .NET 8 | `tools\build.ps1 -Civil3DVersion 2026` |
| 2027 | R26.0 | .NET 10 | `tools\build.ps1 -Civil3DVersion 2027` |

- 2018~2024 대상 PC에는 **.NET Framework 4.8** 런타임이 설치되어 있어야 합니다
  (Windows 10 1903 이후 기본 포함).
- AutoCAD 참조는 NuGet 패키지(`AutoCAD.NET`)로 받으므로 **빌드 PC에 해당 버전의
  AutoCAD/Civil 3D가 설치되어 있지 않아도** 됩니다.
- Civil 3D API(AeccDbMgd)는 리플렉션으로만 접근하므로 Civil 3D가 아닌 순수 AutoCAD에서도
  동작합니다 (이 경우 도면 좌표계 자동 인식만 비활성화되어 `.prj` 없이 출력).

## 빌드

.NET SDK 10 필요 (net48/net8/net10 모두 빌드 가능).

```powershell
# 세 밴드(2018–2024 / 2025–2026 / 2027) 전부 빌드 + 배포 폴더 복사
powershell -File tools\build.ps1 -All

# 특정 버전만 빌드 (2018~2027 아무 값이나 지정하면 해당 밴드로 매핑)
powershell -File tools\build.ps1 -Civil3DVersion 2020

# 단위 테스트
dotnet test
```

## 설치

**방법 1 — NETLOAD (간단)**

1. `tools\build.ps1` 실행 후 Civil 3D에서 `NETLOAD` 명령 실행
2. 사용 중인 버전의 밴드 폴더에서 `LayerExporter.dll` 선택
   - 2018~2024: `deploy\bin\2018\LayerExporter.dll`
   - 2025~2026: `deploy\bin\2025\LayerExporter.dll`
   - 2027: `deploy\bin\2027\LayerExporter.dll`
   (또는 `tools\netload.scr` 스크립트 사용)

**방법 2 — 자동 로드 (.bundle)**

`deploy\LayerExporter.bundle` 폴더 전체를 아래 경로에 복사하면 Civil 3D 시작 시 자동 로드됩니다.

```
%AppData%\Autodesk\ApplicationPlugins\LayerExporter.bundle
```

## 사용법

1. Civil 3D에서 도면을 열고 `EXPORTLAYERS` 명령 실행
2. 내보낼 레이어 체크 → 출력 형식(DXF/SHP) 선택 → 출력 폴더/이름 지정
3. **내보내기** 클릭 → 커맨드 라인에 결과 요약 출력

SHP의 좌표계는 도면의 `Drawing Settings > Units and Zone > Coordinate System`에 설정된 값을 따릅니다. 좌표계가 설정되지 않은 도면은 `.prj` 없이 출력됩니다.

## 프로젝트 구조

- `src/LayerExporter` — Civil 3D 플러그인 (커맨드, UI, AutoCAD 엔티티 변환)
- `src/LayerExporter.Core` — AutoCAD 비의존 로직 (곡선 분할 수학, SHP 쓰기, 좌표계 카탈로그)
- `tests/LayerExporter.Tests` — Core 단위 테스트
- `deploy/LayerExporter.bundle` — autoloader 배포 패키지
