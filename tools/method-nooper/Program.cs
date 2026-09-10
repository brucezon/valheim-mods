using Mono.Cecil;
using Mono.Cecil.Cil;

// usage: method-nooper <in.dll> <out.dll> <TypeFullName> <MethodName>
// Replaces the body of a void method with a bare return, leaving everything else untouched.
var inPath = args[0];
var outPath = args[1];
var typeName = args[2];
var methodName = args[3];

var resolver = new DefaultAssemblyResolver();
resolver.AddSearchDirectory(Path.GetDirectoryName(Path.GetFullPath(inPath)));
var asm = AssemblyDefinition.ReadAssembly(inPath, new ReaderParameters { AssemblyResolver = resolver });

var type = asm.MainModule.GetTypes().FirstOrDefault(t => t.FullName == typeName)
           ?? throw new Exception("type not found: " + typeName);
var method = type.Methods.FirstOrDefault(m => m.Name == methodName && m.ReturnType.FullName == "System.Void")
             ?? throw new Exception("void method not found: " + typeName + "::" + methodName);

int before = method.Body.Instructions.Count;
method.Body.Instructions.Clear();
method.Body.Variables.Clear();
method.Body.ExceptionHandlers.Clear();
method.Body.GetILProcessor().Emit(OpCodes.Ret);
asm.Write(outPath);
Console.WriteLine(typeName + "::" + methodName + ": " + before + " instructions -> ret");
