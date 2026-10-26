using Mono.Cecil;
using Mono.Cecil.Cil;
using NUnit.Framework;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor.Compilation;
using UnityEngine.TestTools;

namespace MikanXR.Unity.Editor.Tests
{
	// A sandboxed host (a Warudo plugin mod) compiles the runtime from source without the
	// host-optional folders, and rejects the result if its IL references anything the sandbox
	// forbids. This compiles that same subset on its own, which proves the optional folders can be
	// left out, then scans it the way the sandbox does.
	public class MikanSandboxCompatibilityTests
	{
		private const string k_packageRuntimePath = "Packages/com.mikanxr.unity/Runtime";

		// Folders whose code needs what a sandbox forbids, and which such a host leaves out
		private static readonly string[] k_hostOptionalFolders = { "Bindings/Native/", "Scripts/FileSystem/" };

		// The rules of the Warudo 0.15.2 mod tools (ModToolsSecurityRestrictions in the mod tool
		// package). A namespace entry matches exactly, except System.IO, which the tools write as
		// System.IO.* and which also rejects System.IO itself.
		private static readonly HashSet<string> k_deniedAssemblies =
			new HashSet<string> { "UnityEditor", "Mono.Cecil", "UMod-ModTools", "Acornima", "Jint" };

		private static readonly HashSet<string> k_deniedNamespaces = new HashSet<string>
		{
			"System.Reflection",
			"System.Reflection.Emit",
			"Vexe.Fast.Reflection",
			"System.Runtime.InteropServices",
			"RoslynCSharp",
			"UMod.AssetSharing",
			"UMod.Bridge",
			"UMod.Debugging",
			"UMod.Moddable",
			"UMod.Moddable.Providers",
			"UMod.Scripting",
			"UMod.Scripting.Runtime",
			"UMod.Settings"
		};

		private static readonly string[] k_deniedNamespacePrefixes = { "System.IO", "RoslynCSharp" };

		private static readonly HashSet<string> k_deniedTypes = new HashSet<string>
		{
			"System.AppDomain",
			"System.Threading.Process",
			"System.Diagnostics.Process",
			"UMod.Mod"
		};

		private static readonly HashSet<string> k_deniedMembers = new HashSet<string>
		{
			"UnityEngine.Application.Quit",
			"UMod.ModHost.AllLoadedModHosts",
			"UMod.ModHost.AllModHosts"
		};

		[UnityTest]
		public IEnumerator RuntimeWithoutHostOptionalFoldersPassesSandboxRules()
		{
			string runtimeRoot = Path.GetFullPath(k_packageRuntimePath).Replace('\\', '/');
			string[] scripts = Directory.GetFiles(runtimeRoot, "*.cs", SearchOption.AllDirectories)
				.Select(path => path.Replace('\\', '/'))
				.Where(path => !k_hostOptionalFolders.Any(folder => path.Substring(runtimeRoot.Length + 1).StartsWith(folder)))
				.ToArray();
			Assert.IsNotEmpty(scripts, "No runtime scripts found under " + runtimeRoot);

			string outputDir = Path.Combine(Path.GetFullPath("Temp"), "MikanSandboxCompatibility");
			Directory.CreateDirectory(outputDir);
			string outputPath = Path.Combine(outputDir, "MikanXR.Unity.Sandboxed.dll");

			// Only what a mod host gives the runtime: the engine, KlakSpout and Newtonsoft
			var builder = new AssemblyBuilder(outputPath, scripts)
			{
				flags = AssemblyBuilderFlags.None,
				referencesOptions = ReferencesOptions.UseEngineModules,
				additionalReferences = new[] { FindAssemblyPath("Klak.Spout.Runtime"), FindPrecompiledPath("Newtonsoft.Json.dll") },
				excludeReferences = CompilationPipeline.GetAssemblies(AssembliesType.Player)
					.Where(assembly => assembly.name != "Klak.Spout.Runtime")
					.Select(assembly => assembly.outputPath)
					.ToArray()
			};

			bool finished = false;
			var errors = new List<string>();
			builder.buildFinished += (path, messages) =>
			{
				errors.AddRange(messages.Where(m => m.type == CompilerMessageType.Error).Select(m => m.message));
				finished = true;
			};

			Assert.IsTrue(builder.Build(), "The runtime subset could not start compiling");
			while (!finished)
			{
				yield return null;
			}

			Assert.IsEmpty(errors, "The runtime without its host-optional folders does not compile:\n" + string.Join("\n", errors));

			List<string> violations = ScanAssembly(outputPath);
			Assert.IsEmpty(violations, "References a sandboxed host rejects:\n" + string.Join("\n", violations));
		}

