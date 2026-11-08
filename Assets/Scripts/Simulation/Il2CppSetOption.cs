using System;

namespace Unity.IL2CPP.CompilerServices
{
	// Lets a method or class opt out of the checks IL2CPP inserts into the C++ it generates
	// (Android and WebGL builds). IL2CPP recognises this attribute by its full name, so the
	// project declares it itself, as Unity's manual describes; Mono and .NET ignore it.
	//
	// The simulation uses it only in its innermost loops, where every array index comes from
	// tables the engine built for exactly those arrays, so the checks can never fail there.
	internal enum Option
	{
		NullChecks = 1,
		ArrayBoundsChecks = 2,
		DivideByZeroChecks = 3
	}

	[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Method | AttributeTargets.Property, Inherited = false, AllowMultiple = true)]
	internal sealed class Il2CppSetOptionAttribute : Attribute
	{
		public Option Option { get; }
		public object Value { get; }

		public Il2CppSetOptionAttribute(Option option, object value)
		{
			Option = option;
			Value = value;
		}
	}
}
