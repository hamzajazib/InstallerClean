// The reader behind check-native-imports.ps1, compiled there by Add-Type. It takes the
// assemblies out of a single-file bundle, reads each one's metadata and IL, and builds the
// set of framework methods whose calls reach one of CoreLib's own ole32.dll or oleaut32.dll
// imports. A CoreLib import is bound by the runtime's own search, which no resolver of
// ours is asked about, so a call of ours into that set can make the runtime look for the
// library in the folder CoreLib was loaded from before it looks in System32.
//
// A METHOD IS KEYED BY TYPE, NAME AND FULL SIGNATURE, so Marshal.ReadInt32(IntPtr) and
// Marshal.ReadInt32(Object, Int32) are different members and only the second reaches an
// import. A type that is not public is keyed with its assembly as well, because several
// framework assemblies compile their own copy of the same internal type (Interop+Ole32,
// CertificateHelper) and an import in one copy says nothing about the others.
//
// The set follows call, callvirt, newobj, jmp, ldftn and ldvirtftn to the member each
// names, and from each of those and every static field read or write to the static
// constructor of the type it names, which the runtime runs before that first use. A
// P/Invoke whose signature the runtime marshals itself, in an assembly that does not
// disable runtime marshalling, is taken to call Marshal.AllocCoTaskMem and FreeCoTaskMem,
// which are what the runtime's marshalling stubs call for strings, buffers and structures.
// The set does not follow a virtual or interface call to the overrides it can reach: with
// those edges an enumerator reaches nearly everything and the set means nothing. A method
// whose IL or signature cannot be read stops the run rather than leaving the set short of
// it.
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;

namespace NativeImports
{
    public sealed class BundleEntry
    {
        public string Path;
        public int Kind;
        public bool Compressed;
        public byte[] Bytes;
    }

    // A single-file bundle as the host reads it. The 32-byte signature follows the
    // manifest's offset; a version 6 manifest holds the entry count, the bundle ID, where
    // the deps.json and the runtimeconfig.json are, the flags, and then each entry's
    // offset, size, compressed size, kind and path. An entry with a compressed size other
    // than zero is stored as raw deflate and comes out at its size.
    public static class Bundle
    {
        static readonly byte[] Signature =
        {
            0x8b, 0x12, 0x02, 0xb9, 0x6a, 0x61, 0x20, 0x38, 0x72, 0x7b, 0x93, 0x02, 0x14, 0xd7, 0xa0, 0x32,
            0x13, 0xf5, 0xb9, 0xe6, 0xef, 0xae, 0x33, 0x18, 0xee, 0x3b, 0x2d, 0xce, 0x24, 0xb3, 0x6a, 0xae,
        };

        public static List<BundleEntry> Read(string path)
        {
            var data = File.ReadAllBytes(path);
            int at = data.AsSpan().IndexOf(Signature);
            if (at < 8) throw new InvalidDataException($"{path} carries no bundle signature.");
            var reader = new BinaryReader(new MemoryStream(data));
            long manifest = BitConverter.ToInt64(data, at - 8);
            if (manifest <= 0 || manifest >= data.Length) throw new InvalidDataException($"{path}'s manifest offset {manifest} is outside the file.");
            reader.BaseStream.Position = manifest;
            uint major = reader.ReadUInt32();
            reader.ReadUInt32();
            int count = reader.ReadInt32();
            if (major != 6) throw new InvalidDataException($"{path} has a version {major} bundle manifest, which this reader does not read.");
            if (count <= 0 || count > (data.Length - reader.BaseStream.Position) / 26) throw new InvalidDataException($"{path}'s manifest counts {count} entries, which its bytes cannot hold.");
            reader.ReadString();
            for (int i = 0; i < 5; i++) reader.ReadInt64();
            var entries = new List<BundleEntry>(count);
            for (int i = 0; i < count; i++)
            {
                long offset = reader.ReadInt64();
                long size = reader.ReadInt64();
                long compressedSize = reader.ReadInt64();
                int kind = reader.ReadByte();
                string file = reader.ReadString();
                long stored = compressedSize != 0 ? compressedSize : size;
                if (offset < 0 || size < 0 || compressedSize < 0 || offset + stored > manifest)
                    throw new InvalidDataException($"{path}'s entry {file} lies outside the bundle's data.");
                byte[] bytes;
                if (compressedSize != 0)
                {
                    using var inflate = new DeflateStream(new MemoryStream(data, (int)offset, (int)compressedSize), CompressionMode.Decompress);
                    using var output = new MemoryStream();
                    inflate.CopyTo(output);
                    bytes = output.ToArray();
                    if (bytes.Length != size)
                        throw new InvalidDataException($"{path}'s entry {file} inflates to {bytes.Length} bytes where its manifest gives {size}.");
                }
                else
                {
                    bytes = new byte[size];
                    Array.Copy(data, offset, bytes, 0, size);
                }
                entries.Add(new BundleEntry { Path = file.Replace('\\', '/'), Kind = kind, Compressed = compressedSize != 0, Bytes = bytes });
            }
            return entries;
        }

