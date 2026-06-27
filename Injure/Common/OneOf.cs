// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Injure.Common;

public static class OneOf {
	public readonly record struct FirstCase<T>(T Value);
	public readonly record struct SecondCase<T>(T Value);
	public static FirstCase<T> First<T>(T value) => new(value);
	public static SecondCase<T> Second<T>(T value) => new(value);
}

public readonly struct OneOf<T1, T2> : IEquatable<OneOf<T1, T2>> {
	public enum OneOfCase {
		Uninitialized,
		First,
		Second,
	}

	private readonly OneOfCase @case;
	private readonly T1 first;
	private readonly T2 second;

	private OneOf(OneOfCase @case, T1 first, T2 second) {
		this.@case = @case;
		this.first = first;
		this.second = second;
	}

	public static OneOf<T1, T2> FromFirst(T1 value) => new(OneOfCase.First, value, default!);
	public static OneOf<T1, T2> FromSecond(T2 value) => new(OneOfCase.Second, default!, value);
	public static implicit operator OneOf<T1, T2>(OneOf.FirstCase<T1> value) => FromFirst(value.Value);
	public static implicit operator OneOf<T1, T2>(OneOf.SecondCase<T2> value) => FromSecond(value.Value);

	public OneOfCase Case => @case;
	public bool IsInitialized => @case is OneOfCase.First or OneOfCase.Second;
	public bool IsFirst => @case == OneOfCase.First;
	public bool IsSecond => @case == OneOfCase.Second;

	public T1 First => @case == OneOfCase.First ? first : throw new InvalidOperationException($"the union contains {@case}, not First");
	public T2 Second => @case == OneOfCase.Second ? second : throw new InvalidOperationException($"the union contains {@case}, not Second");

	public bool TryGetFirst([MaybeNullWhen(false)] out T1 value) {
		if (@case == OneOfCase.First) {
			value = first;
			return true;
		}
		value = default!;
		return false;
	}

	public bool TryGetSecond([MaybeNullWhen(false)] out T2 value) {
		if (@case == OneOfCase.Second) {
			value = second;
			return true;
		}
		value = default!;
		return false;
	}

	public TResult Match<TResult>(Func<T1, TResult> first, Func<T2, TResult> second) {
		ArgumentNullException.ThrowIfNull(first);
		ArgumentNullException.ThrowIfNull(second);
		return @case switch {
			OneOfCase.First => first(this.first),
			OneOfCase.Second => second(this.second),
			_ => throw new InvalidOperationException("the union is uninitialized"),
		};
	}

	public TResult MatchWithUninit<TResult>(Func<T1, TResult> first, Func<T2, TResult> second, TResult uninit) {
		ArgumentNullException.ThrowIfNull(first);
		ArgumentNullException.ThrowIfNull(second);
		return @case switch {
			OneOfCase.First => first(this.first),
			OneOfCase.Second => second(this.second),
			_ => uninit,
		};
	}

	public TResult MatchWithUninit<TResult>(Func<T1, TResult> first, Func<T2, TResult> second, Func<TResult> uninit) {
		ArgumentNullException.ThrowIfNull(first);
		ArgumentNullException.ThrowIfNull(second);
		return @case switch {
			OneOfCase.First => first(this.first),
			OneOfCase.Second => second(this.second),
			_ => uninit(),
		};
	}

	public void Switch(Action<T1> first, Action<T2> second) {
		ArgumentNullException.ThrowIfNull(first);
		ArgumentNullException.ThrowIfNull(second);
		switch (@case) {
		case OneOfCase.First:
			first(this.first);
			return;
		case OneOfCase.Second:
			second(this.second);
			return;
		default:
			throw new InvalidOperationException("the union is uninitialized");
		}
	}

	public void SwitchWithUninit(Action<T1> first, Action<T2> second, Action uninit) {
		ArgumentNullException.ThrowIfNull(first);
		ArgumentNullException.ThrowIfNull(second);
		switch (@case) {
		case OneOfCase.First:
			first(this.first);
			return;
		case OneOfCase.Second:
			second(this.second);
			return;
		default:
			uninit();
			return;
		}
	}

	public bool Equals(OneOf<T1, T2> other) {
		if (@case != other.@case)
			return false;
		return @case switch {
			OneOfCase.Uninitialized => true,
			OneOfCase.First => EqualityComparer<T1>.Default.Equals(first, other.first),
			OneOfCase.Second => EqualityComparer<T2>.Default.Equals(second, other.second),
			_ => false,
		};
	}
	public override bool Equals(object? obj) => obj is OneOf<T1, T2> other && Equals(other);
	public override int GetHashCode() => @case switch {
		OneOfCase.Uninitialized => 0,
		OneOfCase.First => HashCode.Combine(1, first),
		OneOfCase.Second => HashCode.Combine(2, second),
		_ => (int)@case,
	};
	public static bool operator ==(OneOf<T1, T2> left, OneOf<T1, T2> right) => left.Equals(right);
	public static bool operator !=(OneOf<T1, T2> left, OneOf<T1, T2> right) => !left.Equals(right);

	public override string ToString() => @case switch {
		OneOfCase.First when first is null => "First(null)",
		OneOfCase.First => $"First({first})",
		OneOfCase.Second when second is null => "Second(null)",
		OneOfCase.Second => $"Second({second})",
		_ => "<uninitialized>",
	};
}
