using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace BinData;

internal sealed class SerializationContext
{
    public delegate void WriteMethod(object value, Stream stream);

    public Type Type { get; }
    public WriteMethod Write { get; }

    private static readonly ConcurrentDictionary<Type, SerializationContext> _cache = new();

    private static readonly Expression _nullExpression = Expression.Constant(null);
    private static readonly Expression _zeroByteExpression = Expression.Constant((byte)0);
    private static readonly Expression _oneByteExpression = Expression.Constant((byte)1);
    private static readonly Expression _zeroIntegerExpression = Expression.Constant(0);
    private static readonly MethodInfo _streamWriteByte = typeof(Stream).GetMethod(nameof(Stream.WriteByte))!;
    private static readonly MethodInfo _writeString = typeof(BinaryStreamWriter)
        .GetMethod(nameof(BinaryStreamWriter.WriteString))!;
    private delegate Span<byte> AsSpanDelegate(byte[] array);
    private static readonly MethodInfo _asSpan = ((AsSpanDelegate)(MemoryExtensions.AsSpan)).Method;
    private static readonly MethodInfo _writeInt = typeof(BinaryStreamWriter)
        .GetMethod(nameof(BinaryStreamWriter.WritePrimitive))!.MakeGenericMethod(new[] { typeof(int) });
    private static readonly MethodInfo _writeSpan = typeof(Stream)
        .GetMethod(nameof(Stream.Write), new[] { typeof(ReadOnlySpan<byte>) })!;

    public SerializationContext(Type type, WriteMethod write)
    {
        Type = type ?? throw new ArgumentNullException(nameof(type));
        Write = write ?? throw new ArgumentNullException(nameof(write));
    }

    public static SerializationContext Create(Type type)
    {
        // Search cache
        if (_cache.TryGetValue(type, out SerializationContext? context))
        {
            return context;
        }

        // Create parameters
        ParameterExpression valueParameter = Expression.Parameter(typeof(object));
        ParameterExpression streamParameter = Expression.Parameter(typeof(Stream));

        // Create variables
        ParameterExpression value = Expression.Variable(type);
        ParameterExpression iterator = Expression.Variable(typeof(int));
        var variables = new ParameterExpression[] { value, iterator };

        // Create body
        var expressions = new List<Expression>();
        expressions.Add(Expression.Assign(value, Expression.Convert(valueParameter, type)));
        AddWriteExpression(new BuildingInfo(expressions, type, value, streamParameter, iterator));
        BlockExpression block = Expression.Block(variables, expressions);

        // Compile expressions
        WriteMethod write = Expression.Lambda<WriteMethod>(body: block, valueParameter, streamParameter).Compile();

        // Cache & return new context
        context = new SerializationContext(type, write);
        _cache.TryAdd(type, context);
        return context;
    }

    private static void AddWriteExpression(BuildingInfo info)
    {
        if (info.Type.IsEnum)
        {
            AddWriteEnumExpression(info);
        }
        else if (info.Type.IsPrimitive || info.Type == typeof(decimal))
        {
            AddWritePrimitiveExpression(info);
        }
        else if (info.Type == typeof(string))
        {
            AddWriteNullableObjectExpression(info, AddWriteStringExpression);
        }
        else if (info.Type.GetInterfaces().Any(i => i == typeof(ITuple)))
        {
            if (info.Type.IsClass)
            {
                AddWriteNullableObjectExpression(info, AddWriteTupleExpression);
            }
            else
            {
                AddWriteValueTupleExpression(info);
            }
        }
        else if (info.Type == typeof(byte[]))
        {
            AddWriteNullableObjectExpression(info, AddWriteByteArrayExpression);
        }
        else if (info.Type.IsArray)
        {
            AddWriteNullableObjectExpression(info, info => AddWriteForLoopExpression(info, "Length", AddWriteArrayExpression));
        }
        else if (info.Type.IsGenericType && info.Type.GetGenericTypeDefinition() == typeof(List<>))
        {
            AddWriteNullableObjectExpression(info, info => AddWriteForLoopExpression(info, "Count", AddWriteListExpression));
        }
        else if (info.Type.IsGenericType && info.Type.GetGenericTypeDefinition() == typeof(Nullable<>))
        {
            AddWriteNullableExpression(info);
        }
        else if (info.Type.IsClass)
        {
            AddWriteNullableObjectExpression(info, AddWriteClassExpression);
        }
        else
        {
            throw new NotSupportedException($"Unsupported type '{info.Type.FullName}'.");
        }
    }

    private static void AddWriteEnumExpression(BuildingInfo info)
    {
        MethodInfo writeEnum = typeof(BinaryStreamWriter)
            .GetMethod(nameof(BinaryStreamWriter.WriteEnum))!
            .MakeGenericMethod(info.Type);

        info.Add(Expression.Call(writeEnum, info.Value, info.Stream));
    }

    private static void AddWritePrimitiveExpression(BuildingInfo info)
    {
        MethodInfo writePrimitive = typeof(BinaryStreamWriter)
            .GetMethod(nameof(BinaryStreamWriter.WritePrimitive))!
            .MakeGenericMethod(info.Type);

        info.Add(Expression.Call(writePrimitive, info.Value, info.Stream));
    }

    private static void AddWriteStringExpression(BuildingInfo info)
    {
        info.Add(Expression.Call(_writeString, info.Value, info.Stream));
    }

