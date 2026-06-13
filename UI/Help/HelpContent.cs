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

/// <summary>One help topic: a title and its Markdown body.</summary>
public sealed record HelpTopic(string Title, string Markdown);

public static class HelpContent
{
    // Embedded .md resource base name → display title, in display order.
    private static readonly (string Resource, string Title)[] Embedded =
    {
        ("overview", "Overview"),
        ("editing", "Editing & snapshots"),
    };

    /// <summary>Build the topic list: embedded prose topics + the generated keys cheatsheet.</summary>
    public static IReadOnlyList<HelpTopic> Topics(CommandRegistry registry)
    {
        var topics = new List<HelpTopic>();
        foreach (var (res, title) in Embedded)
        {
            var md = LoadEmbedded($"LazyCaddy.Help.{res}.md");
            if (md is not null) topics.Add(new HelpTopic(title, md));
        }
        topics.Add(new HelpTopic("Keyboard shortcuts", HelpKeysTopic.Render(registry)));
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
