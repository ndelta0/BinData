using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace BinData;

[SkipLocalsInit]
internal static class BinaryStreamReader
{
    public static unsafe void WriteEnum<TEnum>(TEnum value, Stream stream) where TEnum : unmanaged, Enum
    {
        if (sizeof(TEnum) == 1)
        {
            stream.WriteByte(Unsafe.As<TEnum, byte>(ref value));
            return;
        }
        else
        {
            Span<byte> buffer = stackalloc byte[sizeof(TEnum)];
            if (sizeof(TEnum) == 2)
            {
                short temp = Unsafe.As<TEnum, short>(ref value);
                BinaryPrimitives.WriteInt16LittleEndian(buffer, temp);
            }
            else if (sizeof(TEnum) == 4)
            {
                int temp = Unsafe.As<TEnum, int>(ref value);
                BinaryPrimitives.WriteInt32LittleEndian(buffer, temp);
            }
            else if (sizeof(TEnum) == 8)
            {
                long temp = Unsafe.As<TEnum, long>(ref value);
                BinaryPrimitives.WriteInt64LittleEndian(buffer, temp);
            }
            stream.Write(buffer);
        }
    }

    public static unsafe TEnum ReadEnum<TEnum>(Stream stream) where TEnum : unmanaged, Enum
    {
        if (sizeof(TEnum) == 1)
        {
            int temp = stream.ReadByte();
            if (temp == -1)
                ThrowHelper.ThrowEndOfStreamException();
            return Unsafe.As<int, TEnum>(ref temp);
        }

        Span<byte> buffer = stackalloc byte[sizeof(TEnum)];
        if (stream.Read(buffer) < sizeof(TEnum))
            ThrowHelper.ThrowEndOfStreamException();

        if (sizeof(TEnum) == 2)
        {
            short temp = BinaryPrimitives.ReadInt16LittleEndian(buffer);
            return Unsafe.As<short, TEnum>(ref temp);
        }
        else if (sizeof(TEnum) == 4)
        {
            int temp = BinaryPrimitives.ReadInt32LittleEndian(buffer);
            return Unsafe.As<int, TEnum>(ref temp);
        }
        else
        {
            long temp = BinaryPrimitives.ReadInt64LittleEndian(buffer);
            return Unsafe.As<long, TEnum>(ref temp);
        }
    }
}
