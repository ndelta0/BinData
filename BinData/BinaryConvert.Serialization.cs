using System.Buffers.Binary;
using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;

namespace BinData;

public static partial class BinaryConvert
{
    public static byte[] Serialize(object? data)
    {
        using var ms = new MemoryStream();

        SerializeInternal(ms, data);

        return ms.ToArray();
    }

    private static void SerializeInternal(Stream s, object? data)
    {
        if (data is null)
        {
            s.WriteByte(0);
            return;
        }

        s.WriteByte(1);

        var type = data.GetType();
        if (type.IsEnum)
        {
            SerializeEnum(s, (Enum)data);
        }
        else if (data is ITuple tuple)
        {
            SerializeTuple(s, tuple);
        }
        else if (type.IsValueType)
        {
            SerializeValueType(s, data);
        }
        else
        {
            switch (data)
            {
                case string str:
                    SerializeString(s, str);
                    break;
                case IEnumerable enumerable:
                    SerializeEnumerable(s, enumerable);
                    break;
                default:
                {
                    if (type.IsClass)
                    {
                        SerializeClass(s, data, type);
                    }

                    break;
                }
            }
        }
    }

    private static void SerializeEnum(Stream stream, IConvertible e)
    {
        Span<byte> buffer = stackalloc byte[8];
        var size = 0;

        // ReSharper disable once SwitchStatementMissingSomeEnumCasesNoDefault
        switch (e.GetTypeCode())
        {
            case TypeCode.SByte:
            {
                size = 1;
                buffer[0] = (byte)(Convert.ToInt32(e) + 128);
                break;
            }
            case TypeCode.Byte:
            {
                size = 1;
                buffer[0] = Convert.ToByte(e);
                break;
            }
            case TypeCode.Int16:
            {
                size = 2;
                BinaryPrimitives.WriteInt16LittleEndian(buffer, Convert.ToInt16(e));
                break;
            }
            case TypeCode.UInt16:
            {
                size = 2;
                BinaryPrimitives.WriteUInt16LittleEndian(buffer, Convert.ToUInt16(e));
                break;
            }
            case TypeCode.Int32:
            {
                size = 4;
                BinaryPrimitives.WriteInt32LittleEndian(buffer, Convert.ToInt32(e));
                break;
            }
            case TypeCode.UInt32:
            {
                size = 4;
                BinaryPrimitives.WriteUInt32LittleEndian(buffer, Convert.ToUInt32(e));
                break;
            }
            case TypeCode.Int64:
            {
                size = 8;
                BinaryPrimitives.WriteInt64LittleEndian(buffer, Convert.ToInt64(e));
                break;
            }
            case TypeCode.UInt64:
            {
                size = 8;
                BinaryPrimitives.WriteUInt64LittleEndian(buffer, Convert.ToUInt64(e));
                break;
            }
        }

        stream.Write(buffer[..size]);
    }

