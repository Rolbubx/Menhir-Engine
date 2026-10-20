using System.Numerics;
using ImGuiNET;
using Menhir.Editor.Mathematics;
using Menhir.Editor.UI;

namespace Menhir.Editor.Inspector;

/// <summary>
/// A small graph to edit a <see cref="ColorCurve"/>: drag a point to move it, double-click to add
/// one, right-click a point to remove it. Edits return a new curve (the old one is kept for undo).
/// </summary>
public static class CurveEditor
{
    private const float Height = 96f;
    private const float PointRadius = 4f;
    private const float GrabDistance = 8f;

    // The point being dragged: the editor's ImGui ID and the point's index.
    private static uint _dragId;
    private static int _dragIndex = -1;

    public static bool Draw(string id, ColorCurve curve, Vector4 color, out ColorCurve edited)
    {
        edited = curve;
        float width = MathF.Max(40f, ImGui.CalcItemWidth());
        Vector2 min = ImGui.GetCursorScreenPos();
        Vector2 size = new(width, Height);
        Vector2 max = min + size;

        ImGui.InvisibleButton(id, size, ImGuiButtonFlags.MouseButtonLeft | ImGuiButtonFlags.MouseButtonRight);
        uint itemId = ImGui.GetItemID();
        bool hovered = ImGui.IsItemHovered();
        bool active = ImGui.IsItemActive();

        Vector2 ToScreen(Vector2 p) => new(min.X + p.X * size.X, max.Y - p.Y * size.Y);
        Vector2 ToCurve(Vector2 s) => Vector2.Clamp(new((s.X - min.X) / size.X, (max.Y - s.Y) / size.Y), Vector2.Zero, Vector2.One);

        ImDrawListPtr drawList = ImGui.GetWindowDrawList();
        drawList.AddRectFilled(min, max, ImGui.GetColorU32(EditorTheme.Field), 3f);
        uint gridColor = ImGui.GetColorU32(new Vector4(1, 1, 1, 0.06f));
        for (int i = 1; i < 4; i++)
        {
            float t = i / 4f;
            drawList.AddLine(new Vector2(min.X + t * size.X, min.Y), new Vector2(min.X + t * size.X, max.Y), gridColor);
            drawList.AddLine(new Vector2(min.X, min.Y + t * size.Y), new Vector2(max.X, min.Y + t * size.Y), gridColor);
        }
        drawList.AddRect(min, max, ImGui.GetColorU32(ImGuiCol.Border), 3f);

        // The curve, sampled.
        const int segments = 64;
        Vector2 previous = ToScreen(new Vector2(0, curve.Evaluate(0)));
        for (int i = 1; i <= segments; i++)
        {
            float x = i / (float)segments;
            Vector2 point = ToScreen(new Vector2(x, curve.Evaluate(x)));
            drawList.AddLine(previous, point, ImGui.GetColorU32(color), 1.5f);
            previous = point;
        }

        List<Vector2> points = curve.Points;
        Vector2 mouse = ImGui.GetMousePos();
        int hoveredPoint = -1;
        for (int i = 0; i < points.Count; i++)
        {
            if (Vector2.Distance(ToScreen(points[i]), mouse) <= GrabDistance)
                hoveredPoint = i;
        }

        bool changed = false;
        if (hovered && ImGui.IsMouseClicked(ImGuiMouseButton.Left) && hoveredPoint >= 0)
        {
            _dragId = itemId;
            _dragIndex = hoveredPoint;
        }

        if (active && _dragId == itemId && _dragIndex >= 0 && _dragIndex < points.Count && ImGui.IsMouseDown(ImGuiMouseButton.Left))
        {
            Vector2 moved = ToCurve(mouse);
            var newPoints = points.ToList();
            newPoints[_dragIndex] = moved;
            edited = curve.WithPoints(newPoints);
            // Sorting may have moved the point: keep dragging the same one.
            _dragIndex = edited.Points.FindIndex(p => p == Vector2.Clamp(moved, Vector2.Zero, Vector2.One));
            changed = true;
        }
        else if (_dragId == itemId && !ImGui.IsMouseDown(ImGuiMouseButton.Left))
        {
            _dragIndex = -1;
        }

        if (hovered && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left) && hoveredPoint < 0)
        {
            Vector2 added = ToCurve(mouse);
            edited = curve.WithPoints(points.Append(added));
            changed = true;
        }
        else if (hovered && ImGui.IsMouseClicked(ImGuiMouseButton.Right) && hoveredPoint >= 0)
        {
            edited = curve.WithPoints(points.Where((_, i) => i != hoveredPoint));
            changed = true;
        }

        for (int i = 0; i < edited.Points.Count; i++)
        {
            Vector2 p = ToScreen(edited.Points[i]);
            bool highlight = i == hoveredPoint || (_dragId == itemId && i == _dragIndex);
            drawList.AddCircleFilled(p, highlight ? PointRadius + 1.5f : PointRadius, ImGui.GetColorU32(highlight ? Vector4.One : color));
        }

        if (hovered && !active)
            ImGui.SetTooltip(hoveredPoint >= 0
                ? $"({points[hoveredPoint].X:0.00}, {points[hoveredPoint].Y:0.00}) - drag to move, right-click to remove"
                : "Double-click to add a point");
        return changed;
    }
}
