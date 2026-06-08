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

    /// <summary>
    /// Launch async work from a synchronous UI event (e.g. the <see cref="Modal.KeyPressed"/>
    /// contract handler, which must stay sync to return <c>e.Handled</c>). The work runs as a
    /// fire-and-forget task that must NEVER block the UI thread — every long operation inside it
    /// has to be <c>await</c>ed (HTTP, nested modals, Task.Delay), never <c>.Result</c>/<c>.Wait()</c>,
    /// or the captured UI SynchronizationContext deadlocks the loop (see ConsoleEx
    /// THREADING_AND_ASYNC.md). Unlike a bare <c>_ = WorkAsync()</c>, exceptions are not swallowed
    /// to the framework log — they are marshalled onto the UI thread and handed to
    /// <paramref name="onError"/> so the user sees the failure instead of a frozen dialog.
    /// </summary>
    protected void RunGuarded(Func<Task> work, Action<string>? onError = null)
    {
        _ = RunGuardedCore(work, onError);
    }

    private async Task RunGuardedCore(Func<Task> work, Action<string>? onError)
    {
        try
        {
            await work();
        }
        catch (Exception ex)
        {
            // Resume on the UI thread before touching any control.
            await WindowSystem.InvokeAsync(() => onError?.Invoke(ex.Message), label: "ModalBase.RunGuarded");
        }
    }
}
