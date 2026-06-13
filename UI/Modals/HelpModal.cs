// -----------------------------------------------------------------------
// LazyCaddy - help viewer. A topic list (left) + a scrollable Markdown pane
// (right). Opened by the "Show Help" command / F1. This is the help-system
// SEAM: a flat, list-navigated viewer. Once ConsoleEx gains markup link-click
// support, in-doc links can replace/augment the topic list — the content
// provider (HelpContent) already returns Markdown, so only this viewer changes.
// -----------------------------------------------------------------------

using LazyCaddy.Configuration;
using LazyCaddy.Services;
using LazyCaddy.UI.Help;
using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Layout;

namespace LazyCaddy.UI.Modals;

public sealed class HelpModal : ModalBase<bool>
{
    private readonly IReadOnlyList<HelpTopic> _topics;
    private ListControl? _topicList;
    private MarkupControl? _pane;

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
            .Build();
        var scroller = Controls.ScrollablePanel()
            .WithVerticalAlignment(VerticalAlignment.Fill)
            .Build();
        scroller.AddControl(_pane);
        var contentCol = new ColumnContainer(grid);
        contentCol.AddContent(scroller);
        grid.AddColumn(contentCol);

        Modal.AddControl(grid);

        Modal.AddControl(Controls.Toolbar()
            .AddButton(UIConstants.ActionButton("Close", "Esc", () => CloseWithResult(true)))
            .WithSpacing(2).WithMargin(2, 0, 2, 0).WithAboveLine().StickyBottom().Build());

        _topicList.SelectedItemChanged += (_, item) => ShowTopic(item?.Tag as HelpTopic);
        if (_topics.Count > 0) { _topicList.SelectedIndex = 0; ShowTopic(_topics[0]); }
    }

    // Reuse the one MarkupControl — just swap its content (Markdig-rendered).
    private void ShowTopic(HelpTopic? topic) => _pane?.SetMarkdown(topic?.Markdown ?? "");

    protected override void SetInitialFocus()
    {
        if (_topicList is not null)
            Modal.FocusManager.SetFocus(_topicList, FocusReason.Programmatic);
    }
}
