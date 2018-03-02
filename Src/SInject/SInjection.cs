﻿#region License
// Copyright (c) 2013 - 2018 Giovanni Campo
//
// Permission is hereby granted, free of charge, to any person
// obtaining a copy of this software and associated documentation
// files (the "Software"), to deal in the Software without
// restriction, including without limitation the rights to use,
// copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the
// Software is furnished to do so, subject to the following
// conditions:
//
// The above copyright notice and this permission notice shall be
// included in all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
// OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
// HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
// WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
// FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
// OTHER DEALINGS IN THE SOFTWARE.
#endregion


using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using BridgeVs.Logging;
using Mono.Cecil;
using Mono.Cecil.Pdb;
using SInject.Reflection;

namespace SInject
{
    /// <summary>
    /// Type of serialization to enable
    /// </summary>
    [Flags]
    public enum SerializationTypes
    {
        /// <summary>
        /// BinarySerialization
        /// </summary>
        BinarySerialization = 0x01,
        /// <summary>
        /// DataContractSerialization
        /// </summary>
        DataContractSerialization = 0x02
    }

    /// <summary>
    /// Type of the Patch for Debug or Release mode.
    /// </summary>
    public enum PatchMode
    {
        /// <summary>
        /// The debug
        /// </summary>
        Debug,
        /// <summary>
        /// The release
        /// </summary>
        Release
    }


    /// <summary>
    /// This class inject the Serializable Attribute to all public class types in a given assembly, and adds
    /// default deserialization constructor for those type which implement ISerializable interface
    /// </summary>
    public class SInjection
    {
        #region [ Private Properties ]
        private readonly string _assemblyLocation;
        private readonly PatchMode _mode;
        private readonly AssemblyDefinition _assemblyDefinition;
        private static readonly Func<string, bool> IsSystemAssembly =
        name => name.Contains("Microsoft") || name.Contains("System") || name.Contains("mscorlib");

        private const string Marker = "SInjected";
        private readonly string _snkCertificatePath;
        //private static readonly List<string> ExcludedAssemblies = new List<string>
        //{
        //    "Moq.dll",
        //    "Newtonsoft.dll",
        //};

        /// <summary>
        /// 
        /// </summary>
        public string SInjectVersion
        {
            get
            {
                var assembly = Assembly.GetExecutingAssembly();
                var fvi = FileVersionInfo.GetVersionInfo(assembly.Location);
                return fvi.FileVersion;
            }
        }

        private bool? _snkFileExists;
        private bool SnkFileExists
        {
            get
            {
                if (_snkFileExists.HasValue) return _snkFileExists.Value;
                _snkFileExists = File.Exists(_snkCertificatePath);
                return _snkFileExists.Value;
            }
        }

        private string PdbName
        {
            get
            {
                return Path.ChangeExtension(_assemblyLocation, "pdb");
            }
        }
        #endregion

        #region [ Constructor ]

        /// <summary>
        /// Initializes a new instance of the <see cref="SInject"/> class.
        /// </summary>
        /// <param name="assemblyLocation">The assembly location.</param>
        /// <param name="snkCertificatePath">The location of snk certificate</param>
        /// <param name="mode">The mode.</param>
        /// <exception cref="System.Exception"></exception>
        public SInjection(string assemblyLocation, string snkCertificatePath = null, PatchMode mode = PatchMode.Release)
        {
            Log.Configure("Bridge", "SInject");
            _assemblyLocation = assemblyLocation;
            _snkCertificatePath = snkCertificatePath;
            _mode = mode;
            Log.Write("Assembly being Injected {0}", assemblyLocation);

            //  if (IsAssemblyInExcludedList(Path.GetFileName(assemblyLocation))) return;

            if (!File.Exists(assemblyLocation))
                throw new Exception(string.Format("Assembly at location {0} doesn't exist", assemblyLocation));

            _assemblyDefinition = AssemblyDefinition.ReadAssembly(assemblyLocation,
                                                                  GetReaderParameters());
        }
        #endregion

