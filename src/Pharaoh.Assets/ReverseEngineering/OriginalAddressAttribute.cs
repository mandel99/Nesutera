namespace Pharaoh.Engine.ReverseEngineering;

/// <summary>Links a reimplemented member to its verified address in Pharaoh.exe 2.1.</summary>
[AttributeUsage(
    AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Method | AttributeTargets.Property | AttributeTargets.Constructor,
    AllowMultiple = true)]
public sealed class OriginalAddressAttribute(uint address, string ghidraName) : Attribute
{
    public uint Address { get; } = address;
    public string GhidraName { get; } = ghidraName;
}

/// <summary>Marks rewrite infrastructure that has no direct Pharaoh.exe counterpart.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Method | AttributeTargets.Property | AttributeTargets.Constructor)]
public sealed class RewriteOnlyAttribute(string reason) : Attribute
{
    public string Reason { get; } = reason;
}
