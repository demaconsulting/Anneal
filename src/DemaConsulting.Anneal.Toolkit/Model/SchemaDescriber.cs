using System.Collections;
using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Text;

namespace DemaConsulting.Anneal.Toolkit.Model;

/// <summary>
///     Renders the schema block for a typed probe result: one line per public property, with the closed
///     vocabulary of every enum-typed property spelled out.
/// </summary>
/// <remarks>
///     The framework owns this rather than the caller, and that is the whole point. A caller supplies its
///     question and its authoritative context; it cannot forget the schema, cannot word it differently from the
///     type it will decode into, and cannot place it early — which is the configuration this design exists to
///     avoid. Deriving the block from the type also means a property renamed in code is renamed in the prompt,
///     with no second definition to drift.
///     <para>
///         Deliberately shallower than a general JSON Schema emitter: probe results are flat by design, so a
///         nested object is described by a coarse label rather than recursed into. If a probe ever needs a nested
///         shape, the honest repair is a flatter result type, not a deeper describer.
///     </para>
///     <para>
///         Thread safety: stateless and safe to call concurrently.
///     </para>
/// </remarks>
public static class SchemaDescriber
{
    /// <summary>
    ///     Describes the response type as the body of the schema block presented to the model.
    /// </summary>
    /// <typeparam name="T">The typed probe result to describe. Its public instance properties are the schema.</typeparam>
    /// <returns>
    ///     One line per public property, in declaration order, with an indented hint line per value of any
    ///     enum-typed property. Never null; empty when the type has no public instance properties.
    /// </returns>
    public static string Describe<T>() => Describe(typeof(T));

    /// <summary>
    ///     Describes a response type as the body of the schema block presented to the model.
    /// </summary>
    /// <param name="type">The typed probe result to describe. Must not be null.</param>
    /// <returns>
    ///     One line per public property, in declaration order, with an indented hint line per value of any
    ///     enum-typed property. Never null; empty when the type has no public instance properties.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="type" /> is null.</exception>
    public static string Describe(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);

        var builder = new StringBuilder();

        // Ordered by metadata token - declaration order - rather than by whatever order reflection happens to
        // return, because an unstable prompt makes one model reply reproducible and the next one not, and the
        // parse-failure rate this design must measure would then be measuring the describer.
        var properties = type
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.GetIndexParameters().Length == 0)
            .OrderBy(property => property.MetadataToken);

        foreach (var property in properties)
            AppendProperty(builder, property.Name, property.PropertyType);

        return builder.ToString().TrimEnd('\n');
    }

    private static void AppendProperty(StringBuilder builder, string name, Type type)
    {
        // A closed vocabulary is the one thing worth spending extra lines on: a model shown the permitted
        // values selects from them, while a model shown only "string" invents a synonym that then fails the
        // closed-enum decode and costs a whole retry.
        var enumType = ResolveEnum(type);
        if (enumType is not null)
        {
            var names = Enum.GetNames(enumType);
            builder.Append(CultureInfo.InvariantCulture,
                $"- \"{name}\": one of {string.Join(" | ", names.Select(value => $"\"{value}\""))}\n");

            foreach (var value in names)
                builder.Append(CultureInfo.InvariantCulture, $"    - \"{value}\": {DescribeMember(enumType, value)}\n");

            return;
        }

        builder.Append(CultureInfo.InvariantCulture, $"- \"{name}\": {DescribeType(type)}\n");
    }

    /// <returns>The enum type behind a property, unwrapping a nullable enum; null when the property is not one.</returns>
    private static Type? ResolveEnum(Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type) ?? type;
        return underlying.IsEnum ? underlying : null;
    }

    /// <returns>
    ///     The one-line hint for an enum member: its <see cref="DescriptionAttribute" /> when it carries one, so
    ///     that the meaning of a value is stated where the model reads the vocabulary rather than left to the
    ///     member name alone.
    /// </returns>
    private static string DescribeMember(Type enumType, string name)
    {
        var description = enumType
            .GetField(name, BindingFlags.Public | BindingFlags.Static)
            ?.GetCustomAttribute<DescriptionAttribute>()
            ?.Description;

        return string.IsNullOrWhiteSpace(description) ? name : description;
    }

    private static string DescribeType(Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type) ?? type;

        if (underlying == typeof(string))
            return "string";

        if (underlying == typeof(bool))
            return "boolean";

        if (underlying == typeof(byte) || underlying == typeof(sbyte) ||
            underlying == typeof(short) || underlying == typeof(ushort) ||
            underlying == typeof(int) || underlying == typeof(uint) ||
            underlying == typeof(long) || underlying == typeof(ulong))
            return "integer";

        if (underlying == typeof(float) || underlying == typeof(double) || underlying == typeof(decimal))
            return "number";

        return typeof(IEnumerable).IsAssignableFrom(underlying) ? "array" : "object";
    }
}
