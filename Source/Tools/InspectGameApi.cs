using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;

internal static class InspectGameApi
{
    private static string managed;
    private static string[] searchDirectories;

    private static int Main(string[] args)
    {
        if (args.Length < 2) return 64;
        managed = args[0];
        searchDirectories = args.Skip(2).Concat(new[] { managed }).Distinct().ToArray();
        var filter = new Regex(args[1], RegexOptions.IgnoreCase);
        AppDomain.CurrentDomain.AssemblyResolve += Resolve;
        foreach (var file in Directory.GetFiles(managed, "*.dll"))
        {
            Assembly assembly;
            try { assembly = Assembly.LoadFrom(file); }
            catch { continue; }
            Type[] types;
            try { types = assembly.GetTypes(); }
            catch (ReflectionTypeLoadException exception) { types = exception.Types.Where(t => t != null).ToArray(); }
            foreach (var type in types.Where(t => filter.IsMatch(t.FullName ?? t.Name)).OrderBy(t => t.FullName))
            {
                try
                {
                    Console.WriteLine("TYPE {0} [{1}]", type.FullName, assembly.GetName().Name);
                    const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic |
                        BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
                    foreach (var field in type.GetFields(flags))
                        Console.WriteLine("  FIELD {0} {1}", Name(field.FieldType), field.Name);
                    foreach (var property in type.GetProperties(flags))
                        Console.WriteLine("  PROP {0} {1}", Name(property.PropertyType), property.Name);
                    foreach (var method in type.GetMethods(flags))
                        Console.WriteLine("  METHOD {0} {1}({2})", Name(method.ReturnType), method.Name,
                            string.Join(", ", method.GetParameters().Select(p => Name(p.ParameterType) + " " + p.Name).ToArray()));
                }
                catch (Exception exception)
                {
                    Console.WriteLine("  ERROR {0}", exception.GetType().Name);
                }
            }
        }
        return 0;
    }

    private static string Name(Type type)
    {
        return type == null ? "?" : (type.FullName ?? type.Name);
    }

    private static Assembly Resolve(object sender, ResolveEventArgs args)
    {
        var requested = new AssemblyName(args.Name);
        if (requested.Name.EndsWith(".resources", StringComparison.OrdinalIgnoreCase)) return null;
        foreach (var directory in searchDirectories)
        {
            var path = Path.Combine(directory, requested.Name + ".dll");
            if (File.Exists(path)) return Assembly.LoadFrom(path);
        }
        return null;
    }
}
