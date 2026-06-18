// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System;
using System.Security.Cryptography;
using System.Text;

using Mono.Cecil;

namespace Injure.Weaver.Patching;

public static class SignatureHasher {
	public static string Hash(MethodDefinition method) {
		StringBuilder sb = new();
		sb.Append(method.DeclaringType.FullName).Append('|');
		sb.Append(method.Name).Append('|');
		sb.Append(method.HasThis ? "instance" : "static").Append('|');
		sb.Append(method.ReturnType.FullName).Append('|');
		sb.Append(method.GenericParameters.Count).Append('|');
		foreach (ParameterDefinition parameter in method.Parameters)
			sb.Append(parameter.ParameterType.FullName).Append(';');
		byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
		return Convert.ToHexString(bytes);
	}
}
