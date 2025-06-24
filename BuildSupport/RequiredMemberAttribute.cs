#if NET5_0
namespace System.Runtime.CompilerServices;

[AttributeUsage(
    AttributeTargets.Class | AttributeTargets.Field | AttributeTargets.Property |
    AttributeTargets.Struct, Inherited = false)]
public sealed class RequiredMemberAttribute : Attribute;

#endif