        public static string Sha256(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    public sealed class Sig : ISignatureTypeProvider<string, object>
    {
        readonly Assembly _assembly;
        public Sig(Assembly assembly) { _assembly = assembly; }
        public string GetPrimitiveType(PrimitiveTypeCode t) => t.ToString();
        public string GetTypeFromDefinition(MetadataReader r, TypeDefinitionHandle h, byte k) => _assembly.TypeName(h);
        public string GetTypeFromReference(MetadataReader r, TypeReferenceHandle h, byte k) => _assembly.TypeName(h);
        public string GetTypeFromSpecification(MetadataReader r, object c, TypeSpecificationHandle h, byte k) => r.GetTypeSpecification(h).DecodeSignature(this, c);
        public string GetSZArrayType(string e) => e + "[]";
        public string GetArrayType(string e, ArrayShape s) => e + "[" + new string(',', s.Rank - 1) + "]";
        public string GetByReferenceType(string e) => e + "&";
        public string GetPointerType(string e) => e + "*";
        public string GetPinnedType(string e) => e;
        public string GetGenericInstantiation(string g, ImmutableArray<string> a) => g + "<" + string.Join(",", a) + ">";
        public string GetGenericMethodParameter(object c, int i) => "!!" + i;
        public string GetGenericTypeParameter(object c, int i) => "!" + i;
        public string GetModifiedType(string m, string u, bool r) => u;
        public string GetFunctionPointerType(MethodSignature<string> s) => "fnptr(" + string.Join(",", s.ParameterTypes) + ")" + s.ReturnType;
    }

    public sealed class Import
    {
        public string Key;
        public string Type;
        public string Library;
        public string EntryPoint;
    }

    public sealed class Call
    {
        public string Caller;
        public string Callee;
        // How the caller reaches the callee where no call instruction names it: "address
        // taken", "static constructor" or "runtime marshalling stub".
        public string Note;
    }

    // One assembly's metadata, its P/Invokes and every call its methods make.
    public sealed class Assembly
    {
        public string Name;
        public string Sha256;
        public string BundlePath;
        public byte[] Bytes;
        public List<Import> Imports = new();
        public List<Call> Calls = new();
        public HashSet<string> Methods = new();
        public List<string> AssemblyReferences = new();
        public Dictionary<string, int> Callis = new();
        public string InformationalVersion;

        // The full names of the types each loaded assembly defines as other than public,
        // shared by every reader so a reference into another assembly is keyed the way that
        // assembly keys its own definition.
        public HashSet<string> NonPublic = new();
        Dictionary<string, HashSet<string>> _nonPublicByAssembly;

        PEReader _pe;
        MetadataReader _md;

        public static bool HasMetadata(byte[] bytes)
        {
            try
            {
                using var pe = new PEReader(new MemoryStream(bytes, false));
                return pe.HasMetadata && pe.GetMetadataReader().IsAssembly;
            }
            catch (BadImageFormatException) { return false; }
        }

        public static Assembly Open(byte[] bytes, string bundlePath)
        {
            var a = new Assembly { Bytes = bytes, BundlePath = bundlePath, Sha256 = Bundle.Sha256(bytes) };
            a._pe = new PEReader(new MemoryStream(bytes, false));
            a._md = a._pe.GetMetadataReader();
            a.Name = a._md.GetString(a._md.GetAssemblyDefinition().Name);
            foreach (var h in a._md.TypeDefinitions)
                if (!IsVisibleOutside(a._md, h)) a.NonPublic.Add(a.RawTypeName(h));
            foreach (var h in a._md.AssemblyReferences)
                a.AssemblyReferences.Add(a._md.GetString(a._md.GetAssemblyReference(h).Name));
            foreach (var h in a._md.CustomAttributes)
            {
                var attribute = a._md.GetCustomAttribute(h);
                if (attribute.Parent.Kind != HandleKind.AssemblyDefinition) continue;
                if (a.AttributeName(attribute) != "AssemblyInformationalVersionAttribute") continue;
                var value = a._md.GetBlobReader(attribute.Value);
                value.ReadUInt16();
                a.InformationalVersion = value.ReadSerializedString();
            }
            return a;
        }

        // A protected nested type is visible to a derived type in another assembly, which
        // names it through a reference, so it is keyed as visible.
        static bool IsVisibleOutside(MetadataReader md, TypeDefinitionHandle h)
        {
            var t = md.GetTypeDefinition(h);
            var visibility = t.Attributes & TypeAttributes.VisibilityMask;
            if (t.IsNested)
                return visibility is TypeAttributes.NestedPublic or TypeAttributes.NestedFamily or TypeAttributes.NestedFamORAssem
                    && IsVisibleOutside(md, t.GetDeclaringType());
            return visibility == TypeAttributes.Public;
        }

        bool DisablesRuntimeMarshalling()
        {
            foreach (var h in _md.GetAssemblyDefinition().GetCustomAttributes())
                if (AttributeName(_md.GetCustomAttribute(h)) == "DisableRuntimeMarshallingAttribute") return true;
            return false;
        }

        string AttributeName(CustomAttribute attribute)
        {
            var type = attribute.Constructor.Kind switch
            {
                HandleKind.MemberReference => _md.GetMemberReference((MemberReferenceHandle)attribute.Constructor).Parent,
                HandleKind.MethodDefinition => (EntityHandle)_md.GetMethodDefinition((MethodDefinitionHandle)attribute.Constructor).GetDeclaringType(),
                _ => default,
            };
            return type.Kind switch
            {
                HandleKind.TypeReference => _md.GetString(_md.GetTypeReference((TypeReferenceHandle)type).Name),
                HandleKind.TypeDefinition => _md.GetString(_md.GetTypeDefinition((TypeDefinitionHandle)type).Name),
                _ => null,
            };
        }

        // Whether the runtime writes a marshalling stub for this P/Invoke that can allocate or
        // free native memory: any parameter or return other than a primitive number, a char,
        // a bool, a pointer or a function pointer, or one carrying a marshalling directive.
        bool IsRuntimeMarshalled(MethodDefinition m)
        {
            var signature = m.DecodeSignature(new Sig(this), null);
            foreach (var type in signature.ParameterTypes.Prepend(signature.ReturnType))
                if (!IsPlain(type)) return true;
            foreach (var h in m.GetParameters())
                if (!_md.GetParameter(h).GetMarshallingDescriptor().IsNil) return true;
            return false;
        }

        static readonly HashSet<string> PlainTypes = new()
        {
            "Void", "Boolean", "Char", "SByte", "Byte", "Int16", "UInt16", "Int32", "UInt32", "Int64", "UInt64",
            "Single", "Double", "IntPtr", "UIntPtr",
        };

        static bool IsPlain(string type) =>
            PlainTypes.Contains(type) || type.EndsWith("*", StringComparison.Ordinal) || type.StartsWith("fnptr(", StringComparison.Ordinal);

        string RawTypeName(TypeDefinitionHandle h)
        {
            var t = _md.GetTypeDefinition(h);
            var name = _md.GetString(t.Name);
            if (t.IsNested) return RawTypeName(t.GetDeclaringType()) + "+" + name;
            var ns = _md.GetString(t.Namespace);
            return ns.Length > 0 ? ns + "." + name : name;
        }

        public string TypeName(TypeDefinitionHandle h)
        {
            var name = RawTypeName(h);
            return NonPublic.Contains(name) ? "[" + Name + "]" + name : name;
        }

        public string TypeName(TypeReferenceHandle h)
        {
            var name = RawReferenceName(h, out var scope);
            return scope != null && _nonPublicByAssembly != null && _nonPublicByAssembly.TryGetValue(scope, out var set) && set.Contains(name)
                ? "[" + scope + "]" + name
                : name;
        }

        // A nested reference names its enclosing type through its resolution scope, so the
        // name is built outwards to the reference that names an assembly or a module.
        string RawReferenceName(TypeReferenceHandle h, out string scope)
        {
            var t = _md.GetTypeReference(h);
            var name = _md.GetString(t.Name);
            switch (t.ResolutionScope.Kind)
            {
                case HandleKind.TypeReference:
                    return RawReferenceName((TypeReferenceHandle)t.ResolutionScope, out scope) + "+" + name;
                case HandleKind.AssemblyReference:
                    scope = _md.GetString(_md.GetAssemblyReference((AssemblyReferenceHandle)t.ResolutionScope).Name);
                    break;
                default:
                    scope = Name;
                    break;
            }
            var ns = _md.GetString(t.Namespace);
            return ns.Length > 0 ? ns + "." + name : name;
        }

        string TypeName(EntityHandle h) => h.Kind switch
        {
            HandleKind.TypeDefinition => TypeName((TypeDefinitionHandle)h),
            HandleKind.TypeReference => TypeName((TypeReferenceHandle)h),
            HandleKind.TypeSpecification => SpecificationName((TypeSpecificationHandle)h),
            _ => throw new BadImageFormatException($"a member's parent is a {h.Kind}"),
        };

        // A generic instantiation is named by its generic type, which is how the type's own
        // definition is keyed; any other specification (an array, a pointer) is decoded.
        string SpecificationName(TypeSpecificationHandle h)
        {
            var spec = _md.GetTypeSpecification(h);
            var blob = _md.GetBlobReader(spec.Signature);
            if (blob.ReadSignatureTypeCode() == SignatureTypeCode.GenericTypeInstance)
            {
                blob.ReadCompressedInteger();
                return TypeName(blob.ReadTypeHandle());
            }
            return spec.DecodeSignature(new Sig(this), null);
        }

        // The type that declares a static field a token names.
        string FieldOwner(EntityHandle h) => h.Kind switch
        {
            HandleKind.FieldDefinition => TypeName(_md.GetFieldDefinition((FieldDefinitionHandle)h).GetDeclaringType()),
            HandleKind.MemberReference => TypeName(_md.GetMemberReference((MemberReferenceHandle)h).Parent),
            _ => null,
        };

        static string Parameters(MethodSignature<string> s) => "(" + string.Join(",", s.ParameterTypes) + ")";

        public string MethodKey(MethodDefinitionHandle h)
        {
            var m = _md.GetMethodDefinition(h);
            return TypeName(m.GetDeclaringType()) + "::" + _md.GetString(m.Name) + Parameters(m.DecodeSignature(new Sig(this), null));
        }

        // The method a call token names, or null where it names a field or something else.
        public string MethodKey(EntityHandle h)
        {
            switch (h.Kind)
            {
                case HandleKind.MethodDefinition:
                    return MethodKey((MethodDefinitionHandle)h);
                case HandleKind.MemberReference:
                {
                    var m = _md.GetMemberReference((MemberReferenceHandle)h);
                    if (m.Parent.Kind == HandleKind.MethodDefinition) return MethodKey((MethodDefinitionHandle)m.Parent);
                    if (m.GetKind() != MemberReferenceKind.Method) return null;
                    return TypeName(m.Parent) + "::" + _md.GetString(m.Name) + Parameters(m.DecodeMethodSignature(new Sig(this), null));
                }
                case HandleKind.MethodSpecification:
                    return MethodKey(_md.GetMethodSpecification((MethodSpecificationHandle)h).Method);
                default:
                    return null;
            }
        }

        // Reads every method: its import where it has one, every call its IL makes, and its
        // calli instructions. Any method that cannot be read throws, naming it.
        public void ReadMethods(Dictionary<string, HashSet<string>> nonPublicByAssembly)
        {
            _nonPublicByAssembly = nonPublicByAssembly;
            bool runtimeMarshalling = !DisablesRuntimeMarshalling();
            foreach (var h in _md.MethodDefinitions)
            {
                var m = _md.GetMethodDefinition(h);
                string key;
                try { key = MethodKey(h); }
                catch (Exception e) { throw new InvalidDataException($"{Name}: method row {MetadataTokens.GetRowNumber(h)} cannot be keyed: {e.Message}", e); }
                Methods.Add(key);
                var import = m.GetImport();
                if (!import.Module.IsNil)
                {
                    var library = _md.GetString(_md.GetModuleReference(import.Module).Name);
                    Imports.Add(new Import { Key = key, Type = RawTypeName(m.GetDeclaringType()), Library = library, EntryPoint = _md.GetString(import.Name) });
                    if (runtimeMarshalling && IsRuntimeMarshalled(m))
                    {
                        Calls.Add(new Call { Caller = key, Callee = "System.Runtime.InteropServices.Marshal::AllocCoTaskMem(Int32)", Note = "runtime marshalling stub" });
                        Calls.Add(new Call { Caller = key, Callee = "System.Runtime.InteropServices.Marshal::FreeCoTaskMem(IntPtr)", Note = "runtime marshalling stub" });
                    }
                    continue;
                }
                if (m.RelativeVirtualAddress == 0) continue;
                try
                {
                    var il = _pe.GetMethodBody(m.RelativeVirtualAddress).GetILBytes();
                    foreach (var (opcode, token) in IL.Calls(il))
                    {
                        if (opcode == ILOpCode.Calli)
                        {
                            Callis[key] = Callis.TryGetValue(key, out var n) ? n + 1 : 1;
                            continue;
                        }
                        if (opcode is ILOpCode.Ldsfld or ILOpCode.Ldsflda or ILOpCode.Stsfld)
                        {
                            var owner = FieldOwner(MetadataTokens.EntityHandle(token));
                            if (owner != null) Calls.Add(new Call { Caller = key, Callee = owner + "::.cctor()", Note = "static constructor" });
                            continue;
                        }
                        var callee = MethodKey(MetadataTokens.EntityHandle(token));
                        if (callee == null) continue;
                        Calls.Add(new Call { Caller = key, Callee = callee, Note = opcode is ILOpCode.Ldftn or ILOpCode.Ldvirtftn ? "address taken" : null });
                        Calls.Add(new Call { Caller = key, Callee = callee.Substring(0, callee.IndexOf("::", StringComparison.Ordinal)) + "::.cctor()", Note = "static constructor" });
                    }
                }
                catch (Exception e) when (e is not InvalidDataException)
                {
                    throw new InvalidDataException($"{Name}: {key} cannot be read: {e.Message}", e);
                }
            }
        }
    }

    public static class IL
    {
        static readonly Dictionary<ushort, int> OperandSize = BuildSizes();

        static Dictionary<ushort, int> BuildSizes()
        {
            var sizes = new Dictionary<ushort, int>();
            foreach (ILOpCode op in Enum.GetValues(typeof(ILOpCode)))
                sizes[(ushort)op] = op switch
                {
                    ILOpCode.Switch => -1,
                    ILOpCode.Ldc_i8 or ILOpCode.Ldc_r8 => 8,
                    ILOpCode.Ldarg or ILOpCode.Ldarga or ILOpCode.Starg or ILOpCode.Ldloc or ILOpCode.Ldloca or ILOpCode.Stloc => 2,
                    _ when op.IsBranch() => op.GetBranchOperandSize(),
                    ILOpCode.Ldarg_s or ILOpCode.Ldarga_s or ILOpCode.Starg_s or ILOpCode.Ldloc_s or ILOpCode.Ldloca_s or ILOpCode.Stloc_s
                        or ILOpCode.Ldc_i4_s or ILOpCode.Unaligned => 1,
                    ILOpCode.Ldc_i4 or ILOpCode.Ldc_r4 or ILOpCode.Jmp or ILOpCode.Call or ILOpCode.Calli or ILOpCode.Callvirt
                        or ILOpCode.Cpobj or ILOpCode.Ldobj or ILOpCode.Ldstr or ILOpCode.Newobj or ILOpCode.Castclass or ILOpCode.Isinst
                        or ILOpCode.Unbox or ILOpCode.Ldfld or ILOpCode.Ldflda or ILOpCode.Stfld or ILOpCode.Ldsfld or ILOpCode.Ldsflda
                        or ILOpCode.Stsfld or ILOpCode.Stobj or ILOpCode.Box or ILOpCode.Newarr or ILOpCode.Ldelema or ILOpCode.Ldelem
                        or ILOpCode.Stelem or ILOpCode.Unbox_any or ILOpCode.Refanyval or ILOpCode.Mkrefany or ILOpCode.Ldtoken
                        or ILOpCode.Ldftn or ILOpCode.Ldvirtftn or ILOpCode.Initobj or ILOpCode.Constrained or ILOpCode.Sizeof => 4,
                    _ => 0,
                };
            return sizes;
        }

        // Every instruction that names a method or a static field, or calls through a pointer,
        // with its token.
        // An opcode the table does not know throws, rather than walking on misaligned.
        public static IEnumerable<(ILOpCode, int)> Calls(byte[] il)
        {
            int i = 0;
            while (i < il.Length)
            {
                ushort code = il[i++];
                if (code == 0xFE) code = (ushort)(0xFE00 | il[i++]);
                if (!OperandSize.TryGetValue(code, out var size)) throw new InvalidDataException($"opcode {code:X} at IL offset {i}");
                if (size == -1)
                {
                    int targets = BitConverter.ToInt32(il, i);
                    i += 4 + 4 * targets;
                    continue;
                }
                var op = (ILOpCode)code;
                if (op is ILOpCode.Call or ILOpCode.Callvirt or ILOpCode.Newobj or ILOpCode.Jmp or ILOpCode.Ldftn or ILOpCode.Ldvirtftn or ILOpCode.Calli
                    or ILOpCode.Ldsfld or ILOpCode.Ldsflda or ILOpCode.Stsfld)
                    yield return (op, BitConverter.ToInt32(il, i));
                i += size;
            }
        }
    }

    // The framework methods that reach one of CoreLib's imports of the given libraries through
    // calls, each with one path down to the import for printing. A path of at most maxDepth
    // members, the import counted, is followed, and a method whose shortest path is longer is
    // not in the set.
    public sealed class Closure
    {
        public const string CoreLib = "System.Private.CoreLib";
        public static readonly string[] BoundByTheRuntime = { "ole32.dll", "oleaut32.dll" };
        readonly string[] Libraries;

        public List<string> Sinks = new();
        public HashSet<string> Members = new();
        readonly Dictionary<string, string> _next = new();
        readonly Dictionary<string, string> _notes = new();
        readonly Dictionary<string, HashSet<string>> _callers;
        Dictionary<string, List<string>> _callees;

        public static string Library(string name)
        {
            name = name.ToLowerInvariant();
            return name.EndsWith(".dll", StringComparison.Ordinal) ? name : name + ".dll";
        }

        public Closure(IEnumerable<Assembly> assemblies) : this(assemblies, BoundByTheRuntime, int.MaxValue) { }

        public Closure(IEnumerable<Assembly> assemblies, string[] libraries, int maxDepth)
        {
            Libraries = libraries;
            var callers = new Dictionary<string, HashSet<string>>();
            var sinks = new List<string>();
            foreach (var a in assemblies)
            {
                if (a.Name == CoreLib)
                    foreach (var import in a.Imports)
                        if (Libraries.Contains(Library(import.Library)))
                        {
                            sinks.Add(import.Key);
                            Sinks.Add(import.Key + "  [" + Library(import.Library) + "!" + import.EntryPoint + "]");
                        }
                foreach (var call in a.Calls)
                {
                    if (!callers.TryGetValue(call.Callee, out var set)) callers[call.Callee] = set = new HashSet<string>();
                    set.Add(call.Caller);
                    if (call.Note != null) _notes.TryAdd(call.Caller + " -> " + call.Callee, call.Note);
                }
            }
            _callers = callers;
            Sinks.Sort(StringComparer.Ordinal);
            var queue = new Queue<string>(sinks);
            var depth = new Dictionary<string, int>();
            foreach (var s in sinks) { Members.Add(s); depth[s] = 1; }
            while (queue.Count > 0)
            {
                var callee = queue.Dequeue();
                if (depth[callee] >= maxDepth) continue;
                if (!callers.TryGetValue(callee, out var set)) continue;
                foreach (var caller in set.OrderBy(c => c, StringComparer.Ordinal))
                    if (Members.Add(caller)) { _next[caller] = callee; depth[caller] = depth[callee] + 1; queue.Enqueue(caller); }
            }
        }

        // Every member of the set the key reaches, itself included, following each edge the set
        // was built from. A change in what a listed call can reach changes this list.
        public List<string> Reach(string key)
        {
            if (_callees == null)
            {
                _callees = new Dictionary<string, List<string>>();
                foreach (var (callee, set) in _callers)
                    if (Members.Contains(callee))
                        foreach (var caller in set)
                            if (Members.Contains(caller))
                            {
                                if (!_callees.TryGetValue(caller, out var list)) _callees[caller] = list = new List<string>();
                                list.Add(callee);
                            }
            }
            var seen = new HashSet<string>();
            if (!Members.Contains(key)) return new List<string>();
            var queue = new Queue<string>();
            queue.Enqueue(key);
            seen.Add(key);
            while (queue.Count > 0)
                if (_callees.TryGetValue(queue.Dequeue(), out var next))
                    foreach (var n in next)
                        if (seen.Add(n)) queue.Enqueue(n);
            return seen.OrderBy(k => k, StringComparer.Ordinal).ToList();
        }

        public string Path(string key)
        {
            var parts = new List<string> { key };
            while (_next.TryGetValue(key, out var next))
            {
                parts.Add((_notes.TryGetValue(key + " -> " + next, out var note) ? "(" + note + ") " : "") + next);
                key = next;
            }
            return string.Join("\n  -> ", parts);
        }
    }
}
