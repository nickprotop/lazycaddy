// -----------------------------------------------------------------------
// LazyCaddy - help viewer. A topic table-of-contents (left) + a scrollable
// Markdown pane (right). Opened by F1 / the "Show Help" command.
//
// Markdown links are clickable (ConsoleEx MarkupControl.LinkClicked, mouse-only):
// in-doc "#anchor" links jump to the matching topic; external http(s) links are
// copied to the clipboard with a status confirmation (terminals can't reliably
// launch a browser).
// -----------------------------------------------------------------------

using LazyCaddy.Configuration;
using LazyCaddy.Services;
using LazyCaddy.UI.Help;
using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Events;
using SharpConsoleUI.Helpers;
using SharpConsoleUI.Layout;

namespace LazyCaddy.UI.Modals;

public sealed class HelpModal : ModalBase<bool>
{
    private readonly IReadOnlyList<HelpTopic> _topics;
    private ListControl? _topicList;
    private MarkupControl? _pane;
    private MarkupControl? _status;

    private HelpModal(IReadOnlyList<HelpTopic> topics) { _topics = topics; }

    public static Task<bool> ShowAsync(ConsoleWindowSystem ws, CommandRegistry registry, Window? parent = null)
        => ((ModalBase<bool>)new HelpModal(HelpContent.Topics(registry))).ShowAsync(ws, parent);

    protected override string GetTitle() => " Help ";
    protected override (int width, int height) GetSize() => (100, 34);
    protected override bool GetDefaultResult() => true;

    protected override void BuildContent()
    {
        var accent = UIConstants.Accent.ToMarkup();
        var muted = UIConstants.MutedText.ToMarkup();

        Modal.AddControl(Controls.Markup()
            .AddLine($"[bold {accent}]LazyCaddy Help[/]  [{muted}]↑↓ pick a topic · Tab to read · Esc to close[/]")
            .WithMargin(2, 1, 2, 0)
            .Build());

        Modal.AddControl(Controls.RuleBuilder().WithColor(UIConstants.MutedText).Build());

        // Two columns: the table of contents (left) and the rendered topic (right).
        var grid = new HorizontalGridControl
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Fill,
        };

        // Left: TOC — a titleless list of topics (fixed width).
        _topicList = Controls.List()
            .WithTitle("")
            .WithColors(UIConstants.PrimaryText, Color.Transparent)
            .WithHighlightColors(Color.White, UIConstants.SelectedBg)
            .WithDoubleClickActivation(false)
            .Build();
        foreach (var t in _topics)
            _topicList.AddItem(new ListItem(t.Title) { Tag = t });
        var tocCol = new ColumnContainer(grid) { Width = 26 };
        tocCol.AddContent(_topicList);
        grid.AddColumn(tocCol);

        // Right: the rendered Markdown topic, in a ScrollablePanel (MarkupControl has no scroll
        // of its own). Transparent background so it blends into the modal.
        _pane = Controls.Markdown(string.Empty)
            .WithColors(UIConstants.PrimaryText, Color.Transparent)
            .WithVerticalAlignment(VerticalAlignment.Top)
            .WithMargin(2, 0, 2, 0)
            .OnLinkClicked(OnLinkClicked)
            .Build();
        var scroller = Controls.ScrollablePanel()
            .WithVerticalAlignment(VerticalAlignment.Fill)
            .Build();
        scroller.AddControl(_pane);
        var contentCol = new ColumnContainer(grid);
        contentCol.AddContent(scroller);
        grid.AddColumn(contentCol);

        Modal.AddControl(grid);

        _status = Controls.Markup()
            .WithMargin(2, 0, 2, 0)
            .StickyBottom()
            .Build();
        Modal.AddControl(_status);

        Modal.AddControl(Controls.Toolbar()
            .AddButton(UIConstants.ActionButton("Close", "Esc", () => CloseWithResult(true)))
            .WithSpacing(2).WithMargin(2, 0, 2, 0).WithAboveLine().StickyBottom().Build());

        _topicList.SelectedItemChanged += (_, item) => ShowTopic(item?.Tag as HelpTopic);
        if (_topics.Count > 0) { _topicList.SelectedIndex = 0; ShowTopic(_topics[0]); }
    }

    // Reuse the one MarkupControl — just swap its content (Markdig-rendered).
    private void ShowTopic(HelpTopic? topic)
    {
        _pane?.SetMarkdown(topic?.Markdown ?? "");
        _status?.SetContent(new List<string> { "" });
    }

    // Markdown link clicked: "#anchor" jumps to that topic; external URLs are copied.
    private void OnLinkClicked(object? sender, LinkClickedEventArgs e)
    {
        var url = e.Url ?? "";
        if (url.StartsWith('#'))
        {
            var anchor = url[1..];
            int idx = IndexOfTopic(anchor);
            if (idx >= 0 && _topicList is not null)
                _topicList.SelectedIndex = idx;   // triggers SelectedItemChanged → ShowTopic
            else
                SetStatus($"[{UIConstants.Warn.ToMarkup()}]No such topic: {Escape(anchor)}[/]");
            return;
        }
        // External link: copy to clipboard (terminals can't reliably open a browser).
        try { ClipboardHelper.SetText(url); SetStatus($"[{UIConstants.Good.ToMarkup()}]✓ Copied link:[/] [{UIConstants.MutedText.ToMarkup()}]{Escape(url)}[/]"); }
        catch { SetStatus($"[{UIConstants.MutedText.ToMarkup()}]{Escape(url)}[/]"); }
    }

    private int IndexOfTopic(string anchor)
    {
        for (int i = 0; i < _topics.Count; i++)
            if (string.Equals(_topics[i].Anchor, anchor, StringComparison.OrdinalIgnoreCase))
                return i;
        return -1;
    }

    private void SetStatus(string markup) => _status?.SetContent(new List<string> { markup });
    private static string Escape(string s) => s.Replace("[", "[[").Replace("]", "]]");

    protected override void SetInitialFocus()
    {
        if (_topicList is not null)
            Modal.FocusManager.SetFocus(_topicList, FocusReason.Programmatic);
    }
}
