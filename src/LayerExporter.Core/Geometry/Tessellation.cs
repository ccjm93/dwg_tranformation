namespace LayerExporter.Core.Geometry;

public readonly record struct Pt2(double X, double Y);

/// <summary>
/// 곡선을 허용오차(sagitta) 기반 폴리라인으로 분할하는 순수 수학 유틸.
/// AutoCAD API에 의존하지 않아 단위 테스트가 가능하다.
/// </summary>
public static class Tessellation
{
    private const int MinSegmentsPerArc = 2;
    private const int MaxSegmentsPerArc = 2048;

    /// <summary>
    /// 허용오차(현과 호 사이 최대 거리)를 만족하는 호의 최대 각도 스텝.
    /// sagitta = r * (1 - cos(step/2)) &lt;= tol 에서 유도.
    /// </summary>
    public static double MaxAngleStep(double radius, double tolerance)
    {
        if (radius <= 0 || tolerance <= 0)
        {
            return Math.PI / 8;
        }

        var ratio = 1.0 - tolerance / radius;
        if (ratio <= -1.0)
        {
            return Math.PI / 2;
        }

        // Math.Clamp은 net48에 없으므로 Min/Max 조합 사용
        var step = 2.0 * Math.Acos(Math.Max(ratio, -1.0));
        return Math.Min(Math.Max(step, 1e-4), Math.PI / 2);
    }

    /// <summary>
    /// 중심/반지름/시작각/스윕각(라디안, CCW 양수)으로 호를 분할한다.
    /// 시작점과 끝점을 모두 포함한다.
    /// </summary>
    public static List<Pt2> TessellateArc(Pt2 center, double radius, double startAngle, double sweepAngle, double tolerance)
    {
        var step = MaxAngleStep(radius, tolerance);
        var n = Math.Min(Math.Max((int)Math.Ceiling(Math.Abs(sweepAngle) / step), MinSegmentsPerArc), MaxSegmentsPerArc);
        var points = new List<Pt2>(n + 1);
        for (var i = 0; i <= n; i++)
        {
            var a = startAngle + sweepAngle * i / n;
            points.Add(new Pt2(center.X + radius * Math.Cos(a), center.Y + radius * Math.Sin(a)));
        }

        return points;
    }

    /// <summary>원 전체를 닫힌 점 목록으로 분할한다 (첫 점 == 끝 점).</summary>
    public static List<Pt2> TessellateCircle(Pt2 center, double radius, double tolerance)
    {
        return TessellateArc(center, radius, 0.0, Math.PI * 2.0, tolerance);
    }

    /// <summary>
    /// bulge 세그먼트(start→end, bulge = tan(sweep/4))를 분할한다.
    /// 반환 목록은 start를 포함하고 end로 끝난다. bulge가 0에 가까우면 직선 처리.
    /// </summary>
    public static List<Pt2> TessellateBulgeSegment(Pt2 start, Pt2 end, double bulge, double tolerance)
    {
        if (Math.Abs(bulge) < 1e-10)
        {
            return [start, end];
        }

        var chordX = end.X - start.X;
        var chordY = end.Y - start.Y;
        var chord = Math.Sqrt(chordX * chordX + chordY * chordY);
        if (chord < 1e-12)
        {
            return [start, end];
        }

        var sweep = 4.0 * Math.Atan(bulge); // 부호 포함 (양수 = CCW)
        var radius = chord / (2.0 * Math.Sin(Math.Abs(sweep) / 2.0));

        // 현의 중점에서 수직 방향으로 중심까지의 거리 (부호는 sweep 방향에 따름)
        var midX = (start.X + end.X) / 2.0;
        var midY = (start.Y + end.Y) / 2.0;
        var h = radius * Math.Cos(sweep / 2.0) * Math.Sign(sweep);
        // 현 방향 단위벡터를 90도 회전(CCW)한 방향으로 h만큼 이동하면 중심
        var ux = chordX / chord;
        var uy = chordY / chord;
        var cx = midX - uy * h;
        var cy = midY + ux * h;

        var startAngle = Math.Atan2(start.Y - cy, start.X - cx);
        return TessellateArc(new Pt2(cx, cy), radius, startAngle, sweep, tolerance);
    }

    /// <summary>
    /// 폴리라인 정점 목록(각 정점의 bulge는 다음 정점까지의 세그먼트에 적용)을 분할한다.
    /// closed면 마지막 정점→첫 정점 세그먼트를 추가하고 결과의 첫 점과 끝 점이 같아진다.
    /// </summary>
    public static List<Pt2> TessellatePolyline(IReadOnlyList<(Pt2 Point, double Bulge)> vertices, bool closed, double tolerance)
    {
        var result = new List<Pt2>();
        if (vertices.Count == 0)
        {
            return result;
        }

        var segmentCount = closed ? vertices.Count : vertices.Count - 1;
        result.Add(vertices[0].Point);
        for (var i = 0; i < segmentCount; i++)
        {
            var (p1, bulge) = vertices[i];
            var p2 = vertices[(i + 1) % vertices.Count].Point;
            var segment = TessellateBulgeSegment(p1, p2, bulge, tolerance);
            // 첫 점은 이미 결과에 있으므로 제외하고 이어붙인다
            for (var j = 1; j < segment.Count; j++)
            {
                result.Add(segment[j]);
            }
        }

        return result;
    }
}
