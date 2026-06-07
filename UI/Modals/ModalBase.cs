// -----------------------------------------------------------------------
// LazyCaddy - reusable modal base.
//
// Adapted from lazynuget's UI/Modals/ModalBase.cs (same author/library).
// Provides the standard modal lifecycle: ShowAsync + TaskCompletionSource,
// centered modal window, Escape handling, and cleanup hook.
// -----------------------------------------------------------------------

using SharpConsoleUI;
using SharpConsoleUI.Builders;
using LazyCaddy.Configuration;

namespace LazyCaddy.UI.Modals;

/// <summary>Abstract base for modals returning a <typeparamref name="TResult"/>.</summary>
public abstract class ModalBase<TResult>
{
    private readonly TaskCompletionSource<TResult> _tcs = new();
    protected TResult? Result { get; set; }

    protected Window Modal { get; private set; } = null!;
    protected ConsoleWindowSystem WindowSystem { get; private set; } = null!;
    protected Window? ParentWindow { get; private set; }

    /// <summary>Show the modal and await its result.</summary>
    public Task<TResult> ShowAsync(ConsoleWindowSystem windowSystem, Window? parentWindow = null)
    {
        WindowSystem = windowSystem;
        ParentWindow = parentWindow;

        Modal = CreateModal();
        BuildContent();
        AttachEventHandlers();

        WindowSystem.AddWindow(Modal);
        WindowSystem.SetActiveWindow(Modal);

        SetInitialFocus();
        return _tcs.Task;
    }

    protected virtual Window CreateModal()
    {
        var builder = new WindowBuilder(WindowSystem)
            .AsModal()
            .Resizable(GetResizable())
            .Movable(GetMovable())
            .Minimizable(false)
            .WithColors(UIConstants.PrimaryText, UIConstants.ContentBg)
            .WithBorderStyle(GetBorderStyle())
            .WithBorderColor(GetBorderColor());

        var title = GetTitle();
        if (!string.IsNullOrEmpty(title))
            builder.WithTitle(title);

        // Centered must come after WithSize.
        var (width, height) = GetSize();
        builder.WithSize(width, height);
        builder.Centered();

        return builder.Build();
    }

    protected abstract void BuildContent();
    protected abstract string GetTitle();
    protected virtual (int width, int height) GetSize() => (60, 18);
    protected virtual bool GetResizable() => true;
    protected virtual bool GetMovable() => true;
    protected virtual BorderStyle GetBorderStyle() => BorderStyle.Rounded;
    protected virtual Color GetBorderColor() => UIConstants.AccentBlue;

    protected virtual void SetInitialFocus() { }

    private void AttachEventHandlers()
    {
        Modal.KeyPressed += OnKeyPressed;
        Modal.OnClosed += OnModalClosed;
    }

    protected virtual void OnKeyPressed(object? sender, KeyPressedEventArgs e)
    {
        if (e.KeyInfo.Key == ConsoleKey.Escape)
        {
            OnEscapePressed();
            e.Handled = true;
        }
    }

    protected virtual void OnEscapePressed() => CloseWithResult(GetDefaultResult());

    protected virtual TResult GetDefaultResult() => default(TResult)!;

    private void OnModalClosed(object? sender, EventArgs e)
    {
        OnCleanup();
        _tcs.TrySetResult(Result ?? GetDefaultResult());
    }

    /// <summary>Override to unsubscribe handlers / release resources when the modal closes.</summary>
    protected virtual void OnCleanup() { }

    protected void CloseWithResult(TResult result)
    {
        Result = result;
        Modal.Close();
    }
}
