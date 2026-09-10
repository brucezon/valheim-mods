using Mono.Cecil;

// usage: version-stamper <in.dll> <out.dll> <newVersion>
// Rewrites the version argument of every [BepInPlugin(guid, name, version)] attribute in the
// assembly. Only the BepInEx plugin version changes; mod-internal version strings (ServerSync
// checks, RPC version handshakes) are left exactly as they were.
var inPath = args[0];
var outPath = args[1];
var newVersion = args[2];

var resolver = new DefaultAssemblyResolver();
resolver.AddSearchDirectory(Path.GetDirectoryName(Path.GetFullPath(inPath)));
// Game + BepInEx assemblies, so plugins that reference them (most do) can be re-written.
var gamePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "refs", "1.0", "gamepath"));
foreach (var extra in args.Skip(3).Concat(new[] {
    Path.Combine(gamePath, "valheim_Data", "Managed"),
    Path.Combine(gamePath, "valheim_Data", "Managed", "publicized_assemblies"),
    Path.Combine(gamePath, "BepInEx", "core") }))
{
    if (Directory.Exists(extra)) resolver.AddSearchDirectory(extra);
}
var asm = AssemblyDefinition.ReadAssembly(inPath, new ReaderParameters { AssemblyResolver = resolver });

int stamped = 0;
foreach (var type in asm.MainModule.GetTypes())
{
    foreach (var attr in type.CustomAttributes)
    {
        if (attr.AttributeType.Name != "BepInPlugin" || attr.ConstructorArguments.Count != 3) continue;
        var old = attr.ConstructorArguments[2].Value;
        attr.ConstructorArguments[2] = new CustomAttributeArgument(asm.MainModule.TypeSystem.String, newVersion);
        Console.WriteLine(type.FullName + ": BepInPlugin version " + old + " -> " + newVersion);
        stamped++;
    }
}
if (stamped == 0) throw new Exception("no [BepInPlugin] attribute found");
asm.Write(outPath);
