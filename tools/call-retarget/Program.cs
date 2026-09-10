using Mono.Cecil;
using Mono.Cecil.Cil;

// usage: call-retarget <in.dll> <out.dll> <gameManagedDir>
// Retargets call sites compiled against pre-1.0 Valheim overloads to their 1.0 equivalents by
// pushing the new trailing default arguments and swapping the method reference:
//   Character.Message(type, msg, amount, icon)      -> Message(type, msg, amount, icon, log:false)
//   Inventory.Changed()                              -> Changed(success:false, cheatedStateChanged:false)
//   ZRoutedRpc.Everybody (static field, now const)   -> ldc.i8 0
var inPath = args[0];
var outPath = args[1];
var managedDir = args[2];

var resolver = new DefaultAssemblyResolver();
resolver.AddSearchDirectory(managedDir);
resolver.AddSearchDirectory(Path.Combine(managedDir, "publicized_assemblies"));
resolver.AddSearchDirectory(Path.GetFullPath(Path.Combine(managedDir, "..", "..", "BepInEx", "core")));
resolver.AddSearchDirectory(Path.GetDirectoryName(Path.GetFullPath(inPath)));
var asm = AssemblyDefinition.ReadAssembly(inPath, new ReaderParameters { AssemblyResolver = resolver });
var module = asm.MainModule;

var valheim = resolver.Resolve(new AssemblyNameReference("assembly_valheim", null)).MainModule;
var characterType = valheim.GetType("Character");
var inventoryType = valheim.GetType("Inventory");
var newMessage = characterType.Methods.First(m => m.Name == "Message" && m.Parameters.Count == 5);
var newChanged = inventoryType.Methods.First(m => m.Name == "Changed" && m.Parameters.Count == 2);
var newMessageRef = module.ImportReference(newMessage);
var newChangedRef = module.ImportReference(newChanged);

int fixedMessage = 0, fixedChanged = 0, fixedEverybody = 0;
foreach (var type in module.GetTypes())
foreach (var method in type.Methods)
{
    if (!method.HasBody) continue;
    var il = method.Body.GetILProcessor();
    foreach (var ins in method.Body.Instructions.ToArray())
    {
        if ((ins.OpCode == OpCodes.Call || ins.OpCode == OpCodes.Callvirt) && ins.Operand is MethodReference mr)
        {
            if (mr.Name == "Message" && mr.Parameters.Count == 4 && IsCharacterOrDerived(mr.DeclaringType))
            {
                // stack currently holds: this, type, msg, amount, icon  -> add log:false, then call the 5-arg overload
                il.InsertBefore(ins, il.Create(OpCodes.Ldc_I4_0));
                ins.Operand = newMessageRef;
                fixedMessage++;
                Console.WriteLine("  Message  " + type.FullName + "::" + method.Name);
            }
            else if (mr.Name == "Changed" && mr.Parameters.Count == 0 && mr.DeclaringType.Name == "Inventory")
            {
                il.InsertBefore(ins, il.Create(OpCodes.Ldc_I4_0));
                il.InsertBefore(ins, il.Create(OpCodes.Ldc_I4_0));
                ins.Operand = newChangedRef;
                fixedChanged++;
                Console.WriteLine("  Changed  " + type.FullName + "::" + method.Name);
            }
        }
        else if (ins.OpCode == OpCodes.Ldsfld && ins.Operand is FieldReference f
                 && f.Name == "Everybody" && f.DeclaringType.Name == "ZRoutedRpc")
        {
            il.Replace(ins, il.Create(OpCodes.Ldc_I8, 0L));
            fixedEverybody++;
            Console.WriteLine("  Everybody " + type.FullName + "::" + method.Name);
        }
    }
}
asm.Write(outPath);
Console.WriteLine(Path.GetFileName(inPath) + ": Message=" + fixedMessage + " Changed=" + fixedChanged + " Everybody=" + fixedEverybody);

static bool IsCharacterOrDerived(TypeReference t)
{
    // Character, Humanoid, Player all resolve to Character.Message (virtual) — accept the family.
    return t.Name == "Character" || t.Name == "Humanoid" || t.Name == "Player";
}
