using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Layout;
using LazyCaddy.Configuration;
using LazyCaddy.Models;

namespace LazyCaddy.UI.Modals;

public sealed class SubroutePickerDialog : ModalBase<Route?>
{
    private readonly IReadOnlyList<Route> _routes;
    private TableControl? _table;

    private SubroutePickerDialog(IReadOnlyList<Route> routes) => _routes = routes;

    public static Task<Route?> ShowPickAsync(ConsoleWindowSystem ws, IReadOnlyList<Route> routes, Window? parent = null)
        => ((ModalBase<Route?>)new SubroutePickerDialog(routes)).ShowAsync(ws, parent);

    protected override string GetTitle() => " Pick nested route ";
    protected override (int width, int height) GetSize() => (70, 16);
    protected override Route? GetDefaultResult() => null;

    protected override void BuildContent()
    {
        _table = Controls.Table().AddColumn("Nested route", TextJustification.Left)
            .Rounded().WithBorderColor(UIConstants.MutedText).Interactive()
            .WithVerticalScrollbar(ScrollbarVisibility.Auto).WithName("subroutePicker").Build();
        foreach (var r in _routes) _table.AddRow(new TableRow(Escape(r.HostOrMatch)) { Tag = r });
        if (_routes.Count > 0) _table.SelectedRowIndex = 0;
        _table.RowActivatedAsync += async (_, _) => { if (_table?.SelectedRow?.Tag is Route r) CloseWithResult(r); await Task.CompletedTask; };
        Modal.AddControl(_table);
    }

    protected override void OnKeyPressed(object? sender, KeyPressedEventArgs e)
    {
        if (e.KeyInfo.Key == ConsoleKey.Escape) { CloseWithResult(null); e.Handled = true; }
    }

    private static string Escape(string s) => s.Replace("[", "[[").Replace("]", "]]");
}
