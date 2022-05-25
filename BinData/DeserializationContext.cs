using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace BinData;

internal sealed class DeserializationContext
{
    public delegate object? ReadMethod(Stream stream);

    public Type Type { get; }
    public ReadMethod Read { get; }

    private static readonly ConcurrentDictionary<Type, DeserializationContext> _cache = new();

    private static readonly Expression _nullConstant = Expression.Constant(null);
    private static readonly Expression _minusOneConstant = Expression.Constant(-1);
    private static readonly Expression _zeroConstant = Expression.Constant(0);
    private static readonly Expression _oneConstant = Expression.Constant(1);
    private static readonly MethodInfo _readByte = typeof(Stream)
        .GetMethod(nameof(Stream.ReadByte))!;
    private static readonly MethodInfo _readInt = typeof(BinaryStreamReader)
        .GetMethod(nameof(BinaryStreamReader.ReadInt))!;
    private static readonly MethodInfo _throwEndOfStreamException = typeof(ThrowHelper)
        .GetMethod(nameof(ThrowHelper.ThrowEndOfStreamException))!;
    private static readonly MethodInfo _readString = typeof(BinaryStreamReader)
        .GetMethod(nameof(BinaryStreamReader.ReadString))!;
    private static readonly MethodInfo _readBytes = typeof(BinaryStreamReader)
        .GetMethod(nameof(BinaryStreamReader.ReadBytes))!;

    public DeserializationContext(Type type, ReadMethod read)
    {
        Type = type ?? throw new ArgumentNullException(nameof(type));
        Read = read ?? throw new ArgumentNullException(nameof(read));
    }

    public static DeserializationContext Create(Type type)
    {
        // Search cache
        if (_cache.TryGetValue(type, out DeserializationContext? context))
        {
            return context;
        }

        // Create parameters
        ParameterExpression streamParameter = Expression.Parameter(typeof(Stream));

        // Create variables
        ParameterExpression value = Expression.Variable(ShadowInterfaces(type));
        ParameterExpression iterator = Expression.Variable(typeof(int));
        ParameterExpression iteratorEnd = Expression.Variable(typeof(int));
        ParameterExpression result = Expression.Variable(typeof(object));
        var variables = new List<ParameterExpression> { value, iterator, iteratorEnd, result };

        // Create body
        var expressions = new List<Expression>();
        AddReadExpression(new BuildingInfo(expressions, variables, type, value, streamParameter, iterator, iteratorEnd));

        // Return value as an object
        LabelTarget returnLabel = Expression.Label(typeof(object));
        expressions.Add(Expression.Assign(result, Expression.Convert(value, typeof(object))));
        expressions.Add(Expression.Return(returnLabel, result));
        expressions.Add(Expression.Label(returnLabel, _nullConstant));
        BlockExpression block = Expression.Block(variables, expressions);

        // Compile expressions
        ReadMethod read = Expression.Lambda<ReadMethod>(body: block, streamParameter).Compile();

        // Cache & return new context
        context = new DeserializationContext(type, read);
        _cache.TryAdd(type, context);
        return context;
    }

    private static void AddReadExpression(BuildingInfo info)
    {
        info = info with { Type = ShadowInterfaces(info.Type) };

        if (info.Type.IsEnum)
        {
            AddReadEnumExpression(info);
        }
        else if (info.Type.IsPrimitive || info.Type == typeof(decimal))
        {
            AddReadPrimitiveExpression(info);
        }
        else if (info.Type == typeof(string))
        {
            AddReadNullableObjectExpression(info, AddReadStringExpression);
        }
        else if (info.Type.GetInterfaces().Any(i => i == typeof(ITuple)))
        {
            if (info.Type.IsClass)
            {
                AddReadNullableObjectExpression(info, AddReadTupleExpression);
            }
            else
            {
                AddReadValueTupleExpression(info);
            }
        }
        else if (info.Type == typeof(byte[]))
        {
            AddReadNullableObjectExpression(info, AddReadByteArrayExpression);
        }
        else if (info.Type.IsArray)
        {
            AddReadNullableObjectExpression(info, AddReadArrayExpression);
        }
        else if (info.Type.IsGenericType && info.Type.GetGenericTypeDefinition() == typeof(List<>))
        {
            AddReadNullableObjectExpression(info, AddReadListExpression);
        }
        else if (info.Type.IsGenericType && info.Type.GetGenericTypeDefinition() == typeof(Nullable<>))
        {
            AddReadNullableExpression(info);
        }
        else if (info.Type.IsClass)
        {
            AddReadNullableObjectExpression(info, AddReadClassExpression);
        }
        else
        {
            throw new NotSupportedException($"Unsupported type '{info.Type.FullName}'.");
        }
    }

