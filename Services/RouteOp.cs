namespace LazyCaddy.Services;

public enum RouteOpKind { Delete, Add, Field, Insert }

/// <summary>One staged operation in a route-edit batch.
/// Delete: remove the node at Path (array element "{arr}/{index}", or an object key).
/// Add: append Json to the array at Path ("{...}/handle").
/// Insert: insert Json into the array at Path "{arr}/{index}", shifting later elements down.
/// Field: upsert Json at Path (create-or-replace a node). OldJson for the diff.</summary>
public readonly record struct RouteOp(RouteOpKind Kind, string Path, string Json, string OldJson, string Label)
{
    public static RouteOp Delete(string nodePath, string oldJson, string label) => new(RouteOpKind.Delete, nodePath, "", oldJson, label);
    public static RouteOp Add(string arrayPath, string json, string label) => new(RouteOpKind.Add, arrayPath, json, "(new)", label);
    public static RouteOp Insert(string arrayPath, int index, string json, string label) => new(RouteOpKind.Insert, $"{arrayPath}/{index}", json, "(new)", label);
    public static RouteOp Field(PendingWrite w) => new(RouteOpKind.Field, w.Path, w.Json, w.OldJson, w.Label);
    public static RouteOp Field(string path, string json, string label) => new(RouteOpKind.Field, path, json, "{}", label);
}
