using Mono.Cecil;
using Mono.Cecil.Cil;

// usage: everybody-patcher <in.dll> <out.dll>
// Rewrites every `ldsfld ZRoutedRpc::Everybody` (a static field in <=0.221.12, a const in 1.0.7)
// into `ldc.i8 0`, which is exactly what the C# compiler emits when compiled against 1.0.
var inPath = args[0]; var outPath = args[1];
var resolver = new DefaultAssemblyResolver();
resolver.AddSearchDirectory(Path.GetDirectoryName(Path.GetFullPath(inPath)));
var asm = AssemblyDefinition.ReadAssembly(inPath, new ReaderParameters { AssemblyResolver = resolver, ReadWrite = false });
int patched = 0, scanned = 0;
foreach (var type in asm.MainModule.GetTypes())
foreach (var method in type.Methods)
{
    if (!method.HasBody) continue;
    scanned++;
    var il = method.Body.GetILProcessor();
    foreach (var ins in method.Body.Instructions.ToArray())
    {
        if (ins.OpCode == OpCodes.Ldsfld && ins.Operand is FieldReference f
            && f.Name == "Everybody" && f.DeclaringType.Name == "ZRoutedRpc")
        {
            il.Replace(ins, il.Create(OpCodes.Ldc_I8, 0L));
            patched++;
            Console.WriteLine($"  patched {type.FullName}::{method.Name}");
        }
    }
}
asm.Write(outPath);
Console.WriteLine($"{Path.GetFileName(inPath)} -> {Path.GetFileName(outPath)}: {patched} site(s) patched ({scanned} methods scanned)");
