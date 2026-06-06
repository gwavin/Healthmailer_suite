using System.Collections;
using System.Reflection;
using System.Text.RegularExpressions;

namespace PrintRxerV3.Tests;

[AttributeUsage(AttributeTargets.Method)]
internal sealed class TestAttribute : Attribute;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
internal sealed class TestCaseAttribute(params object?[] arguments) : Attribute
{
    public object?[] Arguments { get; } = arguments;
}

internal static class TestRunner
{
    public static async Task<int> RunAsync(Assembly assembly)
    {
        List<(string Name, MethodInfo Method, object?[] Arguments)> tests = Discover(assembly);
        int passed = 0;
        int failed = 0;

        foreach ((string name, MethodInfo method, object?[] arguments) in tests)
        {
            try
            {
                object? instance = method.IsStatic ? null : Activator.CreateInstance(method.DeclaringType!);
                object? result = method.Invoke(instance, PrepareArguments(method, arguments));
                if (result is Task task)
                {
                    await task.ConfigureAwait(false);
                }

                Console.WriteLine("PASS " + name);
                passed++;
            }
            catch (Exception ex)
            {
                Exception failure = ex is TargetInvocationException { InnerException: not null } ? ex.InnerException : ex;
                Console.WriteLine("FAIL " + name);
                Console.WriteLine(failure);
                failed++;
            }
        }

        Console.WriteLine();
        Console.WriteLine($"Summary: {passed} passed, {failed} failed, {tests.Count} total");
        return failed == 0 ? 0 : 1;
    }

    private static List<(string Name, MethodInfo Method, object?[] Arguments)> Discover(Assembly assembly)
    {
        List<(string Name, MethodInfo Method, object?[] Arguments)> tests = [];
        foreach (Type type in assembly.GetTypes().Where(type => type.IsClass && !type.IsAbstract).OrderBy(type => type.FullName, StringComparer.Ordinal))
        {
            foreach (MethodInfo method in type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).OrderBy(method => method.Name, StringComparer.Ordinal))
            {
                if (method.GetCustomAttribute<TestAttribute>() is not null)
                {
                    tests.Add(($"{type.Name}.{method.Name}", method, []));
                }

                foreach (TestCaseAttribute testCase in method.GetCustomAttributes<TestCaseAttribute>())
                {
                    string arguments = string.Join(", ", testCase.Arguments.Select(FormatArgument));
                    tests.Add(($"{type.Name}.{method.Name}({arguments})", method, testCase.Arguments));
                }
            }
        }

        return tests;
    }

    private static string FormatArgument(object? value) => value is null ? "null" : value is string text ? "\"" + text + "\"" : value.ToString() ?? string.Empty;

    private static object?[] PrepareArguments(MethodInfo method, object?[] arguments)
    {
        ParameterInfo[] parameters = method.GetParameters();
        if (parameters.Length == 1 && parameters[0].ParameterType.IsArray)
        {
            Type elementType = parameters[0].ParameterType.GetElementType()!;
            Array packed = Array.CreateInstance(elementType, arguments.Length);
            for (int index = 0; index < arguments.Length; index++)
            {
                packed.SetValue(arguments[index], index);
            }

            return [packed];
        }

        return arguments;
    }
}

internal static class Assert
{
    public static void True(bool condition, string? message = null) { if (!condition) Fail(message ?? "Expected true."); }
    public static void False(bool condition, string? message = null) { if (condition) Fail(message ?? "Expected false."); }
    public static void Null(object? value) { if (value is not null) Fail("Expected null."); }
    public static void NotNull(object? value) { if (value is null) Fail("Expected non-null value."); }
    public static void Empty(IEnumerable values) { if (values.Cast<object?>().Any()) Fail("Expected empty collection."); }
    public static T Single<T>(IEnumerable<T> values) { T[] items = values.ToArray(); if (items.Length != 1) Fail($"Expected one item, found {items.Length}."); return items[0]; }
    public static void Equal<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) Fail($"Expected: {expected}{Environment.NewLine}Actual:   {actual}"); }
    public static void Contains(string expected, string actual, StringComparison comparison = StringComparison.Ordinal) { if (!actual.Contains(expected, comparison)) Fail($"Expected string to contain: {expected}"); }
    public static void Contains<T>(T expected, IEnumerable<T> values) { if (!values.Contains(expected)) Fail($"Expected collection to contain: {expected}"); }
    public static void Contains<T>(T expected, IEnumerable<T> values, IEqualityComparer<T> comparer) { if (!values.Contains(expected, comparer)) Fail($"Expected collection to contain: {expected}"); }
    public static void DoesNotContain(string expected, string actual, StringComparison comparison = StringComparison.Ordinal) { if (actual.Contains(expected, comparison)) Fail($"Expected string not to contain: {expected}"); }
    public static void Contains<T>(IEnumerable<T> values, Func<T, bool> predicate) { if (!values.Any(predicate)) Fail("Expected collection to contain a matching item."); }
    public static void DoesNotContain<T>(IEnumerable<T> values, Func<T, bool> predicate) { if (values.Any(predicate)) Fail("Expected collection not to contain a matching item."); }
    public static void EndsWith(string expected, string actual, StringComparison comparison = StringComparison.Ordinal) { if (!actual.EndsWith(expected, comparison)) Fail($"Expected string to end with: {expected}"); }
    public static void StartsWith(string expected, string actual, StringComparison comparison = StringComparison.Ordinal) { if (!actual.StartsWith(expected, comparison)) Fail($"Expected string to start with: {expected}"); }
    public static void Matches(string pattern, string actual) { if (!Regex.IsMatch(actual, pattern)) Fail($"Expected string to match: {pattern}"); }
    public static TException Throws<TException>(Action action) where TException : Exception { try { action(); } catch (TException ex) { return ex; } catch (Exception ex) { Fail($"Expected {typeof(TException).Name}, found {ex.GetType().Name}."); } Fail($"Expected {typeof(TException).Name}."); return null!; }
    private static void Fail(string message) => throw new InvalidOperationException(message);
}
