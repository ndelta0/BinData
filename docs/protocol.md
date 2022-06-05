# BinData Protocol

BinData (de)serializes objects in a deterministic manner. All values are written/read as little-endian.

## Reference types

Types that are by-reference -- can be `null`, have a single byte prefix. The byte prefix is `1` when value is **not** `null` and `0` when value is `null`.
- `null` → `0`, no data follows
- not `null` → `1`, data follows

## Enumeration types

Enumeration types, known as `enum`s in C#, are (de)serialized as a number. Number type is the same as the [enum's underlying type](https://docs.microsoft.com/en-us/dotnet/csharp/language-reference/builtin-types/enum).

## Primitives

BinData supports all [C# primitive types](https://docs.microsoft.com/en-us/dotnet/csharp/language-reference/builtin-types/built-in-types). All of them are written/read as little-endian. There is a slight exception for the `decimal` type (16-byte floating-point number), which is written/read as four little-endian 4-byte signed integers.

## Strings

*See [reference types](#reference-types).* Strings are length-prefixed with a little-endian 4-byte signed integer. Length prefix tells you how many bytes there are (not how long the string is). Used encoding is UTF-8.

## Arrays and Lists

*See [reference types](#reference-types).* Arrays and `List<>`s are length-prefixed with a little-endian 4-byte signed integer.

## Classes

*See [reference types](#reference-types).* First, public instance properties are (de)serialized. Then all instance fields (even non-public) are (de)serialized. (De)serialization order of members is the same as the order in which they are declared.

## Structures

BinData allows for (de)serialization of `struct`s, **unless** they contain references. Structures are seen as a single sequence of bytes. On big-endian systems, the byte order is reversed to ensure little-endianness. (Note that this does not provide complete endianness-safety and may cause issues).

## Tuples

*Don't mistake with [value tuples](#value-tuples). See [reference types](#reference-types).* Tuples (de)serialize their members in the same order as they are declared.

## Value tuples

Value tuples serialize their members in the same order as they are declared.

### Tuples vs. Value tuples

The only difference is that, since `Tuple` is a reference type, it may be `null` and has a [byte prefix](#reference-types).

## Nullable value types
BinData supports [`Nullable<>`](https://docs.microsoft.com/en-us/dotnet/csharp/language-reference/builtin-types/nullable-value-types), which allows value types to be treated as [reference types](#reference-types).

## Interface shadowing

When deserializing certain interfaces, BinData selects implementation type:
- `IEnumerable<>` → `List<>`
