using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using SimpleJson;

namespace ProBuilder.BuildSystem
{
	public static class BuildManager
	{
		static int Main(string[] args)
		{
			List<BuildTarget> m_Targets = new List<BuildTarget>();

			bool m_IsDebug = false;

			foreach(string arg in args)
			{
				if(arg.StartsWith("-debug"))
				{
					m_IsDebug = true;
				}
				// No valid argument prefix, treat this input as a build target
				else
				{
					try
					{
						BuildTarget t = SimpleJson.SimpleJson.DeserializeObject<BuildTarget>(File.ReadAllText(arg));
						Console.WriteLine("added target: " + t.Name);
						m_Targets.Add(t);
					}
					catch
					{
						Console.WriteLine("Failed parsing: " + arg);
					}
				}
			}

			foreach(BuildTarget target in m_Targets)
			{
				foreach(AssemblyTarget at in target.Assemblies)
				{
					Compiler.CompileDLL(at, m_IsDebug);
				}
			}

			return 0;
		}
	}
}