#if NET5_0
namespace System.Diagnostics.CodeAnalysis;

[AttributeUsage(AttributeTargets.Constructor)]
public sealed class SetsRequiredMembersAttribute : Attribute;

#endif