using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace BinData;

internal sealed class DeserializationContext
{
    public delegate object ReadMethod(Stream stream);

    public Type Type { get; }
    public ReadMethod Read { get; }

    private static readonly ConcurrentDictionary<Type, DeserializationContext> _cache = new();

    private static readonly Expression _nullConstant = Expression.Constant((object?)null);

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
        ParameterExpression value = Expression.Variable(type);
        ParameterExpression iterator = Expression.Variable(typeof(int));
        ParameterExpression result = Expression.Variable(typeof(object));
        var variables = new ParameterExpression[] { value, iterator, result };

        // Create body
        var expressions = new List<Expression>();
        AddReadExpression(new BuildingInfo(expressions, type, value, streamParameter, iterator));

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
        if (info.Type.IsEnum)
        {
            AddReadEnumExpression(info);
        }
        else if (info.Type.IsPrimitive || info.Type == typeof(decimal))
        {
            //AddWritePrimitiveExpression(info);
        }
        else if (info.Type == typeof(string))
        {
            //AddWriteNullableObjectExpression(info, AddWriteStringExpression);
        }
        else if (info.Type.GetInterfaces().Any(i => i == typeof(ITuple)))
        {
            if (info.Type.IsClass)
            {
                //AddWriteNullableObjectExpression(info, AddWriteTupleExpression);
            }
            else
            {
                //AddWriteValueTupleExpression(info);
            }
        }
        else if (info.Type == typeof(byte[]))
        {
            //AddWriteNullableObjectExpression(info, AddWriteByteArrayExpression);
        }
        else if (info.Type.IsArray)
        {
            //AddWriteNullableObjectExpression(info, info => AddWriteForLoopExpression(info, "Length", AddWriteArrayExpression));
        }
        else if (info.Type.IsGenericType && info.Type.GetGenericTypeDefinition() == typeof(List<>))
        {
            //AddWriteNullableObjectExpression(info, info => AddWriteForLoopExpression(info, "Count", AddWriteListExpression));
        }
        else if (info.Type.IsGenericType && info.Type.GetGenericTypeDefinition() == typeof(Nullable<>))
        {
            //AddWriteNullableExpression(info);
        }
        else if (info.Type.IsClass)
        {
            //AddWriteNullableObjectExpression(info, AddWriteClassExpression);
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
}
