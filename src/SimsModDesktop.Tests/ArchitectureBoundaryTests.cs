using System.Reflection;
using SimsModDesktop.Application.Modules;

namespace SimsModDesktop.Tests;

public sealed class ArchitectureBoundaryTests
{
    [Fact]
    public void ApplicationLayer_DoesNotDependOnPanelViewModels()
    {
        var assembly = typeof(IActionModule).Assembly;
        var applicationTypes = assembly
            .GetTypes()
            .Where(type => type.Namespace?.StartsWith("SimsModDesktop.Application", StringComparison.Ordinal) == true)
            .ToArray();

        var violations = new List<string>();
        foreach (var type in applicationTypes)
        {
            ValidateTypeReference(type, type.FullName ?? type.Name, type, violations);

            foreach (var constructor in type.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                foreach (var parameter in constructor.GetParameters())
                {
                    ValidateTypeReference(type, $"{constructor.Name}({parameter.Name})", parameter.ParameterType, violations);
                }
            }

            foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                ValidateTypeReference(type, field.Name, field.FieldType, violations);
            }

            foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                ValidateTypeReference(type, property.Name, property.PropertyType, violations);
            }

            foreach (var method in type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                ValidateTypeReference(type, method.Name, method.ReturnType, violations);
                foreach (var parameter in method.GetParameters())
                {
                    ValidateTypeReference(type, $"{method.Name}({parameter.Name})", parameter.ParameterType, violations);
                }
            }
        }

        Assert.True(violations.Count == 0, "Application layer references panel ViewModels:" + Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    private static void ValidateTypeReference(Type owner, string memberName, Type candidate, ICollection<string> violations)
    {
        foreach (var flattened in FlattenTypes(candidate))
        {
            if (flattened.Namespace is null)
            {
                continue;
            }

            if (!flattened.Namespace.StartsWith("SimsModDesktop.ViewModels.Panels", StringComparison.Ordinal))
            {
                continue;
            }

            violations.Add($"{owner.FullName} -> {memberName} -> {flattened.FullName}");
        }
    }

    private static IEnumerable<Type> FlattenTypes(Type type)
    {
        if (type.IsByRef || type.IsPointer || type.IsArray)
        {
            var element = type.GetElementType();
            if (element is not null)
            {
                foreach (var nested in FlattenTypes(element))
                {
                    yield return nested;
                }
            }

            yield break;
        }

        yield return type;

        if (!type.IsGenericType)
        {
            yield break;
        }

        foreach (var argument in type.GetGenericArguments())
        {
            foreach (var nested in FlattenTypes(argument))
            {
                yield return nested;
            }
        }
    }
}
