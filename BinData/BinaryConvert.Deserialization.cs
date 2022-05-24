namespace BinData;

public static partial class BinaryConvert
{
    public static T? Deserialize<T>(byte[] bytes) => (T?)Deserialize(bytes, typeof(T));

    public static object? Deserialize(byte[] bytes, Type type)
    {
        using var ms = new MemoryStream(bytes);

        var context = DeserializationContext.Create(type);
        return context.Read(ms);
    }
}
