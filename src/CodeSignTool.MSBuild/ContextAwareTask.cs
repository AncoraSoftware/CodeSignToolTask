﻿namespace Ancora.MSBuild
{
	using System;
	using System.IO;
	using System.Linq;
	using System.Reflection;
#if NETCOREAPP
#endif
	using Microsoft.Build.Framework;
	using Microsoft.Build.Utilities;

	public abstract class ContextAwareTask : Task
    {
        protected virtual string ManagedDllDirectory => Path.GetDirectoryName(new Uri(this.GetType().GetTypeInfo().Assembly.CodeBase).LocalPath);

        protected virtual string ContentDirectory => Path.GetFullPath(Path.Combine(ManagedDllDirectory, "..", "..", "content"));

        protected virtual string UnmanagedDllDirectory => null;

        public override bool Execute()
        {
#if NETCOREAPP
            string taskAssemblyPath = new Uri(this.GetType().GetTypeInfo().Assembly.CodeBase).LocalPath;

            Assembly inContextAssembly = LoadContext.Instance.LoadFromAssemblyPath(taskAssemblyPath);
            Type innerTaskType = inContextAssembly.GetType(this.GetType().FullName);
            object innerTask = Activator.CreateInstance(innerTaskType);

            var outerProperties = this.GetType().GetRuntimeProperties().ToDictionary(i => i.Name);
            var innerProperties = innerTaskType.GetRuntimeProperties().ToDictionary(i => i.Name);
            var propertiesDiscovery = from outerProperty in outerProperties.Values
                                      where outerProperty.SetMethod is not null && outerProperty.GetMethod is not null
                                      let innerProperty = innerProperties[outerProperty.Name]
                                      select new { outerProperty, innerProperty };
            var propertiesMap = propertiesDiscovery.ToArray();
            var outputPropertiesMap = propertiesMap.Where(pair => pair.outerProperty.GetCustomAttribute<OutputAttribute>() is not null).ToArray();

            foreach (var propertyPair in propertiesMap)
            {
                object outerPropertyValue = propertyPair.outerProperty.GetValue(this);
                propertyPair.innerProperty.SetValue(innerTask, outerPropertyValue);
            }

            var executeInnerMethod = innerTaskType.GetMethod(nameof(ExecuteInner), BindingFlags.Instance | BindingFlags.NonPublic);
            bool result = (bool)executeInnerMethod.Invoke(innerTask, new object[0]);

            foreach (var propertyPair in outputPropertiesMap)
            {
                propertyPair.outerProperty.SetValue(this, propertyPair.innerProperty.GetValue(innerTask));
            }

            return result;
#else
            // On .NET Framework (on Windows), we find native binaries by adding them to our PATH.
            if (this.UnmanagedDllDirectory is not null)
            {
                string pathEnvVar = Environment.GetEnvironmentVariable("PATH");
                string[] searchPaths = pathEnvVar.Split(Path.PathSeparator);
                if (!searchPaths.Contains(this.UnmanagedDllDirectory, StringComparer.OrdinalIgnoreCase))
                {
                    pathEnvVar += Path.PathSeparator + this.UnmanagedDllDirectory;
                    Environment.SetEnvironmentVariable("PATH", pathEnvVar);
                }
            }

            return this.ExecuteInner();
#endif
        }

        protected abstract bool ExecuteInner();
    }
}