    private static void SerializeValueType(Stream stream, object o)
    {
        Span<byte> buffer = stackalloc byte[16];
        var size = 0;

        switch (o)
        {
            case sbyte sb:
            {
                size = 1;
                buffer[0] = (byte)(sb + 128);
                break;
            }
            case byte b:
            {
                size = 1;
                buffer[0] = b;
                break;
            }
            case short s:
            {
                size = 2;
                BinaryPrimitives.WriteInt16LittleEndian(buffer, s);
                break;
            }
            case ushort us:
            {
                size = 2;
                BinaryPrimitives.WriteUInt16LittleEndian(buffer, us);
                break;
            }
            case int i:
            {
                size = 4;
                BinaryPrimitives.WriteInt32LittleEndian(buffer, i);
                break;
            }
            case uint ui:
            {
                size = 4;
                BinaryPrimitives.WriteUInt32LittleEndian(buffer, ui);
                break;
            }
            case long l:
            {
                size = 8;
                BinaryPrimitives.WriteInt64LittleEndian(buffer, l);
                break;
            }
            case ulong ul:
            {
                size = 8;
                BinaryPrimitives.WriteUInt64LittleEndian(buffer, ul);
                break;
            }
            case float f:
            {
                size = 4;
                BinaryPrimitives.WriteSingleLittleEndian(buffer, f);
                break;
            }
            case double d:
            {
                size = 8;
                BinaryPrimitives.WriteDoubleLittleEndian(buffer, d);
                break;
            }
            case decimal d:
            {
                size = 16;
                var bits = decimal.GetBits(d);
                BinaryPrimitives.WriteInt32LittleEndian(buffer[..4], bits[0]);
                BinaryPrimitives.WriteInt32LittleEndian(buffer.Slice(4, 4), bits[1]);
                BinaryPrimitives.WriteInt32LittleEndian(buffer.Slice(8, 4), bits[2]);
                BinaryPrimitives.WriteInt32LittleEndian(buffer.Slice(12, 4), bits[3]);
                break;
            }
            case bool b:
            {
                size = 1;
                buffer[0] = (byte)(b ? 0x01 : 0x00);
                break;
            }
            case char c:
            {
                size = 2;
                BinaryPrimitives.WriteUInt16LittleEndian(buffer, c);
                break;
            }
        }

        stream.Write(buffer[..size]);
    }

    private static void SerializeTuple(Stream stream, ITuple tuple)
    {
        for (var i = 0; i < tuple.Length; i++)
        {
            SerializeInternal(stream, tuple[i]);
        }
    }

    private static void SerializeEnumerable(Stream stream, IEnumerable enumerable)
    {
        var count = 0;

        using var ms = new MemoryStream();

        foreach (var item in enumerable)
        {
            count++;
            SerializeInternal(ms, item);
        }

        Span<byte> buf = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(buf, count);

        stream.Write(buf);
        stream.Write(ms.ToArray());
    }

    private static void SerializeString(Stream stream, string str)
    {
        if (str.Length == 0)
        {
            stream.Write(new byte[] { 0, 0, 0, 0 });
            return;
        }

        var bytes = Encoding.UTF8.GetBytes(str);

        Span<byte> buf = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(buf, bytes.Length);

        stream.Write(buf);
        stream.Write(bytes);
    }

    private static readonly Dictionary<IReflect, MethodInfo[]> PropertyGetterMethodsMap = new();
    private static readonly Dictionary<IReflect, Func<object?, object?>[]> FieldGetterMethodsMap = new();

    private static void SerializeClass(Stream stream, object cls, IReflect type)
    {
        if (!PropertyGetterMethodsMap.ContainsKey(type))
        {
            PropertyGetterMethodsMap.Add(type,
                type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
                    .Where(x =>
                        x.GetCustomAttribute<NotSerializedAttribute>() is null &&
                        x.GetMethod is not null &&
                        (x.GetMethod?.IsPublic ?? false) &&
                        (x.SetMethod?.IsPublic ?? false))
                    .Select(x => x.GetMethod!).ToArray());
        }

        MethodInfo[] propMethods = PropertyGetterMethodsMap[type];
        foreach (var propMethod in propMethods)
        {
            var propVal = propMethod.Invoke(cls, null);
            SerializeInternal(stream, propVal);
        }

        if (!FieldGetterMethodsMap.ContainsKey(type))
        {
            FieldGetterMethodsMap.Add(type,
                type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .Where(x =>
                        x.GetCustomAttribute<SerializedAttribute>() is not null)
                    .Select<FieldInfo, Func<object?, object?>>(x => x.GetValue)
                    .ToArray());
        }

        Func<object?, object?>[] fieldMethods = FieldGetterMethodsMap[type];
        foreach (Func<object?, object?> f in fieldMethods)
        {
            var fieldVal = f(cls);
            SerializeInternal(stream, fieldVal);
        }
    }
}
