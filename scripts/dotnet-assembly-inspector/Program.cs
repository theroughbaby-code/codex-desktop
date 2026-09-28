using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json;

if (args.Length < 2)
{
    Console.Error.WriteLine("Usage: AssemblyMetadataInspector <output.json> <assembly.dll> [assembly.dll ...]");
    return 2;
}

try
{
    var results = args.Skip(1).Select(InspectAssembly).ToArray();
    var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    await File.WriteAllTextAsync(args[0], JsonSerializer.Serialize(results, options));
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine(exception.Message);
    return 1;
}

static AssemblyInspection InspectAssembly(string assemblyPath)
{
    using var stream = File.OpenRead(assemblyPath);
    using var peReader = new PEReader(stream, PEStreamOptions.PrefetchMetadata);
    if (!peReader.HasMetadata)
    {
        throw new InvalidDataException($"{assemblyPath} is not a managed .NET assembly.");
    }

    var reader = peReader.GetMetadataReader();
    if (!reader.IsAssembly)
    {
        throw new InvalidDataException($"{assemblyPath} contains managed metadata but is not an assembly.");
    }

    var definition = reader.GetAssemblyDefinition();
    var attributes = ReadAssemblyAttributes(reader, definition);
    var typeNames = reader.TypeDefinitions
        .Select(handle => FullyQualifiedTypeDefinitionName(reader, handle))
        .Where(name => name != "<Module>")
        .Order(StringComparer.Ordinal)
        .ToArray();

    attributes.TryGetValue("System.Reflection.AssemblyFileVersionAttribute", out var fileVersion);
    attributes.TryGetValue("System.Reflection.AssemblyInformationalVersionAttribute", out var informationalVersion);

    return new AssemblyInspection(
        Path.GetFullPath(assemblyPath),
        reader.GetString(definition.Name),
        definition.Version.ToString(),
        fileVersion,
        informationalVersion,
        typeNames);
}

static Dictionary<string, string?> ReadAssemblyAttributes(
    MetadataReader reader,
    AssemblyDefinition definition)
{
    var attributes = new Dictionary<string, string?>(StringComparer.Ordinal);
    foreach (var handle in definition.GetCustomAttributes())
    {
        var attribute = reader.GetCustomAttribute(handle);
        var typeName = AttributeTypeName(reader, attribute);
        if (typeName is not (
            "System.Reflection.AssemblyFileVersionAttribute" or
            "System.Reflection.AssemblyInformationalVersionAttribute"))
        {
            continue;
        }

        var blob = reader.GetBlobReader(attribute.Value);
        if (blob.RemainingBytes < 2 || blob.ReadUInt16() != 1) continue;
        attributes[typeName] = blob.RemainingBytes > 0 ? blob.ReadSerializedString() : null;
    }
    return attributes;
}

static string? AttributeTypeName(MetadataReader reader, CustomAttribute attribute)
{
    EntityHandle declaringType;
    switch (attribute.Constructor.Kind)
    {
        case HandleKind.MemberReference:
            declaringType = reader.GetMemberReference((MemberReferenceHandle)attribute.Constructor).Parent;
            break;
        case HandleKind.MethodDefinition:
            declaringType = reader.GetMethodDefinition((MethodDefinitionHandle)attribute.Constructor).GetDeclaringType();
            break;
        default:
            return null;
    }

    return declaringType.Kind switch
    {
        HandleKind.TypeDefinition => FullyQualifiedTypeDefinitionName(reader, (TypeDefinitionHandle)declaringType),
        HandleKind.TypeReference => FullyQualifiedTypeReferenceName(reader, (TypeReferenceHandle)declaringType),
        _ => null,
    };
}

static string FullyQualifiedTypeDefinitionName(MetadataReader reader, TypeDefinitionHandle handle)
{
    var type = reader.GetTypeDefinition(handle);
    return JoinNamespace(reader.GetString(type.Namespace), reader.GetString(type.Name));
}

static string FullyQualifiedTypeReferenceName(MetadataReader reader, TypeReferenceHandle handle)
{
    var type = reader.GetTypeReference(handle);
    return JoinNamespace(reader.GetString(type.Namespace), reader.GetString(type.Name));
}

static string JoinNamespace(string namespaceName, string typeName) =>
    string.IsNullOrEmpty(namespaceName) ? typeName : $"{namespaceName}.{typeName}";

internal sealed record AssemblyInspection(
    string Path,
    string AssemblyName,
    string AssemblyVersion,
    string? FileVersion,
    string? InformationalVersion,
    string[] TypeNames);
