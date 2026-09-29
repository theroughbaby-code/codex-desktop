namespace Loupedeck.CodexDesktopPlugin;

internal enum MacActionAttempt
{
    NoTarget,
    AlreadyActive,
    Invoked,
    Clicked,
    ReadyForKeyboardFallback,
    Unavailable,
    PermissionRequired,
}

public enum MacDesktopMode
{
    ChatGPT,
    Work,
    Codex,
}

internal enum MacTargetAction
{
    Unavailable,
    Invoked,
    Clicked,
}

public enum ApprovalDecision
{
    Approve,
    AlwaysApprove,
    Deny,
}

internal sealed class MacAxElement : IDisposable
{
    private IntPtr handle;

    public MacAxElement(IntPtr handle) => this.handle = handle;

    ~MacAxElement() => this.Dispose(false);

    public IntPtr Handle => this.handle;

    public MacAxElement Clone()
    {
        if (this.handle == IntPtr.Zero)
        {
            throw new ObjectDisposedException(nameof(MacAxElement));
        }

        return MacAccessibilityNative.RetainElement(this.handle);
    }

    public void Dispose()
    {
        this.Dispose(true);
        GC.SuppressFinalize(this);
    }

    private void Dispose(Boolean disposing)
        => MacAccessibilityNative.Release(
            Interlocked.Exchange(ref this.handle, IntPtr.Zero));
}

internal sealed class MacAxTarget : IDisposable
{
    private readonly MacAxElement element;
    private readonly MacAxElement window;
    private readonly MacAxElement? boundsContainer;

    public MacAxTarget(
        IntPtr element,
        IntPtr window,
        String role,
        Boolean isFocusedWindow,
        String label,
        IntPtr boundsContainer = default)
        : this(
            MacAccessibilityNative.RetainElement(element),
            MacAccessibilityNative.RetainElement(window),
            role,
            isFocusedWindow,
            label,
            boundsContainer == IntPtr.Zero
                ? null
                : MacAccessibilityNative.RetainElement(boundsContainer))
    {
    }

    private MacAxTarget(
        MacAxElement element,
        MacAxElement window,
        String role,
        Boolean isFocusedWindow,
        String label,
        MacAxElement? boundsContainer)
    {
        this.element = element;
        this.window = window;
        this.Role = role;
        this.IsFocusedWindow = isFocusedWindow;
        this.Label = label;
        this.boundsContainer = boundsContainer;
    }

    public String Role { get; }

    public Boolean IsFocusedWindow { get; }

    public String Label { get; }

    public MacAxTarget Clone()
        => new(
            this.element.Clone(),
            this.window.Clone(),
            this.Role,
            this.IsFocusedWindow,
            this.Label,
            this.boundsContainer?.Clone());

    public MacTargetAction TryPress()
    {
        if (!this.IsFocusedWindow)
        {
            this.ActivateWindow();
            return MacTargetAction.Unavailable;
        }

        if (!this.IsCurrentTarget())
        {
            return MacTargetAction.Unavailable;
        }

        // AXPick is an obsolete selection action on macOS menu items. Electron
        // advertises it alongside AXPress, but a successful AXPick can leave the
        // command selected without executing it.
        var actions = new[] { "AXPress", "AXConfirm", "AXPick" };
        foreach (var action in actions)
        {
            if (!this.IsCurrentTarget())
            {
                return MacTargetAction.Unavailable;
            }

            if (MacAccessibilityNative.TryPerformAdvertisedAction(
                    this.element.Handle,
                    action))
            {
                return MacTargetAction.Invoked;
            }
        }

        return this.IsCurrentTarget()
            && MacAccessibilityNative.TryClickCenter(this.element.Handle)
            ? MacTargetAction.Clicked
            : MacTargetAction.Unavailable;
    }

    public MacTargetAction TryPressOnly()
    {
        if (!this.IsFocusedWindow)
        {
            this.ActivateWindow();
            return MacTargetAction.Unavailable;
        }

        return this.IsCurrentTarget()
            && MacAccessibilityNative.TryPerformAdvertisedAction(
                this.element.Handle,
                "AXPress")
            ? MacTargetAction.Invoked
            : MacTargetAction.Unavailable;
    }

    public MacTargetAction TryPressOnce()
    {
        if (!this.IsFocusedWindow)
        {
            this.ActivateWindow();
            return MacTargetAction.Unavailable;
        }

        return this.IsCurrentTarget()
            && MacAccessibilityNative.TryPerformAdvertisedActionOnce(
                this.element.Handle,
                "AXPress")
            ? MacTargetAction.Invoked
            : MacTargetAction.Unavailable;
    }

    public MacTargetAction TryOpen()
    {
        if (!this.IsFocusedWindow)
        {
            this.ActivateWindow();
            return MacTargetAction.Unavailable;
        }

        if (!this.IsCurrentTarget())
        {
            return MacTargetAction.Unavailable;
        }

        // Chromium currently advertises AXShowMenu on web pop-up buttons and
        // returns success without opening them. Use only press-like semantic
        // actions here, then fall back to the verified center click.
        foreach (var action in new[] { "AXPress", "AXConfirm", "AXPick" })
        {
            if (!this.IsCurrentTarget())
            {
                return MacTargetAction.Unavailable;
            }

            if (MacAccessibilityNative.TryPerformAdvertisedAction(
                    this.element.Handle,
                    action))
            {
                return MacTargetAction.Invoked;
            }
        }

        return this.IsCurrentTarget()
            && MacAccessibilityNative.TryClickCenter(this.element.Handle)
            ? MacTargetAction.Clicked
            : MacTargetAction.Unavailable;
    }

