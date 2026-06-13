// -----------------------------------------------------------------------
// LazyCaddy - help content provider. Loads the embedded Markdown topics and
// appends a live-generated "Keyboard shortcuts" topic from the command registry.
//
// This is the help INFRASTRUCTURE seam: a flat topic list rendered as Markdown.
// Cross-topic clickable-link navigation is deferred until ConsoleEx grows
// markup link-click support; topics are selected from the list for now.
// -----------------------------------------------------------------------

using System.Reflection;
using LazyCaddy.Services;

namespace LazyCaddy.UI.Help;

/// <summary>One help topic: a stable anchor (for in-doc `#anchor` links), a title, and its Markdown body.</summary>
public sealed record HelpTopic(string Anchor, string Title, string Markdown);

public static class HelpContent
{
    // Embedded .md resource base name → (anchor, display title), in display order.
    // The anchor is what in-doc `#anchor` links resolve to.
    private static readonly (string Resource, string Anchor, string Title)[] Embedded =
    {
        ("overview", "overview", "Overview"),
        ("editing", "editing", "Editing & snapshots"),
    };

    /// <summary>Anchor for the generated keyboard-shortcuts topic (link to it with `#keys`).</summary>
    public const string KeysAnchor = "keys";

    /// <summary>Build the topic list: embedded prose topics + the generated keys cheatsheet.</summary>
    public static IReadOnlyList<HelpTopic> Topics(CommandRegistry registry)
    {
        var topics = new List<HelpTopic>();
        foreach (var (res, anchor, title) in Embedded)
        {
            var md = LoadEmbedded($"LazyCaddy.Help.{res}.md");
            if (md is not null) topics.Add(new HelpTopic(anchor, title, md));
        }
        topics.Add(new HelpTopic(KeysAnchor, "Keyboard shortcuts", HelpKeysTopic.Render(registry)));
        return topics;
    }

    private static string? LoadEmbedded(string resourceName)
    {
        var asm = Assembly.GetExecutingAssembly();
        using var stream = asm.GetManifestResourceStream(resourceName);
        if (stream is null) return null;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