        #region [ Public Methods ]

        /// <summary>
        /// Patches the loaded assembly enabling the specified Serialization type.
        /// </summary>
        /// <param name="types">The Serialization Type.</param>
        /// <exception cref="System.Exception"></exception>
        public void Patch(SerializationTypes types)
        {
            //if (IsAssemblyInExcludedList(Path.GetFileName(_assemblyLocation)))
            //{
            //    Log.Write("Assembly excluded");
            //    return;
            //}

            if (CheckIfAlreadySinjected())
            {
                Log.Write("Assembly already Sinjected");

                return;
            }

            var typeToInjects = GetTypesToInject().ToList();

            InjectSerialization(typeToInjects);

            //InjectAssemblyReferences();

            AddSinjectionAttribute();

            WriteAssembly();

            Log.Write("Assembly {0} has been correctly Injected", _assemblyDefinition.FullName);
        }

        //private static bool IsAssemblyInExcludedList(string assemblyName)
        //{
        //    return false;
        //}


        /// <summary>
        /// Gets the serializable types in the current assembly.
        /// </summary>
        /// <returns> returns a name list of serializable types</returns>
        public IEnumerable<string> GetSerializableTypes()
        {
            return
           _assemblyDefinition
               .MainModule
               .Types
               .Where(typeInAssembly => typeInAssembly.IsSerializable)
               .Select(definition => definition.FullName);
        }




        #endregion

        #region [ Private Methods ]

        private static void InjectSerialization(ICollection<TypeDefinition> typeToInjects)
        {
            if (typeToInjects.Count == 0)
                return;

            foreach (var typeInAssembly in typeToInjects)
            {
                try
                {
                    typeInAssembly.AddDefaultConstructor();

                    //if (!types.HasFlag(SerializationTypes.BinarySerialization)) continue;

                    typeInAssembly.IsSerializable = true;

                    //This disable the serialization of non serializable property belonging to Microsoft framework
                    //it acts directly on the backing field of the prop marking it as NonSerialized.
                    SetNonSerializedFields(typeInAssembly);

                    //  if (types.HasFlag(SerializationTypes.DataContractSerialization))
                    //TODO: Inject DataMemberAttribute
                }
                catch (Exception e)
                {
                    Log.Write(e, string.Format("Type {0} wasn't marked as Serializable", typeInAssembly.FullName));
                }
            }
        }

        private IEnumerable<TypeDefinition> GetTypesToInject()
        {
            try
            {
                return _assemblyDefinition
                    .MainModule
                    .Types
                    .Where(
                        typeInAssembly =>
                            !typeInAssembly.IsInterface
                            && (typeInAssembly.IsClass || typeInAssembly.IsAnsiClass)
                            && typeInAssembly.BaseType != null
                    );
            }
            catch (Exception e)
            {
                Log.Write(e, "Error while iterating types to inject");
                throw;
            }
        }

        private void AddSinjectionAttribute()
        {
            try
            {
                var stringType = _assemblyDefinition.MainModule.TypeSystem.String;
                var corlib = (AssemblyNameReference)_assemblyDefinition.MainModule.TypeSystem.Corlib;
                var system = _assemblyDefinition.MainModule.AssemblyResolver.Resolve(new AssemblyNameReference("System", corlib.Version)
                {
                    PublicKeyToken = corlib.PublicKeyToken,
                });
                var generatedCodeAttribute = system.MainModule.GetType("System.CodeDom.Compiler.GeneratedCodeAttribute");
                var generatedCodeCtor = generatedCodeAttribute.Methods.First(m => m.IsConstructor && m.Parameters.Count == 2);

                var result = new CustomAttribute(_assemblyDefinition.MainModule.Import(generatedCodeCtor));
                result.ConstructorArguments.Add(new CustomAttributeArgument(stringType, Marker));
                result.ConstructorArguments.Add(new CustomAttributeArgument(stringType, SInjectVersion));

                _assemblyDefinition.MainModule.Assembly.CustomAttributes.Add(result);
            }
            catch (Exception e)
            {
                Log.Write(e, "Error while adding Sinjection attribute to the assembly");
                throw;
            }
        }

