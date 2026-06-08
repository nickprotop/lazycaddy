namespace LazyCaddy.Services;

/// <summary>One pending node write produced by an editor tab in the consolidated route modal.</summary>
public readonly record struct PendingWrite(string Path, string Json, string OldJson, string Label);
