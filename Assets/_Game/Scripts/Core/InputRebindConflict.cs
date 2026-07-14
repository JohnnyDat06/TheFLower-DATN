/// <summary>
/// Public conflict data passed from the rebind service to UI.
/// </summary>
public readonly struct InputRebindConflict
{
    public InputRebindConflict(
        string actionName,
        string conflictActionName,
        InputBindingTarget target,
        string bindingDisplayName)
    {
        ActionName = actionName;
        ConflictActionName = conflictActionName;
        Target = target;
        BindingDisplayName = bindingDisplayName;
    }

    public string ActionName { get; }
    public string ConflictActionName { get; }
    public InputBindingTarget Target { get; }
    public string BindingDisplayName { get; }
}