    public MacTargetAction TryClick()
    {
        if (!this.IsFocusedWindow)
        {
            this.ActivateWindow();
            return MacTargetAction.Unavailable;
        }

        return this.HasUsableFrame()
            && MacAccessibilityNative.TryClickCenter(this.element.Handle)
            ? MacTargetAction.Clicked
            : MacTargetAction.Unavailable;
    }

    public Boolean TryFocus()
    {
        if (!this.IsCurrentTarget())
        {
            return false;
        }

        return MacAccessibilityNative.TrySetTrue(this.element.Handle, "AXFocused");
    }

    public Boolean HasUsableFrame()
        => this.IsCurrentTarget()
            && MacAccessibilityNative.HasUsableFrame(this.element.Handle)
            && MacAccessibilityNative.IsElementCenterInside(
                this.element.Handle,
                this.window.Handle)
            && (this.boundsContainer is null
                || MacAccessibilityNative.IsElementCenterInside(
                    this.element.Handle,
                    this.boundsContainer.Handle));

    public Boolean TryScrollToVisible()
        => this.IsCurrentTarget()
            && MacAccessibilityNative.TryPerformAdvertisedAction(
                this.element.Handle,
                "AXScrollToVisible");

    public Boolean TrySetValue(String value)
        => this.IsCurrentTarget()
            && MacAccessibilityNative.TrySetString(
                this.element.Handle,
                "AXValue",
                value);

    public void Dispose()
    {
        this.element.Dispose();
        this.window.Dispose();
        this.boundsContainer?.Dispose();
    }

    private Boolean IsCurrentTarget()
        => this.IsFocusedWindow
            && MacAccessibilityNative.IsFrontmostFocusedWindow(
                this.element.Handle,
                this.window.Handle);

    private void ActivateWindow()
    {
        _ = MacAccessibilityNative.TrySetTrue(this.window.Handle, "AXMain");
        _ = MacAccessibilityNative.TryPerformAdvertisedAction(this.window.Handle, "AXRaise");
    }
}

internal sealed class MacApprovalSurface : IDisposable
{
    public MacApprovalSurface(
        Boolean isFocusedWindow,
        MacAxTarget? approve,
        MacAxTarget? persistent,
        MacAxTarget? deny,
        MacAxTarget? options,
        Boolean canApprove,
        Boolean canAlwaysApprove,
        Boolean canDeny)
    {
        this.IsFocusedWindow = isFocusedWindow;
        this.Approve = approve;
        this.Persistent = persistent;
        this.Deny = deny;
        this.Options = options;
        this.CanApprove = canApprove;
        this.CanAlwaysApprove = canAlwaysApprove;
        this.CanDeny = canDeny;
    }

    public Boolean IsFocusedWindow { get; }

    public MacAxTarget? Approve { get; }

    public MacAxTarget? Persistent { get; }

    public MacAxTarget? Deny { get; }

    public MacAxTarget? Options { get; }

    public Boolean CanApprove { get; }

    public Boolean CanAlwaysApprove { get; }

    public Boolean CanDeny { get; }

    public Boolean HasDecisionTarget
        => this.Approve is not null || this.Persistent is not null || this.Deny is not null;

    public MacApprovalSurface Clone()
        => new(
            this.IsFocusedWindow,
            this.Approve?.Clone(),
            this.Persistent?.Clone(),
            this.Deny?.Clone(),
            this.Options?.Clone(),
            this.CanApprove,
            this.CanAlwaysApprove,
            this.CanDeny);

    public void Dispose()
    {
        this.Approve?.Dispose();
        this.Persistent?.Dispose();
        this.Deny?.Dispose();
        this.Options?.Dispose();
    }
}

internal sealed class MacAccessibilitySnapshot : IDisposable
{
    public MacAccessibilitySnapshot(
        Boolean isTrusted,
        IReadOnlyList<MacApprovalSurface> approvals,
        IReadOnlyList<MacAxTarget> stopTargets)
    {
        this.IsTrusted = isTrusted;
        this.Approvals = approvals;
        this.StopTargets = stopTargets;
    }

    public Boolean IsTrusted { get; }

    public IReadOnlyList<MacApprovalSurface> Approvals { get; }

    public IReadOnlyList<MacAxTarget> StopTargets { get; }

    public Boolean HasPendingApproval
        => this.Approvals.Any(approval =>
            approval.CanApprove || approval.CanAlwaysApprove || approval.CanDeny);

    public Boolean HasActionableApproval(ApprovalDecision decision)
        => this.Approvals.Any(approval => decision switch
        {
            ApprovalDecision.Approve => approval.CanApprove,
            ApprovalDecision.AlwaysApprove => approval.CanAlwaysApprove,
            _ => approval.CanDeny,
        });

    public Boolean HasActiveTurn => this.StopTargets.Count > 0;

    public static MacAccessibilitySnapshot Untrusted()
        => new(false, Array.Empty<MacApprovalSurface>(), Array.Empty<MacAxTarget>());

    public MacAccessibilitySnapshot Clone()
        => new(
            this.IsTrusted,
            this.Approvals.Select(approval => approval.Clone()).ToArray(),
            this.StopTargets.Select(target => target.Clone()).ToArray());

    public void Dispose()
    {
        foreach (var approval in this.Approvals)
        {
            approval.Dispose();
        }

        foreach (var target in this.StopTargets)
        {
            target.Dispose();
        }
    }
}
