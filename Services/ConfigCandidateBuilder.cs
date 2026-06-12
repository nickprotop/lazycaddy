// -----------------------------------------------------------------------
// LazyCaddy - pure, in-memory builder of a candidate full-config from a set
// of staged RouteOps. No HTTP, no UI: take the current config JSON, clone its
// node tree, apply every op (Field=upsert, Add=append, Delete=remove element),
// and serialize the result. All-or-nothing — any invalid op throws
// CandidateBuildException and nothing is returned, so the caller never POSTs a
// partially-applied config.
//
// In-memory mutation is what makes a batch transactional: ops operate on the
// live tree, so a delete at a lower index naturally shifts the elements above
// it for a later field write in the same batch. No manual index re-pathing.
// -----------------------------------------------------------------------

using System.Text.Json;
using System.Text.Json.Nodes;

namespace LazyCaddy.Services;

/// <summary>Thrown when a candidate config cannot be built (bad path, index, or fragment JSON).</summary>
public sealed class CandidateBuildException : Exception
{
    public CandidateBuildException(string message, Exception? inner = null) : base(message, inner) { }
}

public static class ConfigCandidateBuilder
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    /// <summary>
    /// Returns a new full-config JSON string with all <paramref name="ops"/> applied to a clone
    /// of <paramref name="currentConfigJson"/>. Throws <see cref="CandidateBuildException"/> on the
    /// first invalid op, having applied nothing the caller can observe.
    /// </summary>
    public static string Apply(string currentConfigJson, IReadOnlyList<RouteOp> ops)
    {
        JsonNode root;
        try
        {
            root = JsonNode.Parse(currentConfigJson)
                   ?? throw new CandidateBuildException("Current config is null JSON.");
        }
        catch (JsonException ex)
        {
            throw new CandidateBuildException($"Current config is not valid JSON: {ex.Message}", ex);
        }

        foreach (var op in ops)
        {
            switch (op.Kind)
            {
                case RouteOpKind.Field: ApplyField(root, op.Path, op.Json); break;
                case RouteOpKind.Add: ApplyAdd(root, op.Path, op.Json); break;
                case RouteOpKind.Insert: ApplyInsert(root, op.Path, op.Json); break;
                case RouteOpKind.Delete: ApplyDelete(root, op.Path); break;
                default: throw new CandidateBuildException($"Unknown op kind: {op.Kind}");
            }
        }

        return root.ToJsonString(Indented);
    }

    // Field: upsert — set (create or replace) the node at path with the parsed fragment.
    private static void ApplyField(JsonNode root, string path, string fragmentJson)
    {
        var segs = Split(path);
        var (parent, last) = Descend(root, segs, path, createMissingObjects: true);
        var value = ParseFragment(fragmentJson, path);

        if (parent is JsonObject obj)
        {
            obj[last] = value;
        }
        else if (parent is JsonArray arr)
        {
            var i = ParseIndex(last, path);
            if (i < 0 || i >= arr.Count)
                throw new CandidateBuildException($"Index {i} out of range for array at '{path}' (count {arr.Count}).");
            arr[i] = value;
        }
        else
        {
            throw new CandidateBuildException($"Cannot set '{last}' on a non-container at '{path}'.");
        }
    }

    // Add: the node at path must be an array; append the parsed fragment as a new element.
    private static void ApplyAdd(JsonNode root, string path, string fragmentJson)
    {
        var node = ResolveExisting(root, Split(path), path);
        if (node is not JsonArray arr)
            throw new CandidateBuildException($"Cannot append at '{path}': node is not an array.");
        arr.Add(ParseFragment(fragmentJson, path));
    }

    // Insert: path is "{arr}/{index}"; the parent must be an array. Insert the parsed fragment at
    // index (0..count, where count appends), shifting later elements down.
    private static void ApplyInsert(JsonNode root, string path, string fragmentJson)
    {
        var segs = Split(path);
        var (parent, last) = Descend(root, segs, path, createMissingObjects: false);
        if (parent is not JsonArray arr)
            throw new CandidateBuildException($"Cannot insert at '{path}': parent is not an array.");
        var i = ParseIndex(last, path);
        if (i < 0 || i > arr.Count)
            throw new CandidateBuildException($"Insert index {i} out of range at '{path}' (count {arr.Count}).");
        arr.Insert(i, ParseFragment(fragmentJson, path));
    }

    // Delete: remove the node at path. For an array element "{arr}/{index}", remove that element;
    // for an object member "{obj}/{key}", remove that key. The target must exist.
    private static void ApplyDelete(JsonNode root, string path)
    {
        var segs = Split(path);
        var (parent, last) = Descend(root, segs, path, createMissingObjects: false);

        if (parent is JsonArray arr)
        {
            var i = ParseIndex(last, path);
            if (i < 0 || i >= arr.Count)
                throw new CandidateBuildException($"Index {i} out of range for array at '{path}' (count {arr.Count}).");
            arr.RemoveAt(i);
        }
        else if (parent is JsonObject obj)
        {
            if (!obj.ContainsKey(last))
                throw new CandidateBuildException($"Cannot delete '{path}': key '{last}' does not exist.");
            obj.Remove(last);
        }
        else
        {
            throw new CandidateBuildException($"Cannot delete '{last}' from a non-container at '{path}'.");
        }
    }

    // ── helpers ──────────────────────────────────────────────────────────

    private static string[] Split(string path)
    {
        var segs = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segs.Length == 0) throw new CandidateBuildException("Empty config path.");
        return segs;
    }

    /// <summary>Walk all but the final segment, returning the parent container and the final segment.
    /// When <paramref name="createMissingObjects"/> is true, absent object keys along the way are
    /// created as empty objects (so a Field write can create a deep absent node).</summary>
    private static (JsonNode parent, string last) Descend(JsonNode root, string[] segs, string path, bool createMissingObjects)
    {
        JsonNode node = root;
        for (int s = 0; s < segs.Length - 1; s++)
        {
            var seg = segs[s];
            if (node is JsonObject obj)
            {
                if (!obj.TryGetPropertyValue(seg, out var next) || next is null)
                {
                    if (!createMissingObjects)
                        throw new CandidateBuildException($"Path segment '{seg}' not found while resolving '{path}'.");
                    var created = new JsonObject();
                    obj[seg] = created;
                    node = created;
                }
                else node = next;
            }
            else if (node is JsonArray arr)
            {
                var i = ParseIndex(seg, path);
                if (i < 0 || i >= arr.Count)
                    throw new CandidateBuildException($"Index {i} out of range while resolving '{path}' (count {arr.Count}).");
                node = arr[i] ?? throw new CandidateBuildException($"Null element at index {i} while resolving '{path}'.");
            }
            else
            {
                throw new CandidateBuildException($"Cannot descend into scalar at segment '{seg}' of '{path}'.");
            }
        }
        return (node, segs[^1]);
    }

    /// <summary>Resolve the full path to an existing node (no creation); throws if any segment is missing.</summary>
    private static JsonNode ResolveExisting(JsonNode root, string[] segs, string path)
    {
        JsonNode node = root;
        foreach (var seg in segs)
        {
            if (node is JsonObject obj)
            {
                if (!obj.TryGetPropertyValue(seg, out var next) || next is null)
                    throw new CandidateBuildException($"Path segment '{seg}' not found while resolving '{path}'.");
                node = next;
            }
            else if (node is JsonArray arr)
            {
                var i = ParseIndex(seg, path);
                if (i < 0 || i >= arr.Count)
                    throw new CandidateBuildException($"Index {i} out of range while resolving '{path}' (count {arr.Count}).");
                node = arr[i] ?? throw new CandidateBuildException($"Null element at index {i} while resolving '{path}'.");
            }
            else
            {
                throw new CandidateBuildException($"Cannot descend into scalar at segment '{seg}' of '{path}'.");
            }
        }
        return node;
    }

    private static int ParseIndex(string seg, string path)
        => int.TryParse(seg, out var i)
            ? i
            : throw new CandidateBuildException($"Expected an array index but found '{seg}' in '{path}'.");

    private static JsonNode? ParseFragment(string fragmentJson, string path)
    {
        try { return JsonNode.Parse(fragmentJson); }
        catch (JsonException ex)
        {
            throw new CandidateBuildException($"Invalid JSON fragment for '{path}': {ex.Message}", ex);
        }
    }
}
