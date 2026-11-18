namespace CordisSharp.Core;

/// <summary>A binding: the provider's value plus its identity. The uid is fresh per
/// Provide call and never reused, so two equal values are still distinguishable.</summary>
internal readonly record struct Binding(object Value, long Uid);
