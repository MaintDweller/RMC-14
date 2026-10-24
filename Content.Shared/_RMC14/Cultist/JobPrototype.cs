using Robust.Shared.Prototypes;

// ReSharper disable CheckNamespace
namespace Content.Shared.Roles;
// ReSharper restore CheckNamespace

public sealed partial class JobPrototype
{
    /// <summary>
    /// Allows this survivor job to be selected for the roundstart cult.
    /// Each variant must opt in explicitly, including variants of civilian jobs.
    /// </summary>
    [DataField, NeverPushInheritance]
    public bool CanBeCultist;
}
