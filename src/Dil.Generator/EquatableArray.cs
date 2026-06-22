using System;
using System.Collections;
using System.Collections.Generic;

namespace Dil.Generator;

/// <summary>
/// An array wrapper with structural (element-wise) equality, so it can be cached by the
/// incremental generator pipeline. A plain array / <c>List</c> uses reference equality, which
/// would make every model instance look "changed" and defeat incremental caching.
/// </summary>
readonly struct EquatableArray<T> : IEquatable<EquatableArray<T>>, IEnumerable<T>
{
    readonly T[]? _array;

    public EquatableArray(T[]? array) => _array = array;

    public int Count => _array?.Length ?? 0;

    public bool Equals(EquatableArray<T> other)
    {
        var a = _array ?? [];
        var b = other._array ?? [];
        if (a.Length != b.Length)
        {
            return false;
        }

        var comparer = EqualityComparer<T>.Default;
        for (var i = 0; i < a.Length; i++)
        {
            if (!comparer.Equals(a[i], b[i]))
            {
                return false;
            }
        }

        return true;
    }

    public override bool Equals(object? obj) => obj is EquatableArray<T> other && Equals(other);

    public override int GetHashCode()
    {
        if (_array is null)
        {
            return 0;
        }

        unchecked
        {
            var hash = 17;
            foreach (var item in _array)
            {
                hash = (hash * 31) + (item?.GetHashCode() ?? 0);
            }

            return hash;
        }
    }

    public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)(_array ?? [])).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
