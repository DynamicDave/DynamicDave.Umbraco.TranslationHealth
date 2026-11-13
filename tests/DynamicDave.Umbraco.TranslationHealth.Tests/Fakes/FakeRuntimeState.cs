using System.Reflection;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Services;

namespace DynamicDave.Umbraco.TranslationHealth.Tests.Fakes;

/// <summary>
/// An <see cref="IRuntimeState"/> that only knows its <see cref="Level"/>; the interface differs between Umbraco 17 and 18,
/// so a proxy is used instead of an implementation. Every other member returns its default.
/// </summary>
public class FakeRuntimeState : DispatchProxy
{
    public RuntimeLevel Level { get; set; } = RuntimeLevel.Run;

    public static (IRuntimeState State, FakeRuntimeState Control) Create()
    {
        var state = Create<IRuntimeState, FakeRuntimeState>();
        return (state, (FakeRuntimeState)(object)state);
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => targetMethod?.Name == "get_Level"
        ? Level
        : targetMethod is { ReturnType.IsValueType: true } && targetMethod.ReturnType != typeof(void)
            ? Activator.CreateInstance(targetMethod.ReturnType)
            : null;
}