        private bool CheckIfAlreadySinjected()
        {
            return _assemblyDefinition.HasCustomAttributes
                   && _assemblyDefinition.CustomAttributes
                       .Any(attribute => attribute.HasConstructorArguments
                                         &&
                                         attribute.ConstructorArguments
                                             .Count(
                                                 argument =>
                                                     argument.Value.Equals(Marker) ||
                                                     argument.Value.Equals(SInjectVersion)) == 2);

        }

        private ReaderParameters GetReaderParameters()
        {
            var assemblyResolver = new DefaultAssemblyResolver();
            var assemblyLocation = Path.GetDirectoryName(_assemblyLocation);
            assemblyResolver.AddSearchDirectory(assemblyLocation);

            var readerParameters = new ReaderParameters { AssemblyResolver = assemblyResolver };

            if (!File.Exists(PdbName)) return readerParameters;

            var symbolReaderProvider = new PdbReaderProvider();
            readerParameters.SymbolReaderProvider = symbolReaderProvider;
            readerParameters.ReadSymbols = _mode == PatchMode.Debug;
            readerParameters.ReadingMode = ReadingMode.Deferred;

            return readerParameters;
        }

        private WriterParameters GetWriterParameters()
        {
            var writerParameters = new WriterParameters();

            if (_mode == PatchMode.Debug && File.Exists(PdbName))
            {
                writerParameters.SymbolWriterProvider = new PdbWriterProvider();
                writerParameters.WriteSymbols = true;
            }

            if (string.IsNullOrEmpty(_snkCertificatePath) || !SnkFileExists) return writerParameters;

            using (var file = File.OpenRead(_snkCertificatePath))
            {
                writerParameters.StrongNameKeyPair = new StrongNameKeyPair(file);

            }

            return writerParameters;
        }

        private static void SetNonSerializedFields(TypeDefinition typeDefinition)
        {
            var fields = typeDefinition.Fields
               .Where(field =>
               {
                   var fieldType = field.FieldType.Resolve();
                   return fieldType != null && !fieldType.IsSerializable && IsSystemAssembly(fieldType.FullName) && !fieldType.IsPrimitive;
               });

            foreach (var fieldDefinition in fields)
                fieldDefinition.IsNotSerialized = true;
        }


        private void InjectAssemblyReferences()
        {
            var currentPath = Path.GetDirectoryName(_assemblyLocation);

            _assemblyDefinition.MainModule.AssemblyReferences.Where(reference => !IsSystemAssembly(reference.FullName)).ToList().ForEach(
                reference =>
                {
                    if (currentPath == null) return;
                    var fileName = Path.Combine(currentPath, reference.Name + ".dll");
                    if (!File.Exists(fileName)) return;
                    var sinjection = new SInjection(fileName);
                    sinjection.Patch(SerializationTypes.BinarySerialization);
                });

        }

        private void WriteAssembly()
        {
            const int retry = 3;

            if (string.IsNullOrEmpty(_snkCertificatePath) || !SnkFileExists)
            {
                _assemblyDefinition.Name.HasPublicKey = false;
                _assemblyDefinition.Name.PublicKey = new byte[0];
                _assemblyDefinition.MainModule.Attributes &= ~ModuleAttributes.StrongNameSigned;
            }
            for (var i = 0; i < retry; i++)
            {
                try
                {
                    _assemblyDefinition.Write(_assemblyLocation, GetWriterParameters());

                    break;
                }
                catch (Exception e)
                {
                    Log.Write(e, string.Format("Error Saving Assembly {0} - Attempt #{1}", _assemblyLocation, i));

                    Thread.Sleep(125);

                }

            }

        }
        #endregion

    }
}
