using System.Reflection;
using Xunit;

namespace MassiveDotNet.Rest.Tests;

/// <summary>
/// Enforces constitution rule 12: NodaTime is the SDK's temporal vocabulary. BCL date and time
/// types may appear only in non-public code, where a BCL API signature forces them.
/// </summary>
public sealed class TemporalTypeTests
{
    private static readonly HashSet<Type> Forbidden =
    [
        typeof(DateTime),
        typeof(DateTimeOffset),
        typeof(DateOnly),
        typeof(TimeOnly),
        typeof(TimeSpan),
    ];

    [Fact]
    public void PublicApiUsesNodaTimeForAllTemporalTypes()
    {
        Assembly[] shipped =
        [
            typeof(MassiveClientOptions).Assembly,
            typeof(MassiveRestClient).Assembly,
        ];

        List<string> violations = [];

        foreach (Assembly assembly in shipped)
        {
            foreach (Type type in assembly.GetExportedTypes())
            {
                Inspect(type, violations);
            }
        }

        Assert.True(
            violations.Count == 0,
            "BCL date and time types must not appear in the public API. Use NodaTime instead "
            + $"(Instant, LocalDate, Duration, ZonedDateTime).{Environment.NewLine}"
            + string.Join(Environment.NewLine, violations.Select(v => $"  {v}")));
    }

    private static void Inspect(Type type, List<string> violations)
    {
        const BindingFlags Flags =
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        foreach (PropertyInfo property in type.GetProperties(Flags))
        {
            if (IsForbidden(property.PropertyType))
            {
                violations.Add($"{type.FullName}.{property.Name} : {Name(property.PropertyType)}");
            }
        }

        foreach (FieldInfo field in type.GetFields(Flags))
        {
            if (IsForbidden(field.FieldType))
            {
                violations.Add($"{type.FullName}.{field.Name} : {Name(field.FieldType)}");
            }
        }

        foreach (MethodInfo method in type.GetMethods(Flags))
        {
            // Property accessors are covered above. Operators are deliberately NOT skipped:
            // an implicit conversion from a BCL type would reintroduce it into the surface.
            if (method.Name.StartsWith("get_", StringComparison.Ordinal)
                || method.Name.StartsWith("set_", StringComparison.Ordinal))
            {
                continue;
            }

            if (IsForbidden(method.ReturnType))
            {
                violations.Add($"{type.FullName}.{method.Name}() -> {Name(method.ReturnType)}");
            }

            foreach (ParameterInfo parameter in method.GetParameters())
            {
                if (IsForbidden(parameter.ParameterType))
                {
                    violations.Add($"{type.FullName}.{method.Name}({parameter.Name}: {Name(parameter.ParameterType)})");
                }
            }
        }

        foreach (ConstructorInfo constructor in type.GetConstructors(Flags))
        {
            foreach (ParameterInfo parameter in constructor.GetParameters())
            {
                if (IsForbidden(parameter.ParameterType))
                {
                    violations.Add($"{type.FullName}..ctor({parameter.Name}: {Name(parameter.ParameterType)})");
                }
            }
        }
    }

    private static bool IsForbidden(Type type)
    {
        Type target = Nullable.GetUnderlyingType(type) ?? type;

        if (target.IsArray)
        {
            target = target.GetElementType()!;
        }

        return Forbidden.Contains(Nullable.GetUnderlyingType(target) ?? target);
    }

    private static string Name(Type type) => Nullable.GetUnderlyingType(type)?.Name + "?" ?? type.Name;
}
