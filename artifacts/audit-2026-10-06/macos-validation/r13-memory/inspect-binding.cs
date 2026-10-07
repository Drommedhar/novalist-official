using Mono.Cecil;
using var assembly=AssemblyDefinition.ReadAssembly(args[0]);
foreach(var typeName in new[]{"Foundation.NSString","Foundation.NSObject","CoreFoundation.CFString"}) {
 var type=assembly.MainModule.GetType(typeName);
 foreach(var method in type.Methods.Where(m=>
   typeName=="Foundation.NSString" ? m.Name==".ctor"&&m.Parameters.Count==1&&m.Parameters[0].ParameterType.FullName=="System.String" :
   typeName=="CoreFoundation.CFString" ? m.Name=="CreateNative"&&m.Parameters.Count==1 :
   m.Name==".ctor"&&m.Parameters.Count>0&&m.Parameters[0].ParameterType.Name=="NativeHandle" || m.Name=="InitializeObject" || m.Name=="CreateManagedRef")) {
  Console.WriteLine(method.FullName);
  foreach(var instruction in method.Body.Instructions)Console.WriteLine(instruction);
 }
}
