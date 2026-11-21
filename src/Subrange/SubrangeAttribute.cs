using System.Numerics;

namespace Subranges;

/// <summary>
/// Turns a <c>sealed partial record</c> into a subrange of <typeparamref name="TValue"/>: a number
/// that is always within <paramref name="min"/> and <paramref name="max"/> (inclusive). The
/// generated private constructor is the only way to create one, and it is only reached through
/// parsing. A value outside the bounds is rejected, never clamped.
/// </summary>
/// <remarks>
/// The bounds are doubles because an attribute can't take a <typeparamref name="TValue"/>
/// argument. The generator rejects bounds that <typeparamref name="TValue"/> can't represent.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class SubrangeAttribute<TValue>(double min, double max) : Attribute
    where TValue : struct, INumber<TValue>
{
    public double Min { get; } = min;

    public double Max { get; } = max;
}
