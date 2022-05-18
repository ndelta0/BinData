using System.Buffers.Binary;
using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using BindingFlags = System.Reflection.BindingFlags;

namespace BinData;

public static partial class BinaryConvert
{
    public static T? Deserialize<T>(byte[] bytes) => (T?)Deserialize(bytes, typeof(T));

    public static object? Deserialize(byte[] bytes, Type type)
    {
        using var ms = new MemoryStream(bytes);

        var value = DeserializeInternal(ms, type);

        return value;
    }

    private static object? DeserializeInternal(Stream s, Type type)
    {
        var b = s.ReadByte();
        switch (b)
        {
            case -1:
                throw new EndOfStreamException("End of stream reached unexpectedly.");
            case 0:
                return null;
            case 1:
            {
                if (type.IsEnum)
                {
                    return DeserializeEnum(s, type);
                }

                if (type.IsAssignableTo(typeof(ITuple)))
                {
                    return DeserializeTuple(s, type);
                }

                if (type.IsValueType)
                {
                    return DeserializeValueType(s, type);
                }

                if (type == typeof(string))
                {
                    return DeserializeString(s);
                }

                if (type.IsAssignableTo(typeof(IEnumerable)))
                {
                    return DeserializeEnumerable(s, type);
                }

                if (type.IsClass)
                {
                    return DeserializeClass(s, type);
                }

                throw new InvalidOperationException("Invalid type.");
            }
            default:
                throw new InvalidOperationException("Nullability byte must be 0 or 1.");
        }
    }

    private static void ReadBytes(Stream stream, Span<byte> span, int size)
    {
        var read = stream.Read(span[..size]);
        if (read != size)
            throw new InvalidOperationException("Invalid number of bytes read.");
    }

    private static object DeserializeValueType(Stream stream, Type type)
    {
        Span<byte> buffer = stackalloc byte[16];

        if (type == typeof(sbyte))
        {
            ReadBytes(stream, buffer, 1);
            return (sbyte)(buffer[0] - 128);
        }

        if (type == typeof(byte))
        {
            ReadBytes(stream, buffer, 1);
            return buffer[0];
        }

        if (type == typeof(short))
        {
            ReadBytes(stream, buffer, 2);
            return BinaryPrimitives.ReadInt16LittleEndian(buffer);
        }

        if (type == typeof(ushort))
        {
            ReadBytes(stream, buffer, 2);
            return BinaryPrimitives.ReadUInt16LittleEndian(buffer);
        }

        if (type == typeof(int))
        {
            ReadBytes(stream, buffer, 4);
            return BinaryPrimitives.ReadInt32LittleEndian(buffer);
        }

        if (type == typeof(uint))
        {
            ReadBytes(stream, buffer, 4);
            return BinaryPrimitives.ReadUInt32LittleEndian(buffer);
        }

        if (type == typeof(long))
        {
            ReadBytes(stream, buffer, 8);
            return BinaryPrimitives.ReadInt64LittleEndian(buffer);
        }

        if (type == typeof(ulong))
        {
            ReadBytes(stream, buffer, 8);
            return BinaryPrimitives.ReadUInt64LittleEndian(buffer);
        }

        if (type == typeof(float))
        {
            ReadBytes(stream, buffer, 4);
            return BinaryPrimitives.ReadSingleLittleEndian(buffer);
        }

        if (type == typeof(double))
        {
            ReadBytes(stream, buffer, 8);
            return BinaryPrimitives.ReadDoubleLittleEndian(buffer);
        }

        if (type == typeof(decimal))
        {
            ReadBytes(stream, buffer, 16);
            var bits = new[]
            {
                BinaryPrimitives.ReadInt32LittleEndian(buffer[..4]),
                BinaryPrimitives.ReadInt32LittleEndian(buffer.Slice(4, 4)),
                BinaryPrimitives.ReadInt32LittleEndian(buffer.Slice(8, 4)),
                BinaryPrimitives.ReadInt32LittleEndian(buffer.Slice(12, 4))
            };
            return new decimal(bits);
        }

        if (type == typeof(bool))
        {
            ReadBytes(stream, buffer, 1);
            return buffer[0] switch
            {
                0 => false,
                1 => true,
                _ => throw new InvalidOperationException("Invalid boolean value.")
            };
        }

        if (type != typeof(char)) throw new InvalidOperationException("Invalid value type.");

        ReadBytes(stream, buffer, 2);
        return (char)BinaryPrimitives.ReadUInt16LittleEndian(buffer);
    }

    private static string DeserializeString(Stream stream)
    {
        Span<byte> buf = stackalloc byte[4];
        ReadBytes(stream, buf, 4);
        var length = BinaryPrimitives.ReadInt32LittleEndian(buf);

        if (length == 0)
            return string.Empty;

        buf = length > 64 ? new byte[length] : stackalloc byte[length];
        ReadBytes(stream, buf, length);

        return Encoding.UTF8.GetString(buf);
    }

    private static ITuple DeserializeTuple(Stream stream, Type type)
    {
        Type[] genericArguments = type.GenericTypeArguments;
        var arguments = new object?[genericArguments.Length];

        for (var i = 0; i < genericArguments.Length; i++)
        {
            var argType = genericArguments[i];
            var argValue = DeserializeInternal(stream, argType);
            arguments[i] = argValue;
        }

        var tuple = (ITuple?)Activator.CreateInstance(type, arguments);
        if (tuple is null)
            throw new InvalidOperationException("Tuple object must not be null.");

        return tuple;
    }

