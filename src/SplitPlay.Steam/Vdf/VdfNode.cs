using System;
using System.Collections.Generic;

namespace SplitPlay.Steam.Vdf;

/// <summary>
/// A node in a parsed Valve KeyValues (VDF/ACF) document. A node is either a
/// container of child nodes (a "{ }" block) or a leaf with a string value.
/// </summary>
public sealed class VdfNode
{
    /// <summary>Child blocks keyed by name (case-insensitive).</summary>
    public Dictionary<string, VdfNode> Children { get; } =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Scalar key/value pairs keyed by name (case-insensitive).</summary>
    public Dictionary<string, string> Values { get; } =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Returns the scalar value for a key, or null if absent.</summary>
    public string? GetValue(string key) =>
        Values.TryGetValue(key, out var value) ? value : null;

    /// <summary>Returns the child block for a key, or null if absent.</summary>
    public VdfNode? GetChild(string key) =>
        Children.TryGetValue(key, out var child) ? child : null;
}
