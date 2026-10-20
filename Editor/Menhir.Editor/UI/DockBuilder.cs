using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using ImGuiNET;

namespace Menhir.Editor.UI;

/// <summary>
/// Bindings for ImGui's DockBuilder API. It lives in imgui_internal.h, so ImGui.NET
/// does not wrap it, but the native cimgui library it ships exports these functions.
/// </summary>
internal static unsafe class DockBuilder
{
    private const string Lib = "cimgui";

    // ImGuiDockNodeFlags_DockSpace (private flag, imgui_internal.h).
    private const ImGuiDockNodeFlags DockSpaceFlag = (ImGuiDockNodeFlags)(1 << 10);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    private static extern uint igDockBuilderAddNode(uint nodeId, ImGuiDockNodeFlags flags);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    private static extern void igDockBuilderRemoveNode(uint nodeId);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    private static extern void igDockBuilderSetNodeSize(uint nodeId, Vector2 size);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    private static extern uint igDockBuilderSplitNode(uint nodeId, ImGuiDir splitDir, float sizeRatioForNodeAtDir,
        uint* outIdAtDir, uint* outIdAtOppositeDir);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    private static extern void igDockBuilderDockWindow(byte* windowName, uint nodeId);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    private static extern void igDockBuilderFinish(uint nodeId);

    /// <summary>Clears <paramref name="dockspaceId"/> and recreates it as an empty dockspace.</summary>
    public static void ResetDockspace(uint dockspaceId, Vector2 size)
    {
        igDockBuilderRemoveNode(dockspaceId);
        igDockBuilderAddNode(dockspaceId, DockSpaceFlag);
        igDockBuilderSetNodeSize(dockspaceId, size);
    }

    /// <summary>
    /// Splits a node. Returns the new node on <paramref name="dir"/>;
    /// <paramref name="remaining"/> is the node on the opposite side.
    /// </summary>
    public static uint Split(uint nodeId, ImGuiDir dir, float ratio, out uint remaining)
    {
        uint atDir, opposite;
        igDockBuilderSplitNode(nodeId, dir, ratio, &atDir, &opposite);
        remaining = opposite;
        return atDir;
    }

    public static void DockWindow(string windowName, uint nodeId)
    {
        byte[] utf8 = Encoding.UTF8.GetBytes(windowName + '\0');
        fixed (byte* name = utf8)
            igDockBuilderDockWindow(name, nodeId);
    }

    public static void Finish(uint dockspaceId) => igDockBuilderFinish(dockspaceId);
}