		private static List<string> ScanAssembly(string assemblyPath)
		{
			var violations = new SortedSet<string>();

			using (ModuleDefinition module = ModuleDefinition.ReadModule(assemblyPath))
			{
				foreach (AssemblyNameReference reference in module.AssemblyReferences)
				{
					if (k_deniedAssemblies.Contains(reference.Name))
						violations.Add($"assembly reference {reference.Name}");
				}

				foreach (TypeDefinition type in module.GetTypes())
				{
					CheckType(type.BaseType, type.FullName, violations);
					foreach (InterfaceImplementation implemented in type.Interfaces)
						CheckType(implemented.InterfaceType, type.FullName, violations);
					CheckAttributes(type.CustomAttributes, type.FullName, violations);

					foreach (FieldDefinition field in type.Fields)
					{
						string site = type.FullName + "." + field.Name;
						CheckType(field.FieldType, site, violations);
						CheckAttributes(field.CustomAttributes, site, violations);
					}

					foreach (MethodDefinition method in type.Methods)
					{
						string site = type.FullName + "." + method.Name;
						if (method.IsPInvokeImpl)
							violations.Add($"{site}: P/Invoke");

						CheckType(method.ReturnType, site, violations);
						foreach (ParameterDefinition parameter in method.Parameters)
							CheckType(parameter.ParameterType, site, violations);
						CheckAttributes(method.CustomAttributes, site, violations);

						if (!method.HasBody)
							continue;

						foreach (VariableDefinition variable in method.Body.Variables)
							CheckType(variable.VariableType, site, violations);

						foreach (Instruction instruction in method.Body.Instructions)
						{
							if (instruction.Operand is MemberReference member)
								CheckMember(member, site, violations);
						}
					}
				}
			}

			return violations.ToList();
		}

		private static void CheckAttributes(IEnumerable<CustomAttribute> attributes, string site, SortedSet<string> violations)
		{
			foreach (CustomAttribute attribute in attributes)
				CheckType(attribute.AttributeType, site, violations);
		}

		private static void CheckMember(MemberReference member, string site, SortedSet<string> violations)
		{
			if (member is TypeReference type)
			{
				CheckType(type, site, violations);
				return;
			}

			TypeReference declaringType = member.DeclaringType;
			if (declaringType == null)
				return;

			CheckType(declaringType, site, violations);

			string memberName = member.Name.StartsWith("get_") || member.Name.StartsWith("set_") ? member.Name.Substring(4) : member.Name;
			string memberPath = OutermostNamespace(declaringType) + "." + TypeNameWithoutNamespace(declaringType.GetElementType()) + "." + memberName;
			if (k_deniedMembers.Contains(memberPath))
				violations.Add($"{site}: {memberPath}");

			if (member is MethodReference method)
			{
				CheckType(method.ReturnType, site, violations);
				foreach (ParameterDefinition parameter in method.Parameters)
					CheckType(parameter.ParameterType, site, violations);
				if (method is GenericInstanceMethod genericMethod)
				{
					foreach (TypeReference argument in genericMethod.GenericArguments)
						CheckType(argument, site, violations);
				}
			}
			else if (member is FieldReference field)
			{
				CheckType(field.FieldType, site, violations);
			}
		}

		private static void CheckType(TypeReference type, string site, SortedSet<string> violations)
		{
			if (type == null || type is GenericParameter)
				return;

			if (type is GenericInstanceType genericInstance)
			{
				foreach (TypeReference argument in genericInstance.GenericArguments)
					CheckType(argument, site, violations);
			}

			TypeReference elementType = type.GetElementType();
			if (elementType != type)
			{
				CheckType(elementType, site, violations);
				return;
			}

			string ns = OutermostNamespace(type);
			string fullName = string.IsNullOrEmpty(ns) ? type.Name : ns + "." + TypeNameWithoutNamespace(type);

			if (k_deniedNamespaces.Contains(ns) ||
				k_deniedNamespacePrefixes.Any(prefix => ns == prefix || ns.StartsWith(prefix + ".")))
			{
				violations.Add($"{site}: {fullName} (namespace {ns})");
			}
			else if (k_deniedTypes.Contains(fullName))
			{
				violations.Add($"{site}: {fullName}");
			}
		}

		// A nested type carries no namespace of its own, so its outermost declaring type's is used
		private static string OutermostNamespace(TypeReference type)
		{
			while (type.DeclaringType != null)
				type = type.DeclaringType;
			return type.Namespace ?? "";
		}

		private static string TypeNameWithoutNamespace(TypeReference type)
		{
			return type.DeclaringType != null ? TypeNameWithoutNamespace(type.DeclaringType) + "/" + type.Name : type.Name;
		}

		private static string FindAssemblyPath(string assemblyName)
		{
			UnityEditor.Compilation.Assembly assembly = CompilationPipeline.GetAssemblies(AssembliesType.Player)
				.FirstOrDefault(candidate => candidate.name == assemblyName);
			Assert.IsNotNull(assembly, "Assembly " + assemblyName + " is not in the project");
			return Path.GetFullPath(assembly.outputPath);
		}

		private static string FindPrecompiledPath(string fileName)
		{
			string path = CompilationPipeline.GetPrecompiledAssemblyPaths(CompilationPipeline.PrecompiledAssemblySources.All)
				.FirstOrDefault(candidate => Path.GetFileName(candidate) == fileName);
			Assert.IsNotNull(path, fileName + " is not in the project");
			return path;
		}
	}
}
