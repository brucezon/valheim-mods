// Required at runtime when compiling against publicized game assemblies: Mono enforces member
// accessibility unless the consuming assembly declares it ignores access checks for the target.
using System.Runtime.CompilerServices;

[assembly: IgnoresAccessChecksTo("assembly_valheim")]
[assembly: IgnoresAccessChecksTo("assembly_utils")]
[assembly: IgnoresAccessChecksTo("assembly_guiutils")]

namespace System.Runtime.CompilerServices
{
    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
    internal sealed class IgnoresAccessChecksToAttribute : Attribute
    {
        public IgnoresAccessChecksToAttribute(string assemblyName) { AssemblyName = assemblyName; }
        public string AssemblyName { get; }
    }
}
