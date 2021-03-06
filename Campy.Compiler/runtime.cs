﻿namespace Campy.Compiler
{
    using MethodImplAttributes = Mono.Cecil.MethodImplAttributes;
    using Mono.Cecil.Cil;
    using Mono.Cecil;
    using Swigged.Cuda;
    using Swigged.LLVM;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Reflection;
    using System.Runtime.InteropServices;
    using System.Text.RegularExpressions;
    using System;
    using Utils;

    public class RUNTIME
    {
        [global::System.Runtime.InteropServices.DllImport(@"campy-runtime-wrapper", EntryPoint = "BclHeapAlloc")]
        public static extern System.IntPtr BclHeapAlloc(System.IntPtr bcl_type);

        [global::System.Runtime.InteropServices.DllImport(@"campy-runtime-wrapper", EntryPoint = "SetOptions")]
        public static extern void SetOptions(UInt64 options);

        [global::System.Runtime.InteropServices.DllImport(@"campy-runtime-wrapper", EntryPoint = "InitTheBcl")]
        public static extern void InitTheBcl(System.IntPtr a1, long a2, long a3, int a4);

        [global::System.Runtime.InteropServices.DllImport(@"campy-runtime-wrapper", EntryPoint = "InitFileSystem")]
        public static extern void InitFileSystem();

        [global::System.Runtime.InteropServices.DllImport(@"campy-runtime-wrapper", EntryPoint = "GfsAddFile")]
        public static extern void GfsAddFile(System.IntPtr name, System.IntPtr file, long length, System.IntPtr result);

        [global::System.Runtime.InteropServices.DllImport(@"campy-runtime-wrapper", EntryPoint = "InitializeBCL1")]
        public static extern void InitializeBCL1();

        [global::System.Runtime.InteropServices.DllImport(@"campy-runtime-wrapper", EntryPoint = "InitializeBCL2")]
        public static extern void InitializeBCL2();

        [global::System.Runtime.InteropServices.DllImport(@"campy-runtime-wrapper", EntryPoint = "CheckHeap")]
        public static extern void CheckHeap();

        [global::System.Runtime.InteropServices.DllImport(@"campy-runtime-wrapper", EntryPoint = "GcCollect")]
        public static extern void GcCollect();

        [global::System.Runtime.InteropServices.DllImport(@"campy-runtime-wrapper", EntryPoint = "BclAllocString")]
        public static extern IntPtr BclAllocString(int length, IntPtr chars);


        [global::System.Runtime.InteropServices.DllImport(@"campy-runtime-wrapper", EntryPoint = "BclArrayAlloc")]
        public static extern System.IntPtr BclArrayAlloc(
            System.IntPtr bcl_type,
            int rank,
            uint[] lengths);




        // This table encodes runtime type information for rewriting BCL types. Use this to determine
        // what a type (represented in Mono.Cecil.TypeReference) in the user's program maps to
        // in the GPU base class layer (also represented in Mono.Cecil.TypeReference).
        private static Dictionary<TypeReference, TypeReference> _substituted_bcl = new Dictionary<TypeReference, TypeReference>();
 
        private static Dictionary<string, TypeRef> _ptx_type_to_llvm_typeref = new Dictionary<string, TypeRef>()
        {
            {".b8", LLVM.Int8Type()},
            {".b16", LLVM.Int16Type()},
            {".b32", LLVM.Int32Type()},
            {".b64", LLVM.Int64Type()},
        };

        public RUNTIME()
        {
        }

        public static void ThrowArgumentOutOfRangeException()
        {
        }

        public class BclNativeMethod
        {
            public TypeReference _bcl_type;
            public MethodDefinition _md;
            public string _nameSpace;
            public string _type;
            public string _full_name;
            public string _short_name;
            public string _native_name;
            public TypeReference _returnType;
            public List<Mono.Cecil.ParameterDefinition> _parameterTypes;

            public BclNativeMethod(TypeReference bcl_type, MethodDefinition md)
            {
                _bcl_type = bcl_type;
                _md = md;
                _nameSpace = bcl_type.Namespace;
                _type = bcl_type.FullName;
                _full_name = md.FullName;
                _short_name = md.Name;
                _returnType = md.ReturnType;
                _parameterTypes = md.Parameters.ToList();
                // Unfortunately, I don't know the C++ name decoration rules in the NVCC compiler. Further,
                // DotNetAnywhere originally didn't implement all the "internal call"-labeled attributed methods in Corlib.
                // Further, the only table that does make note of the internal call-labeled methods was in C. So,
                // for Campy, the BCL was extended with another attribute, GPUBCLAttribute, to indicate the
                // name of the native call, making it visible to C#. The following code grabs and caches this information.
                var cust_attrs = md.CustomAttributes;
                if (cust_attrs.Count > 0)
                {
                    var a = cust_attrs.First();
                    if (a.AttributeType.FullName == "System.GPUBCLAttribute")
                    {
                        var arg = a.ConstructorArguments.First();
                        var v = arg.Value;
                        var s = (string)v;
                        _native_name = s;
                        //string mangled_name = "_Z" + _native_name.Length + _native_name + "PhS_S_";
                        //CampyConverter.built_in_functions.Add(mangled_name,
                        //    LLVM.AddFunction(
                        //        CampyConverter.global_llvm_module,
                        //        mangled_name,
                        //        LLVM.FunctionType(LLVM.Int64Type(),
                        //            new TypeRef[]
                        //            {
                        //                    LLVM.PointerType(LLVM.VoidType(), 0), // "this"
                        //                    LLVM.PointerType(LLVM.VoidType(), 0), // params in a block.
                        //                    LLVM.PointerType(LLVM.VoidType(), 0) // return value block.
                        //            }, false)));
                    }
                }
            }
        }

        public class PtxFunction
        {
            public string _mangled_name;
            public string _short_name;
            public ValueRef _valueref;

            public PtxFunction(string mangled_name, ValueRef value_ref)
            {
                _mangled_name = mangled_name;
                _valueref = value_ref;
            }
        }

        // This table encodes runtime type information for rewriting internal calls in the native portion of
        // the BCL for the GPU. It was originally encoded in dna/internal.c. However, it's easier and
        // safer to derive the information from the C# portion of the BCL using System.Reflection.
        //
        // Why is this information needed? In Inst.c, I need to make a call of a function to the runtime.
        // I only have PTX files, which removes the type information from the signature of
        // the original call (it is all three parameters of void*).
        private static List<BclNativeMethod> _internalCalls = new List<BclNativeMethod>();

        // This table is a record of all '.visible' functions in a generated PTX file. Use this name when calling
        // functions in PTX/LLVM.
        private static List<PtxFunction> _ptx_functions = new List<PtxFunction>();

        private class InternalCallEnumerable : IEnumerable<BclNativeMethod>
        {
            public InternalCallEnumerable()
            {
            }

            public IEnumerator<BclNativeMethod> GetEnumerator()
            {
                foreach (var key in _internalCalls)
                {
                    yield return key;
                }
            }

            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
            {
                return GetEnumerator();
            }
        }

        public static IEnumerable<BclNativeMethod> BclNativeMethods
        {
            get
            {
                return new InternalCallEnumerable();
            }
        }

        public static IEnumerable<PtxFunction> PtxFunctions
        {
            get
            {
                return _ptx_functions;
            }
        }

        public static string FindNativeCoreLib()
        {
            try
            {
                // Let's try the obvious, in the same directory as Campy.Utils.dll.
                var path_of_campy = Campy.Utils.CampyInfo.PathOfCampy();
                var suffix = Campy.Utils.CampyEnv.IsLinux ? ".a" : ".lib";
                string full_path_assem = path_of_campy + Path.DirectorySeparatorChar + "campy-runtime-native" + suffix;
                Stream stream = new FileStream(full_path_assem, FileMode.Open, FileAccess.Read, FileShare.Read);
                stream.Close();
                return full_path_assem;
            }
            catch (Exception)
            {
            }

            // Try something else...

            try
            {
                // Let's say it's in the Nuget packages directory. Go up a few levels and look for it in "contents" directory.
                // .../.nuget/packages/campy/0.0.4/lib/netstandard2.0/Campy.Utils.dll
                // =>
                // .../.nuget/packages/campy/0.0.4/contents/corlib.dll.
                var path_of_campy = Campy.Utils.CampyInfo.PathOfCampy();
                var suffix = Campy.Utils.CampyEnv.IsLinux ? ".a" : ".lib";
                string full_path_assem = path_of_campy + Path.DirectorySeparatorChar
                                                       + ".." + Path.DirectorySeparatorChar
                                                       + ".." + Path.DirectorySeparatorChar
                                                       + "content" + Path.DirectorySeparatorChar
                                                       + "campy-runtime-native"
                                                       + suffix;
                full_path_assem = Path.GetFullPath(full_path_assem);
                Stream stream = new FileStream(full_path_assem, FileMode.Open, FileAccess.Read, FileShare.Read);
                stream.Close();
                return full_path_assem;
            }
            catch (Exception)
            {
            }

            // Try something else...

            try
            {
                // Let's try the calling executable directory.
                var dir = Path.GetDirectoryName(Path.GetFullPath(System.Reflection.Assembly.GetEntryAssembly().Location));
                var suffix = Campy.Utils.CampyEnv.IsLinux ? ".a" : ".lib";
                string full_path_assem = dir + Path.DirectorySeparatorChar + "campy-runtime-native" + suffix;
                full_path_assem = Path.GetFullPath(full_path_assem);
                Stream stream = new FileStream(full_path_assem, FileMode.Open, FileAccess.Read, FileShare.Read);
                stream.Close();
                return full_path_assem;
            }
            catch (Exception)
            {
            }

            // Try something else...

            try
            {
                // This could be a unit test in Campy. If that is true, then look in the standard directory structure
                // for Campy source/object. It should have actually copied the damn corlib.dll to the output executable directory,
                // but someone set up the test wrong. Anyways, assume that the project is up to date, and load from Campy.Runtime.
                // ../../../../../Campy.Runtime/Corlib/bin/Debug/net20/
                var path_of_campy = @"../../../../../x64/Debug";
                var suffix = Campy.Utils.CampyEnv.IsLinux ? ".a" : ".lib";
                string full_path_assem = path_of_campy + Path.DirectorySeparatorChar + "campy-runtime-native" + suffix;
                full_path_assem = Path.GetFullPath(full_path_assem);
                Stream stream = new FileStream(full_path_assem, FileMode.Open, FileAccess.Read, FileShare.Read);
                stream.Close();
                return full_path_assem;
            }
            catch (Exception)
            {
            }

            // Fuck. I have no idea.
            return null;
        }

        public static string FindCoreLib()
        {
            try
            {
                // Let's try the obvious, in the same directory as Campy.Utils.dll.
                var path_of_campy = Campy.Utils.CampyInfo.PathOfCampy();
                string full_path_assem = path_of_campy + Path.DirectorySeparatorChar + "corlib.dll";
                Stream stream = new FileStream(full_path_assem, FileMode.Open, FileAccess.Read, FileShare.Read);
                stream.Close();
                return full_path_assem;
            }
            catch (Exception)
            {
            }

            // Try something else...

            try
            {
                // Let's say it's in the Nuget packages directory. Go up a few levels and look for it in "contents" directory.
                // .../.nuget/packages/campy/0.0.4/lib/netstandard2.0/Campy.Utils.dll
                // =>
                // .../.nuget/packages/campy/0.0.4/contents/corlib.dll.
                var path_of_campy = Campy.Utils.CampyInfo.PathOfCampy();
                string full_path_assem = path_of_campy + Path.DirectorySeparatorChar
                                                       + ".." + Path.DirectorySeparatorChar
                                                       + ".." + Path.DirectorySeparatorChar
                                                       + "lib" + Path.DirectorySeparatorChar
                                                       + "native" + Path.DirectorySeparatorChar
                                                       + "corlib.dll";
                full_path_assem = Path.GetFullPath(full_path_assem);
                Stream stream = new FileStream(full_path_assem, FileMode.Open, FileAccess.Read, FileShare.Read);
                stream.Close();
                return full_path_assem;
            }
            catch (Exception)
            {
            }

            System.Diagnostics.StackTrace callStack = new System.Diagnostics.StackTrace();
            for (int i = 0; i < callStack.FrameCount; i++)
            {
                System.Diagnostics.StackFrame sf = callStack.GetFrame(i);
                try
                {
                    // Let's try going up the stack, looking for anything.
                    var m = sf.GetMethod();
                    var p = m.DeclaringType.Assembly.Location;
                    var dir = Path.GetDirectoryName(Path.GetFullPath(p));
                    string full_path_assem = dir + Path.DirectorySeparatorChar
                                                           + "corlib.dll";
                    full_path_assem = Path.GetFullPath(full_path_assem);
                    Stream stream = new FileStream(full_path_assem, FileMode.Open, FileAccess.Read, FileShare.Read);
                    stream.Close();
                    return full_path_assem;
                }
                catch (Exception)
                {
                }
            }

            try
            {
                // Let's try the calling executable directory.
                var p = typeof(RUNTIME).Assembly.Location;
                var dir = Path.GetDirectoryName(Path.GetFullPath(p));
                string full_path_assem = dir + Path.DirectorySeparatorChar
                                                       + "corlib.dll";
                full_path_assem = Path.GetFullPath(full_path_assem);
                Stream stream = new FileStream(full_path_assem, FileMode.Open, FileAccess.Read, FileShare.Read);
                stream.Close();
                return full_path_assem;
            }
            catch (Exception)
            {
            }

            // Try something else...
            // Panic, assume this is somewhere within the Campy directory.
            // go up the tree and try to find.
            for (int level = 0; level < 10; ++level)
            {
                try
                {
                    // This could be a unit test in Campy. If that is true, then look in the standard directory structure
                    // for Campy source/object. It should have actually copied the damn corlib.dll to the output executable directory,
                    // but someone set up the test wrong. Anyways, assume that the project is up to date, and load from Campy.Runtime.
                    // ../../../../../Campy.Runtime/Corlib/bin/Debug/net20/
                    var prefix = string.Concat(Enumerable.Repeat("../", level));
                    var path_of_campy = prefix + @"Campy.Runtime/Corlib/bin/Debug/netstandard2.0";
                    string full_path_assem = path_of_campy + Path.DirectorySeparatorChar + "corlib.dll";
                    full_path_assem = Path.GetFullPath(full_path_assem);
                    Stream stream = new FileStream(full_path_assem, FileMode.Open, FileAccess.Read, FileShare.Read);
                    stream.Close();
                    return full_path_assem;
                }
                catch (Exception)
                {
                }
            }

            // Fuck. I have no idea.
            return null;
        }

        public static void Initialize()
        {
            // Load C# library for BCL, and grab all types and methods.
            // The tables that this method sets up are:
            // _substituted_bcl -- maps types in program (represented in Mono.Cecil) into GPU BCL types (represented in Mono.Cecil).
            // _system_type_to_mono_type_for_bcl -- associates types in GPU BCL with NET Core/NET Framework/... in user program.
            // Note, there seems to be an underlying bug in System.Type.GetType for certain generics, like System.Collections.Generic.HashSet.
            // The method returns null.
            var xx = typeof(System.Collections.Generic.HashSet<>);
            var x2 = typeof(System.Collections.Generic.HashSet<int>);
            var yy = System.Type.GetType("System.Collections.Generic.HashSet");
            var y2 = System.Type.GetType("System.Collections.Generic.HashSet<>");
            var y3 = System.Type.GetType("System.Collections.Generic.HashSet`1");
            var y4 = System.Type.GetType("System.Collections.Generic.HashSet<T>");
            var y5 = System.Type.GetType(xx.FullName);
            var y6 = System.Type.GetType(@"System.Collections.Generic.HashSet`1[System.Int32]");
            var y7 = System.Type.GetType(@"System.Collections.Generic.Dictionary`2[System.String,System.String]");
            var y8 = System.Type.GetType(x2.FullName);

            // Set up _substituted_bcl.
            var runtime = new RUNTIME();

            // Find corlib.dll. It could be anywhere, but let's check the usual spots.
            Mono.Cecil.ModuleDefinition md = Mono.Cecil.ModuleDefinition.ReadModule(FindCoreLib());
            foreach (var bcl_type in md.GetTypes())
            {
                // Filter out <Module> and <PrivateImplementationDetails>, among possible others.
                Regex regex = new Regex(@"^[<]\w+[>]");
                if (regex.IsMatch(bcl_type.FullName)) continue;

                // Try to map the type into native NET type. Some things just won't.
                var t_system_type = System.Type.GetType(bcl_type.FullName);
                if (t_system_type == null) continue;

                var to_mono = t_system_type.ToMonoTypeReference();

                // Add entry for converting intrinsic NET BCL type to GPU BCL type.
                _substituted_bcl.Add(to_mono, bcl_type);

                foreach (var m in bcl_type.Methods)
                {
                    var x = m.ImplAttributes;
                    if ((x & MethodImplAttributes.InternalCall) != 0)
                    {
                        if (Campy.Utils.Options.IsOn("runtime_trace"))
                            System.Console.WriteLine("Internal call set up " + bcl_type + " " + m);

                        _internalCalls.Add(new BclNativeMethod(bcl_type, m));
                    }
                }
            }

            // Set up _system_type_to_mono_type_for_bcl.
            // There really isn't any good way to set this up because NET Core System.Reflection does not work
            // on .LIB files. So begins the kludge...
            // Parse PTX files for all "visible" functions, and create LLVM declarations.
            // For "Internal Calls", these functions appear here, but also on the _internalCalls list.
            var assembly = Assembly.GetAssembly(typeof(Campy.Compiler.RUNTIME));
            var resource_names = assembly.GetManifestResourceNames();
            foreach (var resource_name in resource_names)
            {
                using (Stream stream = assembly.GetManifestResourceStream(resource_name))
                using (StreamReader reader = new StreamReader(stream))
                {
                    string gpu_bcl_ptx = reader.ReadToEnd();
                    // Parse the PTX for ".visible" functions, and enter each in
                    // the runtime table.

                    // This should match ".visible" <spaces> ".func" <spaces> <function-return>? <function-name>
                    // over many lines, many times.
                    Regex regex = new Regex(
          @"\.visible\s+\.func\s+(?<return>[(][^)]*[)]\s+)?(?<name>\w+)\s*(?<params>[(][^)]*[)]\s+)");
                    foreach (Match match in regex.Matches(gpu_bcl_ptx))
                    {
                        Regex space = new Regex(@"\s\s+");
                        string mangled_name = match.Groups["name"].Value.Trim();
                        string return_type = match.Groups["return"].Value.Trim();
                        return_type = space.Replace(return_type, " ");
                        string parameters = match.Groups["params"].Value.Trim();
                        parameters = space.Replace(parameters, " ");

                        if (Campy.Utils.Options.IsOn("runtime_trace"))
                            System.Console.WriteLine(mangled_name + " " + return_type + " " + parameters);

                        if (JITER.functions_in_internal_bcl_layer.ContainsKey(mangled_name)) continue;

                        TypeRef llvm_return_type = default(TypeRef);
                        TypeRef[] args;

                        // Construct LLVM extern that corresponds to type of function.
                        // Parse return_type and parameters strings...
                        Regex param_regex = new Regex(@"\.param(\s+\.align\s+\d+)?\s+(?<type>\S+)\s");

                        {
                            // parse return.
                            if (return_type == "")
                                llvm_return_type = LLVM.VoidType();
                            else
                            {
                                foreach (Match ret in param_regex.Matches(return_type))
                                {
                                    var x = ret.Groups["type"].Value;
                                    _ptx_type_to_llvm_typeref.TryGetValue(
                                        x, out TypeRef y);
                                    if (Campy.Utils.Options.IsOn("runtime_trace"))
                                        System.Console.WriteLine("name " + x + "  value " + y.ToString());
                                    llvm_return_type = y;
                                }
                            }
                        }

                        {
                            // parse parameters.
                            List<TypeRef> args_list = new List<TypeRef>();
                            foreach (Match ret in param_regex.Matches(parameters))
                            {
                                var x = ret.Groups["type"].Value;
                                if (!_ptx_type_to_llvm_typeref.TryGetValue(
                                    x, out TypeRef y))
                                    throw new Exception("Unknown type syntax in ptx parameter.");
                                if (Campy.Utils.Options.IsOn("runtime_trace"))
                                {
                                    System.Console.Write("parameter ");

                                    System.Console.WriteLine("name " + x + "  value " + y.ToString());
                                }
                                args_list.Add(y);
                            }
                            args = args_list.ToArray();
                        }

                        var decl = LLVM.AddFunction(
                            JITER.global_llvm_module,
                            mangled_name,
                            LLVM.FunctionType(
                                llvm_return_type,
                                args,
                                false));

                        PtxFunction ptxf = new PtxFunction(mangled_name, decl);
                        if (Campy.Utils.Options.IsOn("runtime_trace"))
                            System.Console.WriteLine(ptxf._mangled_name
                                                 + " "
                                                 + ptxf._short_name
                                                 + " "
                                                 + ptxf._valueref);

                        JITER.functions_in_internal_bcl_layer.Add(mangled_name, decl);
                        _ptx_functions.Add(ptxf);
                    }
                }
            }
        }

        public static IntPtr RuntimeCubinImage
        {
            get; private set;
        }

        public static ulong RuntimeCubinImageSize
        {
            get; private set;
        }

        private static Dictionary<IntPtr, CUmodule> cached_modules = new Dictionary<IntPtr, CUmodule>();

        public static CUmodule InitializeModule(IntPtr cubin)
        {
            if (cached_modules.TryGetValue(cubin, out CUmodule value))
            {
                return value;
            }
            uint num_ops = 0;
            var op = new CUjit_option[num_ops];
            ulong[] op_values = new ulong[num_ops];

            var op_values_link_handle = GCHandle.Alloc(op_values, GCHandleType.Pinned);
            var op_values_link_intptr = op_values_link_handle.AddrOfPinnedObject();

            CUresult res = Cuda.cuModuleLoadDataEx(out CUmodule module, cubin, 0, op, op_values_link_intptr);
            CudaHelpers.CheckCudaError(res);
            cached_modules[cubin] = module;
            return module;
        }

        public static CUmodule RuntimeModule
        {
            get; set;
        }

        public static TypeReference FindBCLType(System.Type type)
        {
            var runtime = new RUNTIME();
            TypeReference result = null;
            Mono.Cecil.ModuleDefinition md = Mono.Cecil.ModuleDefinition.ReadModule(FindCoreLib());
            foreach (var bcl_type in md.GetTypes())
            {
                if (bcl_type.FullName == type.FullName)
                    return bcl_type;
            }
            return result;
        }

        public static CUfunction _Z22Initialize_BCL_GlobalsPvyiPP6_BCL_t(CUmodule module)
        {
            CudaHelpers.CheckCudaError(Cuda.cuModuleGetFunction(out CUfunction function, module, "_Z22Initialize_BCL_GlobalsPvyiPP6_BCL_t"));
            return function;
        }

        public static CUfunction _Z15Initialize_BCL1v(CUmodule module)
        {
            CudaHelpers.CheckCudaError(Cuda.cuModuleGetFunction(out CUfunction function, module, "_Z15Initialize_BCL1v"));
            return function;
        }

        public static CUfunction _Z15Initialize_BCL2v(CUmodule module)
        {
            CudaHelpers.CheckCudaError(Cuda.cuModuleGetFunction(out CUfunction function, module, "_Z15Initialize_BCL2v"));
            return function;
        }

        public static CUfunction _Z12Bcl_Gfs_initv(CUmodule module)
        {
            CudaHelpers.CheckCudaError(Cuda.cuModuleGetFunction(out CUfunction function, module, "_Z12Bcl_Gfs_initv"));
            return function;
        }

        public static CUfunction _Z16Bcl_Gfs_add_filePcS_yPi(CUmodule module)
        {
            CudaHelpers.CheckCudaError(Cuda.cuModuleGetFunction(out CUfunction function, module, "_Z16Bcl_Gfs_add_filePcS_yPi"));
            return function;
        }

        public static CUfunction _Z15Set_BCL_GlobalsP6_BCL_t(CUmodule module)
        {
            CudaHelpers.CheckCudaError(Cuda.cuModuleGetFunction(out CUfunction function, module, "_Z15Set_BCL_GlobalsP6_BCL_t"));
            return function;
        }

        public static IntPtr BclPtr { get; set; }
        public static ulong BclPtrSize { get; set; }

        public static TypeReference RewriteType(TypeReference tr)
        {
            foreach (var kv in _substituted_bcl)
            {
                if (kv.Key.FullName == tr.FullName)
                    tr = kv.Value;
            }
            return tr;
        }


        public static MethodReference SubstituteMethod(MethodReference method_reference)
        {
            // Can't do anything if method isn't associated with a type.
            TypeReference declaring_type = method_reference.DeclaringType;
            if (declaring_type == null)
                return null; 

            TypeDefinition declaring_type_resolved = declaring_type.Resolve();
            MethodDefinition method_definition = method_reference.Resolve();

            // Can't do anything if the type isn't part of Campy BCL.
            Mono.Cecil.ModuleDefinition campy_bcl_runtime = Mono.Cecil.ModuleDefinition.ReadModule(FindCoreLib());
            TypeReference substituted_declaring_type = declaring_type.SubstituteMonoTypeReference(campy_bcl_runtime);
            if (substituted_declaring_type == null)
                return null;

            TypeDefinition substituted_declaring_type_definition = substituted_declaring_type.Resolve();
            if (substituted_declaring_type_definition == null)
                return null;

            var substituted_method_definition = substituted_declaring_type_definition.Methods
                .Where(m =>
                {
                    if (m.Name != method_definition.Name) return false;
                    if (m.Parameters.Count != method_definition.Parameters.Count) return false;
                    for (int i = 0; i < m.Parameters.Count; ++i)
                    {
                        // Should do a comparison of paramter types.
                        var p1 = m.Parameters[i];
                        var p2 = method_definition.Parameters[i];
                    }
                    return true;
                }).FirstOrDefault();

            if (substituted_method_definition == null)
            {
                // Yes, this is a problem because I cannot find the equivalent method in Campy BCL.
                return null;
            }

            // Convert method definition back into method reference because it may
            // be a generic instance.
            var new_method_reference = substituted_method_definition.MakeMethodReference(substituted_declaring_type);

            if (method_reference as GenericInstanceMethod != null)
            {
                
                
            }

            return new_method_reference;
        }

        public static void RewriteCilCodeBlock(Mono.Cecil.Cil.MethodBody body)
        {
            List<Instruction> result = new List<Instruction>();
            for (int j = 0; j < body.Instructions.Count; ++j)
            {
                Instruction i = body.Instructions[j];

                var inst_to_insert = i;

                if (i.OpCode.FlowControl == FlowControl.Call)
                {
                    object method = i.Operand;

                    if (method as Mono.Cecil.MethodReference == null)
                        throw new Exception();

                    var method_reference = method as Mono.Cecil.MethodReference;
                    TypeReference mr_dt = method_reference.DeclaringType;

                    var bcl_substitute = SubstituteMethod(method_reference);
                    if (bcl_substitute != null)
                    {
                        CallSite cs = new CallSite(typeof(void).ToMonoTypeReference());
                        body.Instructions.RemoveAt(j);
                        var worker = body.GetILProcessor();
                        Instruction new_inst = worker.Create(i.OpCode, bcl_substitute);
                        new_inst.Offset = i.Offset;
                        body.Instructions.Insert(j, new_inst);
                    }
                }
            }
        }


        [global::System.Runtime.InteropServices.DllImport(@"campy-runtime-wrapper", EntryPoint = "BclGetMetaOfType")]
        public static extern System.IntPtr BclGetMetaOfType(
            [MarshalAs(UnmanagedType.LPStr)] string assemblyName,
            [MarshalAs(UnmanagedType.LPStr)] string nameSpace,
            [MarshalAs(UnmanagedType.LPStr)] string name,
            System.IntPtr nested);

        public static IntPtr GetBclType(TypeReference type)
        {
            // Using the BCL, find the type. Note, Mono has a tendency to list something
            // in a namespace in which the type's metadata says it has no namespace.
            // As far as I can tell, this is for generated methods corresponding to the kernels.
            // Due to the disparity of what the "namespace" means, look up the type from
            // the top-most declaring type, and use that as context for the sub-class search.
            Stack<TypeReference> chain = new Stack<TypeReference>();
            while (type != null)
            {
                chain.Push(type);
                type = type.DeclaringType;
            }
            System.IntPtr result = System.IntPtr.Zero;
            while (chain.Any())
            {
                var tr = chain.Pop();
                tr = RUNTIME.RewriteType(tr);
                var assembly_name = tr.Module.Name;
                var name_space = tr.Namespace;
                var name = tr.Name;
                result = BclGetMetaOfType(assembly_name, name_space, name, result);
            }
            return result;
        }

        public static IntPtr GetBclType(Type type)
        {
            // Using the BCL, find the type. Note, Mono has a tendency to list something
            // in a namespace in which the type's metadata says it has no namespace.
            // As far as I can tell, this is for generated methods corresponding to the kernels.
            // Due to the disparity of what the "namespace" means, look up the type from
            // the top-most declaring type, and use that as context for the sub-class search.
            Stack<Type> chain = new Stack<Type>();
            while (type != null)
            {
                chain.Push(type);
                type = type.DeclaringType;
            }
            System.IntPtr result = System.IntPtr.Zero;
            while (chain.Any())
            {
                var tr = chain.Pop();
                var mt = RUNTIME.RewriteType(tr.ToMonoTypeReference());
                var assembly_name = tr.Module.Name;
                var name_space = tr.Namespace;
                var name = tr.Name;
                result = BclGetMetaOfType(assembly_name, name_space, name, result);
            }

            return result;
        }
    }
}