    private static void AddReadEnumExpression(BuildingInfo info)
    {
        MethodInfo readEnum = typeof(BinaryStreamReader)
            .GetMethod(nameof(BinaryStreamReader.ReadEnum))!
            .MakeGenericMethod(info.Type);

        info.Add(Expression.Assign(info.Value, Expression.Call(null, readEnum, info.Stream)));
    }

    private static void AddReadPrimitiveExpression(BuildingInfo info)
    {
        MethodInfo readPrimitive = typeof(BinaryStreamReader)
            .GetMethod(nameof(BinaryStreamReader.ReadPrimitive))!
            .MakeGenericMethod(info.Type);

        info.Add(Expression.Assign(info.Value, Expression.Call(null, readPrimitive, info.Stream)));
    }

    private static void AddReadForLoopExpression(BuildingInfo info, Action<BuildingInfo> action)
    {
        LabelTarget condition = Expression.Label();
        LabelTarget end = Expression.Label();

        info.Add(Expression.Assign(info.Iterator, _zeroConstant));
        info.Add(Expression.Label(condition));
        info.Add(Expression.IfThen(Expression.Equal(info.Iterator, info.IteratorEnd), Expression.Goto(end)));

        action(info);

        info.Add(Expression.PostIncrementAssign(info.Iterator));
        info.Add(Expression.Goto(condition));
        info.Add(Expression.Label(end));
    }

    private static void AddReadArrayExpression(BuildingInfo info)
    {
        Type elementType = info.Type.GetElementType()!;

        info.Add(Expression.Assign(info.IteratorEnd, Expression.Call(null, _readInt, info.Stream)));
        info.Add(Expression.Assign(info.Value, Expression.NewArrayBounds(elementType, info.IteratorEnd)));

        AddReadForLoopExpression(info, info =>
            AddReadExpression(info with { Type = elementType, Value = Expression.ArrayAccess(info.Value, info.Iterator) })
        );
    }

    private static void AddReadListExpression(BuildingInfo info)
    {
        PropertyInfo indexer = info.Type.GetProperties().FirstOrDefault(prop => prop.GetIndexParameters().Length == 1)!;
        Type elementType = info.Type.GetGenericArguments()[0];

        info.Add(Expression.Assign(info.IteratorEnd, Expression.Call(null, _readInt, info.Stream)));
        info.Add(Expression.Assign(info.Value, Expression.New(info.Type)));

        AddReadForLoopExpression(info, info =>
        {
            Expression tempVariable = ReadToTemporary(info, elementType);
            MethodInfo add = info.Type.GetMethod("Add")!;
            info.Add(Expression.Call(info.Value, add, tempVariable));
        });
    }

    private static void AddReadNullableObjectExpression(BuildingInfo info, Action<BuildingInfo> action)
    {
        LabelTarget notNullLabel = Expression.Label();
        LabelTarget endLabel = Expression.Label();

        info.Add(Expression.Assign(info.Iterator, Expression.Call(info.Stream, _readByte)));
        info.Add(Expression.IfThen(Expression.Equal(info.Iterator, _minusOneConstant), Expression.Call(null, _throwEndOfStreamException)));
        info.Add(Expression.IfThen(Expression.Equal(info.Iterator, _oneConstant), Expression.Goto(notNullLabel)));
        info.Add(Expression.Assign(info.Value, Expression.Constant(null, info.Type)));
        info.Add(Expression.Goto(endLabel));

        info.Add(Expression.Label(notNullLabel));
        action(info);
        info.Add(Expression.Label(endLabel));
    }