    private static object DeserializeEnum(Stream stream, Type type)
    {
        Span<byte> buffer = stackalloc byte[8];

        var baseType = Enum.GetUnderlyingType(type);

        if (baseType == typeof(sbyte))
        {
            ReadBytes(stream, buffer, 1);
            return Enum.ToObject(type, (sbyte)(buffer[0] - 128));
        }

        if (baseType == typeof(byte))
        {
            ReadBytes(stream, buffer, 1);
            return Enum.ToObject(type, buffer[0]);
        }

        if (baseType == typeof(short))
        {
            ReadBytes(stream, buffer, 2);
            return Enum.ToObject(type, BinaryPrimitives.ReadInt16LittleEndian(buffer));
        }

        if (baseType == typeof(ushort))
        {
            ReadBytes(stream, buffer, 2);
            return Enum.ToObject(type, BinaryPrimitives.ReadUInt16LittleEndian(buffer));
        }

        if (baseType == typeof(int))
        {
            ReadBytes(stream, buffer, 4);
            return Enum.ToObject(type, BinaryPrimitives.ReadInt32LittleEndian(buffer));
        }

        if (baseType == typeof(uint))
        {
            ReadBytes(stream, buffer, 4);
            return Enum.ToObject(type, BinaryPrimitives.ReadUInt32LittleEndian(buffer));
        }

        if (baseType == typeof(long))
        {
            ReadBytes(stream, buffer, 8);
            return Enum.ToObject(type, BinaryPrimitives.ReadInt64LittleEndian(buffer));
        }

        if (baseType != typeof(ulong)) throw new InvalidOperationException("Invalid enum underlying type.");

        ReadBytes(stream, buffer, 8);
        return Enum.ToObject(type, BinaryPrimitives.ReadUInt64LittleEndian(buffer));
    }

    private static object DeserializeEnumerable(Stream stream, Type type)
    {
        if (type == typeof(IEnumerable) && !type.IsArray)
            throw new InvalidOperationException("Cannot deserialize untyped enumerable.");

        Span<byte> buf = stackalloc byte[4];
        ReadBytes(stream, buf, 4);
        var count = BinaryPrimitives.ReadInt32LittleEndian(buf);

        Type? enumerableType;

        if (type.IsArray)
        {
            enumerableType = type.GetMethods().First(x => x.Name == "Get").ReturnType;
        }
        else if (type.IsAssignableTo(typeof(IEnumerable)))
        {
            enumerableType = type.GenericTypeArguments.FirstOrDefault();
            if (enumerableType is null)
                throw new InvalidOperationException("Invalid enumerable first generic type argument.");
        }
        else
        {
            throw new InvalidOperationException("Invalid enumerable type.");
        }

        var data = Array.CreateInstance(enumerableType, count);

        for (var i = 0; i < count; i++)
        {
            data.SetValue(DeserializeInternal(stream, enumerableType), i);
        }

        if (type.IsArray || type.Name.Equals("IEnumerable`1"))
            return data;

        var activated = Activator.CreateInstance(type, data);
        if (activated is null)
            throw new InvalidOperationException("Activated object must not be null.");
        return activated;
    }

    private static readonly Dictionary<IReflect, MethodInfo[]> PropertySetterMethodsMap = new();
    private static readonly Dictionary<IReflect, (Action<object?, object?>, Type)[]> FieldSetterMethodsMap = new();

    private static object DeserializeClass(Stream stream, Type type)
    {
        var cls = Activator.CreateInstance(type);
        if (cls is null)
            throw new InvalidOperationException("Activated class must not be null");

        if (!PropertySetterMethodsMap.ContainsKey(type))
        {
            PropertySetterMethodsMap.Add(type,
                type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
                    .Where(x =>
                        x.GetCustomAttribute<NotSerializedAttribute>() is null &&
                        x.GetMethod is not null &&
                        (x.GetMethod?.IsPublic ?? false) &&
                        (x.SetMethod?.IsPublic ?? false))
                    .Select(x => x.SetMethod!).ToArray());
        }

        MethodInfo[] propMethods = PropertySetterMethodsMap[type];
        foreach (var propMethod in propMethods)
        {
            var valueType = propMethod.GetParameters().First().ParameterType;
            var value = DeserializeInternal(stream, valueType);
            propMethod.Invoke(cls, new[] { value });
        }

        if (!FieldSetterMethodsMap.ContainsKey(type))
        {
            FieldSetterMethodsMap.Add(type,
                type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .Where(x =>
                        x.GetCustomAttribute<SerializedAttribute>() is not null)
                    .Select<FieldInfo, (Action<object?, object?>, Type)>(x => (x.SetValue, x.FieldType))
                    .ToArray());
        }

        (Action<object?, object?>, Type)[] fieldMethods = FieldSetterMethodsMap[type];
        foreach ((Action<object?, object?> act, var fieldType) in fieldMethods)
        {
            var value = DeserializeInternal(stream, fieldType);
            act(cls, value);
        }

        return cls;
    }
}