    private static void AddWriteTupleExpression(BuildingInfo info)
    {
        foreach (PropertyInfo item in info.Type.GetProperties())
        {
            if (!item.Name.StartsWith("Item") && item.Name != "Rest")
                continue;

            MemberExpression member = Expression.Property(info.Value, item);
            AddWriteExpression(info with { Type = item.PropertyType, Value = member });
        }
    }

    private static void AddWriteNullableExpression(BuildingInfo info)
    {
        LabelTarget endLabel = Expression.Label();
        LabelTarget nullLabel = Expression.Label();

        info.Add(Expression.IfThen(Expression.Not(Expression.Property(info.Value, "HasValue")), Expression.Goto(nullLabel)));
        info.Add(Expression.Call(info.Stream, _streamWriteByte, _oneByteExpression));

        AddWriteExpression(info with { Type = info.Type.GetGenericArguments()[0], Value = Expression.Property(info.Value, "Value") });

        info.Add(Expression.Goto(endLabel));
        info.Add(Expression.Label(nullLabel));
        info.Add(Expression.Call(info.Stream, _streamWriteByte, _zeroByteExpression));
        info.Add(Expression.Label(endLabel));
    }

    private static void AddWriteValueTupleExpression(BuildingInfo info)
    {
        foreach (FieldInfo item in info.Type.GetFields())
        {
            if (!item.Name.StartsWith("Item") && item.Name != "Rest")
                continue;

            MemberExpression member = Expression.Field(info.Value, item);
            AddWriteExpression(info with { Type = item.FieldType, Value = member });
        }
    }

    private static void AddWriteListExpression(BuildingInfo info)
    {
        PropertyInfo indexer = info.Type.GetProperties().FirstOrDefault(prop => prop.GetIndexParameters().Length == 1)!;
        AddWriteExpression(info with { Type = info.Type.GetGenericArguments()[0], Value = Expression.Property(info.Value, indexer, info.Iterator) });
    }

    private static void AddWriteArrayExpression(BuildingInfo info)
    {
        AddWriteExpression(info with { Type = info.Type.GetElementType()!, Value = Expression.ArrayIndex(info.Value, info.Iterator) });
    }

    private static void AddWriteForLoopExpression(BuildingInfo info, string lengthPropertyName, Action<BuildingInfo> action)
    {
        LabelTarget condition = Expression.Label();
        LabelTarget end = Expression.Label();

        info.Add(Expression.Call(null, _writeInt, Expression.Property(info.Value, lengthPropertyName), info.Stream));

        info.Add(Expression.Assign(info.Iterator, _zeroIntegerExpression));
        info.Add(Expression.Label(condition));
        info.Add(Expression.IfThen(Expression.Equal(info.Iterator, Expression.Property(info.Value, lengthPropertyName)), Expression.Goto(end)));

        action(info);

        info.Add(Expression.PostIncrementAssign(info.Iterator));
        info.Add(Expression.Goto(condition));
        info.Add(Expression.Label(end));
    }

    private static void AddWriteByteArrayExpression(BuildingInfo info)
    {
        info.Add(Expression.Call(null, _writeInt, Expression.Property(info.Value, "Length"), info.Stream));
        info.Add(Expression.Call(info.Stream, _writeSpan, Expression.Convert(Expression.Call(null, _asSpan, info.Value), typeof(ReadOnlySpan<byte>))));
    }

    private static void AddWriteClassExpression(BuildingInfo info)
    {
        IEnumerable<MethodInfo> propertyGetters = info.Type
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(x =>
                x.GetCustomAttribute<NotSerializedAttribute>() is null &&
                x.GetMethod is not null &&
                (x.GetMethod?.IsPublic ?? false) &&
                (x.SetMethod?.IsPublic ?? false))
            .Select(x => x.GetMethod!);

        foreach (MethodInfo propertyGetter in propertyGetters)
        {
            MemberExpression member = Expression.Property(info.Value, propertyGetter);
            AddWriteExpression(info with { Type = propertyGetter.ReturnType, Value = member });
        }

        IEnumerable<FieldInfo> fields = info.Type
            .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(x => x.GetCustomAttribute<SerializedAttribute>() is not null);

        foreach (FieldInfo field in fields)
        {
            MemberExpression member = Expression.Field(info.Value, field);
            AddWriteExpression(info with { Type = field.FieldType, Value = member });
        }
    }

    private static void AddWriteNullableObjectExpression(BuildingInfo info, Action<BuildingInfo> action)
    {
        LabelTarget endLabel = Expression.Label();
        LabelTarget nullLabel = Expression.Label();

        info.Add(Expression.IfThen(Expression.ReferenceEqual(info.Value, _nullExpression), Expression.Goto(nullLabel)));
        info.Add(Expression.Call(info.Stream, _streamWriteByte, _oneByteExpression));

        action(info);

        info.Add(Expression.Goto(endLabel));
        info.Add(Expression.Label(nullLabel));
        info.Add(Expression.Call(info.Stream, _streamWriteByte, _zeroByteExpression));
        info.Add(Expression.Label(endLabel));
    }

    private record BuildingInfo(List<Expression> Expressions, Type Type, Expression Value, ParameterExpression Stream, ParameterExpression Iterator)
    {
        public void Add(Expression expression)
        {
            Expressions.Add(expression);
        }
    }
}