    private static void AddReadStringExpression(BuildingInfo info)
    {
        info.Add(Expression.Assign(info.Value, Expression.Call(null, _readString, info.Stream)));
    }

    private static void AddReadTupleExpression(BuildingInfo info)
    {
        var values = new List<Expression>();
        foreach (PropertyInfo item in info.Type.GetProperties())
        {
            if (!item.Name.StartsWith("Item") && item.Name != "Rest")
                continue;

            MemberExpression member = Expression.Property(info.Value, item);
            values.Add(ReadToTemporary(info, item.PropertyType));
        }

        ConstructorInfo constructor = info.Type.GetConstructor(info.Type.GetGenericArguments())!;
        info.Add(Expression.Assign(info.Value, Expression.New(constructor, values)));
    }

    private static void AddReadValueTupleExpression(BuildingInfo info)
    {
        foreach (FieldInfo item in info.Type.GetFields())
        {
            if (!item.Name.StartsWith("Item") && item.Name != "Rest")
                continue;

            MemberExpression member = Expression.Field(info.Value, item);
            AddReadExpression(info with { Type = item.FieldType, Value = member });
        }
    }

    private static void AddReadByteArrayExpression(BuildingInfo info)
    {
        info.Add(Expression.Assign(info.Value, Expression.Call(null, _readBytes, info.Stream)));
    }

    private static void AddReadNullableExpression(BuildingInfo info)
    {
        LabelTarget notNullLabel = Expression.Label();
        LabelTarget endLabel = Expression.Label();

        info.Add(Expression.Assign(info.Iterator, Expression.Call(info.Stream, _readByte)));
        info.Add(Expression.IfThen(Expression.Equal(info.Iterator, _minusOneConstant), Expression.Call(null, _throwEndOfStreamException)));
        info.Add(Expression.IfThen(Expression.Equal(info.Iterator, _oneConstant), Expression.Goto(notNullLabel)));
        info.Add(Expression.Assign(info.Value, Expression.New(info.Type)));
        info.Add(Expression.Goto(endLabel));

        info.Add(Expression.Label(notNullLabel));
        Type[] genericArguments = info.Type.GetGenericArguments();
        ConstructorInfo constructor = info.Type.GetConstructor(genericArguments)!;
        info.Add(Expression.Assign(info.Value, Expression.New(constructor, ReadToTemporary(info, genericArguments[0]))));
        info.Add(Expression.Label(endLabel));
    }

    private static void AddReadClassExpression(BuildingInfo info)
    {
        IEnumerable<PropertyInfo> properties = info.Type
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(x =>
                x.GetCustomAttribute<NotSerializedAttribute>() is null &&
                x.GetMethod is not null &&
                (x.GetMethod?.IsPublic ?? false) &&
                (x.SetMethod?.IsPublic ?? false));

        info.Add(Expression.Assign(info.Value, Expression.New(info.Type)));

        foreach (PropertyInfo property in properties)
        {
            MemberExpression member = Expression.Property(info.Value, property);
            AddReadExpression(info with { Type = property.PropertyType, Value = member });
        }

        IEnumerable<FieldInfo> fields = info.Type
            .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(x => x.GetCustomAttribute<SerializedAttribute>() is not null);

        foreach (FieldInfo field in fields)
        {
            MemberExpression member = Expression.Field(info.Value, field);
            AddReadExpression(info with { Type = field.FieldType, Value = member });
        }
    }

    private static Expression ReadToTemporary(BuildingInfo info, Type type)
    {
        ParameterExpression temp = Expression.Variable(type);
        info.Variables.Add(temp);
        AddReadExpression(info with { Type = type, Value = temp });
        return temp;
    }

    // Adds a concrete type to selected interfaces
    private static Type ShadowInterfaces(Type type)
    {
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>))
        {
            return typeof(List<>).MakeGenericType(type.GetGenericArguments());
        }

        return type;
    }
}
