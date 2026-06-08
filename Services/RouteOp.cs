namespace LazyCaddy.Services;

public enum RouteOpKind { Delete, Add, Field }

/// <summary>One staged operation in a route-edit batch.
/// Delete: remove the handler node at Path (an array element "{arr}/{index}").
/// Add: POST Json to the array at Path ("{...}/handle"), appending a new handler.
/// Field: Upsert Json at Path (an existing node's field write). OldJson for the diff.</summary>
public readonly record struct RouteOp(RouteOpKind Kind, string Path, string Json, string OldJson, string Label)
{
    public static RouteOp Delete(string nodePath, string oldJson, string label) => new(RouteOpKind.Delete, nodePath, "", oldJson, label);
    public static RouteOp Add(string arrayPath, string json, string label) => new(RouteOpKind.Add, arrayPath, json, "(new)", label);
    public static RouteOp Field(PendingWrite w) => new(RouteOpKind.Field, w.Path, w.Json, w.OldJson, w.Label);
}
