// Unity's Mono skips JIT accessibility checks for assemblies that request SkipVerification.
// Every published Valheim mod compiled against publicized assemblies carries these (the
// publicizer MSBuild package emits them); without them, private member access throws
// FieldAccessException / MethodAccessException at runtime.
using System.Security;
using System.Security.Permissions;

#pragma warning disable CS0618
[assembly: SecurityPermission(SecurityAction.RequestMinimum, SkipVerification = true)]
#pragma warning restore CS0618
[module: UnverifiableCode]

