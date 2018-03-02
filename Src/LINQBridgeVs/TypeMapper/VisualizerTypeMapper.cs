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
using System.IO;
using System.Linq;
using BridgeVs.Logging;

namespace BridgeVs.TypeMapper
{
    /// <summary>
    /// Maps all the types of a given assembly to the type T of the debugger visualizer.
    /// It can map all of the Basic dotNet Framework types like: System.Linq.*, System.*, System.Collection.Generic.*
    /// </summary>
    public class VisualizerTypeMapper
    {
        private const string DotNetFrameworkVisualizerName = "DotNetDynamicVisualizerType.V{0}.dll";

        private readonly VisualizerAttributeInjector _visualizerAttributeInjector;
        public string SourceVisualizerAssemblyLocation { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="VisualizerTypeMapper"/> class.
        /// </summary>
        /// <param name="sourceVisualizerAssemblyLocation"></param>
        public VisualizerTypeMapper(string sourceVisualizerAssemblyLocation)
        {
            Log.Configure("LINQBridgeVs", "Type Mapper");
            _visualizerAttributeInjector = new VisualizerAttributeInjector(sourceVisualizerAssemblyLocation);
            SourceVisualizerAssemblyLocation = sourceVisualizerAssemblyLocation;
        }


        /// <summary>
        /// Maps the dot net framework types. If the file already exists for a given vs version it won't be
        /// regenerated.
        /// </summary>
        /// <param name="targetVisualizerInstallationPath">The target visualizer installation path.</param>
        /// <param name="vsVersion">The vs version.</param>
        /// <param name="sourceVisualizerAssemblyLocation">The source visualizer assembly location.</param>
        /// <returns></returns>
        public static void MapDotNetFrameworkTypes(string visualizerInstallationPath,
            string vsVersion, string sourceVisualizerAssemblyLocation)
        {
            if (visualizerInstallationPath == null)
                throw new ArgumentException(@"Installation Path/s cannot be null", nameof(visualizerInstallationPath));

            if (string.IsNullOrEmpty(vsVersion))
                throw new ArgumentException(@"Visual Studio Version cannot be null", nameof(vsVersion));

            if (string.IsNullOrEmpty(sourceVisualizerAssemblyLocation))
                throw new ArgumentException(@"Visualizer Assembly Location cannot be null",
                    "sourceVisualizerAssemblyLocation");

            var visualizerFileName = string.Format(DotNetFrameworkVisualizerName, vsVersion);
            var dotNetAssemblyVisualizerFilePath = Path.Combine(visualizerInstallationPath, visualizerFileName);

            if (File.Exists(dotNetAssemblyVisualizerFilePath))
            {
                //file already exists don't create it again
                return;
            }

            var visualizerInjector = new VisualizerAttributeInjector(sourceVisualizerAssemblyLocation);

            //Map all the possible System  types
            var systemLinqTypes = typeof(IOrderedEnumerable<>).Assembly
                .GetTypes()
                .Where(type => type != null
                               && (
                                   (type.IsClass && type.IsSerializable)
                                   ||
                                   type.IsInterface
                                   ||
                                   type.Name.Contains("Iterator")
                                  )
                               && !(type.Name.Contains("Func") || type.Name.Contains("Action"))
                               && !string.IsNullOrEmpty(type.Namespace));

            //Map all the possible list types
            var systemGenericsTypes = typeof(IList<>).Assembly
                .GetTypes()
                .Where(type => type != null
                               && (
                                   (type.IsClass && type.IsSerializable)
                                   ||
                                   type.IsInterface
                                  )
                               && !string.IsNullOrEmpty(type.Namespace)
                               && type.IsPublic)
                .Where(type =>
                    !type.Name.Contains("ValueType")
                    && !type.Name.Contains("IFormattable")
                    && !type.Name.Contains("IComparable")
                    && !type.Name.Contains("IConvertible")
                    && !type.Name.Contains("IEquatable")
                    && !type.Name.Contains("Object")
                    && !type.Name.Contains("ICloneable")
                    && !type.Name.Contains("String")
                    && !type.Name.Contains("IDisposable"));

            systemLinqTypes.ForEach(visualizerInjector.MapType);
            systemGenericsTypes.ForEach(visualizerInjector.MapType);

            visualizerInjector.SaveDebuggerVisualizer(dotNetAssemblyVisualizerFilePath);
        }

        /// <summary>
        /// Maps the assembly.
        /// </summary>
        /// <param name="targetAssemblyToMap">The target assembly to map.</param>
        public void MapAssembly(string targetAssemblyToMap)
        {
            _visualizerAttributeInjector.MapTypesFromAssembly(targetAssemblyToMap);
        }

        /// <summary>
        /// Saves the specified debugger visualizer assembly to a given Path.
        /// </summary>
        public void Save(string mappedAssemblyFilePath)
        { 
            _visualizerAttributeInjector.SaveDebuggerVisualizer(mappedAssemblyFilePath);
        }
    }
}
