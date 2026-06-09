// -----------------------------------------------------------------------
// LazyCaddy - pure reorder of a route's handle[] JSON array. Swaps the element
// at `index` with its neighbor (delta -1 up, +1 down). Out-of-bounds or non-array
// input returns the input unchanged. No I/O. Handler order = execution order in
// Caddy, so this is a meaningful edit (PATCH the whole array back).
// -----------------------------------------------------------------------

using System.Text.Json.Nodes;

namespace LazyCaddy.Services;

public static class HandlerReorder
{
    public static string Swap(string handleArrayJson, int index, int delta)
    {
        JsonNode? node;
        try { node = JsonNode.Parse(handleArrayJson); }
        catch { return handleArrayJson; }
        if (node is not JsonArray arr) return handleArrayJson;

        int target = index + delta;
        if (index < 0 || index >= arr.Count || target < 0 || target >= arr.Count)
            return handleArrayJson;

        var a = arr[index]!.DeepClone();
        var b = arr[target]!.DeepClone();
        arr[index] = b;
        arr[target] = a;
        return arr.ToJsonString();
    }
}
