namespace TaskGuide.Domain.Common;

/// <summary>
/// A positional record's synthesised <c>Equals</c> compares an <see cref="IReadOnlyList{T}"/> or
/// <see cref="IReadOnlyDictionary{TKey,TValue}"/> member by <b>reference</b> — both are reference
/// types — so two structurally identical records built from freshly-constructed collections
/// compare unequal (#115). These primitives, generalised from <c>TagSet</c>'s original private
/// helpers, are the two shapes every Domain record's collection member turns out to need:
/// order-sensitive (a sequence) or order-insensitive but duplicate-count-sensitive (a multiset).
/// A multiset's hash must fold order-free (an <c>unchecked</c> sum of element hashes), since
/// <see cref="HashCode.Add{T}(T)"/> folds sequentially and an order-insensitive <c>Equals</c>
/// beside an order-sensitive hash breaks the Equals/GetHashCode contract.
/// </summary>
internal static class StructuralEquality
{
    /// <summary>Order-sensitive comparison — position is part of the value.</summary>
    public static bool SequenceEqual<T>(IReadOnlyList<T>? a, IReadOnlyList<T>? b) where T : notnull
    {
        if (ReferenceEquals(a, b)) return true;
        if (a is null || b is null) return false;
        if (a.Count != b.Count) return false;
        for (var i = 0; i < a.Count; i++)
        {
            if (!a[i].Equals(b[i])) return false;
        }
        return true;
    }

    /// <summary>Order-sensitive hash — safe to fold with <see cref="HashCode.Add{T}(T)"/>.</summary>
    public static int SequenceHash<T>(IReadOnlyList<T> values) where T : notnull
    {
        var hash = new HashCode();
        foreach (var value in values) hash.Add(value);
        return hash.ToHashCode();
    }

    /// <summary>Order-insensitive, duplicate-count-sensitive comparison — a multiset, not a set.</summary>
    public static bool MultisetEqual<T>(IReadOnlyList<T>? a, IReadOnlyList<T>? b) where T : notnull
    {
        if (ReferenceEquals(a, b)) return true;
        if (a is null || b is null) return false;
        if (a.Count != b.Count) return false;
        var counts = new Dictionary<T, int>();
        foreach (var value in a) counts[value] = counts.GetValueOrDefault(value) + 1;
        foreach (var value in b)
        {
            if (!counts.TryGetValue(value, out var count) || count == 0) return false;
            counts[value] = count - 1;
        }
        return true;
    }

    /// <summary>An unchecked sum of element hashes is order-free and duplicate-count-sensitive.</summary>
    public static int MultisetHash<T>(IReadOnlyList<T> values) where T : notnull
    {
        var sum = 0;
        unchecked
        {
            foreach (var value in values) sum += value.GetHashCode();
        }
        return sum;
    }
}
