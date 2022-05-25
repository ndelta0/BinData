namespace BinData;

public static partial class BinaryConvert
{
    public static byte[] Serialize(object? data)
    {
        using var stream = new MemoryStream();

        if (data is null)
        {
            stream.WriteByte(0);
            return stream.ToArray();
        }

        var type = data.GetType();
        var context = SerializationContext.Create(type);
        context.Write(data, stream);

        return stream.ToArray();
    }
}
